using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows;
using CliWrap;
using Vortice.Direct3D11;

namespace FlyleafLib;

public static partial class Utils
{
    public static bool IsTesting { private get; set; } = false;

    public static readonly Rect         RectZero            = new(); // Rect.Empty has infinity values
    public static readonly Point        PointEmpty          = new();
    public static readonly CornerRadius CornerRadiusEmpty   = new();


    // VLC : https://github.com/videolan/vlc/blob/master/modules/gui/qt/dialogs/preferences/simple_preferences.cpp
    // Kodi: https://github.com/xbmc/xbmc/blob/master/xbmc/settings/AdvancedSettings.cpp

    public static readonly List<string> ExtensionsAudio =
    [
        // VLC
          "3ga" , "669" , "a52" , "aac" , "ac3"
        , "adt" , "adts", "aif" , "aifc", "aiff"
        , "au"  , "amr" , "aob" , "ape" , "caf"
        , "cda" , "dts" , "flac", "it"  , "m4a"
        , "m4p" , "mid" , "mka" , "mlp" , "mod"
        , "mp1" , "mp2" , "mp3" , "mpc" , "mpga"
        , "oga" , "oma" , "opus", "qcp" , "ra"
        , "rmi" , "snd" , "s3m" , "spx" , "tta"
        , "voc" , "vqf" , "w64" , "wav" , "wma"
        , "wv"  , "xa"  , "xm"
    ];

    public static readonly List<string> ExtensionsPictures =
    [
        "apng", "bmp", "gif", "jpg", "jpeg", "png", "ico", "tif", "tiff", "tga","jfif"
    ];

    public static readonly List<string> ExtensionsSubtitlesText =
    [
        "ass", "ssa", "srt", "text", "vtt"
    ];

    public static readonly List<string> ExtensionsSubtitlesBitmap =
    [
        "sub", "sup"
    ];

    public static readonly List<string> ExtensionsSubtitles = [..ExtensionsSubtitlesText, ..ExtensionsSubtitlesBitmap];

    public static readonly List<string> ExtensionsVideo =
    [
        // VLC
          "3g2" , "3gp" , "3gp2", "3gpp", "amrec"
        , "amv" , "asf" , "avi" , "bik" , "divx"
        , "drc" , "dv"  , "f4v" , "flv" , "gvi"
        , "gxf" , "m1v" , "m2t" , "m2v" , "m2ts"
        , "m4v" , "mkv" , "mov" , "mp2v", "mp4"
        , "mp4v", "mpa" , "mpe" , "mpeg", "mpeg1"
        , "mpeg2","mpeg4","mpg" , "mpv2", "mts"
        , "mtv" , "mxf" , "nsv" , "nuv" , "ogg"
        , "ogm" , "ogx" , "ogv" , "rec" , "rm"
        , "rmvb", "rpl" , "thp" , "tod" , "ts"
        , "tts" , "vob" , "vro" , "webm", "wmv"
        , "xesc"

        // Additional
        , "dav"
    ];

    private static int uniqueId;
    public static int GetUniqueId() { Interlocked.Increment(ref uniqueId); return uniqueId; }

    /// <summary>
    /// Begin Invokes the UI thread to execute the specified action
    /// </summary>
    /// <param name="action"></param>
    public static void UI(Action action)
    {
#if DEBUG
        if (Application.Current == null)
            return;
#endif

        Application.Current.Dispatcher.BeginInvoke(action, System.Windows.Threading.DispatcherPriority.DataBind);
    }

    /// <summary>
    /// Begin Invokes the UI thread if required to execute the specified action
    /// </summary>
    /// <param name="action"></param>
    public static void UIIfRequired(Action action)
    {
        if (Thread.CurrentThread.ManagedThreadId == Application.Current.Dispatcher.Thread.ManagedThreadId)
            action();
        else
            Application.Current.Dispatcher.BeginInvoke(action);
    }

    /// <summary>
    /// Invokes the UI thread to execute the specified action
    /// </summary>
    /// <param name="action"></param>
    public static void UIInvoke(Action action) => Application.Current.Dispatcher.Invoke(action, System.Windows.Threading.DispatcherPriority.DataBind);

