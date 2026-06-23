using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using LLPlayer.ViewModels;

namespace LLPlayer.Views;

public partial class BatchSubtitlesDialog : UserControl
{
    private readonly BatchSubtitlesDialogVM _vm;
    private INotifyCollectionChanged? _transcript;
    private bool _scrollScheduled;

    public BatchSubtitlesDialog()
    {
        InitializeComponent();
        _vm = ((App)Application.Current).Container.Resolve<BatchSubtitlesDialogVM>();
        DataContext = _vm;

        _vm.PropertyChanged += OnVmPropertyChanged;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    // Show overall batch progress on this dialog window's own taskbar button (so it is visible even when the
    // main player window is closed / minimized to the tray).
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Window? win = Window.GetWindow(this);
        if (win == null)
            return;

        win.TaskbarItemInfo ??= new TaskbarItemInfo();
        BindingOperations.SetBinding(win.TaskbarItemInfo, TaskbarItemInfo.ProgressValueProperty,
            new Binding(nameof(BatchSubtitlesDialogVM.OverallProgress)) { Source = _vm });
        BindingOperations.SetBinding(win.TaskbarItemInfo, TaskbarItemInfo.ProgressStateProperty,
            new Binding(nameof(BatchSubtitlesDialogVM.TaskbarProgressState)) { Source = _vm });
    }

    // Double-click a video row to play it in the main player. Ignored when the click lands on the row's
    // interactive content (the include checkbox or the retry button).
    private void OnJobDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject src)
            return;

        // Only react to double-clicks on an actual data row — not group/column headers or empty space (the
        // list is grouped, so those regions are reachable) — and not on the row's interactive controls
        // (include checkbox / retry button).
        if (FindAncestor<DataGridRow>(src) == null || FindAncestor<ButtonBase>(src) != null)
            return;

        if (_vm.SelectedJob is { } job)
            _vm.CmdOpenInPlayer.Execute(job);
    }

    private static T? FindAncestor<T>(DependencyObject? node) where T : DependencyObject
    {
        while (node != null)
        {
            if (node is T match)
                return match;

            node = node is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(node)
                : LogicalTreeHelper.GetParent(node);
        }

        return null;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _vm.PropertyChanged -= OnVmPropertyChanged;
        if (_transcript != null)
            _transcript.CollectionChanged -= OnTranscriptChanged;
        _transcript = null;
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(BatchSubtitlesDialogVM.ActiveJob))
            return;

        // Re-hook the transcript collection of the newly active job so we keep auto-scrolling.
        if (_transcript != null)
            _transcript.CollectionChanged -= OnTranscriptChanged;

        _transcript = _vm.ActiveJob?.Transcript;
        if (_transcript != null)
            _transcript.CollectionChanged += OnTranscriptChanged;

        ScheduleScrollToEnd();
    }

    private void OnTranscriptChanged(object? sender, NotifyCollectionChangedEventArgs e) => ScheduleScrollToEnd();

    // Auto-scroll must NOT run synchronously inside the transcript's CollectionChanged notification:
    // calling ScrollIntoView there re-enters the ListBox generator before it has finished processing the
    // same change, which throws "ItemsControl is inconsistent with its items source" and crashes the
    // process. Defer it to a later dispatcher turn (after layout) and coalesce rapid segment updates.
    private void ScheduleScrollToEnd()
    {
        if (_scrollScheduled)
            return;

        _scrollScheduled = true;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            _scrollScheduled = false;
            ScrollToEnd();
        }), DispatcherPriority.Background);
    }

    private void ScrollToEnd()
    {
        int count = TranscriptList.Items.Count;
        if (count > 0)
            TranscriptList.ScrollIntoView(TranscriptList.Items[count - 1]);
    }
}
