using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FlyleafLib.MediaPlayer;
using LLPlayer.Avalonia.Controls;
using LLPlayer.Avalonia.Services;
using LLPlayer.Avalonia.ViewModels;
using LLPlayer.Avalonia.Views;

namespace LLPlayer.Avalonia.Tests;

public class HotkeyRowsWindowTests
{
    [AvaloniaTheory]
    [InlineData("Dark")]
    [InlineData("Light")]
    public void Minimum_settings_editor_keeps_action_rows_keyboard_reachable_and_footer_visible(string theme)
    {
        Application.Current!.RequestedThemeVariant = theme == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
        int commits = 0;
        SettingsViewModel draft = new(new AppPrefs().Normalize(), _ => commits++, new(1, 1, 100));
        SettingsWindow window = new(draft) { Width = 560, Height = 440 };
        window.Show();
        try
        {
            window.FindControl<ListBox>("SettingsNavigation")!.SelectedIndex = 2;
            Present(window);
            ScrollViewer scroll = window.FindControl<ScrollViewer>("SettingsScroll")!;
            Assert.True(scroll.Extent.Height > scroll.Viewport.Height);

            int appCount = draft.ShortcutRows.Count;
            Click(window, window.FindControl<Button>("AddAppKeyButton")!);
            Assert.Equal(appCount + 1, draft.ShortcutRows.Count);
            ShortcutEditRow app = draft.ShortcutRows[^1];
            ComboBox appSelector = RowControl<ComboBox>(window, app);
            Reveal(window, appSelector);
            Assert.True(appSelector.Focus(NavigationMethod.Tab));
            appSelector.SelectedItem = ShortcutActionCatalog.AppActions.First(row => row.Name == AppShortcutMap.OpenWindowSettings);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(AppShortcutMap.OpenWindowSettings, app.ActionName);
            Capture(window, RowControl<ChordCaptureButton>(window, app), Key.F8, PhysicalKey.F8);
            Assert.Equal("F8", app.Chord);
            Click(window, RowCheck(window, app, "On release"));
            Assert.True(app.IsKeyUp);
            Click(window, RowCheck(window, app, "Enabled"));
            Assert.False(app.IsEnabled);
            Click(window, RowCheck(window, app, "Enabled"));
            Assert.True(app.IsEnabled);
            Click(window, RowButton(window, app, "Clone"));
            Assert.Equal(appCount + 2, draft.ShortcutRows.Count);
            ShortcutEditRow appClone = draft.ShortcutRows[^1];
            Assert.NotSame(app, appClone);
            Capture(window, RowControl<ChordCaptureButton>(window, appClone), Key.F7, PhysicalKey.F7);
            Assert.Equal("F8", app.Chord);
            Assert.Equal("F7", appClone.Chord);
            Click(window, RowButton(window, appClone, "Delete"));
            Assert.Equal(appCount + 1, draft.ShortcutRows.Count);

            int engineCount = draft.EngineKeyRows.Count;
            Click(window, window.FindControl<Button>("AddEngineKeyButton")!);
            Assert.Equal(engineCount + 1, draft.EngineKeyRows.Count);
            EngineKeyEditRow engine = draft.EngineKeyRows[^1];
            ComboBox engineSelector = RowControl<ComboBox>(window, engine);
            Reveal(window, engineSelector);
            Assert.True(engineSelector.Focus(NavigationMethod.Tab));
            PressKey(window, Key.F4, PhysicalKey.F4);
            Assert.True(engineSelector.IsDropDownOpen);
            // The four unbound A-B actions end the portable catalog: select its first action using real keys.
            PressKey(window, Key.End);
            PressKey(window, Key.Up);
            PressKey(window, Key.Up);
            PressKey(window, Key.Up);
            PressKey(window, Key.Enter, PhysicalKey.Enter);
            Assert.False(engineSelector.IsDropDownOpen);
            Assert.Equal(KeyBindingAction.ABLoopSetStart, engine.Action);
            ChordCaptureButton engineCapture = RowControl<ChordCaptureButton>(window, engine);
            Capture(window, engineCapture, Key.F6, PhysicalKey.F6);
            Assert.Equal("F6", engine.Chord);
            Click(window, engineCapture);
            Assert.True(engineCapture.IsCapturing);
            PressKey(window, Key.Escape, PhysicalKey.Escape);
            Assert.False(engineCapture.IsCapturing);
            Assert.True(window.IsVisible, "Escape cancels chord capture while the Settings draft stays open");
            Assert.Equal("F6", engine.Chord);
            Click(window, RowCheck(window, engine, "On release"));
            Assert.True(engine.IsKeyUp);
            Click(window, RowCheck(window, engine, "Enabled"));
            Assert.False(engine.IsEnabled);
            Click(window, RowCheck(window, engine, "Enabled"));
            Assert.True(engine.IsEnabled);
            Click(window, RowButton(window, engine, "Clone"));
            Assert.Equal(engineCount + 2, draft.EngineKeyRows.Count);
            EngineKeyEditRow engineClone = draft.EngineKeyRows[^1];
            Assert.NotSame(engine, engineClone);
            Capture(window, RowControl<ChordCaptureButton>(window, engineClone), Key.F7, PhysicalKey.F7);
            Assert.Equal("F6", engine.Chord);
            Click(window, RowButton(window, engineClone, "Delete"));
            Assert.Equal(engineCount + 1, draft.EngineKeyRows.Count);

            Reveal(window, window.FindControl<Button>("ResetAppKeysButton")!);
            Assert.True(window.FindControl<Button>("ResetAppKeysButton")!.Focus(NavigationMethod.Tab));
            Reveal(window, window.FindControl<Button>("ResetEngineKeysButton")!);
            Assert.True(window.FindControl<Button>("ResetEngineKeysButton")!.Focus(NavigationMethod.Tab));
            Button save = window.FindControl<Button>("SaveButton")!;
            Assert.True(save.Focus(NavigationMethod.Tab));
            AssertInsideWindow(window, save);
            AssertInsideWindow(window, window.FindControl<Button>("CloseButton")!);
            Point scrollBottom = scroll.TranslatePoint(new(0, scroll.Bounds.Height), window)!.Value;
            Point saveTop = save.TranslatePoint(default, window)!.Value;
            Assert.True(saveTop.Y >= scrollBottom.Y, "Save remains outside the scroll viewport");
            Click(window, save);
            Assert.Null(draft.Error);
            Assert.Equal(1, commits);
            Assert.Contains(draft.EngineKeyBindingPrefs, row => row.Action == "ABLoopSetStart" && row.Key == "F6" && row.IsKeyUp);
            Assert.Contains(draft.AppKeyBindingPrefs!, row => row.ActionName == "OpenWindowSettings" && row.Key == "F8" && row.IsKeyUp);
        }
        finally
        {
            window.Close();
            Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        }
    }

