using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using FlyleafLib.MediaFramework.MediaDevice;
using FlyleafLib.MediaPlayer;
using LLPlayer.Avalonia.Services;

namespace LLPlayer.Avalonia.ViewModels;

public sealed partial class MainWindowViewModel
{
    public Func<ApplicationLaunchKind, Task<bool>>? LaunchApplication { get; set; }

    static IEnumerable<KeyBinding> EngineRows(IEnumerable<EngineKeyBindingInfo> bindings) => bindings.Select(row => new KeyBinding
    {
        Action = row.Action, Key = row.Key, Ctrl = row.Ctrl, Alt = row.Alt, Shift = row.Shift,
        IsKeyUp = row.IsKeyUp, IsEnabled = row.IsEnabled,
    });

    Dictionary<string, ICommand> AppCommandOwners() => new(StringComparer.Ordinal)
    {
        [AppShortcutMap.ToggleSidebar] = ToggleSidebarCommand,
        [AppShortcutMap.ActivateSubsSearch] = ActivateSubsSearchCommand,
        [AppShortcutMap.OpenWindowCheatSheet] = ShowCheatSheetCommand,
        [AppShortcutMap.SubsSizeIncrease] = IncreaseSubtitleSizeCommand,
        [AppShortcutMap.SubsSizeDecrease] = DecreaseSubtitleSizeCommand,
        [AppShortcutMap.SubsPositionUp] = SubtitlesUpCommand,
        [AppShortcutMap.SubsPositionDown] = SubtitlesDownCommand,
        [AppShortcutMap.SubsPrimaryTextCopy] = CopyPrimaryTextCommand,
        [AppShortcutMap.OpenSubtitles] = OpenSubtitlesCommand,
        [AppShortcutMap.ToggleTheme] = ToggleThemeCommand,
        [AppShortcutMap.OpenWindowSettings] = ShowSettingsCommand,
        [AppShortcutMap.OpenWindowWordManager] = ShowWordManagerCommand,
        [AppShortcutMap.OpenWindowSubsExporter] = ShowSubtitlesExportCommand,
        [AppShortcutMap.OpenCommandPalette] = ShowCommandPaletteCommand,
        [AppShortcutMap.ToggleDebugOverlay] = TogglePlayerDebugCommand,
        [AppShortcutMap.OpenWindowAbout] = ShowAboutCommand,
        [AppShortcutMap.OpenNextFile] = OpenNextLocalMediaCommand,
        [AppShortcutMap.OpenPrevFile] = OpenPreviousLocalMediaCommand,
        [AppShortcutMap.OpenCurrentPath] = OpenMediaLocationCommand,
        [AppShortcutMap.CopyMediaPath] = CopyMediaPathCommand,
        [AppShortcutMap.SubsPrimarySizeIncrease] = IncreasePrimarySubtitleSizeCommand,
        [AppShortcutMap.SubsPrimarySizeDecrease] = DecreasePrimarySubtitleSizeCommand,
        [AppShortcutMap.SubsSecondarySizeIncrease] = IncreaseSecondarySubtitleSizeCommand,
        [AppShortcutMap.SubsSecondarySizeDecrease] = DecreaseSecondarySubtitleSizeCommand,
        [AppShortcutMap.SubsDistanceIncrease] = IncreaseSubtitleDistanceCommand,
        [AppShortcutMap.SubsDistanceDecrease] = DecreaseSubtitleDistanceCommand,
        [AppShortcutMap.SubsTextCopy] = CopyAllSubtitleTextCommand,
        [AppShortcutMap.SubsSecondaryTextCopy] = CopySecondaryTextCommand,
        [AppShortcutMap.ToggleSubsAutoTextCopy] = ToggleAutoCopySubtitleTextCommand,
        [AppShortcutMap.ToggleSidebarShowSecondary] = ToggleSidebarSlotCommand,
        [AppShortcutMap.ToggleSidebarShowOriginalText] = ToggleSidebarOriginalTextCommand,
        [AppShortcutMap.ToggleAlwaysOnTop] = ToggleAlwaysOnTopCommand,
        [AppShortcutMap.OpenWindowSubsDownloader] = ShowSubtitleDownloadCommand,
        [AppShortcutMap.OpenWindowAiInsights] = ShowAiInsightsCommand,
        [AppShortcutMap.OpenWindowBatchSubtitles] = ShowBatchSubtitlesCommand,
        [AppShortcutMap.OpenWindowPlaybackSettings] = ShowPlaybackSettingsCommand,
        [AppShortcutMap.OpenWindowPluginOptions] = ShowPluginOptionsCommand,
        [AppShortcutMap.OpenWindowPlaylist] = ShowPlaylistCommand,
        [AppShortcutMap.OpenWindowPdicSettings] = ShowPdicSettingsCommand,
        [AppShortcutMap.OpenWindowOnlineVideo] = ShowOnlineVideoCommand,
        [AppShortcutMap.OpenWindowTranslation] = ShowTranslationCommand,
        [AppShortcutMap.OpenWindowRecognition] = ShowRecognitionCommand,
        [AppShortcutMap.OpenWindowDubbing] = ShowDubbingSettingsCommand,
        [AppShortcutMap.AppNew] = NewApplicationCommand,
        [AppShortcutMap.AppClone] = CloneApplicationCommand,
        [AppShortcutMap.AppRestart] = RestartApplicationCommand,
        [AppShortcutMap.AppExit] = ExitCommand,
        ["OpenAudioCaptureDevices"] = ShowAudioCaptureDevicesCommand,
        ["OpenVideoCaptureDevices"] = ShowVideoCaptureDevicesCommand,
        [nameof(KeyBindingAction.ToggleRecording)] = ToggleRecordingCommand,
        [nameof(KeyBindingAction.TakeSnapshot)] = TakeSnapshotCommand,
        [nameof(KeyBindingAction.ToggleVideoAcceleration)] = ToggleVideoAccelerationCommand,
    };

