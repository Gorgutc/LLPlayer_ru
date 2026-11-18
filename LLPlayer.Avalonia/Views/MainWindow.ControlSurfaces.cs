using Avalonia.Controls;
using Avalonia.Input;
using FlyleafLib.MediaFramework.MediaDevice;
using LLPlayer.ControlApi;

namespace LLPlayer.Avalonia.Views;

public partial class MainWindow
{
    Window? FindControlSurface(ControlUiSurface surface) => surface switch
    {
        ControlUiSurface.CommandPalette => commandPalette,
        ControlUiSurface.DebugOverlay => playerDebugWindow,
        ControlUiSurface.CheatSheet => cheatSheet,
        ControlUiSurface.About => aboutWindow,
        ControlUiSurface.Settings => settingsWindow,
        ControlUiSurface.PlaybackSettings => playbackSettingsWindow,
        ControlUiSurface.PluginOptions => pluginOptionsWindow,
        ControlUiSurface.Playlist => playlistWindow,
        ControlUiSurface.WordManager => wordManagerWindow,
        ControlUiSurface.SubtitleExport => subtitlesExportWindow,
        ControlUiSurface.SubtitleDownload => subtitleDownloadWindow,
        ControlUiSurface.AiInsights => insightsWindow,
        ControlUiSurface.Batch => batchWindow,
        ControlUiSurface.PdicSettings => pdicSettingsWindow,
        ControlUiSurface.OnlineVideo => onlineVideoWindow,
        ControlUiSurface.Translation => translationWindow,
        ControlUiSurface.Recognition => recognitionWindow,
        ControlUiSurface.Dubbing => dubbingWindow,
        ControlUiSurface.AudioCapture => captureWindows.GetValueOrDefault(CaptureDeviceKind.Audio),
        ControlUiSurface.VideoCapture => captureWindows.GetValueOrDefault(CaptureDeviceKind.Video),
        _ => throw new ArgumentOutOfRangeException(nameof(surface)),
    };

    Window? FindControlModal()
    {
        // Nested tools block their parent's input until they finish. Check them before the main-window modals.
        Window?[] modals = [modelManagerWindow, voicePickerWindow, settingsWindow, recognitionWindow,
            translationWindow, onlineVideoWindow, subtitlesExportWindow, playbackSettingsWindow,
            pluginOptionsWindow, subtitleDownloadWindow, pdicSettingsWindow, dubbingWindow];
        return modals.FirstOrDefault(window => window?.IsVisible == true);
    }

    internal ControlUiSurfaceStatus GetControlSurfaceStatus(ControlUiSurface surface)
    {
        Window? child = FindControlSurface(surface);
        Window? modal = FindControlModal();
        bool available = CanUseControlSurfaces
            && (modal == null || ReferenceEquals(modal, child)) && vm?.IsControlSurfaceConfigured(surface) == true;
        return new(surface, child is { IsVisible: true, WindowState: not WindowState.Minimized }, available);
    }

    internal ControlUiSurfaceStatus OpenControlSurface(ControlUiSurface surface, bool toggle = false)
    {
        if (!GetControlSurfaceStatus(surface).Available || vm == null)
            throw new ControlException(new("gui_unavailable", "This tool is unavailable in the selected player."));

        Window? child = FindControlSurface(surface);
        bool show = !toggle || child == null;
        if (!show)
            child!.Close();
        else if (child is BatchSubtitlesWindow batch)
            batch.Restore();
        else if (child is { IsVisible: true })
        {
            if (child.WindowState == WindowState.Minimized) child.WindowState = WindowState.Normal;
            child.Activate();
        }
        else
        {
            var command = vm.GetControlSurfaceCommand(surface);
            if (!command.CanExecute(null))
                throw new ControlException(new("command_unavailable", "This tool is currently disabled."));
            command.Execute(null);
        }

        var result = GetControlSurfaceStatus(surface);
        if (result.Visible != show)
            throw new ControlException(new("gui_operation_failed", "The selected player's tool did not reach the requested visibility."));
        return result;
    }

    bool CanUseControlSurfaces => IsVisible && WindowState != WindowState.Minimized && !shutdownStarted && !shutdownComplete;

    internal bool CanFocusControlSidebarSearch => CanUseControlSurfaces && vm != null && FindControlModal() == null;
    internal bool IsControlSidebarSearchFocused => FilterBox.IsFocused;

    internal void FocusControlSidebarSearch()
    {
        if (!CanFocusControlSidebarSearch || vm == null)
            throw new ControlException(new("gui_unavailable", "Subtitle search is unavailable in the selected player."));
        if (!vm.SidebarVisible) vm.ToggleSidebar();
        Activate();
        if (!FilterBox.Focus(NavigationMethod.Tab) || !FilterBox.IsFocused)
            throw new ControlException(new("gui_operation_failed", "Subtitle search did not gain focus in the selected player."));
    }
}
