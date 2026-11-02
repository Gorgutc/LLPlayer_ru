using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using FlyleafLib.MediaPlayer.Batch;
using LLPlayer.Avalonia.Services;
using LLPlayer.Avalonia.ViewModels;

namespace LLPlayer.Avalonia.Views;

public partial class BatchSubtitlesWindow : Window
{
    readonly DispatcherTimer heartbeat = new() { Interval = TimeSpan.FromSeconds(1) };
    BatchSubtitlesViewModel? model;
    public Action<Window>? BackgroundRequested { get; set; }
    public bool IsShuttingDown { get; set; }
    public BatchSubtitlesWindow() { InitializeComponent(); }
    public BatchSubtitlesWindow(BatchSubtitlesViewModel model) : this()
    {
        this.model = model; DataContext = model;
        heartbeat.Tick += (_, _) =>
        {
            model.Workflow.Tick();
            BatchDesktopFeedback.UpdateTaskbar(this, model.Workflow.IsRunning, model.Workflow.OverallProgress);
        };
        Opened += (_, _) => { model.Opened(); heartbeat.Start(); };
        Closing += (_, e) =>
        {
            if (!IsShuttingDown && (model.Workflow.IsRunning || model.Workflow.IsScanning || model.Workflow.WatchFolder))
            { e.Cancel = true; SendToBackground(); }
        };
        Closed += (_, _) => { heartbeat.Stop(); BatchDesktopFeedback.UpdateTaskbar(this, false, 0); model.Closed(); };
    }
    public void Restore() { Show(); WindowState = WindowState.Normal; Activate(); }
    void SendToBackground()
    {
        if (BackgroundRequested != null) BackgroundRequested(this);
        else { WindowState = WindowState.Minimized; model?.Workflow.SetMessage("Batch work continues in the minimized window. Restore it from the taskbar."); }
    }
    void OnBackground(object? sender, RoutedEventArgs e) => SendToBackground();
    void OnClose(object? sender, RoutedEventArgs e) => Close();
    async void OnRetryJob(object? sender, RoutedEventArgs e)
    {
        if (model != null && sender is Control { DataContext: BatchSubtitleJob job }) await model.RetryJobCommand.ExecuteAsync(job);
    }
    void OnJobDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (model?.OpenInPlayerCommand.CanExecute(null) == true) model.OpenInPlayerCommand.Execute(null);
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Close(); e.Handled = true; }
        base.OnKeyDown(e);
    }
}