    /// <summary>
    /// Invokes the UI thread if required to execute the specified action
    /// </summary>
    /// <param name="action"></param>
    public static void UIInvokeIfRequired(Action action)
    {
        if (IsTesting) return;

        if (Environment.CurrentManagedThreadId == Application.Current.Dispatcher.Thread.ManagedThreadId)
            action();
        else
            Application.Current.Dispatcher.Invoke(action, System.Windows.Threading.DispatcherPriority.DataBind);
    }

    public static Thread STA(Action action)
    {
        Thread thread = new(() => action());
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        return thread;
    }

    public static void STAInvoke(Action action)
    {
        Thread thread = STA(action);
        thread.Join();
    }

    public static int Align(int num, int align)
    {
        int mod = num % align;
        return mod == 0 ? num : num + (align - mod);
    }

    /// <summary>
    /// Works only for power of 2
    /// </summary>
    /// <param name="num"></param>
    /// <param name="align"></param>
    /// <returns></returns>
    public static int FFALIGN(int num, int align)
        => (num + align - 1) & ~(align - 1);

    public static float Scale(float value, float inMin, float inMax, float outMin, float outMax)
        => ((value - inMin) * (outMax - outMin) / (inMax - inMin)) + outMin;

    public static double SnapToInt(double value, double epsilon = 1e-6)
    {
        double nearest = Math.Round(value);
        return Math.Abs(value - nearest) < epsilon ? nearest : value;
    }

    // We can't trust those
    //public static private bool    IsDesignMode=> (bool) DesignerProperties.IsInDesignModeProperty.GetMetadata(typeof(DependencyObject)).DefaultValue;
    //public static bool            IsDesignMode    = LicenseManager.UsageMode == LicenseUsageMode.Designtime; // Will not work properly (need to be called from non-static class constructor)

    //public static bool          IsWin11         = Regex.IsMatch(Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "ProductName", "").ToString(), "Windows 11");
    //public static bool          IsWin10         = Regex.IsMatch(Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "ProductName", "").ToString(), "Windows 10");
    //public static bool          IsWin8          = Regex.IsMatch(Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "ProductName", "").ToString(), "Windows 8");
    //public static bool          IsWin7          = Regex.IsMatch(Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "ProductName", "").ToString(), "Windows 7");

    public static List<string> GetMoviesSorted(List<string> movies)
    {
        List<string> moviesSorted = new();

        for (int i = 0; i < movies.Count; i++)
        {
            if (IsVideoExtension(movies[i]))
                moviesSorted.Add(movies[i]);
        }

        moviesSorted.Sort(new NaturalStringComparer());

        return moviesSorted;
    }

    /// <summary>
    /// True when the path's extension is a known video container (the same <see cref="ExtensionsVideo"/> list the
    /// batch scanner uses). Extracted from <see cref="GetMoviesSorted"/> so the scanner and the watch-folder
    /// watcher share one source of truth for "is this a video file".
    /// </summary>
    public static bool IsVideoExtension(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        string ext = Path.GetExtension(path);
        if (string.IsNullOrWhiteSpace(ext))
            return false;

        return ExtensionsVideo.Contains(ext[1..].ToLower());
    }
    public sealed class NaturalStringComparer : IComparer<string>
        { public int Compare(string a, string b) => NativeMethods.StrCmpLogicalW(a, b); }

    public static string GetRecInnerException(Exception e)
    {
        string dump = "";
        var cur = e.InnerException;

        for (int i = 0; i < 4; i++)
        {
            if (cur == null) break;
            dump += "\r\n - " + cur.Message;
            cur = cur.InnerException;
        }

        return dump;
    }
    public static string GetUrlExtention(string url)
    {
        int index;
        if ((index = url.LastIndexOf('.')) > 0)
            return url[(index + 1)..].ToLower();

        return "";
    }

