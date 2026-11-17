using FlyleafLib.MediaFramework.MediaStream;
using FlyleafLib.MediaPlayer;
using LLPlayer.ControlApi;
using LLPlayer.Core.Services;

namespace LLPlayer.Avalonia.Services;

public sealed partial class FlyleafPlaybackController
{
    internal long ControlMediaRevision => Volatile.Read(ref mediaOpenEpoch);

    internal Task OpenControlSubtitleAsync(string path, int slot, CancellationToken token)
        => ChangeControlSubtitleAsync(slot, token, (_, cancellation) =>
        {
            cancellation.ThrowIfCancellationRequested();
            string? error = player.Open(path, forceSubtitles: true).Error;
            if (error != null) throw new InvalidOperationException(error);
        });

    internal Task SelectControlSubtitleAsync(SubtitleTrack track, int slot, CancellationToken token)
        => ChangeControlSubtitleAsync(slot, token, (_, cancellation) =>
        {
            if (!GetSubtitleTracks().Any(item => ReferenceEquals(item.Source, track.Source)))
                throw StaleControlMedia();
            string subIndex = slot.ToString(System.Globalization.CultureInfo.InvariantCulture);
            PlayerRecognitionBackend.RunSubtitleCommand(player, track.Source,
                () => player.Commands.OpenSubtitlesAction(((object)subIndex, track.Source, (object)SelectSubMethod.Original)),
                cancellation);
        });

    internal Task SetControlSubtitleDelayAsync(int slot, long ticks, CancellationToken token)
        => ChangeControlSubtitleAsync(slot, token, (_, _) => player.Config.Subtitles[slot].Delay = ticks);

    internal Task SynchronizeControlCueAsync(CueInfo cue, int slot, long expectedDelay, CancellationToken token)
        => ChangeControlSubtitleAsync(slot, token, (_, _) =>
        {
            if (!IsCueCurrent(cue, slot) || player.Config.Subtitles[slot].Delay != expectedDelay)
                throw StaleControlMedia();
            player.Config.Subtitles[slot].Delay = checked(player.CurTime - cue.Start.Ticks);
        });

    internal Task SeekControlPlaybackAsync(TimeSpan position, CancellationToken token)
    {
        PlaybackChoiceMedia media = CapturePlaybackChoiceMedia() ?? throw StaleControlMedia();
        return SeekControlAsync(media, position, -1, () => IsCurrentPlaybackMedia(media), token);
    }

    internal Task SeekControlCueAsync(CueInfo cue, int slot, long expectedDelay, CancellationToken token)
    {
        if (cue.Identity?.Source is not SubtitleCueToken target)
            throw StaleControlMedia();
        return SeekControlAsync(target.Source.Media, cue.Start, slot,
            () => player.Config.Subtitles[slot].Delay == expectedDelay && IsCueCurrent(cue, slot), token);
    }

    async Task SeekControlAsync(PlaybackChoiceMedia media, TimeSpan position, int slot, Func<bool> owns, CancellationToken token)
    {
        Task<bool>? completion = null;
        if (!player.TryMutateMedia(media.Generation, media.Item, owns,
            () => completion = player.SeekControlAsync(position, slot, token), token))
            throw StaleControlMedia();
        bool succeeded = await completion!.ConfigureAwait(false);
        if (!owns()) throw StaleControlMedia();
        if (!succeeded) throw new InvalidOperationException("The media engine could not seek to the requested position.");
    }

    async Task ChangeControlSubtitleAsync(int slot, CancellationToken token, Action<PlaybackChoiceMedia, CancellationToken> change)
    {
        if (slot is < 0 or > 1)
            throw new ControlException(new("invalid_arguments", "The subtitle slot must be 0 (primary) or 1 (secondary)."));
        PlaybackChoiceMedia media = CapturePlaybackChoiceMedia() ?? throw StaleControlMedia();
        await MutateWithTranslationAsync(cancellation =>
        {
            using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, token);
            if (!player.TryMutateMedia(media.Generation, media.Item, () => IsCurrentPlaybackMedia(media), () =>
            {
                linked.Token.ThrowIfCancellationRequested();
                int previousSlot = SubtitlesSelectedHelper.CurIndex;
                try
                {
                    SubtitlesSelectedHelper.CurIndex = slot;
                    change(media, linked.Token);
                }
                finally { SubtitlesSelectedHelper.CurIndex = previousSlot; }
            }, linked.Token)) throw StaleControlMedia();
        }).ConfigureAwait(false);
    }

    static ControlException StaleControlMedia()
        => new(new("stale_revision", "The media, stream, or subtitle source changed before the command ran."));
}
