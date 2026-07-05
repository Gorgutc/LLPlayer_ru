using FlyleafLib.MediaFramework.MediaDemuxer;

namespace FlyleafLib.MediaPlayer;

#nullable enable

/// <summary>
/// Shared open/teardown for the "isolated second <c>avformat_open_input</c>" pattern used by the offline readers
/// (<c>AudioReader</c> for ASR, <c>WaveformReader</c> for the F-12 waveform, <c>SubtitleReader</c> for external
/// subtitle load) — HC-44 slice 2. Each such reader opens its OWN <see cref="Demuxer"/> on the media URL (never the
/// playing <c>AVFormatContext</c>; see the media-runtime contract) and wires the cancellation token to
/// force-interrupt a slow open. This helper single-sources that demuxer creation + interrupt registration +
/// log-prefix rename, and the matching teardown, so a fix to the fragile interrupt wiring is made once. The
/// per-reader specifics — stream cast + decoder setup, and each reader's own cancellation policy on an open error
/// (return vs throw) — stay in the caller, so behavior is unchanged. <c>MediaAudioProbe</c> keeps its own
/// local-demuxer / <c>using</c>-registration lifecycle and is intentionally not routed through here.
/// </summary>
internal static class OfflineDemuxer
{
    /// <summary>
    /// Creates an isolated <see cref="Demuxer"/> for <paramref name="type"/> with <paramref name="streamCount"/>,
    /// registers <paramref name="token"/> to force-interrupt a slow open, renames the demuxer log prefix to
    /// <paramref name="demuxerLogPrefix"/>, and opens <paramref name="url"/>. Returns the demuxer — which the caller
    /// stores and later disposes via <see cref="DisposeIsolated"/> — with any open error surfaced through
    /// <paramref name="error"/> for the caller to handle with its own cancellation policy.
    /// </summary>
    /// <remarks>Each offline reader is single-use (one <c>Open</c> per instance), so the interrupt callback capturing
    /// this local demuxer is equivalent to the previous inline capture of the reader's field.</remarks>
    public static Demuxer OpenIsolated(
        Config config, MediaType type, int streamCount, string demuxerLogPrefix,
        string url, CancellationToken token, out string? error)
    {
        Demuxer demuxer = new(config.Demuxer, type, streamCount, false);

        token.Register(() =>
        {
            demuxer.Interrupter.ForceInterrupt = 1;
        });

        demuxer.Log.Prefix = demuxer.Log.Prefix.Replace("Demuxer: ", demuxerLogPrefix);
        error = demuxer.Open(url);

        return demuxer;
    }

    /// <summary>Force-interrupt-clears and disposes an isolated demuxer (no-op when null), mirroring the inline
    /// teardown each offline reader used.</summary>
    public static void DisposeIsolated(Demuxer? demuxer)
    {
        if (demuxer != null)
        {
            demuxer.Interrupter.ForceInterrupt = 0;
            demuxer.Dispose();
        }
    }
}