    public static List<Language> GetSystemLanguages()
    {
        List<Language> Languages = [ Language.English ];

        if (OriginalCulture.ThreeLetterISOLanguageName != "eng")
            Languages.Add(Language.Get(OriginalCulture));

        foreach (System.Windows.Forms.InputLanguage lang in System.Windows.Forms.InputLanguage.InstalledInputLanguages)
            if (lang.Culture.ThreeLetterISOLanguageName != OriginalCulture.ThreeLetterISOLanguageName && lang.Culture.ThreeLetterISOLanguageName != "eng")
                Languages.Add(Language.Get(lang.Culture));

        return Languages;
    }

    public static CultureInfo OriginalCulture { get; private set; }
    public static CultureInfo OriginalUICulture { get; private set; }

    public static void SaveOriginalCulture()
    {
        OriginalCulture = CultureInfo.CurrentCulture;
        OriginalUICulture = CultureInfo.CurrentUICulture;
    }

    public class MediaParts
    {
        public string   Title       { get; set; } = "";
        public string   Extension   { get; set; } = "";
        public int      Season      { get; set; }
        public int      Episode     { get; set; }
        public int      Year        { get; set; }
    }
    public static MediaParts GetMediaParts(string title, bool checkSeasonEpisodeOnly = false)
    {
        Match res;
        MediaParts mp = new();
        int index = int.MaxValue; // title end pos

        res = RxSeasonEpisode1().Match(title);
        if (!res.Success)
        {
            res = RxSeasonEpisode2().Match(title);

            if (!res.Success)
                res = RxEpisodePart().Match(title);
        }

        if (res.Groups.Count > 1)
        {
            if (res.Groups["season"].Value != "")
                mp.Season = int.Parse(res.Groups["season"].Value);

            if (res.Groups["episode"].Value != "")
                mp.Episode = int.Parse(res.Groups["episode"].Value);

            if (checkSeasonEpisodeOnly || res.Index == 0) // 0: No title just season/episode
                return mp;

            index = res.Index;
        }

        mp.Extension = GetUrlExtention(title);
        if (mp.Extension.Length > 0 && mp.Extension.Length < 5)
            title = title[..(title.Length - mp.Extension.Length - 1)];

        // non-movie words, 1080p, 2015
        if ((res = RxExtended().Match(title)).Index > 0 && res.Index < index)
            index = res.Index;

        if ((res = RxDirectorsCut().Match(title)).Index > 0 && res.Index < index)
            index = res.Index;

        if ((res = RxBrrip().Match(title)).Index > 0 && res.Index < index)
            index = res.Index;

        if ((res = RxResolution().Match(title)).Index > 0 && res.Index < index)
            index = res.Index;

        res = RxYear().Match(title);
        Group gc;
        if (res.Success && (gc = res.Groups["year"]).Index > 2)
        {
            mp.Year = int.Parse(gc.Value);
            if (res.Index < index)
                index = res.Index;
        }

        if (index != int.MaxValue)
            title = title[..index];

        title = title.Replace(".", " ").Replace("_", " ");
        title = RxSpaces().Replace(title, " ");
        title = RxNonAlphaNumeric().Replace(title, "");

        mp.Title = title.Trim();

        return mp;
    }

    public static string FindNextAvailableFile(string fileName)
    {
        if (!File.Exists(fileName)) return fileName;

        string tmp = Path.Combine(Path.GetDirectoryName(fileName), Regex.Replace(Path.GetFileNameWithoutExtension(fileName), @"(.*) (\([0-9]+)\)$", "$1"));
        string newName;

        for (int i = 1; i < 101; i++)
        {
            newName = tmp + " (" + i + ")" + Path.GetExtension(fileName);
            if (!File.Exists(newName)) return newName;
        }

        return null;
    }
    public static string GetValidFileName(string name) => string.Join("_", name.Split(Path.GetInvalidFileNameChars()));

    public static string GetFolderPath(string folder)
    {
        if (folder.StartsWith(":"))
        {
            folder = folder[1..];
            return FindFolderBelow(folder);
        }

        return Path.IsPathRooted(folder) ? folder : Path.GetFullPath(folder);
    }

