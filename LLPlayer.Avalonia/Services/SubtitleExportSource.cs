using FlyleafLib.MediaPlayer;

namespace LLPlayer.Avalonia.Services;

public enum SubtitleExportText { Original, TranslatedOnly, TranslatedWithFallback }
public sealed record SubtitleExportSnapshot(IReadOnlyList<SubtitleExportLine> Lines, string SuggestedBaseName, string? InitialDirectory, bool IsLoading);
public sealed record SubtitleExportRequest(string SuggestedFileName, string? InitialDirectory, SubtitleExportFormat Format);
public interface ISubtitleExportSource
{
    SubtitleExportSnapshot Capture(int slot, SubtitleExportText text);
}

/// <summary>Owns no player resources. Capture copies cue values and styles before a picker can suspend the UI.</summary>
public sealed class FlyleafSubtitleExportSource(Player player) : ISubtitleExportSource
{
    public SubtitleExportSnapshot Capture(int slot, SubtitleExportText text)
    {
        if ((uint)slot > 1) throw new ArgumentOutOfRangeException(nameof(slot));
        if (!Enum.IsDefined(text)) throw new ArgumentOutOfRangeException(nameof(text));
        var manager = player.SubtitlesManager[slot];
        bool original = text == SubtitleExportText.Original;
        List<SubtitleExportLine> lines = [];
        foreach (var cue in manager.SnapshotSubs())
        {
            if (!cue.IsText || (text == SubtitleExportText.TranslatedOnly && !cue.IsTranslated)) continue;
            // Match the WPF exporter's DisplayText semantics, including hidden translated text.
            string content = (original ? cue.Text : cue.DisplayText) ?? "";
            lines.Add(new(cue.StartTime, cue.EndTime, content, original ? cue.SubStyles?.ToArray() : null));
        }
        var selected = player.Playlist.Selected;
        string? url = selected?.Url;
        bool local = File.Exists(url);
        string name = local ? Path.GetFileNameWithoutExtension(url!) : selected?.Title ?? "LLPlayer";
        const string invalid = "<>:\"/\\|?*";
        name = new string(name.Select(ch => char.IsControl(ch) || invalid.Contains(ch) ? '_' : ch).ToArray()).Trim().TrimEnd('.');
        if (name.Length == 0) name = "LLPlayer";
        return new(lines.ToArray(), name, local ? Path.GetDirectoryName(url) : null, manager.IsLoading);
    }
}