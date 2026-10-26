using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LLPlayer.Avalonia.Services;
using LLPlayer.Avalonia.ViewModels;
using LLPlayer.Avalonia.Views;

namespace LLPlayer.Avalonia.Tests;

public class SubtitlePositionSettingsTests
{
    [AvaloniaTheory]
    [InlineData("{}")]
    [InlineData("{\"Volume\":42,\"Theme\":\"Light\",\"RecentFiles\":[\"old.mp4\"]}")]
    public void Preferences_without_position_restore_the_existing_24_DIP_default(string json)
    {
        var prefs = JsonSerializer.Deserialize<AppPrefs>(json, PrefsStore.JsonOptions)!.Normalize();
        using var main = new MainWindowViewModel(new FakePlaybackController(), prefs, _ => { }, new FakeTranslator(), new FakeShell());
        Assert.Equal(24, main.SubtitleBottomOffset);
        Assert.Equal(24, Position(prefs));
    }

    [AvaloniaTheory]
    [InlineData("-20", 0)]
    [InlineData("0", 0)]
    [InlineData("13.5", 13.5)]
    [InlineData("400", 400)]
    [InlineData("950", 400)]
    [InlineData("\"NaN\"", 24)]
    [InlineData("\"Infinity\"", 24)]
    [InlineData("\"-Infinity\"", 24)]
    public void Normalize_restores_a_finite_bounded_position_before_startup(string jsonValue, double expected)
    {
        var options = new JsonSerializerOptions(PrefsStore.JsonOptions) { NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals };
        var prefs = JsonSerializer.Deserialize<AppPrefs>("{\"SubtitleBottomOffset\":" + jsonValue + "}", options)!.Normalize();
        using var main = new MainWindowViewModel(new FakePlaybackController(), prefs, _ => { }, new FakeTranslator(), new FakeShell());
        Assert.Equal(expected, main.SubtitleBottomOffset);
        Assert.Equal(expected, Position(prefs));
    }

    [AvaloniaTheory]
    [InlineData("0", 0)]
    [InlineData("24", 24)]
    [InlineData("400", 400)]
    public void An_unchanged_settings_draft_can_save_including_a_loaded_zero_position(string jsonValue, double expected)
    {
        var prefs = ReadPosition(jsonValue);
        AppPrefs? saved = null;
        using var main = new MainWindowViewModel(new FakePlaybackController(), prefs,
            candidate => saved = JsonSerializer.Deserialize<AppPrefs>(JsonSerializer.Serialize(candidate, PrefsStore.JsonOptions), PrefsStore.JsonOptions),
            new FakeTranslator(), new FakeShell());
        var draft = main.CreateSettings();
        draft.SaveCommand.Execute(null);
        Assert.Null(draft.Error);
        Assert.NotNull(saved);
        Assert.Equal(expected, Position(saved));
        Assert.Equal(expected, main.SubtitleBottomOffset);
    }

    [AvaloniaFact]
    public void Movement_commands_keep_8_DIP_steps_and_clamps_in_the_preferences_saved_at_shutdown()
    {
        using TempDir temp = new();
        var store = new PrefsStore(Path.Combine(temp.Path, "preferences.json"));
        var prefs = new AppPrefs().Normalize();
        using var main = new MainWindowViewModel(new FakePlaybackController(), prefs, store.Save, new FakeTranslator(), new FakeShell());
        main.SubtitlesUpCommand.Execute(null);
        Assert.Equal(32, main.SubtitleBottomOffset);
        Assert.Equal(32, Position(prefs));
        main.SubtitlesDownCommand.Execute(null);
        Assert.Equal(24, main.SubtitleBottomOffset);
        Assert.Equal(24, Position(prefs));
        for (int i = 0; i < 60; i++) main.SubtitlesDownCommand.Execute(null);
        Assert.Equal(0, main.SubtitleBottomOffset); Assert.Equal(0, Position(prefs));
        for (int i = 0; i < 60; i++) main.SubtitlesUpCommand.Execute(null);
        Assert.Equal(400, main.SubtitleBottomOffset); Assert.Equal(400, Position(prefs));
        main.SubtitlesDownCommand.Execute(null);
        Assert.Equal(392, main.SubtitleBottomOffset); Assert.Equal(392, Position(prefs));
        main.SavePrefs();
        using var restarted = new MainWindowViewModel(new FakePlaybackController(), store.Load(), _ => { }, new FakeTranslator(), new FakeShell());
        Assert.Equal(392, restarted.SubtitleBottomOffset);
    }

