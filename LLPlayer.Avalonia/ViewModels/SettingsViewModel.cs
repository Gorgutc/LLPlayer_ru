using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LLPlayer.Avalonia.Services;

namespace LLPlayer.Avalonia.ViewModels;

public enum SettingsLink { Translation, Recognition, Shortcuts }

/// <summary>Detached six-field draft. Persistence and live application are owned by the main view model.</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    readonly Action<SettingsViewModel> commit;
    public SettingsViewModel(AppPrefs current, Action<SettingsViewModel> commit)
    {
        this.commit = commit;
        Theme = current.Theme;
        SubtitleFontSize = current.SubtitleFontSize;
        SecondaryFontScale = current.SecondaryFontScale;
        SubtitlesVisible = current.SubtitlesVisible;
        SidebarVisible = current.SidebarVisible;
        SidebarWidth = current.SidebarWidth;
    }

    public static IReadOnlyList<string> Themes { get; } = ["Dark", "Light"];
    [ObservableProperty] public partial string Theme { get; set; } = "Dark";
    [ObservableProperty] public partial double SubtitleFontSize { get; set; }
    [ObservableProperty] public partial double SecondaryFontScale { get; set; }
    [ObservableProperty] public partial bool SubtitlesVisible { get; set; }
    [ObservableProperty] public partial bool SidebarVisible { get; set; }
    [ObservableProperty] public partial double SidebarWidth { get; set; }
    [ObservableProperty] public partial int SelectedSection { get; set; }
    [ObservableProperty] public partial string? Error { get; private set; }
    public bool AppearanceSelected => SelectedSection == 0;
    public bool SubtitlesSelected => SelectedSection == 1;
    public bool ToolsSelected => SelectedSection == 2;
    public SettingsLink? RequestedLink { get; private set; }
    public event Action? CloseRequested;

    partial void OnSelectedSectionChanged(int value)
    {
        OnPropertyChanged(nameof(AppearanceSelected));
        OnPropertyChanged(nameof(SubtitlesSelected));
        OnPropertyChanged(nameof(ToolsSelected));
    }

    [RelayCommand] void Save()
    {
        Error = null;
        if (Theme is not ("Dark" or "Light") || !InRange(SubtitleFontSize, 12, 96)
            || !InRange(SecondaryFontScale, 0.4, 1.5) || !InRange(SidebarWidth, 220, 900))
        {
            Error = "Choose Dark or Light, font size 12–96, secondary scale 0.4–1.5 and sidebar width 220–900.";
            return;
        }
        try { commit(this); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Error = "Could not save preferences: " + ex.Message;
            return;
        }
        CloseRequested?.Invoke();
    }
    static bool InRange(double value, double min, double max) => double.IsFinite(value) && value >= min && value <= max;
    [RelayCommand] void Close() => CloseRequested?.Invoke();
    [RelayCommand] void OpenTool(SettingsLink link)
    {
        RequestedLink = link;
        CloseRequested?.Invoke();
    }
}
