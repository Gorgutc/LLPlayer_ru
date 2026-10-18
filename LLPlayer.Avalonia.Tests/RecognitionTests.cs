using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FlyleafLib;
using FlyleafLib.MediaPlayer;
using LLPlayer.Avalonia.Services;
using LLPlayer.Avalonia.ViewModels;
using LLPlayer.Avalonia.Views;
using LLPlayer.Core.Services;
using Whisper.net.Ggml;

namespace LLPlayer.Avalonia.Tests;

[Collection(FlyleafGlobalsCollection.Name)]
public class RecognitionTests
{
    [AvaloniaFact]
    public async Task Dialog_bound_draft_discard_save_failure_and_render()
    {
        using TempDir temp = new();
        var live = new RecognitionSettings();
        live.WhisperCpp.Prompt = "advanced survives";
        var backend = new Backend();
        using var actions = new RecognitionActions(backend);
        var store = new RecognitionSettingsStore(Path.Combine(temp.Path, "recognition.json"));
        var model = new RecognitionViewModel(live, actions, store);
        var window = new RecognitionWindow(model);
        window.Show();
        var directory = window.FindControl<TextBox>("ModelDirectoryBox")!;
        directory.Text = temp.Path;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(temp.Path, model.ModelDirectory);
        model.Draft.Whisper.Language = "ru";
        window.Close();
        Assert.Equal("en", live.Whisper.Language);
        Assert.Null(store.Load());
        Assert.Null(backend.Applied);

        model = new RecognitionViewModel(live, actions, store) { ModelDirectory = temp.Path };
        model.Draft.OcrPrimaryLanguage = "ru";
        window = new RecognitionWindow(model);
        window.Show();
        await model.SaveCommand.ExecuteAsync(null);
        Assert.Equal("advanced survives", backend.Applied!.WhisperCpp.Prompt);
        Assert.Equal("ru", store.Load()!.OcrPrimaryLanguage);
        Assert.False(window.IsVisible);

        var failed = new RecognitionViewModel(live, actions, new RecognitionSettingsStore(temp.Path)) { ModelDirectory = temp.Path };
        window = new RecognitionWindow(failed);
        window.Show();
        await failed.SaveCommand.ExecuteAsync(null);
        Assert.True(window.IsVisible);
        Assert.NotEmpty(failed.Message);
        string? output = Environment.GetEnvironmentVariable("LLPLAYER_RECOGNITION_ARTIFACTS");
        if (output != null)
        {
            Directory.CreateDirectory(output);
            foreach (string theme in new[] { "Light", "Dark" })
            {
                ThemeService.Apply(theme);
                Dispatcher.UIThread.RunJobs();
                window.CaptureRenderedFrame()!.Save(Path.Combine(output, $"recognition-{theme.ToLowerInvariant()}.png"), PngBitmapEncoderOptions.Default);
            }
            window.FindControl<ScrollViewer>("RecognitionScroll")!.Offset = new Vector(0, 500);
            Dispatcher.UIThread.RunJobs();
            window.CaptureRenderedFrame()!.Save(Path.Combine(output, "recognition-lower.png"), PngBitmapEncoderOptions.Default);
            window.FindControl<ScrollViewer>("RecognitionScroll")!.Offset = default;
            window.Width = 420; window.Height = 360;
            Dispatcher.UIThread.RunJobs();
            window.CaptureRenderedFrame()!.Save(Path.Combine(output, "recognition-small.png"), PngBitmapEncoderOptions.Default);
        }
        window.Close();
    }

