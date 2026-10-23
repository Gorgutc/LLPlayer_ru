using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using LLPlayer.Avalonia.Services;
using System.Globalization;
using System.Collections.ObjectModel;

namespace LLPlayer.Avalonia.ViewModels;

public enum SettingsLink { Translation, Recognition, Shortcuts }

/// <summary>Detached preferences draft. Persistence and live application are owned by the main view model.</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    readonly Action<SettingsViewModel> commit;
    readonly SubtitleFontCatalog fonts = new();
    ObservableCollection<string>? subtitleFontFamilies;
    public SettingsViewModel(AppPrefs current, Action<SettingsViewModel> commit)
    {
        this.commit = commit;
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
    [ObservableProperty] public partial bool SidebarVisible { get; set; }
    [ObservableProperty] public partial double SidebarWidth { get; set; }
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
        try { commit(this); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Error = "Could not save preferences: " + ex.Message;
            return;
        }
        CloseRequested?.Invoke();
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
    [RelayCommand] void Close() => CloseRequested?.Invoke();
    [RelayCommand] void OpenTool(SettingsLink link)
    {
        RequestedLink = link;
        CloseRequested?.Invoke();
    }
}