    Dictionary<string, Action> CreateAppActions() => AppCommandOwners().ToDictionary(pair => pair.Key,
        pair => (Action)(() => { if (!disposed && pair.Value.CanExecute(null)) pair.Value.Execute(null); }), StringComparer.Ordinal);

    public IReadOnlyList<ExecutableCommand> BuildExecutableCommands()
    {
        Dictionary<string, ICommand> owners = AppCommandOwners();
        return ExecutableCommandCatalog.Build(Shortcuts, EngineRows(player.GetEngineKeyBindings()),
            player.ResolveEngineAction, CreateAppActions(), descriptor => !disposed && (descriptor.Target == ShortcutActionTarget.Engine
                ? EngineCommandAvailable(descriptor.EngineAction!.Value) : owners.TryGetValue(descriptor.Name, out ICommand? command)
                    && command.CanExecute(null)),
            [new(ShortcutActionTarget.Application, "OpenAudioCaptureDevices", "Open Audio Capture Devices", "Audio"),
             new(ShortcutActionTarget.Application, "OpenVideoCaptureDevices", "Open Video Capture Devices", "Video")]);
    }

    bool EngineCommandAvailable(KeyBindingAction action) => action switch
    {
        KeyBindingAction.ToggleRecording or KeyBindingAction.TakeSnapshot => CanUseMediaCommands(),
        _ => true,
    };

