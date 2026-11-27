#nullable enable

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FlyleafLib.MediaPlayer.Batch;

namespace FlyleafLib.MediaPlayer;

public enum SrtAlignmentDevice { Cpu, Cuda }

public readonly record struct SrtAlignmentAudioWindow(TimeSpan Start, TimeSpan End)
{
    internal static readonly TimeSpan MaxDuration = TimeSpan.FromSeconds(28);
}

public sealed record SrtAlignmentAudioRequest(string MediaPath, int StreamIndex, string Language, SrtAlignmentDevice Device)
{
    public SrtAlignmentAudioWindow? Window { get; init; }
}

/// <summary>Reads timestamped words from the selected local audio stream without changing playback subtitles.</summary>
public static class SrtAlignmentAudioSource
{
    public static Task<IReadOnlyList<WordTiming>> ReadWordsAsync(Config recognitionSnapshot, SrtAlignmentAudioRequest request,
        CancellationToken token) => Task.Run<IReadOnlyList<WordTiming>>(() =>
    {
        token.ThrowIfCancellationRequested();
        if (!Path.IsPathFullyQualified(request.MediaPath) || new Uri(request.MediaPath).IsUnc ||
            !File.Exists(request.MediaPath) || request.StreamIndex < 0)
            throw new ArgumentException("Alignment requires an existing local media file and a selected audio stream.", nameof(request));
        if (request.Window is { } window && (window.Start < TimeSpan.Zero || window.End <= window.Start ||
            window.End - window.Start > SrtAlignmentAudioWindow.MaxDuration))
            throw new ArgumentException("An alignment retry window must be positive and at most 28 seconds.", nameof(request));

        Config settings = CreateRunSettings(recognitionSnapshot, request);
        using var reader = new AudioReader(settings, 0) { StrictAlignment = true, AlignmentWindow = request.Window };
        reader.Open(request.MediaPath, request.StreamIndex, MediaType.Video, token);
        token.ThrowIfCancellationRequested();

        List<WordTiming> result = [];
        reader.ReadAll(TimeSpan.Zero, cue =>
        {
            if (cue.Words is { } words)
                result.AddRange(words);
        }, token);
        token.ThrowIfCancellationRequested();
        return result.AsReadOnly();
    }, token);

    internal static Config CreateRunSettings(Config source, SrtAlignmentAudioRequest request)
    {
        (string device, string computeType) = request.Device switch
        {
            SrtAlignmentDevice.Cpu => ("cpu", "float32"),
            SrtAlignmentDevice.Cuda => ("cuda", "float16"),
            _ => throw new ArgumentOutOfRangeException(nameof(request))
        };
        if (!WhisperLanguage.LanguageToCode.ContainsValue(request.Language))
            throw new ArgumentException("Select a supported language for alignment.", nameof(request));

        Config snapshot = BatchSubtitleConfigSnapshot.CreateForRecognition(source);
        FasterWhisperConfig engine = snapshot.Subtitles.FasterWhisperConfig;
        string? enginePath = engine.UseManualEngine ? engine.ManualEnginePath : FasterWhisperConfig.DefaultEnginePath;
        if (enginePath is null || !FlyleafLib.Plugins.OnlineVideoProcess.IsExecutablePath(enginePath))
            throw new FileNotFoundException("Configure an installed Faster Whisper XXL executable for alignment.", enginePath);
        if (engine.IsEnglishModel && request.Language != "en")
            throw new InvalidOperationException("The selected Faster Whisper model supports English only.");

        string model = FindLocalModel(engine.UseManualModel ? engine.ManualModelDir : WhisperConfig.ModelsDirectory, engine.Model);
        engine.UseManualModel = true;
        engine.ManualModelDir = Path.GetDirectoryName(model)!;
        engine.Prompt = string.Empty;
        engine.ExtraArguments = $"--device {device} --compute_type {computeType} --task transcribe";
        snapshot.Subtitles.ASREngine = SubASREngineType.FasterWhisper;
        snapshot.Subtitles.WordTimestamps = true;
        snapshot.Subtitles.WhisperConfig.Language = request.Language;
        snapshot.Subtitles.WhisperConfig.LanguageDetection = false;
        snapshot.Subtitles.WhisperConfig.Translate = false;
        snapshot.Demuxer.FormatOpt["protocol_whitelist"] = "file";
        return snapshot;
    }