    public static string FindFolderBelow(string folder)
    {
        string current = AppDomain.CurrentDomain.BaseDirectory;

        while (current != null)
        {
            if (Directory.Exists(Path.Combine(current, folder)))
                return Path.Combine(current, folder);

            current = Directory.GetParent(current)?.FullName;
        }

        return null;
    }
    public static string DownloadToString(string url, int timeoutMs = 30000)
    {
        try
        {
            using HttpClient client = new() { Timeout = TimeSpan.FromMilliseconds(timeoutMs) };
            return client.GetAsync(url).Result.Content.ReadAsStringAsync().Result;
        }
        catch (Exception e)
        {
            Log($"Download failed {e.Message} [Url: {url ?? "Null"}]");
        }

        return null;
    }

    public static string FixFileUrl(string url)
    {
        try
        {
            if (url == null || url.Length < 5)
                return url;

            if (url[..5].Equals("file:", StringComparison.OrdinalIgnoreCase))
                return new Uri(url).LocalPath;
        }
        catch { }

        return url;
    }
    public static string LowerCaseFirstChar(string input)
    {   // check null manually
        Span<char> buffer = stackalloc char[input.Length];
        input.AsSpan().CopyTo(buffer);
        buffer[0] = char.ToLowerInvariant(buffer[0]);

        return new string(buffer);
    }

    /// <summary>
    /// Convert Windows lnk file path to target path
    /// </summary>
    /// <param name="filepath">lnk file path</param>
    /// <returns>targetPath or null</returns>
    public static string GetLnkTargetPath(string filepath)
    {
        try
        {
            // Using dynamic COM
            // ref: https://stackoverflow.com/a/49198242/9070784
            dynamic windowsShell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell", true)!);
            dynamic shortcut = windowsShell!.CreateShortcut(filepath);
            string targetPath = shortcut.TargetPath;

            if (string.IsNullOrEmpty(targetPath))
                throw new InvalidOperationException("TargetPath is empty.");

            return targetPath;
        }
        catch (Exception e)
        {
            Log($"Resolving Windows Link failed {e.Message} [FilePath: {filepath}]");

            return null;
        }
    }

    public static string GetBytesReadable(nuint i)
    {
        // Determine the suffix and readable value
        string suffix;
        double readable;
        if (i >= 0x1000000000000000) // Exabyte
        {
            suffix = "EB";
            readable = i >> 50;
        }
        else if (i >= 0x4000000000000) // Petabyte
        {
            suffix = "PB";
            readable = i >> 40;
        }
        else if (i >= 0x10000000000) // Terabyte
        {
            suffix = "TB";
            readable = i >> 30;
        }
        else if (i >= 0x40000000) // Gigabyte
        {
            suffix = "GB";
            readable = i >> 20;
        }
        else if (i >= 0x100000) // Megabyte
        {
            suffix = "MB";
            readable = i >> 10;
        }
        else if (i >= 0x400) // Kilobyte
        {
            suffix = "KB";
            readable = i;
        }
        else
            return i.ToString("0 B"); // Byte

        // Divide by 1024 to get fractional value
        readable /= 1024;
        // Return formatted number with suffix (InvariantCulture so the decimal separator
        // is always '.', regardless of the current culture, e.g. "1.5 KB" not "1,5 KB" on ru-RU)
        return readable.ToString("0.## ", CultureInfo.InvariantCulture) + suffix;
    }

    public static Dictionary<string, string> ParseQueryString(ReadOnlySpan<char> query)
    {
        Dictionary<string, string> dict = [];

        int nameStart   = 0;
        int equalPos    = -1;
        for (int i = 0; i < query.Length; i++)
        {
            if (query[i] == '=')
                equalPos = i;
            else if (query[i] == '&')
            {
                if (equalPos == -1)
                    dict[query[nameStart..i].ToString()] = null;
                else
                    dict[query[nameStart..equalPos].ToString()] = query.Slice(equalPos + 1, i - equalPos - 1).ToString();

                equalPos    = -1;
                nameStart   = i + 1;
            }
        }

        if (nameStart < query.Length)
        {
            if (equalPos == -1)
                dict[query[nameStart..].ToString()] = null;
            else
                dict[query[nameStart..equalPos].ToString()] = query.Slice(equalPos + 1, query.Length - equalPos - 1).ToString();
        }

        return dict;
    }