    [AvaloniaFact]
    public void Open_filtered_sheet_follows_committed_explicit_rows_but_ignores_draft_reset_and_failed_save()
    {
        FakePlaybackController player = new();
        bool fail = false;
        MainWindow window = new() { Width = 1000, Height = 600 };
        using MainWindowViewModel main = new(player, new AppPrefs().Normalize(),
            _ => { if (fail) throw new IOException("synthetic preferences write failure"); }, new FakeTranslator(), window);
        window.Attach(main, null, () => main.Shortcuts.BuildCheatSheet(player.EngineBindingsForCheatSheet));
        window.Show();
        try
        {
            main.ShowCheatSheet();
            Present(window);
            CheatSheetWindow sheet = Assert.IsType<CheatSheetWindow>(Assert.Single(window.OwnedWindows));
            TextBox filter = sheet.FindControl<TextBox>("SearchBox")!;
            filter.Text = "F8";
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(SheetRows(sheet));

            SettingsViewModel draft = main.CreateSettings();
            draft.ShortcutRows.First(row => row.ActionName == AppShortcutMap.ToggleSidebar).IsEnabled = false;
            draft.AddAppKeyCommand.Execute(null);
            ShortcutEditRow added = draft.ShortcutRows[^1];
            added.ActionName = AppShortcutMap.OpenWindowSettings;
            added.Chord = "F8";
            added.IsKeyUp = true;
            draft.EngineKeyRows.First(row => row.Action == KeyBindingAction.ToggleMute).Chord = "F6";
            draft.SaveCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.Null(draft.Error);
            Assert.Same(sheet, Assert.Single(window.OwnedWindows));
            Assert.Equal("F8", filter.Text);
            Assert.Equal("F8", SheetModel(sheet).FilterText);
            Assert.Equal("OpenWindowSettings", Assert.Single(SheetRows(sheet)).ActionName);
            Assert.Equal(new[] { "F8" }, Chords(Assert.Single(SheetRows(sheet))));
            filter.Text = "ToggleSidebar";
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(SheetRows(sheet));
            filter.Text = "ToggleMute";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(new[] { "F6" }, Chords(Assert.Single(SheetRows(sheet))));
            filter.Text = "F8";
            Dispatcher.UIThread.RunJobs();

            CheatSheetViewModel committedSheet = SheetModel(sheet);
            SettingsViewModel resetDraft = main.CreateSettings();
            resetDraft.ResetAppKeysCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.Same(committedSheet, SheetModel(sheet));
            Assert.Equal("OpenWindowSettings", Assert.Single(SheetRows(sheet)).ActionName);

            SettingsViewModel retry = main.CreateSettings();
            retry.ShortcutRows.First(row => row.ActionName == AppShortcutMap.OpenWindowSettings).Chord = "F7";
            fail = true;
            retry.SaveCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.NotNull(retry.Error);
            Assert.Same(committedSheet, SheetModel(sheet));
            Assert.Equal("F8", filter.Text);
            Assert.Equal(new[] { "F8" }, Chords(Assert.Single(SheetRows(sheet))));

            fail = false;
            retry.SaveCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.Null(retry.Error);
            Assert.Same(sheet, Assert.Single(window.OwnedWindows));
            Assert.Equal("F8", filter.Text);
            Assert.Empty(SheetRows(sheet));
            filter.Text = "F7";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("OpenWindowSettings", Assert.Single(SheetRows(sheet)).ActionName);

            SettingsViewModel reset = main.CreateSettings();
            reset.ResetAppKeysCommand.Execute(null);
            reset.SaveCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.Null(reset.Error);
            Assert.Null(main.Prefs.AppKeyBindings);
            Assert.Same(sheet, Assert.Single(window.OwnedWindows));
            Assert.Equal("F7", filter.Text);
            Assert.Empty(SheetRows(sheet));
            filter.Text = "OpenWindowCheatSheet";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(new[] { "F1", "Shift+/" }, Chords(Assert.Single(SheetRows(sheet))));
        }
        finally { fail = false; window.Close(); }
    }

