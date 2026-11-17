using LLPlayer.ControlApi;

namespace LLPlayer.Avalonia.Services.Automation;

/// <summary>Owns this window's reverse channel and leaves the shared host alive when the window closes.</summary>
internal sealed class ControlConnection : IAsyncDisposable
{
    static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(2);
    static readonly TimeSpan SameSessionRetryLimit = TimeSpan.FromSeconds(30);
    const int MaximumInFlight = 8;

    readonly CancellationTokenSource lifetime = new();
    readonly ControlGuiChannel channel;
    readonly AppPaths paths;
    readonly Action<ControlState?> publish;
    readonly Task<bool>? startupMedia;
    readonly Task connection;

    public ControlConnection(AppPaths paths, ControlGuiChannel channel, Action<ControlState?> publish,
        Task<bool>? startupMedia = null)
    {
        this.paths = paths;
        this.channel = channel;
        this.publish = publish;
        this.startupMedia = startupMedia;
        connection = Task.Run(() => ConnectAsync(lifetime.Token));
    }

    async Task ConnectAsync(CancellationToken token)
    {
        Guid? launchId = Guid.TryParse(Environment.GetEnvironmentVariable(ControlProtocol.GuiLaunchIdVariable), out var assigned)
            ? assigned : null;
        if (launchId.HasValue)
            Environment.SetEnvironmentVariable(ControlProtocol.GuiLaunchIdVariable, null);
        ControlGuiRegistration registration;
        try { registration = await PrepareRegistrationAsync(launchId, startupMedia, token); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
        while (!token.IsCancellationRequested)
        {
            try
            {
                using ControlClient client = await ControlClient.ConnectOrStartAsync(paths.ConfigDir, paths.StateDir, token);
                ControlGuiSession session = await client.RegisterGuiAsync(registration, token);
                registration = registration with { LaunchId = null, LaunchSourceOpened = null };
                await RunSessionAsync(client, session, token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            catch (Exception error) when (CanReconnect(error)) { publish(null); }
            try { await Task.Delay(ReconnectDelay, token); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
        }
    }

    internal static async Task<ControlGuiRegistration> PrepareRegistrationAsync(Guid? launchId, Task<bool>? startupMedia, CancellationToken token)
    {
        bool? opened = launchId.HasValue && startupMedia != null ? await startupMedia.WaitAsync(token) : null;
        return new(ControlProtocol.DefaultProfileId, "LLPlayer Avalonia", Environment.ProcessId, launchId, opened);
    }

    async Task RunSessionAsync(ControlClient client, ControlGuiSession session, CancellationToken token)
    {
        using CancellationTokenSource attached = CancellationTokenSource.CreateLinkedTokenSource(token);
        Dictionary<Guid, CommandExecution> executing = [];
        ControlGuiAcknowledgements acknowledgement = new();
        long lastPoll = System.Diagnostics.Stopwatch.GetTimestamp();
        Task status = PublishStateAsync(client, attached.Token);
        try
        {
            while (!attached.IsCancellationRequested)
            {
                foreach (Guid finished in executing.Where(pair => pair.Value.Completion.IsCompleted).Select(pair => pair.Key).ToArray())
                {
                    CommandExecution command = executing[finished];
                    executing.Remove(finished);
                    using (command.Cancellation) await command.Completion;
                }

                ControlGuiCommand? commandToRun;
                try
                {
                    commandToRun = await client.PollGuiAsync(session.SessionId,
                        new(session.OwnerToken, acknowledgement.PendingDeliveryId), attached.Token);
                    lastPoll = System.Diagnostics.Stopwatch.GetTimestamp();
                }
                catch (Exception error) when (!attached.IsCancellationRequested && CanRetry(error)
                    && System.Diagnostics.Stopwatch.GetElapsedTime(lastPoll) < SameSessionRetryLimit)
                {
                    await Task.Delay(ReconnectDelay, attached.Token);
                    continue;
                }
                if (!acknowledgement.Observe(commandToRun) || commandToRun == null) continue;

                if (commandToRun.Cancel)
                {
                    if (executing.TryGetValue(commandToRun.CommandId, out CommandExecution? command))
                        await command.Cancellation.CancelAsync();
                    continue;
                }
                if (executing.ContainsKey(commandToRun.CommandId))
                    continue;

                if (executing.Count >= MaximumInFlight)
                {
                    await CompleteAsync(client, session, commandToRun.CommandId,
                        ControlResult.Fail("gui_busy", "This window has too many commands in progress.", retryable: true), attached.Token);
                    continue;
                }

                CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(attached.Token);
                Task completion = ExecuteAsync(client, session, commandToRun, cancellation.Token, attached.Token);
                executing.Add(commandToRun.CommandId, new(cancellation, completion));
            }
        }
        finally
        {
            await attached.CancelAsync();
            try { await Task.WhenAll(executing.Values.Select(command => command.Completion).Append(status)); }
            catch (Exception error) when (CanReconnect(error)) { }
            foreach (CommandExecution command in executing.Values) command.Cancellation.Dispose();
            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(2));
            try { await client.DetachGuiAsync(session.SessionId, session.OwnerToken, timeout.Token); }
            catch (Exception error) when (CanReconnect(error)) { }
        }
    }

    async Task ExecuteAsync(ControlClient client, ControlGuiSession session, ControlGuiCommand command,
        CancellationToken commandToken, CancellationToken sessionToken)
    {
        ControlResult result;
        try { result = await channel.DispatchAsync(session.SessionId, command.Request, commandToken); }
        catch (OperationCanceledException)
        {
            result = ControlResult.Fail("cancelled", "The GUI command was cancelled.");
        }
        catch (Exception)
        {
            result = ControlResult.Fail("gui_operation_failed", "The attached player could not complete the command.");
        }
        if (await CompleteAsync(client, session, command.CommandId, result, sessionToken))
            channel.AcknowledgeResult(command.Request, result);
    }

    static async Task<bool> CompleteAsync(ControlClient client, ControlGuiSession session, Guid commandId,
        ControlResult result, CancellationToken token)
    {
        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        while (!token.IsCancellationRequested)
        {
            try
            {
                await client.CompleteGuiAsync(session.SessionId, new(session.OwnerToken, commandId, result), token);
                return true;
            }
            catch (Exception error) when (!token.IsCancellationRequested && CanRetry(error)
                && System.Diagnostics.Stopwatch.GetElapsedTime(start) < SameSessionRetryLimit)
            {
                await Task.Delay(ReconnectDelay, token);
            }
        }
        return false;
    }

    async Task PublishStateAsync(ControlClient client, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try { publish(await client.GetStateAsync(new(ControlProtocol.DefaultProfileId), token)); }
            catch (Exception error) when (!token.IsCancellationRequested && CanReconnect(error)) { publish(null); }
            await Task.Delay(ReconnectDelay, token);
        }
    }

    static bool CanRetry(Exception error)
        => error is HttpRequestException { StatusCode: null or System.Net.HttpStatusCode.RequestTimeout
                    or System.Net.HttpStatusCode.BadGateway or System.Net.HttpStatusCode.ServiceUnavailable or System.Net.HttpStatusCode.GatewayTimeout }
            or IOException or OperationCanceledException or ControlException { Error.Retryable: true };

    static bool CanReconnect(Exception error) => CanRetry(error) || error is HttpRequestException or ControlException
        or InvalidOperationException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or System.Text.Json.JsonException;

    public async ValueTask DisposeAsync()
    {
        await lifetime.CancelAsync();
        try { await connection; }
        finally { lifetime.Dispose(); }
    }

    sealed record CommandExecution(CancellationTokenSource Cancellation, Task Completion);
}
