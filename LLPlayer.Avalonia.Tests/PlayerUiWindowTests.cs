using System.Globalization;
using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
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

public class PlayerUiWindowTests
{
    static readonly string[] NumericNames = ["ActivityTimeoutMsEditor", "SeekBarFadeInTimeMsEditor", "SeekBarFadeOutTimeMsEditor", "SeekBarHeightEditor", "SeekBarThumbHeightEditor", "SeekBarTrackHeightEditor"];
    static readonly string[] BooleanNames = ["SeekBarShowOnlyMouseOverEditor", "AlwaysOnTopEditor", "MouseSingleClickToPlayEditor", "MouseDoubleClickToFullScreenEditor", "MouseWheelToVolumeUpDownEditor"];

    [AvaloniaTheory]
    [InlineData("en-US", "30.5", "18.5", "6.5")]
    [InlineData("ru-RU", "30,5", "18,5", "6,5")]
    public void Real_editors_save_all_eleven_fields_using_the_drafts_captured_numeric_culture(string culture, string height, string thumb, string track)
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
        try
        {
            var (main, _, _, prefs, saved) = TestVm.Create(); using var owned = main;
            var draft = main.CreateSettings(); var window = new SettingsWindow(draft); window.Show(); ShowPlayer(window);
            try
            {
                SetText(window, NumericNames[0], "4200"); SetText(window, NumericNames[1], "75"); SetText(window, NumericNames[2], "350");
                SetText(window, NumericNames[3], height); SetText(window, NumericNames[4], thumb); SetText(window, NumericNames[5], track);
                foreach (string name in BooleanNames)
                {
                    var check = window.FindControl<CheckBox>(name)!;
                    check.IsChecked = name is "SeekBarShowOnlyMouseOverEditor" or "AlwaysOnTopEditor";
                }
                Assert.Equal(2500, main.ActivityTimeoutMs); Assert.False(main.AlwaysOnTop); Assert.Equal(20, prefs.SeekBarHeight);
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture == "ru-RU" ? "en-US" : "ru-RU");
                Click(window, window.FindControl<Button>("SaveButton")!);
                Assert.False(window.IsVisible, draft.Error); Assert.Null(draft.Error); Assert.Single(saved);
                Assert.Equal(4200, prefs.ActivityTimeoutMs); Assert.Equal(75, prefs.SeekBarFadeInTimeMs); Assert.Equal(350, prefs.SeekBarFadeOutTimeMs);
                Assert.Equal(30.5, prefs.SeekBarHeight); Assert.Equal(18.5, prefs.SeekBarThumbHeight); Assert.Equal(6.5, prefs.SeekBarTrackHeight);
                Assert.True(prefs.SeekBarShowOnlyMouseOver); Assert.True(prefs.AlwaysOnTop);
                Assert.False(prefs.MouseSingleClickToPlay); Assert.False(prefs.MouseDoubleClickToFullScreen); Assert.False(prefs.MouseWheelToVolumeUpDown);
                Assert.Equal(30.5, main.SeekBarHeight); Assert.True(main.AlwaysOnTop);
            }
            finally { window.Close(); }
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [AvaloniaTheory]
    [InlineData("ActivityTimeoutMsEditor", "")]
    [InlineData("ActivityTimeoutMsEditor", "1.5")]
    [InlineData("SeekBarFadeInTimeMsEditor", "word")]
    [InlineData("SeekBarFadeOutTimeMsEditor", "-1")]
    [InlineData("SeekBarHeightEditor", "NaN")]
    [InlineData("SeekBarThumbHeightEditor", "Infinity")]
    [InlineData("SeekBarTrackHeightEditor", "1e999")]
    public void Real_invalid_raw_text_remains_visible_and_prevents_any_save(string name, string text)
    {
        var (main, _, _, prefs, saved) = TestVm.Create(); using var owned = main;
        string before = JsonSerializer.Serialize(prefs, PrefsStore.JsonOptions);
        var draft = main.CreateSettings(); var window = new SettingsWindow(draft); window.Show(); ShowPlayer(window);
        try
        {
            SetText(window, name, text);
            window.FindControl<CheckBox>("AlwaysOnTopEditor")!.IsChecked = true;
            Click(window, window.FindControl<Button>("SaveButton")!);
            Assert.True(window.IsVisible); Assert.NotNull(draft.Error); Assert.Empty(saved);
            Assert.Equal(text, EditorText(window.FindControl<Control>(name)!).Text ?? "");
            Assert.Equal(before, JsonSerializer.Serialize(prefs, PrefsStore.JsonOptions)); Assert.False(main.AlwaysOnTop);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Close_button_and_Escape_discard_real_player_editors(bool escape)
    {
        var (main, _, _, prefs, saved) = TestVm.Create(); using var owned = main;
        string before = JsonSerializer.Serialize(prefs, PrefsStore.JsonOptions);
        var draft = main.CreateSettings(); var window = new SettingsWindow(draft); window.Show(); ShowPlayer(window);
        try
        {
            SetText(window, NumericNames[0], "4200"); SetText(window, NumericNames[1], "75"); SetText(window, NumericNames[2], "350");
            SetText(window, NumericNames[3], "30"); SetText(window, NumericNames[4], "18"); SetText(window, NumericNames[5], "6");
            foreach (string name in BooleanNames)
            {
                var check = window.FindControl<CheckBox>(name)!; check.IsChecked = !check.IsChecked;
            }
            if (escape) { window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null); Dispatcher.UIThread.RunJobs(); }
            else Click(window, window.FindControl<Button>("CloseButton")!);
            Assert.False(window.IsVisible); Assert.Empty(saved); Assert.Equal(before, JsonSerializer.Serialize(prefs, PrefsStore.JsonOptions));
            Assert.Equal(2500, main.ActivityTimeoutMs); Assert.False(main.AlwaysOnTop);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void At_minimum_window_size_every_player_editor_is_named_keyboard_reachable_and_scrollable()
    {
        var (main, _, _, _, _) = TestVm.Create(); using var owned = main;
        var window = new SettingsWindow(main.CreateSettings()) { Width = 560, Height = 440 }; window.Show(); ShowPlayer(window);
        try
        {
            var scroll = window.FindControl<ScrollViewer>("SettingsScroll")!;
            foreach (string name in NumericNames.Concat(BooleanNames))
            {
                var editor = window.FindControl<Control>(name)!; editor.BringIntoView(); Pump(window);
                Assert.True(editor.IsEffectivelyVisible, name); Assert.True(editor.IsEnabled, name);
                Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(editor)), name);
                Assert.True(editor.Bounds.Width > 0 && editor.Bounds.Height > 0, name);
                Point position = editor.TranslatePoint(default, scroll)!.Value;
                Assert.True(position.X >= -1 && position.X + editor.Bounds.Width <= scroll.Bounds.Width + 1, name);
                Assert.True(position.Y >= -1 && position.Y + editor.Bounds.Height <= scroll.Bounds.Height + 1, name);
                if (editor is CheckBox checkbox)
                {
                    foreach (var label in checkbox.GetVisualDescendants().OfType<TextBlock>().Where(label => !string.IsNullOrEmpty(label.Text)))
                    {
                        Point labelPosition = label.TranslatePoint(default, scroll)!.Value;
                        Assert.True(labelPosition.X + label.Bounds.Width <= scroll.Bounds.Width + 1, $"{name}: label '{label.Text}' is clipped horizontally.");
                    }
                }
                Control focusTarget = editor is TextBox or NumericUpDown ? EditorText(editor) : editor;
                Assert.True(focusTarget.Focus(NavigationMethod.Tab), name);
            }
            foreach (string name in new[] { "SaveButton", "CloseButton" })
            {
                var button = window.FindControl<Button>(name)!; Point p = button.TranslatePoint(default, window)!.Value;
                Assert.True(button.IsEffectivelyVisible && button.IsEnabled);
                Assert.True(p.X >= 0 && p.Y >= 0 && p.X + button.Bounds.Width <= window.Bounds.Width + 1 && p.Y + button.Bounds.Height <= window.Bounds.Height + 1, name);
            }
        }
        finally { window.Close(); }
    }

    static void ShowPlayer(SettingsWindow window)
    {
        var nav = window.FindControl<ListBox>("SettingsNavigation")!;
        nav.SelectedItem = Assert.Single(nav.Items.OfType<ListBoxItem>(), item => item.Content?.ToString() == "Player"); Pump(window);
        Assert.True(window.FindControl<StackPanel>("PlayerPage")!.IsVisible);
    }
    static void SetText(SettingsWindow window, string name, string text)
    {
        var editor = window.FindControl<Control>(name)!; editor.BringIntoView(); Pump(window);
        var textBox = EditorText(editor);
        Assert.True(textBox.Focus(NavigationMethod.Tab)); textBox.Text = text; Dispatcher.UIThread.RunJobs();
        Assert.Equal(text, textBox.Text);
    }
    static TextBox EditorText(Control editor) => editor as TextBox ?? Assert.Single(editor.GetVisualDescendants().OfType<TextBox>());
    static void Click(Window window, Control control)
    {
        Point point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Dispatcher.UIThread.RunJobs();
    }
    static void Pump(Window window) { Dispatcher.UIThread.RunJobs(); window.CaptureRenderedFrame()?.Dispose(); Dispatcher.UIThread.RunJobs(); }
}
