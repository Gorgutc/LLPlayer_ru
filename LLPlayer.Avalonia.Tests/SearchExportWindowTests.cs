using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FlyleafLib.MediaPlayer;
using LLPlayer.Avalonia.Services;
using LLPlayer.Avalonia.ViewModels;
using LLPlayer.Avalonia.Views;

namespace LLPlayer.Avalonia.Tests;

/// <summary>Shared-window search bindings, key routing and subtitle-export dialog integration.</summary>
public class SearchExportWindowTests
{
    [AvaloniaFact]
    public void Search_option_buttons_change_the_actual_filtered_rows()
    {
        using var fixture = new SearchWindow();
        fixture.TypeFilter("cat");
        Assert.Equal(new[] { 0, 1, 2 }, fixture.MatchIndices());

        ClickControl(fixture.Window.FindControl<ToggleButton>("MatchCaseButton")!);
        Assert.True(fixture.Vm.Sidebar.MatchCase);
        Assert.Equal(new[] { 1, 2 }, fixture.MatchIndices());
        ClickControl(fixture.Window.FindControl<ToggleButton>("WholeWordButton")!);
        Assert.True(fixture.Vm.Sidebar.WholeWord);
        Assert.Equal(new[] { 1 }, fixture.MatchIndices());
        ClickControl(fixture.Window.FindControl<ToggleButton>("MatchCaseButton")!);
        Assert.False(fixture.Vm.Sidebar.MatchCase);
        Assert.Equal(new[] { 0, 1 }, fixture.MatchIndices());
        ClickControl(fixture.Window.FindControl<ToggleButton>("RegexButton")!);
        fixture.TypeFilter("^cat$");
        Assert.True(fixture.Vm.Sidebar.UseRegex);
        Assert.Equal(new[] { 0, 1 }, fixture.MatchIndices());
    }

    [AvaloniaFact]
    public void Enter_and_shift_enter_wrap_matches_keep_filter_focus_and_do_not_reach_playback_keys()
    {
        using var fixture = new SearchWindow();
        fixture.TypeFilter("cat");
        for (int i = 0; i < 4; i++)
        {
            PressKey(fixture.Window, Key.Enter);
            Assert.Same(fixture.Filter, fixture.Window.FocusManager!.GetFocusedElement());
        }
        PressKey(fixture.Window, Key.Enter, RawInputModifiers.Shift);
        Assert.Equal(new[] { 1d, 3d, 5d, 1d, 5d }, fixture.Player.Seeks.Select(seek => seek.TotalSeconds));
        Assert.Same(fixture.Filter, fixture.Window.FocusManager!.GetFocusedElement());
        Assert.Empty(fixture.Player.KeysDown);
        Assert.Empty(fixture.Player.KeysUp);
        Assert.Equal(0, fixture.Player.ToggleCalls);
    }

    [AvaloniaFact]
    public void Escape_clears_typed_filter_returns_focus_and_does_not_dispatch_playback_keys()
    {
        using var fixture = new SearchWindow();
        fixture.TypeFilter("cat");
        fixture.Player.KeysDown.Clear();
        fixture.Player.KeysUp.Clear();

        PressKey(fixture.Window, Key.Escape);

        Assert.Empty(fixture.Filter.Text!);
        Assert.Empty(fixture.Vm.Sidebar.FilterText);
        Assert.Equal(4, fixture.Vm.Sidebar.Cues.Count);
        Assert.NotSame(fixture.Filter, fixture.Window.FocusManager!.GetFocusedElement());
        Assert.Empty(fixture.Player.KeysDown);
        Assert.Empty(fixture.Player.KeysUp);
        Assert.Equal(0, fixture.Player.ToggleCalls);
    }

