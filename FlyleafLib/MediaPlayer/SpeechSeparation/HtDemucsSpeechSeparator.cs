#nullable enable

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using CliWrap.EventStream;
using FlyleafLib.Plugins;

namespace FlyleafLib.MediaPlayer.SpeechSeparation;

/// <summary>Runs the bundled HTDemucs FT adapter with user-provisioned local dependencies.</summary>
public sealed class HtDemucsSpeechSeparator
{
    private const int UnsupportedSourceExitCode = 3;
    private const int SampleRate = 44_100;
    private const int AsrSampleRate = 16_000;
    internal const int MaxAsrEvidenceSamples = 9 * 60 * AsrSampleRate;
    private const int FrameBytes = 2 * sizeof(float);
    private const double DurationToleranceSeconds = 0.1;
    private static readonly Guid FloatSubFormat = new("00000003-0000-0010-8000-00aa00389b71");
    private static readonly LogHandler Log = new("[Speech separation] ");
    private readonly string python;
    private readonly string checkpoint;
    private readonly string ffmpeg;
    private readonly string ffprobe;
    private readonly string runner;
    private readonly bool cpuAsrEvidence;

    private HtDemucsSpeechSeparator(string python, string checkpoint, string ffmpeg, string ffprobe, string runner,
        bool cpuAsrEvidence)
        => (this.python, this.checkpoint, this.ffmpeg, this.ffprobe, this.runner, this.cpuAsrEvidence) =
            (python, checkpoint, ffmpeg, ffprobe, runner, cpuAsrEvidence);

    public static HtDemucsSpeechSeparator? TryCreate()
    {
        string? python = Environment.GetEnvironmentVariable("LLPLAYER_HTDEMUCS_PYTHON");
        string? checkpoint = Environment.GetEnvironmentVariable("LLPLAYER_HTDEMUCS_CHECKPOINT");
        string? ffmpeg = Environment.GetEnvironmentVariable("LLPLAYER_HTDEMUCS_FFMPEG");
        var separator = TryCreate(python, checkpoint, ffmpeg,
            Path.Combine(AppContext.BaseDirectory, "Assets", "SpeechSeparation", "sidecar.py"));
        if (separator is null && (python is not null || checkpoint is not null || ffmpeg is not null) &&
            Engine.Config is not null && CanWarn)
            Log.Warn("HTDemucs FT is unavailable. Check the Python, checkpoint, FFmpeg and adjacent FFprobe paths and the bundled sidecar.");
        return separator;
    }

    internal static HtDemucsSpeechSeparator? TryCreate(string? python, string? checkpoint, string? ffmpeg, string? runner)
        => TryCreate(python, checkpoint, ffmpeg, runner, cpuAsrEvidence: false);

    // Only explicitly constructed test instances can choose the CPU evidence path.
    internal static HtDemucsSpeechSeparator? TryCreateForCpuAsrEvidence(string? python, string? checkpoint,
        string? ffmpeg, string? runner)
        => TryCreate(python, checkpoint, ffmpeg, runner, cpuAsrEvidence: true);

