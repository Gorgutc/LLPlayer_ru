using System.Diagnostics;
using System.Text.Json;
using Avalonia.Headless.XUnit;
using FlyleafLib.MediaPlayer;
using LLPlayer.Avalonia.Services;
using LLPlayer.Avalonia.Services.Automation;
using LLPlayer.Avalonia.ViewModels;
using LLPlayer.Avalonia.Views;
using LLPlayer.ControlApi;
using LLPlayer.Core.Services;

namespace LLPlayer.Avalonia.Tests.Automation;

[Collection(FlyleafGlobalsCollection.Name)]
public sealed class ControlGuiAttachTests
{
    [AvaloniaFact]
    public async Task Headless_avalonia_attaches_and_reconnects_to_the_actual_host_with_the_same_job()
    {
        bool verifyNative = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("LLPLAYER_FFMPEG_DIR"));
        Assert.SkipWhen(!verifyNative, "Requires configured native FFmpeg and the companion build.");
        string host = FindControlHost();
        Assert.True(File.Exists(host), "The Avalonia verification gate must build LLPlayer.ControlHost before this suite.");
        string discoveryPath = Path.Combine(ControlPaths.UserConnectionDirectory, "host.json");

        TempDir state = new();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        CancellationToken token = deadline.Token;
        AppPaths app = new(Path.Combine(state.Path, "config"), Path.Combine(state.Path, "state"));
        Process? process = null;
        ControlClient? client = null;
        var scope = new ControlScope(ControlProtocol.DefaultProfileId);
        try
        {
            string watchFolder = Directory.CreateDirectory(Path.Combine(state.Path, "watch-folder")).FullName;

            process = StartFixtureHost(host, app);
            ControlDiscovery discovery = await ReadOwnDiscoveryAsync(discoveryPath, process, app, token);
            client = new ControlClient(discovery);
            string windowBlock = OperatingSystem.IsLinux() ? "requires_display" : "profile_conflict";
            var windowOperation = Assert.Single((await client.DescribeAsync(scope, token)).Operations,
                capability => capability.Operation.Id == "ui.window.open");
            Assert.False(windowOperation.Available);
            Assert.Equal(windowBlock, windowOperation.Reason);
            var openWindow = await InvokeAsync(client, scope, "ui.window.open", new ControlGuiOperations.Empty(), token);
            Guid windowJobId = Assert.IsType<Guid>(openWindow.JobId);
            ControlJob? windowJob;
            while ((windowJob = await client.GetAsync(scope, windowJobId, token))?.Status is null or ControlJobStatus.Accepted or ControlJobStatus.Running)
                await Task.Delay(100, token);
            Assert.Equal(ControlJobStatus.Failed, windowJob.Status);
            Assert.Equal(windowBlock, windowJob.Error?.Code);
            ControlResult startWatch = await InvokeAsync(client, scope, "watch.start", new { Folder = watchFolder, Recursive = false }, token);
            Assert.Null(startWatch.Error);
            Guid jobId = Assert.IsType<Guid>(startWatch.JobId);
            ControlJob? job;
            while ((job = await client.GetAsync(scope, jobId, token))?.Status is null or ControlJobStatus.Accepted
                   or ControlJobStatus.WaitingForResource or ControlJobStatus.Running)
                await Task.Delay(100, token);
            Assert.Equal(ControlJobStatus.Completed, job.Status);

            using Player native = PlaybackControlOpenTests.CreatePlayer(state, out AvaloniaUIDispatcher dispatcher, out CollectionSyncBridge bridge);
            await using var controller = new FlyleafPlaybackController(native, dispatcher, bridge);
            var window = new MainWindow(new KeyMapper(), new AvaloniaHostServices(() => null));
            await using var translation = new FlyleafWordTranslator(native, new TranslationSettings());
            using var vm = new MainWindowViewModel(controller, new AppPrefs(), _ => { }, translation, window);
            var executor = new ControlGuiExecutor(controller, vm, window);
            ControlGuiChannel gui = new(executor.ExecuteAsync);
            window.Attach(vm, null);
            window.Show();
            try
            {
                Guid firstSession = await AttachAsync(app, gui, client, scope, discovery.InstanceId, jobId, previousSession: null, token);
                Assert.True(Assert.Single(window.OwnedWindows.OfType<AboutWindow>()).IsVisible);
                Guid secondSession = await AttachAsync(app, gui, client, scope, discovery.InstanceId, jobId, firstSession, token);
                Assert.NotEqual(firstSession, secondSession);
                Assert.True(Assert.Single(window.OwnedWindows.OfType<AboutWindow>()).IsVisible);
            }
            finally
            {
                foreach (var child in window.OwnedWindows.ToArray()) child.Close();
                window.Close();
            }
            ControlState after = await client.GetStateAsync(scope, token);
            Assert.Empty(after.Sessions);
            Assert.Contains(after.Jobs, item => item.JobId == jobId && item.Status == ControlJobStatus.Completed);
            ControlResult watch = await InvokeAsync(client, scope, "watch.get", new ControlGuiOperations.Empty(), token);
            Assert.True(watch.Value!.Value.GetProperty("active").GetBoolean());
        }
        finally
        {
            bool cleanupPending = false;
            try
            {
                using (client)
                using (process)
                    if (process != null)
                        await StopFixtureHostAsync(process, client, app.StateDir);
            }
            catch (ControlException error) when (error.Error.Code == "cleanup_pending")
            {
                cleanupPending = true;
                throw new InvalidOperationException($"ControlHost needs confirmed cleanup before releasing fixture state at {app.StateDir}.", error);
            }
            finally
            {
                string bindingPath = Path.Combine(ControlPaths.UserConnectionDirectory, "control.lock");
                if (!cleanupPending && CanDeleteFixtureState(bindingPath, app.StateDir)) state.Dispose();
                else TestContext.Current.TestOutputHelper?.WriteLine($"Preserved fixture state for ControlHost recovery at {app.StateDir}");
            }
        }
    }

    [Fact]
    public void Fixture_cleanup_preserves_retained_and_unverifiable_bindings()
    {
        using TempDir fixture = new();
        string binding = Path.Combine(fixture.Path, "control.lock");
        string state = Path.Combine(fixture.Path, "state");
        Assert.True(CanDeleteFixtureState(binding, state));
        File.WriteAllText(binding, JsonSerializer.Serialize(state, ControlProtocol.Json));
        Assert.False(CanDeleteFixtureState(binding, state));
        File.WriteAllText(binding, "{malformed");
        Assert.False(CanDeleteFixtureState(binding, state));
        File.WriteAllText(binding, JsonSerializer.Serialize(Path.Combine(fixture.Path, "different"), ControlProtocol.Json));
        Assert.True(CanDeleteFixtureState(binding, state));
        File.WriteAllText(binding, "");
        Assert.True(CanDeleteFixtureState(binding, state));
    }

    static Process StartFixtureHost(string host, AppPaths app)
    {
        var startInfo = new ProcessStartInfo(host)
        {
            WorkingDirectory = Path.GetDirectoryName(host)!,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.Environment.Remove("DISPLAY");
        startInfo.Environment.Remove("WAYLAND_DISPLAY");
        foreach (string arg in new[] { "--serve", "--config-dir", app.ConfigDir, "--state-dir", app.StateDir })
            startInfo.ArgumentList.Add(arg);
        return Process.Start(startInfo) ?? throw new InvalidOperationException("The test host could not start.");
    }

    static async Task StopFixtureHostAsync(Process process, ControlClient? client, string stateDirectory)
    {
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            if (client != null && !process.HasExited)
                await client.StopHostAsync(cleanup.Token);
            await process.WaitForExitAsync(cleanup.Token);
        }
        catch (Exception error) when (error is OperationCanceledException or HttpRequestException)
        {
            // Only the original process started by this test can be killed here. Its binding may survive.
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            using var exit = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await process.WaitForExitAsync(exit.Token);
            throw new InvalidOperationException($"ControlHost could not be stopped cleanly. Check the fixture state at {stateDirectory}.", error);
        }
    }

    static bool CanDeleteFixtureState(string bindingPath, string stateDirectory)
    {
        try
        {
            using var binding = new FileStream(bindingPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (binding.Length == 0) return true;
            return binding.Length <= 32_768 && JsonSerializer.Deserialize<string>(binding, ControlProtocol.Json) is { } retained
                && Path.IsPathFullyQualified(retained) && !ControlPaths.SameDirectory(retained, stateDirectory);
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException) { return true; }
        catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    static async Task<Guid> AttachAsync(AppPaths paths, ControlGuiChannel channel, ControlClient client, ControlScope scope,
        Guid instanceId, Guid existingJob, Guid? previousSession, CancellationToken token)
    {
        TaskCompletionSource<ControlState> attached = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var connection = new ControlConnection(paths, channel, state =>
        {
            if (state is { } live && live.InstanceId == instanceId && live.ProfileId == scope.ProfileId
                && live.Sessions.Any(session => session.SessionId != previousSession))
                attached.TrySetResult(state);
        });
        ControlState current = await attached.Task.WaitAsync(token);
        Assert.Contains(current.Jobs, job => job.JobId == existingJob);
        Guid sessionId = Assert.Single(current.Sessions).SessionId;
        ControlResult result = await InvokeAsync(client, scope with { SessionId = sessionId }, "ui.get",
            new ControlGuiOperations.Empty(), token);
        Assert.Null(result.Error);
        Assert.False(result.Value!.Value.GetProperty("fullscreen").GetBoolean());

        ControlScope selected = scope with { SessionId = sessionId };
        var capability = Assert.Single((await client.DescribeAsync(selected, token)).Operations,
            entry => entry.Operation.Id == "ui.surface.open");
        Assert.True(capability.Available);
        Assert.True(capability.ResultSchema.GetProperty("properties").TryGetProperty("visible", out _));
        var missingSession = await InvokeAsync(client, scope, "ui.surface.open", new { surface = "about" }, token);
        Assert.Equal("session_required", missingSession.Error?.Code);
        var admission = await InvokeAsync(client, selected, "ui.surface.open", new { surface = "about" }, token);
        Assert.Null(admission.Error);
        Guid surfaceJobId = Assert.IsType<Guid>(admission.JobId);
        ControlJob? job;
        while ((job = await client.GetAsync(selected, surfaceJobId, token))?.Status is null or ControlJobStatus.Accepted or ControlJobStatus.Running)
            await Task.Delay(50, token);
        Assert.Equal(ControlJobStatus.Completed, job.Status);
        Assert.True(job.Value!.Value.GetProperty("visible").GetBoolean());
        Assert.Equal(sessionId, job.Scope.SessionId);
        return sessionId;
    }

    static Task<ControlResult> InvokeAsync(ControlClient client, ControlScope scope, string operation, object args,
        CancellationToken token)
        => client.InvokeAsync(new(Guid.NewGuid(), operation, scope, null, ControlProtocol.Encode(args)), token);

    static async Task<ControlDiscovery> ReadOwnDiscoveryAsync(string path, Process process, AppPaths paths, CancellationToken token)
    {
        while (true)
        {
            Assert.False(process.HasExited, "The fixture ControlHost exited before announcing its endpoint. The test must have exclusive profile ownership.");
            try
            {
                await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
                    bufferSize: 4096, useAsync: true);
                var discovery = await JsonSerializer.DeserializeAsync<ControlDiscovery>(stream, ControlProtocol.Json, token);
                if (discovery?.ProcessId == process.Id)
                {
                    Assert.Equal(ControlProtocol.DefaultProfileId, discovery.ProfileId);
                    Assert.True(ControlPaths.SameDirectory(paths.ConfigDir, discovery.ConfigDirectory));
                    return discovery;
                }
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
            await Task.Delay(100, token);
        }
    }

    static string FindControlHost()
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "LLPlayer.slnx"))) root = root.Parent;
        Assert.NotNull(root);
        string executable = OperatingSystem.IsWindows() ? "LLPlayer.ControlHost.exe" : "LLPlayer.ControlHost";
        return Path.Combine(root.FullName, "LLPlayer.ControlHost", "bin", "Debug", "net10.0", executable);
    }
}