    [AvaloniaFact]
    public void Invalid_regex_error_is_visible_and_recovers_when_typing_a_valid_pattern()
    {
        using var fixture = new SearchWindow();
        ClickControl(fixture.Window.FindControl<ToggleButton>("RegexButton")!);
        fixture.TypeFilter("[");
        TextBlock error = fixture.Window.FindControl<TextBlock>("SearchErrorText")!;
        Assert.True(error.IsEffectivelyVisible);
        Assert.False(string.IsNullOrWhiteSpace(error.Text));
        Assert.Empty(fixture.Vm.Sidebar.Cues);
        PressKey(fixture.Window, Key.Enter);
        Assert.Empty(fixture.Player.Seeks);

        fixture.TypeFilter("dog");

        Assert.False(error.IsVisible);
        Assert.Empty(fixture.Vm.Sidebar.SearchError);
        Assert.Equal(new[] { 3 }, fixture.MatchIndices());
    }

    [AvaloniaFact]
    public void Export_menu_opens_owned_singleton_preserves_choices_and_reopens_a_new_session()
    {
        using var fixture = new SearchWindow();
        SubtitlesExportWindow first = fixture.OpenExportFromVideoMenu();
        SubtitlesExportViewModel model = Assert.IsType<SubtitlesExportViewModel>(first.DataContext);
        Assert.Same(fixture.Window, first.Owner);
        Assert.True(first.IsDialog);
        Assert.Same(model, fixture.Vm.SubtitlesExport);
        Assert.True(model.IsUtf8Bom);
        first.FindControl<ComboBox>("ExportSlot")!.SelectedIndex = 1;
        first.FindControl<ComboBox>("ExportText")!.SelectedIndex = 2;
        first.FindControl<ComboBox>("ExportFormat")!.SelectedIndex = 1;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, model.SelectedSlot);
        Assert.Equal(SubtitleExportText.TranslatedWithFallback, model.SelectedText);
        Assert.Equal(SubtitleExportFormat.Vtt, model.SelectedFormat);
        Assert.True(first.FindControl<TextBlock>("ExportPartialWarning")!.IsVisible);
        fixture.Vm.ShowSubtitlesExportCommand.Execute(null);
        Assert.Same(first, Assert.Single(fixture.Window.OwnedWindows));
        Assert.Same(model, first.DataContext);
        Assert.Equal(1, model.SelectedSlot);

