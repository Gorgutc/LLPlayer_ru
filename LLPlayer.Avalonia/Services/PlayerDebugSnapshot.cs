namespace LLPlayer.Avalonia.Services;

/// <summary>Managed current-state values only. Constructing or displaying this DTO never probes native devices.</summary>
public sealed record PlayerDebugSnapshot(string Status, string MediaTitle, string MediaSource,
    string Position, string Duration, bool IsRecording, string Video, string Audio,
    string Subtitles, string Renderer, string Hardware)
{
    public static string SafeMediaTitle(string? title)
    {
        if (string.IsNullOrEmpty(title)) return "";
        string candidate = title.TrimStart();
        // A title may be copied from an input URL; ordinary authored titles can also contain a colon.
        return candidate.Contains("://", StringComparison.Ordinal) || candidate.StartsWith("//", StringComparison.Ordinal)
            || candidate.StartsWith("file:", StringComparison.OrdinalIgnoreCase)
            ? SafeMediaSource(title) : title;
    }

    public static string SafeMediaSource(string? source)
    {
        if (string.IsNullOrEmpty(source)) return "";
        string candidate = source.TrimStart();
        bool explicitFileUri = candidate.StartsWith("file:", StringComparison.OrdinalIgnoreCase);
        if (!Uri.TryCreate(source, UriKind.Absolute, out Uri? uri))
        {
            int colon = candidate.IndexOf(':');
            bool networkLike = candidate.Contains("://", StringComparison.Ordinal)
                || candidate.StartsWith("//", StringComparison.Ordinal)
                || colon > 1 && Uri.CheckSchemeName(candidate[..colon]);
            return networkLike ? "[source details omitted]" : source;
        }
        if (uri.IsFile && !explicitFileUri)
        {
            // Some URI parsers accept a scheme-less network string as a file URI. Do not let that bypass redaction.
            return candidate.StartsWith("//", StringComparison.Ordinal) ? "[source details omitted]" : source;
        }
        // Capture input names and custom plugin inputs can carry options too; never display their raw payload.
        if (!uri.IsFile && string.IsNullOrEmpty(uri.Host)) return uri.Scheme + ": [source details omitted]";
        try
        {
            return new UriBuilder(uri) { UserName = "", Password = "", Query = "", Fragment = "" }.Uri.AbsoluteUri;
        }
        catch (UriFormatException) { return uri.Scheme + ": [source details omitted]"; }
    }
}
