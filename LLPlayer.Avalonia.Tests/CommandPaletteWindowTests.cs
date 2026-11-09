using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LLPlayer.Avalonia.Services;
using LLPlayer.Avalonia.ViewModels;
using LLPlayer.Avalonia.Views;

namespace LLPlayer.Avalonia.Tests;

public class CommandPaletteWindowTests
{
    [AvaloniaFact]
    public void Enter_closes_the_palette_before_opening_an_owned_window_once()
    {
        using var fixture = new PaletteFixture();
        fixture.Palette.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        fixture.Palette.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        Assert.Equal(["closed", "owner"], fixture.Events);
        Assert.False(fixture.Palette.IsVisible);
        Assert.Same(fixture.Owner, fixture.Opened?.Owner);
    }

    [AvaloniaFact]
    public void Double_click_activates_the_selected_result_and_Escape_cancels_without_running()
    {
        using (var fixture = new PaletteFixture())
        {
            ListBox results = Assert.IsType<ListBox>(fixture.Palette.GetVisualDescendants().OfType<ListBox>().SingleOrDefault());
            Point point = results.TranslatePoint(new Point(20, 20), fixture.Palette)!.Value;
            fixture.Palette.MouseMove(point);
            fixture.Palette.MouseDown(point, MouseButton.Left);
            fixture.Palette.MouseUp(point, MouseButton.Left);
            fixture.Palette.MouseDown(point, MouseButton.Left);
            Assert.False(fixture.Palette.IsVisible, "The second press closes the palette before opening the command's window.");
            Window opened = Assert.IsType<Window>(fixture.Opened);
            Assert.Same(fixture.Owner, opened.Owner);
            // The physical release belongs to the new live window; the palette's native surface is disposed.
            opened.MouseUp(new Point(20, 20), MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(["closed", "owner"], fixture.Events);
        }
        using (var fixture = new PaletteFixture())
        {
            fixture.Palette.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
            Assert.Equal(["closed"], fixture.Events);
            Assert.False(fixture.Palette.IsVisible);
        }
    }

    [Fact]
    public void Executable_help_refreshes_current_rows_preserves_filter_and_closes_before_running()
    {
        List<string> events = [];
        ExecutableCommand previous = new("app:ToggleSidebar", "ToggleSidebar", "Sidebar", "Window", ["Ctrl+B"], () => { });
        CheatSheetViewModel model = new([previous], new AppPrefs()) { FilterText = "Ctrl+B" };
        ExecutableCommand current = new("app:OpenWindowSettings", "OpenWindowSettings", "Settings", "Window", ["Ctrl+B"], () => events.Add("owner"));
        model.Refresh([current], new AppPrefs());
        Assert.Equal("Ctrl+B", model.FilterText);
        ShortcutRowViewModel row = Assert.Single(Assert.Single(model.Groups).Rows);
        Assert.Equal("OpenWindowSettings", row.ActionName);
        Assert.True(model.TryActivate(row, () => events.Add("close")));
        Assert.False(model.TryActivate(row, () => events.Add("again")));
        Assert.Equal(["close", "owner"], events);
    }

    sealed class PaletteFixture : IDisposable
    {
        public Window Owner { get; } = new() { Width = 400, Height = 300 };
        public CommandPaletteWindow Palette { get; }
        public Window? Opened { get; private set; }
        public List<string> Events { get; } = [];

        public PaletteFixture()
        {
            ExecutableCommand command = new("app:OpenWindowSettings", "OpenWindowSettings", "Settings", "Window", [], () =>
            {
                CommandPaletteWindow active = Palette ?? throw new InvalidOperationException("The palette must be constructed before activation.");
                Assert.False(active.IsVisible);
                Events.Add("owner");
                Opened = new Window();
                Opened.Show(Owner);
            });
            Palette = new CommandPaletteWindow(new CommandPaletteViewModel([command]));
            Palette.Closed += (_, _) => Events.Add("closed");
            Owner.Show();
            Palette.Show(Owner);
            Dispatcher.UIThread.RunJobs();
        }

        public void Dispose()
        {
            Opened?.Close();
            if (Palette.IsVisible) Palette.Close();
            Owner.Close();
        }
    }
}
