using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LLPlayer.Avalonia.Controls;
using LLPlayer.Avalonia.Services;
using LLPlayer.Avalonia.ViewModels;
using LLPlayer.Avalonia.Views;

namespace LLPlayer.Avalonia.Tests;

/// <summary>Real MainWindow hit testing and routed mouse input over a presented software frame.</summary>
public class VideoPointerInteractionTests
{
    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void Left_click_on_presented_video_toggles_playback(bool playing)
    {
        using var fixture = new PlayerWindow(playing: playing);
        Point point = fixture.VideoPoint();
        Assert.Same(fixture.Window.VideoSurface, fixture.Window.InputHitTest(point));
        Assert.Equal(1, fixture.Window.VideoSurface.FramesPresented);

        Click(fixture.Window, point);

        Assert.Equal(1, fixture.Player.ToggleCalls);
        Assert.Equal(!playing, fixture.Player.IsPlaying);
    }

    [AvaloniaFact]
    public void Left_click_without_media_does_not_toggle_playback()
    {
        using var fixture = new PlayerWindow(opened: false, playing: false);
        Click(fixture.Window, fixture.VideoPoint(new Point(20, 20)));
        Assert.Equal(0, fixture.Player.ToggleCalls);
    }

    [AvaloniaFact]
    public void Modified_left_click_does_not_toggle_playback()
    {
        using var fixture = new PlayerWindow();
        Click(fixture.Window, fixture.VideoPoint(), modifiers: RawInputModifiers.Control);
        Assert.Equal(0, fixture.Player.ToggleCalls);
        Assert.True(fixture.Player.IsPlaying);
    }

    [AvaloniaFact]
    public void Right_click_on_presented_video_opens_subtitles_and_settings_menu()
    {
        using var fixture = new PlayerWindow();
        Click(fixture.Window, fixture.VideoPoint(), MouseButton.Right);

        ContextMenu menu = Assert.IsType<ContextMenu>(fixture.VideoHost.ContextMenu);
        Assert.True(menu.IsOpen);
        Assert.Contains(menu.Items.OfType<MenuItem>(), item => Header(item) == "Subtitles");
        MenuItem settings = Assert.Single(menu.Items.OfType<MenuItem>(), item => Header(item).StartsWith("Settings", StringComparison.Ordinal));
        Assert.Same(fixture.Vm.ShowSettingsCommand, settings.Command);
        Assert.Equal(0, fixture.Player.ToggleCalls);
        Assert.True(fixture.Player.IsPlaying);
    }

    [AvaloniaFact]
    public void Right_click_settings_entry_opens_existing_settings_dialog()
    {
        using var fixture = new PlayerWindow();
        Click(fixture.Window, fixture.VideoPoint(), MouseButton.Right);
        ContextMenu menu = Assert.IsType<ContextMenu>(fixture.VideoHost.ContextMenu);
        Assert.True(menu.IsOpen);
        MenuItem settings = Assert.Single(menu.Items.OfType<MenuItem>(), item => Header(item).StartsWith("Settings", StringComparison.Ordinal));

        ClickControl(settings);

        SettingsWindow dialog = Assert.IsType<SettingsWindow>(Assert.Single(fixture.Window.OwnedWindows));
        Assert.Same(fixture.Window, dialog.Owner);
        Assert.True(dialog.IsDialog);
        Assert.Equal(0, fixture.Player.ToggleCalls);
    }

