using FlyleafLib.MediaFramework.MediaStream;
using FlyleafLib.MediaPlayer;

namespace LLPlayer.Avalonia.Services;

public sealed partial class FlyleafPlaybackController
{
    sealed record SubtitleCueSource(FlyleafPlaybackController Owner, PlaybackChoiceMedia Media, int Slot, SubManager Manager,
        SubtitlesStream? Stream, object? Demuxer, string? NativeUrl, ExternalStream? External,
        string? ExternalUrl, Stream? ExternalIO, bool IsASR);

    // Equality describes the underlying cue source, so translation, appends and raw timing updates can retain
    // the same UI row. Currentness separately compares the detached raw timing before a command is admitted.
    sealed record SubtitleCueToken(SubtitleCueSource Source, SubtitleData Cue);

    SubtitleCueSource? CaptureSubtitleCueSource(int slot)
    {
        PlaybackChoiceMedia? media = CapturePlaybackChoiceMedia();
        if (media == null) return null;
        SubtitlesStream? stream = player.decoder.SubtitlesStreams[slot];
        ExternalStream? external = stream?.ExternalStream;
        SubtitleCueSource source = new(this, media, slot, player.SubtitlesManager[slot], stream,
            stream?.Demuxer, stream?.Demuxer.Url, external, external?.Url, external?.IOStream,
            player.Subtitles[slot].EnabledASR);
        return IsCurrentSubtitleCueSource(source) ? source : null;
    }

    bool IsCurrentSubtitleCueSource(SubtitleCueSource source)
    {
        if (!ReferenceEquals(source.Owner, this) || !IsCurrentPlaybackMedia(source.Media)) return false;
        int slot = source.Slot;
        SubtitlesStream? stream = player.decoder.SubtitlesStreams[slot];
        return player.Subtitles[slot].Enabled
            && ReferenceEquals(source.Manager, player.SubtitlesManager[slot])
            && source.IsASR == player.Subtitles[slot].EnabledASR && ReferenceEquals(source.Stream, stream)
            && ReferenceEquals(source.Demuxer, stream?.Demuxer) && source.NativeUrl == stream?.Demuxer.Url
            && ReferenceEquals(source.External, stream?.ExternalStream)
            && source.ExternalUrl == stream?.ExternalStream?.Url
            && ReferenceEquals(source.ExternalIO, stream?.ExternalStream?.IOStream);
    }

    public bool IsCueCurrent(CueInfo cue, int slot)
    {
        if (slot is < 0 or > 1 || cue.Identity?.Source is not SubtitleCueToken token
            || token.Source.Slot != slot || !IsCurrentSubtitleCueSource(token.Source)) return false;
        try
        {
            List<SubtitleData> subs = bridge.Snapshot(token.Source.Manager.Subs);
            return cue.Index >= 0 && cue.Index < subs.Count && ReferenceEquals(subs[cue.Index], token.Cue)
                && token.Cue.Index == cue.Index && token.Cue.StartTime == cue.Start && token.Cue.EndTime == cue.End
                && IsCurrentSubtitleCueSource(token.Source);
        }
        catch (InvalidOperationException) { return false; }
    }

    public void SeekToCue(CueInfo cue, int slot)
    {
        if (!IsCueCurrent(cue, slot) || cue.Identity?.Source is not SubtitleCueToken token) return;
        player.TryMutateMedia(token.Source.Media.Generation, token.Source.Media.Item,
            () => IsCueCurrent(cue, slot), () => player.SeekAccurate(cue.Start, slot));
    }

    public async Task SynchronizeSubtitleToCurrentTimeAsync(CueInfo cue, int slot, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested || !IsCueCurrent(cue, slot) || cue.Identity?.Source is not SubtitleCueToken token) return;
        try
        {
            await Recognition.MutateAsync(cancellation =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                bool synchronized = player.TryMutateMedia(token.Source.Media.Generation, token.Source.Media.Item,
                    () => !cancellationToken.IsCancellationRequested && IsCueCurrent(cue, slot), () =>
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        // Snapshot's collection gate is already released: Delay calls SetCurrentTime/ReSync.
                        // Match WPF exactly. Start is raw authored time and contains none of the existing delay.
                        // ReSync's external-subtitle path reads this legacy helper rather than the stream slot.
                        int previousSlot = SubtitlesSelectedHelper.CurIndex;
                        try
                        {
                            SubtitlesSelectedHelper.CurIndex = slot;
                            player.Config.Subtitles[slot].Delay = checked(player.CurTime - cue.Start.Ticks);
                        }
                        finally { SubtitlesSelectedHelper.CurIndex = previousSlot; }
                    }, cancellation);
                // Delay does not replace a source. Preserve both translator lifetimes/caches and refresh only
                // the selected text overlay after its existing resync path has cleared the old display.
                if (synchronized) RefreshOverlay(slot);
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
    }
}
