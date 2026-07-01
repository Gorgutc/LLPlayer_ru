using System.Collections.Generic;

namespace FlyleafLib.MediaPlayer.Dubbing;

#nullable enable

/// <summary>
/// F-16 persistence: an <see cref="IDubbingVoiceAssignmentProvider"/> backed by the on-disk companion file. Each
/// <see cref="Apply"/> loads the persisted per-line voices for THAT media path (so a folder batch resolves each
/// file's own companion) and fills only cues without an override (current-session choices win). A missing/corrupt
/// companion is a silent no-op.
/// </summary>
public sealed class DiskVoiceAssignmentProvider : IDubbingVoiceAssignmentProvider
{
    public void Apply(string mediaPath, IReadOnlyList<SubtitleData> subtitles)
        => DubbingVoiceAssignmentStore.LoadMap(mediaPath).Apply(mediaPath, subtitles);
}

/// <summary>
/// Applies several providers in order. Earlier providers win because <see cref="DubbingVoiceAssignmentMap.Apply"/>
/// only fills cues that still have no override — so layering the current-session snapshot (added first) over the
/// persisted on-disk assignments gives live edits priority over the saved file.
/// </summary>
public sealed class CompositeVoiceAssignmentProvider : IDubbingVoiceAssignmentProvider
{
    private readonly IReadOnlyList<IDubbingVoiceAssignmentProvider> _providers;

    public CompositeVoiceAssignmentProvider(params IDubbingVoiceAssignmentProvider[] providers)
        => _providers = providers;

    public void Apply(string mediaPath, IReadOnlyList<SubtitleData> subtitles)
    {
        foreach (IDubbingVoiceAssignmentProvider provider in _providers)
            provider.Apply(mediaPath, subtitles);
    }
}
