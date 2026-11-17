using FlyleafLib.MediaPlayer.Dubbing;
using LLPlayer.ControlApi;
using LLPlayer.Core.Batch;
using LLPlayer.Core.Services;

namespace LLPlayer.Avalonia.Services;

public sealed partial class FlyleafPlaybackController
{
    internal string AttachControlDub(string? path, long expectedRevision, CancellationToken token)
    {
        if (CapturePlaybackChoiceMedia() is not { } media || ControlMediaRevision != expectedRevision ||
            LocalDubbingMediaPath(CurrentMediaPath) is not { } mediaPath)
            throw new ControlException(new("stale_revision", "Read the current local media before attaching a dub."));
        if (path is not null && !Path.IsPathFullyQualified(path))
            throw new ControlException(new("invalid_arguments", "Provide an absolute local dubbed-audio path."));
        string? dubPath = path is null ? DubbingOutputPathBuilder.ResolveExistingRussianDubPath(mediaPath) : Path.GetFullPath(path);
        if (dubPath is null || !DubbingOutputPathBuilder.OutputExists(dubPath) || BatchWorkflow.PathComparer.Equals(mediaPath, dubPath))
            throw new ControlException(new("dub_not_found", "No usable rendered dub was found for the requested audio path."));

        bool attached = false;
        if (!player.TryMutateMedia(media.Generation, media.Item,
                () => ControlMediaRevision == expectedRevision && IsCurrentPlaybackMedia(media), () =>
                {
                    token.ThrowIfCancellationRequested();
                    var track = DubbedAudioAutoLoader.Attach(media.Item, dubPath);
                    attached = media.Item.ExternalAudioStreams.Contains(track);
                }, token) || !attached)
            throw new ControlException(new("stale_revision", "The selected media changed before the dub was attached."));
        return dubPath;
    }
}
