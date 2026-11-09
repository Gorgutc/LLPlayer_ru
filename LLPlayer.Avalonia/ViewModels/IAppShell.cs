namespace LLPlayer.Avalonia.ViewModels;

/// <summary>Window-level operations the main view model asks for (implemented by MainWindow; faked in tests).</summary>
public interface IAppShell
{
    bool IsFullScreen { get; set; }

    Task<string?> PickMediaFileAsync(string? initialFolder);
    Task<string?> PickSubtitlesFileAsync(string? initialFolder);
    Task<string?> PickFolderAsync(string? initialFolder) => Task.FromResult<string?>(null);

    void ShowCheatSheet();
    void ShowCommandPalette() { }
    void ShowAbout() { }
    void TogglePlayerDebug() { }
    void ShowCaptureDevices(CaptureDevicesViewModel model) { }
    void ShowSettings(SettingsViewModel model) { }
    void ShowWordManager(WordManagerViewModel model) { }
    void ShowSubtitlesExport(SubtitlesExportViewModel model) { }
    void ShowTranslation(TranslationViewModel model) { }
    void ShowOnlineVideo(OnlineVideoViewModel model) { }
    void ShowRecognition(RecognitionViewModel model) { }
    void ShowPlaybackSettings(PlaybackSettingsViewModel model) { }
    void ShowPluginOptions(PluginOptionsViewModel model) { }
    void ShowPlaylist(PlaylistViewModel model) { }
    void ShowSubtitleDownload(OpenSubtitlesDownloadViewModel model) { }
    void ShowPdicSettings(PdicSettingsViewModel model) { }
    void ShowAiInsights(AiInsightsViewModel model) { }
    void ShowDubbing(DubbingViewModel model) { }
    void ShowBatch(BatchSubtitlesViewModel model) { }
    void ShowVoicePicker(VoicePickerViewModel model) { }
    Task ShowModelManagerAsync(ModelManagerViewModel model) => Task.CompletedTask;
    Task<bool> RevealMediaAsync(string path) => Task.FromResult(false);
    Task<bool> OpenBrowserAsync(Uri uri) => Task.FromResult(false);
    void SetClipboardText(string text);
    void ApplyTheme(string theme);
    void Exit();
}

public enum ToastVariant
{
    Default,
    Success,
    Destructive,
}

/// <summary>A small notification shown top-centre over the video (shadcn "sonner"-like toast).</summary>
public sealed record ToastItem(string Title, string? Message, ToastVariant Variant)
{
    public bool HasMessage => !string.IsNullOrEmpty(Message);
}
