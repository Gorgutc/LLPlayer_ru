using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using CliWrap;
using CliWrap.EventStream;
using FlyleafLib.MediaPlayer.AsrOmissions;
using FlyleafLib.Plugins;

namespace FlyleafLib.MediaPlayer;

#nullable enable

internal static class FasterWhisperOmissionCapture
{
    private const int HeaderSize = 44;
    private const int SampleRate = 16_000;
    private const int BytesPerSample = 2;
    private static readonly Regex VadSpan = new(@"\[([\d:.]+) -> ([\d:.]+)\]", RegexOptions.CultureInvariant);
    private static readonly Regex KnownLanguage = new(@"\A[a-z]{2,3}\z", RegexOptions.CultureInvariant);
    private static readonly Regex CueLine = new(@"^\[\d{2}:(?:\d{2}:)?\d{2}\.\d{3} --> ", RegexOptions.CultureInvariant);

    private sealed record NativeEvidence(string? Language, IReadOnlyList<AsrOmissionCue> Cues);
    internal readonly record struct Tile(int StartSample, int SampleCount);

    internal sealed class RunOutput
    {
        public string? Device { get; private set; }
        public IReadOnlyList<AsrOmissionInterval>? Vad { get; private set; }
        public int CueCount { get; private set; }
        public string? LastLine { get; private set; }

        public void Observe(string line)
        {
            if (!string.IsNullOrWhiteSpace(line)) LastLine = line;
            if (line.StartsWith("Standalone Faster-Whisper", StringComparison.OrdinalIgnoreCase))
            {
                if (line.Contains("running on: CUDA", StringComparison.OrdinalIgnoreCase)) Device = "cuda";
                else if (line.Contains("running on: CPU", StringComparison.OrdinalIgnoreCase)) Device = "cpu";
            }
            if (line.StartsWith("VAD filter kept", StringComparison.OrdinalIgnoreCase))
                Vad = ParseVad(line);
            if (line.StartsWith('['))
            {
                Match cue = CueLine.Match(line);
                // A cue timestamp is fixed width; disregard lines with a genuinely empty text payload.
                if (cue.Success && line.AsSpan(cue.Length).IndexOf("] ") is int payloadIndex && payloadIndex >= 0 &&
                    !line.AsSpan(cue.Length + payloadIndex + 2).Trim().IsEmpty)
                    CueCount++;
            }
        }
    }

