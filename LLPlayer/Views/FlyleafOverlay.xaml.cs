using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using LLPlayer.ViewModels;

namespace LLPlayer.Views;

public partial class FlyleafOverlay : UserControl
{
    private FlyleafOverlayVM VM => (FlyleafOverlayVM)DataContext;

    private DispatcherTimer? _resizeTimer;
    private Size _pendingScreenSize;

    public FlyleafOverlay()
    {
        InitializeComponent();

        DataContext = ((App)Application.Current).Container.Resolve<FlyleafOverlayVM>();
        Unloaded += OnUnloaded;
    }

    private void FlyleafOverlay_OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!e.HeightChanged && !e.WidthChanged)
            return;

        // The height of MainWindow cannot be used because it includes the title bar, so the overlay
        // size is captured here. Debounce: writing ScreenWidth/Height to the shared config on every
        // resize frame triggers a relayout (resize -> config -> relayout loop), so commit once the
        // gesture settles. Both dimensions are captured so a width-only resize is not dropped (only the
        // ScreenHeight setter currently feeds layout; ScreenWidth is captured for completeness).
        double sizeDiff = Math.Max(
            Math.Abs(e.NewSize.Width - e.PreviousSize.Width),
            Math.Abs(e.NewSize.Height - e.PreviousSize.Height));
        if (sizeDiff < 1.0)
            return;

        _pendingScreenSize = e.NewSize;

        _resizeTimer ??= CreateResizeTimer();
        _resizeTimer.Stop();
        _resizeTimer.Start();
    }

    private DispatcherTimer CreateResizeTimer()
    {
        var timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(120)
        };
        timer.Tick += (_, _) =>
        {
            _resizeTimer!.Stop();
            CommitScreenSize();
        };
        return timer;
    }

    private void CommitScreenSize()
    {
        // Order matters: the ScreenHeight setter recomputes subtitle layout and must see the new width.
        VM.FL.Config.ScreenWidth = _pendingScreenSize.Width;
        VM.FL.Config.ScreenHeight = _pendingScreenSize.Height;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        // Flush a pending resize so the final size is not lost if the overlay unloads mid-gesture.
        if (_resizeTimer is { IsEnabled: true })
        {
            _resizeTimer.Stop();
            CommitScreenSize();
        }
    }
}
