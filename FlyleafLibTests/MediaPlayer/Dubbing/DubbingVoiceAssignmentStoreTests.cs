using AwesomeAssertions;

namespace FlyleafLib.MediaPlayer.Dubbing;

public class DubbingVoiceAssignmentStoreTests
{
    // ---- ToJson / FromJson (pure, off-disk) -------------------------------------------------

    [Fact]
    public void ToJson_NoAssignments_ReturnsNull()
    {
        DubbingVoiceAssignmentStore.ToJson([Cue("a", 1, 2, null), Cue("b", 2, 3, "   ")])
            .Should().BeNull();
    }

    [Fact]
    public void ToJson_NullSubtitles_ReturnsNull()
    {
        DubbingVoiceAssignmentStore.ToJson(null).Should().BeNull();
    }

    [Fact]
    public void ToJson_FromJson_RoundTripsAssignments()
    {
        string? json = DubbingVoiceAssignmentStore.ToJson(
            [Cue("a", 1.5, 2.5, " voice-a "), Cue("b", 3, 4, "voice-b")]);

        json.Should().NotBeNull();

        var entries = DubbingVoiceAssignmentStore.FromJson(json);

        entries.Should().HaveCount(2);
        entries[0].StartMs.Should().Be(1500);
        entries[0].EndMs.Should().Be(2500);
        entries[0].VoiceId.Should().Be("voice-a"); // trimmed
        entries[1].VoiceId.Should().Be("voice-b");
    }

