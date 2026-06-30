using System.ComponentModel;
using System.Windows.Data;
using FlyleafLib;
using FlyleafLib.MediaPlayer;
using FlyleafLib.MediaPlayer.Dubbing;
using LLPlayer.Converters;
using LLPlayer.Extensions;
using LLPlayer.Services;

namespace LLPlayer.ViewModels;

public class SubtitlesSidebarVM : Bindable, IDisposable
{
    public FlyleafManager FL { get; }

    public SubtitlesSidebarVM(FlyleafManager fl)
    {
        FL = fl;

        FL.Config.PropertyChanged += OnConfigOnPropertyChanged;
        // F-16 phase 2a: keep each row's per-line dub-voice menu in sync with the dub voice bank.
        FL.PlayerConfig.Subtitles.DubbingConfig.PropertyChanged += OnDubbingConfigPropertyChanged;

        // Initialize filtered view for the sidebar
        for (int i = 0; i < _filteredSubs.Length; i++)
        {
            _filteredSubs[i] = (ListCollectionView)CollectionViewSource.GetDefaultView(FL.Player.SubtitlesManager[i].Subs);
        }
    }

    public void Dispose()
    {
        FL.Config.PropertyChanged -= OnConfigOnPropertyChanged;
        FL.PlayerConfig.Subtitles.DubbingConfig.PropertyChanged -= OnDubbingConfigPropertyChanged;
    }

