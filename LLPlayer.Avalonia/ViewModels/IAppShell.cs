namespace LLPlayer.Avalonia.ViewModels;

/// <summary>Window-level operations the main view model asks for (implemented by MainWindow; faked in tests).</summary>
public interface IAppShell
{
    bool IsFullScreen { get; set; }

    Task<string?> PickMediaFileAsync(string? initialFolder);
    Task<string?> PickSubtitlesFileAsync(string? initialFolder);

    void ShowCheatSheet();
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
