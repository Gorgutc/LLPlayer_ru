using System.Text.Json;
using FlyleafLib;
using FlyleafLib.Plugins;
using LLPlayer.Avalonia.ViewModels;
using LLPlayer.ControlApi;

namespace LLPlayer.Avalonia.Services.Automation;

internal sealed partial class ControlGuiExecutor
{
    private sealed record SubtitleDownloadSession(Guid Id, Guid GuiSessionId, OpenSubtitlesClient Client,
        OpenSubtitlesCredentials Credentials, string Query, string Language, OpenSubtitlesSearchPage Page);

    private readonly SemaphoreSlim subtitleDownloadGate = new(1, 1);
    private SubtitleDownloadSession? subtitleDownloadSession;

    async Task<object?> ExecuteExtendedAsync(ControlRequest request, CancellationToken token)
    {
        switch (request.OperationId)
        {
            case "media.recent.list":
                return model.RecentFiles.Select(path =>
                {
                    if (LocalMediaNavigation.TryGetLocalPath(path, out string absolute))
                        return new ControlRecentMediaItem(Path.GetFileName(absolute), absolute, false);
                    if (OnlineVideoRequest.TryGetUrl(path, out string original))
                        path = original;
                    var name = Uri.TryCreate(path, UriKind.Absolute, out Uri? uri) && WordActions.IsHttpUri(uri)
                        ? uri.Host : "Remote media";
                    return new ControlRecentMediaItem(name, null, true);
                }).ToArray();
            case "media.recent.clear":
                model.ClearControlRecent();
                return new ControlGuiAction(true);
            case "media.neighbor.open":
                RequireMedia();
                var neighbor = ReadExtended<ControlNeighborMedia>(request);
                var sibling = LocalMediaNavigation.GetSibling(player.CurrentMediaPath ?? "", neighbor.Next);
                if (sibling is null)
                    throw Unavailable("The current local media has no sibling in that direction.");
                await player.OpenControlMediaAsync(sibling, false, token);
                return Playback();
            case "playlist.next":
                return await OpenRelativePlaylistAsync(1, token);
            case "playlist.previous":
                return await OpenRelativePlaylistAsync(-1, token);
            case "playlist.reopen":
                return await OpenRelativePlaylistAsync(0, token);
            case "subtitles.search":
                return cues.Search(ReadExtended<ControlSubtitleSearchRequest>(request), token);
            case "ab_loop.set":
                RequireMedia();
                var marker = ReadExtended<ControlLoopSet>(request).Marker;
                if (marker == ControlLoopMarker.End)
                {
                    if (player.HasABLoopA && player.CurTime <= player.ABLoopA)
                        throw Invalid("The B marker must follow the A marker.");
                    player.SetABLoopEnd();
                }
                else if (marker == ControlLoopMarker.Start)
                {
                    player.SetABLoopStart();
                }
                else
                {
                    throw Invalid("The loop marker is unknown.");
                }
                return new ControlAbLoopSnapshot(player.HasABLoopA ? player.ABLoopA : null,
                    player.HasABLoopB ? player.ABLoopB : null, player.ABLoopEnabled);
            case "viewport.get":
                return WithCurrentVideo(request.ExpectedRevision, token, config => UpdateViewport(config, null));
            case "viewport.set":
                var geometry = ReadExtended<ControlViewportUpdate>(request);
                return WithCurrentVideo(request.ExpectedRevision, token, config => UpdateViewport(config, geometry));
            case "viewport.filters.get":
                return WithCurrentVideo(request.ExpectedRevision, token, config => UpdateColorFilters(config, null));
            case "viewport.filters.set":
                var filters = ReadExtended<ControlColorFilterUpdate>(request);
                return WithCurrentVideo(request.ExpectedRevision, token, config => UpdateColorFilters(config, filters));
            case "subtitle_download.search":
            case "subtitle_download.page":
            case "subtitle_download.download":
            case "subtitle_download.load":
            case "subtitle_download.clear":
                return await ExecuteSubtitleDownloadAsync(request, token);
            default:
                return null;
        }
    }

    async Task<ControlPlaybackSnapshot> OpenRelativePlaylistAsync(int offset, CancellationToken token)
    {
        RequireMedia();
        var items = player.GetPlaylistItems().ToArray();
        int current = Array.FindIndex(items, item => item.IsCurrent);
        int target = current + offset;
        if (current < 0 || target < 0 || target >= items.Length)
            throw Unavailable("There is no playlist item in that direction.");
        await player.SelectControlPlaylistItemAsync(items[target], token);
        return Playback();
    }

    static T ReadExtended<T>(ControlRequest request) where T : class
        => request.Arguments.Deserialize<T>(ControlProtocol.Json) ?? throw new JsonException();