    static T RowControl<T>(Window window, object row) where T : Control
        => Assert.Single(window.GetVisualDescendants().OfType<T>(), control => ReferenceEquals(control.DataContext, row));

    static Button RowButton(Window window, object row, string content)
        => Assert.Single(window.GetVisualDescendants().OfType<Button>(),
            button => ReferenceEquals(button.DataContext, row) && Equals(button.Content, content));

    static CheckBox RowCheck(Window window, object row, string content)
        => Assert.Single(window.GetVisualDescendants().OfType<CheckBox>(),
            check => ReferenceEquals(check.DataContext, row) && Equals(check.Content, content));

    static void Capture(Window window, ChordCaptureButton button, Key key, PhysicalKey physical)
    {
        List<string> events = [];
        button.Click += (_, _) => events.Add($"Click(capturing={button.IsCapturing})");
        button.GotFocus += (_, _) => events.Add($"GotFocus(capturing={button.IsCapturing})");
        button.LostFocus += (_, _) => events.Add($"LostFocus(capturing={button.IsCapturing})");
        Click(window, button);
        Assert.True(button.IsCapturing, Describe(window, button) + "; events=" + string.Join(", ", events));
        window.KeyPress(key, RawInputModifiers.None, physical, null);
        window.KeyRelease(key, RawInputModifiers.None, physical, null);
        Dispatcher.UIThread.RunJobs();
        Assert.False(button.IsCapturing);
    }

