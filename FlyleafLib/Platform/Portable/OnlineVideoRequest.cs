using System.IO;
using System.Threading;

namespace FlyleafLib.Plugins;

/// <summary>Explicit portable extraction requests and their synchronous Flyleaf open scope.</summary>
public static class OnlineVideoRequest
{
    public const string PluginName = "YoutubeDL";
    public const string YtDlpPath = "YtDlpPath";
    public const string DenoPath = "DenoPath";
    const string Prefix = "ytdlp:";
    static readonly AsyncLocal<CancellationToken> cancellation = new();
    // Player.Open dispatches plugins synchronously. A queued native OpenAsync worker must
    // never inherit permission to invoke a helper, even if it captures this execution context.
    [ThreadStatic] static string admittedRequest;

    public static bool TryCreate(string url, out string request)
    {
        // Player.Open inspects the final extension before dispatching to plugins. Terminate
        // the envelope so even a page URL ending in ".vtt" reaches the media-open path.
        request = IsPublicUrl(url) ? Prefix + url + "#" : "";
        return request.Length != 0;
    }

    public static bool TryGetUrl(string request, out string url)
    {
        url = request?.StartsWith(Prefix, StringComparison.Ordinal) == true && request.EndsWith('#')
            ? request[Prefix.Length..^1] : "";
        return IsPublicUrl(url);
    }

    static bool IsPublicUrl(string url) => Utils.IsSafeProcessUrl(url) &&
        Uri.TryCreate(url, UriKind.Absolute, out var parsed) && parsed.UserInfo.Length == 0;

    public static bool IsExecutablePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) || !File.Exists(path))
            return false;

        if (OperatingSystem.IsWindows())
            return Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase);

        try
        {
            return (File.GetUnixFileMode(path) &
                (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static CancellationToken CurrentCancellation => cancellation.Value;

    public static bool TryGetAdmittedUrl(string request, out string url)
    {
        url = "";
        return request != null && request == admittedRequest && TryGetUrl(request, out url);
    }

    public static IDisposable ObserveCancellation(CancellationToken token, string onlineRequest = null)
    {
        var scope = new Scope(cancellation.Value, admittedRequest);
        cancellation.Value = token;
        admittedRequest = onlineRequest;
        return scope;
    }

    sealed class Scope(CancellationToken previous, string previousRequest) : IDisposable
    {
        public void Dispose()
        {
            cancellation.Value = previous;
            admittedRequest = previousRequest;
        }
    }
}