    private static HtDemucsSpeechSeparator? TryCreate(string? python, string? checkpoint, string? ffmpeg, string? runner,
        bool cpuAsrEvidence)
    {
        if (!IsLocalExecutable(python) || !IsLocalFile(checkpoint) || !IsLocalExecutable(ffmpeg) || !IsLocalFile(runner))
            return null;

        string ffprobe = Path.Combine(Path.GetDirectoryName(ffmpeg)!, OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe");
        return IsLocalExecutable(ffprobe) ? new(python, checkpoint, ffmpeg, ffprobe, runner, cpuAsrEvidence) : null;
    }

    /// <summary>The caller holds GPU admission until this operation and any required process drain have completed.</summary>
    public async Task<SeparatedAudio?> TrySeparateAsync(string mediaPath, int streamIndex, TimeSpan duration, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        ArgumentOutOfRangeException.ThrowIfNegative(streamIndex);
        if (!IsLocalSourceFile(mediaPath) || duration <= TimeSpan.Zero)
        {
            if (Engine.Config is not null && CanWarn)
                Log.Warn("HTDemucs FT requires a local media file with a known duration. Using the original audio.");
            return null;
        }

        var sourceStamp = ReadSourceStamp(mediaPath);
        var output = new SeparatedAudio(Directory.CreateTempSubdirectory("llplayer-separation-"));
        OnlineVideoProcess? process = null;
        bool keepOutput = false;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromHours(2));

        try
        {
            var info = CreateProcessInfo(output.WavePath, "--source", mediaPath,
                "--stream", streamIndex.ToString(CultureInfo.InvariantCulture), "--ffmpeg", ffmpeg, "--ffprobe", ffprobe,
                "--model", checkpoint, "--output", output.WavePath,
                "--duration", duration.TotalSeconds.ToString("R", CultureInfo.InvariantCulture));

            deadline.Token.ThrowIfCancellationRequested();
            process = OnlineVideoProcess.StartEngine(info);
            (int? exit, string detail) = await ReadResultAsync(process, deadline.Token).ConfigureAwait(false);
            deadline.Token.ThrowIfCancellationRequested();
            if (exit == UnsupportedSourceExitCode)
            {
                if (Engine.Config is not null && CanWarn)
                    Log.Warn($"HTDemucs FT could not use this source. Using the original audio.{detail}");
                return null;
            }
            if (exit != 0)
                throw new IOException($"HTDemucs FT failed (exit code {exit?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}).{detail}");

            using Stream wav = File.OpenRead(output.WavePath);
            ValidateWave(wav, duration);
            EnsureSourceUnchanged(mediaPath, sourceStamp);
            deadline.Token.ThrowIfCancellationRequested();
            keepOutput = true;
            if (Engine.Config is not null && CanInfo) Log.Info("HTDemucs FT vocal separation completed.");
            return output;
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested && deadline.IsCancellationRequested)
        {
            throw new TimeoutException("HTDemucs FT exceeded its two-hour time limit.");
        }
        finally
        {
            await DrainProcessAsync(process, output).ConfigureAwait(false);
            if (!keepOutput)
                output.Dispose();
        }
    }

