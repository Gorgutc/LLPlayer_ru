using System.Globalization;

namespace LLPlayer.Avalonia;

public sealed class AppOptionsException(string message) : Exception(message);

/// <summary>
/// Command line of the Linux app. User options mirror the WPF app (one media path / URL opened at start); the rest are
/// developer options used for screenshots and visual review.
/// </summary>
public sealed class AppOptions
{
    public const string Usage =
        """
        Usage: LLPlayer.Avalonia [options] [media path or URL]

        Options:
          --ffmpeg-dir <dir>        folder with the FFmpeg 8 shared libraries (default: $LLPLAYER_FFMPEG_DIR,
                                    else <app folder>/FFmpeg)
          --sub <file>              subtitles file to open with the media (primary slot)
          --theme <dark|light>      theme for this run (does not change the saved preference)
          -h, --help                show this help
          --version                 print the version

        Developer options:
          --theme-gallery           open the theme gallery (every styled control, dark and light) instead of the player
          --sidebar                 show the subtitles sidebar at start
          --seek <seconds>          start position of the media
          --screenshot <file.png>   render the window to a PNG after --screenshot-delay seconds, then exit
          --screenshot-delay <s>    delay before the screenshot (default 3)
          --dev-word-popup          before the screenshot, pause and open the word popup on the first subtitle word
        """;

    public string? MediaPath { get; private set; }
    public string? SubtitlesPath { get; private set; }
    public string? FFmpegDir { get; private set; }
    public string? Theme { get; private set; }
    public bool ThemeGallery { get; private set; }
    public bool ShowSidebar { get; private set; }
    public double? SeekSeconds { get; private set; }
    public string? ScreenshotPath { get; private set; }
    public double ScreenshotDelaySeconds { get; private set; } = 3;
    public bool DevWordPopup { get; private set; }
    public bool ShowHelp { get; private set; }
    public bool ShowVersion { get; private set; }

    public static AppOptions Parse(IReadOnlyList<string> args)
    {
        AppOptions o = new();
        for (int i = 0; i < args.Count; i++)
        {
            string a = args[i];
            switch (a)
            {
                case "-h":
                case "--help":
                    o.ShowHelp = true;
                    break;
                case "--version":
                    o.ShowVersion = true;
                    break;
                case "--ffmpeg-dir":
                    o.FFmpegDir = Value(args, ref i);
                    break;
                case "--sub":
                    o.SubtitlesPath = Value(args, ref i);
                    break;
                case "--theme":
                    string theme = Value(args, ref i).ToLowerInvariant();
                    if (theme is not ("dark" or "light"))
                        throw new AppOptionsException($"--theme must be 'dark' or 'light' (got '{theme}')");
                    o.Theme = theme == "dark" ? "Dark" : "Light";
                    break;
                case "--theme-gallery":
                    o.ThemeGallery = true;
                    break;
                case "--sidebar":
                    o.ShowSidebar = true;
                    break;
                case "--seek":
                    o.SeekSeconds = Number(a, Value(args, ref i));
                    break;
                case "--screenshot":
                    o.ScreenshotPath = Path.GetFullPath(Value(args, ref i));
                    break;
                case "--screenshot-delay":
                    o.ScreenshotDelaySeconds = Number(a, Value(args, ref i));
                    break;
                case "--dev-word-popup":
                    o.DevWordPopup = true;
                    break;
                default:
                    if (a.StartsWith("--", StringComparison.Ordinal))
                        throw new AppOptionsException($"Unknown option '{a}'. Use --help.");
                    if (o.MediaPath != null)
                        throw new AppOptionsException($"Only one media path or URL is accepted (got '{o.MediaPath}' and '{a}').");
                    o.MediaPath = a;
                    break;
            }
        }

        return o;
    }

    static string Value(IReadOnlyList<string> args, ref int i)
    {
        if (i + 1 >= args.Count)
            throw new AppOptionsException($"{args[i]} needs a value");
        return args[++i];
    }

    static double Number(string option, string value)
    {
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) || d < 0 || double.IsInfinity(d))
            throw new AppOptionsException($"{option} needs a non-negative number (got '{value}')");
        return d;
    }
}
