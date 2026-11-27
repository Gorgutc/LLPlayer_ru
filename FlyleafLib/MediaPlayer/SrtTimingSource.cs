using System.Collections.Immutable;
using System.IO;
using System.Text.RegularExpressions;
using FlyleafLib.MediaFramework.MediaFrame;

namespace FlyleafLib.MediaPlayer;

#nullable enable

/// <summary>A detached copy of the loaded metadata for a one-to-one author cue.</summary>
public sealed record SrtTimingCueMetadata(int Id, string? Text, string? TranslatedText, Language? Language,
    string? SpeakerId, string? AssignedVoiceId, ImmutableArray<SubStyle> Styles);

/// <summary>The revision that supplied both the active subtitle stream and its sidebar cache.</summary>
public sealed class SrtTimingSource
{
    private readonly Func<bool> _ownsNativeInput;

    internal SrtTimingSource(string path, SrtTimingDocument document, ImmutableArray<SrtTimingCueLink> links,
        Func<bool> ownsNativeInput)
    {
        Path = path;
        Document = document;
        Links = links;
        _ownsNativeInput = ownsNativeInput;
    }

    public string Path { get; }
    public SrtTimingDocument Document { get; }
    internal ImmutableArray<SrtTimingCueLink> Links { get; }
    internal bool OwnsNativeInput() => _ownsNativeInput();
}

internal readonly record struct SrtTimingCueLink(int Id, SubtitleData Cue, TimeSpan Start, TimeSpan End, string? Text)
{
    private static readonly Regex SrtTags = new(@"</?(?:b|i|u|s|font)(?:[ \t]+[^>\r\n]*)?>",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    public static bool MatchesAuthorText(string authored, string? decoded)
    {
        if (decoded == null)
            return false;

        string withoutTags = SrtTags.Replace(authored, string.Empty);
        return FoldWhitespace(withoutTags) == FoldWhitespace(decoded);
    }

    private static string FoldWhitespace(string value) =>
        string.Join(" ", value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}

/// <summary>The exact input of the local SubRip FFmpeg memory buffer, including an optional BOM.</summary>
internal sealed class SrtTimingInput
{
    private static readonly byte[] Bom = [0xEF, 0xBB, 0xBF];
    private readonly byte[] _bytes;

    private SrtTimingInput(string path, byte[] bytes)
    {
        Path = path;
        _bytes = bytes;
    }

    public string Path { get; }
    public ReadOnlySpan<byte> DecoderBytes => _bytes.AsSpan().StartsWith(Bom) ? _bytes.AsSpan(Bom.Length) : _bytes;

    public static SrtTimingInput? TryRead(string path, int maximumLength)
    {
        try
        {
            using FileStream file = File.OpenRead(path);
            long length = file.Length;
            if (length <= 0 || length >= maximumLength)
                return null;

            byte[] bytes = new byte[(int)length];
            file.ReadExactly(bytes);
            return file.ReadByte() < 0 && SrtTimingDocument.IsSupportedEncoding(bytes)
                ? new(System.IO.Path.GetFullPath(path), bytes)
                : null;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    public SrtTimingDocument? ParseIfSameInput(SrtTimingInput? other)
    {
        if (other == null || !string.Equals(Path, other.Path, OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
            || !_bytes.AsSpan().SequenceEqual(other._bytes))
            return null;

        try { return SrtTimingDocument.Parse(_bytes); }
        catch (InvalidDataException) { return null; }
    }
}