    internal static async ValueTask<AsrOmissionEvidence> CaptureAsync(Command baselineCommand,
        MemoryStream baselineWave, string nativeBaselinePath, RunOutput baselineOutput, string? language,
        ProcessPriorityClass priority, CancellationToken token)
    {
        AsrOmissionEvidence unavailable = new(AsrOmissionStatus.Unavailable);
        string? ownedRoot = null;
        try
        {
            token.ThrowIfCancellationRequested();
            if (!TryGetPcm(baselineWave, out ReadOnlyMemory<byte> wave, out int samples)) return unavailable;
            NativeEvidence? baseline = ReadNative(nativeBaselinePath);
            if (!HasMatchingNativeCues(baseline, baselineOutput.CueCount)) return unavailable;
            IReadOnlyList<AsrOmissionCue> a = baseline?.Cues ?? Array.Empty<AsrOmissionCue>();
            long duration = SampleMilliseconds(samples);
            if (!AsrOmissionDetector.ValidBaseline(new AsrOmissionChunk(0, duration, a, Array.Empty<AsrOmissionCue>())))
                return unavailable;

            List<string>? baselineArguments = FasterWhisperDevicePolicy.ReadArguments(baselineCommand.Arguments);
            if (baselineArguments is null) return unavailable;
            string? device = baselineOutput.Device ?? ReadOption(baselineArguments, "--device");
            if (FasterWhisperDevicePolicy.HasExplicitCpuDevice(baselineCommand.Arguments) && device != "cpu")
                return unavailable; // A CPU-admitted run must never start a GPU diagnostic pass.
            string? effectiveLanguage = baseline?.Language ?? ReadOption(baselineArguments, "--language") ?? language;
            if (device is not ("cpu" or "cuda") || effectiveLanguage is null || !KnownLanguage.IsMatch(effectiveLanguage))
                return unavailable;
            if (string.Equals(ReadOption(baselineArguments, "--initial_prompt"), "None", StringComparison.OrdinalIgnoreCase))
                return unavailable; // A already ran with the exact donor prompt setting.

            string root = ownedRoot = Directory.CreateTempSubdirectory("llplayer-asr-omissions-").FullName;
            string input = Path.Combine(root, "input.wav");
            await WriteWaveAsync(input, wave, token).ConfigureAwait(false);
            string donorOutput = Directory.CreateDirectory(Path.Combine(root, "b")).FullName;
            List<string>? arguments = MakeArguments(baselineArguments, donorOutput, input, device, effectiveLanguage);
            if (arguments is null) return unavailable;

            RunOutput? donorRun = await RunSupplementAsync(baselineCommand.TargetFilePath, arguments,
                priority, root, token).ConfigureAwait(false);
            if (donorRun is null || (donorRun.Device is not null && donorRun.Device != device)) return unavailable;
            NativeEvidence? donor = ReadNative(Path.Combine(donorOutput, "input.json"));
            if (!HasMatchingNativeCues(donor, donorRun.CueCount)) return unavailable;
            if (donor?.Language is { } donorLanguage && donorLanguage != effectiveLanguage) return unavailable;
            if (baselineOutput.Vad is { } aVad && donorRun.Vad is { } bVad && !aVad.SequenceEqual(bVad))
                return unavailable;

            IReadOnlyList<AsrOmissionCue> b = donor?.Cues ?? Array.Empty<AsrOmissionCue>();
            AsrOmissionStatus status = AsrOmissionStatus.Analyzed;
            List<AsrOmissionConfirmation> confirmations = [];
            var vad = baselineOutput.Vad is not null && donorRun.Vad is not null ? baselineOutput.Vad : null;
            if (MayNeedConfirmation(a, b))
            {
                var tiles = PlanTiles(samples);
                if (tiles.Count == 0 || vad is null)
                    status = AsrOmissionStatus.Partial;
                else if (vad.Count != 0)
                {
                    try
                    {
                        status = await CaptureConfirmationAsync(baselineCommand.TargetFilePath, baselineArguments, wave,
                            tiles, root, device, effectiveLanguage, priority, confirmations, token).ConfigureAwait(false);
                    }
                    catch (Exception e) when (IsDiagnosticFailure(e))
                    {
                        status = AsrOmissionStatus.Partial;
                    }
                }
            }
            token.ThrowIfCancellationRequested();
            return new(status, new AsrOmissionChunk(0, duration, a, b, confirmations, vad));
        }
        catch (EngineProcessDrainException)
        {
            // A still-running supplemental child may own its copied PCM and output files. Retry owns cleanup.
            ownedRoot = null;
            throw;
        }
        catch (Exception e) when (IsDiagnosticFailure(e))
        {
            return unavailable;
        }
        finally
        {
            if (ownedRoot is not null) DeleteOwnedDirectory(ownedRoot);
        }
    }

