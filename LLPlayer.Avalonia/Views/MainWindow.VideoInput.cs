using Avalonia;
using Avalonia.Input;
using Avalonia.Threading;
using FlyleafLib.MediaPlayer;

namespace LLPlayer.Avalonia.Views;

public partial class MainWindow
{
    sealed record VideoPointerPress(IPointer Pointer, MouseButton Button, Point Position, double PanX, double PanY);
    VideoPointerPress? videoPointerPress;
    bool suppressVideoTap;

    void OnVideoPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (vm == null || !IsVideoBackground(e.Source)) return;
        PointerPointProperties properties = e.GetCurrentPoint(Video).Properties;
        MouseButton? button = properties.IsMiddleButtonPressed ? MouseButton.Middle
            : properties.IsXButton1Pressed ? MouseButton.XButton1
            : properties.IsXButton2Pressed ? MouseButton.XButton2
            : properties.IsLeftButtonPressed && e.KeyModifiers.HasFlag(KeyModifiers.Control) ? MouseButton.Left : null;
        if (button is not { } selectedButton) return;
        (double x, double y) = vm.Player.GetVideoPan();
        videoPointerPress = new(e.Pointer, selectedButton, e.GetPosition(Video), x, y);
        suppressVideoTap = true;
        e.Pointer.Capture(Video);
        e.Handled = true;
    }

    void OnVideoPointerMoved(object? sender, PointerEventArgs e)
    {
        if (vm == null || videoPointerPress is not { Button: MouseButton.Left } press
            || !ReferenceEquals(press.Pointer, e.Pointer)) return;
        if (!e.GetCurrentPoint(Video).Properties.IsLeftButtonPressed) { ClearVideoPointerGesture(); return; }
        if (Video.Bounds.Width <= 0 || Video.Bounds.Height <= 0) return;
        Vector distance = e.GetPosition(Video) - press.Position;
        vm.Player.SetVideoPan(press.PanX + distance.X / Video.Bounds.Width, press.PanY + distance.Y / Video.Bounds.Height);
        e.Handled = true;
    }

    void OnVideoPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (videoPointerPress is not { } press || !ReferenceEquals(press.Pointer, e.Pointer)
            || press.Button != e.InitialPressMouseButton) return;
        videoPointerPress = null;
        if (vm != null && IsVideoBackground(e.Source))
        {
            KeyBindingAction? action = press.Button switch
            {
                MouseButton.Middle when e.KeyModifiers.HasFlag(KeyModifiers.Control) => KeyBindingAction.ResetZoom,
                MouseButton.Middle when vm.Player.PrimarySlotEnabled => KeyBindingAction.SubsCurSeek,
                MouseButton.XButton1 => KeyBindingAction.SubsPrevSeekFallback,
                MouseButton.XButton2 => KeyBindingAction.SubsNextSeekFallback,
                _ => null,
            };
            if (action is { } owner) vm.Player.ResolveEngineAction(owner)?.Invoke();
        }
        e.Pointer.Capture(null);
        e.Handled = true;
        // Tapped/DoubleTapped are recognized after release; keep this sequence out of normal click actions.
        Dispatcher.UIThread.Post(() => { if (videoPointerPress == null) suppressVideoTap = false; }, DispatcherPriority.Background);
    }

    void ClearVideoPointerGesture()
    {
        VideoPointerPress? press = videoPointerPress;
        videoPointerPress = null;
        if (press != null) press.Pointer.Capture(null);
        Dispatcher.UIThread.Post(() => { if (videoPointerPress == null) suppressVideoTap = false; }, DispatcherPriority.Background);
    }
}
