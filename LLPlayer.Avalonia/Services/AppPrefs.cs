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
    public const string DefaultSubtitleFontFamily = "";
    public const string DefaultSubtitleFontWeight = "SemiBold";
    public const string DefaultSubtitleFontStyle = "Normal";
    public const double MinSubtitleFontSize = 12;
    public const double MaxSubtitleFontSize = 96;
    public const double DefaultSubtitleFontSize = 34;
    public const double MinSubtitleBottomOffset = 0;
    public const double MaxSubtitleBottomOffset = 400;
    public const double DefaultSubtitleBottomOffset = 24;
    public const double MinSubtitleOutlineThickness = 0;
    public const double MaxSubtitleOutlineThickness = 8;
    public const double DefaultSubtitleOutlineThickness = 3;
    public const double DefaultSecondarySubtitleOutlineThickness = 2.5;
    public const string DefaultSubtitleTextColor = "#FFFFFF";
    public const string DefaultSecondarySubtitleTextColor = "#E8E8E8";
    public const string DefaultSubtitleOutlineColor = "#000000";

    /// <summary>Player volume in percent (0 .. Config.Player.VolumeMax, 150 by default).</summary>
    public int Volume { get; set; } = 100;
    public bool Mute { get; set; }

    /// <summary>Primary overlay font size in DIPs; the secondary line uses <see cref="SecondaryFontScale"/> of it.</summary>
    public double SubtitleFontSize { get; set; } = DefaultSubtitleFontSize;
    public double SecondaryFontScale { get; set; } = 0.8;
    /// <summary>Requested plain family name; empty preserves the existing app Inter font.</summary>
    public string SubtitleFontFamily { get; set; } = DefaultSubtitleFontFamily;
    public string SubtitleFontWeight { get; set; } = DefaultSubtitleFontWeight;
    public string SubtitleFontStyle { get; set; } = DefaultSubtitleFontStyle;
    public string SecondarySubtitleFontFamily { get; set; } = DefaultSubtitleFontFamily;
    public string SecondarySubtitleFontWeight { get; set; } = DefaultSubtitleFontWeight;
    public string SecondarySubtitleFontStyle { get; set; } = DefaultSubtitleFontStyle;
    /// <summary>Inherit only family, weight and style; retain the secondary overrides while enabled.</summary>
    public bool SecondarySubtitleInheritsFont { get; set; } = true;
    /// <summary>Distance of the text subtitle overlay from the bottom edge in DIPs.</summary>
    public double SubtitleBottomOffset { get; set; } = DefaultSubtitleBottomOffset;
    /// <summary>Primary text subtitle outline thickness in DIPs; zero leaves the existing shadow.</summary>
    public double SubtitleOutlineThickness { get; set; } = DefaultSubtitleOutlineThickness;
    /// <summary>Secondary text subtitle outline thickness in DIPs; zero leaves the existing shadow.</summary>
    public double SecondarySubtitleOutlineThickness { get; set; } = DefaultSecondarySubtitleOutlineThickness;
    /// <summary>Opaque primary text subtitle fill in canonical #RRGGBB format.</summary>
    public string SubtitleTextColor { get; set; } = DefaultSubtitleTextColor;
    /// <summary>Opaque secondary text subtitle fill in canonical #RRGGBB format.</summary>
    public string SecondarySubtitleTextColor { get; set; } = DefaultSecondarySubtitleTextColor;
    /// <summary>Opaque outline color shared by both text subtitle overlays in canonical #RRGGBB format.</summary>
    public string SubtitleOutlineColor { get; set; } = DefaultSubtitleOutlineColor;
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
        SubtitleFontFamily = TryNormalizeSubtitleFontFamily(SubtitleFontFamily, out string family) ? family : DefaultSubtitleFontFamily;
        SubtitleFontWeight = TryNormalizeSubtitleFontWeight(SubtitleFontWeight, out string weight) ? weight : DefaultSubtitleFontWeight;
        SubtitleFontStyle = TryNormalizeSubtitleFontStyle(SubtitleFontStyle, out string style) ? style : DefaultSubtitleFontStyle;
        SecondarySubtitleFontFamily = TryNormalizeSubtitleFontFamily(SecondarySubtitleFontFamily, out string secondaryFamily) ? secondaryFamily : DefaultSubtitleFontFamily;
        SecondarySubtitleFontWeight = TryNormalizeSubtitleFontWeight(SecondarySubtitleFontWeight, out string secondaryWeight) ? secondaryWeight : DefaultSubtitleFontWeight;
        SecondarySubtitleFontStyle = TryNormalizeSubtitleFontStyle(SecondarySubtitleFontStyle, out string secondaryStyle) ? secondaryStyle : DefaultSubtitleFontStyle;
        SubtitleFontSize = double.IsFinite(SubtitleFontSize) ? Math.Clamp(SubtitleFontSize, MinSubtitleFontSize, MaxSubtitleFontSize) : DefaultSubtitleFontSize;
        SubtitleBottomOffset = double.IsFinite(SubtitleBottomOffset) ? Math.Clamp(SubtitleBottomOffset, MinSubtitleBottomOffset, MaxSubtitleBottomOffset) : DefaultSubtitleBottomOffset;
        SubtitleOutlineThickness = double.IsFinite(SubtitleOutlineThickness) ? Math.Clamp(SubtitleOutlineThickness, MinSubtitleOutlineThickness, MaxSubtitleOutlineThickness) : DefaultSubtitleOutlineThickness;
        SecondarySubtitleOutlineThickness = double.IsFinite(SecondarySubtitleOutlineThickness) ? Math.Clamp(SecondarySubtitleOutlineThickness, MinSubtitleOutlineThickness, MaxSubtitleOutlineThickness) : DefaultSecondarySubtitleOutlineThickness;
        SubtitleTextColor = TryNormalizeSubtitleTextColor(SubtitleTextColor, out string primaryColor) ? primaryColor : DefaultSubtitleTextColor;
        SecondarySubtitleTextColor = TryNormalizeSubtitleTextColor(SecondarySubtitleTextColor, out string secondaryColor) ? secondaryColor : DefaultSecondarySubtitleTextColor;
        SubtitleOutlineColor = TryNormalizeSubtitleTextColor(SubtitleOutlineColor, out string outlineColor) ? outlineColor : DefaultSubtitleOutlineColor;
        SecondaryFontScale = double.IsFinite(SecondaryFontScale) ? Math.Clamp(SecondaryFontScale, 0.4, 1.5) : 0.8;
        SidebarWidth = double.IsFinite(SidebarWidth) ? Math.Clamp(SidebarWidth, 220, 900) : 360;
        Theme = string.Equals(Theme, "Light", StringComparison.OrdinalIgnoreCase) ? "Light" : "Dark";
        RecentFiles = (RecentFiles ?? []).Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.Ordinal).Take(MaxRecentFiles).ToList();
        if (string.IsNullOrWhiteSpace(WordTranslationService))
            WordTranslationService = "GoogleV1";
        return this;
    }

    /// <summary>Accepts a single plain family name, never a font URI, path or fallback expression.</summary>
    public static bool TryNormalizeSubtitleFontFamily(string? raw, out string normalized)
    {
        normalized = "";
        if (raw == null || raw.Any(char.IsControl))
            return false;
        string value = raw.Trim();
        if (value.IndexOfAny([':', '/', '\\', '#', ',', '$', '<', '>', '"', '|', '*', '?']) >= 0
            || value is "." or "..")
            return false;
        normalized = value;
        return true;
    }

    public static bool TryNormalizeSubtitleFontWeight(string? raw, out string normalized)
    {
        normalized = "";
        string? value = raw?.Trim();
        foreach (string supported in new[] { "Normal", "SemiBold", "Bold" })
            if (string.Equals(value, supported, StringComparison.OrdinalIgnoreCase))
            {
                normalized = supported;
                return true;
            }
        return false;
    }

    public static bool TryNormalizeSubtitleFontStyle(string? raw, out string normalized)
    {
        normalized = "";
        string? value = raw?.Trim();
        foreach (string supported in new[] { "Normal", "Italic" })
            if (string.Equals(value, supported, StringComparison.OrdinalIgnoreCase))
            {
                normalized = supported;
                return true;
            }
        return false;
    }

    /// <summary>Accepts only an opaque RGB hex string, ignoring surrounding whitespace.</summary>
    public static bool TryNormalizeSubtitleTextColor(string? raw, out string normalized)
    {
        normalized = "";
        string? value = raw?.Trim();
        if (value is not { Length: 7 } || value[0] != '#')
            return false;
        foreach (char digit in value.AsSpan(1))
            if (digit is not (>= '0' and <= '9' or >= 'A' and <= 'F' or >= 'a' and <= 'f'))
                return false;
        normalized = value.ToUpperInvariant();
        return true;
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