    private static async ValueTask<AsrOmissionStatus> CaptureConfirmationAsync(string enginePath,
        IReadOnlyList<string> baselineArguments, ReadOnlyMemory<byte> wave, IReadOnlyList<Tile> tiles, string root,
        string device, string language, ProcessPriorityClass priority, List<AsrOmissionConfirmation> confirmations,
        CancellationToken token)
    {
        string tileDirectory = Directory.CreateDirectory(Path.Combine(root, "tiles")).FullName;
        string output = Directory.CreateDirectory(Path.Combine(root, "c")).FullName;
        for (int index = 0; index < tiles.Count; index++)
        {
            Tile tile = tiles[index];
            byte[] header = wave[..HeaderSize].ToArray();
            int length = tile.SampleCount * BytesPerSample;
            BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4), length + HeaderSize - 8);
            BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(40), length);
            string path = Path.Combine(tileDirectory, $"tile-{index:D5}.wav");
            await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await file.WriteAsync(header.AsMemory(), token).ConfigureAwait(false);
            await file.WriteAsync(wave.Slice(HeaderSize + tile.StartSample * BytesPerSample, length), token)
                .ConfigureAwait(false);
        }
        List<string>? args = MakeArguments(baselineArguments, output, tileDirectory, device, language);
        if (args is null) return AsrOmissionStatus.Partial;
        RunOutput? outputEvidence = await RunSupplementAsync(enginePath, args, priority, root, token).ConfigureAwait(false);
        if (outputEvidence is null || (outputEvidence.Device is not null && outputEvidence.Device != device))
            return AsrOmissionStatus.Partial;

        bool incomplete = false;
        for (int index = 0; index < tiles.Count; index++)
        {
            Tile tile = tiles[index];
            NativeEvidence? evidence = ReadNative(Path.Combine(output, $"tile-{index:D5}.json"),
                SampleMilliseconds(tile.SampleCount));
            if (evidence is null || (evidence.Language is not null && evidence.Language != language))
            {
                incomplete = true;
                continue;
            }
            confirmations.Add(new(SampleMilliseconds(tile.StartSample), evidence.Cues));
        }
        return incomplete ? AsrOmissionStatus.Partial : AsrOmissionStatus.Analyzed;
    }

    internal static IReadOnlyList<Tile> PlanTiles(int samples)
    {
        if (samples < 2 * SampleRate) return Array.Empty<Tile>();
        // 24s/18s for normal chunks. A short baseline still needs a different context to be confirmation.
        int tileSize = samples <= 24 * SampleRate ? (int)(samples * 2L / 3) : 24 * SampleRate;
        int stride = tileSize * 3 / 4;
        List<Tile> tiles = [];
        for (int start = 0; start < samples; start += stride)
        {
            int length = Math.Min(tileSize, samples - start);
            tiles.Add(new(start, length));
            if (start + length == samples) break;
        }
        return tiles;
    }

    internal static List<string>? MakeArguments(IReadOnlyList<string> baseline, string output, string input,
        string device, string language)
    {
        List<string> result = [];
        for (int i = 0; i < baseline.Count; i++)
        {
            string value = baseline[i];
            int equals = value.IndexOf('=');
            string option = equals < 0 ? value : value[..equals];
            // An explicitly clipped or batched-input baseline has different position semantics for the C tiles.
            if (option is "--" or "--clip_timestamps" or "-clip") return null;
            if (option is "--output_format" or "-f")
            {
                while (i + 1 < baseline.Count && !baseline[i + 1].StartsWith('-')) i++;
                continue;
            }
            if (option is "--output_dir" or "-o" or "--initial_prompt" or "--language" or "-l" or "--device")
            {
                if (equals < 0 && ++i >= baseline.Count) return null;
                continue;
            }
            result.Add(value);
        }
        result.AddRange(["--output_dir", output, "--output_format", "json", "--language", language,
            "--device", device, "--initial_prompt", "None", input]);
        return result;
    }

    private static string? ReadOption(IReadOnlyList<string> args, string option)
    {
        string? result = null;
        for (int i = 0; i < args.Count; i++)
        {
            if (args[i] == option && i + 1 < args.Count) result = args[++i];
            else if (args[i].StartsWith(option + "=", StringComparison.Ordinal)) result = args[i][(option.Length + 1)..];
        }
        return result;
    }

    private static async ValueTask<RunOutput?> RunSupplementAsync(string enginePath, IEnumerable<string> arguments,
        ProcessPriorityClass priority, string ownedRoot, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var info = new ProcessStartInfo
        {
            FileName = Path.GetFullPath(enginePath), UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (string argument in arguments) info.ArgumentList.Add(argument);
        info.Environment["HF_HUB_OFFLINE"] = "1";
        info.Environment["TRANSFORMERS_OFFLINE"] = "1";
        var engine = OnlineVideoProcess.StartEngine(info, priority);
        try
        {
            RunOutput result = new();
            int? exitCode = null;
            await foreach (CommandEvent item in engine.ReadEngineEventsAsync(token).ConfigureAwait(false))
            {
                if (item is ExitedCommandEvent exited) exitCode = exited.ExitCode;
                if (item is StandardOutputCommandEvent stdout) result.Observe(stdout.Text);
            }
            return FasterWhisperASRService.IsSuccessfulAlignmentExit(exitCode, result.LastLine) ? result : null;
        }
        finally
        {
            try { await engine.DisposeEngineAsync().ConfigureAwait(false); }
            catch (Exception stopFailure)
            {
                throw new EngineProcessDrainException("Could not confirm that supplemental FasterWhisper stopped.",
                    stopFailure, async () =>
                    {
                        await engine.DisposeEngineAsync().ConfigureAwait(false);
                        DeleteOwnedDirectory(ownedRoot);
                    });
            }
        }
    }

    private static bool TryGetPcm(MemoryStream input, out ReadOnlyMemory<byte> wave, out int samples)
    {
        wave = input.TryGetBuffer(out ArraySegment<byte> segment) ? segment.AsMemory() : input.ToArray();
        samples = 0;
        ReadOnlySpan<byte> bytes = wave.Span;
        if (bytes.Length <= HeaderSize || !bytes[..4].SequenceEqual("RIFF"u8) ||
            !bytes.Slice(8, 8).SequenceEqual("WAVEfmt "u8) || BinaryPrimitives.ReadInt32LittleEndian(bytes[16..]) != 16 ||
            BinaryPrimitives.ReadUInt16LittleEndian(bytes[20..]) != 1 || BinaryPrimitives.ReadUInt16LittleEndian(bytes[22..]) != 1 ||
            BinaryPrimitives.ReadInt32LittleEndian(bytes[24..]) != SampleRate || BinaryPrimitives.ReadUInt16LittleEndian(bytes[34..]) != 16 ||
            !bytes.Slice(36, 4).SequenceEqual("data"u8) || BinaryPrimitives.ReadInt32LittleEndian(bytes[40..]) != bytes.Length - HeaderSize ||
            (bytes.Length - HeaderSize) % BytesPerSample != 0) return false;
        samples = (bytes.Length - HeaderSize) / BytesPerSample;
        return true;
    }

    private static async ValueTask WriteWaveAsync(string path, ReadOnlyMemory<byte> wave, CancellationToken token)
    {
        await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await file.WriteAsync(wave, token).ConfigureAwait(false);
    }

    private static long SampleMilliseconds(int samples) => (long)Math.Round(samples * 1000d / SampleRate);

    internal static bool MayNeedConfirmation(IReadOnlyList<AsrOmissionCue> baseline, IReadOnlyList<AsrOmissionCue> donor)
    {
        var occupied = baseline.SelectMany(cue => cue.Words.Count == 0
            ? [new AsrOmissionInterval(cue.StartMs, cue.EndMs)]
            : cue.Words.Where(word => word.EndMs > word.StartMs).Select(word => new AsrOmissionInterval(word.StartMs, word.EndMs)))
            .ToArray();
        // Several point-timed words can still form a positive donor range. Let the detector test the whole range.
        return donor.SelectMany(cue => cue.Words).Any(word => word.EndMs >= word.StartMs &&
            !occupied.Any(span => span.StartMs < word.EndMs && word.StartMs < span.EndMs));
    }

    private static bool HasMatchingNativeCues(NativeEvidence? evidence, int observedCues) =>
        (evidence?.Cues.Count ?? 0) == observedCues &&
        (observedCues == 0 || evidence!.Cues.Any(cue => cue.Words.Count != 0));

    private static NativeEvidence? ReadNative(string path, long? tileDuration = null)
    {
        if (!File.Exists(path)) return null;
        using FileStream file = File.OpenRead(path);
        using JsonDocument json = JsonDocument.Parse(file);
        var root = json.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("segments", out var segments) ||
            segments.ValueKind != JsonValueKind.Array) return null;

        string? language = root.TryGetProperty("language", out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() : null;
        List<AsrOmissionCue> cues = [];
        foreach (var segment in segments.EnumerateArray())
        {
            if (!ReadClock(segment, out long start, out long end) || !ReadText(segment, "text", out string text) ||
                !segment.TryGetProperty("words", out var nativeWords) || nativeWords.ValueKind != JsonValueKind.Array)
                return null;

            List<AsrOmissionWord> words = [];
            foreach (var nativeWord in nativeWords.EnumerateArray())
            {
                if (!ReadText(nativeWord, "word", out string word)) return null;
                bool clockValid = ReadClock(nativeWord, out long wordStart, out long wordEnd);
                if ((!clockValid || wordStart < 0 || wordEnd < wordStart) && tileDuration is null) return null;
                double? probability = null;
                if (clockValid && wordStart >= 0 && wordEnd >= wordStart && (tileDuration is null || wordEnd <= tileDuration) &&
                    nativeWord.TryGetProperty("probability", out var p) && p.ValueKind == JsonValueKind.Number &&
                    p.TryGetDouble(out double score) && double.IsFinite(score) && score is >= 0 and <= 1)
                    probability = score;
                words.Add(new(word, wordStart, wordEnd, probability));
            }
            cues.Add(new(text, start, end, words.AsReadOnly()));
        }
        return new(language, cues.AsReadOnly());
    }

    private static bool ReadClock(JsonElement value, out long start, out long end)
    {
        start = end = 0;
        if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty("start", out var a) ||
            !value.TryGetProperty("end", out var b) || a.ValueKind != JsonValueKind.Number || b.ValueKind != JsonValueKind.Number ||
            !a.TryGetDouble(out double first) || !b.TryGetDouble(out double last) ||
            !double.IsFinite(first) || !double.IsFinite(last) || Math.Abs(first) > int.MaxValue || Math.Abs(last) > int.MaxValue)
            return false;
        start = (long)Math.Round(first * 1000);
        end = (long)Math.Round(last * 1000);
        return true;
    }

    private static bool ReadText(JsonElement value, string name, out string text)
    {
        text = string.Empty;
        if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(name, out var str) ||
            str.ValueKind != JsonValueKind.String) return false;
        text = str.GetString() ?? string.Empty;
        return true;
    }

    private static IReadOnlyList<AsrOmissionInterval>? ParseVad(string line)
    {
        List<AsrOmissionInterval> intervals = [];
        foreach (Match match in VadSpan.Matches(line))
        {
            long? start = ReadVadTime(match.Groups[1].Value);
            long? end = ReadVadTime(match.Groups[2].Value);
            if (start is null || end is null || end <= start) return null;
            intervals.Add(new(start.Value, end.Value));
        }
        return intervals.Count == 0 && line.Contains('[') ? null : intervals;
    }

    private static long? ReadVadTime(string clock)
    {
        string[] parts = clock.Split(':');
        if (parts.Length is < 2 or > 3) return null;
        double seconds = 0;
        foreach (string part in parts)
        {
            if (!double.TryParse(part, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double value) ||
                !double.IsFinite(value) || value < 0 || value > int.MaxValue) return null;
            seconds = seconds * 60 + value;
        }
        return (long)Math.Round(seconds * 1000);
    }

    private static bool IsDiagnosticFailure(Exception error) => error is not EngineProcessDrainException &&
        error is IOException or UnauthorizedAccessException or JsonException or TimeoutException or InvalidOperationException or
            ArgumentException or System.ComponentModel.Win32Exception or NotSupportedException;

    private static void DeleteOwnedDirectory(string directory)
    {
        try { Directory.Delete(directory, recursive: true); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* Primary ASR evidence is independent. */ }
    }
}
