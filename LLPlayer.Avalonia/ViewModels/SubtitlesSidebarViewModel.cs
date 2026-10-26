using System.Collections.ObjectModel;
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
/// text, or an underscore spoiler mask. <see cref="LookupText"/> is the rendered text, so word lookup on a masked row
/// can never receive the hidden source text. <see cref="IsDisplayTranslated"/> describes the rendered text, not the
/// translation availability: showing original text must look the clicked word up as source text.
/// </summary>
public sealed partial class CueItemViewModel : ObservableObject
{
    public CueItemViewModel(CueInfo cue) => Cue = cue;

    public CueInfo Cue { get; private set; }
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
    /// <summary>The text word lookup receives; equals what the user sees.</summary>
    public string LookupText => DisplayText;

    public FontFamily RowFontFamily { get; private set; } = SubtitleFontCatalog.DefaultFamily;
    public FontWeight RowFontWeight { get; private set; } = FontWeight.Normal;
    public double RowFontSize { get; private set; } = AppPrefs.DefaultSidebarFontSize;
    public Thickness RowPadding { get; private set; } = new(AppPrefs.DefaultSidebarSubPadding);

    internal void UpdateCue(CueInfo cue) => Cue = cue;

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
        bool masked = appearance.TextMask && Cue.Index > currentIndex && source.Length > 0;
        string display = masked ? new string('_', source.Length) : source;
        bool displayTranslated = !masked && Cue.IsTranslated && !showSource;
        if (DisplayText != display)
            DisplayText = display;
        if (IsMasked != masked)
            IsMasked = masked;
        if (IsDisplayTranslated != displayTranslated)
            IsDisplayTranslated = displayTranslated;
    }
}

/// <summary>
/// Subtitles sidebar: the primary cue list (times + text), current cue highlight (the view auto-scrolls to
/// <see cref="CurrentCue"/>), click to seek to a cue's start and a case-insensitive filter box (like the WPF sidebar's
/// default search mode). The filter follows the same original/display text choice the rows render.
/// </summary>
public sealed partial class SubtitlesSidebarViewModel : ObservableObject
{
    readonly IPlaybackController player;
    readonly SubtitleFontCatalog fonts = new();
    List<CueItemViewModel> all = [];
    SidebarTextAppearance appearance = SidebarTextAppearance.Default;
    SubtitleFontSelection fontSelection = new(SubtitleFontCatalog.DefaultFamily, FontWeight.Normal, FontStyle.Normal, "");
    int currentIndex = -1;

    public SubtitlesSidebarViewModel(IPlaybackController player)
    {
        this.player = player;
    }

    /// <summary>Cues shown (after the filter).</summary>
    public ObservableCollection<CueItemViewModel> Cues { get; } = [];

    public int TotalCount => all.Count;
    public bool HasCues => all.Count > 0;

    [ObservableProperty]
    public partial string FilterText { get; set; } = "";

    [ObservableProperty]
    public partial CueItemViewModel? CurrentCue { get; private set; }

    [ObservableProperty]
    public partial string CountText { get; private set; } = "";

    /// <summary>The active sidebar text appearance (font/padding/mask/original choice).</summary>
    public SidebarTextAppearance Appearance => appearance;

    /// <summary>Raised when the view should move keyboard focus to the filter box (Ctrl+F).</summary>
    public event Action? FocusFilterRequested;

    partial void OnFilterTextChanged(string value) => ApplyFilter();

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

    /// <summary>Re-reads the primary cues from the player (called on the engine's coalesced change notification).</summary>
    public void Reload()
    {
        IReadOnlyList<CueInfo> cues = player.GetCues(0);

        bool compatible = all.Count <= cues.Count;
        for (int i = 0; compatible && i < all.Count; i++)
        {
            CueInfo existing = all[i].Cue;
            CueInfo current = cues[i];
            compatible = existing.Index == current.Index && existing.Start == current.Start && existing.End == current.End;
        }

        if (compatible && all.Count > 0)
        {
            // Text-only changes (translation toggle, wording update) keep the row objects so the list selection,
            // scroll position and highlight survive; new cues (ASR/OCR/append) are inserted after them.
            for (int i = 0; i < all.Count; i++)
            {
                all[i].UpdateCue(cues[i]);
                all[i].ApplyTypography(fontSelection, appearance);
                all[i].Refresh(appearance, currentIndex);
            }
            string filter = FilterText;
            bool filterActive = !string.IsNullOrEmpty(filter);
            for (int i = all.Count; i < cues.Count; i++)
            {
                CueItemViewModel row = new(cues[i]);
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
                CueItemViewModel row = new(cue);
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

        UpdateCurrent(player.PrimaryCueIndex);
    }

    /// <summary>Marks the cue at <paramref name="index"/> (engine cue index) as current and refreshes the mask boundary.</summary>
    public void UpdateCurrent(int index)
    {
        if (currentIndex != index)
        {
            currentIndex = index;
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
        if (cue != null)
            player.SeekToSubtitle(cue.Start, 0);
    }

    [RelayCommand]
    void ClearFilter() => FilterText = "";

    public void RequestFocusFilter() => FocusFilterRequested?.Invoke();

    void ApplyFilter()
    {
        string f = FilterText;
        List<CueItemViewModel> desired = all.Where(item => Matches(item, f)).ToList();
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

        Cues.Clear();
        foreach (CueItemViewModel item in desired)
            Cues.Add(item);
        UpdateCount();
    }

    void UpdateCount()
    {
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(HasCues));
        CountText = string.IsNullOrEmpty(FilterText) ? $"{all.Count} cues" : $"{Cues.Count} / {all.Count}";
    }

    /// <summary>The text the WPF sidebar searches: the same original/display source the rows show, never the mask.</summary>
    string SearchText(CueItemViewModel item)
        => appearance.ShowOriginalText && item.IsTranslated && !string.IsNullOrEmpty(item.OriginalText)
            ? item.OriginalText
            : item.Text;

    bool Matches(CueItemViewModel item, string filter)
        => string.IsNullOrEmpty(filter) || SearchText(item).Contains(filter, StringComparison.CurrentCultureIgnoreCase);
}