    [AvaloniaFact]
    public void Context_subtitle_action_reuses_live_visibility_command()
    {
        using var fixture = new PlayerWindow();
        Click(fixture.Window, fixture.VideoPoint(), MouseButton.Right);
        ContextMenu menu = Assert.IsType<ContextMenu>(fixture.VideoHost.ContextMenu);
        Assert.True(menu.IsOpen);
        MenuItem subtitles = Assert.Single(menu.Items.OfType<MenuItem>(), item => Header(item) == "Subtitles");
        ClickControl(subtitles);
        Pump(fixture.Window);
        MenuItem visibility = Assert.Single(subtitles.Items.OfType<MenuItem>(), item => ReferenceEquals(item.Command, fixture.Vm.ToggleSubtitlesCommand));
        Assert.True(fixture.Vm.SubtitlesVisible);

        ClickControl(visibility);

        Assert.False(fixture.Vm.SubtitlesVisible);
        Assert.Equal(0, fixture.Player.ToggleCalls);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Subtitle_word_and_phrase_input_keep_selection_ownership(bool drag)
    {
        using var fixture = new PlayerWindow();
        fixture.Player.PrimaryText = "Hello brave world";
        Pump(fixture.Window);
        SubtitleText text = fixture.Window.FindControl<SubtitleText>("PrimarySubtitle")!;
        Point from = WordPoint(fixture.Window, text, "Hello");
        Point to = drag ? WordPoint(fixture.Window, text, "world") : from;
        fixture.Window.MouseMove(from);
        fixture.Window.MouseDown(from, MouseButton.Left, RawInputModifiers.LeftMouseButton);
        if (drag)
            fixture.Window.MouseMove(to, RawInputModifiers.LeftMouseButton);
        fixture.Window.MouseUp(to, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(0, fixture.Player.ToggleCalls);
        Assert.Equal(1, fixture.Player.PauseCalls);
        Assert.Single(fixture.Translator.Calls);
        Assert.Equal(drag ? "Hello brave world" : "Hello", fixture.Translator.Calls[0].Word);
        Assert.True(fixture.Vm.WordPopup.IsOpen);
    }

    [AvaloniaFact]
    public void Popup_button_click_does_not_toggle_video_playback()
    {
        using var fixture = new PlayerWindow();
        fixture.Player.PrimaryText = "Hello world";
        Pump(fixture.Window);
        SubtitleText text = fixture.Window.FindControl<SubtitleText>("PrimarySubtitle")!;
        Click(fixture.Window, WordPoint(fixture.Window, text, "Hello"));
        Assert.True(fixture.Vm.WordPopup.IsOpen);
        Pump(fixture.Window);
        Border popup = fixture.Window.FindControl<Border>("WordPopupCard")!;
        Button close = Assert.Single(popup.GetVisualDescendants().OfType<Button>(), button => ReferenceEquals(button.Command, fixture.Vm.WordPopup.CloseCommand));

        ClickControl(close);

        Assert.False(fixture.Vm.WordPopup.IsOpen);
        Assert.Equal(0, fixture.Player.ToggleCalls);
    }

    [AvaloniaFact]
    public void Transport_button_click_executes_play_pause_once()
    {
        using var fixture = new PlayerWindow();
        ClickControl(fixture.Window.FindControl<Button>("PlayPauseButton")!);
        Assert.Equal(1, fixture.Player.ToggleCalls);
        Assert.False(fixture.Player.IsPlaying);
    }

    [AvaloniaFact]
    public void Video_drag_does_not_become_a_playback_click()
    {
        using var fixture = new PlayerWindow();
        Point from = fixture.VideoPoint();
        Point to = from + new Vector(100, 40);
        fixture.Window.MouseMove(from);
        fixture.Window.MouseDown(from, MouseButton.Left, RawInputModifiers.LeftMouseButton);
        fixture.Window.MouseMove(to, RawInputModifiers.LeftMouseButton);
        fixture.Window.MouseUp(to, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(0, fixture.Player.ToggleCalls);
    }

    [AvaloniaFact]
    public void Double_click_keeps_fullscreen_and_only_first_tap_toggles_playback()
    {
        using var fixture = new PlayerWindow();
        List<string> events = [];
        fixture.VideoHost.AddHandler(InputElement.TappedEvent, (_, _) => events.Add("tap"), RoutingStrategies.Bubble, handledEventsToo: true);
        fixture.VideoHost.AddHandler(InputElement.DoubleTappedEvent, (_, _) => events.Add("double"), RoutingStrategies.Bubble, handledEventsToo: true);
        Point point = fixture.VideoPoint();
        Click(fixture.Window, point);
        Click(fixture.Window, point);

        Assert.Equal(["tap", "double"], events);
        Assert.True(fixture.Window.IsFullScreen);
        // WPF's immediate single-click action may pause before fullscreen; no debounce or rollback is introduced.
        Assert.Equal(1, fixture.Player.ToggleCalls);
        Assert.False(fixture.Player.IsPlaying);
    }

    [AvaloniaFact]
    public void Video_wheel_keeps_existing_volume_action()
    {
        using var fixture = new PlayerWindow();
        fixture.Player.Volume = 60;
        fixture.Window.MouseWheel(fixture.VideoPoint(), new Vector(0, 1));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(65, fixture.Player.Volume);
        Assert.Equal(0, fixture.Player.ToggleCalls);
    }

    static string Header(MenuItem item) => (item.Header?.ToString() ?? "").Replace("_", "", StringComparison.Ordinal);

    static void Click(TopLevel window, Point point, MouseButton button = MouseButton.Left, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        window.MouseMove(point, modifiers);
        window.MouseDown(point, button, modifiers);
        window.MouseUp(point, button, modifiers);
        Dispatcher.UIThread.RunJobs();
    }

    static void ClickControl(Control control)
    {
        TopLevel root = Assert.IsAssignableFrom<TopLevel>(TopLevel.GetTopLevel(control));
        Point point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), root)!.Value;
        Assert.True(control.IsEffectivelyVisible);
        Assert.True(control.Bounds.Width > 0 && control.Bounds.Height > 0);
        Click(root, point);
    }

    static Point WordPoint(MainWindow window, SubtitleText text, string word)
    {
        for (double y = 1; y < text.Bounds.Height - 1; y++)
            for (double x = 1; x < text.Bounds.Width - 1; x++)
            {
                Point local = new(x, y);
                if (!text.TryGetWordAt(local, out string found) || found != word)
                    continue;
                Point point = text.TranslatePoint(local, window)!.Value;
                if (ReferenceEquals(window.InputHitTest(point), text))
                    return point;
            }
        throw new Xunit.Sdk.XunitException($"Expected a clickable '{word}' inside {text.Bounds}.");
    }

    static void Pump(MainWindow window)
    {
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame()?.Dispose();
        Dispatcher.UIThread.RunJobs();
    }

    sealed class PlayerWindow : IDisposable
    {
        public MainWindow Window { get; }
        public MainWindowViewModel Vm { get; }
        public FakePlaybackController Player { get; }
        public FakeTranslator Translator { get; } = new();
        public Panel VideoHost => Window.FindControl<Panel>("VideoHost")!;

        public PlayerWindow(bool opened = true, bool playing = true)
        {
            Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
            Player = new FakePlaybackController { IsOpened = opened, IsPlaying = playing };
            Window = new MainWindow(new KeyMapper(), null) { Width = 1000, Height = 600 };
            Vm = new MainWindowViewModel(Player, new AppPrefs().Normalize(), _ => { }, Translator, Window);
            Window.Attach(Vm, renderer: null);
            Window.Show();
            byte[] pixels = new byte[16 * 9 * 4];
            for (int i = 0; i < pixels.Length; i += 4)
            {
                pixels[i] = 80;
                pixels[i + 1] = 110;
                pixels[i + 2] = 140;
                pixels[i + 3] = 255;
            }
            Window.VideoSurface.PresentFrame(pixels, 16, 9, 16 * 4);
            Pump(Window);
        }

        public Point VideoPoint(Point? local = null)
            => Window.VideoSurface.TranslatePoint(local ?? new Point(Window.VideoSurface.Bounds.Width / 2, Window.VideoSurface.Bounds.Height / 3), Window)!.Value;

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
}