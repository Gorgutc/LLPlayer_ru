using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using LLPlayer.ViewModels;

namespace LLPlayer.Views;

public partial class BatchSubtitlesDialog : UserControl
{
    private readonly BatchSubtitlesDialogVM _vm;
    private INotifyCollectionChanged? _transcript;

    public BatchSubtitlesDialog()
    {
        InitializeComponent();
        _vm = ((App)Application.Current).Container.Resolve<BatchSubtitlesDialogVM>();
        DataContext = _vm;

        _vm.PropertyChanged += OnVmPropertyChanged;
        Unloaded += OnUnloaded;
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

        ScrollToEnd();
    }

    private void OnTranscriptChanged(object? sender, NotifyCollectionChangedEventArgs e) => ScrollToEnd();

    private void ScrollToEnd()
    {
        int count = TranscriptList.Items.Count;
        if (count > 0)
            TranscriptList.ScrollIntoView(TranscriptList.Items[count - 1]);
    }
}
