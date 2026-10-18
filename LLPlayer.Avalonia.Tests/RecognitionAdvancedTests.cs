using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using FlyleafLib;
using LLPlayer.Avalonia.Services;
using LLPlayer.Avalonia.ViewModels;
using LLPlayer.Avalonia.Views;
using LLPlayer.Core.Services;
using Whisper.net.Ggml;
using Whisper.net.LibraryLoader;

namespace LLPlayer.Avalonia.Tests;

[Collection(FlyleafGlobalsCollection.Name)]
public class RecognitionAdvancedTests
{
    [AvaloniaFact]
    public void Advanced_settings_are_available_without_expanding_on_open()
    {
        using var f = new Fixture();
        Assert.False(f.Window.FindControl<Expander>("AdvancedWhisperCppExpander")!.IsExpanded);
    }

    [AvaloniaFact]
    public async Task Bound_advanced_values_save_reload_and_apply_exact_prompt()
    {
        using var f = new Fixture();
        f.Edit("ThreadsBox", "7"); f.Edit("MaxSegmentLengthBox", "120"); f.Edit("MaxTokensPerSegmentBox", "31");
        f.Check("SplitOnWordBox", true); f.Check("NoContextBox", false);
        const string prompt = "  Привет\r\nSecond line\n\t trailing  ";
        f.Edit("PromptBox", prompt);
        Assert.Equal(4, f.Live.WhisperCpp.Threads);
        Assert.Equal("original", f.Live.WhisperCpp.Prompt);
        await f.Save();
        Assert.False(f.Window.IsVisible);
        var saved = f.Store.Load()!;
        Assert.Equal(7, saved.WhisperCpp.Threads); Assert.Equal(120, saved.WhisperCpp.MaxSegmentLength);
        Assert.Equal(31, saved.WhisperCpp.MaxTokensPerSegment); Assert.True(saved.WhisperCpp.SplitOnWord);
        Assert.False(saved.WhisperCpp.NoContext); Assert.Equal(prompt, saved.WhisperCpp.Prompt);
        Assert.Equal(saved.WhisperCpp.Threads, f.Backend.Live.WhisperCppConfig.Threads);
        Assert.Equal(prompt, f.Backend.Live.WhisperCppConfig.Prompt);
        AssertPreserved(saved, f.Temp.Path);
    }

    [AvaloniaTheory]
    [InlineData("ThreadsBox")]
    [InlineData("MaxSegmentLengthBox")]
    [InlineData("MaxTokensPerSegmentBox")]
    public async Task Each_numeric_field_accepts_blank_and_positive_Int32_edges(string name)
    {
        using var f = new Fixture();
        f.Edit(name, "2147483647"); await f.Save();
        Assert.Equal(int.MaxValue, Value(f.Store.Load()!, name));
        f.Reopen(); f.Edit(name, "1"); await f.Save();
        Assert.Equal(1, Value(f.Store.Load()!, name));
        f.Reopen(); f.Edit(name, " \t "); await f.Save();
        Assert.Null(Value(f.Store.Load()!, name));
        Assert.Null(Value(RecognitionSettings.FromConfig(f.Backend.Live), name));
    }

