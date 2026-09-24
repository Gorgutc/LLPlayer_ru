using System.Globalization;

namespace FlyleafLib;

public static partial class Utils
{
    /// <summary>
    /// F-13 portable (Linux) replacement for the Win32 P/Invoke helpers of Utils/NativeMethods.cs (excluded from the
    /// portable TFM). Only the members used by shared (non-WPF) engine code are provided; window-management helpers
    /// (SetWindowLong, SetWindowPos, MonitorFromWindow, ...) are used by the WPF host only and have no portable form.
    /// </summary>
    public static class NativeMethods
    {
        /// <summary>
        /// Managed equivalent of shlwapi <c>StrCmpLogicalW</c> ("natural" sort): digit runs compare by numeric value,
        /// other text compares case-insensitively with the current culture.
        /// </summary>
        public static int StrCmpLogicalW(string psz1, string psz2)
        {
            if (ReferenceEquals(psz1, psz2))
                return 0;
            if (psz1 == null)
                return -1;
            if (psz2 == null)
                return 1;

            int i = 0, j = 0;
            while (i < psz1.Length && j < psz2.Length)
            {
                bool d1 = char.IsAsciiDigit(psz1[i]);
                bool d2 = char.IsAsciiDigit(psz2[j]);

                if (d1 && d2)
                {
                    int s1 = i, s2 = j;
                    while (i < psz1.Length && char.IsAsciiDigit(psz1[i])) i++;
                    while (j < psz2.Length && char.IsAsciiDigit(psz2[j])) j++;

                    ReadOnlySpan<char> n1 = psz1.AsSpan(s1, i - s1).TrimStart('0');
                    ReadOnlySpan<char> n2 = psz2.AsSpan(s2, j - s2).TrimStart('0');

                    if (n1.Length != n2.Length)
                        return n1.Length < n2.Length ? -1 : 1;

                    int cmp = n1.SequenceCompareTo(n2);
                    if (cmp != 0)
                        return cmp < 0 ? -1 : 1;
                }
                else if (d1 != d2)
                    return d1 ? -1 : 1; // digits sort before text
                else
                {
                    int s1 = i, s2 = j;
                    while (i < psz1.Length && !char.IsAsciiDigit(psz1[i])) i++;
                    while (j < psz2.Length && !char.IsAsciiDigit(psz2[j])) j++;

                    int cmp = string.Compare(psz1, s1, psz2, s2, Math.Max(i - s1, j - s2), CultureInfo.CurrentCulture, CompareOptions.IgnoreCase);
                    if (cmp != 0)
                        return cmp < 0 ? -1 : 1;
                }
            }

            if (i < psz1.Length) return 1;
            if (j < psz2.Length) return -1;
            return 0;
        }

        /// <summary>No cursor API off Windows: reports the cursor as hidden (-1) / shown (0) so ShowCursor loops end.</summary>
        public static int ShowCursor(bool bShow) => bShow ? 0 : -1;

        /// <summary>No-op off Windows (the timer resolution request is a winmm concept).</summary>
        public static uint TimeBeginPeriod(uint uMilliseconds) => 0;

        /// <summary>No-op off Windows (the timer resolution request is a winmm concept).</summary>
        public static uint TimeEndPeriod(uint uMilliseconds) => 0;

        /// <summary>
        /// No-op off Windows. Screen-saver / sleep inhibition is a host (desktop session) concern on Linux
        /// (e.g. org.freedesktop.ScreenSaver.Inhibit) and is left to the host application.
        /// </summary>
        public static EXECUTION_STATE SetThreadExecutionState(EXECUTION_STATE esFlags) => 0;

        [Flags]
        public enum EXECUTION_STATE : uint
        {
            ES_AWAYMODE_REQUIRED    = 0x00000040,
            ES_CONTINUOUS           = 0x80000000,
            ES_DISPLAY_REQUIRED     = 0x00000002,
            ES_SYSTEM_REQUIRED      = 0x00000001
        }

        #region DPI
        /// <summary>Render scaling (1.0 = 96 DPI) of the video host; set by the host application.</summary>
        public static double DpiX
        {
            get;
            set
            {
                if (value <= 0)
                    field = 1;
                else
                    field = value;
            }
        } = 1;

        /// <summary>Render scaling (1.0 = 96 DPI) of the video host; set by the host application.</summary>
        public static double DpiY
        {
            get;
            set
            {
                if (value <= 0)
                    field = 1;
                else
                    field = value;
            }
        } = 1;

        /// <summary>Source DPI used for bitmap subtitles (96 by default); set by the host application.</summary>
        public static double DpiXSource
        {
            get;
            set
            {
                if (value <= 0)
                    field = 96;
                else
                    field = value;
            }
        } = 96;

        /// <summary>Source DPI used for bitmap subtitles (96 by default); set by the host application.</summary>
        public static double DpiYSource
        {
            get;
            set
            {
                if (value <= 0)
                    field = 96;
                else
                    field = value;
            }
        } = 96;
        #endregion
    }
}
