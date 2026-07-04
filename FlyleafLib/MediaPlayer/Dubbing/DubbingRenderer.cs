using System.Collections.Generic;
using System.Linq;

namespace FlyleafLib.MediaPlayer.Dubbing;

#nullable enable

/// <summary>
/// Default dub renderer. Owns a run-scoped <see cref="DubSidecarHost"/> across all renders in one run.
/// It synthesizes each translated line, computes the isochrony fit + timeline placement in C# (the unit-tested
/// <see cref="DubbingIsochrony"/> math), then asks the sidecar to assemble + duck + encode the track.
/// </summary>
public sealed class DubbingRenderer : IDubbingRenderer
{
    private readonly string _voiceId;
    private readonly double _atempoMin;
    private readonly double _atempoMax;
    private readonly int _duckingPercent;
    private readonly string _outputFormat;
    private readonly bool _mock;
    private readonly DubSidecarHost.DubbingSnapshot _sidecarSnapshot;
    private readonly SemaphoreSlim _hostGate = new(1, 1);
    // HC-43: outputs whose assemble was canceled/failed may still materialize after the fact (the sidecar's
    // os.replace lands after the HTTP request is cancelled). Re-cleaned at the next render of the same target and in
    // DisposeAsync (once the sidecar is reaped).
    private readonly DubOrphanCleanup _orphans = new();
    private DubSidecarHost? _host;
    private bool _disposed;

    /// <param name="config">Live dubbing config; scalar fields are snapshotted here so a later config
    /// change does not affect an in-flight render.</param>
    /// <param name="mock">Run the sidecar in mock mode (tone/silence, no heavy models) for off-GPU tests.</param>
    public DubbingRenderer(DubbingConfig config, bool mock = false)
    {
        ArgumentNullException.ThrowIfNull(config);
        _voiceId = config.DefaultVoiceId;
        _atempoMin = config.AtempoMin;
        _atempoMax = config.AtempoMax;
        _duckingPercent = config.DuckingPercent;
        _outputFormat = string.IsNullOrWhiteSpace(config.OutputFormat)
            ? DubbingOutputPathBuilder.DefaultExtension
            : config.OutputFormat.TrimStart('.').Trim().ToLowerInvariant();
        _mock = mock;
        _sidecarSnapshot = DubSidecarHost.DubbingSnapshot.From(config);
    }