    public unsafe static string BytePtrToStringUTF8(byte* bytePtr)
        => Marshal.PtrToStringUTF8((nint)bytePtr);

    public static System.Windows.Media.Color WinFormsToWPFColor(System.Drawing.Color sColor)
        => System.Windows.Media.Color.FromArgb(sColor.A, sColor.R, sColor.G, sColor.B);
    public static System.Drawing.Color WPFToWinFormsColor(System.Windows.Media.Color wColor)
        => System.Drawing.Color.FromArgb(wColor.A, wColor.R, wColor.G, wColor.B);

    public static System.Windows.Media.Color VorticeToWPFColor(Vortice.Mathematics.Color sColor)
        => System.Windows.Media.Color.FromArgb(sColor.A, sColor.R, sColor.G, sColor.B);
    public static Vortice.Mathematics.Color WPFToVorticeColor(System.Windows.Media.Color wColor)
        => new(wColor.R, wColor.G, wColor.B, wColor.A);
    public static VideoColor WPFToVideoColor(System.Windows.Media.Color wColor)
    {
        return new()
        {
            Rgba = new()
            {
                R = wColor.R / 255.0f,
                G = wColor.G / 255.0f,
                B = wColor.B / 255.0f,
                A = wColor.A / 255.0f
            }
        };
    }
        

    public static readonly double SWFREQ_TO_TICKS = 10000000.0 / Stopwatch.Frequency;
    public static string ToHexadecimal(byte[] bytes)
    {
        StringBuilder hexBuilder = new();
        for (int i = 0; i < bytes.Length; i++)
            hexBuilder.Append(bytes[i].ToString("x2"));

        return hexBuilder.ToString();
    }
    public static int GCD(int a, int b) => b == 0 ? a : GCD(b, a % b);
    public static void Log(string msg) { try { Debug.WriteLine($"{DateTime.Now:HH.mm.ss.fff} | {msg}"); } catch (Exception) { Debug.WriteLine($"[............] [MediaFramework] {msg}"); } }

    [GeneratedRegex("[^a-z0-9]extended", RegexOptions.IgnoreCase)]
    private static partial Regex RxExtended();
    [GeneratedRegex("[^a-z0-9]directors.cut", RegexOptions.IgnoreCase)]
    private static partial Regex RxDirectorsCut();
    [GeneratedRegex(@"(^|[^a-z0-9])(s|season)[^a-z0-9]*(?<season>[0-9]{1,2})[^a-z0-9]*(e|episode|part)[^a-z0-9]*(?<episode>[0-9]{1,2})($|[^a-z0-9])", RegexOptions.IgnoreCase)]

    // s|season 01 ... e|episode|part 01
    private static partial Regex RxSeasonEpisode1();
    [GeneratedRegex(@"(^|[^a-z0-9])(?<season>[0-9]{1,2})x(?<episode>[0-9]{1,2})($|[^a-z0-9])", RegexOptions.IgnoreCase)]
    // 01x01
    private static partial Regex RxSeasonEpisode2();
    // TODO: in case of single season should check only for e|episode|part 01
    [GeneratedRegex(@"(^|[^a-z0-9])(episode|part)[^a-z0-9]*(?<episode>[0-9]{1,2})($|[^a-z0-9])", RegexOptions.IgnoreCase)]
    private static partial Regex RxEpisodePart();
    [GeneratedRegex("[^a-z0-9]brrip", RegexOptions.IgnoreCase)]
    private static partial Regex RxBrrip();

