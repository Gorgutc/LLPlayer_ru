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
            string python = CreateExecutable("python");
            string ffmpeg = CreateExecutable("ffmpeg");
            string ffprobe = CreateExecutable("ffprobe");
            string checkpoint = CreateFile("vocal.th");
            string runner = CreateFile("sidecar.py");

            Assert.NotNull(HtDemucsSpeechSeparator.TryCreate(python, checkpoint, ffmpeg, runner));
            Assert.Null(HtDemucsSpeechSeparator.TryCreate("python", checkpoint, ffmpeg, runner));
            Assert.Null(HtDemucsSpeechSeparator.TryCreate(python, "vocal.th", ffmpeg, runner));
            Assert.Null(HtDemucsSpeechSeparator.TryCreate(python, checkpoint, null, runner));
            Assert.Null(HtDemucsSpeechSeparator.TryCreate(python, checkpoint, ffmpeg, Path.Combine(workspace.FullName, "missing.py")));
            File.Delete(ffprobe);
            Assert.Null(HtDemucsSpeechSeparator.TryCreate(python, checkpoint, ffmpeg, runner));

            string CreateFile(string name)
            {
                string path = Path.Combine(workspace.FullName, name);
                File.WriteAllText(path, "fixture");
                return path;
            }

            string CreateExecutable(string name)
            {
                string path = CreateFile(OperatingSystem.IsWindows() ? name + ".exe" : name);
                if (!OperatingSystem.IsWindows())
                    File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserExecute);
                return path;
            }
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
