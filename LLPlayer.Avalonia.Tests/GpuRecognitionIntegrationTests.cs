using System.Globalization;
using Avalonia.Headless.XUnit;
using FlyleafLib;
using FlyleafLib.MediaPlayer;
using LLPlayer.Avalonia.Services;
using LLPlayer.Avalonia.Views;

namespace LLPlayer.Avalonia.Tests;

[Collection(FlyleafGlobalsCollection.Name)]
public sealed class GpuRecognitionIntegrationTests
{
    [AvaloniaTheory]
    [InlineData("--device cpu", false)]
    [InlineData("--device=auto", true)]
    public async Task Recognition_admits_the_configured_device_through_the_player(string arguments, bool requiresGpu)
    {
        using var timeout = TestTimeout();
        await using var session = new Session(arguments);
        var failure = new TaskCompletionSource<UnknownErrorOccurredEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        session.Player.UnknownErrorOccurred += (_, error) =>
        {
            if (error.ErrorType == UnknownErrorType.ASR) failure.TrySetResult(error);
        };
        var owner = await session.Coordinator.AcquireAsync("test batch", timeout.Token);
        try
        {
            Task start = session.Controller.StartRecognitionAsync(0);
            if (requiresGpu)
            {
                await UntilAsync(() => session.Controller.Recognition.IsWaitingForGpu || start.IsCompleted, timeout.Token);
                Assert.True(session.Controller.Recognition.IsWaitingForGpu);
                await session.Controller.Recognition.MutateAsync(() => { }).WaitAsync(timeout.Token);
                Assert.False(start.IsCompleted);
                Assert.False(session.Player.Subtitles[0].EnabledASR);
                Assert.False(failure.Task.IsCompleted);
                owner.Complete();
            }

            await start.WaitAsync(timeout.Token);
            var error = await failure.Task.WaitAsync(timeout.Token);
            Assert.StartsWith("Cannot execute ASR:", error.Message);
        }
        finally { owner.Complete(); }
    }

