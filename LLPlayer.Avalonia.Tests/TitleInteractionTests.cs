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

public class TitleInteractionTests
{
    const string MediaTitle = "An actual media title with pointer actions";

    [AvaloniaFact]
    public async Task Left_title_release_invokes_the_normal_media_picker_once_and_opens_the_selected_file()
    {
        using TempDir temporary = new();
        string selected = Path.Combine(temporary.Path, "selected.mkv"); File.WriteAllText(selected, "fixture");
        TitleShell shell = new() { NextPick = selected };
        FakePlaybackController player = new() { IsOpened = true, IsPlaying = true, Title = MediaTitle };
        using var main = new MainWindowViewModel(player, new AppPrefs(), _ => { }, new FakeTranslator(), shell);
        var window = new MainWindow(new KeyMapper(), null) { Width = 1280, Height = 700 };
        window.Attach(main, null); window.Show();
        try
        {
            Control title = TitleControl(window); Point point = Center(window, title);
            window.MouseMove(point); window.MouseDown(point, MouseButton.Left, RawInputModifiers.LeftMouseButton);
            Assert.Equal(0, shell.MediaPicks);
            window.MouseUp(point, MouseButton.Left); Dispatcher.UIThread.RunJobs();
            await (main.OpenMediaCommand.ExecutionTask ?? Task.CompletedTask);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, shell.MediaPicks); Assert.Equal([selected], player.Opened);
            Assert.Empty(player.Seeks); Assert.Equal(0, player.ToggleCalls);
            Assert.False(title.ContextMenu?.IsOpen ?? false);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Right_title_release_opens_local_media_actions_without_picker_seek_or_play_toggle()
    {
        TitleShell shell = new();
        FakePlaybackController player = new() { IsOpened = true, IsPlaying = true, Title = MediaTitle };
        using var main = new MainWindowViewModel(player, new AppPrefs(), _ => { }, new FakeTranslator(), shell);
        var window = new MainWindow(new KeyMapper(), null) { Width = 1280, Height = 700 };
        window.Attach(main, null); window.Show();
        ContextMenu? menu = null;
        try
        {
            Control title = TitleControl(window); Point point = Center(window, title);
            window.MouseMove(point); window.MouseDown(point, MouseButton.Right, RawInputModifiers.RightMouseButton);
            window.MouseUp(point, MouseButton.Right); Dispatcher.UIThread.RunJobs();
            menu = title.ContextMenu;
            Assert.NotNull(menu); Assert.True(menu.IsOpen);
            var actions = menu.Items.OfType<MenuItem>().ToArray();
            Assert.Contains(actions, item => ReferenceEquals(item.Command, main.OpenPreviousLocalMediaCommand));
            Assert.Contains(actions, item => ReferenceEquals(item.Command, main.OpenNextLocalMediaCommand));
            Assert.Contains(actions, item => ReferenceEquals(item.Command, main.OpenMediaLocationCommand));
            Assert.Contains(actions, item => ReferenceEquals(item.Command, main.CopyMediaPathCommand));
            Assert.Equal(0, shell.MediaPicks); Assert.Empty(player.Opened);
            Assert.Empty(player.Seeks); Assert.Empty(player.SubtitleSeeks); Assert.Equal(0, player.ToggleCalls);
        }
        finally { menu?.Close(); window.Close(); }
    }

    static Control TitleControl(Window window)
    {
        Dispatcher.UIThread.RunJobs(); window.CaptureRenderedFrame()?.Dispose(); Dispatcher.UIThread.RunJobs();
        return window.FindControl<Button>("MediaTitleButton") is { } button ? button
            : Assert.Single(window.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == MediaTitle);
    }
    static Point Center(Window window, Control title)
    {
        Assert.True(title.IsEffectivelyVisible); Assert.True(title.Bounds.Width > 0 && title.Bounds.Height > 0);
        Point point = title.TranslatePoint(new Point(title.Bounds.Width / 2, title.Bounds.Height / 2), window)!.Value;
        Assert.NotNull(window.InputHitTest(point));
        return point;
    }
    sealed class TitleShell : IAppShell
    {
        public bool IsFullScreen { get; set; }
        public string? NextPick { get; init; }
        public int MediaPicks { get; private set; }
        public Task<string?> PickMediaFileAsync(string? initialFolder) { MediaPicks++; return Task.FromResult(NextPick); }
        public Task<string?> PickSubtitlesFileAsync(string? initialFolder) => Task.FromResult<string?>(null);
        public void ShowCheatSheet() { }
        public void SetClipboardText(string text) { }
        public void ApplyTheme(string theme) { }
        public void Exit() { }
    }
}
