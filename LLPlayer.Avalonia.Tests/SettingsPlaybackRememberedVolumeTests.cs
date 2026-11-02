using System.Text;
using System.Text.Json;
using LLPlayer.Avalonia.Services;

namespace LLPlayer.Avalonia.Tests;

public class SettingsPlaybackRememberedVolumeTests
{
    static Action<int, bool> SaveVolume(PrefsStore store)
    {
        // Compile against the existing store so the initial RED is a missing behavior assertion, not a compiler error.
        var method = typeof(PrefsStore).GetMethod("SaveRememberedVolume", [typeof(int), typeof(bool)]);
        Assert.NotNull(method);
        return method.CreateDelegate<Action<int, bool>>(store);
    }

    [Fact]
    public void Narrow_save_reads_fresh_preferences_and_preserves_unrelated_known_and_unknown_values()
    {
        using var temp = new TempDir(); string path = Path.Combine(temp.Path, "prefs.json");
        File.WriteAllText(path, "{\"Theme\":\"Dark\",\"Volume\":40}");
        var store = new PrefsStore(path); store.Load();
        // A second editor updates the file after this store was opened; volume save must not restore the old snapshot.
        const string latest = "{\"Theme\":\"Light\",\"Volume\":65,\"Mute\":true,\"RecentFiles\":[\"fresh.mp4\",\"字幕.mkv\"],\"OwnerExtension\":{\"Mode\":\"Keep me\",\"Count\":7}}";
        File.WriteAllText(path, latest);
        SaveVolume(store)(91, false);
        using JsonDocument result = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement root = result.RootElement;
        Assert.Equal(91, root.GetProperty("Volume").GetInt32()); Assert.False(root.GetProperty("Mute").GetBoolean());
        Assert.Equal("Light", root.GetProperty("Theme").GetString());
        Assert.Equal(["fresh.mp4", "字幕.mkv"], root.GetProperty("RecentFiles").EnumerateArray().Select(value => value.GetString()));
        Assert.Equal("Keep me", root.GetProperty("OwnerExtension").GetProperty("Mode").GetString());
        Assert.Equal(7, root.GetProperty("OwnerExtension").GetProperty("Count").GetInt32());
    }

    [Fact]
    public void Corrupt_existing_preferences_are_not_replaced_by_a_volume_only_write()
    {
        using var temp = new TempDir(); string path = Path.Combine(temp.Path, "prefs.json");
        byte[] original = new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes("{broken owner preferences")).ToArray();
        File.WriteAllBytes(path, original); var save = SaveVolume(new PrefsStore(path));
        Assert.ThrowsAny<Exception>(() => save(90, true)); Assert.Equal(original, File.ReadAllBytes(path));
    }

    [Fact]
    public void Absent_preferences_keep_all_normal_absent_key_defaults_after_remembering_volume()
    {
        using var temp = new TempDir(); string path = Path.Combine(temp.Path, "prefs.json"); var store = new PrefsStore(path);
        SaveVolume(store)(92, true); AppPrefs loaded = store.Load();
        Assert.Equal(92, loaded.Volume); Assert.True(loaded.Mute); Assert.Equal("Dark", loaded.Theme);
        Assert.Equal(AppPrefs.DefaultSubtitleFontSize, loaded.SubtitleFontSize); Assert.Empty(loaded.RecentFiles);
        Assert.Equal(AppPrefs.DefaultActivityTimeoutMs, loaded.ActivityTimeoutMs); Assert.Null(store.LoadWarning);
    }

    [Fact]
    public void Corrupt_secondary_preferences_cause_the_playback_candidate_to_roll_back()
    {
        using var temp = new TempDir(); string path = Path.Combine(temp.Path, "prefs.json");
        const string corrupt = "[not preferences]"; File.WriteAllText(path, corrupt);
        Action<int, bool> secondary = SaveVolume(new PrefsStore(path));
        var playback = new PlaybackSettingsStore(Path.Combine(temp.Path, "playback.json"));
        Assert.ThrowsAny<Exception>(() => playback.Save(new(), () => secondary(88, false)));
        Assert.False(File.Exists(playback.Path)); Assert.False(playback.HasSavedSettings); Assert.Equal(corrupt, File.ReadAllText(path));
    }
}