    /// <summary>Checks a bounded exact 16 kHz mono recognition window. The host owns GPU admission until drain.</summary>
    internal async Task<short[]?> TrySeparateAsrEvidenceAsync(ReadOnlyMemory<short> original, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (original.IsEmpty || original.Length > MaxAsrEvidenceSamples || !BitConverter.IsLittleEndian)
            return null;

        SeparatedAudio? staging = null;
        OnlineVideoProcess? process = null;
        short[]? evidence = null;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromMinutes(5));
        try
        {
            var workspace = Directory.CreateTempSubdirectory("llplayer-asr-evidence-");
            staging = new SeparatedAudio(workspace);
            string inputPath = Path.Combine(workspace.FullName, "input.wav");
            string evidencePath = Path.Combine(workspace.FullName, "vocals.s16le");
            using (var input = File.Create(inputPath))
                WriteAsrEvidenceWave(input, original.Span);

            var info = CreateProcessInfo(evidencePath, "--source", inputPath, "--ffmpeg", ffmpeg, "--model", checkpoint,
                "--output", evidencePath, "--asr-pcm-samples", original.Length.ToString(CultureInfo.InvariantCulture));
            if (cpuAsrEvidence)
            {
                info.ArgumentList.Add("--asr-evidence-cpu");
                info.Environment["CUDA_VISIBLE_DEVICES"] = "-1";
            }

            deadline.Token.ThrowIfCancellationRequested();
            process = OnlineVideoProcess.StartEngine(info);
            (int? exit, string detail) = await ReadResultAsync(process, deadline.Token).ConfigureAwait(false);
            deadline.Token.ThrowIfCancellationRequested();
            if (exit != 0)
                throw new IOException($"HTDemucs FT evidence failed (exit code {exit?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}).{detail}");

            using var raw = File.OpenRead(evidencePath);
            short[] pcm = ReadAsrEvidencePcm(raw, original.Length);
            deadline.Token.ThrowIfCancellationRequested();
            evidence = pcm;
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested && deadline.IsCancellationRequested)
        {
            if (Engine.Config is not null && CanWarn) Log.Warn("HTDemucs FT evidence exceeded its five-minute time limit.");
        }
        catch (Exception error) when (error is not EngineProcessDrainException &&
            error is (IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or
                InvalidOperationException or NotSupportedException or TimeoutException or System.ComponentModel.Win32Exception))
        {
            if (Engine.Config is not null && CanWarn) Log.Warn($"HTDemucs FT evidence is unavailable: {error.Message}");
        }
        finally
        {
            if (staging is not null)
            {
                await DrainProcessAsync(process, staging).ConfigureAwait(false);
                staging.Dispose();
            }
        }
        token.ThrowIfCancellationRequested();
        return evidence;
    }

    internal static void WriteAsrEvidenceWave(Stream stream, ReadOnlySpan<short> pcm)
    {
        int bytes = pcm.Length * sizeof(short);
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write("RIFF"u8);
        writer.Write(36 + bytes);
        writer.Write("WAVEfmt "u8);
        writer.Write(16);
        writer.Write((ushort)1); // PCM
        writer.Write((ushort)1); // Mono
        writer.Write(AsrSampleRate);
        writer.Write(AsrSampleRate * sizeof(short));
        writer.Write((ushort)sizeof(short));
        writer.Write((ushort)16);
        writer.Write("data"u8);
        writer.Write(bytes);
        writer.Write(MemoryMarshal.AsBytes(pcm));
    }

    internal static short[] ReadAsrEvidencePcm(Stream stream, int samples)
    {
        if (stream.Length - stream.Position != samples * sizeof(short))
            throw new InvalidDataException("HTDemucs FT evidence does not cover the original PCM exactly.");
        short[] pcm = new short[samples];
        stream.ReadExactly(MemoryMarshal.AsBytes(pcm.AsSpan()));
        return pcm;
    }

    private ProcessStartInfo CreateProcessInfo(string outputPath, params string[] arguments)
    {
        var info = new ProcessStartInfo(python)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(outputPath)!
        };
        foreach (string argument in new[] { "-I", "-B", runner })
            info.ArgumentList.Add(argument);
        foreach (string argument in arguments)
            info.ArgumentList.Add(argument);
        return info;
    }

    private static async ValueTask DrainProcessAsync(OnlineVideoProcess? process, SeparatedAudio staging)
    {
        if (process is null)
            return;
        try { await process.DisposeEngineAsync().ConfigureAwait(false); }
        catch (Exception stopFailure)
        {
            throw new EngineProcessDrainException("Could not confirm that HTDemucs FT stopped. Retry shutdown.",
                stopFailure, async () =>
                {
                    await process.DisposeEngineAsync().ConfigureAwait(false);
                    staging.Dispose();
                });
        }
    }

    private static async Task<(int? Exit, string Detail)> ReadResultAsync(OnlineVideoProcess process, CancellationToken token)
    {
        string detail = string.Empty;
        int? exit = null;
        await foreach (CommandEvent item in process.ReadEngineEventsAsync(token).ConfigureAwait(false))
            switch (item)
            {
                case StandardErrorCommandEvent stderr when !string.IsNullOrWhiteSpace(stderr.Text):
                    detail = " " + stderr.Text[..Math.Min(512, stderr.Text.Length)];
                    break;
                case ExitedCommandEvent exited:
                    exit = exited.ExitCode;
                    break;
            }
        return (exit, detail);
    }

    private static bool IsLocalExecutable([NotNullWhen(true)] string? path) =>
        IsLocalFile(path) && OnlineVideoProcess.IsExecutablePath(path);

    private static bool IsLocalFile([NotNullWhen(true)] string? path) =>
        !string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path) &&
        (!OperatingSystem.IsWindows() || !path.StartsWith(@"\\", StringComparison.Ordinal)) && File.Exists(path);

    internal static bool IsLocalSourceFile(string path)
    {
        if (!IsLocalFile(path))
            return false;
        if (!OperatingSystem.IsWindows())
            return true;

        try
        {
            var file = new FileInfo(path);
            if (new DriveInfo(Path.GetPathRoot(file.FullName)!).DriveType == DriveType.Network ||
                (file.Attributes & FileAttributes.ReparsePoint) != 0)
                return false;
            for (DirectoryInfo? parent = file.Directory; parent is not null; parent = parent.Parent)
                if ((parent.Attributes & FileAttributes.ReparsePoint) != 0)
                    return false;
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    internal static (long Length, DateTime LastWriteTimeUtc) ReadSourceStamp(string path)
    {
        var file = new FileInfo(path);
        return (file.Length, file.LastWriteTimeUtc);
    }

    internal static void EnsureSourceUnchanged(string path, (long Length, DateTime LastWriteTimeUtc) expected)
    {
        const string message = "The selected media file changed during HTDemucs FT separation. Run recognition again.";
        try
        {
            if (IsLocalSourceFile(path) && ReadSourceStamp(path) == expected)
                return;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw new IOException(message, error);
        }
        throw new IOException(message);
    }

    internal static void ValidateWave(Stream stream, TimeSpan? duration = null)
    {
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        if (stream.Length < 12 || !reader.ReadBytes(4).AsSpan().SequenceEqual("RIFF"u8) ||
            reader.ReadUInt32() != stream.Length - 8 || !reader.ReadBytes(4).AsSpan().SequenceEqual("WAVE"u8))
            throw InvalidWave();

        bool validFormat = false;
        while (stream.Length - stream.Position >= 8)
        {
            byte[] chunk = reader.ReadBytes(4);
            uint size = reader.ReadUInt32();
            long next = stream.Position + size + (size & 1);
            if (next > stream.Length)
                throw InvalidWave();
            if (chunk.AsSpan().SequenceEqual("fmt "u8))
                validFormat = IsFloatStereo(reader, size);
            else if (chunk.AsSpan().SequenceEqual("data"u8))
            {
                if (!validFormat || size == 0 || size % FrameBytes != 0)
                    throw InvalidWave();
                if (duration is { } expected && Math.Abs(size / (double)(SampleRate * FrameBytes) - expected.TotalSeconds)
                    > DurationToleranceSeconds + 1d / SampleRate)
                    throw new InvalidDataException("HTDemucs FT returned a vocal track whose duration differs from the source.");
                return;
            }
            stream.Position = next;
        }
        throw InvalidWave();
    }

    private static bool IsFloatStereo(BinaryReader reader, uint size)
    {
        if (size < 16)
            return false;
        ushort format = reader.ReadUInt16();
        if (reader.ReadUInt16() != 2 || reader.ReadUInt32() != SampleRate ||
            reader.ReadUInt32() != SampleRate * FrameBytes || reader.ReadUInt16() != FrameBytes || reader.ReadUInt16() != 32)
            return false;
        if (format == 3)
            return true;
        if (format != 0xfffe || size < 40 || reader.ReadUInt16() < 22 || reader.ReadUInt16() != 32)
            return false;
        _ = reader.ReadUInt32(); // channel mask
        return new Guid(reader.ReadBytes(16)) == FloatSubFormat;
    }

    private static InvalidDataException InvalidWave() =>
        new("HTDemucs FT did not produce a valid 44.1 kHz stereo float WAV.");
}

/// <summary>Owns a vocal track and its private staging directory until the consumer finishes reading.</summary>
public sealed class SeparatedAudio : IDisposable
{
    private static readonly LogHandler Log = new("[Speech separation] ");
    private DirectoryInfo? directory;

    internal SeparatedAudio(DirectoryInfo directory)
    {
        this.directory = directory;
        WavePath = Path.Combine(directory.FullName, "vocals.wav");
    }

    public string WavePath { get; }

    public void Dispose()
    {
        DirectoryInfo? owned = Interlocked.Exchange(ref directory, null);
        if (owned is null)
            return;
        // Only the fresh, process-owned directory can be deleted, including a canceled runner's partial files.
        try { owned.Delete(recursive: true); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            if (Engine.Config is not null && CanWarn)
                Log.Warn($"Could not clean up the HTDemucs FT staging directory: {error.Message}");
        }
    }
}
