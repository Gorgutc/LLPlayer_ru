using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using LLPlayer.Core.Batch;

namespace LLPlayer.Avalonia.Services;

/// <summary>One process-owned tray. Linux keeps a minimized taskbar window when indicator availability is unknown.</summary>
public sealed class BatchTrayService : IDisposable, IAsyncDisposable
{
    readonly Application app;
    readonly BatchWorkflow workflow;
    readonly Action restoreMain, showBatch;
    readonly Func<Task> quit;
    TrayIcon? icon;
    readonly HashSet<Window> backgroundWindows = [];
    readonly BatchDesktopFeedback feedback = new();
    readonly NativeMenuItem status = new() { Header = "Batch idle", IsEnabled = false };
    bool disposed, quitting, iconAttempted, wasRunning;
    string? reportedWarning;
    public bool CanHideToTray => OperatingSystem.IsWindows() && !disposed && EnsureIcon();
    public string BackgroundDescription => CanHideToTray
        ? "Batch work continues in the tray. Use the LLPlayer tray menu to restore the player, open Batch Subtitles or Quit."
        : "Batch work continues in the minimized window. Restore it from the taskbar; tray availability cannot be confirmed on this desktop.";

    public BatchTrayService(Application app, BatchWorkflow workflow, Action restoreMain, Action showBatch, Func<Task> quit)
    {
        this.app = app; this.workflow = workflow; this.restoreMain = restoreMain; this.showBatch = showBatch; this.quit = quit;
        workflow.Changed += Update;
        Update();
    }
    bool EnsureIcon()
    {
        if (icon != null) return true;
        if (iconAttempted || disposed) return false;
        iconAttempted = true;
        try
        {
            var menu = new NativeMenu();
            menu.Items.Add(status);
            menu.Items.Add(Item("Restore player", restoreMain));
            menu.Items.Add(Item("Batch subtitles…", showBatch));
            menu.Items.Add(new NativeMenuItemSeparator());
            menu.Items.Add(Item("Quit LLPlayer", () => _ = QuitAsync()));
            icon = new TrayIcon
            {
                Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://LLPlayer.Avalonia/Assets/llplayer.png"))),
                Menu = menu, ToolTipText = "LLPlayer", IsVisible = true
            };
            icon.Clicked += OnClicked;
            TrayIcon.SetIcons(app, new TrayIcons { icon });
        }
        catch (Exception ex)
        {
            icon?.Dispose(); icon = null;
            workflow.SetMessage($"Tray is unavailable: {ex.Message} Background windows remain accessible from the taskbar.");
        }
        return icon != null;
    }
    static NativeMenuItem Item(string text, Action action)
    {
        var item = new NativeMenuItem { Header = text };
        item.Click += (_, _) => action(); return item;
    }
    void OnClicked(object? sender, EventArgs args) => restoreMain();
    void Update()
    {
        if (disposed) return;
        bool show = workflow.IsRunning || backgroundWindows.Count > 0;
        if (show) EnsureIcon();
        if (icon != null) icon.IsVisible = show;
        if (wasRunning && !workflow.IsRunning && workflow.KeepAlive && backgroundWindows.Count > 0 && !quitting)
            feedback.Notify(workflow.LastRunCanceled ? "Batch subtitles canceled." : "Batch subtitles finished.");
        wasRunning = workflow.IsRunning;
        if (feedback.LastWarning is { } warning && warning != reportedWarning)
        { reportedWarning = warning; workflow.SetMessage(warning); }
        status.Header = workflow.Summary;
        if (icon != null) icon.ToolTipText = workflow.IsRunning ? $"LLPlayer · {workflow.OverallProgress:P0} {workflow.EtaText}" : workflow.WatchFolder ? "LLPlayer · Watching folder" : "LLPlayer";
    }
    public void SendToBackground(Window window)
    {
        if (backgroundWindows.Add(window)) { window.Activated += OnWindowActivated; window.Closed += OnWindowClosed; }
        if (CanHideToTray) window.Hide(); else window.WindowState = WindowState.Minimized;
        workflow.SetMessage(BackgroundDescription);
        Update();
    }
    void OnWindowActivated(object? sender, EventArgs args) { if (sender is Window window) RemoveBackground(window); }
    void OnWindowClosed(object? sender, EventArgs args) { if (sender is Window window) RemoveBackground(window); }
    void RemoveBackground(Window window)
    {
        if (!backgroundWindows.Remove(window)) return;
        window.Activated -= OnWindowActivated; window.Closed -= OnWindowClosed; Update();
    }
    public async Task QuitAsync()
    {
        if (quitting || disposed) return;
        quitting = true;
        try { await workflow.DisposeAsync(); await feedback.DisposeAsync(); await quit(); }
        catch (Exception ex) { quitting = false; workflow.SetMessage($"Cannot quit cleanly: {ex.Message}"); restoreMain(); showBatch(); }
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true; workflow.Changed -= Update;
        feedback.Cancel();
        foreach (var window in backgroundWindows) { window.Activated -= OnWindowActivated; window.Closed -= OnWindowClosed; }
        backgroundWindows.Clear();
        if (icon != null) { icon.Clicked -= OnClicked; icon.Dispose(); }
        TrayIcon.SetIcons(app, new TrayIcons());
    }
    public async ValueTask DisposeAsync() { Dispose(); await feedback.DisposeAsync(); }
}
