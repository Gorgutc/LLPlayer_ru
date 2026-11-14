#nullable enable

namespace FlyleafLib.MediaPlayer.Diarization;

/// <summary>A local media file and its selected absolute FFmpeg audio stream index.</summary>
public sealed record DiarizationAudioSource(string Path, int StreamIndex);

/// <summary>A speaker interval measured from the start of the media container.</summary>
public sealed record DiarizationTurn(TimeSpan Start, TimeSpan End, string SpeakerId);

public interface IDiarizationEngine
{
    Task<IReadOnlyList<DiarizationTurn>> RunAsync(DiarizationAudioSource source, CancellationToken token);
}
