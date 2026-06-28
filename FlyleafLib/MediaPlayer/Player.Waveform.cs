using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FlyleafLib.MediaFramework.MediaStream;
using static FlyleafLib.Logger;

namespace FlyleafLib.MediaPlayer;

#nullable enable

partial class Player
{
    // ===== F-12 audio waveform (opt-in seek-bar visualization) =====
    // Decoded once per opened file on a background worker via an isolated WaveformReader (its own
    // demuxer/decoder — never the playing pipeline), reduced to a fixed bucket envelope, and exposed bindably to
    // the seek bar. OFF by default (WaveformEnabled=false): no decode runs, no peaks, the overlay is collapsed →
    // OFF byte-identical. The enabled flag is owned here and mirrored from the LLPlayer AppConfig toggle
    // (FlyleafLib cannot reference the app config), so the engine stays the single owner of the build lifecycle.
    //
    // CTS ownership: each build creates its own CancellationTokenSource and is the SOLE disposer of it (in its
    // worker's finally, after detaching the shared field under the lock). The swap/clear/reset sites only Cancel
    // the current CTS — they never Dispose it — so a queued worker can never hit a disposed token in Open
    // (which would throw ObjectDisposedException). The worker body always runs (no token passed to Task.Run) so
    // its finally always disposes exactly once.

    // Envelope bucket count across the whole file. ~2000 exceeds any realistic seek-bar pixel width, so both the
    // peaks float[] and the rendered geometry stay tiny regardless of file length (a 3 h film is a few KB).
    private const int WaveformBucketCount = 2000;

    private readonly object _waveformLock = new();
    private CancellationTokenSource? _waveformCts;
    private bool _waveformEnabled;

    /// <summary>0..1 max-abs amplitude buckets across the whole file, or null when off/not-yet-built. Bindable.</summary>
    public float[]? WaveformPeaks { get; private set; }

    /// <summary>True when an envelope is available to draw (drives the overlay visibility). Bindable.</summary>
    public bool WaveformActive => WaveformPeaks != null;

    /// <summary>
    /// Opt-in toggle, mirrored from the app's persisted setting. Turning it on (with a file open) kicks a
    /// background build; turning it off cancels any build and clears the envelope so the overlay disappears.
    /// </summary>
    public bool WaveformEnabled
    {
        get => _waveformEnabled;
        set
        {
            if (_waveformEnabled == value)
                return;

            _waveformEnabled = value;
            Raise(nameof(WaveformEnabled));

            if (value)
                TriggerWaveformBuild();
            else
                ClearWaveform();
        }
    }

    /// <summary>
    /// Starts a background waveform build for the currently open file, if enabled and applicable. Called from the
    /// audio-open completion (duration known) and from the enable toggle. No-op for live/HLS, no audio stream,
    /// unknown duration, or a non-local URL (v1 targets local files with a known duration).
    /// </summary>
    internal void TriggerWaveformBuild()
    {
        if (!_waveformEnabled || !canPlay || isLive || duration <= 0 || !Audio.IsOpened)
            return;

        string? url = AudioDecoder?.Demuxer?.Url;
        if (string.IsNullOrEmpty(url) || !File.Exists(url))
            return;

        AudioStream? stream = AudioDecoder?.AudioStream;
        if (stream == null)
            return;

        int streamIndex = stream.StreamIndex;
        MediaType type = AudioDecoder!.Demuxer!.Type;
        long dur = duration;

        CancellationTokenSource cts = new();
        CancellationToken token = cts.Token;
        lock (_waveformLock)
        {
            _waveformCts?.Cancel(); // signal any prior build; that build's own worker disposes its CTS
            _waveformCts = cts;
        }

        SetWaveformPeaks(null);

        // No token passed to Task.Run: the body must always run so its finally disposes this CTS exactly once.
        _ = Task.Run(() =>
        {
            float[]? peaks = null;
            try
            {
                if (token.IsCancellationRequested)
                    return;

                using WaveformReader reader = new(Config);
                reader.Open(url, streamIndex, type, token);
                peaks = reader.ReadPeaks(WaveformBucketCount, dur, null, token);
            }
            catch (Exception e)
            {
                // Fail-soft: unsupported codec / decode error → no overlay, never a popup.
                if (CanWarn) Log.Warn($"Waveform build failed: {e.Message}");
                peaks = null;
            }
            finally
            {
                if (!token.IsCancellationRequested)
                    SetWaveformPeaks(peaks);

                lock (_waveformLock)
                {
                    if (_waveformCts == cts)
                        _waveformCts = null;
                }
                cts.Dispose(); // this worker owns this CTS; dispose exactly once, after detaching the field
            }
        });
    }

    /// <summary>Cancels any in-flight build and clears the envelope (used by the toggle-off path).</summary>
    private void ClearWaveform()
    {
        lock (_waveformLock)
            _waveformCts?.Cancel(); // the owning worker disposes its CTS in its finally

        SetWaveformPeaks(null);
    }

    // Reset on new file open / stop (no UI raise — called from the non-UI part of ResetMe; the matching raise
    // runs in its UIAdd block, mirroring ResetABLoop/RaiseABLoopChanged).
    internal void ResetWaveform()
    {
        lock (_waveformLock)
            _waveformCts?.Cancel(); // cancel any in-flight build; its worker disposes its CTS

        WaveformPeaks = null;
    }

    private void SetWaveformPeaks(float[]? peaks)
    {
        WaveformPeaks = peaks;
        UI(() =>
        {
            Raise(nameof(WaveformPeaks));
            Raise(nameof(WaveformActive));
        });
    }

    // Matching UI raise for ResetWaveform (called from ResetMe's UIAdd block, like RaiseABLoopChanged).
    private void RaiseWaveformChanged()
    {
        Raise(nameof(WaveformPeaks));
        Raise(nameof(WaveformActive));
    }
}
