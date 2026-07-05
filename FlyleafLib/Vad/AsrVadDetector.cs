using System;
using System.Collections.Generic;
using System.IO;

namespace FlyleafLib.Vad;

#nullable enable

/// <summary>
/// F-19 slice 2: speech-aware cue-boundary snapping. Wraps the vendored Silero VAD (ONNX Runtime, CPU) to turn an
/// in-memory S16-mono-16kHz WAV chunk (the exact buffer the ASR pipeline already produces for Whisper) into a list
/// of <see cref="SpeechSegment"/>s. <see cref="SubtitleSegmenter.Resegment"/> then snaps split-cue boundaries to the
/// nearest speech/silence edge, so a cue switches during a pause instead of mid-word — insurance where the slice-1
/// word timings drift (music/noise).
///
/// Everything here is fail-soft: <see cref="TryCreate"/> returns null when the model file is missing or ONNX Runtime
/// cannot load, and <see cref="Detect"/> returns an empty list on any error — in both cases re-segmentation falls
/// back to the slice-1 (word-timestamp / character-proportion) timing, byte-identical. Native (the ONNX inference)
/// is not unit-tested; the pure snapping math is (see SubtitleSegmenter / SubtitleSegmenterTests).
/// </summary>
public sealed class AsrVadDetector : IDisposable
{
    // Silero example defaults; sensible for cue-boundary snapping over speech. 16 kHz matches the ASR resampler.
    private const float Threshold = 0.5f;
    private const int SampleRate = 16000;
    private const int MinSpeechDurationMs = 250;
    private const float MaxSpeechDurationSeconds = float.PositiveInfinity;
    private const int MinSilenceDurationMs = 100;
    private const int SpeechPadMs = 30;

    // The ASR pipeline hands us the same WAV it writes for Whisper: a 44-byte canonical header + S16 mono PCM.
    private const int WavHeaderSize = 44;

    private static readonly LogHandler Log = new(("[#1]").PadRight(8, ' ') + " [AsrVadDetector] ");

    private readonly SileroVadDetector _detector;

    private AsrVadDetector(SileroVadDetector detector) => _detector = detector;

    /// <summary>Path to the bundled Silero VAD model (shipped next to the exe, like the other native assets).</summary>
    public static string ModelPath { get; } =
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "silero_vad.onnx");

    /// <summary>
    /// Loads the VAD model. Returns null (logged) if the model is missing or ONNX Runtime fails to initialize, so the
    /// caller can skip VAD snapping entirely and keep the previous timing behaviour (byte-identical).
    /// </summary>
    public static AsrVadDetector? TryCreate()
    {
        try
        {
            if (!File.Exists(ModelPath))
            {
                Log.Warn($"Silero VAD model not found at '{ModelPath}'; cue-timing VAD snapping disabled for this run.");
                return null;
            }

            var detector = new SileroVadDetector(ModelPath, Threshold, SampleRate,
                MinSpeechDurationMs, MaxSpeechDurationSeconds, MinSilenceDurationMs, SpeechPadMs);
            return new AsrVadDetector(detector);
        }
        catch (Exception e)
        {
            Log.Warn($"Could not initialize Silero VAD (ONNX Runtime); cue-timing VAD snapping disabled: {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// Detects speech segments (chunk-relative time) from an in-memory S16 mono 16 kHz WAV stream (44-byte header +
    /// PCM). Non-destructive: reads the underlying buffer without touching the stream position, so the same stream is
    /// still handed to the ASR engine unchanged. Returns an empty list on any failure (fail-soft).
    /// </summary>
    public IReadOnlyList<SpeechSegment> Detect(MemoryStream wavStream)
    {
        try
        {
            // GetBuffer() exposes the backing array without moving Position/Length (the stream is a parameterless
            // MemoryStream, so it is publicly visible). The ASR engine reads it afterwards via WriteTo (whole buffer).
            byte[] buf = wavStream.GetBuffer();
            int len = (int)wavStream.Length;
            if (len <= WavHeaderSize)
                return Array.Empty<SpeechSegment>();

            int sampleCount = (len - WavHeaderSize) / 2;
            if (sampleCount < 512) // one VAD window @16k; nothing to analyze
                return Array.Empty<SpeechSegment>();

            float[] samples = new float[sampleCount];
            for (int i = 0; i < sampleCount; i++)
            {
                int b = WavHeaderSize + (i * 2);
                short s16 = (short)(buf[b] | (buf[b + 1] << 8)); // little-endian S16
                samples[i] = s16 / 32768f;
            }

            List<SileroSpeechSegment> segments = _detector.GetSpeechSegments(samples);
            if (segments.Count == 0)
                return Array.Empty<SpeechSegment>();

            List<SpeechSegment> result = new(segments.Count);
            foreach (SileroSpeechSegment seg in segments)
            {
                if (seg.StartSecond is float a && seg.EndSecond is float b && b > a)
                    result.Add(new SpeechSegment(TimeSpan.FromSeconds(a), TimeSpan.FromSeconds(b)));
            }

            return result;
        }
        catch (Exception e)
        {
            Log.Warn($"Silero VAD detect failed for a chunk; skipping snap (falling back to prior timing): {e.Message}");
            return Array.Empty<SpeechSegment>();
        }
    }

    public void Dispose() => _detector.Dispose();
}
