using System.Runtime.InteropServices;

namespace LLPlayer.Services;

/// <summary>
/// Lightweight Win32 keyboard/mouse idle monitor that keeps a long batch friendly to interactive use: while
/// the user is active, the batch transcribes the next audio chunk on CPU instead of the GPU (the chunk in
/// flight finishes on its current device, so nothing computed is lost). Stateless and thread-safe, so
/// <see cref="IsUserActive"/> can be polled from the engine's ASR thread once per chunk.
/// </summary>
public sealed class BatchActivityMonitor
{
    private readonly Func<bool> _enabled;
    private readonly Func<int> _idleThresholdSeconds;

    /// <param name="enabled">Whether the CPU-while-active feature is on (read live from config).</param>
    /// <param name="idleThresholdSeconds">How long the keyboard/mouse must be idle before the user is
    /// considered away again (read live from config).</param>
    public BatchActivityMonitor(Func<bool> enabled, Func<int> idleThresholdSeconds)
    {
        _enabled = enabled;
        _idleThresholdSeconds = idleThresholdSeconds;
    }

    /// <summary>True when the feature is on AND the keyboard/mouse have been used within the idle threshold.</summary>
    public bool IsUserActive()
    {
        if (!_enabled())
            return false;

        uint idleMs = GetIdleMilliseconds();
        long thresholdMs = Math.Max(1, _idleThresholdSeconds()) * 1000L;
        return idleMs < thresholdMs;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    private static uint GetIdleMilliseconds()
    {
        LASTINPUTINFO info = new() { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        if (!GetLastInputInfo(ref info))
            return 0;

        // Unsigned subtraction is correct across the ~49-day GetTickCount wrap.
        return unchecked((uint)Environment.TickCount - info.dwTime);
    }
}
