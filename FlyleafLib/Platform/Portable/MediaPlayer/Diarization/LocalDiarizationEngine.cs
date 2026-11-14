#nullable enable

using System.Diagnostics;
using System.Text.Json;
using CliWrap.EventStream;
using FlyleafLib.Plugins;

namespace FlyleafLib.MediaPlayer.Diarization;

/// <summary>Runs the packaged pyannote sidecar using an explicitly provisioned interpreter and model.</summary>
public sealed class LocalDiarizationEngine : IDiarizationEngine
{
    private const int MaxResponseChars = 4 * 1024 * 1024;
    private const int MaxTurns = 32_768;
    private const int MaxSpeakerChars = 100;
    private static readonly LogHandler Log = new("[Diarization] ");
    private readonly string _pythonExecutablePath;
    private readonly string _localModelPath;
    private readonly string _device;

    public LocalDiarizationEngine(string pythonExecutablePath, string localModelPath, string device)
    {
        if (!OnlineVideoRequest.IsExecutablePath(pythonExecutablePath))
            throw new ArgumentException("Select an existing absolute Python executable path.", nameof(pythonExecutablePath));
        if (!Path.IsPathFullyQualified(localModelPath) || !Directory.Exists(localModelPath))
            throw new ArgumentException("Select the existing local model directory.", nameof(localModelPath));
        if (device is not ("cpu" or "cuda"))
            throw new ArgumentOutOfRangeException(nameof(device));

        _pythonExecutablePath = pythonExecutablePath;
        _localModelPath = localModelPath;
        _device = device;
    }

    public Task<IReadOnlyList<DiarizationTurn>> RunAsync(DiarizationAudioSource source, CancellationToken token)
        => Task.Run(() => RunCoreAsync(source, token), token);

    private async Task<IReadOnlyList<DiarizationTurn>> RunCoreAsync(DiarizationAudioSource source, CancellationToken token)
    {
        string scriptPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Diarization", "sidecar.py");
        if (!File.Exists(scriptPath))
            throw new FileNotFoundException("The packaged diarization sidecar is missing.");

        string directory = Directory.CreateTempSubdirectory("llplayer-diarization-").FullName;
        string wavePath = Path.Combine(directory, "audio.wav");
        OnlineVideoProcess? process = null;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromHours(2));

        try
        {
            TimeSpan duration;
            using (var wave = new FileStream(wavePath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                duration = DiarizationAudioExtractor.WriteWave(source, wave, deadline.Token);

            deadline.Token.ThrowIfCancellationRequested();
            var info = new ProcessStartInfo(_pythonExecutablePath)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = directory
            };
            foreach (string argument in new[] { "-I", "-B", scriptPath, "--audio", wavePath, "--model", _localModelPath, "--device", _device })
                info.ArgumentList.Add(argument);

            process = OnlineVideoProcess.StartEngine(info);
            string response = await ReadResponseAsync(process, deadline.Token).ConfigureAwait(false);
            deadline.Token.ThrowIfCancellationRequested();
            return ParseResponse(response, duration);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested && deadline.IsCancellationRequested)
        {
            throw new TimeoutException("Local diarization exceeded its two-hour time limit.");
        }
        finally
        {
            if (process is not null)
            {
                try { await process.DisposeEngineAsync().ConfigureAwait(false); }
                catch (Exception stopFailure)
                {
                    throw new EngineProcessDrainException("Could not confirm that the diarization sidecar stopped. Retry shutdown.",
                        stopFailure, async () =>
                        {
                            await process.DisposeEngineAsync().ConfigureAwait(false);
                            CleanupWave();
                        });
                }
            }
            CleanupWave();
        }

        void CleanupWave()
        {
            try
            {
                File.Delete(wavePath);
                Directory.Delete(directory);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (CanWarn) Log.Warn($"Could not clean up the diarization temporary audio: {ex.Message}");
            }
        }
    }

    internal static async Task<string> ReadResponseAsync(OnlineVideoProcess process, CancellationToken token)
    {
        string? response = null;
        string? detail = null;
        int? exitCode = null;
        await foreach (CommandEvent item in process.ReadEngineEventsAsync(token).ConfigureAwait(false))
        {
            switch (item)
            {
                case StandardOutputCommandEvent stdout:
                    if (response is not null || stdout.Text.Length > MaxResponseChars)
                        throw new InvalidDataException("The diarization sidecar returned an invalid response size.");
                    response = stdout.Text;
                    break;
                case StandardErrorCommandEvent stderr when stderr.Text.Length > 0:
                    detail = stderr.Text[..Math.Min(512, stderr.Text.Length)];
                    break;
                case ExitedCommandEvent exited:
                    exitCode = exited.ExitCode;
                    break;
            }
        }

        if (exitCode != 0)
        {
            string status = detail is null ? "" : $" {detail}";
            throw new IOException($"The local diarization sidecar failed (exit code {exitCode}).{status}");
        }
        return response ?? throw new InvalidDataException("The diarization sidecar did not return a response.");
    }

    internal static IReadOnlyList<DiarizationTurn> ParseResponse(string response, TimeSpan duration)
    {
        try
        {
            using var json = JsonDocument.Parse(response, new JsonDocumentOptions { MaxDepth = 8 });
            JsonElement root = json.RootElement;
            if (root.GetProperty("version").GetInt32() != 1)
                throw new InvalidDataException("The local diarization response version is unsupported.");

            JsonElement rows = root.GetProperty("turns");
            int count = rows.GetArrayLength();
            if (count > MaxTurns)
                throw new InvalidDataException("The local diarization response contains too many speaker intervals.");

            var turns = new List<DiarizationTurn>(count);
            foreach (JsonElement row in rows.EnumerateArray())
            {
                double start = row.GetProperty("start").GetDouble();
                double end = row.GetProperty("end").GetDouble();
                string? speaker = row.GetProperty("speaker").GetString();
                if (!double.IsFinite(start) || !double.IsFinite(end) ||
                    start < 0 || end <= start || end > duration.TotalSeconds ||
                    string.IsNullOrWhiteSpace(speaker) || speaker.Length > MaxSpeakerChars)
                    throw new InvalidDataException("The local diarization response contains an invalid speaker interval.");

                TimeSpan startTime = TimeSpan.FromSeconds(start);
                TimeSpan endTime = TimeSpan.FromSeconds(end);
                if (endTime <= startTime)
                    throw new InvalidDataException("The local diarization response contains an empty speaker interval.");
                turns.Add(new(startTime, endTime, speaker));
            }
            return turns.AsReadOnly();
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        {
            throw new InvalidDataException("The local diarization sidecar returned invalid data.", ex);
        }
    }
}
