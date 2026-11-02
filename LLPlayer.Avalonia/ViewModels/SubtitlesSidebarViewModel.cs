using System.Collections.ObjectModel;
using FlyleafLib;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia;
using Avalonia.Media;
using LLPlayer.Avalonia.Services;

namespace LLPlayer.Avalonia.ViewModels;

/// <summary>
/// Sidebar-only text appearance (F-13). Separate from the video overlay typography so the sidebar font/padding
/// never changes the subtitles rendered over the video.
/// </summary>
public sealed record SidebarTextAppearance(
    bool TextMask,
    bool ShowOriginalText,
    int SubPadding,
    string FontFamily,
    string FontWeight,
    double FontSize)
{
    public static SidebarTextAppearance Default { get; } = new(
        AppPrefs.DefaultSidebarTextMask, AppPrefs.DefaultSidebarShowOriginalText, AppPrefs.DefaultSidebarSubPadding,
        AppPrefs.DefaultSidebarFontFamily, AppPrefs.DefaultSidebarFontWeight, AppPrefs.DefaultSidebarFontSize);
}

/// <summary>
/// One sidebar cue row. <see cref="DisplayText"/> is what the row renders: the source text, the translated display
/// text, or an underscore spoiler mask; word lookup receives that rendered text, so a masked row can never expose the
/// hidden source text. <see cref="IsDisplayTranslated"/> describes the rendered text, not the translation
/// availability: showing original text must look the clicked word up as source text.
/// </summary>
public sealed partial class CueItemViewModel : ObservableObject
{
    public CueItemViewModel(CueInfo cue, int slot = 0)
    {
        Cue = cue;
        Slot = slot;
    }

    public CueInfo Cue { get; private set; }
    /// <summary>The subtitle slot this row was created for; changing the selection creates new rows.</summary>
    public int Slot { get; }
    public int Index => Cue.Index;
    public TimeSpan Start => Cue.Start;
    public TimeSpan End => Cue.End;
    /// <summary>Current display text from the player (translated when translation display is on).</summary>
    public string Text => Cue.Text;
    /// <summary>Source text snapshot captured by the player adapter ("" when unknown).</summary>
    public string OriginalText => Cue.OriginalText;
    public bool IsTranslated => Cue.IsTranslated;
    public string SourceLanguage => Cue.SourceLanguage;
    public string TargetLanguage => Cue.TargetLanguage;
    public string StartText => TimeFormat.Cue(Cue.Start);
    /// <summary>OCR text replaces its image. Spoiler masking never reveals the underlying bitmap.</summary>
    public bool ShowBitmap => Cue.IsBitmap && string.IsNullOrEmpty(Cue.OriginalText) && !IsMasked;
    public bool ShowText => !ShowBitmap;

    [ObservableProperty]
    public partial bool IsCurrent { get; set; }

    /// <summary>Text rendered in the row (masked/original/display chosen by the sidebar appearance).</summary>
    [ObservableProperty]
    public partial string DisplayText { get; set; } = "";
    /// <summary>The rendered text is masked (future cue, spoiler protection).</summary>
    [ObservableProperty]
    public partial bool IsMasked { get; set; }
    /// <summary>The rendered text is translated display text; false for original or masked rows.</summary>
    [ObservableProperty]
    public partial bool IsDisplayTranslated { get; set; }
    // Observable so a Settings commit can restyle already-realized rows without rebuilding the list.
    [ObservableProperty] public partial FontFamily RowFontFamily { get; private set; } = SubtitleFontCatalog.DefaultFamily;
    [ObservableProperty] public partial FontWeight RowFontWeight { get; private set; } = FontWeight.Normal;
    [ObservableProperty] public partial double RowFontSize { get; private set; } = AppPrefs.DefaultSidebarFontSize;
    [ObservableProperty] public partial Thickness RowPadding { get; private set; } = new(AppPrefs.DefaultSidebarSubPadding);

