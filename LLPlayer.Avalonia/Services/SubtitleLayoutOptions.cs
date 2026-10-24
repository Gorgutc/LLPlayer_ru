using Avalonia.Media;

namespace LLPlayer.Avalonia.Services;

/// <summary>
/// Single source of truth for the text-subtitle line alignment options: the persisted strings, their settings list
/// and the mapping into Avalonia's <see cref="TextAlignment"/> used by both the overlay and the settings preview.
/// </summary>
public static class SubtitleLayoutOptions
{
    public static IReadOnlyList<string> LineAlignments { get; } = ["Center", "Left", "Right"];

    /// <summary>Maps a persisted option to Avalonia alignment; unsupported/null values fall back to Center.</summary>
    public static TextAlignment ToTextAlignment(string? value)
        => value is { } name && LineAlignments.Contains(name) && Enum.TryParse(name, out TextAlignment parsed)
            ? parsed
            : TextAlignment.Center;
}
