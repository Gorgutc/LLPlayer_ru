using CommunityToolkit.Mvvm.Input;
using LLPlayer.Avalonia.Services;

namespace LLPlayer.Avalonia.ViewModels;

public sealed partial class MainWindowViewModel
{
    public PlaylistViewModel Playlist { get; private set; } = null!;
    public Func<PlaybackSettingsViewModel>? PlaybackSettingsDialog { get; set; }
    public Func<PluginOptionsViewModel>? PluginOptionsDialog { get; set; }
    public Func<OpenSubtitlesDownloadViewModel>? SubtitleDownloadDialog { get; set; }
    public Func<bool>? SubtitleExportBom { get; set; }
    public Func<PdicSettingsViewModel>? PdicSettingsDialog { get; set; }
    public string PrimarySourceLanguage => player.GetSubtitleLanguage(0) ?? "";
    public string SecondarySourceLanguage => player.GetSubtitleLanguage(1) ?? "";
    public string SubtitleTargetLanguage => player.GetSubtitleTargetLanguage() ?? "";

    void RefreshOverlayLanguages()
    {
        OnPropertyChanged(nameof(PrimarySourceLanguage));
        OnPropertyChanged(nameof(SecondarySourceLanguage));
        OnPropertyChanged(nameof(SubtitleTargetLanguage));
    }

    [RelayCommand]
    void ShowPlaybackSettings()
    {
        if (disposed) return;
        if (PlaybackSettingsDialog is { } factory) shell.ShowPlaybackSettings(factory());
    }
    [RelayCommand]
    void ShowPluginOptions()
    {
        if (!disposed && PluginOptionsDialog is { } factory) shell.ShowPluginOptions(factory());
    }
    [RelayCommand]
    void ShowPlaylist() { if (!disposed) shell.ShowPlaylist(Playlist); }
    [RelayCommand]
    void ShowSubtitleDownload()
    {
        if (!disposed && SubtitleDownloadDialog is { } factory) shell.ShowSubtitleDownload(factory());
    }
    [RelayCommand]
    void ShowPdicSettings()
    {
        if (!disposed && PdicSettingsDialog is { } factory) shell.ShowPdicSettings(factory());
    }
}