    [GeneratedRegex("[^a-z0-9][0-9]{3,4}p", RegexOptions.IgnoreCase)]
    private static partial Regex RxResolution();
    [GeneratedRegex(@"[^a-z0-9](?<year>(19|20)[0-9][0-9])($|[^a-z0-9])", RegexOptions.IgnoreCase)]
    private static partial Regex RxYear();
    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex RxSpaces();
    [GeneratedRegex(@"[^a-z0-9]$", RegexOptions.IgnoreCase)]
    private static partial Regex RxNonAlphaNumeric();

    #region Temp Transfer (v4)
    #nullable enable
    static string metaSpaces = new(' ',"[Metadata] ".Length);
    public static string GetDumpMetadata(Dictionary<string, string>? metadata, string? exclude = null)
    {
        if (metadata == null || metadata.Count == 0)
            return "";

        int maxLen = 0;
        foreach(var item in metadata)
            if (item.Key.Length > maxLen && item.Key != exclude)
                maxLen = item.Key.Length;

        string dump = "";
        int i = 1;
        foreach(var item in metadata)
        {
            if (item.Key == exclude)
            {
                i++;
                continue;
            }

            if (i == metadata.Count)
                dump += $"{item.Key.PadRight(maxLen)}: {item.Value}";
            else
                dump += $"{item.Key.PadRight(maxLen)}: {item.Value}\r\n\t{metaSpaces}";

            i++;
        }

        if (dump == "")
            return "";

        return $"\t[Metadata] {dump}";
    }
    public static string TicksToTime(long ticks)
    {
        if (ticks == NoTs)
            return "-";

        if (ticks == 0)
            return "00:00:00.000";

        return TsToTime(TimeSpan.FromTicks(ticks)); // TimeSpan.FromTicks(ticks).ToString("g");
    }
    public static string McsToTime(long micro)
    {
        if (micro == NoTs)
            return "-";

        if (micro == 0)
            return "00:00:00.000";

        return TsToTime(TimeSpan.FromMicroseconds(micro));
    }
    public static string TsToTime(TimeSpan ts)
    {
        if (ts.Ticks > 0)
        {
            if (ts.TotalDays < 1)
                return ts.ToString(@"hh\:mm\:ss\.fff");
            else
                return ts.ToString(@"d\-hh\:mm\:ss\.fff");
        }

        if (ts.TotalDays > -1)
            return ts.ToString(@"\-hh\:mm\:ss\.fff");
        else
            return ts.ToString(@"\-d\-hh\:mm\:ss\.fff");
    }
    public static string DoubleToTimeMini(double d) => d.ToString("#.000", CultureInfo.InvariantCulture);
    public static string TicksToTimeMini(long ticks)
    {
        if (ticks == NoTs)
            return "-";

        if (ticks == 0)
            return "00.000";

        return TsToTimeMini(TimeSpan.FromTicks(ticks));
    }
    static string TsToTimeMini(TimeSpan ts)
    {
        if (ts.Ticks > 0)
        {
            if (ts.TotalMinutes < 1)
                return ts.ToString(@"ss\.fff");
            else if (ts.TotalHours < 1)
                return ts.ToString(@"mm\:ss\.fff");
            else if (ts.TotalDays < 1)
                return ts.ToString(@"hh\:mm\:ss\.fff");
            else
                return ts.ToString(@"d\-hh\:mm\:ss\.fff");
        }
        
        if (ts.TotalMinutes > -1)
            return ts.ToString(@"\-ss\.fff");
        else if (ts.TotalHours > -1)
            return ts.ToString(@"\-mm\:ss\.fff");
        else if (ts.TotalDays > -1)
            return ts.ToString(@"\-hh\:mm\:ss\.fff");
        else
            return ts.ToString(@"\-d\-hh\:mm\:ss\.fff");
    }
    public static List<T> GetFlagsAsList<T>(T value) where T : Enum
    {
        List<T> values = [];

        var enumValues = Enum.GetValuesAsUnderlyingType(typeof(T));
        //var enumValues = Enum.GetValues(typeof(T)); // breaks AOT?

        foreach(T flag in enumValues)
            if (value.HasFlag(flag) && flag.ToString() != "None")
                values.Add(flag);

        return values;
    }
    public static string? GetFlagsAsString<T>(T value, string separator = " | ") where T : Enum
    {
        string? ret = null;
        List<T> values = GetFlagsAsList(value);

        if (values.Count == 0)
            return ret;

        for (int i = 0; i < values.Count - 1; i++)
            ret += values[i] + separator;

        return ret + values[^1];
    }
    public unsafe static string GetFourCCString(uint fourcc)
    {
        byte* t1 = (byte*)av_mallocz(AV_FOURCC_MAX_STRING_SIZE);
        av_fourcc_make_string(t1, fourcc);
        string ret = BytePtrToStringUTF8(t1)!;
        av_free(t1);
        return ret;
    }
    #nullable disable
    #endregion

