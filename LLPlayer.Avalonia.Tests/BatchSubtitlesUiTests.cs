using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using FlyleafLib.MediaPlayer.Batch;
using FlyleafLib.MediaPlayer.Dubbing;
using LLPlayer.Avalonia.Services;
using LLPlayer.Avalonia.ViewModels;
using LLPlayer.Avalonia.Views;
using LLPlayer.Core.Batch;

namespace LLPlayer.Avalonia.Tests;

public sealed class BatchSubtitlesUiTests
{
    [Fact]
    public async Task Quit_waits_for_admitted_aggregate_options_save_before_disposing_workflow()
    {
        string root = Path.Combine(Path.GetTempPath(), "llplayer-batch-save-drain-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        string voice = "A"; TtsVoice[] voices = [new("A", "A", "", "ru"), new("B", "B", "", "ru")];
        var store = new BatchSettingsStore(Path.Combine(root, "batch.json"));
        await using var workflow = new BatchWorkflow(new BatchSettings { LastFolder = root }, new Runs(), new Watcher(), a => a(), new Catalog());
        var model = new BatchSubtitlesViewModel(workflow, store, _ => Task.FromResult<string?>(null), _ => Task.CompletedTask, _ => Task.FromResult(true),
            getVoices: () => voices, getDefaultVoiceId: () => voice, saveDefaultVoiceId: async value => { entered.TrySetResult(); await release.Task; voice = value; });
        try
        {
            model.Opened(); model.SelectedVoice = voices[1];
            Task save = model.SaveOptionsCommand.ExecuteAsync(null); await entered.Task;
            Task quit = model.DisposeAsync().AsTask(); Assert.False(quit.IsCompleted);
            release.TrySetResult(); await Task.WhenAll(save, quit);
            Assert.Equal("B", voice); Assert.False(workflow.KeepAlive);
        }
        finally { release.TrySetResult(); await model.DisposeAsync(); foreach (string file in Directory.GetFiles(root)) File.Delete(file); Directory.Delete(root); }
    }
    [AvaloniaFact]
    public async Task Failed_batch_write_preserves_voice_and_both_live_settings()
    {
        string root = Path.Combine(Path.GetTempPath(), "llplayer-batch-fail-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string blocked = Path.Combine(root, "batch.json"); Directory.CreateDirectory(blocked);
        string voice = "A"; int voiceWrites = 0;
        TtsVoice[] voices = [new("A", "A", "", "ru"), new("B", "B", "", "ru")];
        var store = new BatchSettingsStore(blocked);
        await using var workflow = new BatchWorkflow(new BatchSettings { LastFolder = root }, new Runs(), new Watcher(), a => a(), new Catalog());
        await using var model = new BatchSubtitlesViewModel(workflow, store, _ => Task.FromResult<string?>(null), _ => Task.CompletedTask, _ => Task.FromResult(true),
            getVoices: () => voices, getDefaultVoiceId: () => voice, saveDefaultVoiceId: value => { voiceWrites++; voice = value; return Task.CompletedTask; });
        try
        {
            model.Opened(); model.SelectedVoice = voices[1]; model.Draft.Recursive = false;
            await model.SaveOptionsCommand.ExecuteAsync(null);
            Assert.Equal("A", voice); Assert.Equal(0, voiceWrites); Assert.True(workflow.Settings.Recursive);
            Assert.True(Directory.Exists(blocked)); Assert.Contains("Cannot save", workflow.Message);
        }
        finally { Directory.Delete(blocked); Directory.Delete(root); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failed_voice_write_rolls_back_batch_bytes_or_absence_before_any_live_apply(bool originallyPresent)
    {
        string root = Path.Combine(Path.GetTempPath(), "llplayer-batch-rollback-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        string path = Path.Combine(root, "batch.json"); var initial = new BatchSettings { LastFolder = root };
        var store = new BatchSettingsStore(path); if (originallyPresent) store.Save(initial);
        byte[]? original = originallyPresent ? File.ReadAllBytes(path) : null;
        TtsVoice[] voices = [new("A", "A", "", "ru"), new("B", "B", "", "ru")];
        await using var workflow = new BatchWorkflow(initial, new Runs(), new Watcher(), a => a(), new Catalog());
        await using var model = new BatchSubtitlesViewModel(workflow, store, _ => Task.FromResult<string?>(null), _ => Task.CompletedTask, _ => Task.FromResult(true),
            getVoices: () => voices, getDefaultVoiceId: () => "A", saveDefaultVoiceId: _ => throw new IOException("voice write denied"));
        try
        {
            model.Opened(); model.SelectedVoice = voices[1]; model.Draft.Recursive = false;
            await model.SaveOptionsCommand.ExecuteAsync(null);
            Assert.True(workflow.Settings.Recursive); Assert.Equal("B", model.SelectedVoice!.Id);
            Assert.Contains("voice write denied", workflow.Message);
            if (originallyPresent) Assert.Equal(original, File.ReadAllBytes(path)); else Assert.False(File.Exists(path));
        }
        finally { if (File.Exists(path)) File.Delete(path); Directory.Delete(root); }
    }

    [Fact]
    public async Task Failed_voice_write_never_rolls_back_a_foreign_batch_replacement()
    {
        string root = Path.Combine(Path.GetTempPath(), "llplayer-batch-conflict-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        string path = Path.Combine(root, "batch.json"); var store = new BatchSettingsStore(path);
        TtsVoice[] voices = [new("A", "A", "", "ru"), new("B", "B", "", "ru")];
        await using var workflow = new BatchWorkflow(new BatchSettings { LastFolder = root }, new Runs(), new Watcher(), a => a(), new Catalog());
        await using var model = new BatchSubtitlesViewModel(workflow, store, _ => Task.FromResult<string?>(null), _ => Task.CompletedTask, _ => Task.FromResult(true),
            getVoices: () => voices, getDefaultVoiceId: () => "A", saveDefaultVoiceId: _ => { File.WriteAllText(path, "foreign bytes"); throw new IOException("voice denied"); });
        try
        {
            model.Opened(); model.SelectedVoice = voices[1]; await model.SaveOptionsCommand.ExecuteAsync(null);
            Assert.Equal("foreign bytes", File.ReadAllText(path)); Assert.Contains("rollback", workflow.Message); Assert.Contains("changed", workflow.Message);
        }
        finally { File.Delete(path); Directory.Delete(root); }
    }
    [AvaloniaFact]
    public async Task Untouched_batch_voice_preserves_newer_default_and_explicit_selection_wins()
    {
        string folder = Path.Combine(Path.GetTempPath(), "llplayer-batch-voice-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string current = "A"; var saved = new List<string>();
        TtsVoice[] voices = [new("A", "A", "", "ru"), new("B", "B", "", "ru"), new("C", "C", "", "ru")];
        var store = new BatchSettingsStore(Path.Combine(folder, "prefs.json"));
        await using var workflow = new BatchWorkflow(new BatchSettings { LastFolder = folder }, new Runs(), new Watcher(), a => a(), new Catalog());
        await using var model = new BatchSubtitlesViewModel(workflow, store, _ => Task.FromResult<string?>(null), _ => Task.CompletedTask, _ => Task.FromResult(true),
            getVoices: () => voices, getDefaultVoiceId: () => current, saveDefaultVoiceId: voice => { saved.Add(voice); current = voice; return Task.CompletedTask; });
        try
        {
            model.Opened(); Assert.Equal("A", model.SelectedVoice!.Id);
            current = "B";
            await model.ScanCommand.ExecuteAsync(null); await model.StartCommand.ExecuteAsync(null); await model.SaveOptionsCommand.ExecuteAsync(null);
            Assert.Equal("B", current); Assert.Empty(saved);
            model.SelectedVoice = voices[2];
            await model.SaveOptionsCommand.ExecuteAsync(null);
            Assert.Equal("C", current); Assert.Equal(new[] { "C" }, saved);
        }
        finally { foreach (string file in Directory.GetFiles(folder)) File.Delete(file); Directory.Delete(folder); }
    }

    [Theory]
    [InlineData("wayland", "", ":0", false)]
    [InlineData("x11", "wayland-0", ":0", false)]
    [InlineData("x11", "", ":0", true)]
    [InlineData("", "", "", false)]
    public void XWayland_is_never_advertised_as_global_idle(string session, string wayland, string display, bool allowed) =>
        Assert.Equal(allowed, BatchActivityMonitor.CanUseGlobalX11Idle(session, wayland, display));
    [AvaloniaFact]
    public async Task Watch_start_commits_options_then_starts_and_close_keeps_window_accessible()
    {
        string folder = Path.Combine(Path.GetTempPath(), "llplayer-batch-ui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var watcher = new Watcher();
        var store = new BatchSettingsStore(Path.Combine(folder, "prefs.json"));
        await using var workflow = new BatchWorkflow(new BatchSettings { LastFolder = folder }, new Runs(), watcher, a => a(), new Catalog());
        await using var model = new BatchSubtitlesViewModel(workflow, store, _ => Task.FromResult<string?>(null), _ => Task.CompletedTask, _ => Task.FromResult(true));
        var window = new BatchSubtitlesWindow(model);
        try
        {
            window.Show();
            Assert.NotNull(window.FindControl<Button>("BatchWatchStartStop"));
            Assert.False(workflow.WatchFolder);
            await model.ToggleWatchCommand.ExecuteAsync(null);
            Assert.True(watcher.Started);
            Assert.True(store.Load().WatchFolder);
            Assert.Equal("Stop watching", model.WatchButtonText);
            window.Close();
            Assert.True(window.IsVisible); // No tray owner: closing backgrounds by minimizing rather than hiding.
            Assert.Equal(WindowState.Minimized, window.WindowState);
            Assert.True(workflow.KeepAlive);
            window.Restore();
            Assert.Equal(WindowState.Normal, window.WindowState);
            await model.ToggleWatchCommand.ExecuteAsync(null);
            Assert.False(workflow.WatchFolder);
            window.Close();
            Assert.False(workflow.IsOpen);
        }
        finally
        {
            window.IsShuttingDown = true; window.Close();
            foreach (string file in Directory.GetFiles(folder)) File.Delete(file);
            Directory.Delete(folder);
        }
    }

    [Fact]
    public void Missing_settings_keep_Wpf_defaults_and_corrupt_bytes_survive_load()
    {
        string path = Path.Combine(Path.GetTempPath(), "llplayer-batch-" + Guid.NewGuid().ToString("N") + ".json");
        var store = new BatchSettingsStore(path);
        var defaults = store.Load();
        Assert.True(defaults.Recursive); Assert.True(defaults.SerializeAsrAndTranslate);
        Assert.True(defaults.RunOnCpuWhenActive); Assert.True(defaults.PreferRussianAudio);
        Assert.Equal(45, defaults.ActiveIdleThresholdSeconds);
        Assert.False(defaults.OverwriteExisting); Assert.False(defaults.GenerateDubbing); Assert.False(defaults.WatchFolder);
        try
        {
            File.WriteAllText(path, "{broken");
            store.Load();
            Assert.NotNull(store.LoadWarning);
            Assert.Equal("{broken", File.ReadAllText(path));
            Assert.Throws<ArgumentException>(() => store.Save(new BatchSettings { ActiveIdleThresholdSeconds = 0 }));
            Assert.Equal("{broken", File.ReadAllText(path));
        }
        finally { File.Delete(path); }
    }

    sealed class Watcher : IBatchFolderWatcher
    {
        public event Action<BatchWatchReady>? FileReady { add { } remove { } }
        public event Action<BatchWatchError>? Error { add { } remove { } }
        public bool Started { get; private set; }
        public long Start(string path, bool recursive) { Started = true; return 1; }
        public void Stop() => Started = false;
        public void Dispose() => Stop();
    }
    sealed class Runs : IBatchRunFactory
    {
        public IBatchRun Create(BatchSettings settings, IReadOnlyList<BatchSubtitleJob> jobs) => throw new InvalidOperationException("Test never starts inference.");
    }
    sealed class Catalog : IBatchMediaCatalog
    {
        public bool FolderExists(string path) => Directory.Exists(path);
        public bool TranslationExists(string path) => false;
        public bool DubExists(string path) => false;
        public Task<IReadOnlyList<string>> ScanAsync(string folder, bool recursive, CancellationToken token) => Task.FromResult<IReadOnlyList<string>>([]);
        public Task<IReadOnlyList<MediaAudioTrack>> ProbeAsync(string path, CancellationToken token) => Task.FromResult<IReadOnlyList<MediaAudioTrack>>([]);
    }
}
