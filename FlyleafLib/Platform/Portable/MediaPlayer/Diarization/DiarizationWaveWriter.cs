#nullable enable

namespace FlyleafLib.MediaPlayer.Diarization;

/// <summary>Streams 16 kHz mono PCM at explicit media sample positions into a seekable RIFF WAV.</summary>
internal sealed class DiarizationWaveWriter(Stream output)
{
    public const int SampleRate = 16_000;
    private const int BytesPerSample = sizeof(short);
    private const int HeaderSize = 44;
    private const long MaxSamples = (uint.MaxValue - (HeaderSize - 8)) / BytesPerSample;
    private static readonly byte[] Silence = new byte[8192];
    private long _writtenSamples;

    public void WriteAt(long startSample, ReadOnlySpan<byte> pcm, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (pcm.IsEmpty)
            return;

        long endSample = checked(startSample + pcm.Length / BytesPerSample);
        if (endSample > MaxSamples)
            throw new InvalidDataException("The selected audio timeline exceeds the maximum size of a PCM WAV file.");
        if (endSample <= _writtenSamples)
            return;

        // A gap is silence. Negative samples and overlapping samples already on the media clock are trimmed.
        output.Position = HeaderSize + _writtenSamples * BytesPerSample;
        long gapBytes = Math.Max(0, startSample - _writtenSamples) * BytesPerSample;
        while (gapBytes > 0)
        {
            token.ThrowIfCancellationRequested();
            int length = (int)Math.Min(Silence.Length, gapBytes);
            output.Write(Silence.AsSpan(0, length));
            gapBytes -= length;
        }

        int trimBytes = (int)Math.Max(0, _writtenSamples - startSample) * BytesPerSample;
        output.Write(pcm[trimBytes..]);
        _writtenSamples = endSample;
    }

    public TimeSpan Finish()
    {
        if (_writtenSamples == 0)
            throw new InvalidDataException("The selected audio stream contains no audio on the media timeline.");

        uint dataSize = (uint)(_writtenSamples * BytesPerSample);
        output.Position = 0;
        using var writer = new BinaryWriter(output, Encoding.ASCII, leaveOpen: true);
        writer.Write("RIFF"u8);
        writer.Write(dataSize + 36);
        writer.Write("WAVEfmt "u8);
        writer.Write(16);
        writer.Write((short)1); // PCM
        writer.Write((short)1);
        writer.Write(SampleRate);
        writer.Write(SampleRate * BytesPerSample);
        writer.Write((short)BytesPerSample);
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(dataSize);
        return TimeSpan.FromTicks(_writtenSamples * (TimeSpan.TicksPerSecond / SampleRate));
    }
}
