using System.Text;
using System.Linq;

namespace FlyleafLib.Plugins;

/// <summary>Extraction-only yt-dlp options. No command shell, config/plugin loading or file-producing switches.</summary>
public static class OnlineVideoArguments
{
    public const int MaxLength = 16384;
    static readonly HashSet<string> values = new(StringComparer.Ordinal)
    {
        "--cookies", "--cookies-from-browser", "--proxy", "--socket-timeout", "--source-address",
        "--user-agent", "--referer", "--add-headers", "--username", "--password", "--video-password",
        "--twofactor", "--netrc-location", "--client-certificate", "--client-certificate-key",
        "--client-certificate-password", "--extractor-args", "--geo-bypass-country", "--geo-bypass-ip-block",
        "--format", "-f", "--format-sort", "-S", "--playlist-items", "-I", "--playlist-start",
        "--playlist-end", "--retries", "--extractor-retries", "--retry-sleep", "--impersonate"
    };
    static readonly HashSet<string> switches = new(StringComparer.Ordinal)
    {
        "--yes-playlist", "--no-playlist", "--playlist-reverse", "--no-playlist-reverse",
        "--ignore-errors", "--no-abort-on-error", "--geo-bypass", "--no-geo-bypass",
        "--no-check-certificates", "--prefer-free-formats", "--no-prefer-free-formats",
        "--format-sort-force", "--no-format-sort-force", "--netrc"
    };

    public static IReadOnlyList<string> Parse(string text)
    {
        if (text == null || text.Length > MaxLength || text.Any(c => char.IsControl(c) && c != '\t'))
            throw new ArgumentException("Extra arguments must be one line of at most 16384 characters.");
        List<string> tokens = [];
        StringBuilder current = new();
        char quote = '\0';
        bool active = false;
        foreach (char c in text)
        {
            if (quote != '\0')
            {
                if (c == quote) quote = '\0'; else current.Append(c);
                active = true;
            }
            else if (c is '\'' or '"') { quote = c; active = true; }
            else if (char.IsWhiteSpace(c))
            {
                if (active) { tokens.Add(current.ToString()); current.Clear(); active = false; }
            }
            else { current.Append(c); active = true; }
        }
        if (quote != '\0') throw new ArgumentException("Extra arguments contain an unclosed quote.");
        if (active) tokens.Add(current.ToString());
        if (tokens.Count > 256) throw new ArgumentException("Extra arguments contain too many options.");
        List<string> result = [];
        int? playlistStart = null, playlistEnd = null;
        bool playlistItems = false;
        for (int i = 0; i < tokens.Count; i++)
        {
            string token = tokens[i];
            int equals = token.IndexOf('=');
            string option = equals < 0 ? token : token[..equals];
            if (switches.Contains(option) && equals < 0) { result.Add(option); continue; }
            if (!values.Contains(option))
                throw new ArgumentException("An extra argument is unsupported or can change extractor process/file ownership. Use extraction, network or authentication options only.");
            string value = equals >= 0 ? token[(equals + 1)..] : ++i < tokens.Count ? tokens[i] : "";
            if (string.IsNullOrEmpty(value) || value.StartsWith('-'))
                throw new ArgumentException("An extra argument is missing its value.");
            if (option is "--playlist-items" or "-I") { ValidatePlaylistRange(value); playlistItems = true; }
            if (option is "--playlist-start" or "--playlist-end")
            {
                if (!int.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int index) || index < 1)
                    throw new ArgumentException("Playlist start and end must be positive item numbers.");
                if (option == "--playlist-start") playlistStart = index; else playlistEnd = index;
                continue;
            }
            result.Add(option); result.Add(value);
        }
        if (playlistStart != null || playlistEnd != null)
        {
            if (playlistItems) throw new ArgumentException("Use either playlist items or playlist start/end, rather than both.");
            int start = playlistStart ?? 1;
            long end = playlistEnd ?? Math.Min(int.MaxValue, (long)start + 999);
            string range = start.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + end.ToString(System.Globalization.CultureInfo.InvariantCulture);
            ValidatePlaylistRange(range);
            result.Add("--playlist-items"); result.Add(range);
        }
        return result;
    }

    static void ValidatePlaylistRange(string value)
    {
        long count = 0;
        foreach (string segment in value.Split(','))
        {
            string[] parts = segment.Split(':');
            if (parts.Length is < 1 or > 3) throw new ArgumentException("Playlist items must be positive numbers or closed start:end[:step] ranges.");
            int[] indices = new int[parts.Length];
            for (int i = 0; i < parts.Length; i++)
                if (!int.TryParse(parts[i], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out indices[i]) || indices[i] < 1)
                    throw new ArgumentException("Playlist items must be positive numbers or closed start:end[:step] ranges.");
            int start = indices[0], end = indices.Length > 1 ? indices[1] : start, step = indices.Length > 2 ? indices[2] : 1;
            if (end < start) throw new ArgumentException("Playlist item range end must be at or after its start.");
            count += ((long)end - start) / step + 1;
            if (count > 1000) throw new ArgumentException("Select at most 1000 playlist entries per request.");
        }
    }
}
