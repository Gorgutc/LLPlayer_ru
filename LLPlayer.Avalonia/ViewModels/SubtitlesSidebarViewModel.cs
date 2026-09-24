using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LLPlayer.Avalonia.Services;

namespace LLPlayer.Avalonia.ViewModels;

public sealed partial class CueItemViewModel(CueInfo cue) : ObservableObject
{
    public CueInfo Cue { get; } = cue;
    public int Index => Cue.Index;
    public TimeSpan Start => Cue.Start;
    public TimeSpan End => Cue.End;
    public string Text => Cue.Text;
    public string StartText => TimeFormat.Cue(Cue.Start);

    [ObservableProperty]
    public partial bool IsCurrent { get; set; }
}

/// <summary>
/// Subtitles sidebar: the primary cue list (times + text), current cue highlight (the view auto-scrolls to
/// <see cref="CurrentCue"/>), click to seek to a cue's start and a case-insensitive filter box (like the WPF sidebar's
/// default search mode).
/// </summary>
public sealed partial class SubtitlesSidebarViewModel : ObservableObject
{
    readonly IPlaybackController player;
    List<CueItemViewModel> all = [];

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

    /// <summary>Raised when the view should move keyboard focus to the filter box (Ctrl+F).</summary>
    public event Action? FocusFilterRequested;

    partial void OnFilterTextChanged(string value) => ApplyFilter();

    /// <summary>Re-reads the primary cues from the player (called on the engine's coalesced change notification).</summary>
    public void Reload()
    {
        IReadOnlyList<CueInfo> cues = player.GetCues(0);

        bool isAppend = cues.Count >= all.Count;
        for (int i = 0; isAppend && i < all.Count; i++)
            isAppend = all[i].Cue == cues[i];

        if (isAppend && all.Count > 0)
        {
            List<CueItemViewModel> added = cues.Skip(all.Count).Select(c => new CueItemViewModel(c)).ToList();
            all.AddRange(added);
            string f = FilterText;
            foreach (CueItemViewModel item in added)
                if (Matches(item, f))
                    Cues.Add(item);
            UpdateCount();
        }
        else if (!isAppend || cues.Count != all.Count)
        {
            all = cues.Select(c => new CueItemViewModel(c)).ToList();
            ApplyFilter();
        }

        UpdateCurrent(player.PrimaryCueIndex);
    }

    /// <summary>Marks the cue at <paramref name="index"/> (engine cue index) as current.</summary>
    public void UpdateCurrent(int index)
    {
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
            player.SeekTo(cue.Start);
    }

    [RelayCommand]
    void ClearFilter() => FilterText = "";

    public void RequestFocusFilter() => FocusFilterRequested?.Invoke();

    void ApplyFilter()
    {
        string f = FilterText;
        Cues.Clear();
        foreach (CueItemViewModel item in all)
            if (Matches(item, f))
                Cues.Add(item);
        UpdateCount();
    }

    void UpdateCount()
    {
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(HasCues));
        CountText = string.IsNullOrEmpty(FilterText) ? $"{all.Count} cues" : $"{Cues.Count} / {all.Count}";
    }

    static bool Matches(CueItemViewModel item, string filter)
        => string.IsNullOrEmpty(filter) || item.Text.Contains(filter, StringComparison.CurrentCultureIgnoreCase);
}
