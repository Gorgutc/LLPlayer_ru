using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using FlyleafLib;

namespace LLPlayer.Avalonia.Services;

/// <summary>
/// FlyleafLib's portable desktop-shell seam (<see cref="Utils.HostServices"/>): clipboard and file picker of the main
/// window's <see cref="TopLevel"/>. The completion sound is not played (no audio device API in the app yet; the
/// engine's own player would be needed) — the host shows a toast instead.
/// </summary>
public sealed class AvaloniaHostServices(Func<TopLevel?> topLevel) : IHostServices
{
    static readonly TimeSpan ClipboardTimeout = TimeSpan.FromSeconds(2);

    /// <summary>File-type filter of the "Open media" picker.</summary>
    public static readonly IReadOnlyList<FilePickerFileType> MediaFileTypes =
    [
        new("Media files")
        {
            Patterns = ["*.mp4", "*.mkv", "*.webm", "*.avi", "*.mov", "*.m4v", "*.ts", "*.m2ts", "*.flv", "*.wmv", "*.mpg", "*.mpeg",
                        "*.mp3", "*.m4a", "*.aac", "*.flac", "*.ogg", "*.opus", "*.wav", "*.m3u", "*.m3u8", "*.pls"],
        },
        new("Subtitles") { Patterns = ["*.srt", "*.ass", "*.ssa", "*.vtt", "*.sub", "*.sup", "*.idx", "*.txt"] },
        FilePickerFileTypes.All,
    ];

    public static readonly IReadOnlyList<FilePickerFileType> SubtitleFileTypes =
    [
        new("Subtitles") { Patterns = ["*.srt", "*.ass", "*.ssa", "*.vtt", "*.sub", "*.sup", "*.idx", "*.txt"] },
        FilePickerFileTypes.All,
    ];

    /// <summary>Raised on the UI thread with the folder of a file the user picked (remembered as "last folder").</summary>
    public event Action<string>? FolderPicked;

    /// <summary>Raised when the engine asks for the completion sound (ASR finished); the app shows a toast.</summary>
    public event Action? CompletionRequested;

    public string? GetClipboardText()
    {
        IClipboard? clipboard = topLevel()?.Clipboard;
        if (clipboard == null)
            return null;

        Task<string?> task = clipboard.TryGetTextAsync();
        return WaitOnUIThread(task);
    }

    public void SetClipboardText(string text)
    {
        IClipboard? clipboard = topLevel()?.Clipboard;
        if (clipboard == null)
            return;

        _ = clipboard.SetTextAsync(text);
    }

    public Task<string?> PickMediaFileAsync(string? initialDirectory)
        => PickFileAsync("Open media", MediaFileTypes, initialDirectory);

    public async Task<string?> PickFileAsync(string title, IReadOnlyList<FilePickerFileType> types, string? initialDirectory)
    {
        if (!Dispatcher.UIThread.CheckAccess())
            return await Dispatcher.UIThread.InvokeAsync(() => PickFileAsync(title, types, initialDirectory));

        TopLevel? top = topLevel();
        if (top == null)
            return null;

        IStorageFolder? start = null;
        if (!string.IsNullOrEmpty(initialDirectory) && Directory.Exists(initialDirectory))
            start = await top.StorageProvider.TryGetFolderFromPathAsync(initialDirectory);

        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = types,
            SuggestedStartLocation = start,
        });

        string? path = files.Count > 0 ? files[0].TryGetLocalPath() : null;
        if (path != null && Path.GetDirectoryName(path) is { } folder)
            FolderPicked?.Invoke(folder);

        return path;
    }

    public void PlayCompletionSound(string soundPath) => CompletionRequested?.Invoke();

    /// <summary>
    /// Waits for an async clipboard read from the UI thread without dead-locking: the platform clipboard needs the UI
    /// loop, so a nested dispatcher frame runs it until the task completes (bounded by <see cref="ClipboardTimeout"/>).
    /// </summary>
    static string? WaitOnUIThread(Task<string?> task)
    {
        if (!task.IsCompleted && Dispatcher.UIThread.CheckAccess())
        {
            DispatcherFrame frame = new();
            using CancellationTokenSource timeout = new(ClipboardTimeout);
            using var reg = timeout.Token.Register(() => Dispatcher.UIThread.Post(() => frame.Continue = false));
            task.ContinueWith(_ => Dispatcher.UIThread.Post(() => frame.Continue = false), TaskScheduler.Default);
            Dispatcher.UIThread.PushFrame(frame);
        }
        else if (!task.IsCompleted)
        {
            task.Wait(ClipboardTimeout);
        }

        return task.IsCompletedSuccessfully ? task.Result : null;
    }
}
