using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FlyleafLib.MediaPlayer;

namespace LLPlayer.Core.Automation;

public sealed record ControlCueStyle(string Kind, int Start, int Length, int Argb);

public sealed record ControlCue(Guid CueId, TimeSpan Start, TimeSpan End, string? Text, string? TranslatedText,
    string? Language, bool IsBitmap, string? SpeakerId, string? AssignedVoiceId, ImmutableArray<ControlCueStyle> Styles);

/// <summary>A versioned, detached subtitle document. No native cue or bitmap storage crosses its ownership boundary.</summary>
public sealed record ControlCueDocument(Guid DocumentId, string SourceStamp, string Revision, string? SourceLanguage,
    string? TargetLanguage, ImmutableArray<ControlCue> Cues)
{
    public static ControlCueDocument Capture(Guid documentId, string sourceRevision, string? sourceLanguage,
        IReadOnlyList<SubtitleData> cues, string? targetLanguage = null)
    {
        if (documentId == Guid.Empty) throw new ArgumentException("A document ID is required.", nameof(documentId));
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceRevision);
        ArgumentNullException.ThrowIfNull(cues);

        var copies = ImmutableArray.CreateBuilder<ControlCue>(cues.Count);
        for (int i = 0; i < cues.Count; i++)
        {
            SubtitleData cue = cues[i];
            ImmutableArray<ControlCueStyle> styles = cue.SubStyles is { } nativeStyles
                ? nativeStyles.Select(style => new ControlCueStyle(style.style.ToString(), style.from, style.len,
                    style.value.ToArgb())).ToImmutableArray()
                : [];
            copies.Add(new(CueId(documentId, i), cue.StartTime, cue.EndTime, cue.Text, cue.TranslatedText,
                cue.Language?.ISO6391, cue.IsBitmap, cue.SpeakerId, cue.AssignedVoiceId, styles));
        }
        string sourceStamp = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sourceRevision)));
        return Create(documentId, sourceStamp, sourceLanguage, targetLanguage, copies.MoveToImmutable());
    }

    public ImmutableArray<int> SelectTranslationCueOrdinals(int start, int? count)
    {
        if (Cues.IsDefaultOrEmpty)
            throw new ControlCueException("no_text_cues", "The document contains no translatable subtitle text.");
        if (start < 0 || start >= Cues.Length || count is <= 0 || count > Cues.Length - start)
            throw new ControlCueException("invalid_arguments", "The selected range is outside the subtitle document.");

        var selected = ImmutableArray.CreateBuilder<int>();
        int end = count is int length ? start + length : Cues.Length;
        for (int index = start; index < end; index++)
            if (!string.IsNullOrWhiteSpace(Cues[index].Text))
                selected.Add(index);
        if (selected.Count == 0)
            throw new ControlCueException("no_text_cues", "The selected range contains no translatable subtitle text.");
        return selected.ToImmutable();
    }

    public ControlCueDocument WithTranslation(string targetLanguage, IReadOnlyDictionary<Guid, string> translations)
    {
        string language = ControlTranslationDraft.ValidateLanguage(targetLanguage);
        int translatedCount = 0;
        bool same = string.Equals(TargetLanguage, language, StringComparison.OrdinalIgnoreCase);
        var copies = ImmutableArray.CreateBuilder<ControlCue>(Cues.Length);
        foreach (ControlCue cue in Cues)
        {
            if (translations.TryGetValue(cue.CueId, out string? text))
            {
                if (string.IsNullOrWhiteSpace(cue.Text))
                    throw new ControlCueException("unknown_cue", "The translated cue does not contain source text.");
                ControlTranslationDraft.ValidateText(text);
                translatedCount++;
                same &= text == cue.TranslatedText;
                copies.Add(cue with { TranslatedText = text });
                continue;
            }
            if (cue.TranslatedText is not null && !string.Equals(TargetLanguage, language, StringComparison.OrdinalIgnoreCase))
                throw new ControlCueException("target_language_conflict", "Translate the complete document to change its target language.");
            copies.Add(cue);
        }
        if (translatedCount == 0 || translatedCount != translations.Count)
            throw new ControlCueException("unknown_cue", "At least one translated cue is outside the document.");
        return same ? this : Create(DocumentId, SourceStamp, SourceLanguage, language, copies.MoveToImmutable());
    }

    private static ControlCueDocument Create(Guid id, string sourceStamp, string? sourceLanguage, string? targetLanguage,
        ImmutableArray<ControlCue> cues)
    {
        var revisionData = new CueRevisionData(id, sourceStamp, sourceLanguage, targetLanguage, cues);
        string revision = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(revisionData)));
        return new(id, sourceStamp, revision, sourceLanguage, targetLanguage, cues);
    }

    private static Guid CueId(Guid documentId, int ordinal)
    {
        Span<byte> data = stackalloc byte[20];
        documentId.TryWriteBytes(data[..16]);
        BinaryPrimitives.WriteInt32LittleEndian(data[16..], ordinal);
        Span<byte> sha = stackalloc byte[32];
        SHA256.HashData(data, sha);
        return new Guid(sha[..16]);
    }

    private sealed record CueRevisionData(Guid DocumentId, string SourceStamp, string? SourceLanguage,
        string? TargetLanguage, ImmutableArray<ControlCue> Cues);
}

public sealed class ControlCueException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