    static void PressKey(Window window, Key key, PhysicalKey physical = PhysicalKey.None)
    {
        window.KeyPress(key, RawInputModifiers.None, physical, null);
        window.KeyRelease(key, RawInputModifiers.None, physical, null);
        Dispatcher.UIThread.RunJobs();
    }

    static void Click(Window window, Control control)
    {
        Reveal(window, control);
        Point point = control.TranslatePoint(new(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        Visual? hit = window.InputHitTest(point) as Visual;
        Assert.True(ReferenceEquals(hit, control) || hit?.GetVisualAncestors().Any(ancestor => ReferenceEquals(ancestor, control)) == true,
            "The actual pointer must hit its intended control. " + Describe(window, control));
        window.MouseMove(point);
        window.MouseDown(point, MouseButton.Left, RawInputModifiers.LeftMouseButton);
        window.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
    }

    static void Reveal(Window window, Control control)
    {
        control.BringIntoView();
        Present(window);
        AssertInsideWindow(window, control);
    }

    static void Present(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame()?.Dispose();
        Dispatcher.UIThread.RunJobs();
    }

    static void AssertInsideWindow(Window window, Control control)
    {
        Assert.True(control.IsEffectivelyVisible);
        Assert.True(control.Bounds.Width > 0 && control.Bounds.Height > 0);
        Point origin = control.TranslatePoint(default, window)!.Value;
        Assert.True(origin.X >= -1 && origin.Y >= -1
            && origin.X + control.Bounds.Width <= window.Bounds.Width + 1
            && origin.Y + control.Bounds.Height <= window.Bounds.Height + 1,
            $"{control.Name ?? control.GetType().Name}: {origin}/{control.Bounds.Size} outside {window.Bounds.Size}");
    }

    static CheatSheetViewModel SheetModel(CheatSheetWindow sheet) => Assert.IsType<CheatSheetViewModel>(sheet.DataContext);
    static ShortcutRowViewModel[] SheetRows(CheatSheetWindow sheet) => [.. SheetModel(sheet).Groups.SelectMany(group => group.Rows)];
    static string[] Chords(ShortcutRowViewModel row) => [.. row.Chords.Select(chord => string.Join("+", chord.Keys))];

    static string Describe(Window window, Control control)
    {
        Point point = control.TranslatePoint(new(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        Visual? hit = window.InputHitTest(point) as Visual;
        string path = hit == null ? "<none>" : string.Join(" > ", new[] { hit }.Concat(hit.GetVisualAncestors())
            .Take(8).Select(visual => visual is Control item ? $"{item.GetType().Name}#{item.Name}" : visual.GetType().Name));
        ScrollViewer scroll = window.FindControl<ScrollViewer>("SettingsScroll")!;
        return $"target={control.GetType().Name}#{control.Name}; point={point}; targetBounds={control.Bounds}; "
            + $"scrollOrigin={control.TranslatePoint(default, scroll)}; scrollBounds={scroll.Bounds}; viewport={scroll.Viewport}; offset={scroll.Offset}; "
            + $"focused={window.FocusManager?.GetFocusedElement()?.GetType().Name}; "
            + $"openCombos={window.GetVisualDescendants().OfType<ComboBox>().Count(combo => combo.IsDropDownOpen)}; hit={path}";
    }
}
