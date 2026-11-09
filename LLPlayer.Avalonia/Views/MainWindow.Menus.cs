using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace LLPlayer.Avalonia.Views;

public partial class MainWindow
{
    public static readonly AttachedProperty<bool> KeepOpenGesturesProperty =
        AvaloniaProperty.RegisterAttached<MainWindow, MenuItem, bool>("KeepOpenGestures");
    static readonly AttachedProperty<IPointer?> RightMenuPressProperty =
        AvaloniaProperty.RegisterAttached<MainWindow, MenuItem, IPointer?>("RightMenuPress");

    static MainWindow()
    {
        KeepOpenGesturesProperty.Changed.AddClassHandler<MenuItem>((item, _) =>
        {
            if (GetKeepOpenGestures(item))
            {
                item.AddHandler(PointerPressedEvent, OnMenuPointerPressed, RoutingStrategies.Tunnel);
                item.AddHandler(PointerReleasedEvent, OnMenuPointerReleased, RoutingStrategies.Tunnel);
            }
            else
            {
                item.RemoveHandler(PointerPressedEvent, OnMenuPointerPressed);
                item.RemoveHandler(PointerReleasedEvent, OnMenuPointerReleased);
                item.ClearValue(RightMenuPressProperty);
            }
        });
    }

    public static bool GetKeepOpenGestures(MenuItem item) => item.GetValue(KeepOpenGesturesProperty);
    public static void SetKeepOpenGestures(MenuItem item, bool value) => item.SetValue(KeepOpenGesturesProperty, value);

    static bool OwnsMenuPointer(MenuItem item, object? source)
        => source is Visual visual && ReferenceEquals(visual.FindAncestorOfType<MenuItem>(true), item);

    static void OnMenuPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not MenuItem item || !OwnsMenuPointer(item, e.Source)) return;
        item.ClearValue(RightMenuPressProperty);
        PointerPointProperties properties = e.GetCurrentPoint(item).Properties;
        item.StaysOpenOnClick = properties.IsRightButtonPressed || e.KeyModifiers.HasFlag(KeyModifiers.Control);
        if (!properties.IsRightButtonPressed || item.HasSubMenu || !item.IsEffectivelyEnabled) return;
        item.SetValue(RightMenuPressProperty, e.Pointer);
        e.Handled = true;
    }

    static void OnMenuPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (sender is not MenuItem item || e.InitialPressMouseButton != MouseButton.Right
            || !ReferenceEquals(item.GetValue(RightMenuPressProperty), e.Pointer)) return;
        item.ClearValue(RightMenuPressProperty);
        e.Handled = true;
        Point local = e.GetPosition(item);
        if (OwnsMenuPointer(item, e.Source) && item.IsEffectivelyEnabled && item.IsEffectivelyVisible
            && new Rect(item.Bounds.Size).Contains(local))
            // Avalonia 12's internal IMenuItem.RaiseClick uses this same public routed-event path.
            // MenuItem.OnClick owns command execution, so invoking Command here would execute it twice.
            item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
    }
}
