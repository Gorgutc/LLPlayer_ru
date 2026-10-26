using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using FlyleafLib.MediaPlayer;
using LLPlayer.Avalonia.Services;
using System.Globalization;
using System.Collections.ObjectModel;
using WpfKey = System.Windows.Input.Key;

namespace LLPlayer.Avalonia.ViewModels;

public enum SettingsLink { Translation, Recognition, Shortcuts }

/// <summary>Detached preferences draft. Persistence and live application are owned by the main view model.</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    readonly Action<SettingsViewModel> commit;
    readonly SubtitleFontCatalog fonts = new();
    public ObservableCollection<ShortcutEditRow> ShortcutRows { get; } = [];
    public IReadOnlyDictionary<string, string> AppShortcuts { get; private set; } = new Dictionary<string, string>();
    public ObservableCollection<EngineKeyEditRow> EngineKeyRows { get; } = [];
    /// <summary>True while the engine editor has at least one draft row (drives the empty-state text).</summary>
    public bool HasEngineKeyRows => EngineKeyRows.Count > 0;
    /// <summary>Validated engine key list built by Save (committed atomically with the other preferences).</summary>
    public IReadOnlyList<EngineKeyBindingPref> EngineKeyBindingPrefs { get; private set; } = [];
    /// <summary>False means the profile keeps the engine's built-in defaults.</summary>
    public bool EngineKeysCustomized { get; private set; }
    ObservableCollection<string>? subtitleFontFamilies;
    public SettingsViewModel(AppPrefs current, Action<SettingsViewModel> commit, BitmapSubtitleRuntimeState bitmapState)
    {
        this.commit = commit;
        foreach (string action in AppShortcutMap.ActionNames)
        {
            AppShortcut first = AppShortcutMap.Defaults.First(s => s.ActionName == action);
            string defaultChord = AppShortcutMap.FormatChord(first.Key, first.Ctrl, first.Alt, first.Shift);
            string chord = current.AppShortcutOverrides.TryGetValue(action, out string? saved)
                && AppShortcutMap.TryParseChord(saved, out WpfKey key, out bool ctrl, out bool alt, out bool shift)
                ? AppShortcutMap.FormatChord(key, ctrl, alt, shift)
                : defaultChord;
            ShortcutRows.Add(new ShortcutEditRow(action, first.Description, defaultChord, chord));
        }
        IReadOnlyList<EngineKeyBindingInfo> engineBindings = current.EngineKeysCustomized
            ? EngineKeyBindings.FromPrefs(current.EngineKeyBindings)
            : EngineKeyBindings.Defaults();
        foreach (EngineKeyBindingInfo binding in engineBindings)
            EngineKeyRows.Add(new EngineKeyEditRow(binding, DeleteEngineKeyRow));
        Theme = current.Theme;
        SubtitleFontFamily = current.SubtitleFontFamily;
        SubtitleFontWeight = current.SubtitleFontWeight;
        SubtitleFontStyle = current.SubtitleFontStyle;
        SecondarySubtitleFontFamily = current.SecondarySubtitleFontFamily;
        SecondarySubtitleFontWeight = current.SecondarySubtitleFontWeight;
        SecondarySubtitleFontStyle = current.SecondarySubtitleFontStyle;
        SecondarySubtitleInheritsFont = current.SecondarySubtitleInheritsFont;
        SubtitleFontSize = current.SubtitleFontSize;
        SecondaryFontScale = current.SecondaryFontScale;
        SubtitleBottomOffset = current.SubtitleBottomOffset;
        // A zero offset matches the generated property's initial value and does not invoke its change hook.
        SubtitleBottomOffsetText = current.SubtitleBottomOffset.ToString(NumberFormat);
        SubtitleOutlineThickness = current.SubtitleOutlineThickness;
        SecondarySubtitleOutlineThickness = current.SecondarySubtitleOutlineThickness;
        SubtitleTextColor = current.SubtitleTextColor;
        SecondarySubtitleTextColor = current.SecondarySubtitleTextColor;
        SubtitleOutlineColor = current.SubtitleOutlineColor;
        SubtitleBackgroundColor = current.SubtitleBackgroundColor;
        SubtitleBackgroundOpacityPercent = current.SubtitleBackgroundOpacityPercent;
        SecondarySubtitleBackgroundOpacityPercent = current.SecondarySubtitleBackgroundOpacityPercent;
        // Default zero values also need raw editor text when no generated change hook runs.
        SubtitleBackgroundOpacityPercentText = current.SubtitleBackgroundOpacityPercent.ToString(NumberFormat);
        SecondarySubtitleBackgroundOpacityPercentText = current.SecondarySubtitleBackgroundOpacityPercent.ToString(NumberFormat);
        // Loaded zero thickness also needs text when the generated change hook does not run.
        SubtitleOutlineThicknessText = current.SubtitleOutlineThickness.ToString(NumberFormat);
        SecondarySubtitleOutlineThicknessText = current.SecondarySubtitleOutlineThickness.ToString(NumberFormat);
        SubtitlesVisible = current.SubtitlesVisible;
        SidebarVisible = current.SidebarVisible;
        SidebarWidth = current.SidebarWidth;
        // Sidebar-only appearance (F-13): mask/original choice, padding and typography. Raw text mirrors are seeded
        // even for default values because the generated change hook does not run for an unchanged initial value.
        SidebarLeft = current.SidebarLeft;
        SidebarTextMask = current.SidebarTextMask;
        SidebarShowOriginalText = current.SidebarShowOriginalText;
        SidebarSubPadding = current.SidebarSubPadding;
        SidebarSubPaddingText = current.SidebarSubPadding.ToString(NumberFormat);
        SidebarFontFamily = current.SidebarFontFamily;
        SidebarFontWeight = current.SidebarFontWeight;
        SidebarFontSize = current.SidebarFontSize;
        SidebarFontSizeText = current.SidebarFontSize.ToString(NumberFormat);
        SubtitleWidthPercent = current.SubtitleWidthPercent;
        SubtitleWidthPercentText = current.SubtitleWidthPercent.ToString(NumberFormat);
        SubtitleMaxWidth = current.SubtitleMaxWidth;
        SubtitleMaxWidthText = current.SubtitleMaxWidth.ToString(NumberFormat);
        SubtitlePrimarySecondaryDistance = current.SubtitlePrimarySecondaryDistance;
        SubtitlePrimarySecondaryDistanceText = current.SubtitlePrimarySecondaryDistance.ToString(NumberFormat);
        SubtitleIgnoreLineBreak = current.SubtitleIgnoreLineBreak;
        SubtitleLineAlignment = current.SubtitleLineAlignment;
        SubtitleSeparatorMaxWidthText = current.SubtitleSeparatorMaxWidth.ToString(NumberFormat);
        SubtitleSeparatorOpacityPercentText = current.SubtitleSeparatorOpacityPercent.ToString(NumberFormat);
        SubtitleSeparatorMaxWidth = current.SubtitleSeparatorMaxWidth;
        SubtitleSeparatorOpacityPercent = current.SubtitleSeparatorOpacityPercent;
        SubtitlePositionAlignment = current.SubtitlePositionAlignment;
        SubtitlePositionAlignmentWhenDual = current.SubtitlePositionAlignmentWhenDual;
        PrimaryBitmapScale = bitmapState.PrimaryScale;
        SecondaryBitmapScale = bitmapState.SecondaryScale;
        PrimaryBitmapPosition = bitmapState.PrimaryPosition;
        PrimaryBitmapScaleText = bitmapState.PrimaryScale.ToString(NumberFormat);
        SecondaryBitmapScaleText = bitmapState.SecondaryScale.ToString(NumberFormat);
        PrimaryBitmapPositionText = bitmapState.PrimaryPosition.ToString(NumberFormat);
    }

    public static IReadOnlyList<string> Themes { get; } = ["Dark", "Light"];
    // NumericUpDown's framework default may have been initialized under another culture.
    public NumberFormatInfo NumberFormat { get; } = NumberFormatInfo.ReadOnly((NumberFormatInfo)CultureInfo.CurrentCulture.NumberFormat.Clone());
    [ObservableProperty] public partial string Theme { get; set; } = "Dark";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(PreviewSecondarySubtitleFontSize))]
    public partial double SubtitleFontSize { get; set; }
    [ObservableProperty] public partial string SubtitleFontFamily { get; set; } = AppPrefs.DefaultSubtitleFontFamily;
    [ObservableProperty] public partial string SubtitleFontWeight { get; set; } = AppPrefs.DefaultSubtitleFontWeight;
    [ObservableProperty] public partial string SubtitleFontStyle { get; set; } = AppPrefs.DefaultSubtitleFontStyle;
    [ObservableProperty] public partial string SecondarySubtitleFontFamily { get; set; } = AppPrefs.DefaultSubtitleFontFamily;
    [ObservableProperty] public partial string SecondarySubtitleFontWeight { get; set; } = AppPrefs.DefaultSubtitleFontWeight;
    [ObservableProperty] public partial string SecondarySubtitleFontStyle { get; set; } = AppPrefs.DefaultSubtitleFontStyle;
    [ObservableProperty] public partial bool SecondarySubtitleInheritsFont { get; set; } = true;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(PreviewSecondarySubtitleFontSize))]
    public partial double SecondaryFontScale { get; set; }
    [ObservableProperty] public partial double SubtitleBottomOffset { get; set; }
    [ObservableProperty] public partial double SubtitleOutlineThickness { get; set; }
    [ObservableProperty] public partial double SecondarySubtitleOutlineThickness { get; set; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(SubtitleTextBrush))]
    public partial string SubtitleTextColor { get; set; } = AppPrefs.DefaultSubtitleTextColor;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(SecondarySubtitleTextBrush))]
    public partial string SecondarySubtitleTextColor { get; set; } = AppPrefs.DefaultSecondarySubtitleTextColor;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(SubtitleOutlineBrush))]
    public partial string SubtitleOutlineColor { get; set; } = AppPrefs.DefaultSubtitleOutlineColor;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(SubtitleBackgroundColorBrush)), NotifyPropertyChangedFor(nameof(SubtitleBackgroundBrush)), NotifyPropertyChangedFor(nameof(SecondarySubtitleBackgroundBrush))]
    public partial string SubtitleBackgroundColor { get; set; } = AppPrefs.DefaultSubtitleBackgroundColor;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(SubtitleBackgroundBrush))]
    public partial int SubtitleBackgroundOpacityPercent { get; set; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(SecondarySubtitleBackgroundBrush))]
    public partial int SecondarySubtitleBackgroundOpacityPercent { get; set; }
    [ObservableProperty] public partial bool SubtitlesVisible { get; set; }
    public static IReadOnlyList<string> LineAlignments { get; } = SubtitleLayoutOptions.LineAlignments;
    public static IReadOnlyList<string> PositionAlignments { get; } = SubtitleLayoutOptions.PositionAlignments;
    [ObservableProperty] public partial string SubtitleSeparatorMaxWidthText { get; set; } = "";
    [ObservableProperty] public partial string SubtitleSeparatorOpacityPercentText { get; set; } = "";
    [ObservableProperty] public partial double SubtitleSeparatorMaxWidth { get; set; }
    [ObservableProperty] public partial int SubtitleSeparatorOpacityPercent { get; set; }
    [ObservableProperty] public partial string SubtitlePositionAlignment { get; set; } = AppPrefs.DefaultSubtitlePositionAlignment;
    [ObservableProperty] public partial string SubtitlePositionAlignmentWhenDual { get; set; } = AppPrefs.DefaultSubtitlePositionAlignment;
    [ObservableProperty] public partial double PrimaryBitmapScale { get; set; }
    [ObservableProperty] public partial double SecondaryBitmapScale { get; set; }
    [ObservableProperty] public partial double PrimaryBitmapPosition { get; set; }
    [ObservableProperty] public partial string PrimaryBitmapScaleText { get; set; } = "";
    [ObservableProperty] public partial string SecondaryBitmapScaleText { get; set; } = "";
    [ObservableProperty] public partial string PrimaryBitmapPositionText { get; set; } = "";

    /// <summary>The draft alignment mapped for the shared preview control.</summary>
    public TextAlignment PreviewTextAlignment => SubtitleLayoutOptions.ToTextAlignment(SubtitleLineAlignment);

    partial void OnSubtitleLineAlignmentChanged(string value) => OnPropertyChanged(nameof(PreviewTextAlignment));
    [ObservableProperty] public partial int SubtitleWidthPercent { get; set; }
    [ObservableProperty] public partial double SubtitleMaxWidth { get; set; }
    [ObservableProperty] public partial bool SubtitleIgnoreLineBreak { get; set; }
    [ObservableProperty] public partial string SubtitleLineAlignment { get; set; } = AppPrefs.DefaultSubtitleLineAlignment;
    [ObservableProperty] public partial double SubtitlePrimarySecondaryDistance { get; set; }
    [ObservableProperty] public partial string SubtitleWidthPercentText { get; set; } = "";
    [ObservableProperty] public partial string SubtitleMaxWidthText { get; set; } = "";
    [ObservableProperty] public partial string SubtitlePrimarySecondaryDistanceText { get; set; } = "";
    [ObservableProperty] public partial bool SidebarVisible { get; set; }
    [ObservableProperty] public partial double SidebarWidth { get; set; }
    [ObservableProperty] public partial bool SidebarLeft { get; set; }
    [ObservableProperty] public partial bool SidebarTextMask { get; set; }
    [ObservableProperty] public partial bool SidebarShowOriginalText { get; set; }
    [ObservableProperty] public partial int SidebarSubPadding { get; set; } = AppPrefs.DefaultSidebarSubPadding;
    [ObservableProperty] public partial string SidebarSubPaddingText { get; set; } = "";
    [ObservableProperty] public partial string SidebarFontFamily { get; set; } = AppPrefs.DefaultSidebarFontFamily;
    [ObservableProperty] public partial string SidebarFontWeight { get; set; } = AppPrefs.DefaultSidebarFontWeight;
    [ObservableProperty] public partial double SidebarFontSize { get; set; } = AppPrefs.DefaultSidebarFontSize;
    [ObservableProperty] public partial string SidebarFontSizeText { get; set; } = "";
    public static IReadOnlyList<string> SidebarFontWeights => SubtitleFontCatalog.Weights;
    /// <summary>Families for the sidebar font picker; keeps the requested family selectable even if not installed.</summary>
    public ObservableCollection<string> SidebarFontFamilies => sidebarFontFamilies ??= new(fonts.GetFamilies(SidebarFontFamily));
    ObservableCollection<string>? sidebarFontFamilies;
    /// <summary>Resolved-font feedback (fallback family when the requested one is unavailable).</summary>
    public string SidebarFontStatus => SubtitleFontCatalog.DescribeFace(fonts.Resolve(SidebarFontFamily, SidebarFontWeight, "Normal"));
    partial void OnSidebarSubPaddingChanged(int value) => SidebarSubPaddingText = value.ToString(NumberFormat);
    partial void OnSidebarFontSizeChanged(double value) => SidebarFontSizeText = value.ToString(NumberFormat);
    partial void OnSidebarFontFamilyChanged(string value)
    {
        if (sidebarFontFamilies != null && AppPrefs.TryNormalizeSubtitleFontFamily(value, out _) && !sidebarFontFamilies.Contains(value))
            sidebarFontFamilies.Add(value);
        OnPropertyChanged(nameof(SidebarFontStatus));
    }
    partial void OnSidebarFontWeightChanged(string value) => OnPropertyChanged(nameof(SidebarFontStatus));
    [ObservableProperty] public partial string SubtitleFontSizeText { get; set; } = "";
    [ObservableProperty] public partial string SecondaryFontScaleText { get; set; } = "";
    [ObservableProperty] public partial string SubtitleBottomOffsetText { get; set; } = "";
    [ObservableProperty] public partial string SubtitleOutlineThicknessText { get; set; } = "";
    [ObservableProperty] public partial string SecondarySubtitleOutlineThicknessText { get; set; } = "";
    [ObservableProperty] public partial string SubtitleBackgroundOpacityPercentText { get; set; } = "";
    [ObservableProperty] public partial string SecondarySubtitleBackgroundOpacityPercentText { get; set; } = "";
    [ObservableProperty] public partial string SidebarWidthText { get; set; } = "";
    [ObservableProperty] public partial int SelectedSection { get; set; }
    [ObservableProperty] public partial string? Error { get; private set; }
    public bool AppearanceSelected => SelectedSection == 0;
    public bool SubtitlesSelected => SelectedSection == 1;
    public bool ToolsSelected => SelectedSection == 2;
    public ImmutableSolidColorBrush SubtitleTextBrush => new(Color.Parse(AppPrefs.TryNormalizeSubtitleTextColor(SubtitleTextColor, out string color) ? color : AppPrefs.DefaultSubtitleTextColor));
    public ImmutableSolidColorBrush SecondarySubtitleTextBrush => new(Color.Parse(AppPrefs.TryNormalizeSubtitleTextColor(SecondarySubtitleTextColor, out string color) ? color : AppPrefs.DefaultSecondarySubtitleTextColor));
    public ImmutableSolidColorBrush SubtitleOutlineBrush => new(Color.Parse(AppPrefs.TryNormalizeSubtitleTextColor(SubtitleOutlineColor, out string color) ? color : AppPrefs.DefaultSubtitleOutlineColor));
    public ImmutableSolidColorBrush SubtitleBackgroundColorBrush => new(Color.Parse(AppPrefs.TryNormalizeSubtitleTextColor(SubtitleBackgroundColor, out string color) ? color : AppPrefs.DefaultSubtitleBackgroundColor));
    public ImmutableSolidColorBrush SubtitleBackgroundBrush => new(SubtitleBackgroundColorBrush.Color, Math.Clamp(SubtitleBackgroundOpacityPercent, 0, 100) / 100d);
    public ImmutableSolidColorBrush SecondarySubtitleBackgroundBrush => new(SubtitleBackgroundColorBrush.Color, Math.Clamp(SecondarySubtitleBackgroundOpacityPercent, 0, 100) / 100d);
    // Keep the ItemsSource stable: replacing it clears ComboBox selection during a two-way family edit.
    public ObservableCollection<string> SubtitleFontFamilies => subtitleFontFamilies ??= new(fonts.GetFamilies(SubtitleFontFamily, SecondarySubtitleFontFamily));
    SubtitleFontSelection PrimaryPreview => fonts.Resolve(SubtitleFontFamily, SubtitleFontWeight, SubtitleFontStyle);
    SubtitleFontSelection SecondaryPreview => SecondarySubtitleInheritsFont ? PrimaryPreview
        : fonts.Resolve(SecondarySubtitleFontFamily, SecondarySubtitleFontWeight, SecondarySubtitleFontStyle);
    public FontFamily PreviewSubtitleFontFamily => PrimaryPreview.Family;
    public FontWeight PreviewSubtitleFontWeight => PrimaryPreview.Weight;
    public FontStyle PreviewSubtitleFontStyle => PrimaryPreview.Style;
    public FontFamily PreviewSecondarySubtitleFontFamily => SecondaryPreview.Family;
    public FontWeight PreviewSecondarySubtitleFontWeight => SecondaryPreview.Weight;
    public FontStyle PreviewSecondarySubtitleFontStyle => SecondaryPreview.Style;
    public double PreviewSecondarySubtitleFontSize => Math.Round(SubtitleFontSize * SecondaryFontScale, 1);
    public string SubtitleFontStatus => SubtitleFontCatalog.DescribeFace(PrimaryPreview);
    public string SecondarySubtitleFontStatus => (SecondarySubtitleInheritsFont ? "Inherits primary. " : "")
        + SubtitleFontCatalog.DescribeFace(SecondaryPreview);
    public bool SecondaryFontOverridesEnabled => !SecondarySubtitleInheritsFont;
    public SettingsLink? RequestedLink { get; private set; }
    public event Action? CloseRequested;

    partial void OnSubtitleFontSizeChanged(double value) => SubtitleFontSizeText = value.ToString(NumberFormat);
    partial void OnSecondaryFontScaleChanged(double value) => SecondaryFontScaleText = value.ToString(NumberFormat);
    partial void OnSubtitleBottomOffsetChanged(double value) => SubtitleBottomOffsetText = value.ToString(NumberFormat);
    partial void OnSubtitleOutlineThicknessChanged(double value) => SubtitleOutlineThicknessText = value.ToString(NumberFormat);
    partial void OnSecondarySubtitleOutlineThicknessChanged(double value) => SecondarySubtitleOutlineThicknessText = value.ToString(NumberFormat);
    partial void OnSubtitleBackgroundOpacityPercentChanged(int value) => SubtitleBackgroundOpacityPercentText = value.ToString(NumberFormat);
    partial void OnSecondarySubtitleBackgroundOpacityPercentChanged(int value) => SecondarySubtitleBackgroundOpacityPercentText = value.ToString(NumberFormat);
    partial void OnSubtitleBackgroundOpacityPercentTextChanged(string value)
    {
        if (TryReadOpacity(value, out int opacity)) SubtitleBackgroundOpacityPercent = opacity;
    }
    partial void OnSecondarySubtitleBackgroundOpacityPercentTextChanged(string value)
    {
        if (TryReadOpacity(value, out int opacity)) SecondarySubtitleBackgroundOpacityPercent = opacity;
    }
    partial void OnSidebarWidthChanged(double value) => SidebarWidthText = value.ToString(NumberFormat);
    partial void OnSubtitleWidthPercentChanged(int value) => SubtitleWidthPercentText = value.ToString(NumberFormat);
    partial void OnSubtitleMaxWidthChanged(double value) => SubtitleMaxWidthText = value.ToString(NumberFormat);
    partial void OnSubtitlePrimarySecondaryDistanceChanged(double value) => SubtitlePrimarySecondaryDistanceText = value.ToString(NumberFormat);
    partial void OnSubtitleSeparatorMaxWidthChanged(double value) => SubtitleSeparatorMaxWidthText = value.ToString(NumberFormat);
    partial void OnSubtitleSeparatorOpacityPercentChanged(int value) => SubtitleSeparatorOpacityPercentText = value.ToString(NumberFormat);
    partial void OnPrimaryBitmapScaleChanged(double value) => PrimaryBitmapScaleText = value.ToString(NumberFormat);
    partial void OnSecondaryBitmapScaleChanged(double value) => SecondaryBitmapScaleText = value.ToString(NumberFormat);
    partial void OnPrimaryBitmapPositionChanged(double value) => PrimaryBitmapPositionText = value.ToString(NumberFormat);

    partial void OnSubtitleFontFamilyChanged(string value) { IncludeRequestedFamily(value); NotifyFontPreviewChanged(); }
    partial void OnSubtitleFontWeightChanged(string value) => NotifyFontPreviewChanged();
    partial void OnSubtitleFontStyleChanged(string value) => NotifyFontPreviewChanged();
    partial void OnSecondarySubtitleFontFamilyChanged(string value) { IncludeRequestedFamily(value); NotifyFontPreviewChanged(); }
    partial void OnSecondarySubtitleFontWeightChanged(string value) => NotifyFontPreviewChanged();
    partial void OnSecondarySubtitleFontStyleChanged(string value) => NotifyFontPreviewChanged();
    partial void OnSecondarySubtitleInheritsFontChanged(bool value)
    {
        OnPropertyChanged(nameof(SecondaryFontOverridesEnabled));
        NotifyFontPreviewChanged();
    }

    void IncludeRequestedFamily(string value)
    {
        if (subtitleFontFamilies != null && AppPrefs.TryNormalizeSubtitleFontFamily(value, out _)
            && !subtitleFontFamilies.Contains(value))
            subtitleFontFamilies.Add(value);
    }

    void NotifyFontPreviewChanged()
    {
        OnPropertyChanged(nameof(PreviewSubtitleFontFamily));
        OnPropertyChanged(nameof(PreviewSubtitleFontWeight));
        OnPropertyChanged(nameof(PreviewSubtitleFontStyle));
        OnPropertyChanged(nameof(PreviewSecondarySubtitleFontFamily));
        OnPropertyChanged(nameof(PreviewSecondarySubtitleFontWeight));
        OnPropertyChanged(nameof(PreviewSecondarySubtitleFontStyle));
        OnPropertyChanged(nameof(SubtitleFontStatus));
        OnPropertyChanged(nameof(SecondarySubtitleFontStatus));
    }

    [RelayCommand] void CopyPrimaryFont()
    {
        SecondarySubtitleFontFamily = SubtitleFontFamily;
        SecondarySubtitleFontWeight = SubtitleFontWeight;
        SecondarySubtitleFontStyle = SubtitleFontStyle;
        SecondarySubtitleInheritsFont = false;
    }

    [RelayCommand] void ResetSecondaryFont() => SecondarySubtitleInheritsFont = true;

    [RelayCommand] void ResetSubtitleFonts()
    {
        SubtitleFontFamily = AppPrefs.DefaultSubtitleFontFamily;
        SubtitleFontWeight = AppPrefs.DefaultSubtitleFontWeight;
        SubtitleFontStyle = AppPrefs.DefaultSubtitleFontStyle;
        SecondarySubtitleFontFamily = AppPrefs.DefaultSubtitleFontFamily;
        SecondarySubtitleFontWeight = AppPrefs.DefaultSubtitleFontWeight;
        SecondarySubtitleFontStyle = AppPrefs.DefaultSubtitleFontStyle;
        SecondarySubtitleInheritsFont = true;
    }

    [RelayCommand] void ResetSubtitleBackground()
    {
        SubtitleBackgroundColor = AppPrefs.DefaultSubtitleBackgroundColor;
        SubtitleBackgroundOpacityPercent = 0;
        SecondarySubtitleBackgroundOpacityPercent = 0;
        // Replace invalid raw text too, even when the accepted numeric value was already zero.
        SubtitleBackgroundOpacityPercentText = 0.ToString(NumberFormat);
        SecondarySubtitleBackgroundOpacityPercentText = 0.ToString(NumberFormat);
    }

    partial void OnSelectedSectionChanged(int value)
    {
        OnPropertyChanged(nameof(AppearanceSelected));
        OnPropertyChanged(nameof(SubtitlesSelected));
        OnPropertyChanged(nameof(ToolsSelected));
    }

    [RelayCommand] void Save()
    {
        Error = null;
        if (!TryRead(SubtitleFontSizeText, 12, 96, out double fontSize)
            || !TryRead(SecondaryFontScaleText, 0.4, 1.5, out double scale)
            || !TryRead(SubtitleBottomOffsetText, AppPrefs.MinSubtitleBottomOffset, AppPrefs.MaxSubtitleBottomOffset, out double offset)
            || !TryRead(SubtitleOutlineThicknessText, AppPrefs.MinSubtitleOutlineThickness, AppPrefs.MaxSubtitleOutlineThickness, out double outline)
            || !TryRead(SecondarySubtitleOutlineThicknessText, AppPrefs.MinSubtitleOutlineThickness, AppPrefs.MaxSubtitleOutlineThickness, out double secondaryOutline)
            || !TryRead(SidebarWidthText, 220, 900, out double width)
            || Theme is not ("Dark" or "Light"))
        {
            Error = "Choose Dark or Light, font size 12–96, secondary scale 0.4–1.5, text subtitle bottom offset 0–400, primary and secondary outline thickness 0–8 and sidebar width 220–900.";
            return;
        }
        if (!AppPrefs.TryNormalizeSubtitleTextColor(SubtitleTextColor, out string primaryColor)
            || !AppPrefs.TryNormalizeSubtitleTextColor(SecondarySubtitleTextColor, out string secondaryColor))
        {
            Error = "Use #RRGGBB for both text subtitle colors (for example #FFFFFF).";
            return;
        }
        if (!AppPrefs.TryNormalizeSubtitleTextColor(SubtitleOutlineColor, out string outlineColor))
        {
            Error = "Use #RRGGBB for the primary and secondary text subtitle outline color (for example #000000).";
            return;
        }
        if (!AppPrefs.TryNormalizeSubtitleTextColor(SubtitleBackgroundColor, out string backgroundColor))
        {
            Error = "Use #RRGGBB for the primary and secondary text subtitle background color (for example #000000).";
            return;
        }
        if (!TryReadOpacity(SubtitleBackgroundOpacityPercentText, out int backgroundOpacity)
            || !TryReadOpacity(SecondarySubtitleBackgroundOpacityPercentText, out int secondaryBackgroundOpacity))
        {
            Error = "Use a whole number from 0 to 100 for each text subtitle background opacity. Zero removes the background.";
            return;
        }
        if (!AppPrefs.TryNormalizeSubtitleFontFamily(SubtitleFontFamily, out string family)
            || !AppPrefs.TryNormalizeSubtitleFontWeight(SubtitleFontWeight, out string weight)
            || !AppPrefs.TryNormalizeSubtitleFontStyle(SubtitleFontStyle, out string style)
            || !AppPrefs.TryNormalizeSubtitleFontFamily(SecondarySubtitleFontFamily, out string secondaryFamily)
            || !AppPrefs.TryNormalizeSubtitleFontWeight(SecondarySubtitleFontWeight, out string secondaryWeight)
            || !AppPrefs.TryNormalizeSubtitleFontStyle(SecondarySubtitleFontStyle, out string secondaryStyle))
        {
            Error = "Choose a plain font family name, Normal / SemiBold / Bold weight and Normal / Italic style for both subtitles.";
            return;
        }
        if (!TryReadWhole(SidebarSubPaddingText, AppPrefs.MinSidebarSubPadding, AppPrefs.MaxSidebarSubPadding, out int sidebarPadding)
            || !TryRead(SidebarFontSizeText, AppPrefs.MinSidebarFontSize, AppPrefs.MaxSidebarFontSize, out double sidebarFontSize))
        {
            Error = $"Use a sidebar subtitle padding {AppPrefs.MinSidebarSubPadding}–{AppPrefs.MaxSidebarSubPadding} DIP and a sidebar font size {AppPrefs.MinSidebarFontSize}–{AppPrefs.MaxSidebarFontSize} DIP.";
            return;
        }
        if (!AppPrefs.TryNormalizeSubtitleFontFamily(SidebarFontFamily, out string sidebarFamily)
            || !AppPrefs.TryNormalizeSubtitleFontWeight(SidebarFontWeight, out string sidebarWeight))
        {
            Error = "Choose a plain sidebar font family name and a Normal / SemiBold / Bold weight.";
            return;
        }
        if (!TryReadWhole(SubtitleWidthPercentText, AppPrefs.MinSubtitleWidthPercent, AppPrefs.MaxSubtitleWidthPercent, out int widthPercent)
            || !TryRead(SubtitleMaxWidthText, AppPrefs.MinSubtitleMaxWidth, AppPrefs.MaxSubtitleMaxWidth, out double maxWidth)
            || !TryRead(SubtitlePrimarySecondaryDistanceText, AppPrefs.MinSubtitlePrimarySecondaryDistance, AppPrefs.MaxSubtitlePrimarySecondaryDistance, out double distance))
        {
            Error = "Use a whole-number subtitle width 1–100, a max width 0–4000 DIP and a primary/secondary distance 0–200 DIP.";
            return;
        }
        if (!AppPrefs.TryNormalizeSubtitleLineAlignment(SubtitleLineAlignment, out string alignment))
        {
            Error = "Choose Center, Left or Right for subtitle line alignment.";
            return;
        }
        if (!TryRead(SubtitleSeparatorMaxWidthText, AppPrefs.MinSubtitleSeparatorMaxWidth, AppPrefs.MaxSubtitleSeparatorMaxWidth, out double separatorWidth)
            || !TryReadWhole(SubtitleSeparatorOpacityPercentText, AppPrefs.MinSubtitleSeparatorOpacityPercent, AppPrefs.MaxSubtitleSeparatorOpacityPercent, out int separatorOpacity))
        {
            Error = "Use a separator max width 0–2000 and a whole-number separator opacity 0–100.";
            return;
        }
        if (!AppPrefs.TryNormalizeSubtitlePositionAlignment(SubtitlePositionAlignment, out string positionAlignment)
            || !AppPrefs.TryNormalizeSubtitlePositionAlignment(SubtitlePositionAlignmentWhenDual, out string dualPositionAlignment))
        {
            Error = "Choose Top, Center or Bottom for the subtitle position alignment and the dual position alignment.";
            return;
        }
        if (!TryRead(PrimaryBitmapScaleText, 0.1, 8, out double primaryBitmapScale)
            || !TryRead(SecondaryBitmapScaleText, 0.1, 8, out double secondaryBitmapScale)
            || !TryRead(PrimaryBitmapPositionText, 0, 150, out double primaryBitmapPosition))
        {
            Error = "Use a bitmap subtitle scale 0.1–8 for both slots and a primary bitmap position 0–150.";
            return;
        }
        SubtitleWidthPercent = widthPercent;
        SubtitleWidthPercentText = widthPercent.ToString(NumberFormat);
        SubtitleMaxWidth = maxWidth;
        SubtitleMaxWidthText = maxWidth.ToString(NumberFormat);
        SubtitlePrimarySecondaryDistance = distance;
        SubtitlePrimarySecondaryDistanceText = distance.ToString(NumberFormat);
        SubtitleLineAlignment = alignment;
        SubtitleSeparatorMaxWidth = separatorWidth;
        SubtitleSeparatorMaxWidthText = separatorWidth.ToString(NumberFormat);
        SubtitleSeparatorOpacityPercent = separatorOpacity;
        SubtitleSeparatorOpacityPercentText = separatorOpacity.ToString(NumberFormat);
        SubtitlePositionAlignment = positionAlignment;
        SubtitlePositionAlignmentWhenDual = dualPositionAlignment;
        PrimaryBitmapScale = primaryBitmapScale;
        PrimaryBitmapScaleText = primaryBitmapScale.ToString(NumberFormat);
        SecondaryBitmapScale = secondaryBitmapScale;
        SecondaryBitmapScaleText = secondaryBitmapScale.ToString(NumberFormat);
        PrimaryBitmapPosition = primaryBitmapPosition;
        PrimaryBitmapPositionText = primaryBitmapPosition.ToString(NumberFormat);
        SubtitleFontFamily = family;
        SubtitleFontWeight = weight;
        SubtitleFontStyle = style;
        SecondarySubtitleFontFamily = secondaryFamily;
        SecondarySubtitleFontWeight = secondaryWeight;
        SecondarySubtitleFontStyle = secondaryStyle;
        SubtitleTextColor = primaryColor;
        SecondarySubtitleTextColor = secondaryColor;
        SubtitleOutlineColor = outlineColor;
        SubtitleBackgroundColor = backgroundColor;
        SubtitleBackgroundOpacityPercent = backgroundOpacity;
        SecondarySubtitleBackgroundOpacityPercent = secondaryBackgroundOpacity;
        SubtitleBackgroundOpacityPercentText = backgroundOpacity.ToString(NumberFormat);
        SecondarySubtitleBackgroundOpacityPercentText = secondaryBackgroundOpacity.ToString(NumberFormat);
        SubtitleFontSize = fontSize;
        SecondaryFontScale = scale;
        SubtitleBottomOffset = offset;
        SubtitleOutlineThickness = outline;
        SecondarySubtitleOutlineThickness = secondaryOutline;
        SidebarWidth = width;
        SidebarSubPadding = sidebarPadding;
        SidebarSubPaddingText = sidebarPadding.ToString(NumberFormat);
        SidebarFontSize = sidebarFontSize;
        SidebarFontSizeText = sidebarFontSize.ToString(NumberFormat);
        SidebarFontFamily = sidebarFamily;
        SidebarFontWeight = sidebarWeight;
        if (!TryBuildAppShortcuts(out Dictionary<string, string> appShortcuts, out AppShortcutMap appMap, out string shortcutError))
        {
            Error = shortcutError;
            return;
        }
        if (!TryBuildEngineKeys(appMap, out List<EngineKeyBindingPref> engineKeyPrefs, out bool engineKeysCustomized, out string engineKeyError))
        {
            Error = engineKeyError;
            return;
        }
        AppShortcuts = appShortcuts;
        EngineKeyBindingPrefs = engineKeyPrefs;
        EngineKeysCustomized = engineKeysCustomized;
        try { commit(this); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Error = "Could not save preferences: " + ex.Message;
            return;
        }
        CloseRequested?.Invoke();
    }
    bool TryBuildAppShortcuts(out Dictionary<string, string> overrides, out AppShortcutMap map, out string error)
    {
        overrides = new Dictionary<string, string>(StringComparer.Ordinal);
        error = "";
        foreach (ShortcutEditRow row in ShortcutRows)
        {
            if (!AppShortcutMap.TryParseChord(row.Chord, out WpfKey key, out bool ctrl, out bool alt, out bool shift))
            {
                error = $"Use a key chord such as Ctrl+Shift+B for \"{row.Description}\" (a modifier alone is not a chord).";
                map = new AppShortcutMap(new Dictionary<string, Action>());
                return false;
            }
            string canonical = AppShortcutMap.FormatChord(key, ctrl, alt, shift);
            // Untouched rows keep no override, so actions with several default chords (the cheat sheet has F1 and
            // Shift+OemQuestion) keep all of them; Reset therefore really restores the defaults.
            if (canonical != row.DefaultChord)
                overrides[row.ActionName] = canonical;
        }

        map = new AppShortcutMap(new Dictionary<string, Action>(), overrides);
        return map.TryValidateEffective(out error);
    }

    /// <summary>Validates the engine draft: parseable chords, no duplicate enabled chords, no app↔engine collision.</summary>
    bool TryBuildEngineKeys(AppShortcutMap appMap, out List<EngineKeyBindingPref> prefs, out bool customized, out string error)
    {
        prefs = [];
        customized = false;
        error = "";
        if (EngineKeyRows.Count == 0)
        {
            error = "Keep at least one engine key binding; use Reset to restore the engine defaults.";
            return false;
        }

        List<EngineKeyBindingInfo> bindings = [];
        foreach (EngineKeyEditRow row in EngineKeyRows)
        {
            if (!AppShortcutMap.TryParseChord(row.Chord, out WpfKey key, out bool ctrl, out bool alt, out bool shift))
            {
                error = $"Use a key chord such as Ctrl+Shift+B for engine action \"{row.Description}\".";
                return false;
            }
            bindings.Add(new EngineKeyBindingInfo(row.Action, key, ctrl, alt, shift, row.IsKeyUp, row.IsEnabled));
        }

        // The same shared policy runs at start-up (warning toast) and here (blocking error).
        if (!EngineKeyBindings.TryValidate(bindings, appMap.Shortcuts, out error))
            return false;

        prefs = bindings.Select(binding => new EngineKeyBindingPref
        {
            Action = binding.Action.ToString(),
            Key = binding.Key.ToString(),
            Ctrl = binding.Ctrl,
            Alt = binding.Alt,
            Shift = binding.Shift,
            IsKeyUp = binding.IsKeyUp,
            IsEnabled = binding.IsEnabled,
        }).ToList();
        customized = !EngineKeyBindings.SameAsDefaults(prefs);
        return true;
    }

    [RelayCommand]
    void ResetEngineKeys()
    {
        EngineKeyRows.Clear();
        foreach (EngineKeyBindingInfo binding in EngineKeyBindings.Defaults())
            EngineKeyRows.Add(new EngineKeyEditRow(binding, DeleteEngineKeyRow));
        OnPropertyChanged(nameof(HasEngineKeyRows));
    }

    void DeleteEngineKeyRow(EngineKeyEditRow row)
    {
        EngineKeyRows.Remove(row);
        OnPropertyChanged(nameof(HasEngineKeyRows));
    }

    bool TryRead(string text, double min, double max, out double value)
    {
        value = 0;
        if (!decimal.TryParse(text, NumberStyles.Any, NumberFormat, out decimal parsed)) return false;
        value = (double)parsed;
        return value >= min && value <= max;
    }
    bool TryReadOpacity(string text, out int value)
        => int.TryParse(text, NumberStyles.Integer, NumberFormat, out value) && value is >= 0 and <= 100;
    bool TryReadWhole(string text, int min, int max, out int value)
        => int.TryParse(text, NumberStyles.Integer, NumberFormat, out value) && value >= min && value <= max;
    [RelayCommand] void Close() => CloseRequested?.Invoke();
    [RelayCommand] void OpenTool(SettingsLink link)
    {
        RequestedLink = link;
        CloseRequested?.Invoke();
    }
}

