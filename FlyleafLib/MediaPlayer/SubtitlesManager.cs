using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using FlyleafLib.MediaFramework.MediaDecoder;
using FlyleafLib.MediaFramework.MediaDemuxer;
using FlyleafLib.MediaFramework.MediaFrame;
using FlyleafLib.MediaFramework.MediaStream;
using FlyleafLib.MediaPlayer.Dubbing;
using FlyleafLib.MediaPlayer.Translation;

namespace FlyleafLib.MediaPlayer;

#nullable enable

public class SubtitlesManager
{
    private readonly SubManager[] _subManagers;
    public SubManager this[int subIndex] => _subManagers[subIndex];
    private readonly int _subNum;

    public SubtitlesManager(Config config, int subNum)
    {
        _subNum = subNum;
        _subManagers = new SubManager[subNum];
        for (int i = 0; i < subNum; i++)
        {
            _subManagers[i] = new SubManager(config, i);
        }
    }

    /// <summary>
    /// Open a file and read all subtitle data by streaming
    /// </summary>
    /// <param name="subIndex">0: Primary, 1: Secondary</param>
    /// <param name="url">subtitle file path or video file path</param>
    /// <param name="streamIndex">streamIndex of subtitle</param>
    /// <param name="type">demuxer media type</param>
    /// <param name="useBitmap">Use bitmap subtitles or immediately release bitmap if not used</param>
    /// <param name="lang">subtitle language</param>
    public void Open(int subIndex, string url, int streamIndex, MediaType type, bool useBitmap, Language lang)
    {
        // TODO: L: Add caching subtitle data for the same stream and URL?
        this[subIndex].Open(url, streamIndex, type, useBitmap, lang);
    }

    public void SetCurrentTime(TimeSpan currentTime)
    {
        for (int i = 0; i < _subNum; i++)
        {
            this[i].SetCurrentTime(currentTime);
        }
    }

