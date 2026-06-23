using System.Drawing;
using System.Windows;
using FlyleafLib;
using FlyleafLib.MediaPlayer;
using WinForms = System.Windows.Forms;

namespace LLPlayer.Services;

/// <summary>
/// System-tray presence for background batch processing. When the main video window is closed while a batch
/// is running (or its window is open), the app keeps running, the player window hides, and this tray icon
/// shows the overall batch progress and offers Open / Quit. The player can be brought back from the tray or
/// by double-clicking a video row in the batch list.
///
/// Uses the built-in WinForms <see cref="WinForms.NotifyIcon"/> (no extra NuGet dependency; WinForms was
/// already pulled in transitively via FlyleafLib). The icon is created lazily and only made visible while it
/// is needed (a batch is active, or the main window is hidden), so normal foreground use is unaffected.
/// </summary>
public sealed class AppTrayService : IDisposable
{
    private readonly FlyleafManager _fl;
    private readonly BatchActivityService _activity;
    private readonly LogHandler Log = new("[App] [TrayService   ] ");

    private WinForms.NotifyIcon? _icon;
    private WinForms.ContextMenuStrip? _menu;
    private Icon? _appIcon;
    private bool _backgroundHintShown;
    private bool _disposed;

    public AppTrayService(FlyleafManager fl, BatchActivityService activity)
    {
        _fl = fl;
        _activity = activity;
        _activity.Changed += (_, _) => OnActivityChanged();
        _activity.RunCompleted += (_, canceled) => OnRunCompleted(canceled);
    }

    private static Window? MainWindow => Application.Current?.MainWindow;

    /// <summary>
    /// Called from the main window's close handler. If a batch should keep the app alive, hide the window to
    /// the tray (instead of quitting) and return true so the caller cancels the close.
    /// </summary>
    public bool TryMinimizeToTrayOnClose()
    {
        if (_activity.IsShuttingDown || !_activity.KeepAlive)
            return false;

        MinimizeMainWindowToTray();
        return true;
    }

    /// <summary>Hide the main window to the tray, pausing playback so nothing plays unseen in the background.</summary>
    public void MinimizeMainWindowToTray()
    {
        WinForms.NotifyIcon icon = EnsureIcon();

        try
        {
            if (_fl.Player.Status == Status.Playing)
                _fl.Player.Pause();
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to pause on minimize-to-tray: {ex.Message}");
        }

        MainWindow?.Hide();
        UpdateIcon(mainHidden: true);

        if (!_backgroundHintShown)
        {
            _backgroundHintShown = true;
            TryBalloon("LLPlayer is still running in the tray — batch subtitles continue. Double-click to reopen.");
        }
    }

    /// <summary>Bring the player window back from the tray and focus it.</summary>
    public void RestoreMainWindow()
    {
        if (MainWindow is not { } w)
            return;

        if (!w.IsVisible)
            w.Show();
        if (w.WindowState == WindowState.Minimized)
            w.WindowState = WindowState.Normal;
        w.Activate();

        UpdateIcon(mainHidden: false);
    }

    private void OnActivityChanged()
    {
        if (_disposed)
            return;

        bool mainHidden = MainWindow is { IsVisible: false };

        // If nothing keeps us alive in the background and the player window is hidden (e.g. the user closed
        // the batch dialog after the run finished), bring the window back so the app is never stuck running
        // invisibly.
        if (!_activity.KeepAlive && !_activity.IsShuttingDown && mainHidden)
        {
            RestoreMainWindow();
            return;
        }

        UpdateIcon(mainHidden);
    }

    private void OnRunCompleted(bool canceled)
    {
        // Only nag from the tray when we're actually in the background; never during an app quit.
        if (_activity.IsShuttingDown || MainWindow is { IsVisible: true })
            return;

        TryBalloon(canceled ? "Batch subtitles canceled." : "Batch subtitles finished.");
    }

    private void UpdateIcon(bool mainHidden)
    {
        // Show the icon only when actually in the background: a batch is running, or the main window is
        // hidden to the tray. An open-but-foregrounded batch dialog with no active run does not surface one.
        bool show = _activity.IsRunning || mainHidden;

        if (!show)
        {
            if (_icon != null)
                _icon.Visible = false;
            return;
        }

        WinForms.NotifyIcon icon = EnsureIcon();
        icon.Text = BuildTooltip();
        icon.Visible = true;
    }

    private string BuildTooltip()
    {
        if (_activity.IsRunning)
        {
            // Reuse the detailed status the batch VM publishes (e.g. "CPU 3/7 • 42%"); fall back to a percent.
            string detail = _activity.ProgressText;
            string text = string.IsNullOrWhiteSpace(detail)
                ? $"LLPlayer — Batch {(int)Math.Round(_activity.Progress * 100)}%"
                : $"LLPlayer — {detail}";
            // NotifyIcon.Text is capped at 63 chars on older shells; the formatted string is always short,
            // so this slice (to the legacy cap) is just a defensive guard and is effectively never taken.
            return text.Length <= 63 ? text : text[..63];
        }

        return "LLPlayer";
    }

    private WinForms.NotifyIcon EnsureIcon()
    {
        if (_icon != null)
            return _icon;

        _menu = new WinForms.ContextMenuStrip();
        _menu.Items.Add(new WinForms.ToolStripMenuItem("Open LLPlayer", null, (_, _) => RestoreMainWindow()));
        _menu.Items.Add(new WinForms.ToolStripMenuItem("Batch subtitles…", null, (_, _) =>
        {
            try { _fl.Action.CmdOpenWindowBatchSubtitles.Execute(); }
            catch (Exception ex) { Log.Error($"Failed to open batch from tray: {ex.Message}"); }
        }));
        _menu.Items.Add(new WinForms.ToolStripSeparator());
        _menu.Items.Add(new WinForms.ToolStripMenuItem("Quit", null, (_, _) => _activity.RequestQuit()));

        // Own the extracted Icon so it can be disposed; NotifyIcon.Dispose does not dispose an assigned Icon.
        _appIcon = TryLoadAppIcon();

        _icon = new WinForms.NotifyIcon
        {
            Text = "LLPlayer",
            Visible = false,
            ContextMenuStrip = _menu,
            Icon = _appIcon,
        };
        _icon.DoubleClick += (_, _) => RestoreMainWindow();

        return _icon;
    }

    private static Icon TryLoadAppIcon()
    {
        try
        {
            string? exe = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exe))
            {
                Icon? extracted = Icon.ExtractAssociatedIcon(exe);
                if (extracted != null)
                    return extracted;
            }
        }
        catch
        {
            // fall through to the generic application icon
        }

        return SystemIcons.Application;
    }

    private void TryBalloon(string message)
    {
        try
        {
            EnsureIcon().ShowBalloonTip(4000, "LLPlayer", message, WinForms.ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to show tray balloon: {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        if (_icon != null)
        {
            _icon.Visible = false;
            _icon.Dispose();
            _icon = null;
        }

        _menu?.Dispose();
        _menu = null;

        // Dispose the extracted icon handle (but never the shared SystemIcons.Application instance).
        if (_appIcon != null && !ReferenceEquals(_appIcon, SystemIcons.Application))
        {
            _appIcon.Dispose();
        }
        _appIcon = null;
    }
}
