using FlyleafLib.MediaFramework.MediaDemuxer;
using FlyleafLib.MediaFramework.MediaPlaylist;
using FlyleafLib.MediaFramework.MediaStream;

namespace FlyleafLib.MediaPlayer;

#nullable enable

public unsafe partial class Player
{
    public sealed class SrtAlignmentTarget
    {
        internal SrtAlignmentTarget(PlaylistItem item, long mediaGeneration, AudioStream audio, long audioGeneration,
            SubtitlesStream subtitles, int slot, SubManager manager, SrtTimingSource source, LocalMediaFileRevision revision, long duration)
        {
            Item = item;
            MediaGeneration = mediaGeneration;
            Audio = audio;
            AudioGeneration = audioGeneration;
            Subtitles = subtitles;
            Slot = slot;
            Manager = manager;
            Source = source;
            MediaRevision = revision;
            Duration = TimeSpan.FromTicks(duration);
            StreamIndex = audio.StreamIndex;
            string description = audio.Title ?? audio.Language?.TopEnglishName ?? "";
            AudioLabel = string.IsNullOrWhiteSpace(description) ? $"#{StreamIndex}" : $"#{StreamIndex} — {description}";
            string? language = audio.Language?.ISO6391;
            Language = language != null && WhisperLanguage.LanguageToCode.ContainsValue(language) ? language : "en";
        }

        internal PlaylistItem Item { get; }
        internal long MediaGeneration { get; }
        internal AudioStream Audio { get; }
        internal long AudioGeneration { get; }
        internal SubtitlesStream Subtitles { get; }
        internal int Slot { get; }
        public SubManager Manager { get; }
        public SrtTimingSource Source { get; }
        internal LocalMediaFileRevision MediaRevision { get; }
        public string MediaPath => MediaRevision.Path;
        public int StreamIndex { get; }
        public TimeSpan Duration { get; }
        public string AudioLabel { get; }
        public string Language { get; }
    }

    public SrtAlignmentTarget? CaptureSrtAlignmentTarget(int slot)
    {
        if ((uint)slot >= (uint)subNum) return null;
        lock (lockActions)
        {
            var audioDecoder = AudioDecoder;
            if (IsDisposed || IsMediaResetting || audioDecoder == null) return null;
            lock (audioDecoder.lockActions)
            {
                if (Playlist.Selected is not { } item || item.IOStream != null || item.ExternalVideoStream != null
                    || !Video.IsOpened || !Audio.IsOpened || Duration <= 0
                    || audioDecoder.AudioStream is not { StreamIndex: >= 0, ExternalStream: null } audio
                    || !ReferenceEquals(audio.Demuxer, VideoDemuxer) || VideoDemuxer.IsLive
                    || VideoDemuxer.LoadedLocalVideoFileRevision is not { } revision || !IsCurrentVideoFile(revision)
                    || !Subtitles[slot].Enabled || Subtitles[slot].EnabledASR
                    || decoder.SubtitlesStreams[slot] is not { ExternalStream: ExternalSubtitlesStream } subtitle)
                    return null;

                SubManager manager = SubtitlesManager[slot];
                if (manager.CaptureSrtTimingSource() is not { } source) return null;
                return new(item, MediaGeneration, audio, audioDecoder.StreamGeneration, subtitle,
                    slot, manager, source, revision, Duration);
            }
        }
    }

    /// <summary>Checks native input identity and the subtitle loader's revision while performing a short publication.</summary>
    public bool TryUseSrtAlignmentTarget(SrtAlignmentTarget target, Func<bool> owns, Action commit)
    {
        lock (lockActions)
        {
            var audioDecoder = AudioDecoder;
            if (IsDisposed || IsMediaResetting || audioDecoder == null || MediaGeneration != target.MediaGeneration
                || !ReferenceEquals(Playlist.Selected, target.Item))
                return false;

            lock (audioDecoder.lockActions)
            {
                int slot = target.Slot;
                if (audioDecoder.StreamGeneration != target.AudioGeneration || !Audio.IsOpened
                    || !ReferenceEquals(audioDecoder.AudioStream, target.Audio)
                    || !ReferenceEquals(decoder.SubtitlesStreams[slot], target.Subtitles)
                    || !Subtitles[slot].Enabled || Subtitles[slot].EnabledASR || !IsCurrentVideoFile(target.MediaRevision))
                    return false;
                return target.Manager.TryUseSrtTimingSource(target.Source, owns, commit);
            }
        }
    }

    static bool IsCurrentVideoFile(LocalMediaFileRevision revision) =>
        Demuxer.TryGetLocalMediaFileRevision(revision.Path, out var current) && current == revision;
}
