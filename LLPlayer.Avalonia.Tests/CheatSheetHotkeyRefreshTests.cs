using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AwesomeAssertions;
using FlyleafLib.MediaPlayer;
using LLPlayer.Avalonia.Services;
using LLPlayer.Avalonia.ViewModels;
using LLPlayer.Avalonia.Views;
using WpfKey = System.Windows.Input.Key;

namespace LLPlayer.Avalonia.Tests;

public class CheatSheetHotkeyRefreshTests
{
    [AvaloniaFact]
    public void Settings_save_refreshes_the_open_sheet_without_losing_its_window_or_filter()
    {
        var (main, model) = Build();
        using var lifetime = model;
        try
        {
            model.ShowCheatSheet();
            Dispatcher.UIThread.RunJobs();
            CheatSheetWindow sheet = GetSheet(main);
            TextBox search = sheet.FindControl<TextBox>("SearchBox")!;
            search.Text = "Ctrl+B";
            Dispatcher.UIThread.RunJobs();
            GetModel(sheet).HitText.Should().Be("1 of 11");
            VisibleKeyCaps(sheet).Should().Equal("Ctrl", "B");

            SettingsViewModel draft = model.CreateSettings();
            draft.ShortcutRows.Single(r => r.ActionName == AppShortcutMap.ToggleSidebar).Chord = "Ctrl+Shift+B";
            draft.SaveCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();

            draft.Error.Should().BeNull();
            GetSheet(main).Should().BeSameAs(sheet);
            sheet.IsVisible.Should().BeTrue();
            search.Text.Should().Be("Ctrl+B");
            GetModel(sheet).FilterText.Should().Be("Ctrl+B");
            GetModel(sheet).HitText.Should().Be("0 of 11", "the old chord no longer matches after Save");
            GetModel(sheet).Groups.Should().BeEmpty();
            VisibleKeyCaps(sheet).Should().BeEmpty();

            search.Text = "Ctrl+Shift+B";
            Dispatcher.UIThread.RunJobs();
            GetModel(sheet).HitText.Should().Be("1 of 11");
            GetModel(sheet).TotalCount.Should().Be(11);
            GetModel(sheet).Groups.SelectMany(g => g.Rows).Single().ActionName.Should().Be(AppShortcutMap.ToggleSidebar);
            VisibleKeyCaps(sheet).Should().Equal("Ctrl", "Shift", "B");

            model.ShowCheatSheet();
            GetSheet(main).Should().BeSameAs(sheet, "opening the viewer again activates the existing window");
        }
        finally
        {
            main.Close();
        }
    }

    [AvaloniaFact]
    public void Reset_refreshes_both_default_cheat_sheet_chords_in_the_open_filtered_window()
    {
        AppPrefs prefs = new AppPrefs().Normalize();
        prefs.AppShortcutOverrides[AppShortcutMap.OpenWindowCheatSheet] = "Ctrl+Shift+F1";
        var (main, model) = Build(prefs);
        using var lifetime = model;
        try
        {
            model.ShowCheatSheet();
            Dispatcher.UIThread.RunJobs();
            CheatSheetWindow sheet = GetSheet(main);
            TextBox search = sheet.FindControl<TextBox>("SearchBox")!;
            search.Text = "OpenWindowCheatSheet";
            Dispatcher.UIThread.RunJobs();
            VisibleKeyCaps(sheet).Should().Equal("Ctrl", "Shift", "F1");

            SettingsViewModel draft = model.CreateSettings();
            draft.ShortcutRows.Single(r => r.ActionName == AppShortcutMap.OpenWindowCheatSheet).ResetCommand.Execute(null);
            draft.SaveCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();

            draft.Error.Should().BeNull();
            GetSheet(main).Should().BeSameAs(sheet);
            sheet.IsVisible.Should().BeTrue();
            search.Text.Should().Be("OpenWindowCheatSheet");
            CheatSheetViewModel current = GetModel(sheet);
            current.FilterText.Should().Be("OpenWindowCheatSheet");
            current.TotalCount.Should().Be(11, "multiple chords still form one shortcut row");
            current.HitText.Should().Be("1 of 11");
            ShortcutRowViewModel row = current.Groups.SelectMany(g => g.Rows).Single();
            row.Chords.Select(c => string.Join("+", c.Keys)).Should().Equal("F1", "Shift+/");
            VisibleKeyCaps(sheet).Should().Equal("F1", "Shift", "/");
        }
        finally
        {
            main.Close();
        }
    }