    [AvaloniaTheory]
    [InlineData("malformed")]
    [InlineData("1.5")]
    [InlineData("2147483648")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("1e2")]
    public async Task Invalid_input_in_each_field_blocks_file_and_apply_and_keeps_text(string invalid)
    {
        foreach (string name in new[] { "ThreadsBox", "MaxSegmentLengthBox", "MaxTokensPerSegmentBox" })
        {
            using var f = new Fixture();
            string before = File.ReadAllText(f.Path);
            f.Edit("ThreadsBox", "8"); f.Edit(name, invalid);
            await f.Save();
            Assert.True(f.Window.IsVisible); Assert.False(f.Model.Busy);
            Assert.Equal(invalid, f.Window.FindControl<TextBox>(name)!.Text);
            Assert.Contains(name.Replace("Box", ""), f.Model.Message);
            Assert.Contains("2147483647", f.Model.Message);
            Assert.Equal(before, File.ReadAllText(f.Path)); Assert.Equal(0, f.Backend.ApplyCount);
            Assert.Equal(4, f.Model.Draft.WhisperCpp.Threads);
            AssertContained(f.Window.FindControl<TextBlock>("RecognitionMessage")!, f.Window);
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Close_and_Escape_discard_all_advanced_edits(bool escape)
    {
        using var f = new Fixture();
        string before = File.ReadAllText(f.Path);
        f.Edit("ThreadsBox", "8"); f.Edit("MaxSegmentLengthBox", "90"); f.Edit("MaxTokensPerSegmentBox", "11");
        f.Edit("PromptBox", "discard me"); f.Check("SplitOnWordBox", true); f.Check("NoContextBox", true);
        if (escape) f.Window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        else f.Window.FindControl<Button>("CloseButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.False(f.Window.IsVisible); Assert.Equal(before, File.ReadAllText(f.Path));
        Assert.Equal(0, f.Backend.ApplyCount); Assert.Equal("original", f.Live.WhisperCpp.Prompt);
    }

    [AvaloniaFact]
    public async Task Reset_changes_exactly_six_draft_fields_and_applies_only_on_Save()
    {
        using var f = new Fixture();
        f.Edit("ThreadsBox", "invalid"); f.Edit("PromptBox", "changed"); f.Check("SplitOnWordBox", true);
        string before = File.ReadAllText(f.Path);
        f.Window.FindControl<Button>("ResetAdvancedButton")!.Command!.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("", f.Window.FindControl<TextBox>("ThreadsBox")!.Text);
        Assert.Equal("", f.Window.FindControl<TextBox>("MaxSegmentLengthBox")!.Text);
        Assert.Equal("", f.Window.FindControl<TextBox>("MaxTokensPerSegmentBox")!.Text);
        Assert.False(f.Model.Draft.WhisperCpp.SplitOnWord); Assert.True(f.Model.Draft.WhisperCpp.NoContext);
        Assert.Equal("", f.Model.Draft.WhisperCpp.Prompt); AssertPreserved(f.Model.Draft, f.Temp.Path);
        Assert.False(f.Window.FindControl<CheckBox>("SplitOnWordBox")!.IsChecked);
        Assert.True(f.Window.FindControl<CheckBox>("NoContextBox")!.IsChecked);
        Assert.Equal("", f.Window.FindControl<TextBox>("PromptBox")!.Text);
        Assert.Equal(f.Temp.Path, f.Model.ModelDirectory); Assert.NotNull(f.Model.SelectedModel);
        Assert.Equal(before, File.ReadAllText(f.Path)); Assert.Equal(0, f.Backend.ApplyCount);
        await f.Save();
        var saved = f.Store.Load()!; Assert.Null(saved.WhisperCpp.Threads);
        Assert.Null(saved.WhisperCpp.MaxSegmentLength); Assert.Null(saved.WhisperCpp.MaxTokensPerSegment);
        Assert.False(saved.WhisperCpp.SplitOnWord); Assert.True(saved.WhisperCpp.NoContext); Assert.Equal("", saved.WhisperCpp.Prompt);
        AssertPreserved(saved, f.Temp.Path);
    }

    [AvaloniaFact]
    public async Task Legacy_nonpositive_values_display_empty_and_normalize_only_on_explicit_Save()
    {
        using var f = new Fixture();
        f.Live.WhisperCpp.Threads = 0; f.Live.WhisperCpp.MaxSegmentLength = -5; f.Live.WhisperCpp.MaxTokensPerSegment = -1;
        f.Store.Save(f.Live); string before = File.ReadAllText(f.Path); f.Reopen();
        foreach (string name in new[] { "ThreadsBox", "MaxSegmentLengthBox", "MaxTokensPerSegmentBox" })
            Assert.Equal("", f.Window.FindControl<TextBox>(name)!.Text);
        Assert.Equal(0, f.Model.Draft.WhisperCpp.Threads); Assert.Equal(-5, f.Model.Draft.WhisperCpp.MaxSegmentLength);
        Assert.Equal(before, File.ReadAllText(f.Path)); Assert.Equal(0, f.Backend.ApplyCount);
        f.Window.Close(); Assert.Equal(before, File.ReadAllText(f.Path)); f.Reopen(); await f.Save();
        Assert.Null(f.Store.Load()!.WhisperCpp.Threads); Assert.Null(f.Store.Load()!.WhisperCpp.MaxSegmentLength);
        Assert.Null(f.Store.Load()!.WhisperCpp.MaxTokensPerSegment); Assert.Equal(0, f.Live.WhisperCpp.Threads);
    }

    [AvaloniaFact]
    public async Task Failed_atomic_save_keeps_live_values_and_allows_retry()
    {
        using var f = new Fixture();
        f.Window.Close(); File.Delete(f.Path); Directory.CreateDirectory(f.Path); f.Reopen();
        f.Edit("ThreadsBox", "9"); f.Edit("PromptBox", "retry exact\r\n "); await f.Save();
        Assert.True(f.Window.IsVisible); Assert.Equal(0, f.Backend.ApplyCount); Assert.NotEmpty(f.Model.Message);
        Assert.Equal(4, f.Backend.Live.WhisperCppConfig.Threads);
        Directory.Delete(f.Path); await f.Save();
        Assert.False(f.Window.IsVisible); Assert.Equal(9, f.Store.Load()!.WhisperCpp.Threads);
        Assert.Equal("retry exact\r\n ", f.Backend.Live.WhisperCppConfig.Prompt);
    }

    [AvaloniaFact]
    public async Task Busy_and_canceled_actions_keep_file_and_live_unchanged()
    {
        using var f = new Fixture(); string before = File.ReadAllText(f.Path);
        f.Edit("ThreadsBox", "9"); f.Backend.Busy = true; await f.Save();
        Assert.True(f.Window.IsVisible); Assert.Contains("finish", f.Model.Message);
        Assert.Equal(before, File.ReadAllText(f.Path)); Assert.Equal(0, f.Backend.ApplyCount);
        f.Backend.Busy = false; f.Actions.Dispose(); await f.Save();
        Assert.True(f.Window.IsVisible); Assert.Contains("canceled", f.Model.Message);
        Assert.Equal(before, File.ReadAllText(f.Path)); Assert.Equal(0, f.Backend.ApplyCount);
    }

    [AvaloniaFact]
    public async Task Media_generation_cancels_queued_work_and_Save_waits_for_its_gate()
    {
        using var f = new Fixture();
        using ManualResetEventSlim entered = new(), release = new();
        string before = File.ReadAllText(f.Path);
        Task first = f.Actions.MutateAsync(() => { entered.Set(); release.Wait(); });
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Task stale = f.Actions.StartAsync(0);
        try
        {
            f.Actions.Invalidate(); f.Edit("ThreadsBox", "12"); await f.Save();
            Assert.True(f.Window.IsVisible); Assert.Contains("finish", f.Model.Message);
            Assert.Equal(before, File.ReadAllText(f.Path)); Assert.Equal(0, f.Backend.ApplyCount);
        }
        finally { release.Set(); await first; }
        await Assert.ThrowsAsync<RecognitionActionCanceledException>(() => stale);
        Assert.Equal(before, File.ReadAllText(f.Path)); Assert.Equal(0, f.Backend.ApplyCount);
        await f.Save(); Assert.Equal(12, f.Store.Load()!.WhisperCpp.Threads);
    }

    [AvaloniaFact]
    public async Task Controls_footer_and_validation_are_reachable_in_both_themes_and_sizes()
    {
        foreach (string theme in new[] { "Dark", "Light" })
        foreach (bool small in new[] { false, true })
        {
            ThemeService.Apply(theme);
            using var f = new Fixture();
            f.Window.Width = small ? 400 : 620; f.Window.Height = small ? 360 : 600;
            var scroll = f.Window.FindControl<ScrollViewer>("RecognitionScroll")!;
            var advanced = f.Window.FindControl<Expander>("AdvancedWhisperCppExpander")!;
            advanced.IsExpanded = false; scroll.Offset = default; Dispatcher.UIThread.RunJobs();
            advanced.BringIntoView(); Dispatcher.UIThread.RunJobs();
            AssertContained(advanced, f.Window);
            await Capture(f.Window, $"{theme}-{(small ? "minimum" : "normal")}-collapsed");
            advanced.IsExpanded = true; Dispatcher.UIThread.RunJobs();
            f.Window.FindControl<TextBox>("ThreadsBox")!.BringIntoView(); Dispatcher.UIThread.RunJobs();
            AssertContained(f.Window.FindControl<TextBox>("ThreadsBox")!, f.Window);
            await Capture(f.Window, $"{theme}-{(small ? "minimum" : "normal")}-expanded-numeric");
            foreach (string name in new[] { "MaxSegmentLengthBox", "MaxTokensPerSegmentBox", "SplitOnWordBox", "NoContextBox", "PromptBox", "ResetAdvancedButton" })
            {
                var control = f.Window.FindControl<Control>(name)!;
                control.BringIntoView(); Dispatcher.UIThread.RunJobs();
                AssertContained(control, f.Window);
                if (small) await Capture(f.Window, $"{theme}-minimum-expanded-{name}");
            }
            AssertContained(f.Window.FindControl<Button>("ResetAdvancedButton")!, f.Window);
            AssertContained(f.Window.FindControl<Button>("SaveButton")!, f.Window);
            await Capture(f.Window, $"{theme}-{(small ? "minimum" : "normal")}-expanded-reset");
            scroll.Offset = new Vector(0, scroll.Extent.Height); Dispatcher.UIThread.RunJobs();
            await Capture(f.Window, $"{theme}-{(small ? "minimum" : "normal")}-expanded-bottom");
            f.Edit("ThreadsBox", "0"); await f.Save(); Dispatcher.UIThread.RunJobs();
            AssertContained(f.Window.FindControl<TextBlock>("RecognitionMessage")!, f.Window);
            AssertContained(f.Window.FindControl<Button>("SaveButton")!, f.Window);
            await Capture(f.Window, $"{theme}-{(small ? "minimum" : "normal")}-invalid-save");
            f.Edit("ThreadsBox", "4");
        }
    }

    static int? Value(RecognitionSettings settings, string name) => name switch
    {
        "ThreadsBox" => settings.WhisperCpp.Threads,
        "MaxSegmentLengthBox" => settings.WhisperCpp.MaxSegmentLength,
        _ => settings.WhisperCpp.MaxTokensPerSegment
    };
    static void AssertPreserved(RecognitionSettings s, string directory)
    {
        Assert.Equal(directory, s.WhisperCpp.ModelDirectory); Assert.Equal(GgmlType.Base, s.WhisperCpp.Model!.Model);
        Assert.Equal("ru", s.Whisper.Language); Assert.False(s.Whisper.LanguageDetection); Assert.True(s.Whisper.Translate);
        Assert.Equal("ru", s.OcrPrimaryLanguage); Assert.Equal("en", s.OcrSecondaryLanguage); Assert.False(s.OcrSecondarySame);
        Assert.Equal(3, s.WhisperCpp.GpuDevice); Assert.Equal(0.35f, s.WhisperCpp.NoSpeechThreshold);
        Assert.Equal(0.2f, s.WhisperCpp.Temperature); Assert.Equal(0.3f, s.WhisperCpp.TemperatureInc);
        Assert.Equal(2.1f, s.WhisperCpp.EntropyThreshold); Assert.Equal(-0.8f, s.WhisperCpp.LogProbThreshold);
        Assert.Equal(512, s.WhisperCpp.AudioContextSize);
        Assert.Equal(new[] { RuntimeLibrary.CpuNoAvx, RuntimeLibrary.Cpu }, s.WhisperCpp.RuntimeLibraries);
    }
    static void AssertContained(Control control, Window window)
    {
        // Scrollable controls must fit the visible viewport, not merely the enclosing window.
        Control boundary = window;
        if (control.FindAncestorOfType<ScrollViewer>() is { } scroll)
            boundary = scroll.GetVisualDescendants().OfType<ScrollContentPresenter>().First();
        Point point = control.TranslatePoint(default, boundary)!.Value;
        Assert.True(control.IsEffectivelyVisible && control.Bounds.Width > 0 && control.Bounds.Height > 0, control.Name);
        Assert.True(point.X >= -1 && point.Y >= -1 && point.X + control.Bounds.Width <= boundary.Bounds.Width + 1 && point.Y + control.Bounds.Height <= boundary.Bounds.Height + 1,
            $"{control.Name}: {point} / {control.Bounds.Size} in {boundary.GetType().Name} {boundary.Bounds.Size}");
    }
    static async Task Capture(Window window, string name)
    {
        // Fluent enabled/disabled transitions are 100ms; wait beyond them before forcing a current Skia frame.
        await Task.Delay(160, TestContext.Current.CancellationToken);
        window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(2); Dispatcher.UIThread.RunJobs();
        string? dir = Environment.GetEnvironmentVariable("LLPLAYER_RECOGNITION_ADVANCED_ARTIFACTS");
        if (dir == null) return;
        Directory.CreateDirectory(dir);
        using var frame = window.CaptureRenderedFrame()!;
        Assert.Equal((int)window.Bounds.Width, frame.PixelSize.Width);
        Assert.Equal((int)window.Bounds.Height, frame.PixelSize.Height);
        AssertContained(window.FindControl<Button>("SaveButton")!, window);
        frame.Save(Path.Combine(dir, name.ToLowerInvariant() + ".png"), PngBitmapEncoderOptions.Default);
    }
    sealed class Fixture : IDisposable
    {
        public TempDir Temp { get; } = new();
        public string Path { get; }
        public RecognitionSettings Live { get; } = new();
        public Backend Backend { get; } = new();
        public RecognitionActions Actions { get; }
        public RecognitionSettingsStore Store { get; }
        public RecognitionViewModel Model { get; private set; } = null!;
        public RecognitionWindow Window { get; private set; } = null!;
        public Fixture()
        {
            Path = System.IO.Path.Combine(Temp.Path, "recognition.json");
            File.WriteAllText(System.IO.Path.Combine(Temp.Path, "ggml-base.bin"), "fixture weights not loaded");
            Live.WhisperCpp = new() { Threads = 4, MaxSegmentLength = 80, MaxTokensPerSegment = 20, Prompt = "original", NoContext = false,
                ModelDirectory = Temp.Path, Model = new() { Model = GgmlType.Base }, GpuDevice = 3, NoSpeechThreshold = .35f,
                Temperature = .2f, TemperatureInc = .3f, EntropyThreshold = 2.1f, LogProbThreshold = -.8f, AudioContextSize = 512,
                RuntimeLibraries = [RuntimeLibrary.CpuNoAvx, RuntimeLibrary.Cpu] };
            Live.Whisper.Language = "ru"; Live.Whisper.LanguageDetection = false; Live.Whisper.Translate = true;
            Live.OcrPrimaryLanguage = "ru"; Live.OcrSecondaryLanguage = "en"; Live.OcrSecondarySame = false;
            Live.Apply(Backend.Live); Actions = new(Backend); Store = new(Path); Store.Save(Live); Reopen();
        }
        public void Reopen()
        {
            Window?.Close(); Model = new(Store.Load() ?? Live, Actions, Store); Window = new(Model); Window.Show();
            Dispatcher.UIThread.RunJobs();
        }
        public void Edit(string name, string value) { Window.FindControl<TextBox>(name)!.Text = value; Dispatcher.UIThread.RunJobs(); }
        public void Check(string name, bool value) { Window.FindControl<CheckBox>(name)!.IsChecked = value; Dispatcher.UIThread.RunJobs(); }
        public Task Save() => ((IAsyncRelayCommand)Window.FindControl<Button>("SaveButton")!.Command!).ExecuteAsync(null);
        public void Dispose() { Window.Close(); Actions.Dispose(); Temp.Dispose(); }
    }
    sealed class Backend : IRecognitionBackend
    {
        public Config.SubtitlesConfig Live { get; } = new();
        public int ApplyCount;
        public bool Busy;
        public bool HasAudio => true; public bool IsLive => false; public bool IsBusy => Busy; public bool IsPaused => false;
        public void Start(int slot) { } public void Pause() { } public void Resume() { } public void Cancel(int slot) { } public void Ocr(int slot) { }
        public void Apply(RecognitionSettings settings) { settings.Apply(Live); ApplyCount++; }
    }
}
