using FlyleafLib;
using FlyleafLib.MediaPlayer;
using FlyleafLib.MediaPlayer.Dubbing;
using LLPlayer.Core.Services;

namespace FlyleafLibTests.Core;

public class DubbingSettingsTests
{
    [Fact]
    public void Old_editor_preserves_new_batch_voice_unless_user_explicitly_changes_it()
    {
        DubbingSettings baseline = new();
        var current = baseline.Clone();
        current.Engine.DefaultVoiceId = "batch-chosen-voice";
        current.Engine.CustomVoiceIds = ["current-custom"];
        var edited = baseline.Clone();
        edited.Engine.DuckingPercent = 0;
        var merged = DubbingSettings.MergeChanges(current, baseline, edited);
        Assert.Equal("batch-chosen-voice", merged.Engine.DefaultVoiceId);
        Assert.Equal(0, merged.Engine.DuckingPercent);
        Assert.Equal(new[] { "current-custom" }, merged.Engine.CustomVoiceIds);
        merged.Engine.CustomVoiceIds.Add("merged-only");
        Assert.Single(current.Engine.CustomVoiceIds);
        edited.Engine.DefaultVoiceId = "ru-preset-2";
        edited.Engine.CustomVoiceIds = ["explicit-custom"];
        merged = DubbingSettings.MergeChanges(current, baseline, edited);
        Assert.Equal("ru-preset-2", merged.Engine.DefaultVoiceId);
        Assert.Equal(new[] { "explicit-custom" }, merged.Engine.CustomVoiceIds);
        edited.Engine.CustomVoiceIds.Add("later-draft-change");
        Assert.Single(merged.Engine.CustomVoiceIds);
        Assert.Equal("batch-chosen-voice", current.Engine.DefaultVoiceId);
    }

    [Fact]
    public void Draft_is_detached_and_applies_all_existing_engine_fields()
    {
        var original = new DubbingSettings();
        original.Engine.CustomVoiceIds = ["custom"];
        var draft = original.Clone();
        draft.Engine.CustomVoiceIds.Add("another");
        draft.Engine.DefaultVoiceId = "custom";
        draft.Engine.DuckingPercent = 0;
        draft.PersistPerLineVoices = true;
        Assert.Single(original.Engine.CustomVoiceIds);
        Assert.False(original.PersistPerLineVoices);
        Config.SubtitlesConfig target = new();
        draft.Apply(target);
        Assert.Equal("custom", target.DubbingConfig.DefaultVoiceId);
        Assert.Equal(0, target.DubbingConfig.DuckingPercent);
        Assert.True(target.PersistPerLineVoices);
        Assert.NotSame(draft.Engine, target.DubbingConfig);
    }

    [Fact]
    public void Store_preserves_corrupt_input_and_roundtrips_without_enabling_dubbing()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            var store = new DubbingSettingsStore(path);
            Assert.False(store.Load().PersistPerLineVoices);
            Assert.False(File.Exists(path));
            File.WriteAllText(path, "{broken");
            Assert.False(store.Load().PersistPerLineVoices);
            Assert.NotNull(store.LoadWarning);
            Assert.Equal("{broken", File.ReadAllText(path));
            var settings = new DubbingSettings();
            settings.Engine.CustomVoiceIds = ["custom"];
            settings.Engine.DefaultVoiceId = "custom";
            settings.Engine.ManualVenvPython = "/opt/tts/bin/python";
            settings.Engine.UseManualEngine = true;
            settings.Engine.AtempoMin = 0.8;
            settings.Engine.AtempoMax = 1.3;
            settings.PersistPerLineVoices = true;
            store.Save(settings);
            var loaded = store.Load();
            Assert.Null(store.LoadWarning);
            Assert.Equal("custom", loaded.Engine.DefaultVoiceId);
            Assert.Equal("/opt/tts/bin/python", loaded.Engine.ManualVenvPython);
            Assert.Equal(0.8, loaded.Engine.AtempoMin);
            Assert.True(loaded.PersistPerLineVoices);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Assignment_snapshot_is_immutable_and_disk_layer_is_opt_in()
    {
        string dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string media = Path.Combine(dir, "video.mkv");
        try
        {
            SubtitleData live = Cue("live");
            DubbingVoiceAssignmentStore.SaveAtomic(media, [Cue("disk")]);
            var snapshot = DubbingWorkflow.CreateVoiceAssignments(true, media, [live]);
            live.AssignedVoiceId = "edited-after-snapshot";
            SubtitleData target = Cue(null);
            snapshot!.Apply(media, [target]);
            Assert.Equal("live", target.AssignedVoiceId);
            target = Cue(null);
            DubbingWorkflow.CreateVoiceAssignments(true, null, null)!.Apply(media, [target]);
            Assert.Equal("disk", target.AssignedVoiceId);
            Assert.Null(DubbingWorkflow.CreateVoiceAssignments(false, null, null));
        }
        finally { Directory.Delete(dir, true); }
    }

    static SubtitleData Cue(string? voice) => new()
    { Text = "line", StartTime = TimeSpan.Zero, EndTime = TimeSpan.FromSeconds(1), AssignedVoiceId = voice };
}
