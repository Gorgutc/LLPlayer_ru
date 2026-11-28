using System.Globalization;

namespace LLPlayer.Avalonia.Services;

public sealed record AsrOmissionReviewRow(string Text, long StartMs, long EndMs, bool IsReviewOnly,
    string? ReviewOnlyReason = null)
{
    public string Timing => $"{Clock(StartMs)} – {Clock(EndMs)}";

    public string ReviewLabel => ReviewOnlyReason switch
    {
        "baselineCueAlreadyOnScreen" => "Review only · overlaps a displayed subtitle",
        "partialDonorCue" => "Review only · part of a recognized line",
        _ => "Review only"
    };

    static string Clock(long ms) => string.Create(CultureInfo.InvariantCulture,
        $"{ms / 3600000:00}:{ms / 60000 % 60:00}:{ms / 1000 % 60:00}.{ms % 1000:000}");
}

public sealed record AsrOmissionReviewSnapshot(int Slot, bool Enabled, string Status,
    IReadOnlyList<AsrOmissionReviewRow> Rows, object SourceToken);

/// <summary>Detached findings that belong to one slot and recognition run.</summary>
public interface IAsrOmissionReviewController
{
    bool CanReviewAsrOmissions(int slot);
    Task<AsrOmissionReviewSnapshot?> ReadAsrOmissionsAsync(int slot, CancellationToken cancellationToken);
    bool IsAsrOmissionReviewCurrent(AsrOmissionReviewSnapshot snapshot);
}
