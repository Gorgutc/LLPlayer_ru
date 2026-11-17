using FlyleafLib;
using LLPlayer.ControlApi;

namespace LLPlayer.Avalonia.Services.Automation;

/// <summary>Maps live cue identities to scoped public IDs without exposing the underlying native references.</summary>
internal sealed class ControlCueProjection(FlyleafPlaybackController player)
{
    sealed record Snapshot(long MediaRevision, long Delay, string? Language, bool Enabled,
        Guid Revision, CueInfo[] Cues, IReadOnlyDictionary<Guid, CueInfo> Handles);

    readonly Snapshot?[] slots = new Snapshot?[2];

    public ControlSubtitleSlotSnapshot Describe(int slot)
    {
        Snapshot snapshot = Capture(slot);
        return new(slot, snapshot.Revision, snapshot.Enabled, snapshot.Language, snapshot.Delay, snapshot.Cues.Length);
    }

    public ControlGuiSubtitlePage Read(ControlSubtitlePageRequest request)
    {
        if (request.Cursor < 0 || request.Limit is < 1 or > 500)
            throw new ControlException(new("invalid_arguments", "Use a non-negative cursor and a limit of 1 to 500 cues."));
        Snapshot snapshot = Capture(request.Slot);
        if ((request.Cursor > 0 || request.Revision.HasValue) && request.Revision != snapshot.Revision)
            throw new ControlException(new("stale_revision", "The subtitle document changed. Read its first page again."));
        if (request.Cursor > snapshot.Cues.Length)
            throw new ControlException(new("invalid_arguments", "The cursor is beyond the current subtitle document."));
        ControlGuiCue[] cues = snapshot.Handles.Skip(request.Cursor).Take(request.Limit).Select(ToWireCue).ToArray();
        int end = request.Cursor + cues.Length;
        return new(snapshot.Revision, snapshot.Cues.Length, end < snapshot.Cues.Length ? end : null, cues);
    }

    public ControlGuiSubtitlePage Search(ControlSubtitleSearchRequest request, CancellationToken token)
    {
        string query = request.Query.Trim();
        if (query.Length is < 1 or > 512 || request.Cursor < 0 || request.Limit is < 1 or > 500)
            throw new ControlException(new("invalid_arguments", "Use a query of 1 to 512 characters, a non-negative cursor and a limit of 1 to 500."));
        var searcher = SubtitleSearcher.TryCreate(query, request.MatchCase, request.WholeWord, request.Regex)
            ?? throw new ControlException(new("invalid_search_pattern", "The regular expression is invalid."));
        Snapshot snapshot = Capture(request.Slot);
        if ((request.Cursor > 0 || request.Revision.HasValue) && request.Revision != snapshot.Revision)
            throw new ControlException(new("stale_revision", "The subtitle document changed. Search the first page again."));
        List<ControlGuiCue> page = [];
        int matches = 0;
        foreach (var pair in snapshot.Handles)
        {
            token.ThrowIfCancellationRequested();
            CueInfo cue = pair.Value;
            string? text = request.Text == ControlSubtitleSearchText.Original ? cue.OriginalText ?? cue.Text : cue.Text;
            bool matched = searcher.IsMatch(text, out bool timedOut);
            if (timedOut)
                throw new ControlException(new("search_timed_out", "The search pattern timed out.", Retryable: true));
            if (!matched)
                continue;
            if (matches >= request.Cursor && page.Count < request.Limit)
                page.Add(ToWireCue(pair));
            matches++;
        }
        if (request.Cursor > matches)
            throw new ControlException(new("invalid_arguments", "The cursor is beyond the current search results."));
        int end = request.Cursor + page.Count;
        return new(snapshot.Revision, matches, end < matches ? end : null, page);
    }

    private static ControlGuiCue ToWireCue(KeyValuePair<Guid, CueInfo> pair)
    {
        CueInfo cue = pair.Value;
        return new(pair.Key, cue.Start.Ticks, cue.End.Ticks, cue.Text, cue.OriginalText,
            cue.SourceLanguage, cue.TargetLanguage, cue.IsTranslated, cue.IsBitmap, cue.AssignedVoiceId, cue.SpeakerId);
    }

    public (CueInfo Cue, long Delay) Resolve(ControlSubtitleCueRequest request)
    {
        Snapshot snapshot = Capture(request.Slot);
        if (request.Revision != snapshot.Revision || !snapshot.Handles.TryGetValue(request.CueId, out CueInfo? cue)
            || !player.IsCueCurrent(cue, request.Slot))
            throw new ControlException(new("stale_revision", "Read the current subtitle document before using a cue."));
        return (cue, snapshot.Delay);
    }

    Snapshot Capture(int slot)
    {
        if (slot is < 0 or > 1)
            throw new ControlException(new("invalid_arguments", "The subtitle slot must be 0 (primary) or 1 (secondary)."));
        long mediaRevision = player.ControlMediaRevision;
        long delay = player.Player.Config.Subtitles[slot].Delay;
        string? language = player.GetSubtitleLanguage(slot);
        bool enabled = slot == 0 ? player.PrimarySlotEnabled : player.SecondarySlotEnabled;
        CueInfo[] cues = player.GetCues(slot).ToArray();
        if (mediaRevision != player.ControlMediaRevision || delay != player.Player.Config.Subtitles[slot].Delay)
            throw new ControlException(new("stale_revision", "The subtitle source changed while reading it.", Retryable: true));
        Snapshot? previous = slots[slot];
        if (previous != null && previous.MediaRevision == mediaRevision && previous.Delay == delay
            && previous.Language == language && previous.Enabled == enabled && cues.SequenceEqual(previous.Cues))
            return previous;

        Dictionary<Guid, CueInfo> handles = new(cues.Length);
        Dictionary<SubtitleCueIdentity, Guid> previousIds = [];
        if (previous?.MediaRevision == mediaRevision)
            foreach ((Guid id, CueInfo old) in previous.Handles)
                if (old.Identity != null)
                    previousIds.TryAdd(old.Identity, id);
        foreach (CueInfo cue in cues)
        {
            Guid id = cue.Identity != null && previousIds.TryGetValue(cue.Identity, out Guid found) ? found : Guid.NewGuid();
            handles.Add(id, cue);
        }
        return slots[slot] = new(mediaRevision, delay, language, enabled, Guid.NewGuid(), cues, handles);
    }
}
