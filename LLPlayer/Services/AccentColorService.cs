using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using FlyleafLib;
using Microsoft.Win32;

namespace LLPlayer.Services;

/// <summary>
/// Watches Windows appearance changes (theme + accent) and re-applies the app theme.
/// Handles live "Follow Windows" theme switching and opt-in accent-colour sync. SystemEvents fires
/// on a dedicated STA listener thread, so all theme work is marshalled onto the WPF UI thread
/// (PaletteHelper mutates Application.Current.Resources, which has UI-thread affinity).
/// </summary>
public sealed class AccentColorService : IDisposable
{
    private readonly FlyleafManager FL;
    private readonly LogHandler _log = new("[App] [AccentColor   ] ");

    public AccentColorService(FlyleafManager fl)
    {
        FL = fl;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    /// <summary>
    /// The current Windows accent colour (HKCU\SOFTWARE\Microsoft\Windows\DWM\AccentColor, a REG_DWORD
    /// stored as 0xAABBGGRR; the alpha byte is discarded for an opaque WPF Color). Falls back to the
    /// app's default pink on any failure.
    /// </summary>
    public static Color GetWindowsAccentColor()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\DWM");
            if (key?.GetValue("AccentColor") is int abgr)
            {
                byte r = (byte)(abgr & 0xFF);
                byte g = (byte)((abgr >> 8) & 0xFF);
                byte b = (byte)((abgr >> 16) & 0xFF);
                return Color.FromRgb(r, g, b);
            }
        }
        catch
        {
            // fall through to default
        }

        return (Color)ColorConverter.ConvertFromString("#D23D6F");
    }

    /// <summary>Re-apply the theme on an OS appearance change. Safe to call from any thread.</summary>
    public void OnOsAppearanceChanged()
    {
        Dispatcher? dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null)
        {
            return;
        }

        if (dispatcher.CheckAccess())
        {
            Apply();
        }
        else
        {
            dispatcher.BeginInvoke(Apply);
        }
    }

    private void Apply()
    {
        // Theme/accent re-apply is cosmetic and runs from an OS-appearance window message (WndProc /
        // SystemEvents). A colorization message can arrive during startup before the MDIX resources are
        // ready, where PaletteHelper.GetTheme/SetTheme throws — and on the dispatcher that would crash the
        // app. Guard it exactly like App.OnInitialized: theme re-apply must never crash the app.
        try
        {
            // FollowOS live-switch (a no-op for fixed Dark/Light modes).
            FL.Config.Theme.ApplyBaseTheme();

            // Accent sync (only when the user opted in).
            if (FL.Config.AccentColorSync)
            {
                FL.Config.Theme.ApplyAccentSync(GetWindowsAccentColor());
            }
        }
        catch (Exception ex)
        {
            _log.Error($"Failed to re-apply theme on OS appearance change: {ex}");
        }
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color)
        {
            OnOsAppearanceChanged();
        }
    }

    public void Dispose()
    {
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
    }
}
