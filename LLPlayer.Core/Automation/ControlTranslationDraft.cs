using System.Collections.Immutable;

namespace LLPlayer.Core.Automation;

public sealed record ControlCueTranslation(Guid CueId, string Text);

public sealed record ControlTranslationPage(Guid DocumentId, string Revision, string? SourceLanguage,
    string TargetLanguage, ImmutableArray<ControlCue> Cues, int TotalCount, int ReceivedCount, int? NextOffset);

/// <summary>Serializable checkpoint for accepting translated text against exactly one ordered source revision.</summary>
public sealed record ControlTranslationDraft(ControlCueDocument Source, string TargetLanguage,
    ImmutableArray<int> CueOrdinals, ImmutableDictionary<Guid, string> Submitted)
{
    public const int MaximumPageSize = 128;
    public const int MaximumCueCharacters = 16_384;

    public static ControlTranslationDraft Start(ControlCueDocument source, string targetLanguage,
        int start = 0, int? count = null)
    {
        string language = ValidateLanguage(targetLanguage);
        var ordinals = source.SelectTranslationCueOrdinals(start, count);
        return new(source, language, ordinals, ImmutableDictionary<Guid, string>.Empty);
    }

    public ControlTranslationPage Read(string expectedRevision, int offset, int limit)
    {
        RequireRevision(expectedRevision);
        if (offset < 0 || offset > CueOrdinals.Length || limit is < 1 or > MaximumPageSize)
            throw new ControlCueException("invalid_arguments", "The cue page range is invalid.");
        int end = offset + Math.Min(limit, CueOrdinals.Length - offset);
        var page = ImmutableArray.CreateBuilder<ControlCue>(end - offset);
        for (int i = offset; i < end; i++) page.Add(Source.Cues[CueOrdinals[i]]);
        return new(Source.DocumentId, Source.Revision, Source.SourceLanguage, TargetLanguage, page.MoveToImmutable(),
            CueOrdinals.Length, Submitted.Count, end < CueOrdinals.Length ? end : null);
    }

    public ControlTranslationDraft Submit(string expectedRevision, IReadOnlyList<ControlCueTranslation> translations)
    {
        RequireRevision(expectedRevision);
        if (translations is null || translations.Count is < 1 or > MaximumPageSize)
            throw new ControlCueException("invalid_arguments", "Submit a nonempty bounded page of translated cues.");

        var selectedIds = CueOrdinals.Select(ordinal => Source.Cues[ordinal].CueId).ToHashSet();
        var submittedIds = new HashSet<Guid>();
        var pending = Submitted.ToBuilder();
        foreach (ControlCueTranslation entry in translations)
        {
            if (entry is null || !selectedIds.Contains(entry.CueId))
                throw new ControlCueException("unknown_cue", "At least one translated cue is outside the selected source range.");
            if (!submittedIds.Add(entry.CueId))
                throw new ControlCueException("duplicate_cue", "A cue occurs more than once in the submitted page.");
            ValidateText(entry.Text);
            if (pending.TryGetValue(entry.CueId, out string? previous) && previous != entry.Text)
                throw new ControlCueException("translation_conflict", "A cue already has different draft text.");
            pending[entry.CueId] = entry.Text;
        }

        return pending.Count == Submitted.Count ? this : this with { Submitted = pending.ToImmutable() };
    }

    public ControlCueDocument Commit(string expectedRevision, ControlCueDocument current)
    {
        RequireRevision(expectedRevision);
        if (current.DocumentId != Source.DocumentId || current.Revision != Source.Revision)
            throw new ControlCueException("revision_conflict", "The source document changed before the translation was applied.");
        if (Submitted.Count != CueOrdinals.Length)
            throw new ControlCueException("translation_incomplete", "Submit translations for every selected cue before committing.");
        return current.WithTranslation(TargetLanguage, Submitted);
    }

    internal static string ValidateLanguage(string language)
    {
        if (string.IsNullOrWhiteSpace(language) || language.Length > 35 || language.Any(char.IsWhiteSpace))
            throw InvalidLanguage();
        string[] parts = language.Split('-');
        if (parts[0].Length is < 2 or > 8 || !parts[0].All(char.IsAsciiLetter)
            || parts.Skip(1).Any(part => part.Length is < 1 or > 8 || !part.All(char.IsAsciiLetterOrDigit)))
            throw InvalidLanguage();
        return language.ToLowerInvariant();
    }

    internal static void ValidateText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > MaximumCueCharacters || text.Contains('\0'))
            throw new ControlCueException("invalid_translation", "A translation must contain bounded, nonempty subtitle text.");
    }

    private static ControlCueException InvalidLanguage() =>
        new("invalid_arguments", "A target language code is required, for example fr or pt-BR.");

    private void RequireRevision(string expectedRevision)
    {
        if (expectedRevision != Source.Revision)
            throw new ControlCueException("revision_conflict", "The supplied source revision does not match the translation draft.");
    }
}