    [Fact]
    public void ToJson_DeduplicatesByTimingWindow_FirstWins()
    {
        string? json = DubbingVoiceAssignmentStore.ToJson(
            [Cue("a", 1, 2, "voice-a"), Cue("dup", 1, 2, "voice-b")]);

        var entries = DubbingVoiceAssignmentStore.FromJson(json);

        entries.Should().ContainSingle();
        entries[0].VoiceId.Should().Be("voice-a");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{ not json")]
    [InlineData("[1,2,3]")]
    [InlineData("\"a string\"")]
    public void FromJson_BlankOrCorrupt_ReturnsEmpty(string? json)
    {
        DubbingVoiceAssignmentStore.FromJson(json).Should().BeEmpty();
    }

    [Fact]
    public void FromJson_DropsBlankVoiceIds()
    {
        string json = "{\"SchemaVersion\":1,\"Assignments\":["
            + "{\"StartMs\":1000,\"EndMs\":2000,\"VoiceId\":\"voice-a\"},"
            + "{\"StartMs\":3000,\"EndMs\":4000,\"VoiceId\":\"  \"}]}";

        var entries = DubbingVoiceAssignmentStore.FromJson(json);

        entries.Should().ContainSingle();
        entries[0].VoiceId.Should().Be("voice-a");
    }

    // ---- BuildVoicesPath --------------------------------------------------------------------

    [Fact]
    public void BuildVoicesPath_IsBesideMediaWithVoicesExtension()
    {
        string mediaPath = Path.Combine("C:", "videos", "movie.mkv");

        string voicesPath = DubbingVoiceAssignmentStore.BuildVoicesPath(mediaPath);

        Path.GetFileName(voicesPath).Should().Be("movie.ru.voices.json");
        Path.GetDirectoryName(voicesPath).Should().Be(Path.GetDirectoryName(mediaPath));
    }

    [Fact]
    public void BuildVoicesPath_DoesNotCollideWithDubGlob()
    {
        // Must NOT be a ".ru.dub.*" name — that glob is how an existing rendered dub is detected.
        string voicesPath = DubbingVoiceAssignmentStore.BuildVoicesPath(Path.Combine("C:", "v", "movie.mkv"));

        Path.GetFileName(voicesPath).Should().NotContain(".ru.dub.");
    }

    // ---- SaveAtomic / LoadMap (round-trip on disk) ------------------------------------------

    [Fact]
    public void SaveAtomic_ThenLoadMap_RestoresVoiceOntoMatchingCue()
    {
        string mediaPath = NewTempMediaPath();
        try
        {
            DubbingVoiceAssignmentStore.SaveAtomic(mediaPath, [Cue("src", 1.123, 2.5, "voice-a")]);

            File.Exists(DubbingVoiceAssignmentStore.BuildVoicesPath(mediaPath)).Should().BeTrue();

            SubtitleData reloaded = Cue("translated", 1.123, 2.5, null);
            DubbingVoiceAssignmentStore.LoadMap(mediaPath).Apply(mediaPath, [reloaded]);

            reloaded.AssignedVoiceId.Should().Be("voice-a");
        }
        finally
        {
            CleanupDir(mediaPath);
        }
    }

    [Fact]
    public void SaveAtomic_NoAssignments_WritesNoFile()
    {
        string mediaPath = NewTempMediaPath();
        try
        {
            DubbingVoiceAssignmentStore.SaveAtomic(mediaPath, [Cue("src", 1, 2, null)]);

            File.Exists(DubbingVoiceAssignmentStore.BuildVoicesPath(mediaPath)).Should().BeFalse();
        }
        finally
        {
            CleanupDir(mediaPath);
        }
    }

    [Fact]
    public void SaveAtomic_NoAssignments_DeletesStaleCompanion()
    {
        string mediaPath = NewTempMediaPath();
        try
        {
            DubbingVoiceAssignmentStore.SaveAtomic(mediaPath, [Cue("src", 1, 2, "voice-a")]);
            File.Exists(DubbingVoiceAssignmentStore.BuildVoicesPath(mediaPath)).Should().BeTrue();

            // Clearing all overrides removes the companion so nothing stale persists.
            DubbingVoiceAssignmentStore.SaveAtomic(mediaPath, [Cue("src", 1, 2, null)]);

            File.Exists(DubbingVoiceAssignmentStore.BuildVoicesPath(mediaPath)).Should().BeFalse();
        }
        finally
        {
            CleanupDir(mediaPath);
        }
    }

    [Fact]
    public void SaveAtomic_EmptyAuthoritativeSnapshot_DeletesStaleCompanion()
    {
        string mediaPath = NewTempMediaPath();
        try
        {
            DubbingVoiceAssignmentStore.SaveAtomic(mediaPath, [Cue("src", 1, 2, "voice-a")]);
            File.Exists(DubbingVoiceAssignmentStore.BuildVoicesPath(mediaPath)).Should().BeTrue();

            DubbingVoiceAssignmentStore.SaveAtomic(mediaPath, []);

            File.Exists(DubbingVoiceAssignmentStore.BuildVoicesPath(mediaPath)).Should().BeFalse();
        }
        finally
        {
            CleanupDir(mediaPath);
        }
    }

    [Fact]
    public void LoadMap_MissingFile_ReturnsEmptyAndAppliesNothing()
    {
        string mediaPath = NewTempMediaPath();
        SubtitleData cue = Cue("t", 1, 2, null);

        DubbingVoiceAssignmentStore.LoadMap(mediaPath).Apply(mediaPath, [cue]);

        cue.AssignedVoiceId.Should().BeNull();
    }

    [Fact]
    public void LoadMap_ForDifferentMedia_DoesNotApply()
    {
        string mediaPath = NewTempMediaPath();
        try
        {
            DubbingVoiceAssignmentStore.SaveAtomic(mediaPath, [Cue("src", 1, 2, "voice-a")]);

            string otherMedia = NewTempMediaPath();
            SubtitleData cue = Cue("t", 1, 2, null);
            DubbingVoiceAssignmentStore.LoadMap(mediaPath).Apply(otherMedia, [cue]);

            cue.AssignedVoiceId.Should().BeNull();
        }
        finally
        {
            CleanupDir(mediaPath);
        }
    }

    // ---- DiskVoiceAssignmentProvider / Composite --------------------------------------------

    [Fact]
    public void DiskProvider_FillsOnlyCuesWithoutOverride()
    {
        string mediaPath = NewTempMediaPath();
        try
        {
            DubbingVoiceAssignmentStore.SaveAtomic(mediaPath, [Cue("a", 1, 2, "disk-a"), Cue("b", 3, 4, "disk-b")]);

            SubtitleData live = Cue("a", 1, 2, "live-a");   // already assigned this session
            SubtitleData empty = Cue("b", 3, 4, null);

            new DiskVoiceAssignmentProvider().Apply(mediaPath, [live, empty]);

            live.AssignedVoiceId.Should().Be("live-a");     // not clobbered
            empty.AssignedVoiceId.Should().Be("disk-b");    // filled from disk
        }
        finally
        {
            CleanupDir(mediaPath);
        }
    }

    [Fact]
    public void Composite_EarlierProviderWins()
    {
        string mediaPath = Path.Combine(Path.GetTempPath(), "movie.mkv");
        IDubbingVoiceAssignmentProvider live = DubbingVoiceAssignmentMap.FromCurrentSubtitles(
            mediaPath, [Cue("a", 1, 2, "live-a")]);
        IDubbingVoiceAssignmentProvider fallback = DubbingVoiceAssignmentMap.FromCurrentSubtitles(
            mediaPath, [Cue("a", 1, 2, "fallback-a")]);

        SubtitleData cue = Cue("a", 1, 2, null);
        new CompositeVoiceAssignmentProvider(live, fallback).Apply(mediaPath, [cue]);

        cue.AssignedVoiceId.Should().Be("live-a");
    }

    // ---- helpers ----------------------------------------------------------------------------

    private static SubtitleData Cue(string text, double startSec, double endSec, string? voiceId) => new()
    {
        Text = text,
        StartTime = TimeSpan.FromSeconds(startSec),
        EndTime = TimeSpan.FromSeconds(endSec),
        AssignedVoiceId = voiceId,
    };

    private static string NewTempMediaPath()
        => Path.Combine(Path.GetTempPath(), $"llp-voices-{Guid.NewGuid():N}", "movie.mkv");

    private static void CleanupDir(string mediaPath)
    {
        string? dir = Path.GetDirectoryName(mediaPath);
        if (dir is not null && Directory.Exists(dir))
            Directory.Delete(dir, recursive: true);
    }
}
