using System.Text.Json;
using System.Text.Json.Serialization;

namespace LLPlayer.Avalonia.Services;

/// <summary>
/// Persisted preferences of the Linux app (JSON in the XDG config dir, <see cref="AppPaths.PrefsFile"/>).
/// Unknown / out-of-range values are clamped by <see cref="Normalize"/> so a hand-edited file cannot break start-up.
/// </summary>
public sealed class AppPrefs
{
    public const int MaxRecentFiles = 10;
    public const double MinSubtitleFontSize = 12;
    public const double MaxSubtitleFontSize = 96;
    public const double DefaultSubtitleFontSize = 34;

    /// <summary>Player volume in percent (0 .. Config.Player.VolumeMax, 150 by default).</summary>
    public int Volume { get; set; } = 100;
    public bool Mute { get; set; }

    /// <summary>Primary overlay font size in DIPs; the secondary line uses <see cref="SecondaryFontScale"/> of it.</summary>
    public double SubtitleFontSize { get; set; } = DefaultSubtitleFontSize;
    public double SecondaryFontScale { get; set; } = 0.8;
    public bool SubtitlesVisible { get; set; } = true;

    public bool SidebarVisible { get; set; }
    public double SidebarWidth { get; set; } = 360;

    /// <summary>"Dark" (default, like the WPF app) or "Light".</summary>
    public string Theme { get; set; } = "Dark";

    public List<string> RecentFiles { get; set; } = [];
    public string? LastFolder { get; set; }

    /// <summary>
    /// Word-click translation service (a FlyleafLib <c>TranslateServiceType</c> name, default GoogleV1 as in the WPF
    /// app) or "Off" to disable online lookups.
    /// </summary>
    public string WordTranslationService { get; set; } = "GoogleV1";

    /// <summary>Optional target language (FlyleafLib <c>TargetLanguage</c> name); null = system language.</summary>
    public string? TranslateTargetLanguage { get; set; }

    public void AddRecent(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        RecentFiles.RemoveAll(p => string.Equals(p, path, StringComparison.Ordinal));
        RecentFiles.Insert(0, path);
        if (RecentFiles.Count > MaxRecentFiles)
            RecentFiles.RemoveRange(MaxRecentFiles, RecentFiles.Count - MaxRecentFiles);
    }

    public AppPrefs Normalize()
    {
        Volume = Math.Clamp(Volume, 0, 1000);
        SubtitleFontSize = double.IsFinite(SubtitleFontSize) ? Math.Clamp(SubtitleFontSize, MinSubtitleFontSize, MaxSubtitleFontSize) : DefaultSubtitleFontSize;
        SecondaryFontScale = double.IsFinite(SecondaryFontScale) ? Math.Clamp(SecondaryFontScale, 0.4, 1.5) : 0.8;
        SidebarWidth = double.IsFinite(SidebarWidth) ? Math.Clamp(SidebarWidth, 220, 900) : 360;
        Theme = string.Equals(Theme, "Light", StringComparison.OrdinalIgnoreCase) ? "Light" : "Dark";
        RecentFiles = (RecentFiles ?? []).Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.Ordinal).Take(MaxRecentFiles).ToList();
        if (string.IsNullOrWhiteSpace(WordTranslationService))
            WordTranslationService = "GoogleV1";
        return this;
    }
}

/// <summary>Loads / saves <see cref="AppPrefs"/> atomically (temp file + rename).</summary>
public sealed class PrefsStore(string path)
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public string Path { get; } = path;

    /// <summary>Last load problem (corrupt file), shown to the user once; null when the load was clean.</summary>
    public string? LoadWarning { get; private set; }

    public AppPrefs Load()
    {
        LoadWarning = null;
        if (!File.Exists(Path))
            return new AppPrefs().Normalize();

        try
        {
            AppPrefs? prefs = JsonSerializer.Deserialize<AppPrefs>(File.ReadAllText(Path), JsonOptions);
            return (prefs ?? new AppPrefs()).Normalize();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            LoadWarning = $"Preferences could not be read ({ex.Message}); defaults are used and the file was kept as .bad";
            try
            {
                File.Copy(Path, Path + ".bad", overwrite: true);
            }
            catch
            {
                // ignored
            }
            return new AppPrefs().Normalize();
        }
    }

    public void Save(AppPrefs prefs)
    {
        string? dir = System.IO.Path.GetDirectoryName(Path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        string tmp = Path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(prefs, JsonOptions));
        File.Move(tmp, Path, overwrite: true);
    }
}
