using System.Text;
using FlyleafLib.MediaPlayer.SpeechSeparation;

namespace FlyleafLib.MediaPlayer;

public sealed class HtDemucsSpeechSeparatorTests
{
    [Fact]
    public void Runtime_probe_requires_existing_absolute_files()
    {
        DirectoryInfo workspace = Directory.CreateTempSubdirectory("llplayer-test-separation-");
        try
        {
            string python = CreateFixture(workspace, "python", executable: true);
            string ffmpeg = CreateFixture(workspace, "ffmpeg", executable: true);
            string ffprobe = CreateFixture(workspace, "ffprobe", executable: true);
            string checkpoint = CreateFixture(workspace, "vocal.th");
            string runner = CreateFixture(workspace, "sidecar.py");

            Assert.NotNull(HtDemucsSpeechSeparator.TryCreate(python, checkpoint, ffmpeg, runner));
            Assert.Null(HtDemucsSpeechSeparator.TryCreate("python", checkpoint, ffmpeg, runner));
            Assert.Null(HtDemucsSpeechSeparator.TryCreate(python, "vocal.th", ffmpeg, runner));
            Assert.Null(HtDemucsSpeechSeparator.TryCreate(python, checkpoint, null, runner));
            Assert.Null(HtDemucsSpeechSeparator.TryCreate(python, checkpoint, ffmpeg, Path.Combine(workspace.FullName, "missing.py")));
            File.Delete(ffprobe);
            Assert.Null(HtDemucsSpeechSeparator.TryCreate(python, checkpoint, ffmpeg, runner));
        }
        finally { workspace.Delete(true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Valid_float_stereo_wave_is_accepted(bool extensible)
    {
        using MemoryStream wave = CreateWave(extensible);
        HtDemucsSpeechSeparator.ValidateWave(wave);
    }

    [Theory]
    [InlineData(1, 2, 44100, 8)]
    [InlineData(3, 1, 44100, 8)]
    [InlineData(3, 2, 48000, 8)]
    [InlineData(3, 2, 44100, 0)]
    [InlineData(3, 2, 44100, 9)]
    public void Unsupported_format_or_sample_count_is_rejected(int format, int channels, int rate, int dataBytes)
    {
        using MemoryStream wave = CreateWave(format: format, channels: channels, rate: rate, dataBytes: dataBytes);
        Assert.Throws<InvalidDataException>(() => HtDemucsSpeechSeparator.ValidateWave(wave));
    }

    [Fact]
    public void Declared_audio_cannot_extend_past_file()
    {
        using MemoryStream wave = CreateWave();
        wave.Position = wave.Length - 12;
        using var writer = new BinaryWriter(wave, Encoding.UTF8, leaveOpen: true);
        writer.Write(16);
        wave.Position = 0;

        Assert.Throws<InvalidDataException>(() => HtDemucsSpeechSeparator.ValidateWave(wave));
    }

    [Fact]
    public void Full_length_vocal_track_is_required()
    {
        using MemoryStream wav = CreateWave(dataBytes: 44_100 * 8);
        HtDemucsSpeechSeparator.ValidateWave(wav, TimeSpan.FromSeconds(1));
        wav.Position = 0;
        Assert.Throws<InvalidDataException>(() => HtDemucsSpeechSeparator.ValidateWave(wav, TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public void Asr_evidence_wave_preserves_exact_PCM16_mono_sample_clock()
    {
        short[] pcm = [short.MinValue, -1, 0, 1, short.MaxValue];
        using MemoryStream stream = new();
        HtDemucsSpeechSeparator.WriteAsrEvidenceWave(stream, pcm);

        using BinaryReader reader = new(stream);
        stream.Position = 0;
        Assert.Equal("RIFF"u8.ToArray(), reader.ReadBytes(4));
        Assert.Equal(36 + pcm.Length * sizeof(short), reader.ReadInt32());
        Assert.Equal("WAVEfmt "u8.ToArray(), reader.ReadBytes(8));
        Assert.Equal(16, reader.ReadInt32());
        Assert.Equal((ushort)1, reader.ReadUInt16()); // PCM
        Assert.Equal((ushort)1, reader.ReadUInt16()); // mono
        Assert.Equal(16_000, reader.ReadInt32());
        Assert.Equal(32_000, reader.ReadInt32());
        Assert.Equal((ushort)2, reader.ReadUInt16());
        Assert.Equal((ushort)16, reader.ReadUInt16());
        Assert.Equal("data"u8.ToArray(), reader.ReadBytes(4));
        Assert.Equal(pcm.Length * sizeof(short), reader.ReadInt32());
        Assert.Equal(pcm, HtDemucsSpeechSeparator.ReadAsrEvidencePcm(stream, pcm.Length));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(6)]
    public void Asr_evidence_rejects_any_returned_sample_count_other_than_requested(int returnedBytes)
    {
        using MemoryStream stream = new(new byte[returnedBytes]);
        Assert.Throws<InvalidDataException>(() => HtDemucsSpeechSeparator.ReadAsrEvidencePcm(stream, 2));
    }

    [Fact]
    public async Task Asr_evidence_checks_user_cancellation_and_rejects_unsupported_input_without_running_a_model()
    {
        DirectoryInfo workspace = Directory.CreateTempSubdirectory("llplayer-test-separation-");
        try
        {
            CreateFixture(workspace, "ffprobe", executable: true);
            var separator = HtDemucsSpeechSeparator.TryCreateForCpuAsrEvidence(
                CreateFixture(workspace, "python", executable: true),
                CreateFixture(workspace, "vocal.th"),
                CreateFixture(workspace, "ffmpeg", executable: true),
                CreateFixture(workspace, "sidecar.py"));
            Assert.NotNull(separator);
            using var canceled = new CancellationTokenSource();
            await canceled.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                separator.TrySeparateAsrEvidenceAsync(ReadOnlyMemory<short>.Empty, canceled.Token));

            CancellationToken token = TestContext.Current.CancellationToken;
            Assert.Null(await separator.TrySeparateAsrEvidenceAsync(ReadOnlyMemory<short>.Empty, token));
            Assert.Null(await separator.TrySeparateAsrEvidenceAsync(
                new short[HtDemucsSpeechSeparator.MaxAsrEvidenceSamples + 1], token));
            // The local fixtures pass availability checks but are not real executables.
            Assert.Null(await separator.TrySeparateAsrEvidenceAsync(new short[] { 1, 2 }, token));
        }
        finally { workspace.Delete(true); }
    }

    [Fact]
    public void A_replaced_or_edited_source_cannot_use_the_stem()
    {
        DirectoryInfo workspace = Directory.CreateTempSubdirectory("llplayer-test-separation-");
        try
        {
            string input = Path.Combine(workspace.FullName, "fixture.aif");
            File.WriteAllText(input, "input");
            var original = HtDemucsSpeechSeparator.ReadSourceStamp(input);
            HtDemucsSpeechSeparator.EnsureSourceUnchanged(input, original);

            File.WriteAllText(input, "input changed");
            File.SetLastWriteTimeUtc(input, original.LastWriteTimeUtc);
            Assert.Throws<IOException>(() => HtDemucsSpeechSeparator.EnsureSourceUnchanged(input, original));

            File.WriteAllText(input, "input");
            File.SetLastWriteTimeUtc(input, original.LastWriteTimeUtc.AddMinutes(1));
            Assert.Throws<IOException>(() => HtDemucsSpeechSeparator.EnsureSourceUnchanged(input, original));
        }
        finally { workspace.Delete(true); }
    }

    private static string CreateFixture(DirectoryInfo workspace, string name, bool executable = false)
    {
        string path = Path.Combine(workspace.FullName, executable && OperatingSystem.IsWindows() ? name + ".exe" : name);
        File.WriteAllText(path, "fixture");
        if (executable && !OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        return path;
    }

    private static MemoryStream CreateWave(bool extensible = false, int format = 3, int channels = 2,
        int rate = 44_100, int dataBytes = 8)
    {
        int formatBytes = extensible ? 40 : 16;
        var wave = new MemoryStream();
        using var writer = new BinaryWriter(wave, Encoding.UTF8, leaveOpen: true);
        writer.Write("RIFF"u8);
        writer.Write(4 + 8 + formatBytes + 8 + dataBytes + (dataBytes & 1));
        writer.Write("WAVEfmt "u8);
        writer.Write(formatBytes);
        writer.Write(extensible ? (ushort)0xfffe : (ushort)format);
        writer.Write((ushort)channels);
        writer.Write(rate);
        writer.Write(rate * channels * sizeof(float));
        writer.Write((ushort)(channels * sizeof(float)));
        writer.Write((ushort)32);
        if (extensible)
        {
            writer.Write((ushort)22);
            writer.Write((ushort)32);
            writer.Write(3);
            writer.Write(new Guid("00000003-0000-0010-8000-00aa00389b71").ToByteArray());
        }
        writer.Write("data"u8);
        writer.Write(dataBytes);
        writer.Write(new byte[dataBytes + (dataBytes & 1)]);
        wave.Position = 0;
        return wave;
    }
}
