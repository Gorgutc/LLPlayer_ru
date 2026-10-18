using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LLPlayer.Avalonia.Services;
using System.Globalization;

namespace LLPlayer.Avalonia.ViewModels;

public enum SettingsLink { Translation, Recognition, Shortcuts }

/// <summary>Detached preferences draft. Persistence and live application are owned by the main view model.</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    readonly Action<SettingsViewModel> commit;
    public SettingsViewModel(AppPrefs current, Action<SettingsViewModel> commit)
    {
        this.commit = commit;
        Theme = current.Theme;
        SubtitleFontSize = current.SubtitleFontSize;
        SecondaryFontScale = current.SecondaryFontScale;
        SubtitleBottomOffset = current.SubtitleBottomOffset;
        // A zero offset matches the generated property's initial value and does not invoke its change hook.
        SubtitleBottomOffsetText = current.SubtitleBottomOffset.ToString(NumberFormat);
        SubtitleOutlineThickness = current.SubtitleOutlineThickness;
        SecondarySubtitleOutlineThickness = current.SecondarySubtitleOutlineThickness;
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
    [ObservableProperty] public partial double SubtitleFontSize { get; set; }
    [ObservableProperty] public partial double SecondaryFontScale { get; set; }
    [ObservableProperty] public partial double SubtitleBottomOffset { get; set; }
    [ObservableProperty] public partial double SubtitleOutlineThickness { get; set; }
    [ObservableProperty] public partial double SecondarySubtitleOutlineThickness { get; set; }
    [ObservableProperty] public partial bool SubtitlesVisible { get; set; }
    [ObservableProperty] public partial bool SidebarVisible { get; set; }
    [ObservableProperty] public partial double SidebarWidth { get; set; }
    [ObservableProperty] public partial string SubtitleFontSizeText { get; set; } = "";
    [ObservableProperty] public partial string SecondaryFontScaleText { get; set; } = "";
    [ObservableProperty] public partial string SubtitleBottomOffsetText { get; set; } = "";
    [ObservableProperty] public partial string SubtitleOutlineThicknessText { get; set; } = "";
    [ObservableProperty] public partial string SecondarySubtitleOutlineThicknessText { get; set; } = "";
    [ObservableProperty] public partial string SidebarWidthText { get; set; } = "";
    [ObservableProperty] public partial int SelectedSection { get; set; }
    [ObservableProperty] public partial string? Error { get; private set; }
    public bool AppearanceSelected => SelectedSection == 0;
    public bool SubtitlesSelected => SelectedSection == 1;
    public bool ToolsSelected => SelectedSection == 2;
    public SettingsLink? RequestedLink { get; private set; }
    public event Action? CloseRequested;

    partial void OnSubtitleFontSizeChanged(double value) => SubtitleFontSizeText = value.ToString(NumberFormat);
    partial void OnSecondaryFontScaleChanged(double value) => SecondaryFontScaleText = value.ToString(NumberFormat);
    partial void OnSubtitleBottomOffsetChanged(double value) => SubtitleBottomOffsetText = value.ToString(NumberFormat);
    partial void OnSubtitleOutlineThicknessChanged(double value) => SubtitleOutlineThicknessText = value.ToString(NumberFormat);
    partial void OnSecondarySubtitleOutlineThicknessChanged(double value) => SecondarySubtitleOutlineThicknessText = value.ToString(NumberFormat);
    partial void OnSidebarWidthChanged(double value) => SidebarWidthText = value.ToString(NumberFormat);

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
    [RelayCommand] void Close() => CloseRequested?.Invoke();
    [RelayCommand] void OpenTool(SettingsLink link)
    {
        RequestedLink = link;
        CloseRequested?.Invoke();
    }
}
