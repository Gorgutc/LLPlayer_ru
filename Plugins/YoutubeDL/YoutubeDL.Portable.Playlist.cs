#if !WINDOWS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace FlyleafLib.Plugins;

public partial class YoutubeDL
{
    internal sealed record PortablePlaylistData(string Title, int ExpectedCount, List<YoutubeDLJson> Items, int UnavailableCount);
    sealed class PortableMetadata : YoutubeDLJson
    {
        public List<PortableMetadata> entries { get; set; }
    }
    const int MaximumPlaylistItems = 1000;
    const int MaximumMetadataBytes = 32 * 1024 * 1024;

    internal static PortablePlaylistData ParsePortablePlaylist(string json, string requestedUrl)
    {
        if (json.Length > MaximumMetadataBytes) throw new JsonException("Online metadata exceeds the 32 MiB limit.");
        var root = JsonSerializer.Deserialize<PortableMetadata>(json, portableJson)
            ?? throw new JsonException("The extractor returned no metadata.");
        List<YoutubeDLJson> items = [];
        int unavailable = 0, encountered = 0;
        bool playlist = root._type is "playlist" or "multi_video" || root.entries != null;
        void Visit(PortableMetadata data, int depth)
        {
            if (depth > 8) throw new JsonException("Online playlist nesting exceeds the supported limit.");
            if (depth > 0 && ++encountered > MaximumPlaylistItems)
                throw new JsonException("Online playlists are limited to 1000 entries. Select a smaller range with --playlist-items.");
            if (data == null) { unavailable++; return; }
            if (data._type is "playlist" or "multi_video" || data.entries != null)
            {
                if (data.entries == null) return;
                foreach (var entry in data.entries) Visit(entry, depth + 1);
                return;
            }
            if (data._type is "url" or "url_transparent") { unavailable++; return; }
            // An entry page is required for a safe, meaningful Copy/Open path. A root single video
            // may use the explicitly admitted page; a playlist entry must never inherit that parent URL.
            if (!SafeMetadataUrl(data.webpage_url)) data.webpage_url = playlist ? null : requestedUrl;
            if (data.webpage_url == null) { unavailable++; return; }
            data.formats = (data.formats ?? [data]).Where(format => format != null && SafeMetadataUrl(format.url)
                && double.IsFinite(format.width) && format.width is >= 0 and <= 32768
                && double.IsFinite(format.height) && format.height is >= 0 and <= 32768
                && double.IsFinite(format.fps) && format.fps is >= 0 and <= 1000
                && double.IsFinite(format.abr) && format.abr is >= 0 and <= 1000000000000
                && double.IsFinite(format.vbr) && format.vbr is >= 0 and <= 1000000000000
                && double.IsFinite(format.tbr) && format.tbr is >= 0 and <= 1000000000000).ToList();
            if (data.formats.Count == 0) { unavailable++; return; }
            if (!SafeMetadataUrl(data.manifest_url)) data.manifest_url = null;
            foreach (var format in data.formats)
            {
                if (!SafeMetadataUrl(format.manifest_url)) format.manifest_url = null;
                if (format.http_headers != null)
                    format.http_headers = format.http_headers.Where(pair => pair.Key.Length <= 256 && pair.Value?.Length <= 16384
                        && !pair.Key.Any(char.IsControl) && !pair.Value.Any(char.IsControl))
                        .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
                if (format.cookies?.Any(char.IsControl) == true) format.cookies = null;
            }
            if (data.automatic_captions != null)
                foreach (var subtitles in data.automatic_captions.Values)
                    subtitles?.RemoveAll(subtitle => !SafeMetadataUrl(subtitle?.url));
            data.chapters?.RemoveAll(chapter => chapter == null || !double.IsFinite(chapter.start_time)
                || !double.IsFinite(chapter.end_time) || chapter.start_time < 0 || chapter.end_time < chapter.start_time
                || chapter.end_time > TimeSpan.MaxValue.TotalSeconds - 1);
            data.title = CleanMetadataTitle(data.title);
            items.Add(data);
        }
        Visit(root, 0);
        int expected = double.IsFinite(root.playlist_count) && root.playlist_count > 0
            ? (int)Math.Min(root.playlist_count, int.MaxValue) : items.Count + unavailable;
        expected = Math.Max(expected, items.Count + unavailable);
        return new(CleanMetadataTitle(root.playlist_title ?? root.title ?? "Playlist"), expected, items, unavailable);
    }

    static bool SafeMetadataUrl(string url) => Utils.IsSafeProcessUrl(url)
        && Uri.TryCreate(url, UriKind.Absolute, out var parsed) && parsed.UserInfo.Length == 0;
    static string CleanMetadataTitle(string title)
        => title == null ? "" : new string(title.Take(4096).Select(c => char.IsControl(c) ? ' ' : c).ToArray());
}
#endif