    internal void UpdateCue(CueInfo cue)
    {
        if (Cue == cue)
            return;

        CueInfo previous = Cue;
        Cue = cue;
        OnPropertyChanged(nameof(Cue));
        OnPropertyChanged(nameof(ShowBitmap));
        OnPropertyChanged(nameof(ShowText));
        if (previous.Index != cue.Index) OnPropertyChanged(nameof(Index));
        if (previous.Start != cue.Start)
        {
            OnPropertyChanged(nameof(Start));
            OnPropertyChanged(nameof(StartText));
        }
        if (previous.End != cue.End) OnPropertyChanged(nameof(End));
        if (previous.Text != cue.Text) OnPropertyChanged(nameof(Text));
        if (previous.OriginalText != cue.OriginalText) OnPropertyChanged(nameof(OriginalText));
        if (previous.IsTranslated != cue.IsTranslated) OnPropertyChanged(nameof(IsTranslated));
        if (previous.SourceLanguage != cue.SourceLanguage) OnPropertyChanged(nameof(SourceLanguage));
        if (previous.TargetLanguage != cue.TargetLanguage) OnPropertyChanged(nameof(TargetLanguage));
    }

    internal void ApplyTypography(SubtitleFontSelection font, SidebarTextAppearance appearance)
    {
        RowFontFamily = font.Family;
        RowFontWeight = font.Weight;
        RowFontSize = appearance.FontSize;
        RowPadding = new Thickness(appearance.SubPadding);
    }

    /// <summary>Recomputes the rendered text for the current appearance/current-cue state.</summary>
    internal void Refresh(SidebarTextAppearance appearance, int currentIndex)
    {
        bool showSource = appearance.ShowOriginalText && Cue.IsTranslated && !string.IsNullOrEmpty(Cue.OriginalText);
        string source = showSource ? Cue.OriginalText : Cue.Text;
        bool masked = appearance.TextMask && Cue.Index > currentIndex && (source.Length > 0 || Cue.IsBitmap);
        string display = masked ? new string('_', source.Length) : source;
        bool displayTranslated = !masked && Cue.IsTranslated && !showSource;
        if (DisplayText != display)
            DisplayText = display;
        if (IsMasked != masked)
            IsMasked = masked;
        if (IsDisplayTranslated != displayTranslated)
            IsDisplayTranslated = displayTranslated;
    }

    partial void OnIsMaskedChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowBitmap));
        OnPropertyChanged(nameof(ShowText));
    }
}

/// <summary>
/// Subtitles sidebar: the selected slot's cue list (times + text), current cue highlight (the view auto-scrolls to
/// <see cref="CurrentCue"/>), click to seek to a cue's start and the WPF-compatible case/whole-word/regex search.
/// Search follows the original/display text choice, through visual spoiler masks, and stops on the first timeout.
/// </summary>
public sealed partial class SubtitlesSidebarViewModel : ObservableObject, IDisposable
{
    readonly IPlaybackController player;
    readonly SubtitleFontCatalog fonts = new();
    List<CueItemViewModel> all = [];
    SidebarTextAppearance appearance = SidebarTextAppearance.Default;
    SubtitleFontSelection fontSelection = new(SubtitleFontCatalog.DefaultFamily, FontWeight.Normal, FontStyle.Normal, "");
    int currentIndex = -1;
    int? navigationIndex;
    string effectiveFilter = "";
    SubtitleSearcher? searcher;
    int selectedSlot;
    CancellationTokenSource commandLifetime = new();
    bool disposed;

    public SubtitlesSidebarViewModel(IPlaybackController player)
    {
        this.player = player;
    }

    /// <summary>Cues shown (after the filter).</summary>
    public ObservableCollection<CueItemViewModel> Cues { get; } = [];

    public int TotalCount => all.Count;
    public bool HasCues => all.Count > 0;

    /// <summary>The sidebar source slot. Invalid/transient ComboBox indices retain the valid selection.</summary>
    public int SelectedSlot
    {
        get => selectedSlot;
        set
        {
            if (disposed || value is < 0 or > 1 || value == selectedSlot)
                return;
            selectedSlot = value;
            Clear();
            Reload();
            OnPropertyChanged();
        }
    }

    [ObservableProperty]
    public partial string FilterText { get; set; } = "";

    [ObservableProperty]
    public partial bool MatchCase { get; set; }

    [ObservableProperty]
    public partial bool WholeWord { get; set; }

