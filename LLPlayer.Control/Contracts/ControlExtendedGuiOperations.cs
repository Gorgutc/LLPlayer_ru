namespace LLPlayer.ControlApi;

public sealed record ControlRecentMediaItem(string Name, string? LocalPath, bool Remote);
public sealed record ControlNeighborMedia(bool Next);
public enum ControlLoopMarker { Start, End }
public sealed record ControlLoopSet(ControlLoopMarker Marker);
public enum ControlSubtitleSearchText { Displayed, Original }
public sealed record ControlSubtitleSearchRequest(int Slot, string Query, ControlSubtitleSearchText Text = ControlSubtitleSearchText.Displayed,
    bool MatchCase = false, bool WholeWord = false, bool Regex = false, int Cursor = 0, int Limit = 100, Guid? Revision = null);
public sealed record ControlOpenSubtitleCredentials(string ApiKey, string Username, string Password)
{
    public override string ToString() => "OpenSubtitles credentials";
}
public sealed record ControlOpenSubtitleSearch(ControlOpenSubtitleCredentials Credentials, string Query, string Language = "");
public sealed record ControlOpenSubtitlePageRequest(Guid SearchId, int Page);
public sealed record ControlOpenSubtitleFile(int FileId, string Name, string Language, string Release, int DownloadCount, double Rating, int? Year);
public sealed record ControlOpenSubtitlePage(Guid SearchId, int Page, int TotalPages, IReadOnlyList<ControlOpenSubtitleFile> Files);
public sealed record ControlOpenSubtitleDownload(Guid SearchId, int FileId, string Path, bool Overwrite = false, bool Utf8Bom = true);
public sealed record ControlOpenSubtitleLoad(Guid SearchId, int FileId, bool Utf8Bom = true);
public sealed record ControlOpenSubtitleSaved(string Path);
public sealed record ControlViewportCrop(uint Top, uint Left, uint Bottom, uint Right);
public sealed record ControlViewportSnapshot(double ZoomPercent, double PanX, double PanY, uint Rotation,
    bool HorizontalFlip, bool VerticalFlip, ControlViewportCrop Crop, string AspectRatio);
public sealed record ControlViewportUpdate(double? ZoomPercent = null, double? PanX = null, double? PanY = null,
    uint? Rotation = null, bool? HorizontalFlip = null, bool? VerticalFlip = null, ControlViewportCrop? Crop = null,
    string? AspectRatio = null);
public sealed record ControlColorFilterSnapshot(int Brightness, int Contrast, int Hue, int Saturation);
public sealed record ControlColorFilterUpdate(int? Brightness = null, int? Contrast = null, int? Hue = null, int? Saturation = null);

public static class ControlExtendedGuiOperations
{
    public static IReadOnlyList<ControlGuiOperationSchema> All { get; } =
    [
        Define<ControlGuiOperations.Empty, ControlRecentMediaItem[]>("media.recent.list", "media", "Read recently opened local media paths and remote source names."),
        Define<ControlGuiOperations.Empty, ControlGuiAction>("media.recent.clear", "media", "Clear and save the recent media list.", true),
        Define<ControlNeighborMedia, ControlPlaybackSnapshot>("media.neighbor.open", "media", "Open the next or previous local sibling file.", true),
        Define<ControlGuiOperations.Empty, ControlPlaybackSnapshot>("playlist.next", "playlists", "Open the next current playlist item.", true),
        Define<ControlGuiOperations.Empty, ControlPlaybackSnapshot>("playlist.previous", "playlists", "Open the previous current playlist item.", true),
        Define<ControlGuiOperations.Empty, ControlPlaybackSnapshot>("playlist.reopen", "playlists", "Reopen the current playlist item.", true),
        Define<ControlSubtitleSearchRequest, ControlGuiSubtitlePage>("subtitles.search", "subtitles", "Search the selected subtitle document with the sidebar's matching rules."),
        Define<ControlLoopSet, ControlAbLoopSnapshot>("ab_loop.set", "ab_loop", "Set a loop marker at the current playhead.", true),
        Define<ControlOpenSubtitleSearch, ControlOpenSubtitlePage>("subtitle_download.search", "subtitle_download", "Search OpenSubtitles with credentials retained only by the current GUI connection."),
        Define<ControlOpenSubtitlePageRequest, ControlOpenSubtitlePage>("subtitle_download.page", "subtitle_download", "Read another page of the current OpenSubtitles search."),
        Define<ControlOpenSubtitleDownload, ControlOpenSubtitleSaved>("subtitle_download.download", "subtitle_download", "Download the selected OpenSubtitles result to an explicit local path.", true),
        Define<ControlOpenSubtitleLoad, ControlGuiAction>("subtitle_download.load", "subtitle_download", "Download and load the selected result in the current video's primary subtitle slot.", true),
        Define<ControlGuiOperations.Empty, ControlGuiAction>("subtitle_download.clear", "subtitle_download", "Clear the current OpenSubtitles search and discard its credentials.", true),
        Define<ControlGuiOperations.Empty, ControlViewportSnapshot>("viewport.get", "viewport", "Read the current video viewport geometry and aspect mode."),
        Define<ControlViewportUpdate, ControlViewportSnapshot>("viewport.set", "viewport", "Update current video geometry and read back the live values.", true),
        Define<ControlGuiOperations.Empty, ControlColorFilterSnapshot>("viewport.filters.get", "viewport", "Read the four current portable video color filter values."),
        Define<ControlColorFilterUpdate, ControlColorFilterSnapshot>("viewport.filters.set", "viewport", "Update the current video's portable color filters without saving preferences.", true)
    ];

    private static ControlGuiOperationSchema Define<TArguments, TResult>(
        string id, string family, string description, bool mutates = false)
        => new(new(id, family, description, mutates, Context: ControlOperationContext.Gui), typeof(TArguments), typeof(TResult));
}