    public async Task RenderAsync(
        IReadOnlyList<SubtitleData> translatedSubtitles,
        string mediaPath,
        string outputPath,
        IProgress<DubbingProgress>? progress,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(translatedSubtitles);
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        // HC-43: if a prior render of this same target was canceled, an orphaned dub may have landed after the fact —
        // delete it before we (re)render, and forget the record so a fresh success is not re-cleaned on dispose.
        _orphans.ClearAndClean(outputPath, TryDeleteOutput);

        List<DubbingLine> lines = BuildLines(translatedSubtitles);
        if (lines.Count == 0)
            return;

        DubSidecarHost host = await GetHostAsync(token).ConfigureAwait(false);

        // 1. Synthesize each line; record the atempo fit and the resulting (post-stretch) duration.
        double[] renderedMs = new double[lines.Count];
        double[] atempo = new double[lines.Count];
        string[] wav = new string[lines.Count];

        for (int i = 0; i < lines.Count; i++)
        {
            token.ThrowIfCancellationRequested();

            int targetMs = (int)System.Math.Round(System.Math.Max(1, lines[i].SlotMs));
            TtsResult res = await host.SynthesizeAsync(
                new TtsRequest(lines[i].Text, ResolveVoiceId(lines[i].VoiceId, _voiceId), targetMs), token).ConfigureAwait(false);

            double f = DubbingIsochrony.ComputeAtempo(res.DurationMs, lines[i].SlotMs, _atempoMin, _atempoMax);
            atempo[i] = f;
            renderedMs[i] = f > 0 ? res.DurationMs / f : res.DurationMs;
            wav[i] = res.WavPath;

            progress?.Report(new DubbingProgress(i + 1, lines.Count, "synthesize"));
        }

        // 2. Place clips on the timeline (drift-reset at pauses) — pure C# math.
        IReadOnlyList<DubbingIsochrony.Placement> placements = DubbingIsochrony.Place(lines, renderedMs);

        List<DubClip> clips = new(lines.Count);
        for (int i = 0; i < lines.Count; i++)
            clips.Add(new DubClip(wav[i], placements[i].StartMs, atempo[i]));

        double totalMs = placements.Count > 0 ? placements[^1].EndMs : 0;

        // 3. Assemble + duck + encode in the sidecar (decodes the original via libav in Python).
        progress?.Report(new DubbingProgress(lines.Count, lines.Count, "assemble"));
        try
        {
            await host.AssembleAsync(
                new AssembleRequest(mediaPath, outputPath, _outputFormat, _duckingPercent, totalMs, clips),
                token).ConfigureAwait(false);
        }
        catch
        {
            // The sidecar writes the final atomically (no truncated file), but a cancel can still leave a
            // complete-but-unwanted dub. Best-effort delete now; but the sidecar's os.replace can land AFTER this
            // (the HTTP cancel doesn't stop the worker), so also record the target (HC-43) for a reliable re-clean at
            // the next render of this file and in DisposeAsync, once the sidecar is reaped. Rethrow so the caller
            // marks the job Canceled/Failed.
            TryDeleteOutput(outputPath);
            _orphans.MarkCanceled(outputPath);
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        await _hostGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed)
                return;

            _disposed = true;
            if (_host is not null)
            {
                await _host.DisposeAsync().ConfigureAwait(false);
                _host = null;
            }

            // HC-43: the sidecar process is now reaped (DubSidecarHost.DisposeAsync waits for exit / kills the tree),
            // so any orphaned dub from a canceled render has finished landing. Re-delete the recorded targets — this
            // is the reliable catch the immediate in-catch delete can miss.
            _orphans.CleanAll(TryDeleteOutput);
        }
        finally
        {
            _hostGate.Release();
        }
    }

    private async Task<DubSidecarHost> GetHostAsync(CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_host is not null)
            return _host;

        await _hostGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_host is not null)
                return _host;

            DubSidecarHost host = DubSidecarHost.Create(_sidecarSnapshot, _mock);
            try
            {
                if (!await host.TryInitializeAsync(token).ConfigureAwait(false))
                    throw new InvalidOperationException("The dubbing sidecar did not become ready.");

                _host = host;
                return host;
            }
            catch
            {
                await host.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }
        finally
        {
            _hostGate.Release();
        }
    }

    private static void TryDeleteOutput(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // best-effort cleanup
        }
    }

    // Russian lines to dub: prefer the translated text, fall back to the (already-Russian) source text.
    // Ordered by start time; lines with no usable text or a non-positive slot are skipped. Each line carries its
    // optional per-line voice override (F-16 phase 2a) — a blank AssignedVoiceId becomes null so RenderAsync falls
    // back to the run's default voice. internal for unit tests (the mapping must stay byte-identical when no cue
    // carries an override).
    internal static List<DubbingLine> BuildLines(IReadOnlyList<SubtitleData> subtitles)
    {
        return subtitles
            .Where(s => !string.IsNullOrWhiteSpace(s.TranslatedText) || !string.IsNullOrWhiteSpace(s.Text))
            .OrderBy(s => s.StartTime)
            .Select((s, index) =>
            {
                string text = !string.IsNullOrWhiteSpace(s.TranslatedText) ? s.TranslatedText! : s.Text!;
                string? voiceId = string.IsNullOrWhiteSpace(s.AssignedVoiceId) ? null : s.AssignedVoiceId.Trim();
                return new DubbingLine(index, text.Trim(), s.StartTime, s.EndTime, voiceId);
            })
            .Where(l => l.SlotMs > 0 && !string.IsNullOrWhiteSpace(l.Text))
            .ToList();
    }

    // Per-line dub voice (F-16 phase 2a): a cue's per-line override wins; a blank/absent override falls back to the
    // run's snapshotted default voice. With no overrides this returns the default for every line, so the
    // synthesized request — and the whole dub — is byte-identical to the single-voice render. internal for tests.
    internal static string ResolveVoiceId(string? lineVoiceId, string defaultVoiceId)
        => string.IsNullOrWhiteSpace(lineVoiceId) ? defaultVoiceId : lineVoiceId;
}