    [AvaloniaTheory]
    [InlineData("cancel slot")]
    [InlineData("reopen media")]
    [InlineData("stop player")]
    [InlineData("shutdown")]
    public async Task Waiting_ASR_is_canceled_before_its_engine_can_start(string action)
    {
        using var timeout = TestTimeout();
        await using var session = new Session("--device cuda");
        var owner = await session.Coordinator.AcquireAsync("test batch", timeout.Token);
        try
        {
            Task start = session.Controller.StartRecognitionAsync(0);
            await UntilAsync(() => session.Controller.Recognition.IsWaitingForGpu || start.IsCompleted, timeout.Token);
            Assert.True(session.Controller.Recognition.IsWaitingForGpu);
            await session.Controller.Recognition.MutateAsync(() => { }).WaitAsync(timeout.Token);
            Assert.False(start.IsCompleted);
            switch (action)
            {
                case "cancel slot":
                    await session.Controller.CancelRecognitionAsync(0).WaitAsync(timeout.Token);
                    break;
                case "reopen media":
                    // URL equality must not make the old request current again.
                    await session.ReopenAsync(timeout.Token);
                    break;
                case "stop player":
                    // The native Stop command replaces media identity without an Opening event.
                    session.Player.Stop();
                    break;
                case "shutdown":
                    session.Controller.BeginRecognitionShutdown();
                    await session.Controller.DisposeAsync().AsTask().WaitAsync(timeout.Token);
                    break;
            }

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => start.WaitAsync(timeout.Token));
            Assert.True(start.IsCanceled);
            owner.Complete();
            var next = await session.Coordinator.AcquireAsync("next job", timeout.Token);
            next.Complete();
            Assert.False(session.Player.Subtitles[0].EnabledASR);
            Assert.False(session.Player.IsASRRunning);
        }
        finally { owner.Complete(); }
    }

    [AvaloniaFact]
    public async Task Shared_ASR_slots_hold_one_gpu_admission_until_the_last_slot_stops()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "The interactive process fixture here uses a Linux shell.");
        using var timeout = TestTimeout();
        await using var session = new Session("--device cuda", """
            #!/bin/sh
            folder=${0%/*}
            printf '%s\n' "$$" > "$folder/pid.pending"
            mv "$folder/pid.pending" "$folder/pid"
            printf '[00:00.500 --> 00:01.500] Controlled ASR cue.\n'
            while [ ! -f "$folder/finish" ]; do sleep 0.05; done
            printf 'Operation finished in: 1s\n'
            """);
        try
        {
            await session.Controller.StartRecognitionAsync(0).WaitAsync(timeout.Token);
            await UntilAsync(() => File.Exists(session.PidPath), timeout.Token);
            int enginePid = int.Parse(await File.ReadAllTextAsync(session.PidPath, timeout.Token), CultureInfo.InvariantCulture);

            Task<GpuWorkLease> waiting = session.Coordinator.AcquireAsync("test batch", timeout.Token).AsTask();
            Assert.False(waiting.IsCompleted);

            await session.Controller.StartRecognitionAsync(1).WaitAsync(timeout.Token);
            Assert.True(session.Player.Subtitles[1].EnabledASR);
            await session.Controller.CancelRecognitionAsync(0).WaitAsync(timeout.Token);
            Assert.True(IsExecuting(enginePid));
            Assert.False(waiting.IsCompleted);

            await session.Controller.CancelRecognitionAsync(1).WaitAsync(timeout.Token);
            var next = await waiting.WaitAsync(timeout.Token);
            next.Complete();
            Assert.False(IsExecuting(enginePid), "The ASR process must exit before the next job can start.");
        }
        finally { File.WriteAllText(session.FinishPath, ""); }
    }

    [AvaloniaFact]
    public async Task Quit_retries_a_failed_GPU_drain_before_shutting_down_the_host()
    {
        using var timeout = TestTimeout();
        int attempts = 0;
        int recoveries = 0;
        int shutdowns = 0;
        var window = new MainWindow
        {
            DrainGpuWorkBeforeShutdown = () => ++attempts == 1
                ? Task.FromException(new IOException("The test engine is still stopping."))
                : Task.CompletedTask,
            GpuShutdownFailed = () => recoveries++,
            ShutdownRequested = () => { shutdowns++; return Task.CompletedTask; }
        };
        window.Show();
        try
        {
            await window.RequestApplicationExitAsync().WaitAsync(timeout.Token);
            Assert.Equal(1, attempts);
            Assert.Equal(1, recoveries);
            Assert.Equal(0, shutdowns);
            Assert.True(window.IsVisible);
            Assert.True(window.IsEnabled);

            await window.RequestApplicationExitAsync().WaitAsync(timeout.Token);
            Assert.Equal(2, attempts);
            Assert.Equal(1, recoveries);
            Assert.Equal(1, shutdowns);
            Assert.False(window.IsVisible);
        }
        finally
        {
            if (window.IsVisible)
            {
                window.DrainGpuWorkBeforeShutdown = null;
                await window.RequestApplicationExitAsync().WaitAsync(timeout.Token);
            }
        }
    }

    static CancellationTokenSource TestTimeout()
        => new(TimeSpan.FromSeconds(25));

    static async Task UntilAsync(Func<bool> condition, CancellationToken token)
    {
        while (!condition()) await Task.Delay(10, token);
    }

    static bool IsExecuting(int pid)
    {
        string stat;
        try { stat = File.ReadAllText($"/proc/{pid}/stat"); }
        catch (IOException) when (!Directory.Exists($"/proc/{pid}")) { return false; }
        // A terminated descendant may still have a process entry until init reaps it.
        return stat[stat.LastIndexOf(')') + 2] is not ('Z' or 'X');
    }

    sealed class Session : IAsyncDisposable
    {
        readonly TempDir folder;
        readonly string media;

        public Player Player { get; }
        public FlyleafPlaybackController Controller { get; }
        public GpuWorkCoordinator Coordinator { get; } = new();
        public string PidPath => Path.Combine(folder.Path, "pid");
        public string FinishPath => Path.Combine(folder.Path, "finish");

        public Session(string arguments, string? engineScript = null)
        {
            string? ffmpeg = Environment.GetEnvironmentVariable("LLPLAYER_FFMPEG_DIR");
            string? fixture = Environment.GetEnvironmentVariable("LLPLAYER_TEST_MEDIA");
            Assert.SkipWhen(string.IsNullOrWhiteSpace(ffmpeg) || string.IsNullOrWhiteSpace(fixture),
                "Requires the native FFmpeg libraries and the canonical offline media fixture.");
            media = fixture!;
            folder = new TempDir();
            var dispatcher = new AvaloniaUIDispatcher();
            var bridge = new CollectionSyncBridge(dispatcher);
            EngineBootstrap.InstallSeams(dispatcher, new AvaloniaHostServices(() => null), bridge, new KeyMapper());
            if (!Engine.IsLoaded) EngineBootstrap.Start(EngineBootstrap.CreateEngineConfig(ffmpeg!, new(folder.Path, folder.Path)));

            string executable = Path.Combine(folder.Path, OperatingSystem.IsWindows() ? "fixture.exe" : "fixture");
            // The invalid executable passes local path validation and deterministically fails when the engine actually launches it.
            File.WriteAllText(executable, engineScript ?? "invalid fixture executable\n");
            if (OperatingSystem.IsLinux())
                File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

            var config = EngineBootstrap.CreatePlayerConfig(new AppPrefs().Normalize());
            config.Player.AutoPlay = false;
            config.Subtitles.SearchLocal = false;
            config.Subtitles.LanguageAutoDetect = false;
            config.Subtitles.ASREngine = SubASREngineType.FasterWhisper;
            config.Subtitles.WhisperConfig.LanguageDetection = false;
            config.Subtitles.WhisperConfig.Language = "en";
            config.Subtitles.FasterWhisperConfig.UseManualEngine = true;
            config.Subtitles.FasterWhisperConfig.ManualEnginePath = executable;
            config.Subtitles.FasterWhisperConfig.UseManualModel = true;
            config.Subtitles.FasterWhisperConfig.ManualModelDir = folder.Path;
            config.Subtitles.FasterWhisperConfig.ExtraArguments = arguments;
            config.Subtitles.WordTimestamps = false;
            config.Subtitles.VadCueSnapping = false;
            Player = new Player(config);
            Controller = new FlyleafPlaybackController(Player, dispatcher, bridge, Coordinator);
            Assert.Null(Player.Open(media).Error);
            Assert.True(Player.Audio.IsOpened);
        }

        public async Task ReopenAsync(CancellationToken token)
        {
            var opened = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            Controller.Open(media, error => opened.TrySetResult(error));
            Assert.Null(await opened.Task.WaitAsync(token));
        }

        public async ValueTask DisposeAsync()
        {
            try { await Controller.DisposeAsync(); }
            finally
            {
                Player.Dispose();
                folder.Dispose();
            }
        }
    }
}