    #region Security helpers (HC-01 / HC-05 / HC-35)
    #nullable enable
    /// <summary>
    /// True only for a well-formed absolute http/https URL that is safe to interpolate into a
    /// quoted Windows process argument. Rejects '"' and '\' (which break out of the surrounding
    /// quotes when parsed by CommandLineToArgvW) and any control characters. A legitimate URL
    /// percent-encodes all of these, so no valid input is lost.
    /// </summary>
    public static bool IsSafeProcessUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;

        foreach (char c in url)
            if (c == '"' || c == '\\' || char.IsControl(c))
                return false;

        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
            return false;

        return uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase)
            || uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Resolves <paramref name="untrustedName"/> to a path directly inside <paramref name="baseDir"/>,
    /// stripping any directory components (path traversal, absolute paths) it may carry. Returns null
    /// when the name is empty, is "."/".." or would resolve outside <paramref name="baseDir"/>.
    /// </summary>
    public static string? GetSafeFileNameChildPath(string baseDir, string? untrustedName)
    {
        if (string.IsNullOrEmpty(baseDir) || string.IsNullOrEmpty(untrustedName))
            return null;

        string fileName = Path.GetFileName(untrustedName);
        if (string.IsNullOrEmpty(fileName) || fileName == "." || fileName == "..")
            return null;

        string baseFull = Path.GetFullPath(baseDir);
        string full     = Path.GetFullPath(Path.Combine(baseFull, fileName));

        string baseWithSep = baseFull.EndsWith(Path.DirectorySeparatorChar)
            ? baseFull
            : baseFull + Path.DirectorySeparatorChar;

        if (!full.StartsWith(baseWithSep, StringComparison.OrdinalIgnoreCase))
            return null;

        return full;
    }

    /// <summary>
    /// Copies <paramref name="text"/> into a char[] with an explicit trailing '\0'. Native consumers of
    /// CF_UNICODETEXT require the null terminator; a raw ToCharArray() omits it and leaves a garbage tail.
    /// </summary>
    public static char[] ToNullTerminatedUtf16(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        char[] buffer = new char[text.Length + 1];
        text.CopyTo(0, buffer, 0, text.Length);
        buffer[text.Length] = '\0';
        return buffer;
    }
    #nullable disable
    #endregion

    public static string TruncateString(string str, int maxLength, string suffix = "...")
    {
        if (string.IsNullOrEmpty(str))
            return str;

        if (str.Length <= maxLength)
            return str;

        int availableLength = maxLength - suffix.Length;

        if (availableLength <= 0)
        {
            return suffix.Substring(0, Math.Min(maxLength, suffix.Length));
        }

        return str.Substring(0, availableLength) + suffix;
    }

    // TODO: L: move to app, using event
    public static void PlayCompletionSound()
    {
        string soundPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets/completion.mp3");

        if (!File.Exists(soundPath))
        {
            return;
        }

        UI(() =>
        {
            try
            {
                // play completion sound
                System.Windows.Media.MediaPlayer mp = new();
                mp.Open(new Uri(soundPath));
                mp.Play();
            }
            catch
            {
                // ignored
            }
        });
    }

    public static string CommandToText(this Command cmd)
    {
        if (cmd.TargetFilePath.Any(char.IsWhiteSpace))
        {
            return $"& \"{cmd.TargetFilePath}\" {cmd.Arguments}";
        }

        return cmd.ToString();
    }
}
