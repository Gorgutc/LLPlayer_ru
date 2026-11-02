using FlyleafLib;
using FlyleafLib.MediaPlayer;
using FlyleafLib.MediaPlayer.Dubbing;

namespace LLPlayer.Core.Services;

/// <summary>Shared run construction; neither opening settings nor enumerating voices starts Python.</summary>
public static class DubbingWorkflow
{
    public static IDubbingRenderer CreateRenderer(DubbingConfig snapshot)
    {
        if (snapshot.TtsServiceType != TtsServiceType.LocalCosyVoice)
            throw new NotSupportedException("Only the existing local CosyVoice dubbing backend is available. Choose LocalCosyVoice in Dubbing settings.");
        return new DubbingRenderer(snapshot);
    }

    public static IDubbingVoiceAssignmentProvider? CreateVoiceAssignments(bool persistPerLineVoices,
        string? mediaPath, IEnumerable<SubtitleData>? currentSubtitles)
    {
        var current = DubbingVoiceAssignmentMap.FromCurrentSubtitles(mediaPath, currentSubtitles);
        if (persistPerLineVoices)
            return new CompositeVoiceAssignmentProvider(current, new DiskVoiceAssignmentProvider());
        return ReferenceEquals(current, DubbingVoiceAssignmentMap.Empty) ? null : current;
    }
}
