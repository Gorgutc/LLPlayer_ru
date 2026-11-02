using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Animation;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.Styling;
using Avalonia.VisualTree;
using LLPlayer.Avalonia.Controls;
using LLPlayer.Avalonia.Services;
using LLPlayer.Avalonia.ViewModels;
using LLPlayer.Avalonia.Views;

namespace LLPlayer.Avalonia.Tests;

public class PlayerUiBehaviorTests
{
    [AvaloniaFact]
    public void Loaded_activity_timeout_controls_the_actual_fullscreen_idle_boundary()
    {
        var time = new ManualTimeProvider();
        var (vm, player, _, _, _) = TestVm.Create(time, prefs: Read("{\"ActivityTimeoutMs\":4000}"));
        using var owned = vm;
        player.IsOpened = player.IsPlaying = true;
        vm.OnFullScreenChanged(true);
        time.Advance(TimeSpan.FromMilliseconds(3999)); vm.UpdateIdle();
        Assert.True(vm.ControlsVisible);
        Assert.False(vm.CursorHidden);
        time.Advance(TimeSpan.FromMilliseconds(1)); vm.UpdateIdle();
        Assert.False(vm.ControlsVisible);
        Assert.True(vm.CursorHidden);
        vm.NotifyActivity();
        Assert.True(vm.ControlsVisible);
        Assert.False(vm.CursorHidden);
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void Loaded_single_click_flag_controls_real_video_input(bool enabled)
    {
        using var fixture = new PlayerWindow("{\"MouseSingleClickToPlay\":" + enabled.ToString().ToLowerInvariant() + "}");
        fixture.Click();
        Assert.Equal(enabled ? 1 : 0, fixture.Player.ToggleCalls);
        Assert.Equal(!enabled, fixture.Player.IsPlaying);
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void Loaded_double_click_flag_controls_fullscreen_without_enabling_single_click(bool enabled)
    {
        using var fixture = new PlayerWindow("{\"MouseSingleClickToPlay\":false,\"MouseDoubleClickToFullScreen\":" + enabled.ToString().ToLowerInvariant() + "}");
        fixture.Click(); fixture.Click();
        Assert.Equal(enabled, fixture.Window.IsFullScreen);
        Assert.Equal(0, fixture.Player.ToggleCalls);
    }

    [AvaloniaTheory]
    [InlineData(true, 1, 65)]
    [InlineData(true, -1, 55)]
    [InlineData(false, 1, 60)]
    [InlineData(false, -1, 60)]
    public void Loaded_wheel_flag_controls_real_video_volume(bool enabled, int delta, int expected)
    {
        using var fixture = new PlayerWindow("{\"MouseWheelToVolumeUpDown\":" + enabled.ToString().ToLowerInvariant() + "}");
        fixture.Player.Volume = 60;
        fixture.Window.MouseWheel(fixture.VideoPoint, new Vector(0, delta));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(expected, fixture.Player.Volume);
        Assert.Equal(0, fixture.Player.ToggleCalls);
    }

    [AvaloniaFact]
    public void Loaded_always_on_top_sets_actual_window_topmost()
    {
        using var fixture = new PlayerWindow("{\"AlwaysOnTop\":true}");
        Assert.True(fixture.Window.Topmost);
    }

    [AvaloniaFact]
    public void Settings_exposes_the_approved_player_ui_and_mouse_controls()
    {
        var (main, _, _, _, _) = TestVm.Create(); using var owned = main;
        var window = new SettingsWindow(main.CreateSettings());
        window.Show(); Dispatcher.UIThread.RunJobs();
        try
        {
            var navigation = window.FindControl<ListBox>("SettingsNavigation")!;
            var page = Assert.Single(navigation.Items.OfType<ListBoxItem>(), item => item.Content?.ToString() == "Player");
            navigation.SelectedItem = page; Dispatcher.UIThread.RunJobs();
            Assert.Contains(window.FindControl<StackPanel>("PlayerPage")!.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Player UI");
            foreach (string name in new[] { "ActivityTimeoutMsEditor", "SeekBarFadeInTimeMsEditor", "SeekBarFadeOutTimeMsEditor", "SeekBarHeightEditor", "SeekBarThumbHeightEditor", "SeekBarTrackHeightEditor" })
                Assert.True(window.FindControl<Control>(name) is TextBox or NumericUpDown, name);
            foreach (string name in new[] { "SeekBarShowOnlyMouseOverEditor", "AlwaysOnTopEditor", "MouseSingleClickToPlayEditor", "MouseDoubleClickToFullScreenEditor", "MouseWheelToVolumeUpDownEditor" })
                Assert.IsType<CheckBox>(window.FindControl<Control>(name));
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("Dark")]
    [InlineData("Light")]
    public void Live_save_updates_topmost_actual_slider_geometry_and_scoped_overlay_insets(string theme)
    {
        Application.Current!.RequestedThemeVariant = theme == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
        try
        {
            using var fixture = new PlayerWindow("{}");
            var slider = fixture.Window.FindControl<Slider>("SeekSlider")!;
            var volume = fixture.Window.FindControl<Slider>("VolumeSlider")!;
            Assert.Equal(20, slider.Bounds.Height); Assert.False(fixture.Window.Topmost);
            var draft = fixture.Vm.CreateSettings(); draft.SeekBarHeightText = "40"; draft.SeekBarThumbHeightText = "24"; draft.SeekBarTrackHeightText = "8"; draft.AlwaysOnTop = true;
            Assert.False(fixture.Window.Topmost); Assert.Equal(20, slider.Bounds.Height);
            draft.SaveCommand.Execute(null); Assert.Null(draft.Error); fixture.Pump();
            Assert.True(fixture.Window.Topmost); Assert.Equal(40, slider.Height); Assert.Equal(40, slider.MinHeight); Assert.Equal(40, slider.Bounds.Height);
            Assert.Equal(24d, slider.Resources["SliderHorizontalThumbHeight"]); Assert.Equal(24d, slider.Resources["SliderHorizontalThumbWidth"]);
            Assert.Equal(8d, slider.Resources["SliderTrackThemeHeight"]); Assert.Equal(new CornerRadius(12), slider.Resources["SliderThumbCornerRadius"]);
            Thumb thumb = Assert.Single(slider.GetVisualDescendants().OfType<Thumb>());
            Assert.Equal(24, thumb.Bounds.Height); Assert.Equal(24, thumb.Bounds.Width);
            Assert.Equal(new Thickness(12, 0), fixture.Window.FindControl<WaveformEnvelope>("WaveformEnvelope")!.Margin);
            Assert.Equal(12, fixture.Window.FindControl<AbLoopMarkers>("AbLoopMarkers")!.Inset);
            Assert.Equal(96, volume.Width);
            Assert.False(volume.Resources.ContainsKey("SliderHorizontalThumbHeight"));
            fixture.Window.Width = 760; fixture.Pump();
            Assert.Equal(40, slider.Bounds.Height); Assert.Equal(24, thumb.Bounds.Height);
            fixture.Window.IsFullScreen = true; fixture.Pump();
            Assert.True(fixture.Window.Topmost); Assert.Equal(40, slider.Bounds.Height);
            var cancel = fixture.Vm.CreateSettings(); cancel.AlwaysOnTop = false; cancel.SeekBarHeightText = "20"; cancel.CloseCommand.Execute(null);
            Assert.True(fixture.Window.Topmost); Assert.Equal(40, slider.Bounds.Height);
            var reset = fixture.Vm.CreateSettings(); reset.AlwaysOnTop = false; reset.SaveCommand.Execute(null); fixture.Pump();
            Assert.False(fixture.Window.Topmost);
        }
        finally { Application.Current!.RequestedThemeVariant = ThemeVariant.Dark; }
    }

    [AvaloniaFact]
    public void Actual_transition_uses_each_committed_direction_and_zero_is_immediate()
    {
        using var fixture = new PlayerWindow("{\"SeekBarFadeInTimeMs\":75,\"SeekBarFadeOutTimeMs\":350}");
        var bar = fixture.Window.FindControl<Border>("TransportBar")!;
        var transition = Assert.IsType<DoubleTransition>(Assert.Single(bar.Transitions!));
        Assert.Equal(Visual.OpacityProperty, transition.Property); Assert.Equal(TimeSpan.FromMilliseconds(75), transition.Duration);
        fixture.Window.IsFullScreen = true; fixture.Pump();
        fixture.Time.Advance(TimeSpan.FromSeconds(3)); fixture.Vm.UpdateIdle();
        Assert.False(fixture.Vm.ControlsVisible); Assert.False(bar.IsHitTestVisible); Assert.Equal(TimeSpan.FromMilliseconds(350), transition.Duration);
        fixture.Vm.NotifyActivity(); Assert.True(fixture.Vm.ControlsVisible); Assert.True(bar.IsHitTestVisible); Assert.Equal(TimeSpan.FromMilliseconds(75), transition.Duration);
        // Several reversals must keep a single owner; a stale hide completion must not win after activity.
        for (int i = 0; i < 3; i++)
        {
            fixture.Time.Advance(TimeSpan.FromSeconds(3)); fixture.Vm.UpdateIdle(); Assert.Equal(TimeSpan.FromMilliseconds(350), transition.Duration);
            fixture.Vm.NotifyActivity(); Assert.Equal(TimeSpan.FromMilliseconds(75), transition.Duration);
        }
        var draft = fixture.Vm.CreateSettings(); draft.SeekBarFadeInTimeMsText = "0"; draft.SeekBarFadeOutTimeMsText = "0"; draft.SaveCommand.Execute(null); Assert.Null(draft.Error);
        fixture.Time.Advance(TimeSpan.FromSeconds(3)); fixture.Vm.UpdateIdle(); fixture.Pump();
        Assert.Equal(TimeSpan.Zero, transition.Duration); Assert.Equal(0, bar.Opacity);
        fixture.Vm.NotifyActivity(); fixture.Pump(); Assert.Equal(TimeSpan.Zero, transition.Duration); Assert.Equal(1, bar.Opacity);
        Assert.Same(transition, Assert.Single(bar.Transitions!));
    }

    [AvaloniaFact]
    public void Hidden_transport_can_be_revealed_by_real_pointer_and_hidden_press_does_not_toggle_playback()
    {
        using var fixture = new PlayerWindow("{\"SeekBarShowOnlyMouseOver\":true,\"SeekBarFadeInTimeMs\":0,\"SeekBarFadeOutTimeMs\":0}");
        fixture.Window.IsFullScreen = true; fixture.Pump();
        var bar = fixture.Window.FindControl<Border>("TransportBar")!;
        var reveal = fixture.Window.FindControl<Border>("TransportRevealRegion")!;
        Assert.True(reveal.Bounds.Width > 0 && reveal.Bounds.Height > 0); Assert.False(reveal.IsHitTestVisible);
        fixture.Window.MouseMove(fixture.VideoPoint); fixture.Pump(); Assert.False(fixture.Vm.ControlsVisible); Assert.False(bar.IsHitTestVisible);
        Point regionPoint = reveal.TranslatePoint(new Point(reveal.Bounds.Width / 2, reveal.Bounds.Height / 2), fixture.Window)!.Value;
        Assert.Same(fixture.Window.VideoSurface, fixture.Window.InputHitTest(regionPoint));
        // No preceding move: hit testing belongs to the hidden video; this press only reveals the transport.
        fixture.Window.MouseDown(regionPoint, MouseButton.Left); fixture.Window.MouseUp(regionPoint, MouseButton.Left); fixture.Pump();
        Assert.True(fixture.Vm.ControlsVisible); Assert.Equal(0, fixture.Player.ToggleCalls); Assert.True(fixture.Window.IsFullScreen);
        fixture.Window.MouseMove(fixture.VideoPoint); fixture.Pump(); Assert.False(fixture.Vm.ControlsVisible);
        fixture.Window.MouseMove(regionPoint); fixture.Pump(); Assert.True(fixture.Vm.ControlsVisible); Assert.True(bar.IsHitTestVisible);
        ClickControl(fixture.Window.FindControl<Button>("PlayPauseButton")!); Assert.Equal(1, fixture.Player.ToggleCalls);
    }

    [AvaloniaFact]
    public void Classic_idle_hidden_transport_preserves_the_existing_immediate_video_click()
    {
        using var fixture = new PlayerWindow("{\"SeekBarFadeInTimeMs\":0,\"SeekBarFadeOutTimeMs\":0}");
        fixture.Window.IsFullScreen = true; fixture.Pump();
        fixture.Window.MouseMove(fixture.VideoPoint);
        fixture.Time.Advance(TimeSpan.FromSeconds(3)); fixture.Vm.UpdateIdle(); fixture.Pump();
        Assert.False(fixture.Vm.ControlsVisible);
        var reveal = fixture.Window.FindControl<Border>("TransportRevealRegion")!;
        Point point = reveal.TranslatePoint(new Point(reveal.Bounds.Width / 2, reveal.Bounds.Height / 2), fixture.Window)!.Value;
        Assert.Same(fixture.Window.VideoSurface, fixture.Window.InputHitTest(point));
        // The existing default policy has no reveal-only gesture. A click still owns play/pause.
        fixture.Window.MouseDown(point, MouseButton.Left); fixture.Window.MouseUp(point, MouseButton.Left); fixture.Pump();
        Assert.Equal(1, fixture.Player.ToggleCalls); Assert.False(fixture.Player.IsPlaying);
    }

    [AvaloniaFact]
    public void Actual_transport_keyboard_focus_and_detached_flyout_keep_hover_only_controls_reachable()
    {
        using var fixture = new PlayerWindow("{\"SeekBarShowOnlyMouseOver\":true,\"SeekBarFadeInTimeMs\":0,\"SeekBarFadeOutTimeMs\":0}");
        var play = fixture.Window.FindControl<Button>("PlayPauseButton")!;
        var speed = fixture.Window.FindControl<Button>("SpeedButton")!;
        fixture.Vm.NotifyKeyboardActivity(); fixture.Pump();
        Assert.True(play.Focus(NavigationMethod.Tab)); fixture.Pump();
        fixture.Window.MouseMove(fixture.VideoPoint); fixture.Time.Advance(TimeSpan.FromMinutes(1)); fixture.Vm.UpdateIdle(); fixture.Pump();
        Assert.True(play.IsFocused); Assert.True(fixture.Vm.ControlsVisible);
        speed.Flyout!.ShowAt(speed); fixture.Pump(); Assert.True(speed.Flyout.IsOpen);
        // Clearing button focus must leave the detached flyout as the active transport owner.
        fixture.Window.FocusManager!.Focus(null); fixture.Pump();
        fixture.Time.Advance(TimeSpan.FromMinutes(1)); fixture.Vm.UpdateIdle(); Assert.True(fixture.Vm.ControlsVisible);
        speed.Flyout.Hide(); fixture.Pump();
        // Avalonia restores the prior button focus when closing a flyout. This remains a legitimate owner.
        var bar = fixture.Window.FindControl<Border>("TransportBar")!;
        Assert.True(bar.IsKeyboardFocusWithin); Assert.True(fixture.Vm.ControlsVisible);
        fixture.Window.FocusManager!.Focus(null); fixture.Pump(); Assert.False(bar.IsKeyboardFocusWithin); Assert.False(fixture.Vm.ControlsVisible);
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void Mouse_flags_preserve_subtitle_selection_and_popup_close_ownership(bool flags)
    {
        string flag = flags.ToString().ToLowerInvariant();
        using var fixture = new PlayerWindow("{\"MouseSingleClickToPlay\":" + flag + ",\"MouseDoubleClickToFullScreen\":" + flag + ",\"MouseWheelToVolumeUpDown\":" + flag + "}");
        fixture.Player.PrimaryText = "Hello brave world"; fixture.Pump();
        var text = fixture.Window.FindControl<SubtitleText>("PrimarySubtitle")!;
        Point from = WordPoint(fixture.Window, text, "Hello"), to = WordPoint(fixture.Window, text, "world");
        fixture.Window.MouseMove(from); fixture.Window.MouseDown(from, MouseButton.Left, RawInputModifiers.LeftMouseButton);
        fixture.Window.MouseMove(to, RawInputModifiers.LeftMouseButton); fixture.Window.MouseUp(to, MouseButton.Left); fixture.Pump();
        Assert.Equal(0, fixture.Player.ToggleCalls); Assert.Equal(1, fixture.Player.PauseCalls);
        Assert.Equal("Hello brave world", Assert.Single(fixture.Translator.Calls).Word); Assert.True(fixture.Vm.WordPopup.IsOpen);
        // The popup close action on bare video still works when the play/pause gesture is disabled.
        fixture.Click(); Assert.False(fixture.Vm.WordPopup.IsOpen); Assert.Equal(0, fixture.Player.ToggleCalls);
    }

    [AvaloniaFact]
    public void Hover_only_recomputes_hover_after_resize_without_a_new_pointer_event()
    {
        using var fixture = new PlayerWindow("{\"SeekBarShowOnlyMouseOver\":true,\"SeekBarFadeInTimeMs\":0,\"SeekBarFadeOutTimeMs\":0}");
        var reveal = fixture.Window.FindControl<Border>("TransportRevealRegion")!;
        Point point = reveal.TranslatePoint(new Point(reveal.Bounds.Width / 2, reveal.Bounds.Height / 2), fixture.Window)!.Value;
        fixture.Window.MouseMove(point); fixture.Pump(); Assert.True(fixture.Vm.ControlsVisible);
        fixture.Window.Height = 800; fixture.Pump();
        Point newRegionOrigin = reveal.TranslatePoint(default, fixture.Window)!.Value;
        Assert.False(new Rect(newRegionOrigin, reveal.Bounds.Size).Contains(point));
        Assert.False(fixture.Vm.ControlsVisible);
        fixture.Window.IsFullScreen = true; fixture.Pump();
        newRegionOrigin = reveal.TranslatePoint(default, fixture.Window)!.Value;
        Assert.Equal(new Rect(newRegionOrigin, reveal.Bounds.Size).Contains(point), fixture.Vm.ControlsVisible);
    }

    static Point WordPoint(MainWindow window, SubtitleText text, string word)
    {
        for (double y = 1; y < text.Bounds.Height - 1; y++)
            for (double x = 1; x < text.Bounds.Width - 1; x++)
                if (text.TryGetWordAt(new Point(x, y), out string found) && found == word)
                {
                    Point point = text.TranslatePoint(new Point(x, y), window)!.Value;
                    if (ReferenceEquals(window.InputHitTest(point), text)) return point;
                }
        throw new Xunit.Sdk.XunitException($"No clickable '{word}' in {text.Bounds}.");
    }

    static void ClickControl(Control control)
    {
        TopLevel root = Assert.IsAssignableFrom<TopLevel>(TopLevel.GetTopLevel(control));
        Point point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), root)!.Value;
        root.MouseMove(point); root.MouseDown(point, MouseButton.Left); root.MouseUp(point, MouseButton.Left); Dispatcher.UIThread.RunJobs();
    }

    static AppPrefs Read(string json) => JsonSerializer.Deserialize<AppPrefs>(json, PrefsStore.JsonOptions)!.Normalize();

    sealed class PlayerWindow : IDisposable
    {
        public MainWindow Window { get; }
        public MainWindowViewModel Vm { get; }
        public FakePlaybackController Player { get; } = new() { IsOpened = true, IsPlaying = true };
        public ManualTimeProvider Time { get; } = new();
        public FakeTranslator Translator { get; } = new();
        public Point VideoPoint => Window.VideoSurface.TranslatePoint(new Point(Window.VideoSurface.Bounds.Width / 2, Window.VideoSurface.Bounds.Height / 3), Window)!.Value;

        public PlayerWindow(string json)
        {
            Window = new MainWindow(new KeyMapper(), null) { Width = 1000, Height = 600 };
            Vm = new MainWindowViewModel(Player, Read(json), _ => { }, Translator, Window, Time);
            Window.Attach(Vm, null); Window.Show();
            byte[] pixels = new byte[16 * 9 * 4];
            for (int i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
            Window.VideoSurface.PresentFrame(pixels, 16, 9, 16 * 4);
            Dispatcher.UIThread.RunJobs(); Window.CaptureRenderedFrame()?.Dispose(); Dispatcher.UIThread.RunJobs();
            Assert.Same(Window.VideoSurface, Window.InputHitTest(VideoPoint));
        }

        public void Click()
        {
            Window.MouseMove(VideoPoint); Window.MouseDown(VideoPoint, MouseButton.Left); Window.MouseUp(VideoPoint, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
        }

        public void Pump()
        {
            Dispatcher.UIThread.RunJobs(); Window.CaptureRenderedFrame()?.Dispose(); Dispatcher.UIThread.RunJobs();
        }

        public void Dispose()
        {
            foreach (Window owned in Window.OwnedWindows.ToArray()) owned.Close();
            Window.Close(); Vm.Dispose();
        }
    }
}
