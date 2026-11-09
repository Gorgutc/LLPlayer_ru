#nullable enable
namespace FlyleafLib.MediaFramework.MediaDevice;

/// <summary>Shared demuxer URL syntax. Legacy fmt/device inputs are intentionally not URI-decoded.</summary>
internal static class FmtInputUrl
{
    internal const string CapturePrefix = "capturefmt://";
    internal static bool IsSupported(string url) => url.StartsWith("fmt://") || url.StartsWith("device://") || url.StartsWith("capturefmt://");

    // Dots are escaped too: the existing Player/plugin entrypoints inspect extensions before demuxing.
    internal static string Escape(string value) => Uri.EscapeDataString(value).Replace(".", "%2E", StringComparison.Ordinal);

    internal static bool IsValidBackend(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 64)
            return false;
        foreach (char c in value)
            if (!char.IsAsciiLetterOrDigit(c) && c != '_')
                return false;
        return true;
    }

    internal static void Parse(string url, out string format, out string? input, out Dictionary<string, string>? options)
    {
        if (url.StartsWith(CapturePrefix, StringComparison.Ordinal))
        {
            ParseCapture(url, out format, out input, out options);
            return;
        }

        input = null;
        options = null;
        int formatStarts = url.IndexOf('/') + 2;
        int queryStarts = url.IndexOf('?');
        format = queryStarts == -1 ? url[formatStarts..] : url[formatStarts..queryStarts];
        if (queryStarts != -1)
        {
            string query = url[(queryStarts + 1)..];
            int inputEnds = query.IndexOf('&');
            input = inputEnds == -1 ? query : query[..inputEnds];
            if (inputEnds != -1)
                options = ParseQueryString(query.AsSpan(inputEnds + 1));
        }
        format = format.Replace("/", "");
    }

    private static void ParseCapture(string url, out string format, out string? input, out Dictionary<string, string>? options)
    {
        int queryStarts = url.IndexOf('?', CapturePrefix.Length);
        if (queryStarts == -1)
            throw new FormatException("Capture input URL requires a native input.");
        format = url[CapturePrefix.Length..queryStarts];
        if (!IsValidBackend(format))
            throw new FormatException("Invalid capture backend.");

        ReadOnlySpan<char> query = url.AsSpan(queryStarts + 1);
        int inputEnds = query.IndexOf('&');
        input = DecodeCaptureComponent(inputEnds == -1 ? query : query[..inputEnds]);
        if (input.Length == 0)
            throw new FormatException("Capture input cannot be empty.");

        options = null;
        if (inputEnds == -1)
            return;

        options = [];
        query = query[(inputEnds + 1)..];
        while (true)
        {
            int optionEnds = query.IndexOf('&');
            ReadOnlySpan<char> option = optionEnds == -1 ? query : query[..optionEnds];
            int valueStarts = option.IndexOf('=');
            if (valueStarts <= 0)
                throw new FormatException("Capture options require a nonempty key and a value.");
            string key = DecodeCaptureComponent(option[..valueStarts]);
            string value = DecodeCaptureComponent(option[(valueStarts + 1)..]);
            if (!options.TryAdd(key, value))
                throw new FormatException("Duplicate capture option.");
            if (optionEnds == -1)
                return;
            query = query[(optionEnds + 1)..];
        }
    }

    private static string DecodeCaptureComponent(ReadOnlySpan<char> encoded)
    {
        for (int i = 0; i < encoded.Length; i++)
        {
            if (encoded[i] != '%')
                continue;
            if (i + 2 >= encoded.Length || !char.IsAsciiHexDigit(encoded[i + 1]) || !char.IsAsciiHexDigit(encoded[i + 2]))
                throw new FormatException("Invalid capture percent escape.");
            i += 2;
        }

        string decoded = Uri.UnescapeDataString(encoded.ToString());
        if (decoded.Contains('\0'))
            throw new FormatException("Capture input and options cannot contain null characters.");
        return decoded;
    }
}
