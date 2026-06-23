using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using FlyleafLib.Controls.WPF;
using LLPlayer.Services;
using LLPlayer.ViewModels;

namespace LLPlayer.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        // If this is not called first, the constructor of the other control will
        // run before FlyleafHost is initialized, so it will not work.
        DataContext = ((App)Application.Current).Container.Resolve<MainWindowVM>();

        InitializeComponent();

        SetWindowSize();
        SetTitleBarDarkMode(this);
    }

    private void SetWindowSize()
    {
        // 16:9 size list
        List<Size> candidateSizes =
        [
            new(1280, 720),
            new(1024, 576),
            new(960, 540),
            new(800, 450),
            new(640, 360),
            new(480, 270),
            new(320, 180)
        ];

        // Get available screen width / height
        double availableWidth = SystemParameters.WorkArea.Width;
        double availableHeight = SystemParameters.WorkArea.Height;

        // Get the largest size that will fit on the screen
        Size selectedSize = candidateSizes.FirstOrDefault(
            s => s.Width <= availableWidth && s.Height <= availableHeight,
            candidateSizes[^1]);

        // Set
        Width = selectedSize.Width;
        Height = selectedSize.Height;
    }

    #region Dark Title Bar
    /// <summary>
    /// ref: <see href="https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/nf-dwmapi-dwmsetwindowattribute" />
    /// </summary>
    [DllImport("DwmApi")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, int[] attrValue, int attrSize);
    const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    public static void SetTitleBarDarkMode(Window window)
    {
        try
        {
            var fl = ((App)Application.Current).Container.Resolve<FlyleafManager>();
            // Single source of truth: theme resolution lives in AppConfigTheme.EffectiveDark.
            // IsDarkTitlebar=false forces a light title bar regardless of the app theme.
            bool dark = fl.Config.IsDarkTitlebar && fl.Config.Theme.EffectiveDark;
            ApplyTitleBarDark(window, dark);
        }
        catch
        {
            // Title bar styling is cosmetic; never let it crash the app or a dialog window.
        }
    }

    /// <summary>Re-applies the title-bar appearance when the theme mode changes at runtime.</summary>
    public static void ReapplyTitleBar(bool dark)
    {
        try
        {
            // MainWindow is null during config load / CreateShell — the ctor's SetTitleBarDarkMode applies it then.
            if (Application.Current is not App app || app.MainWindow is not { } window)
            {
                return;
            }

            var fl = app.Container.Resolve<FlyleafManager>();
            ApplyTitleBarDark(window, dark && fl.Config.IsDarkTitlebar);
        }
        catch
        {
            // Cosmetic; ignore.
        }
    }

    private static void ApplyTitleBarDark(Window window, bool dark)
    {
        // Check OS Version
        if (!(Environment.OSVersion.Version >= new Version(10, 0, 18985)))
        {
            return;
        }

        // ref: https://stackoverflow.com/questions/71362654/wpf-window-titlebar
        IntPtr hWnd = new WindowInteropHelper(window).EnsureHandle();
        DwmSetWindowAttribute(hWnd, DWMWA_USE_IMMERSIVE_DARK_MODE, [dark ? 1 : 0], 4);
    }
    #endregion

    #region Win11 backdrop + OS appearance hook
    private const int WM_DWMCOLORIZATIONCOLORCHANGED = 0x0320;
    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
    private const int DWMWA_MICA_EFFECT = 1029;
    private const int DWMSBT_MAINWINDOW = 2;
    private AccentColorService? _accent;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // Live "Follow Windows" theme + opt-in accent sync: keep the watcher alive with the window and
        // hook the accent-changed message (this runs on the UI thread).
        _accent = ((App)Application.Current).Container.Resolve<AccentColorService>();
        if (PresentationSource.FromVisual(this) is HwndSource src)
        {
            src.AddHook(WndProc);
        }

        ApplyMicaBackdrop();
    }

    private void ApplyMicaBackdrop()
    {
        var fl = ((App)Application.Current).Container.Resolve<FlyleafManager>();
        if (!fl.Config.MicaBackdrop)
        {
            return;
        }

        // Win11 only. Note: the video is a DirectX child HWND, so by WPF airspace rules the Mica backdrop
        // can only show on chrome/borders/empty regions, never through the video surface.
        if (!(Environment.OSVersion.Version >= new Version(10, 0, 22000)))
        {
            return;
        }

        IntPtr hWnd = new WindowInteropHelper(this).EnsureHandle();
        if (Environment.OSVersion.Version >= new Version(10, 0, 22621))
        {
            DwmSetWindowAttribute(hWnd, DWMWA_SYSTEMBACKDROP_TYPE, [DWMSBT_MAINWINDOW], 4);
        }
        else
        {
            DwmSetWindowAttribute(hWnd, DWMWA_MICA_EFFECT, [1], 4);
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_DWMCOLORIZATIONCOLORCHANGED)
        {
            _accent?.OnOsAppearanceChanged();
        }

        return IntPtr.Zero;
    }

    private AppTrayService? _tray;
    private bool _playerDisposed;

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (e.Cancel)
        {
            return;
        }

        // If a batch is running (or its window is open), don't quit: hide to the tray and keep the app — and
        // the batch — alive. The player is NOT disposed on this path so it can be brought back instantly.
        try
        {
            _tray ??= ((App)Application.Current).Container.Resolve<AppTrayService>();
            if (_tray.TryMinimizeToTrayOnClose())
            {
                e.Cancel = true;
            }
        }
        catch (Exception ex)
        {
            // Never let tray/lifetime logic block a normal close.
            System.Diagnostics.Debug.WriteLine($"Minimize-to-tray on close failed: {ex}");
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        // Reached only on a real close (the minimize-to-tray path cancels Closing above).
        // ShutdownMode is OnExplicitShutdown, so closing the (real) main window must end the app itself —
        // the Shutdown() in finally is the single auto-quit guarantee, so it must run even if a disposal throws.
        try
        {
            try { _accent?.Dispose(); } catch { /* cosmetic; ignore */ }

            if (!_playerDisposed)
            {
                _playerDisposed = true;
                try
                {
                    ((App)Application.Current).Container.Resolve<FlyleafManager>().Player.Dispose();
                }
                catch
                {
                    // best-effort; teardown must not throw
                }
            }

            base.OnClosed(e);
        }
        finally
        {
            Application.Current.Shutdown();
        }
    }
    #endregion

    #region Disable IME
    private void FlyleafHost_SurfaceCreated(object sender, EventArgs e)
    {
        FlyleafHost host = (FlyleafHost)sender;
        DisableIME(host.Surface);
    }

    private void FlyleafHost_OverlayCreated(object sender, EventArgs e)
    {
        FlyleafHost host = (FlyleafHost)sender;
        DisableIME(host.Overlay);
    }

    private static void DisableIME(Window window)
    {
        InputMethod.SetIsInputMethodEnabled(window, false);
    }
    #endregion
}