    [AvaloniaFact]
    public void Closing_then_saving_and_reopening_reads_the_current_app_chords_and_keeps_engine_rows()
    {
        var (main, model) = Build();
        using var lifetime = model;
        try
        {
            model.ShowCheatSheet();
            Dispatcher.UIThread.RunJobs();
            CheatSheetWindow original = GetSheet(main);
            original.Close();
            Dispatcher.UIThread.RunJobs();

            SettingsViewModel draft = model.CreateSettings();
            draft.ShortcutRows.Single(r => r.ActionName == AppShortcutMap.ToggleSidebar).Chord = "Ctrl+Shift+B";
            draft.SaveCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            main.OwnedWindows.Should().BeEmpty("changing shortcuts does not open a closed viewer");

            model.ShowCheatSheet();
            Dispatcher.UIThread.RunJobs();
            CheatSheetWindow reopened = GetSheet(main);
            reopened.Should().NotBeSameAs(original);
            CheatSheetViewModel current = GetModel(reopened);
            current.FilterText.Should().BeEmpty();
            current.TotalCount.Should().Be(11);
            current.HitText.Should().Be("11 shortcuts");
            ShortcutRowViewModel[] rows = [.. current.Groups.SelectMany(g => g.Rows)];
            rows.Single(r => r.ActionName == AppShortcutMap.ToggleSidebar).Chords.Select(c => string.Join("+", c.Keys))
                .Should().Equal("Ctrl+Shift+B");
            rows.Single(r => r.ActionName == AppShortcutMap.OpenWindowCheatSheet).Chords.Select(c => string.Join("+", c.Keys))
                .Should().Equal("F1", "Shift+/");
            rows.Single(r => r.ActionName == nameof(KeyBindingAction.TogglePlayPause)).Chords.Select(c => string.Join("+", c.Keys))
                .Should().Equal("Space");

            reopened.FindControl<TextBox>("SearchBox")!.Text = "Space";
            Dispatcher.UIThread.RunJobs();
            current.HitText.Should().Be("1 of 11");
            VisibleKeyCaps(reopened).Should().Equal("Space");
        }
        finally
        {
            main.Close();
        }
    }

    static (MainWindow Window, MainWindowViewModel Model) Build(AppPrefs? prefs = null)
    {
        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        MainWindow main = new() { Width = 1000, Height = 600 };
        MainWindowViewModel model = new(new FakePlaybackController(), prefs ?? new AppPrefs().Normalize(),
            _ => { }, new FakeTranslator(), main);
        KeyBinding[] engineBindings = [new() { Key = WpfKey.Space, Action = KeyBindingAction.TogglePlayPause }];
        main.Attach(model, null, () => model.Shortcuts.BuildCheatSheet(engineBindings));
        main.Show();
        Dispatcher.UIThread.RunJobs();
        return (main, model);
    }

    static CheatSheetWindow GetSheet(MainWindow main) => Assert.IsType<CheatSheetWindow>(Assert.Single(main.OwnedWindows));

    static CheatSheetViewModel GetModel(CheatSheetWindow sheet) => Assert.IsType<CheatSheetViewModel>(sheet.DataContext);

    static string?[] VisibleKeyCaps(CheatSheetWindow sheet)
        => [.. sheet.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("kbd"))
            .Select(b => Assert.IsType<TextBlock>(b.Child).Text)];
}