    [ObservableProperty]
    public partial bool UseRegex { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSearchError))]
    public partial string SearchError { get; private set; } = "";

    public bool HasSearchError => !string.IsNullOrEmpty(SearchError);

    [ObservableProperty]
    public partial CueItemViewModel? CurrentCue { get; private set; }

    [ObservableProperty]
    public partial string CountText { get; private set; } = "";

    /// <summary>The active sidebar text appearance (font/padding/mask/original choice).</summary>
    public SidebarTextAppearance Appearance => appearance;

    /// <summary>Raised when the view should move keyboard focus to the filter box (Ctrl+F).</summary>
    public event Action? FocusFilterRequested;
    /// <summary>Reports a failed live cue synchronization through the main window's existing toast surface.</summary>
    public event Action<string>? SynchronizationFailed;

    partial void OnFilterTextChanged(string value) => UpdateSearch();
    partial void OnMatchCaseChanged(bool value) => UpdateSearch();
    partial void OnWholeWordChanged(bool value) => UpdateSearch();
    partial void OnUseRegexChanged(bool value) => UpdateSearch();

    void UpdateSearch()
    {
        effectiveFilter = FilterText?.Trim() ?? "";
        // The engine matcher is shared with WPF. Compile once per query/options change, never per cue or reload.
        searcher = effectiveFilter.Length == 0
            ? null
            : SubtitleSearcher.TryCreate(effectiveFilter, MatchCase, WholeWord, UseRegex);
        ApplyFilter();
    }

    /// <summary>Applies the sidebar-only appearance and refreshes rows/typography; the active filter is re-evaluated.</summary>
    public void ApplyAppearance(SidebarTextAppearance value)
    {
        appearance = value;
        fontSelection = fonts.ResolveOrRequested(value.FontFamily, value.FontWeight, "Normal");
        foreach (CueItemViewModel row in all)
        {
            row.ApplyTypography(fontSelection, appearance);
            row.Refresh(appearance, currentIndex);
        }
        ApplyFilter();
    }

    /// <summary>Retires rows immediately when media opening replaces their source.</summary>
    public void Clear()
    {
        RetireCommands();
        navigationIndex = null;
        currentIndex = -1;
        if (CurrentCue != null)
            CurrentCue.IsCurrent = false;
        CurrentCue = null;
        all = [];
        ApplyFilter();
    }

    /// <summary>Re-reads the selected cues from the player (called on the engine's coalesced change notification).</summary>
    public void Reload()
    {
        if (disposed)
            return;
        navigationIndex = null;
        IReadOnlyList<CueInfo> cues = player.GetCues(SelectedSlot);

        bool compatible = all.Count <= cues.Count;
        for (int i = 0; compatible && i < all.Count; i++)
            compatible = all[i].Cue.Index == cues[i].Index && Equals(all[i].Cue.Identity, cues[i].Identity);
        if (!compatible)
            RetireCommands();

        if (compatible && all.Count > 0)
        {
            // Cue updates (translation toggle, delay shift, wording change) keep the row objects so the list
            // selection, scroll position and highlight survive; new cues (ASR/OCR/append) are inserted after them.
            // Typography only changes through ApplyAppearance, so a cue notification never restyles every row.
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i].Cue == cues[i])
                    continue;
                all[i].UpdateCue(cues[i]);
                all[i].Refresh(appearance, currentIndex);
            }
            bool filterActive = effectiveFilter.Length != 0;
            for (int i = all.Count; i < cues.Count; i++)
            {
                CueItemViewModel row = new(cues[i], SelectedSlot);
                row.ApplyTypography(fontSelection, appearance);
                row.Refresh(appearance, currentIndex);
                all.Add(row);
                if (!filterActive)
                    Cues.Add(row);
            }
            if (filterActive)
                ApplyFilter(); // text flips (translation display) can change filter membership
            else
                UpdateCount();
        }
        else if (all.Count != 0 || cues.Count != 0)
        {
            all = cues.Select(cue =>
            {
                CueItemViewModel row = new(cue, SelectedSlot);
                row.ApplyTypography(fontSelection, appearance);
                row.Refresh(appearance, currentIndex);
                return row;
            }).ToList();
            ApplyFilter();
        }
        else
        {
            ApplyFilter();
        }

        UpdateCurrent(SelectedSlot == 0 ? player.PrimaryCueIndex : player.SecondaryCueIndex);
    }

    /// <summary>Marks the cue at <paramref name="index"/> (engine cue index) as current and refreshes the mask boundary.</summary>
    public void UpdateCurrent(int index)
    {
        if (currentIndex != index)
        {
            currentIndex = index;
            navigationIndex = null;
            foreach (CueItemViewModel row in all)
                row.Refresh(appearance, index);
        }

        CueItemViewModel? current = index >= 0 && index < all.Count && all[index].Index == index
            ? all[index]
            : all.FirstOrDefault(c => c.Index == index);

        if (ReferenceEquals(current, CurrentCue))
            return;

        if (CurrentCue != null)
            CurrentCue.IsCurrent = false;
        if (current != null)
            current.IsCurrent = true;
        CurrentCue = current;
    }

    [RelayCommand]
    void Seek(CueItemViewModel? cue)
    {
        if (cue != null && IsCurrentRow(cue))
            player.SeekToCue(cue.Cue, cue.Slot);
    }

    /// <summary>Only a displayed row from the selected, still-current source may execute a row action.</summary>
    public bool IsCurrentRow(CueItemViewModel row)
        => !disposed && row.Slot == SelectedSlot && Cues.Contains(row) && player.IsCueCurrent(row.Cue, row.Slot);

    [RelayCommand]
    async Task SyncAsync(CueItemViewModel? cue)
    {
        if (cue == null || !IsCurrentRow(cue))
            return;
        try
        {
            await player.SynchronizeSubtitleToCurrentTimeAsync(cue.Cue, cue.Slot, commandLifetime.Token);
        }
        catch (OperationCanceledException)
        {
            // Slot/source/filter changes and window shutdown retire queued commands without a failure toast.
        }
        catch (Exception ex)
        {
            SynchronizationFailed?.Invoke(ex.Message);
        }
    }

    [RelayCommand]
    void ClearFilter() => FilterText = "";

    [RelayCommand]
    void NextMatch() => NavigateMatch(forward: true);

    [RelayCommand]
    void PreviousMatch() => NavigateMatch(forward: false);

    void NavigateMatch(bool forward)
    {
        if (Cues.Count == 0 || HasSearchError)
            return;

        // A seek completes asynchronously. Keep the requested hit as the cursor for repeated navigation until
        // playback reports a new current cue; changing the query/options or reloading resets to playback too.
        int anchor = navigationIndex ?? currentIndex;
        CueItemViewModel match = forward
            ? Cues.FirstOrDefault(cue => cue.Index > anchor) ?? Cues[0]
            : Cues.LastOrDefault(cue => cue.Index < anchor) ?? Cues[^1];
        navigationIndex = match.Index;
        Seek(match);
    }

    public void RequestFocusFilter() => FocusFilterRequested?.Invoke();

    void RetireCommands()
    {
        if (disposed)
            return;
        CancellationTokenSource retired = commandLifetime;
        commandLifetime = new();
        retired.Cancel();
        retired.Dispose();
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        commandLifetime.Cancel();
        commandLifetime.Dispose();
    }

    void ApplyFilter()
    {
        navigationIndex = null;
        SearchError = effectiveFilter.Length != 0 && searcher == null ? "Invalid regex" : "";
        List<CueItemViewModel> desired = [];
        foreach (CueItemViewModel item in all)
        {
            if (effectiveFilter.Length == 0)
            {
                desired.Add(item);
                continue;
            }
            if (searcher == null)
                break;

            bool matches = searcher.IsMatch(SearchText(item), out bool timedOut);
            if (timedOut)
            {
                // Stop at the first engine regex timeout rather than paying it again for every remaining cue,
                // and do not show a misleading subset consisting only of matches before the timed-out cue.
                desired.Clear();
                SearchError = "Search timed out";
                break;
            }
            if (matches)
                desired.Add(item);
        }
        if (desired.Count == Cues.Count)
        {
            bool same = true;
            for (int i = 0; same && i < desired.Count; i++)
                same = ReferenceEquals(desired[i], Cues[i]);
            if (same)
            {
                UpdateCount();
                return;
            }
        }

        HashSet<CueItemViewModel> remaining = desired.ToHashSet();
        if (Cues.Any(row => !remaining.Contains(row)))
            RetireCommands();
        Cues.Clear();
        foreach (CueItemViewModel item in desired)
            Cues.Add(item);
        UpdateCount();
    }

    void UpdateCount()
    {
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(HasCues));
        CountText = HasSearchError ? SearchError
            : effectiveFilter.Length == 0 ? $"{all.Count} cues" : $"{Cues.Count} / {all.Count}";
    }

    /// <summary>The text the WPF sidebar searches: the same original/display source the rows show, never the mask.</summary>
    string SearchText(CueItemViewModel item)
        => appearance.ShowOriginalText && item.IsTranslated && !string.IsNullOrEmpty(item.OriginalText)
            ? item.OriginalText
            : item.Text;
}
