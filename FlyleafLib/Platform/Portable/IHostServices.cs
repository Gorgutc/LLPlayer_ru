namespace FlyleafLib;

#nullable enable

/// <summary>
/// Desktop-shell services the portable (non-Windows) build of FlyleafLib cannot implement by itself (clipboard,
/// file picker, notification sound). On Windows these are implemented with WPF/WinForms directly; on the portable
/// TFM the host application assigns an implementation to <see cref="Utils.HostServices"/>. Every member is optional:
/// when <see cref="Utils.HostServices"/> is null the related player commands become no-ops.
/// </summary>
public interface IHostServices
{
    /// <summary>Returns the current clipboard text (or null/empty when none). Called on the UI thread.</summary>
    string? GetClipboardText();

    /// <summary>Puts <paramref name="text"/> on the clipboard. Called on the UI thread.</summary>
    void SetClipboardText(string text);

    /// <summary>
    /// Shows an "open media file" picker and returns the selected path, or null when cancelled.
    /// <paramref name="initialDirectory"/> is the folder of the currently opened file (may be null).
    /// </summary>
    Task<string?> PickMediaFileAsync(string? initialDirectory);

    /// <summary>Plays the "long operation completed" notification sound located at <paramref name="soundPath"/>.</summary>
    void PlayCompletionSound(string soundPath);
}
