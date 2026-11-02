using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FlyleafLib.MediaPlayer.AI;
using FlyleafLib.MediaPlayer.Batch;
using FlyleafLib.MediaPlayer.Dubbing;
using LLPlayer.Avalonia.Services;
using LLPlayer.Avalonia.ViewModels;
using LLPlayer.Avalonia.Views;
using LLPlayer.Core.Batch;
using LLPlayer.Core.Services;
using LLPlayer.Core.Services.Models;

namespace LLPlayer.Avalonia.Tests;

public sealed class LanguageWorkflowRoutingTests
{
    [AvaloniaFact]
    public async Task Mixed_AI_and_busy_legacy_Translation_preserves_child_veto_and_resets_close_policy()
    {
        using TempDir temp = new();
        var (window, vm, _) = Create();
        var first = Insights(temp);
        var second = Insights(temp);
        PendingDiagnostics diagnostics = new();
        var translation = new TranslationViewModel(new() { WordProvider = "OpenAILike" }, _ => Task.CompletedTask,
            diagnostics: diagnostics);
        int shutdownCalls = 0, parentClosingCalls = 0;
        window.ShutdownRequested = () => { shutdownCalls++; return Task.CompletedTask; };
        window.Closing += (_, _) => parentClosingCalls++;
        Task? diagnostic = null;
        try
        {
            Assert.Equal(WindowClosingBehavior.OwnerAndChildWindows, window.ClosingBehavior);
            window.ShowAiInsights(first);
            Assert.Equal(WindowClosingBehavior.OwnerWindowOnly, window.ClosingBehavior);
            var firstWindow = Assert.Single(window.OwnedWindows.OfType<AiInsightsWindow>());
            firstWindow.Close();
            await Until(() => !window.OwnedWindows.OfType<AiInsightsWindow>().Any());
            Assert.Equal(WindowClosingBehavior.OwnerAndChildWindows, window.ClosingBehavior);

            window.ShowAiInsights(second);
            Assert.Equal(WindowClosingBehavior.OwnerWindowOnly, window.ClosingBehavior);
            window.ShowTranslation(translation);
            var translationWindow = Assert.Single(window.OwnedWindows.OfType<TranslationWindow>());
            Assert.Equal(WindowClosingBehavior.OwnerAndChildWindows, window.ClosingBehavior);
            diagnostic = translation.CheckProviderCommand.ExecuteAsync(null);
            Assert.True(translation.DiagnosticRunning);
            Assert.False(diagnostics.Token.IsCancellationRequested);

            window.Close();
            await Task.Yield();
            Dispatcher.UIThread.RunJobs();
            Assert.True(diagnostics.Token.IsCancellationRequested);
            Assert.Equal(0, parentClosingCalls);
            Assert.Equal(0, shutdownCalls);
            Assert.True(window.IsVisible);

            diagnostics.Result.SetResult(new(TranslationDiagnosticOutcome.Success));
            await diagnostic;
            await Until(() => !translationWindow.IsVisible);
            window.Close();
            await Until(() => !window.IsVisible);
            Assert.True(parentClosingCalls > 0);
            Assert.Equal(1, shutdownCalls);
        }
        finally
        {
            diagnostics.Result.TrySetResult(new(TranslationDiagnosticOutcome.Success));
            if (diagnostic != null) await diagnostic;
            await translation.DrainDiagnosticsAsync();
            window.RequestApplicationExit();
            await Until(() => !window.IsVisible);
            await first.DisposeAsync(); await second.DisposeAsync(); vm.Dispose();
        }
    }