/// <summary>One engine key-binding draft row (bounded WPF SettingsKeys equivalent: edit/disable/release/delete).</summary>
public sealed partial class EngineKeyEditRow(EngineKeyBindingInfo binding, Action<EngineKeyEditRow> delete) : ObservableObject
{
    public KeyBindingAction Action { get; } = binding.Action;
    public string ActionName => Action.ToString();
    public string Description => EngineKeyBindings.Describe(Action);
    [ObservableProperty] public partial string Chord { get; set; } = binding.FormatChord();
    [ObservableProperty] public partial bool IsEnabled { get; set; } = binding.IsEnabled;
    [ObservableProperty] public partial bool IsKeyUp { get; set; } = binding.IsKeyUp;
    /// <summary>Friendly display form (Enter, +, Play/Pause) while <see cref="Chord"/> stays canonical for persistence.</summary>
    public string DisplayChord => AppShortcutMap.TryParseChord(Chord, out WpfKey key, out bool ctrl, out bool alt, out bool shift)
        ? string.Join("+", KeyMapper.ChordParts(key, ctrl, alt, shift))
        : Chord;
    /// <summary>Distinct screen-reader name: repeated actions differ by their current chord.</summary>
    public string AccessibleName => $"{Description} ({DisplayChord})";
    partial void OnChordChanged(string value)
    {
        OnPropertyChanged(nameof(DisplayChord));
        OnPropertyChanged(nameof(AccessibleName));
    }
    [RelayCommand] void Delete() => delete(this);
}

public sealed partial class ShortcutEditRow(string actionName, string description, string defaultChord, string chord)
    : ObservableObject
{
    public string ActionName { get; } = actionName;
    public string Description { get; } = description;
    public string DefaultChord { get; } = defaultChord;
    [ObservableProperty] public partial string Chord { get; set; } = chord;
    [RelayCommand] void Reset() => Chord = DefaultChord;
}
