using System.Globalization;

namespace LLPlayer.Avalonia.ViewModels;

public static class TimeFormat
{
    /// <summary>"m:ss" below one hour, "h:mm:ss" above (transport bar).</summary>
    public static string Clock(TimeSpan t)
    {
        if (t < TimeSpan.Zero)
            t = TimeSpan.Zero;

        return t.TotalHours >= 1
            ? string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}:{2:00}", (int)t.TotalHours, t.Minutes, t.Seconds)
            : string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}", t.Minutes, t.Seconds);
    }

    /// <summary>"mm:ss.f" (sidebar cue start), with hours when needed.</summary>
    public static string Cue(TimeSpan t)
    {
        if (t < TimeSpan.Zero)
            t = TimeSpan.Zero;

        return t.TotalHours >= 1
            ? string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}:{2:00}.{3}", (int)t.TotalHours, t.Minutes, t.Seconds, t.Milliseconds / 100)
            : string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}.{2}", t.Minutes, t.Seconds, t.Milliseconds / 100);
    }
}