    internal static string FindLocalModel(string? directory, string model)
    {
        if (directory is null || !Path.IsPathFullyQualified(directory) || new Uri(directory).IsUnc ||
            !FasterWhisperConfig.ModelOptions.Contains(model))
            throw new FileNotFoundException("Configure a local Faster Whisper model directory for alignment.");

        // XXL resolves a named model to this exact child; passing an arbitrary absolute --model path is unsupported.
        string modelName = $"faster-whisper-{model}";
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        string path = string.Equals(Path.GetFileName(root), modelName, StringComparison.Ordinal)
            ? root : Path.Combine(root, modelName);
        if (!HasFile("model.bin") || !HasFile("config.json") || !HasFile("tokenizer.json") ||
            (!HasFile("vocabulary.json") && !HasFile("vocabulary.txt")))
            throw new FileNotFoundException("Alignment requires a complete installed Faster Whisper model.", path);
        return path;

        bool HasFile(string name)
        {
            FileInfo file = new(Path.Combine(path, name));
            return file.Exists && file.Length > 0;
        }
    }

    internal static IReadOnlyList<IReadOnlyList<WordTiming>> ReadWordEvidence(string path,
        IReadOnlyList<(string text, TimeSpan start, TimeSpan end, string language)> expected)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using JsonDocument document = JsonDocument.Parse(stream);
            JsonElement segments = document.RootElement.GetProperty("segments");
            if (segments.GetArrayLength() != expected.Count)
                throw InvalidEvidence();

            var result = new List<IReadOnlyList<WordTiming>>(expected.Count);
            foreach (JsonElement segment in segments.EnumerateArray())
            {
                var cue = expected[result.Count];
                if (segment.GetProperty("text").GetString()?.Trim() != cue.text ||
                    (ReadTime(segment, "start") - cue.start).Duration() > TimeSpan.FromMilliseconds(2) ||
                    (ReadTime(segment, "end") - cue.end).Duration() > TimeSpan.FromMilliseconds(2))
                    throw InvalidEvidence();

                JsonElement values = segment.GetProperty("words");
                var words = new List<WordTiming>(values.GetArrayLength());
                foreach (JsonElement word in values.EnumerateArray())
                {
                    string? text = word.GetProperty("word").GetString();
                    if (string.IsNullOrWhiteSpace(text))
                        throw InvalidEvidence();
                    words.Add(new(text, ReadTime(word, "start"), ReadTime(word, "end")));
                }
                if (words.Count == 0 || string.Concat(words.Select(word => word.Word)).Trim() != cue.text)
                    throw InvalidEvidence();
                result.Add(words);
            }
            return result;
        }
        catch (Exception error) when (error is FileNotFoundException or JsonException or KeyNotFoundException
            or InvalidOperationException or OverflowException or FormatException)
        {
            throw new InvalidDataException("Faster Whisper returned invalid word timing JSON.", error);
        }
    }

    internal static IReadOnlyList<WordTiming> ToMediaWords(IReadOnlyList<WordTiming> words, TimeSpan start, TimeSpan duration)
    {
        var result = new List<WordTiming>(words.Count);
        TimeSpan previous = TimeSpan.Zero;
        foreach (WordTiming word in words)
        {
            if (word.Start < previous || word.End < word.Start || word.End > duration)
                throw InvalidEvidence();

            // XXL can recognize a word while reporting equal start and end. Its text is part of
            // the transcript, but it cannot provide a timing anchor. Keep the other words.
            if (word.End > word.Start)
                result.Add(new(word.Word, start + word.Start, start + word.End));
            previous = word.Start;
        }
        return result;
    }

    static TimeSpan ReadTime(JsonElement value, string key) => TimeSpan.FromSeconds(value.GetProperty(key).GetDouble());

    static InvalidDataException InvalidEvidence() => new("Faster Whisper word timings do not match the recognized audio segment.");
}