    private void OnDubbingConfigPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        // Keep each row's per-line dub-voice menu (DubVoiceMenuItems) in sync with the dub voice bank.
        if (args.PropertyName is nameof(DubbingConfig.DefaultVoiceId) or nameof(DubbingConfig.CustomVoiceIds))
            OnPropertyChanged(nameof(DubVoiceMenuItems));
    }

    private void OnConfigOnPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        switch (args.PropertyName)
        {
            case nameof(FL.Config.SidebarShowSecondary):
                OnPropertyChanged(nameof(SubIndex));
                OnPropertyChanged(nameof(SubManager));

                // prevent unnecessary filter update
                if (_lastSearchText[SubIndex] != _trimSearchText)
                {
                    ApplyFilter(); // ensure filter applied
                }
                break;
            case nameof(FL.Config.SidebarShowOriginalText):
                // Update ListBox
                OnPropertyChanged(nameof(SubManager));

                if (_trimSearchText.Length != 0)
                {
                    // because of switch between Text and DisplayText
                    ApplyFilter();
                }
                break;
            case nameof(FL.Config.SidebarSearchMatchCase):
            case nameof(FL.Config.SidebarSearchWholeWord):
            case nameof(FL.Config.SidebarSearchRegex):
                // F-14: a search-option toggle changed — re-run the active search with the new options.
                if (FL.Config.SidebarSearchActive && SearchText.Trim().Length != 0)
                {
                    // An option toggle changes match semantics WITHOUT changing the query text, so the
                    // SidebarShowSecondary guard (which only re-applies on a text change) would otherwise leave
                    // the non-visible slot's view materialized under the old options. Invalidate that slot's cache
                    // so switching to it re-applies the filter with the new options.
                    _lastSearchText[1 - SubIndex] = null!;
                    ApplyFilter();
                }
                break;
        }
    }

    public int SubIndex => !FL.Config.SidebarShowSecondary ? 0 : 1;

    public SubManager SubManager => FL.Player.SubtitlesManager[SubIndex];

    private readonly ListCollectionView[] _filteredSubs = new ListCollectionView[2];

    private string _searchText = string.Empty;
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (Set(ref _searchText, value))
            {
                DebounceFilter();
            }
        }
    }

    private string _trimSearchText = string.Empty; // for performance

    // F-14: compiled matcher for the current search (rebuilt on query/option change). Null when the query is empty
    // or, for a regex search, the pattern is invalid (surfaced as "Invalid regex" in the hit count).
    private SubtitleSearcher? _searcher;

    public string HitCount { get; set => Set(ref field, value); } = string.Empty;

    public event EventHandler? RequestScrollToTop;

    // TODO: L: Fix implicit changes to reflect
    public DelegateCommand<string> CmdSubFontSizeChange => field ??= new(increase =>
    {
        if (int.TryParse(increase, out var value))
        {
            FL.Config.SidebarFontSize += value;
        }
    });

    public DelegateCommand CmdSubTextMaskToggle => field ??= new(() =>
    {
        FL.Config.SidebarTextMask = !FL.Config.SidebarTextMask;

        // Update ListBox
        OnPropertyChanged(nameof(SubManager));
    });

    public DelegateCommand CmdShowDownloadSubsDialog => field ??= new(() =>
    {
        FL.Action.CmdOpenWindowSubsDownloader.Execute();
    });

    public DelegateCommand CmdShowExportSubsDialog => field ??= new(() =>
    {
        FL.Action.CmdOpenWindowSubsExporter.Execute();
    });

    public DelegateCommand CmdSwapSidebarPosition => field ??= new(() =>
    {
        FL.Config.SidebarLeft = !FL.Config.SidebarLeft;
    });

    public DelegateCommand<int?> CmdSubPlay => field ??= new((index) =>
    {
        if (!index.HasValue)
        {
            return;
        }

        var sub = SubManager.Subs[index.Value];
        FL.Player.SeekAccurate(sub.StartTime, SubIndex);
    });

    public DelegateCommand<int?> CmdSubSync => field ??= new((index) =>
    {
        if (!index.HasValue)
        {
            return;
        }

        var sub = SubManager.Subs[index.Value];
        var newDelay = FL.Player.CurTime - sub.StartTime.Ticks;

        // Delay's OSD is not displayed, temporarily set to Active
        FL.Player.Activity.ForceActive();

        FL.PlayerConfig.Subtitles[SubIndex].Delay = newDelay;
    });

    // F-16 phase 2a: the voice choices for a sidebar row's per-line dub-voice menu — a leading "Use default
    // voice" entry (empty id) that clears any override, then the GPU-free voice bank (built-in presets + the
    // user's custom ids), exactly as the Settings/batch pickers. Rebuilt when the dub voice config changes.
    public IReadOnlyList<TtsVoice> DubVoiceMenuItems
    {
        get
        {
            DubbingConfig cfg = FL.PlayerConfig.Subtitles.DubbingConfig;
            IReadOnlyList<TtsVoice> voices = VoiceBankResolver.ForConfig(cfg.DefaultVoiceId, cfg.CustomVoiceIds);

            List<TtsVoice> items = new(voices.Count + 1)
            {
                new TtsVoice(string.Empty, "Use default voice", string.Empty, string.Empty),
            };
            items.AddRange(voices);
            return items;
        }
    }

    // F-16 phase 2a: assign (or clear) a per-line dub voice on a sidebar cue. The "use default voice" entry (or a
    // blank id) clears the override (null) so the renderer falls back to DubbingConfig.DefaultVoiceId; otherwise
    // the trimmed id is stored and passed verbatim to the engine at dub time. Interactive/in-memory only.
    public DelegateCommand<SubVoiceAssignment> CmdSubSetVoice => field ??= new(assignment =>
    {
        if (assignment?.Sub is null)
            return;

        assignment.Sub.AssignedVoiceId =
            string.IsNullOrWhiteSpace(assignment.VoiceId) ? null : assignment.VoiceId.Trim();
    });

    public DelegateCommand CmdClearSearch => field ??= new(() =>
    {
        if (!FL.Config.SidebarSearchActive)
            return;

        Set(ref _searchText, string.Empty, nameof(SearchText));
        _trimSearchText = string.Empty;
        HitCount = string.Empty;

        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
        _debounceCts = null;

        for (int i = 0; i < _filteredSubs.Length; i++)
        {
            _lastSearchText[i] = string.Empty;
            _filteredSubs[i].Filter = null; // remove filter completely
            _filteredSubs[i].Refresh();
        }

        FL.Config.SidebarSearchActive = false;

        // move focus to video and enable keybindings
        FL.FlyleafHost!.Surface.Focus();

        // for scrolling to current sub
        var prev = SubManager.SelectedSub;
        SubManager.SelectedSub = null;
        SubManager.SelectedSub = prev;
    });

    public DelegateCommand CmdNextMatch => field ??= new(() =>
    {
        if (_filteredSubs[SubIndex].IsEmpty)
            return;

        if (!_filteredSubs[SubIndex].MoveCurrentToNext())
        {
            // if last, move to first
            _filteredSubs[SubIndex].MoveCurrentToFirst();
        }

        var nextItem = (SubtitleData)_filteredSubs[SubIndex].CurrentItem;
        if (nextItem != null)
        {
            FL.Player.SeekAccurate(nextItem.StartTime, SubIndex);
            FL.Player.Activity.RefreshFullActive();
        }
    });

    public DelegateCommand CmdPrevMatch => field ??= new(() =>
    {
        if (_filteredSubs[SubIndex].IsEmpty)
            return;

        if (!_filteredSubs[SubIndex].MoveCurrentToPrevious())
        {
            // if first, move to last
            _filteredSubs[SubIndex].MoveCurrentToLast();
        }

        var prevItem = (SubtitleData)_filteredSubs[SubIndex].CurrentItem;
        if (prevItem != null)
        {
            FL.Player.SeekAccurate(prevItem.StartTime, SubIndex);
            FL.Player.Activity.RefreshFullActive();
        }
    });

    // Debounce logic
    private CancellationTokenSource? _debounceCts;
    private async void DebounceFilter()
    {
        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
        var cts = new CancellationTokenSource();
        _debounceCts = cts;
        var token = cts.Token;

        try
        {
            await Task.Delay(300, token); // 300ms debounce

            // Apply only if this invocation's CTS is still the active one — CmdClearSearch or a newer
            // DebounceFilter may have cancelled/disposed/replaced it while we were awaiting.
            if (!token.IsCancellationRequested && ReferenceEquals(_debounceCts, cts))
            {
                ApplyFilter();
            }
        }
        catch (OperationCanceledException)
        {
            // ignore: superseded by a newer search or cleared
        }
        catch (ObjectDisposedException)
        {
            // ignore: CmdClearSearch disposed the CTS backing our token while awaiting (UI-thread continuation race)
        }
    }

    private readonly string[] _lastSearchText = [string.Empty, string.Empty];

    private void ApplyFilter()
    {
        _trimSearchText = SearchText.Trim();
        _lastSearchText[SubIndex] = _trimSearchText;

        // F-14: rebuild the matcher under the current options. Null = empty query (no filter) or invalid regex.
        _searcher = _trimSearchText.Length == 0
            ? null
            : SubtitleSearcher.TryCreate(
                _trimSearchText,
                FL.Config.SidebarSearchMatchCase,
                FL.Config.SidebarSearchWholeWord,
                FL.Config.SidebarSearchRegex);

        // initialize filter lazily
        _filteredSubs[SubIndex].Filter ??= SubFilter;
        _filteredSubs[SubIndex].Refresh();

        // Invalid regex: the filter matched nothing — tell the user instead of a misleading "No hits".
        if (_trimSearchText.Length != 0 && _searcher == null)
        {
            HitCount = "Invalid regex";
            return;
        }

        int count = _filteredSubs[SubIndex].Count;
        HitCount = count > 0 ? $"{count} hits" : "No hits";

        if (SubManager.SelectedSub != null && _filteredSubs[SubIndex].MoveCurrentTo(SubManager.SelectedSub))
        {
            // scroll to current playing item
            var prev = SubManager.SelectedSub;
            SubManager.SelectedSub = null;
            SubManager.SelectedSub = prev;
        }
        else
        {
            // scroll to top
            if (!_filteredSubs[SubIndex].IsEmpty)
            {
                RequestScrollToTop?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    private bool SubFilter(object obj)
    {
        if (_trimSearchText.Length == 0) return true;
        if (_searcher == null) return false; // invalid regex → no matches
        if (obj is not SubtitleData sub) return false;

        string? source = FL.Config.SidebarShowOriginalText ? sub.Text : sub.DisplayText;
        return _searcher.IsMatch(source);
    }
}
