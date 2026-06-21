using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using FlyleafLib.MediaPlayer;
using LLPlayer.Services;

namespace LLPlayer.Controls;

public partial class SubtitlesControl : UserControl
{
    public FlyleafManager FL { get; }

    private DispatcherTimer? _subsResizeTimer;
    private Size _pendingSubsSize;

    public SubtitlesControl()
    {
        InitializeComponent();

        FL = ((App)Application.Current).Container.Resolve<FlyleafManager>();

        DataContext = this;
        Unloaded += OnUnloaded;
    }

    private void SubtitlePanel_OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!e.HeightChanged)
            return;

        // Sometimes there is a very small decimal-point difference when subtitles switch and this event
        // fires; only react past a threshold. Debounce too: writing SubsPanelSize on every resize frame
        // re-lays-out the window (resize -> config -> relayout loop).
        double heightDiff = Math.Abs(e.NewSize.Height - e.PreviousSize.Height);
        if (heightDiff < 1.0)
            return;

        _pendingSubsSize = e.NewSize;
        _subsResizeTimer ??= CreateSubsResizeTimer();
        _subsResizeTimer.Stop();
        _subsResizeTimer.Start();
    }

    private DispatcherTimer CreateSubsResizeTimer()
    {
        var timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(120)
        };
        timer.Tick += (_, _) =>
        {
            _subsResizeTimer!.Stop();
            FL.Config.Subs.SubsPanelSize = _pendingSubsSize;
        };
        return timer;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        // Flush a pending resize so the final size is not lost if the control unloads mid-gesture.
        if (_subsResizeTimer is { IsEnabled: true })
        {
            _subsResizeTimer.Stop();
            FL.Config.Subs.SubsPanelSize = _pendingSubsSize;
        }
    }

    private async void SelectableSubtitleText_OnWordClicked(object sender, WordClickedEventArgs e)
    {
        try
        {
            await WordPopupControl.OnWordClicked(e);
        }
        catch (Exception ex)
        {
            // async void: never let a word-lookup failure (Process.Start, PDIC, translate) escape to the
            // global dispatcher handler as an intrusive modal — surface it as a non-blocking snackbar.
            FL.MessageQueue.Enqueue($"Word lookup failed: {ex.Message}");
        }
    }

    private void SelectableSubtitleText_OnWordClickedDown(object? sender, EventArgs e)
    {
        // Assume drag and stop playback.
        if (FL.Player.Status == Status.Playing)
        {
            FL.Player.Pause();
        }
    }
}
