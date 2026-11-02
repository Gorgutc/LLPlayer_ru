using System.Globalization;
using System.Text;
using NMeCab.Specialized;

namespace LLPlayer.Avalonia.Services;

/// <summary>Overlay-only WPF language segmentation, retaining exact UTF-16 offsets into the displayed cue.</summary>
public static class SubtitleWordSegmenter
{
    static readonly Lazy<MeCabIpaDicTagger> tagger = new(() => MeCabIpaDicTagger.Create(), true);
    static readonly object gate = new();
    static readonly Dictionary<(string Language, string Text), (int Start, int End)[]> cache = [];
    const int MaxCachedLines = 256;

    public static bool IsCjkLanguage(string? language) => Normalize(language) is "ja" or "zh";
    static string Normalize(string? language) => (language ?? "").Split('-', '_')[0].ToLowerInvariant();

    public static bool TryGetRange(string text, int index, string? language, out int start, out int end)
    {
        start = end = -1;
        if (index < 0 || index >= text.Length || char.IsWhiteSpace(text[index])) return false;
        string lang = Normalize(language);
        if (lang is not ("ja" or "zh")) return false;
        // The existing tagger and bounded cache are shared. Serializing Parse also protects MeCab's reusable buffers.
        lock (gate)
        {
            var key = (lang, text);
            if (!cache.TryGetValue(key, out var ranges))
            {
                ranges = lang == "ja" ? Japanese(text) : Chinese(text);
                if (cache.Count >= MaxCachedLines) cache.Remove(cache.Keys.First());
                cache.Add(key, ranges);
            }
            foreach (var range in ranges)
                if (index >= range.Start && index < range.End)
                {
                    start = range.Start; end = range.End; return true;
                }
        }
        return false;
    }

    static (int Start, int End)[] Japanese(string text)
    {
        List<(int, int)> result = [];
        int cursor = 0;
        foreach (var node in tagger.Value.Parse(text))
        {
            string surface = node.Surface;
            if (surface.Length == 0) continue;
            // Locate only forward: MeCab excludes intervening whitespace from Surface, while popup offsets include it.
            int offset = text.IndexOf(surface, cursor, StringComparison.Ordinal);
            if (offset < 0) continue;
            cursor = offset + surface.Length;
            if (surface.EnumerateRunes().Any(IsWord)) result.Add((offset, cursor));
        }
        return result.ToArray();
    }

    static (int Start, int End)[] Chinese(string text)
    {
        List<(int, int)> result = [];
        int run = -1, offset = 0;
        foreach (Rune rune in text.EnumerateRunes())
        {
            int length = rune.Utf16SequenceLength;
            bool han = IsHan(rune);
            bool innerJoiner = run >= 0 && (rune.Value is '\'' or '’' or '-')
                && Rune.TryGetRuneAt(text, offset + length, out Rune next) && IsWord(next) && !IsHan(next);
            if (han || !IsWord(rune) && !innerJoiner)
            {
                if (run >= 0) { result.Add((run, offset)); run = -1; }
                if (han) result.Add((offset, offset + length));
            }
            else if (run < 0) run = offset;
            offset += length;
        }
        if (run >= 0) result.Add((run, offset));
        return result.ToArray();
    }

    static bool IsWord(Rune rune) => Rune.IsLetterOrDigit(rune)
        || Rune.GetUnicodeCategory(rune) == UnicodeCategory.NonSpacingMark;
    static bool IsHan(Rune rune) => rune.Value is >= 0x3400 and <= 0x4DBF or >= 0x4E00 and <= 0x9FFF
        or >= 0xF900 and <= 0xFAFF or >= 0x20000 and <= 0x323AF;
}