    [AvaloniaTheory]
    [InlineData("")]
    [InlineData("en-US")]
    [InlineData("ru-RU")]
    public void Real_editor_saves_before_apply_with_its_captured_culture_and_updates_only_the_text_margin(string culture)
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
        try
        {
            using TempDir temp = new();
            var store = new PrefsStore(Path.Combine(temp.Path, "preferences.json"));
            var prefs = new AppPrefs().Normalize(); store.Save(prefs);
            var player = new FakePlaybackController { IsOpened = true, IsPlaying = true, PrimaryText = "Primary", SecondaryText = "Secondary" };
            var time = new ManualTimeProvider();
            var window = new MainWindow { Width = 1000, Height = 700 };
            MainWindowViewModel? live = null; int writes = 0;
            using var main = new MainWindowViewModel(player, prefs, candidate =>
            {
                Assert.Equal(24, live!.SubtitleBottomOffset); Assert.Equal(24, Position(prefs));
                Assert.Equal(24, window.FindControl<StackPanel>("SubtitleOverlay")!.Margin.Bottom);
                Assert.NotSame(prefs, candidate); Assert.NotSame(prefs.RecentFiles, candidate.RecentFiles);
                Assert.Equal(80.5, Position(candidate)); store.Save(candidate); writes++;
            }, new FakeTranslator(), window, time);
            live = main;
            window.Attach(main, null); window.Show(); Dispatcher.UIThread.RunJobs();
            var model = main.CreateSettings(); model.SelectedSection = 1;
            var settings = new SettingsWindow(model) { Height = 700 }; settings.Show(); Dispatcher.UIThread.RunJobs();
            try
            {
                var editor = PositionEditor(settings);
                Assert.Equal(24m, editor.Value);
                SetText(editor, 80.5m.ToString(CultureInfo.CurrentCulture));
                Assert.Equal(24, main.SubtitleBottomOffset);
                prefs.Volume = 77; prefs.Mute = true; prefs.LastFolder = "/new"; prefs.RecentFiles.Add("new.mp4");
                prefs.WordTranslationService = "Off"; prefs.TranslateTargetLanguage = "Russian";
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture == "ru-RU" ? "en-US" : "ru-RU");
                Click(settings, settings.FindControl<Button>("SaveButton")!);
                Assert.False(settings.IsVisible); Assert.Null(model.Error); Assert.Equal(1, writes);
                var saved = store.Load();
                Assert.Equal(80.5, Position(saved)); Assert.Equal(80.5, main.SubtitleBottomOffset);
                Assert.Equal(77, saved.Volume); Assert.True(saved.Mute); Assert.Equal("/new", saved.LastFolder);
                Assert.Equal(["new.mp4"], saved.RecentFiles); Assert.Equal("Off", saved.WordTranslationService);
                Assert.Equal("Russian", saved.TranslateTargetLanguage);
                var overlay = window.FindControl<StackPanel>("SubtitleOverlay")!;
                // The 48-DIP side margins now live inside the computed box width (overlay is centered and sized),
                // so only the vertical base distance remains in the margin; geometry at 100% is unchanged.
                Assert.Equal(new Thickness(0, 0, 0, 80.5), overlay.Margin);
                var videoHost = window.FindControl<Panel>("VideoHost")!;
                double expectedWidth = MainWindowViewModel.ComputeSubtitleBoxWidth(videoHost.Bounds.Width, 100, 0);
                Assert.InRange(overlay.Width, expectedWidth - 1, expectedWidth + 1);

                main.ToggleFullScreen(); Dispatcher.UIThread.RunJobs();
                var bar = window.FindControl<Border>("TransportBar")!;
                Assert.True(main.IsFullScreen); Assert.True(bar.Bounds.Height > 0);
                Assert.Equal(80.5 + bar.Bounds.Height, overlay.Margin.Bottom);
                Assert.Equal(80.5, Position(prefs)); Assert.Equal(80.5, Position(store.Load()));
                time.Advance(MainWindowViewModel.IdleTimeout + TimeSpan.FromSeconds(1)); main.UpdateIdle(); Dispatcher.UIThread.RunJobs();
                Assert.False(main.ControlsVisible); Assert.Equal(80.5, overlay.Margin.Bottom);
                main.NotifyActivity(); Dispatcher.UIThread.RunJobs();
                Assert.Equal(80.5 + bar.Bounds.Height, overlay.Margin.Bottom);
                main.ToggleFullScreen(); Dispatcher.UIThread.RunJobs();
                Assert.Equal(80.5, overlay.Margin.Bottom);
                using var restarted = new MainWindowViewModel(new FakePlaybackController(), store.Load(), _ => { }, new FakeTranslator(), new FakeShell());
                Assert.Equal(80.5, restarted.SubtitleBottomOffset);
            }
            finally { settings.Close(); window.ShutdownRequested = () => Task.CompletedTask; window.Close(); }
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [AvaloniaTheory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("-1")]
    [InlineData("401")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    [InlineData("1e9999")]
    public void Invalid_position_text_keeps_the_dialog_open_and_all_preferences_unchanged(string text)
    {
        using TempDir temp = new();
        var store = new PrefsStore(Path.Combine(temp.Path, "preferences.json"));
        var prefs = new AppPrefs().Normalize(); store.Save(prefs);
        string before = File.ReadAllText(store.Path);
        int writes = 0;
        using var main = new MainWindowViewModel(new FakePlaybackController(), prefs, _ => writes++, new FakeTranslator(), new FakeShell());
        var model = main.CreateSettings(); model.SelectedSection = 1;
        var window = new SettingsWindow(model) { Height = 700 }; window.Show(); Dispatcher.UIThread.RunJobs();
        try
        {
            SetText(PositionEditor(window), text);
            Click(window, window.FindControl<Button>("SaveButton")!);
            Assert.True(window.IsVisible); Assert.NotNull(model.Error); Assert.Equal(0, writes);
            Assert.False(string.IsNullOrWhiteSpace(window.FindControl<TextBlock>("SaveError")!.Text));
            Assert.Equal(before, File.ReadAllText(store.Path));
            Assert.Equal(24, main.SubtitleBottomOffset); Assert.Equal(24, Position(prefs));
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("0", 0)]
    [InlineData("400", 400)]
    public void Position_editor_accepts_both_range_boundaries(string text, double expected)
    {
        var (main, _, _, prefs, saved) = TestVm.Create(); using var owned = main;
        var model = main.CreateSettings(); model.SelectedSection = 1;
        var window = new SettingsWindow(model) { Height = 700 }; window.Show(); Dispatcher.UIThread.RunJobs();
        try
        {
            SetText(PositionEditor(window), text);
            Click(window, window.FindControl<Button>("SaveButton")!);
            Assert.False(window.IsVisible); Assert.Null(model.Error); Assert.Single(saved);
            Assert.Equal(expected, main.SubtitleBottomOffset); Assert.Equal(expected, Position(prefs));
            Assert.Equal(expected, Position(saved[0]));
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Close_and_Escape_discard_position_text_without_touching_live_or_saved_preferences(bool escape)
    {
        using TempDir temp = new();
        var store = new PrefsStore(Path.Combine(temp.Path, "preferences.json"));
        var prefs = new AppPrefs().Normalize(); store.Save(prefs);
        string before = File.ReadAllText(store.Path); int writes = 0;
        using var main = new MainWindowViewModel(new FakePlaybackController(), prefs, _ => writes++, new FakeTranslator(), new FakeShell());
        var model = main.CreateSettings(); model.SelectedSection = 1;
        var window = new SettingsWindow(model) { Height = 700 }; window.Show(); Dispatcher.UIThread.RunJobs();
        try
        {
            SetText(PositionEditor(window), "88");
            if (escape) window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            else Click(window, window.FindControl<Button>("CloseButton")!);
            Dispatcher.UIThread.RunJobs();
            Assert.False(window.IsVisible); Assert.Equal(0, writes); Assert.Equal(before, File.ReadAllText(store.Path));
            Assert.Equal(24, main.SubtitleBottomOffset); Assert.Equal(24, Position(prefs));
            var reopened = main.CreateSettings(); reopened.SelectedSection = 1;
            var second = new SettingsWindow(reopened) { Height = 700 }; second.Show(); Dispatcher.UIThread.RunJobs();
            try { Assert.Equal(24m, PositionEditor(second).Value); }
            finally { second.Close(); }
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Failed_position_write_preserves_all_live_state_and_keeps_the_draft_available_for_retry()
    {
        using TempDir temp = new();
        var store = new PrefsStore(Path.Combine(temp.Path, "preferences.json"));
        var prefs = new AppPrefs().Normalize(); store.Save(prefs);
        string beforeFile = File.ReadAllText(store.Path), beforePrefs = JsonSerializer.Serialize(prefs, PrefsStore.JsonOptions);
        var player = new FakePlaybackController(); var shell = new FakeShell(); bool fail = true; int writes = 0;
        using var main = new MainWindowViewModel(player, prefs, candidate =>
        {
            writes++;
            if (fail) throw new IOException("disk full");
            store.Save(candidate);
        }, new FakeTranslator(), shell);
        var model = main.CreateSettings(); model.SelectedSection = 1; model.Theme = "Light"; model.SubtitlesVisible = false;
        var window = new SettingsWindow(model) { Height = 700 }; window.Show(); Dispatcher.UIThread.RunJobs();
        try
        {
            SetText(PositionEditor(window), "96");
            Click(window, window.FindControl<Button>("SaveButton")!);
            Assert.True(window.IsVisible); Assert.Contains("disk full", model.Error!); Assert.Equal(1, writes);
            Assert.Equal(beforeFile, File.ReadAllText(store.Path)); Assert.Equal(beforePrefs, JsonSerializer.Serialize(prefs, PrefsStore.JsonOptions));
            Assert.Equal(24, main.SubtitleBottomOffset); Assert.True(main.IsDarkTheme); Assert.True(player.SubtitlesVisible); Assert.Empty(shell.Themes);
            Assert.Equal(96m, PositionEditor(window).Value);
            fail = false; Click(window, window.FindControl<Button>("SaveButton")!);
            Assert.False(window.IsVisible); Assert.Null(model.Error); Assert.Equal(2, writes);
            Assert.Equal(96, main.SubtitleBottomOffset); Assert.Equal(96, Position(store.Load()));
            Assert.False(main.IsDarkTheme); Assert.False(player.SubtitlesVisible); Assert.Equal(["Light"], shell.Themes);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Position_editor_can_be_scrolled_to_focused_and_typed_in_the_minimum_window_with_an_accessible_footer()
    {
        var (main, _, _, prefs, saved) = TestVm.Create(); using var owned = main;
        var model = main.CreateSettings(); model.SelectedSection = 1;
        var window = new SettingsWindow(model) { Width = 560, Height = 440 }; window.Show(); Dispatcher.UIThread.RunJobs();
        try
        {
            var editor = PositionEditor(window);
            var scroll = window.FindControl<ScrollViewer>("SettingsScroll")!;
            editor.BringIntoView(); Dispatcher.UIThread.RunJobs();
            Assert.True(scroll.Offset.Y > 0);
            Assert.True(editor.IsEffectivelyVisible); Assert.True(editor.Bounds.Width > 0 && editor.Bounds.Height > 0);
            Point position = editor.TranslatePoint(default, scroll)!.Value;
            Assert.True(position.Y >= -1 && position.Y + editor.Bounds.Height <= scroll.Bounds.Height + 1,
                $"PositionEditor: {position} / {editor.Bounds.Size} in {scroll.Bounds.Size}");

            var textBox = Assert.Single(editor.GetVisualDescendants().OfType<TextBox>());
            Assert.True(textBox.Focus(NavigationMethod.Tab)); textBox.SelectAll(); Dispatcher.UIThread.RunJobs();
            window.KeyTextInput("88"); Dispatcher.UIThread.RunJobs();
            Assert.True(textBox.IsFocused); Assert.Equal("88", editor.Text);
            Assert.Equal(24, main.SubtitleBottomOffset);
            foreach (string name in new[] { "CloseButton", "SaveButton" })
            {
                var button = window.FindControl<Button>(name)!;
                Assert.True(button.IsEffectivelyVisible); Assert.True(button.IsEnabled);
                Point point = button.TranslatePoint(default, window)!.Value;
                Assert.True(button.Bounds.Width > 0 && button.Bounds.Height > 0);
                Assert.True(point.X >= 0 && point.Y >= 0
                    && point.X + button.Bounds.Width <= window.Bounds.Width + 1
                    && point.Y + button.Bounds.Height <= window.Bounds.Height + 1, name);
            }
            Click(window, window.FindControl<Button>("SaveButton")!);
            Assert.False(window.IsVisible); Assert.Null(model.Error); Assert.Single(saved);
            Assert.Equal(88, main.SubtitleBottomOffset); Assert.Equal(88, Position(prefs));
        }
        finally { window.Close(); }
    }

    static AppPrefs ReadPosition(string jsonValue)
        => JsonSerializer.Deserialize<AppPrefs>("{\"SubtitleBottomOffset\":" + jsonValue + "}", PrefsStore.JsonOptions)!.Normalize();

    static double Position(AppPrefs prefs)
    {
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(prefs, PrefsStore.JsonOptions));
        Assert.True(json.RootElement.TryGetProperty("SubtitleBottomOffset", out var value), "Persisted preferences must contain SubtitleBottomOffset.");
        return value.GetDouble();
    }

    static NumericUpDown PositionEditor(SettingsWindow window)
        => Assert.IsType<NumericUpDown>(window.FindControl<Control>("PositionEditor"));

    static void SetText(NumericUpDown editor, string text)
    {
        var textBox = Assert.Single(editor.GetVisualDescendants().OfType<TextBox>());
        textBox.Text = text; Dispatcher.UIThread.RunJobs();
        Assert.Equal(text, editor.Text);
    }

    static void Click(Window window, Control control)
    {
        Point point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Dispatcher.UIThread.RunJobs();
    }
}
