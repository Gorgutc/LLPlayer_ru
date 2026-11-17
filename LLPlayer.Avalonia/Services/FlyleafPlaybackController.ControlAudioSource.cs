using System.Security.Cryptography;
using FlyleafLib;
using FlyleafLib.MediaFramework.MediaDemuxer;
using FlyleafLib.MediaFramework.MediaPlaylist;
using FlyleafLib.MediaPlayer.Diarization;
using LLPlayer.ControlApi;

namespace LLPlayer.Avalonia.Services;

public sealed partial class FlyleafPlaybackController
{
    // URL and authenticated demux options are held only by the GUI. The host receives the resulting WAV.
    sealed class ControlNetworkAudio(Config configuration, MediaType demuxerType, string url, string? sourceLanguage)
    {
        public string? SourceLanguage { get; } = sourceLanguage;

        public TimeSpan WriteWave(int streamIndex, Stream wav, CancellationToken cancellationToken)
            => DiarizationAudioExtractor.WriteNetworkWave(configuration, demuxerType, url, streamIndex, wav,
                cancellationToken);
    }

    sealed record ControlPreparedAudio(string Path, int StreamIndex, string? SourceRevision, bool IsSnapshot,
        string? SourceLanguage = null);

    ControlNetworkAudio CaptureControlNetworkAudio(PlaybackChoiceMedia media, PlaybackAudioDelaySource audio)
    {
        if (audio.NativeUrl is not { } url || !Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)
            || uri.Scheme is not ("http" or "https"))
            throw new ControlException(new("unsupported_source", "Recognition needs a local audio file or a finite HTTP or HTTPS source."));
        if (audio.Source.Demuxer.Duration <= 0)
            throw new ControlException(new("source_duration_unknown", "The selected HTTP audio source does not report a finite duration."));
        MediaType demuxerType = audio.Source.Demuxer.Type;
        Config config = player.Config.Clone();
        if (player.Playlist.InputType == InputType.Web)
        {
            DemuxerInput input = (DemuxerInput?)audio.External ?? media.Item;
            input.ApplyHttpFormatOptions(config.Demuxer.GetFormatOptPtr(demuxerType));
        }
        string? language = audio.Source.Language?.ISO6391;
        return new(config, demuxerType, url, language is { Length: 2 } ? language : null);
    }

    async Task<ControlPreparedAudio> PrepareControlAudioAsync(ControlRecognitionInput input, AppPaths paths,
        Guid requestId, CancellationToken cancellationToken)
    {
        if (input.NetworkAudio is not { } network)
            return new(input.Path!, input.StreamIndex, null, false);

        string statePath = ControlPaths.ResolveDirectory(paths.StateDir);
        string snapshotDirectory = Path.Combine(statePath, "Automation", "Inputs");
        StringComparison pathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!string.Equals(snapshotDirectory, ControlPaths.ResolveDirectory(snapshotDirectory), pathComparison))
            throw new InvalidOperationException("The automation audio snapshot directory cannot be redirected.");
        if (OperatingSystem.IsWindows())
            Directory.CreateDirectory(snapshotDirectory);
        else
            Directory.CreateDirectory(snapshotDirectory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        if (!string.Equals(snapshotDirectory, ControlPaths.ResolveDirectory(snapshotDirectory), pathComparison))
            throw new InvalidOperationException("The automation audio snapshot directory was replaced.");

        string snapshot = Path.Combine(snapshotDirectory, $"gui-asr-{requestId:N}.wav");
        string staging = snapshot + ".part";
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.ReadWrite };
        if (!OperatingSystem.IsWindows())
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

        bool stagingCreated = false;
        try
        {
            string revision;
            using (var wav = new FileStream(staging, options))
            {
                stagingCreated = true;
                await Task.Run(() =>
                {
                    network.WriteWave(input.StreamIndex, wav, cancellationToken);
                    wav.Flush(flushToDisk: true);
                    wav.Position = 0;
                }, cancellationToken);
                revision = Convert.ToHexString(await SHA256.HashDataAsync(wav, cancellationToken));
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (!OwnsControlRecognitionAudio(input))
                throw new InvalidOperationException("The selected audio source changed during capture.");

            File.Move(staging, snapshot);
            stagingCreated = false;
            return new(snapshot, 0, revision, true, network.SourceLanguage);
        }
        finally
        {
            if (stagingCreated)
                DeleteUnsubmittedControlAudio(staging);
        }
    }

    static void DeleteUnsubmittedControlAudio(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
