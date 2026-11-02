using FlyleafLib.MediaFramework.MediaPlaylist;
using LLPlayer.Core.Services;

namespace FlyleafLibTests.Core;

public class DubbedAudioAutoLoaderTests
{
    [Fact]
    public void Dispatcher_checks_current_media_and_attaches_only_once_without_selecting_audio()
    {
        string dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string first = Path.Combine(dir, "first.mkv"), second = Path.Combine(dir, "second.mkv");
            File.WriteAllBytes(first, [1]);
            File.WriteAllBytes(second, [1]);
            File.WriteAllBytes(Path.Combine(dir, "first.ru.dub.flac"), [1]);
            PlaylistItem firstItem = new() { Url = first }, replacement = new() { Url = second };
            PlaylistItem current = firstItem;
            Action? queued = null;
            DubbedAudioAutoLoader.TryAttach(() => current, first, action => queued = action);
            Assert.Empty(firstItem.ExternalAudioStreams);
            current = replacement;
            queued!();
            Assert.Empty(replacement.ExternalAudioStreams);
            current = firstItem;
            DubbedAudioAutoLoader.TryAttach(() => current, first, action => action());
            DubbedAudioAutoLoader.TryAttach(() => current, first, action => action());
            Assert.Single(firstItem.ExternalAudioStreams);
            Assert.Null(firstItem.ExternalAudioStream);
        }
        finally { Directory.Delete(dir, true); }
    }
}
