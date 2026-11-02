using Avalonia.Controls;
using Avalonia.Platform.Storage;
using LLPlayer.Avalonia.ViewModels;

namespace LLPlayer.Avalonia.Views;

public partial class MainWindow
{
    PlaybackSettingsWindow? playbackSettingsWindow;
    PluginOptionsWindow? pluginOptionsWindow;
    PlaylistWindow? playlistWindow;
    OpenSubtitlesDownloadWindow? subtitleDownloadWindow;
    PdicSettingsWindow? pdicSettingsWindow;

    public void ShowPdicSettings(PdicSettingsViewModel model)
    {
        vm?.CancelAppShortcuts();
        if (pdicSettingsWindow != null) { pdicSettingsWindow.Activate(); return; }
        var window = new PdicSettingsWindow(model);
        pdicSettingsWindow = window;
        OwnKeyboardSurface(window);
        window.Closed += (_, _) => pdicSettingsWindow = null;
        _ = window.ShowDialog(this);
    }

    internal async Task DrainParityWindowsAsync()
    {
        if (subtitleDownloadWindow?.DataContext is OpenSubtitlesDownloadViewModel download)
            await download.DisposeAsync();
        if (playbackSettingsWindow?.DataContext is PlaybackSettingsViewModel playback)
        {
            playbackSettingsWindow.IsEnabled = false;
            await Task.WhenAll(playback.SaveCommand.ExecutionTask ?? Task.CompletedTask,
                playback.RefreshDevicesCommand.ExecutionTask ?? Task.CompletedTask);
        }
        if (pluginOptionsWindow?.DataContext is PluginOptionsViewModel plugin)
            await (plugin.SaveCommand.ExecutionTask ?? Task.CompletedTask);
        if (pdicSettingsWindow?.DataContext is PdicSettingsViewModel pdic)
            await (pdic.SaveCommand.ExecutionTask ?? Task.CompletedTask);
        CloseParityWindows();
    }

    void CloseParityWindows()
    {
        subtitleDownloadWindow?.Close();
        playbackSettingsWindow?.Close();
        pluginOptionsWindow?.Close();
        playlistWindow?.Close();
        pdicSettingsWindow?.Close();
    }

    public void ShowPlaybackSettings(PlaybackSettingsViewModel model)
    {
        vm?.CancelAppShortcuts();
        if (playbackSettingsWindow != null) { playbackSettingsWindow.Activate(); return; }
        var window = new PlaybackSettingsWindow(model);
        playbackSettingsWindow = window;
        OwnKeyboardSurface(window);
        window.Closed += (_, _) => playbackSettingsWindow = null;
        _ = window.ShowDialog(this);
    }

    public void ShowPluginOptions(PluginOptionsViewModel model)
    {
        vm?.CancelAppShortcuts();
        if (pluginOptionsWindow != null) { pluginOptionsWindow.Activate(); return; }
        var window = new PluginOptionsWindow(model);
        pluginOptionsWindow = window;
        OwnKeyboardSurface(window);
        window.Closed += (_, _) => pluginOptionsWindow = null;
        _ = window.ShowDialog(this);
    }

    public void ShowPlaylist(PlaylistViewModel model)
    {
        vm?.CancelAppShortcuts();
        if (playlistWindow != null) { playlistWindow.Activate(); return; }
        var window = new PlaylistWindow(model);
        playlistWindow = window;
        OwnKeyboardSurface(window);
        window.Closed += (_, _) => playlistWindow = null;
        window.Show(this);
    }

    public void ShowSubtitleDownload(OpenSubtitlesDownloadViewModel model)
    {
        vm?.CancelAppShortcuts();
        if (subtitleDownloadWindow != null)
        {
            // The factory creates an owned HTTP client; dispose the unused new draft too.
            _ = model.DisposeAsync();
            subtitleDownloadWindow.Activate(); return;
        }
        var window = new OpenSubtitlesDownloadWindow(model);
        subtitleDownloadWindow = window;
        OwnKeyboardSurface(window);
        window.Closed += (_, _) => subtitleDownloadWindow = null;
        _ = window.ShowDialog(this);
    }

    public async Task<bool> RevealMediaAsync(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path)) return false;
        string? directory = Directory.Exists(path) ? Path.GetFullPath(path) : Path.GetDirectoryName(Path.GetFullPath(path));
        if (directory == null) return false;
        try { return await Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(directory)); }
        catch (Exception) { return false; }
    }
}
