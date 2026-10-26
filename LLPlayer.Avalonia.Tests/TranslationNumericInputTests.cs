using System.Globalization;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LLPlayer.Avalonia.ViewModels;
using LLPlayer.Avalonia.Views;
using LLPlayer.Core.Services;

namespace LLPlayer.Avalonia.Tests;

public class TranslationNumericInputTests
{
    [AvaloniaTheory]
    [InlineData("TranslateCountBackwardBox", "", "0 to 1000")]
    [InlineData("TranslateCountBackwardBox", "abc", "0 to 1000")]
    [InlineData("TranslateCountBackwardBox", "1.5", "0 to 1000")]
    [InlineData("TranslateCountBackwardBox", "-1", "0 to 1000")]
    [InlineData("TranslateCountBackwardBox", "1001", "0 to 1000")]
    [InlineData("TranslateCountBackwardBox", "2147483648", "0 to 1000")]
    [InlineData("TranslateCountForwardBox", "", "0 to 1000")]
    [InlineData("TranslateCountForwardBox", "abc", "0 to 1000")]
    [InlineData("TranslateCountForwardBox", "1.5", "0 to 1000")]
    [InlineData("TranslateCountForwardBox", "-1", "0 to 1000")]
    [InlineData("TranslateCountForwardBox", "1001", "0 to 1000")]
    [InlineData("TranslateCountForwardBox", "2147483648", "0 to 1000")]
    [InlineData("TranslateMaxConcurrencyBox", "", "1 to 64")]
    [InlineData("TranslateMaxConcurrencyBox", "abc", "1 to 64")]
    [InlineData("TranslateMaxConcurrencyBox", "1.5", "1 to 64")]
    [InlineData("TranslateMaxConcurrencyBox", "0", "1 to 64")]
    [InlineData("TranslateMaxConcurrencyBox", "65", "1 to 64")]
    [InlineData("TranslateMaxConcurrencyBox", "2147483648", "1 to 64")]
    public async Task Invalid_input_survives_save_focus_and_blocks_persistence(string name, string text, string range)
    {
        using var session = new EditSession();
        var editor = EnterText(session.Window, name, text);
        BlurToSave(session.Window);

        Assert.Equal(text, editor.Text);
        await session.Model.SaveCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();

        Assert.True(session.Window.IsVisible);
        Assert.Contains(range, session.Model.Message);
        Assert.Equal(text, editor.Text);
        session.AssertUnchanged();
    }

    [AvaloniaTheory]
    [InlineData("en-US", "1.5")]
    [InlineData("ru-RU", "1,5")]
    [InlineData("en-US", "1,000")]
    [InlineData("en-US", "1e1")]
    [InlineData("en-US", " ")]
    public async Task Invalid_input_is_preserved_across_repeated_blur_and_editor_cultures(string culture, string text)
    {
        using var session = new EditSession(culture);
        foreach (string name in new[] { "TranslateCountBackwardBox", "TranslateCountForwardBox", "TranslateMaxConcurrencyBox" })
            Assert.Equal(CultureInfo.GetCultureInfo(culture).NumberFormat.NumberDecimalSeparator,
                Editor(session.Window, name).NumberFormat?.NumberDecimalSeparator);
        var editor = EnterText(session.Window, "TranslateCountForwardBox", text);
        var other = Editor(session.Window, "TranslateCountBackwardBox");
        Assert.True(Input(other).Focus(NavigationMethod.Tab));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(text, editor.Text);
        Assert.True(Input(editor).Focus(NavigationMethod.Tab));
        Dispatcher.UIThread.RunJobs();
        BlurToSave(session.Window);
        Assert.Equal(text, editor.Text);

        await session.Model.SaveCommand.ExecuteAsync(null);
        Assert.NotEmpty(session.Model.Message);
        session.AssertUnchanged();
    }

    [AvaloniaTheory]
    [InlineData("TranslateCountBackwardBox", "0", 0)]
    [InlineData("TranslateCountBackwardBox", "1000", 1000)]
    [InlineData("TranslateCountForwardBox", "0", 0)]
    [InlineData("TranslateCountForwardBox", "1000", 1000)]
    [InlineData("TranslateMaxConcurrencyBox", "1", 1)]
    [InlineData("TranslateMaxConcurrencyBox", "64", 64)]
    public async Task Valid_integer_bounds_save_exactly(string name, string text, int expected)
    {
        using var session = new EditSession();
        var editor = EnterText(session.Window, name, text);
        BlurToSave(session.Window);
        Assert.Equal(text, editor.Text);
        await session.Model.SaveCommand.ExecuteAsync(null);

        Assert.False(session.Window.IsVisible);
        Assert.Equal(1, session.SaveCalls);
        Assert.Equal(expected, Count(session.Live, name));
        Assert.Equal(expected, Count(session.Store.Load("Off", null), name));
    }