    private T WithCurrentVideo<T>(long? expectedRevision, CancellationToken token, Func<Config, T> action) where T : class
    {
        RequireMedia();
        var native = player.Player;
        if (!native.Video.IsOpened)
            throw Unavailable("Open a video to use the viewport.");
        long revision = expectedRevision ?? player.ControlMediaRevision;
        long generation = native.MediaGeneration;
        var item = native.Playlist?.Selected;
        T? result = null;
        bool applied = native.TryMutateMedia(generation, item,
            () => revision == player.ControlMediaRevision && native.Video.IsOpened,
            () => result = action(native.Config), token);
        if (!applied || result is null)
            throw new ControlException(new("stale_revision", "The video changed before its viewport could be read or updated."));
        return result;
    }

    private static ControlViewportSnapshot UpdateViewport(Config config, ControlViewportUpdate? update)
    {
        var video = config.Video;
        if (update != null)
        {
            var values = new PlaybackRuntimeSettings
            {
                ZoomPercent = update.ZoomPercent ?? video.Zoom,
                PanX = update.PanX ?? video.PanXOffset,
                PanY = update.PanY ?? video.PanYOffset,
                Rotation = update.Rotation ?? video.Rotation,
                CropTop = update.Crop?.Top ?? video.Crop.Top,
                CropLeft = update.Crop?.Left ?? video.Crop.Left,
                CropBottom = update.Crop?.Bottom ?? video.Crop.Bottom,
                CropRight = update.Crop?.Right ?? video.Crop.Right
            };
            FlyleafPlaybackController.ValidatePlaybackRuntime(values);
            if (update.ZoomPercent is < 0.1 or > 10000)
                throw Invalid("Use a zoom between 0.1 and 10000 percent.");
            var aspect = video.AspectRatio;
            if (update.AspectRatio is { } requested)
            {
                var known = AspectRatio.AspectRatios;
                if (!known.Any(value => string.Equals(value.ToString(), requested, StringComparison.OrdinalIgnoreCase)))
                    throw Invalid("Use an aspect mode from the existing playback settings.");
                aspect = known.First(value => string.Equals(value.ToString(), requested, StringComparison.OrdinalIgnoreCase));
            }
            if (update.ZoomPercent.HasValue) video.Zoom = values.ZoomPercent;
            if (update.PanX.HasValue) video.PanXOffset = values.PanX;
            if (update.PanY.HasValue) video.PanYOffset = values.PanY;
            if (update.Rotation.HasValue) video.Rotation = values.Rotation;
            if (update.HorizontalFlip is { } horizontal) video.HFlip = horizontal;
            if (update.VerticalFlip is { } vertical) video.VFlip = vertical;
            if (update.Crop != null) video.Crop = new(values.CropTop, values.CropLeft, values.CropBottom, values.CropRight);
            if (update.AspectRatio != null) video.AspectRatio = aspect;
        }
        return new(video.Zoom, video.PanXOffset, video.PanYOffset, video.Rotation, video.HFlip, video.VFlip,
            new(video.Crop.Top, video.Crop.Left, video.Crop.Bottom, video.Crop.Right), video.AspectRatio.ToString());
    }

    private ControlColorFilterSnapshot UpdateColorFilters(Config config, ControlColorFilterUpdate? update)
    {
        var current = player.GetPlaybackSettings();
        if (update != null)
        {
            current.Brightness = update.Brightness ?? current.Brightness;
            current.Contrast = update.Contrast ?? current.Contrast;
            current.Hue = update.Hue ?? current.Hue;
            current.Saturation = update.Saturation ?? current.Saturation;
            if (current.Brightness is < -100 or > 100 || current.Contrast is < -100 or > 100
                || current.Hue is < -180 or > 180 || current.Saturation is < -100 or > 100)
                throw Invalid("Colour filter values are outside the playback settings range.");
            current.ApplyColorFiltersTo(config);
            current = player.GetPlaybackSettings();
        }
        return new(current.Brightness, current.Contrast, current.Hue, current.Saturation);
    }

    internal void ClearSubtitleDownloadSession()
    {
        var old = subtitleDownloadSession;
        subtitleDownloadSession = null;
        old?.Client.Dispose();
    }

