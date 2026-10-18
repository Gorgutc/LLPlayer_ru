using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using System.Globalization;
using LLPlayer.Avalonia.Services;
using LLPlayer.Avalonia.ViewModels;
using LLPlayer.Avalonia.Views;

namespace LLPlayer.Avalonia.Tests;

public class SettingsNumericInputRegressionTests
{
    [AvaloniaTheory]
    [InlineData("SidebarWidthEditor", 0, "")]
    [InlineData("SidebarWidthEditor", 0, "abc")]
    [InlineData("SidebarWidthEditor", 0, "901")]
    [InlineData("FontSizeEditor", 1, "")]
    [InlineData("FontSizeEditor", 1, "abc")]
    [InlineData("FontSizeEditor", 1, "11")]
    [InlineData("ScaleEditor", 1, "")]
    [InlineData("ScaleEditor", 1, "abc")]
    [InlineData("ScaleEditor", 1, "0")]
    public void Invalid_numeric_text_keeps_dialog_open_and_preferences_unchanged(string name, int section, string text)
    {
        using TempDir temp = new();
        var store = new PrefsStore(Path.Combine(temp.Path, "preferences.json"));
        var prefs = new AppPrefs(); store.Save(prefs);
        string before = File.ReadAllText(store.Path);
        using var main = new MainWindowViewModel(new FakePlaybackController(), prefs, store.Save, new FakeTranslator(), new FakeShell());
        var model = main.CreateSettings(); model.SelectedSection = section;
        var window = new SettingsWindow(model); window.Show(); Dispatcher.UIThread.RunJobs();
        try
        {
            SetText(window, name, text);
            model.SaveCommand.Execute(null); Dispatcher.UIThread.RunJobs();

            Assert.True(window.IsVisible);
            Assert.False(string.IsNullOrWhiteSpace(window.FindControl<TextBlock>("SaveError")!.Text));
            Assert.Equal(before, File.ReadAllText(store.Path));
            Assert.Equal(34, main.SubtitleFontSize); Assert.Equal(360, prefs.SidebarWidth);
            Assert.Equal(0.8, prefs.SecondaryFontScale);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("en-US")]
    [InlineData("ru-RU")]
    public void Corrected_numeric_text_saves_using_editor_culture(string culture)
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
        try
        {
            using TempDir temp = new();
            var store = new PrefsStore(Path.Combine(temp.Path, "preferences.json"));
            var prefs = new AppPrefs(); store.Save(prefs);
            using var main = new MainWindowViewModel(new FakePlaybackController(), prefs, store.Save, new FakeTranslator(), new FakeShell());
            var model = main.CreateSettings();
            var window = new SettingsWindow(model); window.Show(); Dispatcher.UIThread.RunJobs();
            try
            {
                SetText(window, "SidebarWidthEditor", "abc");
                model.SaveCommand.Execute(null); Dispatcher.UIThread.RunJobs();
                Assert.True(window.IsVisible); Assert.NotNull(model.Error);
                SetText(window, "SidebarWidthEditor", "440");
                model.SelectedSection = 1; Dispatcher.UIThread.RunJobs();
                SetText(window, "FontSizeEditor", "42");
                SetText(window, "ScaleEditor", culture == "ru-RU" ? "1,25" : "1.25");
                model.SaveCommand.Execute(null); Dispatcher.UIThread.RunJobs();

                Assert.False(window.IsVisible); Assert.Null(model.Error);
                var saved = store.Load();
                Assert.Equal(440, saved.SidebarWidth); Assert.Equal(42, saved.SubtitleFontSize);
                Assert.Equal(1.25, saved.SecondaryFontScale); Assert.Equal(52.5, main.SecondaryFontSize);
            }
            finally { window.Close(); }
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Unsaved_numeric_text_is_discarded_by_Close_or_Escape(bool escape)
    {
        using TempDir temp = new();
        var store = new PrefsStore(Path.Combine(temp.Path, "preferences.json"));
        var prefs = new AppPrefs(); store.Save(prefs);
        string before = File.ReadAllText(store.Path);
        using var main = new MainWindowViewModel(new FakePlaybackController(), prefs, store.Save, new FakeTranslator(), new FakeShell());
        var model = main.CreateSettings();
        var window = new SettingsWindow(model); window.Show(); Dispatcher.UIThread.RunJobs();
        try
        {
            SetText(window, "SidebarWidthEditor", "440");
            model.SelectedSection = 1; Dispatcher.UIThread.RunJobs();
            SetText(window, "FontSizeEditor", "42");
            SetText(window, "ScaleEditor", "abc");
            if (escape) window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            else model.CloseCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.False(window.IsVisible); Assert.Equal(before, File.ReadAllText(store.Path));
            var reopened = main.CreateSettings();
            Assert.Equal(34, reopened.SubtitleFontSize); Assert.Equal(360, reopened.SidebarWidth);
            Assert.Equal(0.8, reopened.SecondaryFontScale);
        }
        finally { window.Close(); }
    }

    static void SetText(SettingsWindow window, string name, string text)
    {
        var editor = window.FindControl<NumericUpDown>(name)!;
        var textBox = Assert.Single(editor.GetVisualDescendants().OfType<TextBox>());
        textBox.Text = text; Dispatcher.UIThread.RunJobs();
        Assert.Equal(text, editor.Text);
    }
}