    [AvaloniaTheory]
    [InlineData("TranslateCountBackwardBox")]
    [InlineData("TranslateCountForwardBox")]
    [InlineData("TranslateMaxConcurrencyBox")]
    public async Task Spinner_changes_valid_integer_input_and_saves_the_displayed_value(string name)
    {
        using var session = new EditSession();
        var editor = EnterText(session.Window, name, "3");
        BlurToSave(session.Window);
        var increase = editor.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "PART_IncreaseButton");
        var decrease = editor.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "PART_DecreaseButton");
        Assert.True(increase.IsEnabled);
        increase.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("4", editor.Text);
        decrease.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("3", editor.Text);
        await session.Model.SaveCommand.ExecuteAsync(null);

        Assert.False(session.Window.IsVisible);
        Assert.Equal(3, Count(session.Live, name));
    }

    [AvaloniaTheory]
    [InlineData("TranslateCountBackwardBox", "1.5")]
    [InlineData("TranslateCountForwardBox", "1.5")]
    [InlineData("TranslateMaxConcurrencyBox", "1.5")]
    [InlineData("TranslateMaxConcurrencyBox", "65")]
    public async Task Spinner_cannot_silently_repair_invalid_input(string name, string text)
    {
        using var session = new EditSession();
        var editor = EnterText(session.Window, name, text);
        foreach (var button in editor.GetVisualDescendants().OfType<Button>())
        {
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(text, editor.Text);
        }
        BlurToSave(session.Window);
        await session.Model.SaveCommand.ExecuteAsync(null);

        Assert.True(session.Window.IsVisible);
        Assert.Equal(text, editor.Text);
        session.AssertUnchanged();
    }

    [AvaloniaTheory]
    [InlineData("TranslateCountBackwardBox", "0", "PART_DecreaseButton")]
    [InlineData("TranslateCountBackwardBox", "1000", "PART_IncreaseButton")]
    [InlineData("TranslateCountForwardBox", "0", "PART_DecreaseButton")]
    [InlineData("TranslateCountForwardBox", "1000", "PART_IncreaseButton")]
    [InlineData("TranslateMaxConcurrencyBox", "1", "PART_DecreaseButton")]
    [InlineData("TranslateMaxConcurrencyBox", "64", "PART_IncreaseButton")]
    public void Spinner_disables_the_direction_outside_the_integer_bounds(string name, string text, string buttonName)
    {
        using var session = new EditSession();
        var editor = EnterText(session.Window, name, text);
        BlurToSave(session.Window);
        var button = editor.GetVisualDescendants().OfType<Button>().Single(button => button.Name == buttonName);

        Assert.False(button.IsEnabled);
        Assert.Equal(text, editor.Text);
        session.AssertUnchanged();
    }

    [AvaloniaFact]
    public void Replacing_the_draft_does_not_restore_a_previous_invalid_input()
    {
        using var session = new EditSession();
        var editor = EnterText(session.Window, "TranslateCountForwardBox", "1.5");
        BlurToSave(session.Window);
        var replacement = new TranslationViewModel(new() { WordProvider = "Off", TranslateCountForward = 33 }, _ => Task.CompletedTask);
        session.Window.DataContext = replacement;
        Dispatcher.UIThread.RunJobs();
        Assert.True(Input(editor).Focus());
        Dispatcher.UIThread.RunJobs();
        BlurToSave(session.Window);

        Assert.Equal("33", editor.Text);
        Assert.Equal("33", replacement.TranslateCountForward);
        session.AssertUnchanged();
    }

    [AvaloniaFact]
    public async Task Correcting_invalid_input_allows_retry_without_applying_the_failed_attempt()
    {
        using var session = new EditSession();
        EnterText(session.Window, "TranslateCountForwardBox", "1.5");
        BlurToSave(session.Window);
        await session.Model.SaveCommand.ExecuteAsync(null);
        Assert.True(session.Window.IsVisible);
        Assert.NotEmpty(session.Model.Message);
        session.AssertUnchanged();

        EnterText(session.Window, "TranslateCountForwardBox", "17");
        BlurToSave(session.Window);
        await session.Model.SaveCommand.ExecuteAsync(null);
        Assert.False(session.Window.IsVisible);
        Assert.Equal(1, session.SaveCalls);
        Assert.Equal(17, session.Live.TranslateCountForward);
        Assert.Equal(17, session.Store.Load("Off", null).TranslateCountForward);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Close_and_escape_discard_numeric_edits(bool escape)
    {
        using var session = new EditSession();
        EnterText(session.Window, "TranslateCountBackwardBox", "23");
        EnterText(session.Window, "TranslateMaxConcurrencyBox", "65");
        if (escape) session.Window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        else session.Window.Close();
        Dispatcher.UIThread.RunJobs();

        Assert.False(session.Window.IsVisible);
        session.AssertUnchanged();
        var reopened = new TranslationViewModel(session.Live, _ => Task.CompletedTask);
        Assert.Equal("1", reopened.TranslateCountBackward);
        Assert.Equal("2", reopened.TranslateMaxConcurrency);
    }

    [AvaloniaFact]
    public async Task Write_failure_keeps_live_and_file_unchanged_and_can_be_retried()
    {
        using var session = new EditSession { FailWrite = true };
        EnterText(session.Window, "TranslateCountBackwardBox", "23");
        EnterText(session.Window, "TranslateCountForwardBox", "24");
        EnterText(session.Window, "TranslateMaxConcurrencyBox", "6");
        BlurToSave(session.Window);
        await session.Model.SaveCommand.ExecuteAsync(null);

        Assert.True(session.Window.IsVisible);
        Assert.Contains("could not be saved", session.Model.Message);
        Assert.Equal("23", Editor(session.Window, "TranslateCountBackwardBox").Text);
        Assert.Equal("24", Editor(session.Window, "TranslateCountForwardBox").Text);
        Assert.Equal("6", Editor(session.Window, "TranslateMaxConcurrencyBox").Text);
        session.AssertUnchanged(expectedSaveCalls: 1);

        session.FailWrite = false;
        await session.Model.SaveCommand.ExecuteAsync(null);
        Assert.False(session.Window.IsVisible);
        Assert.Equal(23, session.Live.TranslateCountBackward);
        Assert.Equal(24, session.Live.TranslateCountForward);
        Assert.Equal(6, session.Live.TranslateMaxConcurrency);
    }

    static NumericUpDown Editor(TranslationWindow window, string name) => window.FindControl<NumericUpDown>(name)!;
    static TextBox Input(NumericUpDown editor) => Assert.Single(editor.GetVisualDescendants().OfType<TextBox>());
    static NumericUpDown EnterText(TranslationWindow window, string name, string text)
    {
        var editor = Editor(window, name);
        var input = Input(editor);
        Assert.True(input.Focus());
        Dispatcher.UIThread.RunJobs();
        Assert.True(input.IsFocused);
        input.Text = text;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(text, editor.Text);
        return editor;
    }
    static void BlurToSave(TranslationWindow window)
    {
        Assert.True(window.FindControl<Button>("SaveButton")!.Focus());
        Dispatcher.UIThread.RunJobs();
    }
    static int Count(TranslationSettings settings, string name) => name switch
    {
        "TranslateCountBackwardBox" => settings.TranslateCountBackward,
        "TranslateCountForwardBox" => settings.TranslateCountForward,
        "TranslateMaxConcurrencyBox" => settings.TranslateMaxConcurrency,
        _ => throw new ArgumentOutOfRangeException(nameof(name))
    };

    sealed class EditSession : IDisposable
    {
        readonly TempDir directory = new();
        readonly CultureInfo previousCulture = CultureInfo.CurrentCulture;
        readonly string path;
        readonly string initialFile;
        public TranslationSettings Live { get; private set; } = new() { WordProvider = "Off" };
        public TranslationSettingsStore Store { get; }
        public TranslationViewModel Model { get; }
        public TranslationWindow Window { get; }
        public int SaveCalls { get; private set; }
        public bool FailWrite { get; set; }

        public EditSession(string culture = "")
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            try
            {
                path = Path.Combine(directory.Path, "LLPlayer.Translation.json");
                Store = new(path);
                Store.Save(Live);
                initialFile = File.ReadAllText(path);
                Model = new(Live, draft =>
                {
                    SaveCalls++;
                    // A directory as the destination gives a deterministic filesystem write failure.
                    new TranslationSettingsStore(FailWrite ? directory.Path : path).Save(draft);
                    Live = draft;
                    return Task.CompletedTask;
                });
                Window = new(Model);
                Window.Show();
                foreach (string name in new[] { "TranslateCountBackwardBox", "TranslateCountForwardBox", "TranslateMaxConcurrencyBox" })
                    Editor(Window, name).NumberFormat = CultureInfo.GetCultureInfo(culture).NumberFormat;
                Dispatcher.UIThread.RunJobs();
            }
            catch
            {
                try { Window?.Close(); }
                finally
                {
                    try { directory.Dispose(); }
                    finally { CultureInfo.CurrentCulture = previousCulture; }
                }
                throw;
            }
        }

        public void AssertUnchanged(int expectedSaveCalls = 0)
        {
            Assert.Equal(expectedSaveCalls, SaveCalls);
            Assert.Equal(initialFile, File.ReadAllText(path));
            Assert.Equal(1, Live.TranslateCountBackward);
            Assert.Equal(12, Live.TranslateCountForward);
            Assert.Equal(2, Live.TranslateMaxConcurrency);
        }
        public void Dispose()
        {
            try { Window.Close(); }
            finally
            {
                try { directory.Dispose(); }
                finally { CultureInfo.CurrentCulture = previousCulture; }
            }
        }
    }
}