        first.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Dispatcher.UIThread.RunJobs();
        Assert.False(first.IsVisible);
        Assert.Null(fixture.Vm.SubtitlesExport);
        SubtitlesExportWindow second = fixture.OpenExportFromVideoMenu();
        Assert.NotSame(first, second);
        SubtitlesExportViewModel replacement = Assert.IsType<SubtitlesExportViewModel>(second.DataContext);
        Assert.NotSame(model, replacement);
        Assert.Equal(0, replacement.SelectedSlot);
        Assert.Equal(SubtitleExportText.Original, replacement.SelectedText);
    }

    [AvaloniaFact]
    public async Task Export_window_close_waits_for_pending_picker_then_clears_owned_session()
    {
        using var fixture = new SearchWindow();
        SubtitlesExportWindow dialog = fixture.OpenExportFromVideoMenu();
        SubtitlesExportViewModel model = Assert.IsType<SubtitlesExportViewModel>(dialog.DataContext);
        TaskCompletionSource<string?> picker = new();
        int pickerCalls = 0;
        model.PickExportPath = _ => { pickerCalls++; return picker.Task; };
        ClickControl(dialog.FindControl<Button>("ExportButton")!);
        Assert.Equal(1, pickerCalls);
        Assert.True(model.Busy);
        Task export = Assert.IsAssignableFrom<Task>(model.ExportCommand.ExecutionTask);

        Button close = Assert.Single(dialog.GetVisualDescendants().OfType<Button>(), button => Equals(button.Content, "Close"));
        ClickControl(close);
        Assert.True(dialog.IsVisible);
        Assert.Same(model, fixture.Vm.SubtitlesExport);
        Assert.False(export.IsCompleted);
        picker.SetResult(null);
        await export;
        Dispatcher.UIThread.RunJobs();

        Assert.False(model.Busy);
        Assert.False(dialog.IsVisible);
        Assert.Null(fixture.Vm.SubtitlesExport);
        Assert.Empty(fixture.Window.OwnedWindows);
        Assert.Equal(1, pickerCalls);
    }

    [AvaloniaFact]
    public void Search_controls_remain_inside_the_minimum_width_sidebar_and_receive_clicks()
    {
        using var fixture = new SearchWindow(width: 640, sidebarWidth: AppPrefs.MinSidebarWidth);
        fixture.TypeFilter("cat");
        Border sidebar = fixture.Window.FindControl<Border>("Sidebar")!;
        Assert.Equal(AppPrefs.MinSidebarWidth, sidebar.Bounds.Width);
        string[] names = ["FilterBox", "MatchCaseButton", "WholeWordButton", "RegexButton", "PreviousMatchButton", "NextMatchButton"];
        foreach (string name in names)
        {
            Control control = fixture.Window.FindControl<Control>(name)!;
            Point origin = control.TranslatePoint(default, sidebar)!.Value;
            Assert.True(control.IsEffectivelyVisible && control.Bounds.Width > 0 && control.Bounds.Height > 0, name);
            Assert.True(origin.X >= 12 - 1 && origin.Y >= 0
                && origin.X + control.Bounds.Width <= sidebar.Bounds.Width - 12 + 1
                && origin.Y + control.Bounds.Height <= sidebar.Bounds.Height + 1,
                $"{name}: {origin} / {control.Bounds.Size} must fit inside sidebar {sidebar.Bounds.Size} and its 12-DIP content margins.");
        }
        foreach (string name in names.Skip(1))
            ClickControl(fixture.Window.FindControl<Control>(name)!);
        Assert.True(fixture.Vm.Sidebar.MatchCase);
        Assert.True(fixture.Vm.Sidebar.WholeWord);
        Assert.True(fixture.Vm.Sidebar.UseRegex);
        Assert.Equal(2, fixture.Player.Seeks.Count);
    }
    [AvaloniaTheory]
    [InlineData(AppPrefs.MinSidebarWidth)]
    [InlineData(AppPrefs.DefaultSidebarWidth)]
    public void Whole_word_label_fits_its_actual_text_slot_without_clipping(double sidebarWidth)
    {
        using var fixture = new SearchWindow(width: 1100, sidebarWidth: sidebarWidth);
        ToggleButton button = fixture.Window.FindControl<ToggleButton>("WholeWordButton")!;
        TextBlock label = Assert.Single(button.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Word");
        Assert.True(label.IsEffectivelyVisible);
        Assert.True(label.Bounds.Height > 0);
        FormattedText intrinsic = new(label.Text!, CultureInfo.CurrentUICulture, label.FlowDirection,
            new Typeface(label.FontFamily, label.FontStyle, label.FontWeight, label.FontStretch), label.FontSize, label.Foreground);
        Assert.True(intrinsic.WidthIncludingTrailingWhitespace > 0);
        Assert.True(label.Bounds.Width >= intrinsic.WidthIncludingTrailingWhitespace,
            $"Whole-word label '{label.Text}' needs {intrinsic.WidthIncludingTrailingWhitespace:F3} DIP at its actual {label.FontFamily}/{label.FontSize} font, "
            + $"but its rendered TextBlock receives only {label.Bounds.Width:F3} DIP in a {sidebarWidth}-DIP sidebar.");
    }
    static void PressKey(TopLevel window, Key key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        PhysicalKey physical = key switch
        {
            Key.A => PhysicalKey.A,
            Key.Enter => PhysicalKey.Enter,
            Key.Escape => PhysicalKey.Escape,
            _ => throw new ArgumentOutOfRangeException(nameof(key))
        };
        string? symbol = key == Key.A ? "a" : null;
        window.KeyPress(key, modifiers, physical, symbol);
        window.KeyRelease(key, modifiers, physical, symbol);
        Dispatcher.UIThread.RunJobs();
    }

    static void ClickControl(Control control)
    {
        TopLevel root = Assert.IsAssignableFrom<TopLevel>(TopLevel.GetTopLevel(control));
        Assert.True(control.IsEffectivelyVisible);
        Assert.True(control.Bounds.Width > 0 && control.Bounds.Height > 0);
        Point point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), root)!.Value;
        root.MouseMove(point);
        root.MouseDown(point, MouseButton.Left);
        root.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    static void Pump(MainWindow window)
    {
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame()?.Dispose();
        Dispatcher.UIThread.RunJobs();
    }

    sealed class SearchWindow : IDisposable
    {
        public MainWindow Window { get; }
        public MainWindowViewModel Vm { get; }
        public FakePlaybackController Player { get; }
        public TextBox Filter => Window.FindControl<TextBox>("FilterBox")!;

        public SearchWindow(double width = 1100, double sidebarWidth = AppPrefs.DefaultSidebarWidth)
        {
            Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
            Player = new FakePlaybackController { IsOpened = true, IsPlaying = true };
            string[] cues = ["Cat", "cat", "catalog", "dog"];
            for (int i = 0; i < cues.Length; i++)
                Player.Cues[0].Add(new CueInfo(i, TimeSpan.FromSeconds(i * 2 + 1), TimeSpan.FromSeconds(i * 2 + 2), cues[i]));
            Window = new MainWindow(new KeyMapper(), null) { Width = width, Height = 700 };
            Vm = new MainWindowViewModel(Player, new AppPrefs { SidebarVisible = true, SidebarWidth = sidebarWidth }.Normalize(), _ => { }, new FakeTranslator(), Window)
            { SubtitleExportSource = new ExportSource() };
            Window.Attach(Vm, renderer: null);
            Player.RaiseCuesChanged(0);
            Window.Show();
            Pump(Window);
            Assert.Equal(4, Vm.Sidebar.Cues.Count);
        }

        public int[] MatchIndices() => Vm.Sidebar.Cues.Select(cue => cue.Index).ToArray();

        public void TypeFilter(string text)
        {
            ClickControl(Filter);
            PressKey(Window, Key.A, RawInputModifiers.Control);
            Window.KeyTextInput(text);
            Pump(Window);
            Assert.Equal(text, Filter.Text);
            Assert.Equal(text, Vm.Sidebar.FilterText);
        }

        public SubtitlesExportWindow OpenExportFromVideoMenu()
        {
            Point point = Window.VideoSurface.TranslatePoint(new Point(30, 30), Window)!.Value;
            Window.MouseMove(point);
            Window.MouseDown(point, MouseButton.Right);
            Window.MouseUp(point, MouseButton.Right);
            Dispatcher.UIThread.RunJobs();
            ContextMenu menu = Assert.IsType<ContextMenu>(Window.FindControl<Panel>("VideoHost")!.ContextMenu);
            Assert.True(menu.IsOpen);
            MenuItem subtitles = Assert.Single(menu.Items.OfType<MenuItem>(), item => (item.Header?.ToString() ?? "").Replace("_", "", StringComparison.Ordinal) == "Subtitles");
            ClickControl(subtitles);
            MenuItem export = Assert.Single(subtitles.Items.OfType<MenuItem>(), item => ReferenceEquals(item.Command, Vm.ShowSubtitlesExportCommand));
            ClickControl(export);
            return Assert.IsType<SubtitlesExportWindow>(Assert.Single(Window.OwnedWindows));
        }

        public void Dispose()
        {
            foreach (Window owned in Window.OwnedWindows.ToArray())
                owned.Close();
            if (Window.IsVisible)
                Window.Close();
            Vm.Dispose();
            Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        }
    }

    sealed class ExportSource : ISubtitleExportSource
    {
        public SubtitleExportSnapshot Capture(int slot, SubtitleExportText text)
            => new([new SubtitleExportLine(TimeSpan.Zero, TimeSpan.FromSeconds(1), slot == 0 ? "Primary fixture" : "Secondary fixture")], "fixture", null, slot == 1);
    }
}