    private async Task<object> ExecuteSubtitleDownloadAsync(ControlRequest request, CancellationToken token)
    {
        await subtitleDownloadGate.WaitAsync(token);
        try
        {
            Guid guiSession = request.Scope.SessionId
                ?? throw Invalid("Choose an active GUI session for OpenSubtitles operations.");
            if (subtitleDownloadSession is { } old && old.GuiSessionId != guiSession)
                ClearSubtitleDownloadSession();
            switch (request.OperationId)
            {
                case "subtitle_download.search":
                    return await SearchOpenSubtitlesAsync(guiSession, ReadExtended<ControlOpenSubtitleSearch>(request), token);
                case "subtitle_download.page":
                    var next = ReadExtended<ControlOpenSubtitlePageRequest>(request);
                    var current = GetSubtitleDownloadSession(next.SearchId);
                    if (next.Page < 1 || next.Page > current.Page.TotalPages)
                        throw Invalid("Choose a page from the current search result.");
                    var page = await current.Client.SearchAsync(current.Credentials, current.Query, current.Language, next.Page, token);
                    subtitleDownloadSession = current with { Id = Guid.NewGuid(), Page = page };
                    return DescribeSubtitleDownload(subtitleDownloadSession);
                case "subtitle_download.download":
                    return await SaveOpenSubtitleAsync(ReadExtended<ControlOpenSubtitleDownload>(request), token);
                case "subtitle_download.load":
                    return await LoadOpenSubtitleAsync(ReadExtended<ControlOpenSubtitleLoad>(request), token);
                default:
                    ClearSubtitleDownloadSession();
                    return new ControlGuiAction(true);
            }
        }
        catch (HttpRequestException)
        {
            throw new ControlException(new("subtitle_provider_failed", "OpenSubtitles could not complete the request.", Retryable: true));
        }
        finally
        {
            subtitleDownloadGate.Release();
        }
    }

    private async Task<ControlOpenSubtitlePage> SearchOpenSubtitlesAsync(Guid guiSession, ControlOpenSubtitleSearch request,
        CancellationToken token)
    {
        var supplied = request.Credentials;
        if (string.IsNullOrWhiteSpace(supplied.ApiKey) || string.IsNullOrWhiteSpace(supplied.Username)
            || string.IsNullOrEmpty(supplied.Password))
            throw Invalid("Provide an OpenSubtitles application key, username and password.");
        var credentials = new OpenSubtitlesCredentials(supplied.ApiKey.Trim(), supplied.Username.Trim(), supplied.Password);
        var client = new OpenSubtitlesClient();
        try
        {
            var page = await client.SearchAsync(credentials, request.Query, request.Language, cancellationToken: token);
            token.ThrowIfCancellationRequested();
            ClearSubtitleDownloadSession();
            subtitleDownloadSession = new(Guid.NewGuid(), guiSession, client, credentials, request.Query, request.Language, page);
            return DescribeSubtitleDownload(subtitleDownloadSession);
        }
        finally
        {
            if (subtitleDownloadSession?.Client != client)
                client.Dispose();
        }
    }

    private async Task<ControlOpenSubtitleSaved> SaveOpenSubtitleAsync(ControlOpenSubtitleDownload request, CancellationToken token)
    {
        var current = GetSubtitleDownloadSession(request.SearchId);
        var selected = GetSubtitleDownloadFile(current, request.FileId);
        string path = ValidateLocalPath(request.Path);
        string? parent = Path.GetDirectoryName(path);
        if (parent is null || !Directory.Exists(parent))
            throw Invalid("The destination folder must exist.");
        if (!request.Overwrite && File.Exists(path))
            throw new ControlException(new("output_exists", "The destination already exists."));
        var downloaded = await current.Client.DownloadAsync(current.Credentials, selected, token);
        await OpenSubtitlesDownloadViewModel.SaveAsync(path, downloaded.Utf8Bytes, request.Utf8Bom, token, request.Overwrite);
        return new(path);
    }

    private async Task<ControlGuiAction> LoadOpenSubtitleAsync(ControlOpenSubtitleLoad request, CancellationToken token)
    {
        var current = GetSubtitleDownloadSession(request.SearchId);
        var selected = GetSubtitleDownloadFile(current, request.FileId);
        var target = player.CaptureSubtitlePrimaryLoadTarget();
        var downloaded = await current.Client.DownloadAsync(current.Credentials, selected, token);
        var owned = await OwnedSubtitleFile.CreateAsync(downloaded.FileName, downloaded.Utf8Bytes, request.Utf8Bom, token);
        try
        {
            if (!await target(owned, selected.Language, token))
                throw new ControlException(new("stale_revision", "The media changed during the subtitle download."));
            return new(true);
        }
        finally
        {
            if (!owned.RetainedByPlayer && !owned.TryDelete(out _))
                throw new IOException("The temporary downloaded subtitle could not be removed.");
        }
    }

    private SubtitleDownloadSession GetSubtitleDownloadSession(Guid id)
        => subtitleDownloadSession is { } current && current.Id == id ? current
            : throw new ControlException(new("stale_revision", "Start a search again in the current GUI connection."));

    private static OpenSubtitlesResult GetSubtitleDownloadFile(SubtitleDownloadSession current, int id)
        => current.Page.Results.FirstOrDefault(row => row.FileId == id)
            ?? throw new ControlException(new("stale_revision", "Choose a file from the current search page."));

    private static ControlOpenSubtitlePage DescribeSubtitleDownload(SubtitleDownloadSession current)
        => new(current.Id, current.Page.Page, current.Page.TotalPages, current.Page.Results
            .Select(row => new ControlOpenSubtitleFile(row.FileId, row.FileName, row.Language, row.Release, row.DownloadCount,
                row.Rating, row.Year)).ToArray());
}
