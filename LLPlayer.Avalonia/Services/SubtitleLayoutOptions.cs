using Avalonia.Media;

namespace LLPlayer.Avalonia.Services;

/// <summary>Vertical block alignment for the text-subtitle overlay (adapted to the approved DIP bottom model).</summary>
public enum SubtitleVerticalAlignment
{
    Bottom,
    Center,
    Top,
}

/// <summary>Single source of truth for the text-subtitle layout option strings and their Avalonia mappings.</summary>
public static class SubtitleLayoutOptions
{
    public static IReadOnlyList<string> LineAlignments { get; } = ["Center", "Left", "Right"];

    // WPF exposes Top/Center/Bottom for both single and dual text blocks. The Avalonia transport keeps its approved
    // DIP bottom model and treats the option as which block edge is anchored at the configured bottom distance.
    public static IReadOnlyList<string> PositionAlignments { get; } = ["Top", "Center", "Bottom"];

    /// <summary>Maps a persisted option to Avalonia alignment; unsupported/null values fall back to Center.</summary>
    public static TextAlignment ToTextAlignment(string? value)
        => value is { } name && LineAlignments.Contains(name) && Enum.TryParse(name, out TextAlignment parsed)
            ? parsed
            : TextAlignment.Center;

    /// <summary>Maps a persisted position-alignment option; unsupported/null values fall back to Bottom.</summary>
    public static SubtitleVerticalAlignment ToVerticalAlignment(string? value)
        => value is { } name && PositionAlignments.Contains(name) && Enum.TryParse(name, out SubtitleVerticalAlignment parsed)
            ? parsed
            : SubtitleVerticalAlignment.Bottom;
}
