using FlyleafLib;
using FlyleafLib.MediaPlayer.Batch;
using LLPlayer.Core.Services;

namespace LLPlayer.Avalonia.Services;

public sealed partial class FlyleafPlaybackController : ISrtAlignmentController
{
    readonly SrtAlignmentWorkflow srtAlignment;
    int pendingSrtAlignmentValidation;

    public bool CanStartSrtAlignment(int slot) => !disposed && srtAlignment.CanStart(player, slot);
    public ISrtAlignmentSession CaptureSrtAlignment(int slot) => srtAlignment.Capture(player, slot, CaptureSrtAlignmentConfig);

    Config CaptureSrtAlignmentConfig()
    {
        lock (languageConfigurationGate) return BatchSubtitleConfigSnapshot.CreateForRecognition(player.Config);
    }

    void QueueSrtAlignmentValidation()
    {
        if (Interlocked.Exchange(ref pendingSrtAlignmentValidation, 1) != 0) return;

        // Player notifications may be under engine locks. Check active ownership on a separate callback.
        if (ui == null) _ = Task.Run(ValidateSrtAlignmentSources);
        else ui.Post(ValidateSrtAlignmentSources);
    }

    void ValidateSrtAlignmentSources()
    {
        Interlocked.Exchange(ref pendingSrtAlignmentValidation, 0);
        if (disposed) return;
        srtAlignment.CancelStaleSources();
        Raise(nameof(ISrtAlignmentController.CanStartSrtAlignment));
    }

    internal void BeginSrtAlignmentShutdown() => srtAlignment.BeginShutdown();
    internal Task DrainSrtAlignmentAsync() => srtAlignment.DrainAsync();
    internal void ResumeSrtAlignmentAfterFailedShutdown() => srtAlignment.ResumeAfterFailedShutdown();
}