    [AvaloniaFact]
    public async Task Closing_busy_model_manager_owned_by_Recognition_drains_only_that_manager()
    {
        using TempDir temp = new();
        var (window, vm, _) = Create();
        using RecognitionActions actions = new(new NoRecognition());
        var recognition = new RecognitionViewModel(new(), actions, new(Path.Combine(temp.Path, "recognition.json")));
        var resources = new CancelableModels();
        var model = new ModelManagerViewModel(resources, ModelKind.Whisper, _ => { });
        int shutdowns = 0;
        window.ShutdownRequested = () => { shutdowns++; return Task.CompletedTask; };
        Task? showing = null;
        Task? download = null;
        try
        {
            window.ShowRecognition(recognition);
            var recognitionWindow = Assert.Single(window.OwnedWindows.OfType<RecognitionWindow>());
            showing = window.ShowModelManagerAsync(model);
            var managerWindow = Assert.Single(recognitionWindow.OwnedWindows.OfType<ModelManagerWindow>());
            Assert.Same(recognitionWindow, managerWindow.Owner);
            download = model.DownloadCommand.ExecuteAsync(null);
            await resources.DownloadStarted.Task;
            Assert.True(model.Busy);
            Assert.False(resources.DownloadToken.IsCancellationRequested);

            managerWindow.Close();
            await Until(() => showing.IsCompleted);
            await showing;
            await download;
            Assert.True(resources.DownloadToken.IsCancellationRequested);
            Assert.False(model.Busy);
            Assert.False(managerWindow.IsVisible);
            Assert.False(model.RefreshCommand.CanExecute(null));
            Assert.True(recognitionWindow.IsVisible);
            Assert.True(recognitionWindow.IsEnabled);
            Assert.True(window.IsVisible);
            Assert.Equal(0, shutdowns);
        }
        finally
        {
            await model.DisposeAsync();
            if (download != null) await download;
            window.RequestApplicationExit();
            await Until(() => !window.IsVisible);
            if (showing != null) await showing;
            vm.Dispose();
        }
    }

