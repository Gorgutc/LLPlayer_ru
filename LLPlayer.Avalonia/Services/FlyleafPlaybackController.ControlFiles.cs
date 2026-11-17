using LLPlayer.ControlApi;

namespace LLPlayer.Avalonia.Services;

public sealed partial class FlyleafPlaybackController
{
    sealed record ControlRecordingOwner(string Path, string TemporaryPath, bool Overwrite);

    ControlRecordingOwner? controlRecording;
    string? publishedControlRecording;

    internal ControlRecordingSnapshot ControlRecording
    {
        get
        {
            var owner = Volatile.Read(ref controlRecording);
            string? published = Volatile.Read(ref publishedControlRecording);
            return new(player.IsRecording, owner?.Path ?? published, owner == null && !player.IsRecording && published != null);
        }
    }

    internal async Task<ControlRecordingSnapshot> StartControlRecordingAsync(string path, bool overwrite, CancellationToken token)
    {
        string staged = ControlOutputStage(path, overwrite);
        PlaybackChoiceMedia media = CapturePlaybackChoiceMedia() ?? throw StaleControlMedia();
        await Recognition.MutateAsync(cancellation =>
        {
            using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(token, cancellation);
            if (!player.TryMutateMedia(media.Generation, media.Item, () => IsCurrentPlaybackMedia(media), () =>
            {
                if (controlRecording != null || player.IsRecording)
                    throw new InvalidOperationException("Finish the current recording before starting a new one.");
                string actualPath = staged;
                try
                {
                    player.StartRecording(ref actualPath, useRecommendedExtension: false);
                    if (!player.IsRecording) throw new InvalidOperationException("The video engine could not start the recording.");
                    Volatile.Write(ref controlRecording, new(path, actualPath, overwrite));
                    Volatile.Write(ref publishedControlRecording, null);
                }
                catch
                {
                    if (!player.IsRecording) File.Delete(actualPath);
                    throw;
                }
            }, linked.Token)) throw StaleControlMedia();
        }).ConfigureAwait(false);
        return ControlRecording;
    }

    internal async Task<ControlRecordingSnapshot> StopControlRecordingAsync(CancellationToken token)
    {
        await Recognition.MutateAsync(cancellation =>
        {
            token.ThrowIfCancellationRequested();
            cancellation.ThrowIfCancellationRequested();
            CompleteControlRecordingCore(stopManualRecording: true);
        }).ConfigureAwait(false);
        return ControlRecording;
    }

    internal void FinishOwnedControlRecordingBeforeClose()
        => CompleteControlRecordingCore(stopManualRecording: false);

    void CompleteControlRecordingCore(bool stopManualRecording)
    {
        ControlRecordingOwner? owner = Volatile.Read(ref controlRecording);
        if (owner == null && !stopManualRecording) return;
        if (player.IsRecording) player.StopRecording();
        if (player.IsRecording) throw new InvalidOperationException("The player could not confirm that recording stopped.");
        if (owner == null) return;
        if (!File.Exists(owner.TemporaryPath) || new FileInfo(owner.TemporaryPath).Length == 0)
            throw new InvalidOperationException("The video engine did not produce a recording.");
        File.Move(owner.TemporaryPath, owner.Path, owner.Overwrite);
        Volatile.Write(ref publishedControlRecording, owner.Path);
        Volatile.Write(ref controlRecording, null);
    }

    internal async Task<ControlSnapshotResult> TakeControlSnapshotAsync(string path, bool overwrite,
        uint width, uint height, CancellationToken token)
    {
        if (width > 8192 || height > 8192) throw new ArgumentOutOfRangeException(nameof(width), "Use dimensions up to 8192 pixels.");
        string staged = ControlOutputStage(path, overwrite);
        PlaybackChoiceMedia media = CapturePlaybackChoiceMedia() ?? throw StaleControlMedia();
        long bytes = 0;
        try
        {
            await Recognition.MutateAsync(cancellation =>
            {
                using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(token, cancellation);
                if (!player.TryMutateMedia(media.Generation, media.Item, () => IsCurrentPlaybackMedia(media), () =>
                {
                    if (!player.Video.IsOpened) throw new InvalidOperationException("No video frame is available.");
                    player.TakeSnapshotToFile(staged, width, height);
                    bytes = File.Exists(staged) ? new FileInfo(staged).Length : 0;
                    if (bytes == 0) throw new InvalidOperationException("The video engine did not produce a snapshot.");
                    linked.Token.ThrowIfCancellationRequested();
                    File.Move(staged, path, overwrite);
                }, linked.Token)) throw StaleControlMedia();
            }).ConfigureAwait(false);
            return new(path, bytes);
        }
        finally { File.Delete(staged); }
    }

    static string ControlOutputStage(string path, bool overwrite)
    {
        if (File.Exists(path) && !overwrite) throw new ControlException(new("output_exists", "The requested output file already exists."));
        string extension = Path.GetExtension(path);
        if (extension.Length == 0) throw new ArgumentException("The output path must include a file extension.", nameof(path));
        string directory = Path.GetDirectoryName(path) ?? throw new ArgumentException("The output path needs a directory.", nameof(path));
        if (!Directory.Exists(directory)) throw new DirectoryNotFoundException("The requested output directory does not exist.");
        return Path.Combine(directory, $".llplayer-{Guid.NewGuid():N}{extension}");
    }
}
