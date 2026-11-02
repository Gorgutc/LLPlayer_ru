using System.Runtime.InteropServices;

namespace LLPlayer.Avalonia.Services;

/// <summary>OS input-idle adapter. If global idle cannot be observed, enabled CPU fallback stays on.</summary>
public sealed class BatchActivityMonitor
{
    public string Status { get; private set; } = "CPU while active applies to faster-whisper only.";
    public bool IsUserActive(int thresholdSeconds)
    {
        uint? idle = GetIdleMilliseconds();
        if (idle == null)
        {
            Status = "Global input idle detection is unavailable; CPU mode stays on while enabled (faster-whisper only).";
            return true;
        }
        Status = "Global input idle detection is available. CPU while active applies to faster-whisper only.";
        return idle.Value < Math.Clamp(thresholdSeconds, 5, 600) * 1000L;
    }
    static uint? GetIdleMilliseconds()
    {
        if (OperatingSystem.IsWindows())
        {
            var info = new LastInput { Size = (uint)Marshal.SizeOf<LastInput>() };
            return GetLastInputInfo(ref info) ? unchecked((uint)Environment.TickCount - info.Time) : null;
        }
        if (!OperatingSystem.IsLinux() || !CanUseGlobalX11Idle(Environment.GetEnvironmentVariable("XDG_SESSION_TYPE"),
            Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"), Environment.GetEnvironmentVariable("DISPLAY"))) return null;
        // XScreenSaver is optional. Wayland sessions without a global idle protocol use the conservative fallback.
        IntPtr display = IntPtr.Zero, infoPointer = IntPtr.Zero;
        try
        {
            display = XOpenDisplay(IntPtr.Zero);
            if (display == IntPtr.Zero) return null;
            infoPointer = XScreenSaverAllocInfo();
            if (infoPointer == IntPtr.Zero || XScreenSaverQueryInfo(display, XDefaultRootWindow(display), infoPointer) == 0) return null;
            return (uint)Math.Min((ulong)Marshal.PtrToStructure<ScreenSaverInfo>(infoPointer).Idle, uint.MaxValue);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException) { return null; }
        finally { if (infoPointer != IntPtr.Zero) XFree(infoPointer); if (display != IntPtr.Zero) XCloseDisplay(display); }
    }
    internal static bool CanUseGlobalX11Idle(string? sessionType, string? waylandDisplay, string? display) =>
        !string.Equals(sessionType, "wayland", StringComparison.OrdinalIgnoreCase)
        && string.IsNullOrEmpty(waylandDisplay) && !string.IsNullOrEmpty(display);
    [StructLayout(LayoutKind.Sequential)] struct LastInput { public uint Size, Time; }
    [StructLayout(LayoutKind.Sequential)] struct ScreenSaverInfo { public nuint Window; public int State, Kind; public nuint Since, Idle, EventMask; }
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] static extern bool GetLastInputInfo(ref LastInput info);
    [DllImport("libX11.so.6")] static extern IntPtr XOpenDisplay(IntPtr name);
    [DllImport("libX11.so.6")] static extern nuint XDefaultRootWindow(IntPtr display);
    [DllImport("libX11.so.6")] static extern int XCloseDisplay(IntPtr display);
    [DllImport("libX11.so.6")] static extern int XFree(IntPtr pointer);
    [DllImport("libXss.so.1")] static extern IntPtr XScreenSaverAllocInfo();
    [DllImport("libXss.so.1")] static extern int XScreenSaverQueryInfo(IntPtr display, nuint drawable, IntPtr info);
}
