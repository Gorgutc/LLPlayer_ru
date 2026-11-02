using FlyleafLib.MediaPlayer;
using FlyleafLib;
using FlyleafLib.MediaFramework.MediaStream;

namespace LLPlayer.Avalonia.Services;

public sealed partial class FlyleafPlaybackController
{
    readonly object downloadedSubtitleGate = new();
    readonly List<OwnedSubtitleFile> downloadedSubtitleFiles = [];
    const int MaxRetainedSubtitleDownloads = 16;

    /// <summary>Captures this explicit action's media before any download. Successful loads keep their file alive.</summary>
    public SubtitlePrimaryLoadTarget CaptureSubtitlePrimaryLoadTarget()
    {
        if (disposed || player.IsDisposed) throw new InvalidOperationException("The player is closed.");
        if (Recognition.IsBusy || player.IsMediaResetting || player.Status == Status.Opening
            || Volatile.Read(ref cancelableAppEpoch) != 0)
            throw new InvalidOperationException("Wait for media opening and subtitle work to finish before loading subtitles.");
        lock (downloadedSubtitleGate)
            if (downloadedSubtitleFiles.Count >= MaxRetainedSubtitleDownloads)
                throw new InvalidOperationException("The player is retaining 16 loaded subtitle files. Restart the player before loading more temporary subtitles, or use Save selected subtitle.");
        PlaybackChoiceMedia? media = CapturePlaybackChoiceMedia();
        if (media == null || !player.Video.IsOpened)
            throw new InvalidOperationException("Open a video before loading downloaded subtitles.");
        return async (subtitle, language, cancellationToken) =>
        {
            if (media == null || !IsCurrentPlaybackMedia(media)) return false;
            bool opened = false;
            await MutateWithTranslationAsync(cancellation =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                player.TryMutateMedia(media.Generation, media.Item,
                    () => !cancellationToken.IsCancellationRequested && IsCurrentPlaybackMedia(media), () =>
                    {
                        int previous = SubtitlesSelectedHelper.CurIndex;
                        SelectSubMethod previousPrimaryMethod = SubtitlesSelectedHelper.PrimaryMethod;
                        try
                        {
                            var plugin = player.decoder.Plugins.Values.OfType<FlyleafLib.Plugins.OpenSubtitles>().FirstOrDefault()
                                ?? throw new InvalidOperationException("The local subtitle loader is unavailable.");
                            ExternalSubtitlesStream stream = CreateDownloadedSubtitleStream(subtitle, language);
                            // The decoder and its asynchronous cache reader can both retain this file after Open.
                            // Adopt before entering native code, including a partially failed native open.
                            lock (downloadedSubtitleGate)
                            {
                                if (downloadedSubtitleFiles.Count >= MaxRetainedSubtitleDownloads)
                                    throw new InvalidOperationException("Too many temporary subtitle loads are retained. Save this subtitle instead.");
                                downloadedSubtitleFiles.Add(subtitle);
                                subtitle.RetainedByPlayer = true;
                            }
                            plugin.AddExternalStream(stream, item: media.Item);
                            SubtitlesSelectedHelper.CurIndex = 0;
                            SubtitlesSelectedHelper.PrimaryMethod = SelectSubMethod.Original;
                            var result = player.Open(stream);
                            if (result?.Success != true) throw new IOException(result?.Error ?? "The downloaded subtitle could not be loaded.");
                            opened = true;
                        }
                        finally
                        {
                            SubtitlesSelectedHelper.CurIndex = previous;
                            if (!opened) SubtitlesSelectedHelper.PrimaryMethod = previousPrimaryMethod;
                        }
                    }, cancellation);
            }, requireIdle: true).ConfigureAwait(false);
            return opened;
        };
    }

    internal static ExternalSubtitlesStream CreateDownloadedSubtitleStream(OwnedSubtitleFile subtitle, string language)
    {
        Language source = Language.Get(language);
        if (source == Language.Unknown && language.Contains('-')) source = Language.Get(language.Split('-')[0]);
        return new ExternalSubtitlesStream
        {
            Url = subtitle.FilePath, Title = Path.GetFileName(subtitle.FilePath), Language = source,
            Downloaded = true, ManualDownloaded = true, LanguageDetected = false, IsBitmap = false,
        };
    }

    /// <summary>
    /// Call after full Player.Dispose, when background readers and native demuxer file handles have drained.
    /// Returns owned-path failures for logging; failed children remain registered for a later explicit retry.
    /// </summary>
    public IReadOnlyList<string> CleanupDownloadedSubtitleFiles()
    {
        if (!player.IsDisposed) return ["Temporary subtitle cleanup is deferred until the native player is closed."];
        List<string> failures = [];
        lock (downloadedSubtitleGate)
            for (int i = downloadedSubtitleFiles.Count - 1; i >= 0; i--)
            {
                if (downloadedSubtitleFiles[i].TryDelete(out string? error)) downloadedSubtitleFiles.RemoveAt(i);
                else if (error != null) failures.Add(error);
            }
        return failures;
    }
    /// <summary>Copies only a currently owned row, serializing with OCR/media mutation; no native pointers escape.</summary>
    public async Task<BgraBitmap?> GetCueBitmapAsync(CueInfo cue, int slot, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested || !cue.IsBitmap || !string.IsNullOrEmpty(cue.OriginalText)
            || !IsCueCurrent(cue, slot) || cue.Identity?.Source is not SubtitleCueToken token) return null;
        BgraBitmap? result = null;
        await Recognition.MutateAsync(cancellation =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            player.TryMutateMedia(token.Source.Media.Generation, token.Source.Media.Item,
                () => !cancellationToken.IsCancellationRequested && IsCueCurrent(cue, slot), () =>
                {
                    SubtitleBitmapData? native = token.Cue.Bitmap;
                    if (!string.IsNullOrEmpty(token.Cue.Text) || native == null) return;
                    if (native.TryCopyBitmap(2_097_152, cancellationToken, out byte[] bytes, out int width, out int height)
                        && !cancellationToken.IsCancellationRequested && IsCueCurrent(cue, slot)
                        && ReferenceEquals(native, token.Cue.Bitmap) && string.IsNullOrEmpty(token.Cue.Text))
                        result = new BgraBitmap(bytes, width, height);
                }, cancellation);
        }).ConfigureAwait(false);
        return !cancellationToken.IsCancellationRequested && IsCueCurrent(cue, slot) ? result : null;
    }

}