    [AvaloniaFact]
    public async Task Model_manager_singleton_retires_unused_draft_and_remains_owned_until_closed()
    {
        var (window, vm, _) = Create();
        var first = new ModelManagerViewModel(new EmptyModels(), ModelKind.Whisper, _ => { });
        var unused = new ModelManagerViewModel(new EmptyModels(), ModelKind.Tesseract, _ => { });
        Task? showing = null;
        try
        {
            Assert.True(unused.RefreshCommand.CanExecute(null));
            showing = window.ShowModelManagerAsync(first);
            Assert.False(showing.IsCompleted);
            await window.ShowModelManagerAsync(unused);
            var child = Assert.Single(window.OwnedWindows.OfType<ModelManagerWindow>());
            Assert.Same(first, child.DataContext);
            Assert.False(unused.RefreshCommand.CanExecute(null));
            Assert.True(first.RefreshCommand.CanExecute(null));
            child.Close();
            await showing;
            Assert.False(first.RefreshCommand.CanExecute(null));
        }
        finally
        {
            window.RequestApplicationExit();
            if (showing != null) await showing;
            await first.DisposeAsync(); await unused.DisposeAsync(); vm.Dispose();
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Owned_voice_save_and_inherit_default_are_drained_before_main_shutdown(bool inheritDefault)
    {
        var (window, vm, _) = Create();
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        bool shutdown = false;
        var model = new VoicePickerViewModel(VoiceBankResolver.BuiltIn, VoiceBankResolver.BuiltIn[0].Id, _ => release.Task);
        window.ShutdownRequested = () => { shutdown = true; return Task.CompletedTask; };
        Task? editing = null;
        try
        {
            window.ShowVoicePicker(model);
            editing = inheritDefault ? model.UseDefaultCommand.ExecuteAsync(null) : model.SaveCommand.ExecuteAsync(null);
            Assert.True(model.Busy);
            window.RequestApplicationExit();
            Assert.False(shutdown);
            Assert.True(window.IsVisible);
            release.SetResult();
            await editing;
            await Until(() => !window.IsVisible);
            Assert.True(shutdown);
            Assert.False(model.Busy);
        }
        finally
        {
            release.TrySetResult(); if (editing != null) await editing;
            window.RequestApplicationExit(); vm.Dispose();
        }
    }

    [AvaloniaFact]
    public async Task Bound_menu_actions_use_injected_factories_and_AI_keeps_one_session_until_closed()
    {
        using TempDir temp = new();
        var (window, vm, _) = Create();
        await using var batch = new BatchWorkflow(new(), new NoRuns(), new NoWatcher(), action => action(), new EmptyCatalog());
        await using var batchModel = new BatchSubtitlesViewModel(batch, new(Path.Combine(temp.Path, "batch.json")),
            _ => Task.FromResult<string?>(null), _ => Task.CompletedTask, _ => Task.FromResult(false));
        using RecognitionActions actions = new(new NoRecognition());
        var recognition = new RecognitionViewModel(new(), actions, new(Path.Combine(temp.Path, "recognition.json")));
        var dubbing = new DubbingViewModel(new(), _ => throw new InvalidOperationException("Opening must not save."));
        var created = new List<AiInsightsViewModel>();
        int recognitionFactories = 0, batchFactories = 0, dubbingFactories = 0;
        vm.AiInsightsDialog = () => { var model = Insights(temp); created.Add(model); return model; };
        vm.RecognitionDialog = () => { recognitionFactories++; return recognition; };
        vm.BatchDialog = () => { batchFactories++; return batchModel; };
        vm.DubbingDialog = () => { dubbingFactories++; return dubbing; };
        try
        {
            RunMenu(window, "AI Insights…");
            RunMenu(window, "AI Insights…");
            Assert.Single(created);
            var aiWindow = Assert.Single(window.OwnedWindows.OfType<AiInsightsWindow>());
            Assert.Same(created[0], aiWindow.DataContext);
            aiWindow.Close();
            await Until(() => !window.OwnedWindows.OfType<AiInsightsWindow>().Any());
            RunMenu(window, "AI Insights…");
            Assert.Equal(2, created.Count);
            Assert.True(created[0].IsClosing);

            RunMenu(window, "Recognition settings…");
            var recognitionWindow = Assert.Single(window.OwnedWindows.OfType<RecognitionWindow>());
            Assert.Same(recognition, recognitionWindow.DataContext);
            recognitionWindow.Close();
            RunMenu(window, "Dubbing settings…");
            var dubbingWindow = Assert.Single(window.OwnedWindows.OfType<DubbingWindow>());
            Assert.Same(dubbing, dubbingWindow.DataContext);
            dubbingWindow.Close();
            RunMenu(window, "Batch subtitles…");
            Assert.True(batch.IsOpen);
            Assert.Equal(1, recognitionFactories);
            Assert.Equal(1, dubbingFactories);
            Assert.Equal(1, batchFactories);
            Assert.False(File.Exists(Path.Combine(temp.Path, "recognition.json")));
            Assert.False(File.Exists(Path.Combine(temp.Path, "batch.json")));
        }
        finally
        {
            window.RequestApplicationExit(); vm.Dispose();
            foreach (var model in created) await model.DisposeAsync();
        }
    }

    [AvaloniaFact]
    public async Task Main_close_background_preserves_work_but_explicit_Exit_drains_once_then_closes()
    {
        var (window, vm, _) = Create();
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int background = 0, shutdown = 0;
        window.KeepAliveOnClose = () => true;
        window.BackgroundRequested = requested => { Assert.Same(window, requested); background++; };
        window.ShutdownRequested = async () => { shutdown++; await release.Task; };
        try
        {
            window.Close();
            Assert.True(window.IsVisible);
            Assert.True(window.IsEnabled);
            Assert.Equal(1, background);
            Assert.Equal(0, shutdown);
            vm.ExitCommand.Execute(null);
            Assert.Equal(1, shutdown);
            Assert.False(window.IsEnabled);
            Assert.True(window.IsVisible);
            window.RequestApplicationExit();
            Assert.Equal(1, shutdown);
            release.SetResult();
            await Until(() => !window.IsVisible);
            Assert.Equal(1, background);
        }
        finally { release.TrySetResult(); window.RequestApplicationExit(); vm.Dispose(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Main_shutdown_waits_for_owned_AI_request_and_suppresses_its_late_result(bool explicitExit)
    {
        using TempDir temp = new();
        var (window, vm, _) = Create();
        TaskCompletionSource<string> reply = new(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken requestToken = default;
        var model = new AiInsightsViewModel((slot, translated) => Snapshot(slot, translated),
            () => new("offline stub", new AiInsightService((_, _, token) => { requestToken = token; return reply.Task; })),
            new WordLibrary(Path.Combine(temp.Path, "words.json"))) { SelectedMode = AiInsightMode.Summary };
        bool shutdown = false;
        window.ShutdownRequested = () => { shutdown = true; return Task.CompletedTask; };
        try
        {
            window.ShowAiInsights(model);
            Task generate = model.StartGenerationAsync();
            Assert.True(model.Busy);
            if (explicitExit) window.RequestApplicationExit(); else window.Close();
            Assert.True(requestToken.IsCancellationRequested);
            Assert.False(shutdown);
            Assert.True(window.IsVisible);
            reply.SetResult("obsolete provider reply");
            await generate;
            await Until(() => !window.IsVisible);
            Assert.True(shutdown);
            Assert.False(model.HasResults);
            Assert.True(model.IsClosing);
        }
        finally { reply.TrySetResult("cleanup"); await model.DisposeAsync(); window.RequestApplicationExit(); vm.Dispose(); }
    }

    [AvaloniaFact]
    public void Voice_column_updates_existing_row_and_click_routes_only_a_current_source()
    {
        var (window, vm, player) = Create();
        var chosen = new List<CueItemViewModel>();
        vm.VoicePickerDialog = row => { chosen.Add(row); return null; };
        try
        {
            player.IsOpened = true;
            player.Cues[0].Add(new(0, TimeSpan.Zero, TimeSpan.FromSeconds(2), "line", false, "en", "ru", "line"));
            player.RaiseCuesChanged(0);
            if (!vm.SidebarVisible) vm.ToggleSidebarCommand.Execute(null);
            Assert.True(vm.SidebarVisible);
            Pump(window);
            var row = Assert.Single(vm.Sidebar.Cues);
            var button = Assert.Single(window.GetVisualDescendants().OfType<Button>(), control => control.Name == "CueVoiceButton");
            Assert.Equal("Default", button.Content);
            Assert.NotNull(button.Command);
            Assert.Same(row, button.CommandParameter);
            Assert.True(button.IsEffectivelyVisible);
            Assert.True(button.Bounds.Width > 0 && button.Bounds.Height > 0);
            Point point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
            window.MouseMove(point);
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            Pump(window);
            Assert.Same(row, Assert.Single(chosen));
            Assert.Empty(player.Seeks);
            Assert.Equal(0, player.ToggleCalls);

            player.Cues[0][0] = player.Cues[0][0] with { AssignedVoiceId = "selected-voice" };
            player.RaiseCuesChanged(0);
            Pump(window);
            Assert.Same(row, Assert.Single(vm.Sidebar.Cues));
            Assert.Equal("selected-voice", button.Content);

            // Same index/text/timing, different opaque source: a retained visual must not open a stale picker.
            player.ReplaceCueSource(0);
            vm.AssignCueVoiceCommand.Execute(row);
            Assert.Single(chosen);
            player.RaiseCuesChanged(0);
            Pump(window);
            var replacement = Assert.Single(vm.Sidebar.Cues);
            Assert.NotSame(row, replacement);
            vm.AssignCueVoiceCommand.Execute(replacement);
            Assert.Equal(2, chosen.Count);
            Assert.Same(replacement, chosen[1]);
        }
        finally { window.RequestApplicationExit(); vm.Dispose(); }
    }

    [AvaloniaFact]
    public void Owned_language_dialog_consumes_child_key_release_after_Escape_without_playback_leak()
    {
        var (window, vm, player) = Create();
        try
        {
            window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            var model = new DubbingViewModel(new(), _ => Task.CompletedTask);
            window.ShowDubbing(model);
            var child = Assert.Single(window.OwnedWindows.OfType<DubbingWindow>());
            child.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            child.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            Dispatcher.UIThread.RunJobs();
            Assert.False(child.IsVisible);
            Assert.Equal(new[] { System.Windows.Input.Key.Space }, player.KeysDown);
            Assert.Equal(new[] { System.Windows.Input.Key.Space }, player.KeysUp);
        }
        finally { window.RequestApplicationExit(); vm.Dispose(); }
    }

    static (MainWindow Window, MainWindowViewModel Vm, FakePlaybackController Player) Create()
    {
        FakePlaybackController player = new();
        MainWindow window = new(new KeyMapper(), null) { Width = 1100, Height = 700 };
        MainWindowViewModel vm = new(player, new AppPrefs().Normalize(), _ => { }, new FakeTranslator(), window);
        window.Attach(vm, null); window.Show(); Dispatcher.UIThread.RunJobs();
        return (window, vm, player);
    }

    static void RunMenu(MainWindow window, string header)
    {
        static IEnumerable<MenuItem> Descendants(IEnumerable<object> items)
        {
            foreach (var item in items.OfType<MenuItem>())
            {
                yield return item;
                foreach (var child in Descendants(item.Items.Cast<object>())) yield return child;
            }
        }
        var menu = Assert.Single(Descendants(window.FindControl<Menu>("MainMenu")!.Items.Cast<object>()), item => Equals(item.Header, header));
        Assert.NotNull(menu.Command);
        Assert.True(menu.Command.CanExecute(menu.CommandParameter));
        menu.Command.Execute(menu.CommandParameter);
        Dispatcher.UIThread.RunJobs();
    }

    static AiInsightsViewModel Insights(TempDir temp) => new((slot, translated) => Snapshot(slot, translated),
        () => null, new WordLibrary(Path.Combine(temp.Path, "words.json")));
    static InsightsTranscriptSnapshot Snapshot(int slot, bool translated) => new(1, "fixture", DateTimeOffset.UtcNow,
        slot, translated, "English", "en", "Russian", "ru", ["offline cue"], false);
    static void Pump(MainWindow window)
    {
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame()?.Dispose();
        Dispatcher.UIThread.RunJobs();
    }
    static async Task Until(Func<bool> condition)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        while (!condition()) { Dispatcher.UIThread.RunJobs(); await Task.Delay(10, timeout.Token); }
    }

    sealed class NoRecognition : IRecognitionBackend
    {
        public bool HasAudio => false;
        public bool IsLive => false;
        public bool IsBusy => false;
        public bool IsPaused => false;
        public void Start(int slot) => throw new InvalidOperationException("No inference in routing tests.");
        public void Pause() { }
        public void Resume() { }
        public void Cancel(int slot) { }
        public void Ocr(int slot) => throw new InvalidOperationException("No inference in routing tests.");
        public void Apply(RecognitionSettings settings) => throw new InvalidOperationException("No settings save in routing tests.");
    }
    sealed class NoRuns : IBatchRunFactory
    {
        public IBatchRun Create(BatchSettings settings, IReadOnlyList<BatchSubtitleJob> jobs) => throw new InvalidOperationException("No inference in routing tests.");
    }
    sealed class NoWatcher : IBatchFolderWatcher
    {
        public event Action<BatchWatchReady>? FileReady { add { } remove { } }
        public event Action<BatchWatchError>? Error { add { } remove { } }
        public long Start(string path, bool recursive) => throw new InvalidOperationException("No file watch in routing tests.");
        public void Stop() { }
        public void Dispose() { }
    }
    sealed class EmptyCatalog : IBatchMediaCatalog
    {
        public bool FolderExists(string path) => false;
        public bool TranslationExists(string path) => false;
        public bool DubExists(string path) => false;
        public Task<IReadOnlyList<string>> ScanAsync(string folder, bool recursive, CancellationToken token) => Task.FromResult<IReadOnlyList<string>>([]);
        public Task<IReadOnlyList<MediaAudioTrack>> ProbeAsync(string path, CancellationToken token) => Task.FromResult<IReadOnlyList<MediaAudioTrack>>([]);
    }
    sealed class EmptyModels : IModelManagementService
    {
        public IReadOnlyList<ModelEntry> Discover(ModelKind kind) => [];
        public string GetDirectory(ModelKind kind) => Path.GetTempPath();
        public Task DownloadAsync(string id, IProgress<ModelDownloadProgress>? progress, CancellationToken cancellationToken) => throw new InvalidOperationException("No downloads in routing tests.");
        public Task DeleteAsync(string id, CancellationToken cancellationToken) => throw new InvalidOperationException("No model deletion in routing tests.");
    }
    sealed class CancelableModels : IModelManagementService
    {
        public TaskCompletionSource DownloadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken DownloadToken { get; private set; }
        public IReadOnlyList<ModelEntry> Discover(ModelKind kind) =>
            [new("fixture", kind, "Fixture model", "fixture.bin", Path.GetTempPath(), false, 0)];
        public string GetDirectory(ModelKind kind) => Path.GetTempPath();
        public Task DownloadAsync(string id, IProgress<ModelDownloadProgress>? progress, CancellationToken cancellationToken)
        {
            DownloadToken = cancellationToken;
            DownloadStarted.TrySetResult();
            return Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
        public Task DeleteAsync(string id, CancellationToken cancellationToken) => throw new InvalidOperationException("No model deletion in routing tests.");
    }
    sealed class PendingDiagnostics : ITranslationDiagnostics
    {
        public TaskCompletionSource<TranslationDiagnosticResult> Result { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken Token { get; private set; }
        public Task<TranslationDiagnosticResult> RunAsync(TranslationDiagnosticRequest request, CancellationToken token)
        { Token = token; return Result.Task; }
    }
}
