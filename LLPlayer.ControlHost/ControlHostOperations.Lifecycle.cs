using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using LLPlayer.ControlApi;
using LLPlayer.Core.Automation;

namespace LLPlayer.ControlHost;

internal sealed partial class ControlHostOperations
{
    private readonly ConcurrentDictionary<Guid, byte> lifecycleInProgress = new();

    private void RegisterApplicationLifecycle()
    {
        foreach (var schema in ControlGuiLifecycleOperations.All)
        {
            var operation = schema.Operation;
            if (!ControlGuiLifecycleOperations.IsApplicationLifecycle(operation.Id))
                continue;
            handlers.Add(operation.Id, RunApplicationLifecycleAsync);
            catalog.Add(operation, schema.Arguments, schema.Result,
                (request, args, ct) => request.Scope.SessionId is null
                    ? Task.FromResult(ControlResult.Fail("session_required", "Select the GUI session that owns the window."))
                    : jobs.EnqueueAsync(Normalize(request, args), ct),
                scope => scope.SessionId is null ? "session_required" : broker.GetUnavailableReason(scope),
                requireAvailabilityAtInvocation: false);
        }
    }

    private async Task<ControlJobOutcome> RunApplicationLifecycleAsync(ControlJobContext context, CancellationToken token)
    {
        var request = context.Request;
        if (request.Scope.SessionId is not { } sessionId)
            throw new ControlJobException("session_required", "Select the GUI session that owns the window.");
        if (!lifecycleInProgress.TryAdd(sessionId, 0))
            throw new ControlJobException("gui_busy", "A lifecycle operation is already running for this window.", retryable: true);

        try
        {
            if (broker.GetUnavailableReason(request.Scope) is { } reason)
                throw new ControlJobException(reason, "The requested GUI session is no longer attached.");

            bool exit = request.OperationId == "app.exit";
            bool restart = request.OperationId == "app.restart";
            using var original = exit || restart ? ObserveRegisteredProcess(sessionId) : null;
            context.RequireOriginalHost();
            ControlApplicationWindow? replacement = null;
            long? sourceRevision = null;
            if (!exit)
            {
                var launchId = Guid.NewGuid();
                var launch = await broker.ExecuteAsync(request with
                {
                    Arguments = ControlProtocol.Encode(new ControlApplicationLaunchTicket(launchId))
                }, token);
                if (launch.Error != null)
                    return FromResult(launch);

                var started = launch.Value?.Deserialize<ControlApplicationStarted>(ControlProtocol.Json);
                if (started is not { ProcessId: > 0 })
                    throw new ControlJobException("gui_launch_unverified", "The window returned no verifiable process identity.");
                sourceRevision = started.SourceRevision;
                using var child = ObserveProcess(started.ProcessId, "gui_exited_before_attach");
                context.Report("waiting_for_gui_registration");
                try
                {
                    replacement = await ObserveWindowAttachmentAsync(child, launchId, token);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    return new(ControlJobStatus.Interrupted, Error: new("gui_launch_interrupted",
                        "The replacement started. Waiting for its registration was cancelled."));
                }
                if (replacement == null)
                    return new(ControlJobStatus.Interrupted, Error: new("gui_attach_timeout",
                        "The new process started but its GUI registration could not be confirmed within 30 seconds."));
                if (!restart)
                    return Done(new ControlApplicationLifecycleResult(sessionId, false, replacement));
            }

            var exitRequest = restart
                ? request with { OperationId = "app.exit", ExpectedRevision = sourceRevision, Arguments = ControlProtocol.Encode(new ControlGuiOperations.Empty()) }
                : request;
            var reply = await broker.ExecuteAsync(exitRequest, token);
            var pending = new ControlApplicationLifecycleResult(sessionId, false, replacement);
            if (reply.Error != null)
                return FromResult(reply) with { Value = ControlProtocol.Encode(pending) };

            context.Report("waiting_for_gui_exit");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(WindowObservationTimeout);
            try
            {
                await original!.WaitForExitAsync(timeout.Token);
                return Done(pending with { SourceExited = true });
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                return new(ControlJobStatus.Interrupted, ControlProtocol.Encode(pending),
                    new("gui_exit_unverified", "The window acknowledged quit but its process exit could not be confirmed."));
            }
        }
        finally
        {
            lifecycleInProgress.TryRemove(sessionId, out _);
        }
    }

    private Process ObserveRegisteredProcess(Guid sessionId)
    {
        int processId = broker.RegisteredProcessId(sessionId)
            ?? throw new ControlJobException("gui_exit_unverified", "This session did not register an observable GUI process.");
        return ObserveProcess(processId, "session_unavailable");
    }

    private static Process ObserveProcess(int processId, string unavailableCode)
    {
        try { return Process.GetProcessById(processId); }
        catch (ArgumentException)
        {
            throw new ControlJobException(unavailableCode, "The registered GUI process is no longer running.");
        }
    }
}