    [AvaloniaFact]
    public async Task Real_player_headless_main_UI_starts_whisper_and_selected_bitmap_OCR()
    {
        string? ffmpeg = Environment.GetEnvironmentVariable("LLPLAYER_FFMPEG_DIR");
        string? speech = Environment.GetEnvironmentVariable("LLPLAYER_RECOGNITION_SPEECH");
        string? modelDirectory = Environment.GetEnvironmentVariable("LLPLAYER_RECOGNITION_MODEL_DIR");
        string? bitmap = Environment.GetEnvironmentVariable("LLPLAYER_RECOGNITION_BITMAP");
        Assert.SkipWhen(ffmpeg == null || speech == null || modelDirectory == null || bitmap == null,
            "Real recognition UI smoke requires explicit FFmpeg, existing speech/model and bitmap media paths.");
        using TempDir temp = new();
        AvaloniaUIDispatcher dispatcher = new();
        CollectionSyncBridge bridge = new(dispatcher);
        KeyMapper keys = new();
        MainWindow? window = null;
        AvaloniaHostServices host = new(() => window);
        EngineBootstrap.InstallSeams(dispatcher, host, bridge, keys);
        if (!Engine.IsLoaded) EngineBootstrap.Start(EngineBootstrap.CreateEngineConfig(ffmpeg!, new(temp.Path, temp.Path)));
        var prefs = new AppPrefs().Normalize();
        Config config = EngineBootstrap.CreatePlayerConfig(prefs);
        config.Subtitles.SearchLocal = false;
        config.Subtitles.LanguageAutoDetect = false;
        config.Subtitles.WhisperConfig.LanguageDetection = false;
        config.Subtitles.WhisperConfig.Language = "en";
        config.Subtitles.WhisperCppConfig.Model = new WhisperCppModel { Model = GgmlType.LargeV3 };
        config.Subtitles.WhisperCppConfig.ModelDirectory = modelDirectory;
        Player player = new(config);
        var controller = new FlyleafPlaybackController(player, dispatcher, bridge);
        window = new MainWindow(keys, host) { Width = 960, Height = 600 };
        var vm = new MainWindowViewModel(controller, prefs, _ => { }, new FakeTranslator(), window);
        try
        {
            window.Attach(vm, player.Renderer);
            player.Host = window;
            window.Show();
            vm.Open(speech!);
            Assert.True(await Wait(() => vm.IsOpened, 30), "Speech media must open.");
            controller.Pause();
            await vm.StartRecognitionCommand.ExecuteAsync("0");
            Assert.True(await Wait(() => controller.GetCues(0).Count > 0, 240),
                "Whisper large-v3 must produce cues through the UI command: " + string.Join(" | ", vm.Toasts.Select(t => t.Message)));
            Assert.Contains(controller.GetCues(0), cue => cue.Text.Contains("country", StringComparison.OrdinalIgnoreCase));
            await vm.CancelRecognitionCommand.ExecuteAsync("0");
            Assert.True(await Wait(() => !player.IsASRRunning, 30));
            await vm.StartRecognitionCommand.ExecuteAsync("0");
            await vm.ToggleRecognitionPauseCommand.ExecuteAsync(null);
            Assert.True(await Wait(() => player.IsASRPaused, 10), "Pause follows this exact ASR startup attempt.");
            await vm.CancelRecognitionCommand.ExecuteAsync("0");
            Assert.True(await Wait(() => !player.IsASRRunning, 30), "Cancel while globally paused settles ASR.");
            vm.Open(bitmap!);
            Assert.True(await Wait(() => vm.IsOpened && controller.Url == bitmap, 30));
            controller.Pause();
            SubtitleTrack track = controller.GetSubtitleTracks().First(track => !track.IsExternal);
            controller.SelectSubtitleTrack(0, track);
            Assert.True(await Wait(() => player.Subtitles[0].IsBitmap && player.Subtitles[0].IsOpened, 30));
            await vm.OcrRecognitionCommand.ExecuteAsync("0");
            Assert.True(await Wait(() => controller.GetCues(0).Any(cue => cue.Text.Contains("HELLO", StringComparison.OrdinalIgnoreCase)), 60),
                "Canonical selected-bitmap OCR must produce text: " + string.Join(" | ", vm.Toasts.Select(t => t.Message)));
            Assert.Contains(controller.GetCues(0), cue => cue.Text.Contains("LEARNING", StringComparison.OrdinalIgnoreCase));
            string? secondaryBitmap = Environment.GetEnvironmentVariable("LLPLAYER_RECOGNITION_BITMAP_SECONDARY");
            if (secondaryBitmap != null)
            {
                vm.Open(secondaryBitmap);
                Assert.True(await Wait(() => vm.IsOpened && controller.Url == secondaryBitmap, 30));
                controller.Pause();
                SubtitleTrack secondaryTrack = controller.GetSubtitleTracks().First(track => !track.IsExternal);
                controller.SelectSubtitleTrack(1, secondaryTrack);
                Assert.True(await Wait(() => player.Subtitles[1].IsBitmap && player.Subtitles[1].IsOpened, 30));
                await vm.OcrRecognitionCommand.ExecuteAsync("1");
                Assert.True(await Wait(() => controller.GetCues(1).Any(cue => cue.Text.Contains("ПРИВЕТ", StringComparison.OrdinalIgnoreCase)), 60),
                    "Secondary selected-bitmap OCR uses Russian stream metadata: " + string.Join(" | ", vm.Toasts.Select(t => t.Message)));
                Assert.Contains(controller.GetCues(1), cue => cue.Text.Contains("ИЗУЧАЕМ", StringComparison.OrdinalIgnoreCase));
            }
            string? output = Environment.GetEnvironmentVariable("LLPLAYER_RECOGNITION_ARTIFACTS");
            if (output != null)
            {
                File.WriteAllText(Path.Combine(output, "real-recognition-ui.txt"), "Real Player + MainWindowViewModel commands: large-v3 ASR produced country cue; second exact-attempt ASR paused then canceled; selected bitmap OCR produced HELLO and LEARNING cues; secondary Russian metadata OCR produced ПРИВЕТ and ИЗУЧАЕМ cues when explicitly enabled.\n" + controller.RecognitionStatus + "\nToasts: " + string.Join(" | ", vm.Toasts.Select(t => t.Title + ": " + t.Message)));
                window.CaptureRenderedFrame()!.Save(Path.Combine(output, "real-recognition-ui.png"), PngBitmapEncoderOptions.Default);
            }
        }
        finally
        {
            window.Close();
            vm.Dispose();
            await controller.DisposeAsync();
            player.Dispose();
        }
    }
    static async Task<bool> Wait(Func<bool> condition, int seconds)
    {
        Stopwatch watch = Stopwatch.StartNew();
        while (!condition()) { if (watch.Elapsed.TotalSeconds > seconds) return false; await Task.Delay(50); }
        return true;
    }
    sealed class Backend : IRecognitionBackend
    {
        public RecognitionSettings? Applied;
        public bool HasAudio => true;
        public bool IsLive => false;
        public bool IsBusy => false;
        public bool IsPaused => false;
        public void Start(int slot) { }
        public void Pause() { }
        public void Resume() { }
        public void Cancel(int slot) { }
        public void Ocr(int slot) { }
        public void Apply(RecognitionSettings settings) => Applied = settings;
    }
}
