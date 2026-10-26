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
    public const string DefaultSubtitleBackgroundColor = "#000000";
    public const int MinSubtitleWidthPercent = 1;
    public const int MaxSubtitleWidthPercent = 100;
    public const int DefaultSubtitleWidthPercent = 100;
    public const double MinSubtitleMaxWidth = 0;
    public const double MaxSubtitleMaxWidth = 4000;
    public const double DefaultSubtitleMaxWidth = 0;
    public const double MinSubtitlePrimarySecondaryDistance = 0;
    public const double MaxSubtitlePrimarySecondaryDistance = 200;
    public const double DefaultSubtitlePrimarySecondaryDistance = 2;
    public const string DefaultSubtitleLineAlignment = "Center";
    public const double MinSubtitleSeparatorMaxWidth = 0;
    public const double MaxSubtitleSeparatorMaxWidth = 2000;
    public const double DefaultSubtitleSeparatorMaxWidth = 280;
    public const int MinSubtitleSeparatorOpacityPercent = 0;
    public const int MaxSubtitleSeparatorOpacityPercent = 100;
    public const int DefaultSubtitleSeparatorOpacityPercent = 30;
    public const string DefaultSubtitlePositionAlignment = "Bottom";

    // F-13 Subtitles Sidebar (WPF parity; defaults from LLPlayer/Services/AppConfig.cs:182-228). The sidebar has its
    // own typography and padding so it never changes the video overlay font. Numeric bounds are the approved safe
    // limits: width 220-900, padding 0-48 DIP, font 8-72 DIP.
    public const double DefaultSidebarWidth = 360;
    public const double MinSidebarWidth = 220;
    public const double MaxSidebarWidth = 900;
    public const bool DefaultSidebarLeft = false;
    public const bool DefaultSidebarTextMask = false;
    public const bool DefaultSidebarShowOriginalText = false;
    public const int MinSidebarSubPadding = 0;
    public const int MaxSidebarSubPadding = 48;
    public const int DefaultSidebarSubPadding = 5;
    public const double MinSidebarFontSize = 8;
    public const double MaxSidebarFontSize = 72;
    public const double DefaultSidebarFontSize = 16;
    public const string DefaultSidebarFontFamily = "Segoe UI";
    public const string DefaultSidebarFontWeight = "Normal";

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
    /// <summary>Opaque background RGB shared by both text subtitle overlays in canonical #RRGGBB format.</summary>
    public string SubtitleBackgroundColor { get; set; } = DefaultSubtitleBackgroundColor;
    /// <summary>Primary text subtitle background opacity in whole percent; zero draws no background.</summary>
    public int SubtitleBackgroundOpacityPercent { get; set; }
    /// <summary>Secondary text subtitle background opacity, independent of font inheritance.</summary>
    public int SecondarySubtitleBackgroundOpacityPercent { get; set; }
    /// <summary>Text subtitle box width as a percentage (1..100) of the padded video width.</summary>
    public int SubtitleWidthPercent { get; set; } = DefaultSubtitleWidthPercent;
    /// <summary>Optional box width cap in DIPs; zero means no cap.</summary>
    public double SubtitleMaxWidth { get; set; } = DefaultSubtitleMaxWidth;
    /// <summary>
    /// Flatten cue text with the canonical portable <see cref="FlyleafLib.SubtitleTextUtil.FlattenText"/> semantics used
    /// by WPF: dash lists keep their line breaks, other cue text becomes one line with single spaces.
    /// </summary>
    public bool SubtitleIgnoreLineBreak { get; set; }
    /// <summary>Horizontal alignment of each subtitle line: Center, Left or Right.</summary>
    public string SubtitleLineAlignment { get; set; } = DefaultSubtitleLineAlignment;
    /// <summary>Gap between the secondary and primary text lines in DIPs.</summary>
    public double SubtitlePrimarySecondaryDistance { get; set; } = DefaultSubtitlePrimarySecondaryDistance;
    public double SubtitleSeparatorMaxWidth { get; set; } = DefaultSubtitleSeparatorMaxWidth;
    public int SubtitleSeparatorOpacityPercent { get; set; } = DefaultSubtitleSeparatorOpacityPercent;
    public string SubtitlePositionAlignment { get; set; } = DefaultSubtitlePositionAlignment;
    public string SubtitlePositionAlignmentWhenDual { get; set; } = DefaultSubtitlePositionAlignment;
    public bool SubtitlesVisible { get; set; } = true;

    /// <summary>
    /// F-12 waveform overlay in the transport seek bar. Off by default (like the WPF app), so no audio decode runs
    /// and the engine keeps <c>WaveformEnabled=false</c> until the user asks for the envelope.
    /// </summary>
    public bool ShowWaveform { get; set; }

    /// <summary>App-level shortcut overrides (action name -> canonical chord, e.g. Ctrl+Shift+B).</summary>
    public Dictionary<string, string> AppShortcutOverrides { get; set; } = [];

    /// <summary>True when the profile stores an explicit replacement list for the engine key bindings; false uses the
    /// engine's built-in defaults (the list is ignored then). Additive/absent-defaulting.</summary>
    public bool EngineKeysCustomized { get; set; }
    /// <summary>Explicit engine key-binding list applied at start-up; only meaningful while <see cref="EngineKeysCustomized"/>.</summary>
    public List<EngineKeyBindingPref> EngineKeyBindings { get; set; } = [];

    public bool SidebarVisible { get; set; }
    public double SidebarWidth { get; set; } = DefaultSidebarWidth;
    /// <summary>Dock the sidebar left of the video (WPF <c>SidebarLeft</c>); false keeps it on the right.</summary>
    public bool SidebarLeft { get; set; } = DefaultSidebarLeft;
    /// <summary>Mask text of cues after the current one with underscores to avoid spoilers.</summary>
    public bool SidebarTextMask { get; set; } = DefaultSidebarTextMask;
    /// <summary>Show the original cue text instead of the translated display text; search follows the choice.</summary>
    public bool SidebarShowOriginalText { get; set; } = DefaultSidebarShowOriginalText;
    /// <summary>Uniform cue-row padding in DIP (sidebar only).</summary>
    public int SidebarSubPadding { get; set; } = DefaultSidebarSubPadding;
    /// <summary>Requested sidebar font family; resolved through the font catalog with a fallback.</summary>
    public string SidebarFontFamily { get; set; } = DefaultSidebarFontFamily;
    /// <summary>Sidebar cue-row font size in DIP (independent from <see cref="SubtitleFontSize"/>).</summary>
    public double SidebarFontSize { get; set; } = DefaultSidebarFontSize;
    /// <summary>Sidebar font weight: Normal, SemiBold or Bold.</summary>
    public string SidebarFontWeight { get; set; } = DefaultSidebarFontWeight;

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
        SubtitleBackgroundColor = TryNormalizeSubtitleTextColor(SubtitleBackgroundColor, out string backgroundColor) ? backgroundColor : DefaultSubtitleBackgroundColor;
        SubtitleBackgroundOpacityPercent = Math.Clamp(SubtitleBackgroundOpacityPercent, 0, 100);
        SecondarySubtitleBackgroundOpacityPercent = Math.Clamp(SecondarySubtitleBackgroundOpacityPercent, 0, 100);
        SubtitleWidthPercent = Math.Clamp(SubtitleWidthPercent, MinSubtitleWidthPercent, MaxSubtitleWidthPercent);
        SubtitleMaxWidth = double.IsFinite(SubtitleMaxWidth) ? Math.Clamp(SubtitleMaxWidth, MinSubtitleMaxWidth, MaxSubtitleMaxWidth) : DefaultSubtitleMaxWidth;
        SubtitlePrimarySecondaryDistance = double.IsFinite(SubtitlePrimarySecondaryDistance)
            ? Math.Clamp(SubtitlePrimarySecondaryDistance, MinSubtitlePrimarySecondaryDistance, MaxSubtitlePrimarySecondaryDistance)
            : DefaultSubtitlePrimarySecondaryDistance;
        SubtitleLineAlignment = TryNormalizeSubtitleLineAlignment(SubtitleLineAlignment, out string alignment) ? alignment : DefaultSubtitleLineAlignment;
        SubtitleSeparatorMaxWidth = double.IsFinite(SubtitleSeparatorMaxWidth)
            ? Math.Clamp(SubtitleSeparatorMaxWidth, MinSubtitleSeparatorMaxWidth, MaxSubtitleSeparatorMaxWidth)
            : DefaultSubtitleSeparatorMaxWidth;
        SubtitleSeparatorOpacityPercent = Math.Clamp(SubtitleSeparatorOpacityPercent, MinSubtitleSeparatorOpacityPercent, MaxSubtitleSeparatorOpacityPercent);
        SubtitlePositionAlignment = TryNormalizeSubtitlePositionAlignment(SubtitlePositionAlignment, out string positionAlignment) ? positionAlignment : DefaultSubtitlePositionAlignment;
        SubtitlePositionAlignmentWhenDual = TryNormalizeSubtitlePositionAlignment(SubtitlePositionAlignmentWhenDual, out string dualAlignment) ? dualAlignment : DefaultSubtitlePositionAlignment;
        SecondaryFontScale = double.IsFinite(SecondaryFontScale) ? Math.Clamp(SecondaryFontScale, 0.4, 1.5) : 0.8;
        SidebarWidth = double.IsFinite(SidebarWidth) ? Math.Clamp(SidebarWidth, MinSidebarWidth, MaxSidebarWidth) : DefaultSidebarWidth;
        SidebarSubPadding = Math.Clamp(SidebarSubPadding, MinSidebarSubPadding, MaxSidebarSubPadding);
        SidebarFontFamily = TryNormalizeSubtitleFontFamily(SidebarFontFamily, out string sidebarFamily) ? sidebarFamily : DefaultSidebarFontFamily;
        SidebarFontWeight = TryNormalizeSubtitleFontWeight(SidebarFontWeight, out string sidebarWeight) ? sidebarWeight : DefaultSidebarFontWeight;
        SidebarFontSize = double.IsFinite(SidebarFontSize)
            ? Math.Clamp(SidebarFontSize, MinSidebarFontSize, MaxSidebarFontSize)
            : DefaultSidebarFontSize;
        Theme = string.Equals(Theme, "Light", StringComparison.OrdinalIgnoreCase) ? "Light" : "Dark";
        _ = AppShortcutMap.TryNormalizeOverrides(AppShortcutOverrides, out Dictionary<string, string> shortcuts);
        AppShortcutOverrides = shortcuts;
        if (EngineKeysCustomized)
            EngineKeyBindings = global::LLPlayer.Avalonia.Services.EngineKeyBindings.NormalizePrefs(EngineKeyBindings);
        else
            EngineKeyBindings = [];
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

    /// <summary>Accepts the supported horizontal line alignments, ignoring surrounding whitespace.</summary>
    public static bool TryNormalizeSubtitleLineAlignment(string? raw, out string normalized)
    {
        normalized = "";
        string? value = raw?.Trim();
        foreach (string supported in SubtitleLayoutOptions.LineAlignments)
            if (string.Equals(value, supported, StringComparison.OrdinalIgnoreCase))
            {
                normalized = supported;
                return true;
            }
        return false;
    }

    /// <summary>Accepts the supported vertical block alignments, ignoring surrounding whitespace.</summary>
    public static bool TryNormalizeSubtitlePositionAlignment(string? raw, out string normalized)
    {
        normalized = "";
        string? value = raw?.Trim();
        foreach (string supported in SubtitleLayoutOptions.PositionAlignments)
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