    [RelayCommand] void ShowCommandPalette() { if (!disposed) shell.ShowCommandPalette(); }
    [RelayCommand] void ShowAbout() { if (!disposed) shell.ShowAbout(); }
    [RelayCommand] void TogglePlayerDebug() { if (!disposed) shell.TogglePlayerDebug(); }
    [RelayCommand] Task NewApplicationAsync() => LaunchInstanceAsync(ApplicationLaunchKind.New);
    [RelayCommand] Task CloneApplicationAsync() => LaunchInstanceAsync(ApplicationLaunchKind.Clone);
    [RelayCommand] Task RestartApplicationAsync() => LaunchInstanceAsync(ApplicationLaunchKind.Restart);
    async Task LaunchInstanceAsync(ApplicationLaunchKind kind)
    {
        if (disposed) return;
        if (LaunchApplication == null || !await LaunchApplication(kind))
            ShowToast("Could not launch LLPlayer", "The new application instance could not be started. The current application remains open.", ToastVariant.Destructive);
    }
    [RelayCommand] void ToggleAlwaysOnTop() => ApplyCommandSettings(draft => draft.AlwaysOnTop = !AlwaysOnTop);
    [RelayCommand] void IncreasePrimarySubtitleSize() => ChangeSlotSubtitleSize(0, SubtitleSizeStep);
    [RelayCommand] void DecreasePrimarySubtitleSize() => ChangeSlotSubtitleSize(0, -SubtitleSizeStep);
    [RelayCommand] void IncreaseSecondarySubtitleSize() => ChangeSlotSubtitleSize(1, SubtitleSizeStep);
    [RelayCommand] void DecreaseSecondarySubtitleSize() => ChangeSlotSubtitleSize(1, -SubtitleSizeStep);
    void ChangeSlotSubtitleSize(int slot, double step)
    {
        if (slot == 0 && !player.PrimarySlotEnabled || slot == 1 && !player.SecondarySlotEnabled) return;
        bool bitmap = slot == 0 ? player.PrimaryImage != null && string.IsNullOrEmpty(PrimaryText)
            : player.SecondaryImage != null && string.IsNullOrEmpty(SecondaryText);
        if (bitmap)
        {
            if (slot == 0) player.PrimaryBitmapScale = Math.Max(.1, player.PrimaryBitmapScale + Math.Sign(step) * .05);
            else player.SecondaryBitmapScale = Math.Max(.1, player.SecondaryBitmapScale + Math.Sign(step) * .05);
            return;
        }
        double primary = prefs.SubtitleFontSize;
        double scale = prefs.SecondaryFontScale;
        double secondary = primary * scale;
        double nextPrimary = primary;
        double nextScale;
        if (slot == 0)
        {
            // Secondary size is stored as a ratio. Keep its absolute size fixed by restricting the primary
            // change to values that can still represent that same secondary size with a legal ratio.
            double minimum = Math.Max(AppPrefs.MinSubtitleFontSize, secondary / 1.5);
            double maximum = Math.Min(AppPrefs.MaxSubtitleFontSize, secondary / .4);
            nextPrimary = Math.Clamp(primary + step, minimum, maximum);
            if (nextPrimary == primary) return;
            nextScale = Math.Clamp(secondary / nextPrimary, .4, 1.5);
        }
        else
        {
            nextScale = Math.Clamp((secondary + step) / primary, .4, 1.5);
            if (nextScale == scale) return;
        }
        ApplyCommandSettings(draft =>
        {
            draft.SubtitleFontSize = nextPrimary;
            draft.SecondaryFontScale = nextScale;
        });
    }
    [RelayCommand] void IncreaseSubtitleDistance() => ChangeSubtitleDistance(2);
    [RelayCommand] void DecreaseSubtitleDistance() => ChangeSubtitleDistance(-2);
    void ChangeSubtitleDistance(double step) => ApplyCommandSettings(draft => draft.SubtitlePrimarySecondaryDistance =
        Math.Clamp(draft.SubtitlePrimarySecondaryDistance + step, AppPrefs.MinSubtitlePrimarySecondaryDistance, AppPrefs.MaxSubtitlePrimarySecondaryDistance));
    [RelayCommand] void CopyAllSubtitleText() => shell.SetClipboardText(WordActions.BuildAutoCopyText(SubtitleAutoCopyTarget.All, PrimaryText, SecondaryText));
    [RelayCommand] void CopySecondaryText() { if (!string.IsNullOrEmpty(SecondaryText)) shell.SetClipboardText(SecondaryText); }
    [RelayCommand] void ToggleAutoCopySubtitleText() => ApplyCommandSettings(draft => draft.SubsAutoTextCopy = !draft.SubsAutoTextCopy);
    [RelayCommand] void ToggleSidebarSlot() => Sidebar.SelectedSlot = Sidebar.SelectedSlot == 0 ? 1 : 0;
    [RelayCommand] void ToggleSidebarOriginalText() => ApplyCommandSettings(draft => draft.SidebarShowOriginalText = !draft.SidebarShowOriginalText);
    void ApplyCommandSettings(Action<SettingsViewModel> edit)
    {
        if (disposed) return;
        SettingsViewModel draft = CreateSettings();
        edit(draft);
        draft.SaveCommand.Execute(null);
        if (draft.Error != null) ShowToast("Could not apply setting", draft.Error, ToastVariant.Destructive);
    }
    [RelayCommand] void ShowAudioCaptureDevices() => ShowCaptureDevices(CaptureDeviceKind.Audio);
    [RelayCommand] void ShowVideoCaptureDevices() => ShowCaptureDevices(CaptureDeviceKind.Video);
    void ShowCaptureDevices(CaptureDeviceKind kind)
    {
        if (disposed) return;
        shell.ShowCaptureDevices(new(kind, () => player.GetCaptureDevices(kind),
            async token => { await player.RefreshCaptureDevicesAsync(kind, token); },
            player.GetCaptureFormatsAsync, player.OpenCaptureDeviceAsync));
    }
    bool CanUseMediaCommands() => !disposed && player.IsOpened && !player.IsOpening;
    [RelayCommand(CanExecute = nameof(CanUseMediaCommands))]
    void ToggleRecording() => player.ResolveEngineAction(KeyBindingAction.ToggleRecording)?.Invoke();
    [RelayCommand(CanExecute = nameof(CanUseMediaCommands))]
    void TakeSnapshot() => player.ResolveEngineAction(KeyBindingAction.TakeSnapshot)?.Invoke();
    [RelayCommand] void ToggleVideoAcceleration() => player.ResolveEngineAction(KeyBindingAction.ToggleVideoAcceleration)?.Invoke();
    public bool IsRecording => player.IsRecording;
    public bool VideoHardwareAccelerationRequested => player.GetVideoDecodeStatus().RequestedMode != FlyleafLib.MediaFramework.MediaRenderer.PortableVideoDecodeMode.Software;
    public void RefreshServiceCommandState()
    {
        OnPropertyChanged(nameof(IsRecording));
        OnPropertyChanged(nameof(VideoHardwareAccelerationRequested));
        ToggleRecordingCommand.NotifyCanExecuteChanged();
        TakeSnapshotCommand.NotifyCanExecuteChanged();
    }
}
