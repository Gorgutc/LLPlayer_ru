using CommunityToolkit.Mvvm.ComponentModel;
using LLPlayer.Avalonia.Services;

namespace LLPlayer.Avalonia.ViewModels;

public sealed class AsrOmissionReviewViewModel(AsrOmissionReviewSnapshot snapshot) : ObservableObject
{
    bool stale;

    internal AsrOmissionReviewSnapshot Snapshot { get; } = snapshot;
    public string TrackLabel => Snapshot.Slot == 0 ? "Primary subtitle track" : "Secondary subtitle track";
    public string ModeLabel => !Snapshot.Enabled ? "Omission search: Off"
        : Snapshot.Status?.ToLowerInvariant() is "analyzed" or "partial" ? "Omission search: On" : "Omission search requested: On";
    public IReadOnlyList<AsrOmissionReviewRow> Rows => stale ? [] : Snapshot.Rows;
    public string StatusMessage => stale ? "The audio or subtitle track changed. Reopen the report for the current track."
        : Snapshot.Status?.ToLowerInvariant() switch
        {
            "disabled" => "Omission search was turned off for this run.",
            "unsupported" => "Omission search is unsupported by this recognition engine or host.",
            "analyzed" when Rows.Count == 0 => "No suggestions found.",
            "analyzed" => $"{Rows.Count} {(Rows.Count == 1 ? "suggestion" : "suggestions")} for review.",
            "partial" => $"Analysis was incomplete. {Rows.Count} {(Rows.Count == 1 ? "suggestion" : "suggestions")} for review.",
            _ => "Omission analysis was unavailable for this run."
        };

    internal void Invalidate()
    {
        if (stale) return;
        stale = true;
        OnPropertyChanged(nameof(Rows));
        OnPropertyChanged(nameof(StatusMessage));
    }
}