    /// <summary>Updates a cue only if it still belongs to one of the current subtitle tracks.</summary>
    public bool TrySetAssignedVoiceId(SubtitleData sub, string? voiceId)
    {
        ArgumentNullException.ThrowIfNull(sub);

        foreach (SubManager manager in _subManagers)
        {
            if (manager.TrySetAssignedVoiceId(sub, voiceId))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Captures both tracks in track order together with per-track generations. Call
    /// <see cref="IsVoiceAssignmentSnapshotCurrent"/> before enqueueing to reject a concurrent load/edit.
    /// </summary>
    internal List<SubtitleData> SnapshotVoiceAssignments(out long[] generations)
    {
        generations = new long[_subManagers.Length];
        List<SubtitleData> assigned = [];
        for (int i = 0; i < _subManagers.Length; i++)
        {
            assigned.AddRange(_subManagers[i].SnapshotVoiceAssignments(out long generation));
            generations[i] = generation;
        }

        return assigned;
    }

    internal bool IsVoiceAssignmentSnapshotCurrent(IReadOnlyList<long> generations)
    {
        if (generations.Count != _subManagers.Length)
            return false;

        for (int i = 0; i < _subManagers.Length; i++)
        {
            if (!_subManagers[i].IsVoiceAssignmentGenerationCurrent(generations[i]))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Retries a compact capture when a concurrent subtitle mutation invalidates its generations. A false context
    /// predicate aborts immediately (for example, when the owning media changed); null means no stable capture.
    /// </summary>
    public List<SubtitleData>? TrySnapshotVoiceAssignments(Func<bool> contextIsCurrent, int maxAttempts)
    {
        ArgumentNullException.ThrowIfNull(contextIsCurrent);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxAttempts);

        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            List<SubtitleData> assigned = SnapshotVoiceAssignments(out long[] generations);
            if (!contextIsCurrent())
                return null;

            if (IsVoiceAssignmentSnapshotCurrent(generations))
                return assigned;
        }

        return null;
    }
}

public class SubManager : INotifyPropertyChanged
{
    private readonly Lock _locker = new();
    private CancellationTokenSource? _cts;
    public SubtitleData? SelectedSub { get; set => Set(ref field, value); }
    public int CurrentIndex { get; private set => Set(ref field, value); } = -1;

    public PositionState State
    {
        get;
        private set
        {
            bool prevIsDisplaying = IsDisplaying;
            if (Set(ref field, value) && prevIsDisplaying != IsDisplaying)
            {
                OnPropertyChanged(nameof(IsDisplaying));
            }
        }
    } = PositionState.First;

    public bool IsDisplaying => State == PositionState.Showing;

    /// <summary>
    /// List of subtitles that can be bound to ItemsControl
    /// Must be sorted with timestamp to perform binary search.
    /// </summary>
    public BulkObservableCollection<SubtitleData> Subs { get; } = new();

    /// <summary>
    /// True when addition to Subs is running... (Reading all subtitles, OCR, ASR)
    /// </summary>
    public bool IsLoading { get; private set => Set(ref field, value); }

    // LanguageSource with fallback
    public Language? Language
    {
        get
        {
            if (LanguageSource == Language.Unknown)
            {
                // fallback to user set language
                return _subIndex == 0 ? _config.Subtitles.LanguageFallbackPrimary : _config.Subtitles.LanguageFallbackSecondary;
            }

            return LanguageSource;
        }
    }

    public Language? LanguageSource
    {
        get;
        set
        {
            if (Set(ref field, value))
            {
                OnPropertyChanged(nameof(Language));
            }
        }
    }

    // For displaying bitmap subtitles, manage video width and height
    public int Width { get; internal set; }
    public int Height { get; internal set; }

    private readonly object _subsLocker = new();
    private readonly HashSet<SubtitleData> _voiceAssignedSubs = [];
    private long _voiceAssignmentGeneration;
    private readonly Config _config;
    private readonly int _subIndex;
    private readonly SubTranslator _subTranslator;
    private readonly LogHandler Log;

    public SubManager(Config config, int subIndex)
    {
        _config = config;
        _subIndex = subIndex;
        // TODO: L: Review whether to initialize it here.
        _subTranslator = new SubTranslator(this, config.Subtitles, subIndex);
        Log = new LogHandler(("[#1]").PadRight(8, ' ') + $" [SubManager{subIndex + 1}   ] ");

        // Enable binding to ItemsControl
        UIInvokeIfRequired(() =>
        {
            BindingOperations.EnableCollectionSynchronization(Subs, _subsLocker);
        });
    }

    public enum PositionState
    {
        First,   // still haven't reached the first subtitle
        Showing, // currently displaying
        Around,  // not displayed and can seek before and after
        Last     // After the last subtitle
    }

    /// <summary>
    /// Force UI refresh
    /// </summary>
    internal void Refresh()
    {
        // NOTE: If it is not executed in the main thread, the following error occurs.
        // System.NotSupportedException: 'This type of CollectionView does not support'
        UI(() =>
        {
            CollectionViewSource.GetDefaultView(Subs).Refresh();
            OnPropertyChanged(nameof(CurrentIndex)); // required for translating current sub
        });
    }

    /// <summary>
    /// HC-17: called after the translation-display toggle (SubConfig.EnabledTranslated) flips DisplayText on every cue.
    /// Per-cue INPC (SubtitleData.DisplayText/UseTranslated) already refreshes the bound, visible sidebar rows, so the
    /// full ListCollectionView rebuild that Refresh() does is avoided in the common case. Two things INPC does not
    /// cover are handled here: (1) an ACTIVE sidebar search filter matches against DisplayText (SubtitlesSidebarVM.
    /// SubFilter), so its membership must follow the flipped text — re-run it, but only when a filter is set (skipped
    /// when there is no search, which is the whole perf win); (2) the CurrentIndex nudge Refresh() used to raise
    /// (re-translates the current sub).
    /// </summary>
    internal void RefreshAfterTranslationToggle()
    {
        // Same main-thread requirement as Refresh (the CollectionView notifications must marshal to the UI thread).
        UI(() =>
        {
            var view = CollectionViewSource.GetDefaultView(Subs);
            if (view.Filter != null)
            {
                // A search is active; re-run it so rows follow the new DisplayText (the same call Refresh() made).
                view.Refresh();
            }

            OnPropertyChanged(nameof(CurrentIndex)); // required for translating current sub
        });
    }

    /// <summary>
    /// This must be called when doing heavy operation
    /// </summary>
    /// <returns></returns>
    internal IDisposable StartLoading()
    {
        IsLoading = true;

        return Disposable.Create(() =>
        {
            IsLoading = false;
        });
    }

    public void Load(IEnumerable<SubtitleData> items)
    {
        List<SubtitleData> loaded = [.. items];
        lock (_subsLocker)
        {
            CurrentIndex = -1;
            SelectedSub = null;
            Subs.Clear();
            Subs.AddRange(loaded);
            ReindexSubtitlesLocked();
            RebuildVoiceAssignmentIndexLocked();
            _voiceAssignmentGeneration++;
        }
    }

    public void Add(SubtitleData sub)
    {
        lock (_subsLocker)
        {
            sub.Index = Subs.Count;
            Subs.Add(sub);
            if (TrackVoiceAssignmentLocked(sub))
                _voiceAssignmentGeneration++;
        }
    }

    public void AddRange(IEnumerable<SubtitleData> items)
    {
        List<SubtitleData> added = [.. items];
        lock (_subsLocker)
        {
            int nextIndex = Subs.Count;
            foreach (SubtitleData sub in added)
                sub.Index = nextIndex++;

            Subs.AddRange(added);
            bool addedAssignment = false;
            foreach (SubtitleData sub in added)
                addedAssignment |= TrackVoiceAssignmentLocked(sub);
            if (addedAssignment)
                _voiceAssignmentGeneration++;
        }
    }

    /// <summary>
    /// A thread-safe copy of the current cues. Enumerating <see cref="Subs"/> directly off the owning code path is
    /// unsafe: it is mutated on background ASR/OCR threads under <c>_subsLocker</c> (the same lock WPF uses via
    /// <c>EnableCollectionSynchronization</c>), so take the copy under that lock. The <see cref="SubtitleData"/>
    /// references are shared (not cloned), so a caller that mutates a returned cue mutates the live cue.
    /// </summary>
    public List<SubtitleData> SnapshotSubs()
    {
        lock (_subsLocker)
        {
            return Subs.ToList();
        }
    }

    /// <summary>
    /// Captures only cues that currently override the default dubbing voice. The assigned-cue index is maintained
    /// incrementally by load/add/restore/sidebar-edit paths, so an interactive save is O(k overrides), not O(n cues).
    /// Returned cues are minimal owned clones and may be handed directly to a background save queue.
    /// </summary>
    internal List<SubtitleData> SnapshotVoiceAssignments()
        => SnapshotVoiceAssignments(out _);

    internal List<SubtitleData> SnapshotVoiceAssignments(out long generation)
    {
        lock (_subsLocker)
        {
            generation = _voiceAssignmentGeneration;
            return _voiceAssignedSubs
                .OrderBy(sub => sub.Index)
                .ThenBy(sub => sub.StartTime)
                .ThenBy(sub => sub.EndTime)
                .Select(sub => new SubtitleData
                {
                    StartTime = sub.StartTime,
                    EndTime = sub.EndTime,
                    AssignedVoiceId = sub.AssignedVoiceId,
                })
                .ToList();
        }
    }

    /// <summary>Updates a live cue only when it still belongs to this manager.</summary>
    internal bool TrySetAssignedVoiceId(SubtitleData sub, string? voiceId)
    {
        ArgumentNullException.ThrowIfNull(sub);

        lock (_subsLocker)
        {
            if (sub.Index < 0 || sub.Index >= Subs.Count || !ReferenceEquals(Subs[sub.Index], sub))
                return false;

            string? normalized = string.IsNullOrWhiteSpace(voiceId) ? null : voiceId.Trim();
            bool changed = !string.Equals(sub.AssignedVoiceId, normalized, StringComparison.Ordinal);
            sub.AssignedVoiceId = normalized;
            TrackVoiceAssignmentLocked(sub);
            if (changed)
                _voiceAssignmentGeneration++;
            return true;
        }
    }

    internal bool IsVoiceAssignmentGenerationCurrent(long generation)
    {
        lock (_subsLocker)
        {
            return _voiceAssignmentGeneration == generation;
        }
    }

    /// <summary>Applies fill-empty persisted values and rebuilds the compact index as one locked operation.</summary>
    internal void ApplyVoiceAssignments(string mediaPath, IDubbingVoiceAssignmentProvider assignments)
    {
        ArgumentNullException.ThrowIfNull(assignments);

        lock (_subsLocker)
        {
            if (Subs.Count == 0)
                return;

            Dictionary<SubtitleData, string?> before = _voiceAssignedSubs.ToDictionary(
                sub => sub,
                sub => sub.AssignedVoiceId);
            assignments.Apply(mediaPath, Subs.ToList());
            RebuildVoiceAssignmentIndexLocked();
            bool changed = before.Count != _voiceAssignedSubs.Count
                           || _voiceAssignedSubs.Any(sub =>
                               !before.TryGetValue(sub, out string? oldVoice)
                               || !string.Equals(oldVoice, sub.AssignedVoiceId, StringComparison.Ordinal));
            if (changed)
                _voiceAssignmentGeneration++;
        }
    }

    private void ReindexSubtitlesLocked()
    {
        for (int i = 0; i < Subs.Count; i++)
            Subs[i].Index = i;
    }

    private void RebuildVoiceAssignmentIndexLocked()
    {
        _voiceAssignedSubs.Clear();
        foreach (SubtitleData sub in Subs)
            TrackVoiceAssignmentLocked(sub);
    }

    private bool TrackVoiceAssignmentLocked(SubtitleData sub)
    {
        if (string.IsNullOrWhiteSpace(sub.AssignedVoiceId))
        {
            _voiceAssignedSubs.Remove(sub);
            return false;
        }

        _voiceAssignedSubs.Add(sub);
        return true;
    }

    public SubtitleData? GetCurrent()
    {
        lock (_subsLocker)
        {
            if (Subs.Count == 0 || CurrentIndex == -1)
            {
                return null;
            }

            Debug.Assert(CurrentIndex < Subs.Count);

            if (State == PositionState.Showing)
            {
                return Subs[CurrentIndex];
            }

            return null;
        }
    }

    public SubtitleData? GetNext()
    {
        lock (_subsLocker)
        {
            if (Subs.Count == 0)
            {
                return null;
            }

            switch (State)
            {
                case PositionState.First:
                    return Subs[0];

                case PositionState.Showing:
                    if (CurrentIndex < Subs.Count - 1)
                        return Subs[CurrentIndex + 1];
                    break;

                case PositionState.Around:
                    if (CurrentIndex < Subs.Count - 1)
                        return Subs[CurrentIndex + 1];
                    break;
            }

            return null;
        }
    }

    public SubtitleData? GetPrev()
    {
        lock (_subsLocker)
        {
            if (Subs.Count == 0 || CurrentIndex == -1)
                return null;

            switch (State)
            {
                case PositionState.Showing:
                    if (CurrentIndex > 0)
                        return Subs[CurrentIndex - 1];
                    break;

                case PositionState.Around:
                    if (CurrentIndex >= 0)
                        return Subs[CurrentIndex];
                    break;

                case PositionState.Last:
                    return Subs[^1];
            }
        }

        return null;
    }

    // Snapshots the surrounding subtitle source texts for the focal cue under the same lock that guards every
    // Subs mutation (_subsLocker), so a context read cannot tear against concurrent ASR Add/Clear/Load on the
    // background consumer thread. Returns the raw (un-flattened) Text of up to `before` preceding and `after`
    // following non-empty cues, nearest-first in playback order. Returns empty lists when the focal cue is no
    // longer at its recorded index (the list was reloaded/cleared). Flattening is left to the caller so the
    // critical section stays minimal.
    internal (List<string> before, List<string> after) GetContextWindow(SubtitleData focal, int before, int after)
    {
        List<string> beforeList = new();
        List<string> afterList = new();

        lock (_subsLocker)
        {
            int idx = focal.Index;
            if (idx < 0 || idx >= Subs.Count || !ReferenceEquals(Subs[idx], focal))
            {
                return (beforeList, afterList);
            }

            for (int i = Math.Max(0, idx - before); i < idx; i++)
            {
                string? t = Subs[i].Text;
                if (!string.IsNullOrWhiteSpace(t))
                {
                    beforeList.Add(t);
                }
            }

            for (int i = idx + 1; i <= Math.Min(Subs.Count - 1, idx + after); i++)
            {
                string? t = Subs[i].Text;
                if (!string.IsNullOrWhiteSpace(t))
                {
                    afterList.Add(t);
                }
            }
        }

        return (beforeList, afterList);
    }

    private readonly SubtitleData _searchSub = new();

    public SubManager SetCurrentTime(TimeSpan currentTime)
    {
        // Adjust the display timing of subtitles by adjusting the timestamp of the video
        currentTime = currentTime.Subtract(new TimeSpan(_config.Subtitles[_subIndex].Delay));

        lock (_subsLocker)
        {
            // If no subtitle data is loaded, nothing is done.
            if (Subs.Count == 0)
                return this;

            // If it is a subtitle that is displaying, it does nothing.
            var curSub = GetCurrent();
            if (curSub != null && curSub.StartTime < currentTime && curSub.EndTime > currentTime)
            {
                return this;
            }

            _searchSub.StartTime = currentTime;

            int ret = Subs.BinarySearch(_searchSub, SubtitleTimeStartComparer.Instance);
            int cur = -1;

            if (~ret == 0)
            {
                CurrentIndex = -1;
                SelectedSub = null;
                State = PositionState.First;
                return this;
            }

            if (ret < 0)
            {
                // The reason subtracting 1 is that the result of the binary search is the next big index.
                cur = (~ret) - 1;
            }
            else
            {
                // If the starting position is matched, it is unlikely
                cur = ret;
            }

            Debug.Assert(cur >= 0, "negative index detected");
            Debug.Assert(cur < Subs.Count, "out of bounds detected");

            if (cur == Subs.Count - 1)
            {
                if (Subs[cur].EndTime < currentTime)
                {
                    CurrentIndex = cur;
                    SelectedSub = Subs[cur];
                    State = PositionState.Last;
                }
                else
                {
                    CurrentIndex = cur;
                    SelectedSub = Subs[cur];
                    State = PositionState.Showing;
                }
            }
            else
            {
                if (Subs[cur].StartTime <= currentTime && Subs[cur].EndTime >= currentTime)
                {
                    // Show subtitles
                    CurrentIndex = cur;
                    SelectedSub = Subs[cur];
                    State = PositionState.Showing;
                }
                else if (Subs[cur].StartTime <= currentTime)
                {
                    // Almost there to display in currentIndex.
                    CurrentIndex = cur;
                    SelectedSub = Subs[cur];
                    State = PositionState.Around;
                }
            }
        }

        return this;
    }

    public void Sort()
    {
        lock (_subsLocker)
        {
            if (Subs.Count == 0)
                return;

            Subs.Sort(SubtitleTimeStartComparer.Instance);
            ReindexSubtitlesLocked();
            RebuildVoiceAssignmentIndexLocked();
            _voiceAssignmentGeneration++;
        }
    }

    public void DeleteAfter(TimeSpan time)
    {
        lock (_subsLocker)
        {
            if (Subs.Count == 0)
                return;

            int index = Subs.BinarySearch(new SubtitleData { EndTime = time }, new SubtitleTimeEndComparer());

            if (index < 0)
            {
                index = ~index;
            }

            if (index < Subs.Count)
            {
                var newSubs = Subs.GetRange(0, index).ToList();
                var deleteSubs = Subs.GetRange(index, Subs.Count - index).ToList();
                Load(newSubs);

                foreach (var sub in deleteSubs)
                {
                    sub.Dispose();
                }
            }
        }
    }

    public void Open(string url, int streamIndex, MediaType type, bool useBitmap, Language lang)
    {
        // Asynchronously read subtitle timestamps and text

        // Cancel if already executed
        TryCancelWait();

        lock (_locker)
        {
            using var loading = StartLoading();

            List<SubtitleData> subChunk = new();

            try
            {
                _cts = new CancellationTokenSource();
                using SubtitleReader reader = new(this, _config, _subIndex);
                reader.Open(url, streamIndex, type, _cts.Token);

                _cts.Token.ThrowIfCancellationRequested();

                bool isFirst = true;
                int subCnt = 0;

                Stopwatch refreshSw = new();
                refreshSw.Start();

                reader.ReadAll(useBitmap, data =>
                {
                    if (isFirst)
                    {
                        isFirst = false;
                        // Set the language at the timing of the first subtitle data set.
                        LanguageSource = lang;

                        Log.Info($"Start loading subs... (lang:{lang.TopEnglishName})");
                    }

                    // F-01: universal re-segmentation — split an over-long loaded/sidecar/embedded TEXT cue into
                    // short, capped-line cues (line/character overflow, proportional timings), gated by
                    // ResegmentSubtitles (default on). Bitmap and styled (ASS) cues pass through unchanged; a cue
                    // that already fits is left untouched, so well-formatted subtitles keep their authored timing.
                    foreach (SubtitleData cue in ResegmentLoaded(
                                 data, _config.Subtitles.ResegmentSubtitles, _config.Subtitles.SubtitleSegmentOptions))
                    {
                        cue.Index = subCnt++;
                        subChunk.Add(cue);
                    }

                    // Large files and network files take time to load to the end.
                    // To prevent frequent UI updates, use AddRange to group files to some extent before adding them.
                    if (subChunk.Count >= 2 && refreshSw.Elapsed > TimeSpan.FromMilliseconds(500))
                    {
                        AddRange(subChunk);
                        subChunk.Clear();
                        refreshSw.Restart();
                    }
                }, _cts.Token);

                // Process remaining
                if (subChunk.Count > 0)
                {
                    AddRange(subChunk);
                }
                refreshSw.Stop();
                Log.Info("End loading subs");
            }
            catch (OperationCanceledException)
            {
                foreach (var sub in subChunk)
                {
                    sub.Dispose();
                }

                Clear();
            }
        }
    }

    // F-01: decide whether a loaded/sidecar/embedded subtitle cue should be re-segmented and, if so, split it
    // into short capped-line cues with proportional timings. A bitmap subtitle or a styled (ASS, SubStyles
    // present) cue is returned unchanged — re-segmenting would drop the bitmap or invalidate the per-character
    // style offsets; an empty cue is short-circuited by the IsText gate (it never reaches the segmenter) and a
    // text cue that already fits is left untouched. Output cues stay sorted within the original [Start, End]
    // (first.Start == Start, last.End == End), so the Subs binary-search invariant is preserved.
    //
    // Loaded subtitles keep their AUTHORED timing: they are split only on line/character overflow, NOT on
    // duration. A hand-authored cue that fits the line/char budget but is deliberately held longer than the
    // duration cap must not be fragmented into timed pieces (unlike the ASR path, where a long Whisper
    // "wall of text" should be paced). Forcing MaxCueDurationSec = 0 disables only the duration trigger; a
    // genuine giant block still overflows the line/char budget and is split.
    internal static List<SubtitleData> ResegmentLoaded(SubtitleData data, bool enabled, SubtitleSegmentOptions opt)
    {
        bool canResegment = enabled && !data.IsBitmap && data.IsText
                            && (data.SubStyles == null || data.SubStyles.Count == 0);
        if (!canResegment)
            return [data];

        SubtitleSegmentOptions loadedOpt = opt.MaxCueDurationSec == 0 ? opt : new SubtitleSegmentOptions
        {
            MaxCharsPerLine = opt.MaxCharsPerLine,
            MaxLinesPerCue = opt.MaxLinesPerCue,
            MaxCjkCharsPerLine = opt.MaxCjkCharsPerLine,
            MaxCueDurationSec = 0, // loaded subs: split on line/character overflow only, never on duration
            MinCueDurationSec = opt.MinCueDurationSec,
        };

        List<(string Text, TimeSpan Start, TimeSpan End)> cues;
        try
        {
            cues = SubtitleSegmenter.Resegment(data.Text!, data.StartTime, data.EndTime, loadedOpt);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Re-segmentation is readability post-processing for loaded subtitles. A malformed/edge cue or
            // hand-edited config value must not fault the subtitle-loading worker; keep the authored cue.
            return [data];
        }

        if (cues.Count == 1)
        {
            // Already fits (or a single wrapped cue) — keep the original object and its fields; only the text
            // may gain '\n' line breaks. Resegment guarantees the single-cue times equal the input, but assign
            // them defensively so the data and the cue can never silently diverge.
            data.Text = cues[0].Text;
            data.StartTime = cues[0].Start;
            data.EndTime = cues[0].End;
            return [data];
        }

        List<SubtitleData> result = new(cues.Count);
        foreach ((string text, TimeSpan start, TimeSpan end) in cues)
        {
            // Split cues inherit the parent cue's per-cue metadata (T-10 language + F-03 speaker + F-16 per-line
            // dub voice) for parity with the single-cue fast path above (which keeps the original object). Null for
            // loaded subs today.
            result.Add(new SubtitleData { Text = text, StartTime = start, EndTime = end, Language = data.Language, SpeakerId = data.SpeakerId, AssignedVoiceId = data.AssignedVoiceId });
        }
        return result;
    }

    public void TryCancelWait()
    {
        // If it has already been executed, cancel it and wait until the preceding process is finished (it waits
        // because it takes the lock). HC-37: capture+cancel the CTS locally outside the lock, then compare-and-clear
        // under the lock so a concurrent teardown can't NRE/double-dispose and a freshly-installed CTS isn't clobbered.
        CancellationTokenSource? cts = CtsGuard.CancelCaptured(ref _cts);
        if (cts == null)
            return;

        lock (_locker)
        {
            // dispose after it is no longer used.
            CtsGuard.TryDisposeAndClear(ref _cts, cts);
        }
    }

    public void Clear()
    {
        lock (_subsLocker)
        {
            CurrentIndex = -1;
            SelectedSub = null;
            foreach (var sub in Subs)
            {
                sub.Dispose();
            }
            Subs.Clear();
            _voiceAssignedSubs.Clear();
            _voiceAssignmentGeneration++;
            State = PositionState.First;
            LanguageSource = null;
            IsLoading = false;
            Width = 0;
            Height = 0;
        }
    }

    public void Reset()
    {
        TryCancelWait();
        Clear();
    }

    #region INotifyPropertyChanged
    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
    #endregion
}

public unsafe class SubtitleReader : IDisposable
{
    private readonly SubManager _manager;
    private readonly Config _config;
    private readonly LogHandler Log;
    private readonly int _subIndex;

    private OfflineDemuxer.IsolatedDemuxer? _demuxer;
    private SubtitlesDecoder? _decoder;
    private SubtitlesStream? _stream;

    private AVPacket* _packet = null;

    public SubtitleReader(SubManager manager, Config config, int subIndex)
    {
        _manager = manager;
        _config = config;
        Log = new LogHandler(("[#1]").PadRight(8, ' ') + $" [SubReader{subIndex + 1}    ] ");

        _subIndex = subIndex;
    }

    public void Open(string url, int streamIndex, MediaType type, CancellationToken token)
    {
        _demuxer = OfflineDemuxer.OpenIsolated(_config, type, _subIndex + 1, "DemuxerS:", url, token, out string? error);

        if (error != null)
        {
            token.ThrowIfCancellationRequested(); // if canceled

            throw new InvalidOperationException($"demuxer open error: {error}");
        }

        _stream = (SubtitlesStream)_demuxer.Demuxer.AVStreamToStream[streamIndex];

        if (type == MediaType.Subs)
        {

            _stream.ExternalStream = new ExternalSubtitlesStream()
            {
                Url = url,
                IsBitmap = _stream.IsBitmap
            };

            _stream.ExternalStreamAdded();
        }

        _decoder = new SubtitlesDecoder(_config, _subIndex + 1);
        _decoder.Log.Prefix = _decoder.Log.Prefix.Replace("Decoder: ", "DecoderS:");

        if (!_decoder.Open(_stream))
        {
            token.ThrowIfCancellationRequested(); // if canceled

            throw new InvalidOperationException($"decoder open error");
        }
    }

    /// <summary>
    /// Read subtitle stream to the end and get all subtitle data
    /// </summary>
    /// <param name="useBitmap"></param>
    /// <param name="addSub"></param>
    /// <param name="token"></param>
    /// <exception cref="OperationCanceledException">The token has had cancellation requested.</exception>
    public void ReadAll(bool useBitmap, Action<SubtitleData> addSub, CancellationToken token)
    {
        if (_demuxer == null || _decoder == null || _stream == null)
            throw new InvalidOperationException("Open() is not called");

        Demuxer demuxer = _demuxer.Demuxer;
        SubtitleData? prevSub = null;
        // HC-21: track prevSub's raw end_display_time in a local. The PGS "display until next packet" correction
        // below used prevSub.Bitmap?.Sub.end_display_time, which is null when useBitmap == false (timestamp-only
        // mode), so the correction was skipped and bitmap cues kept a ~49.7-day end. Tracking it here makes the
        // correction (and the final-cue clamp) work regardless of whether the decoded bitmap is retained.
        uint prevEndDisplayTime = 0;

        _packet = av_packet_alloc();

        int demuxErrors = 0;
        int decodeErrors = 0;

        while (!token.IsCancellationRequested)
        {
            demuxer.Interrupter.ReadRequest();
            int ret = av_read_frame(demuxer.fmtCtx, _packet);

            if (ret != 0)
            {
                av_packet_unref(_packet);

                if (demuxer.Interrupter.Timedout)
                {
                    if (token.IsCancellationRequested)
                        break;

                    ret.ThrowExceptionIfError("av_read_frame (timed out)");
                }

                if (ret == AVERROR_EOF || token.IsCancellationRequested)
                {
                    break;
                }

                // demux error
                if (CanWarn) Log.Warn($"av_read_frame: {FFmpegEngine.ErrorCodeToMsg(ret)} ({ret})");

                if (++demuxErrors == _config.Demuxer.MaxErrors)
                {
                    ret.ThrowExceptionIfError("av_read_frame");
                }

                continue;
            }

            // Discard all but the subtitle stream.
            if (_packet->stream_index != _stream.StreamIndex)
            {
                av_packet_unref(_packet);
                continue;
            }

            SubtitleData subData = new();
            int gotSub = 0;
            AVSubtitle sub = default;

            ret = avcodec_decode_subtitle2(_decoder.CodecCtx, &sub, &gotSub, _packet);
            if (ret < 0)
            {
                // decode error
                av_packet_unref(_packet);
                if (CanWarn) Log.Warn($"avcodec_decode_subtitle2: {FFmpegEngine.ErrorCodeToMsg(ret)} ({ret})");
                if (++decodeErrors == _config.Decoder.MaxErrors)
                {
                    ret.ThrowExceptionIfError("avcodec_decode_subtitle2");
                }

                continue;
            }

            if (gotSub == 0)
            {
                av_packet_unref(_packet);
                continue;
            }

            long pts = NoTs; // 0.1us
            if (sub.pts != NoTs)
            {
                pts = sub.pts /*us*/ * 10;
            }
            else if (_packet->pts != NoTs)
            {
                pts = (long)(_packet->pts * _stream.Timebase);
            }

            av_packet_unref(_packet);

            if (pts == NoTs)
            {
                continue;
            }

            if (_stream.IsBitmap)
            {
                // Cache the width and height of the video for use in displaying bitmap subtitles
                // width and height may be 0 unless after decoding the subtitles
                // In this case, bitmap subtitles cannot be displayed correctly, so the size should be cached here
                if (_manager.Width != _decoder.CodecCtx->width)
                    _manager.Width = _decoder.CodecCtx->width;
                if (_manager.Height != _decoder.CodecCtx->height)
                    _manager.Height = _decoder.CodecCtx->height;
            }

            // General guard: num_rects < 1 means an empty/clear segment (sub.rects is NULL). This MUST run before
            // switch(sub.rects[0]->type) below — otherwise the first packet of a bitmap stream (prevSub == null),
            // or a text stream emitting an empty cue, dereferences NULL and crashes the process with an
            // AccessViolationException. Mirrors SubtitlesDecoder's num_rects guard.
            if (sub.num_rects < 1)
            {
                if (_stream.IsBitmap && prevSub != null)
                {
                    // Support for special format bitmap subtitles.
                    // In the case of bitmap subtitles, num_rects = 0 and 1 may alternate.
                    // In this case sub->start_display_time and sub->end_display_time are always fixed at 0 and
                    // AVPacket->duration is also always 0.
                    // This indicates the end of the previous subtitle, and the time in pts is the end time of the previous subtitle.

                    // Note that not all bitmap subtitles have this behavior.

                    // Assign pts as the end time of the previous subtitle
                    prevSub.EndTime = new TimeSpan(pts - demuxer.StartTime);
                    addSub(prevSub);
                    prevSub = null;
                    prevEndDisplayTime = 0;
                }

                avsubtitle_free(&sub);
                continue;
            }

            // Bitmap PGS has a special format.
            if (_stream.IsBitmap && prevSub != null
                /*&& _stream->codecpar->codec_id == AVCodecID.AV_CODEC_ID_HDMV_PGS_SUBTITLE*/)
            {
                // There are cases where num_rects = 1 is consecutive.
                // In this case, the previous subtitle end time is corrected by pts, and a new subtitle is started with the same pts.
                // HC-21: gate on the tracked end_display_time, not prevSub.Bitmap (null in timestamp-only mode).
                if (prevEndDisplayTime == uint.MaxValue) // 4294967295
                {
                    prevSub.EndTime = new TimeSpan(pts - demuxer.StartTime);
                    addSub(prevSub);
                    prevSub = null;
                    prevEndDisplayTime = 0;
                }
            }

            uint endDisplayTime = sub.end_display_time;
            subData.StartTime = new TimeSpan(pts - demuxer.StartTime);
            subData.EndTime = subData.StartTime.Add(TimeSpan.FromMilliseconds(endDisplayTime));

            switch (sub.rects[0]->type)
            {
                case AVSubtitleType.Text:
                    subData.Text = BytePtrToStringUTF8(sub.rects[0]->text).Trim();
                    avsubtitle_free(&sub);

                    if (string.IsNullOrEmpty(subData.Text))
                    {
                        continue;
                    }

                    break;
                case AVSubtitleType.Ass:
                    string text = BytePtrToStringUTF8(sub.rects[0]->ass).Trim();
                    avsubtitle_free(&sub);

                    subData.Text = ParseSubtitles.SSAtoSubStyles(text, out var subStyles).Trim();
                    subData.SubStyles = subStyles;

                    if (string.IsNullOrEmpty(subData.Text))
                    {
                        continue;
                    }

                    break;

                case AVSubtitleType.Bitmap:
                    subData.IsBitmap = true;

                    if (useBitmap)
                    {
                        // Save subtitle data for (OCR or subtitle cache)
                        subData.Bitmap = new SubtitleBitmapData(sub);
                    }
                    else
                    {
                        // Only subtitle timestamp information is used, so bitmap is released
                        avsubtitle_free(&sub);
                    }

                    break;
            }

            if (prevSub != null)
            {
                addSub(prevSub);
            }

            prevSub = subData;
            prevEndDisplayTime = endDisplayTime;
        }

        if (token.IsCancellationRequested)
        {
            prevSub?.Dispose();
            token.ThrowIfCancellationRequested();
        }

        // Process last
        if (prevSub != null)
        {
            // HC-21: the final bitmap cue has no following packet to correct its end. If it carried the PGS
            // "until next" sentinel (end_display_time == uint.MaxValue) its end is otherwise ~49.7 days; clamp
            // it to a bounded default so it does not swallow the whole timeline / break prev/next intervals.
            if (_stream.IsBitmap && prevEndDisplayTime == uint.MaxValue)
            {
                prevSub.EndTime = prevSub.StartTime.Add(TimeSpan.FromSeconds(5));
            }

            addSub(prevSub);
        }
    }

    private bool _isDisposed;
    public void Dispose()
    {
        if (_isDisposed)
            return;

        // av_packet_alloc
        if (_packet != null)
        {
            fixed (AVPacket** ptr = &_packet)
            {
                av_packet_free(ptr);
            }
        }

        _decoder?.Dispose();
        OfflineDemuxer.DisposeIsolated(_demuxer);

        _isDisposed = true;
    }
}

public class SubtitleBitmapData : IDisposable
{
    public SubtitleBitmapData(AVSubtitle sub)
    {
        Sub = sub;
    }

    private readonly ReaderWriterLockSlim _rwLock = new();
    private bool _isDisposed;

    public AVSubtitle Sub;

    public WriteableBitmap SubToWritableBitmap(bool isGrey)
    {
        (byte[] data, AVSubtitleRect rect) = SubToBitmap(isGrey);
        return SubsBitmap.CreateWritableBitmap(data, rect.w, rect.h);
    }

    public unsafe (byte[] data, AVSubtitleRect rect) SubToBitmap(bool isGrey)
    {
        if (_isDisposed)
            throw new InvalidOperationException("already disposed");

        try
        {
            // Prevent from disposing
            _rwLock.EnterReadLock();

            AVSubtitleRect rect = *Sub.rects[0];
            byte[] data = SubtitlesDecoder.ConvertBitmapSub(Sub, isGrey);

            return (data, rect);
        }
        finally
        {
            _rwLock.ExitReadLock();
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;

        _rwLock.EnterWriteLock();

        if (Sub.num_rects > 0)
        {
            unsafe
            {
                fixed (AVSubtitle* subPtr = &Sub)
                {
                    avsubtitle_free(subPtr);
                }
            }
        }

        _isDisposed = true;
        _rwLock.ExitWriteLock();

#if DEBUG
        GC.SuppressFinalize(this);
#endif
    }

#if DEBUG
    ~SubtitleBitmapData()
    {
        System.Diagnostics.Debug.Fail("Dispose is not called");
    }
#endif
}

public class SubtitleData : IDisposable, INotifyPropertyChanged
{
    public int Index { get; set; }

    public string? Text
    {
        get;
        set
        {
            var prevIsText = IsText;
            if (Set(ref field, value))
            {
                if (prevIsText != IsText)
                    OnPropertyChanged(nameof(IsText));
                OnPropertyChanged(nameof(DisplayText));
            }
        }
    }

    public string? TranslatedText
    {
        get;
        set
        {
            var prevUseTranslated = UseTranslated;
            if (Set(ref field, value))
            {
                if (prevUseTranslated != UseTranslated)
                {
                    OnPropertyChanged(nameof(UseTranslated));
                }
                OnPropertyChanged(nameof(DisplayText));
            }
        }
    }

    public bool IsText => !string.IsNullOrEmpty(Text);

    // Treat empty/whitespace as "not translated" so a blank result falls back to the source text
    // (DisplayText) and is retried, instead of being cached as a permanent blank subtitle.
    public bool IsTranslated => !string.IsNullOrEmpty(TranslatedText);
    public bool UseTranslated => EnabledTranslated && IsTranslated;

    public bool EnabledTranslated
    {
        get;
        set
        {
            // HC-17: notify like TranslatedText so toggling the translation display updates only the bound (visible)
            // sidebar rows via INPC, instead of the SubConfig setter forcing a full ListCollectionView.Refresh().
            // DisplayText depends on this through UseTranslated.
            var prevUseTranslated = UseTranslated;
            if (Set(ref field, value))
            {
                if (prevUseTranslated != UseTranslated)
                {
                    OnPropertyChanged(nameof(UseTranslated));
                }
                OnPropertyChanged(nameof(DisplayText));
            }
        }
    } = true;

    public string? DisplayText => UseTranslated ? TranslatedText : Text;

    public List<SubStyle>? SubStyles;
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }
#if DEBUG
    public int ChunkNo { get; set => Set(ref field, value); }
    public TimeSpan StartTimeChunk { get; set => Set(ref field, value); }
    public TimeSpan EndTimeChunk { get; set => Set(ref field, value); }
#endif
    public TimeSpan Duration => EndTime - StartTime;

    public SubtitleBitmapData? Bitmap { get; set; }

    public bool IsBitmap { get; set; }

    /// <summary>
    /// Source language of this cue as reported by ASR (T-10), or null when unknown / not applicable (loaded or
    /// translated subtitles do not set it). Inert metadata — it does not change rendering by itself. With the
    /// per-segment ASR toggle off it mirrors the pinned transcript language; on, it is the cue's own detected language.
    /// Notifies (like <see cref="AssignedVoiceId"/>) so the sidebar language badge stays live if a future writer
    /// mutates it post-Add; today it is only set in object initializers before Add.
    /// </summary>
    public Language? Language { get; set => Set(ref field, value); }

    /// <summary>
    /// Speaker label for this cue (F-03 diarization prep), or null when unknown / not applicable. Inert metadata —
    /// it does not change rendering, export, or translation by itself, and nothing populates it yet (speaker
    /// diarization is a future GPU sidecar). Reserved so the per-cue speaker schema is in place; a plain string id
    /// (e.g. "SPEAKER_00") mirrors the per-cue <see cref="Language"/> field added in T-10.
    /// </summary>
    public string? SpeakerId { get; set; }

    /// <summary>
    /// Per-line dub voice override (F-16 phase 2a), or null to use the run's default dub voice
    /// (<see cref="DubbingConfig.DefaultVoiceId"/>). Default null → byte-identical: the dub renderer falls back to
    /// the default voice, so a track with no assignments renders exactly as the single-voice dub. Inert for
    /// everything except the AI dub (display/export/translation ignore it). The cue itself is never serialized;
    /// a separate opt-in companion-file workflow can persist the override across restarts and SRT-only re-renders.
    /// A blank value means "no override". Set via the sidebar per-row voice picker. Notifies so the picker's set/unset
    /// visual state updates live.
    /// </summary>
    public string? AssignedVoiceId { get; set => Set(ref field, value); }

    private bool _isDisposed;

    public void Dispose()
    {
        if (_isDisposed)
            return;

        if (IsBitmap && Bitmap != null)
        {
            Bitmap.Dispose();
            Bitmap = null;
        }

        _isDisposed = true;
    }

    public SubtitleData Clone()
    {
        return new SubtitleData()
        {
            Index = Index,
            Text = Text,
            TranslatedText = TranslatedText,
            EnabledTranslated = EnabledTranslated,
            StartTime = StartTime,
            EndTime = EndTime,
            Language = Language,
            SpeakerId = SpeakerId,
            AssignedVoiceId = AssignedVoiceId,
#if DEBUG
            ChunkNo = ChunkNo,
            StartTimeChunk = StartTimeChunk,
            EndTimeChunk = EndTimeChunk,
#endif
            IsBitmap = IsBitmap,
            Bitmap = null,
        };
    }

    #region INotifyPropertyChanged
    public event PropertyChangedEventHandler? PropertyChanged;
    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
    #endregion
}

public class SubtitleTimeStartComparer : Comparer<SubtitleData>
{
    public static SubtitleTimeStartComparer Instance { get; } = new();
    private SubtitleTimeStartComparer() { }
    static SubtitleTimeStartComparer() { }

    public override int Compare(SubtitleData? x, SubtitleData? y)
    {
        if (object.Equals(x, y)) return 0;
        if (x == null) return -1;
        if (y == null) return 1;

        return x.StartTime.CompareTo(y.StartTime);
    }
}

public class SubtitleTimeEndComparer : Comparer<SubtitleData>
{
    public override int Compare(SubtitleData? x, SubtitleData? y)
    {
        if (object.Equals(x, y)) return 0;
        if (x == null) return -1;
        if (y == null) return 1;

        return x.EndTime.CompareTo(y.EndTime);
    }
}

internal static class WrapperHelper
{
    public static int ThrowExceptionIfError(this int error, string message)
    {
        if (error < 0)
        {
            string errStr = AvErrorStr(error);
            throw new InvalidOperationException($"{message}: {errStr} ({error})");
        }

        return error;
    }

    public static unsafe string AvErrorStr(this int error)
    {
        int bufSize = 1024;
        byte* buf = stackalloc byte[bufSize];

        if (av_strerror(error, buf, (nuint)bufSize) == 0)
        {
            string errStr = Marshal.PtrToStringAnsi((IntPtr)buf)!;
            return errStr;
        }

        return "unknown error";
    }
}

public static class ObservableCollectionExtensions
{
    public static int BinarySearch<T>(this ObservableCollection<T> collection, T item, IComparer<T> comparer)
    {
        ArgumentNullException.ThrowIfNull(collection);

        //comparer ??= Comparer<T>.Default;
        int low = 0;
        int high = collection.Count - 1;

        while (low <= high)
        {
            int mid = low + ((high - low) / 2);
            int comparison = comparer.Compare(collection[mid], item);

            if (comparison == 0)
                return mid;
            if (comparison < 0)
                low = mid + 1;
            else
                high = mid - 1;
        }

        return ~low;
    }

    public static IEnumerable<T> GetRange<T>(this ObservableCollection<T> collection, int index, int count)
    {
        ArgumentNullException.ThrowIfNull(collection);
        if (index < 0 || count < 0 || (index + count) > collection.Count)
            throw new ArgumentOutOfRangeException();

        return collection.Skip(index).Take(count);
    }

    public static void Sort<T>(this ObservableCollection<T> collection, IComparer<T> comparer)
    {
        ArgumentNullException.ThrowIfNull(collection);
        ArgumentNullException.ThrowIfNull(comparer);

        List<T> sortedList = collection.ToList();
        sortedList.Sort(comparer);

        collection.Clear();
        foreach (var item in sortedList)
        {
            collection.Add(item);
        }
    }
}

public class BulkObservableCollection<T> : ObservableCollection<T>
{
    private bool _suppressNotification;

    protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
    {
        if (!_suppressNotification)
            base.OnCollectionChanged(e);
    }

    public void AddRange(IEnumerable<T> list)
    {
        ArgumentNullException.ThrowIfNull(list);

        _suppressNotification = true;

        foreach (T item in list)
        {
            Add(item);
        }
        _suppressNotification = false;

        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
