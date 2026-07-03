using AwesomeAssertions;

namespace FlyleafLib.MediaPlayer;

// SubtitleData.Clone must carry the per-cue metadata — source language (T-10), speaker id (F-03 prep), and the
// per-line dub voice (F-16 phase 2a) — along with the existing scalar fields.
public class SubtitleDataTests
{
    [Fact]
    public void Clone_CopiesLanguage()
    {
        var sub = new SubtitleData
        {
            Index = 3,
            Text = "Hello",
            StartTime = TimeSpan.FromSeconds(1),
            EndTime = TimeSpan.FromSeconds(2),
            Language = Language.English,
        };

        SubtitleData clone = sub.Clone();

        clone.Language.Should().Be(Language.English);
        // Sanity: the clone is a distinct instance still carrying the other scalar fields (non-vacuous).
        clone.Should().NotBeSameAs(sub);
        clone.Index.Should().Be(3);
        clone.Text.Should().Be("Hello");
        clone.StartTime.Should().Be(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void Clone_NullLanguage_StaysNull()
    {
        // Loaded / translated cues never set a language; the clone must keep it null (byte-identical default).
        var sub = new SubtitleData { Text = "x", Language = null };

        sub.Clone().Language.Should().BeNull();
    }

    [Fact]
    public void Clone_CopiesSpeakerId()
    {
        var sub = new SubtitleData
        {
            Index = 7,
            Text = "Hi",
            StartTime = TimeSpan.FromSeconds(3),
            EndTime = TimeSpan.FromSeconds(4),
            SpeakerId = "SPEAKER_00",
        };

        SubtitleData clone = sub.Clone();

        clone.SpeakerId.Should().Be("SPEAKER_00");
        // Sanity: distinct instance still carrying the other scalar fields (non-vacuous).
        clone.Should().NotBeSameAs(sub);
        clone.Index.Should().Be(7);
        clone.Text.Should().Be("Hi");
        clone.StartTime.Should().Be(TimeSpan.FromSeconds(3));
    }

    [Fact]
    public void Clone_NullSpeakerId_StaysNull()
    {
        // Nothing populates the speaker yet (diarization is a future GPU sidecar); the clone must keep it null
        // (byte-identical default for loaded/ASR cues).
        var sub = new SubtitleData { Text = "x", SpeakerId = null };

        sub.Clone().SpeakerId.Should().BeNull();
    }

    [Fact]
    public void Clone_CopiesAssignedVoiceId()
    {
        var sub = new SubtitleData
        {
            Index = 9,
            Text = "Line",
            StartTime = TimeSpan.FromSeconds(5),
            EndTime = TimeSpan.FromSeconds(6),
            AssignedVoiceId = "ru-preset-2",
        };

        SubtitleData clone = sub.Clone();

        clone.AssignedVoiceId.Should().Be("ru-preset-2");
        // Sanity: distinct instance still carrying the other scalar fields (non-vacuous).
        clone.Should().NotBeSameAs(sub);
        clone.Index.Should().Be(9);
        clone.Text.Should().Be("Line");
    }

    [Fact]
    public void Clone_NullAssignedVoiceId_StaysNull()
    {
        // Default for every cue (no per-line dub voice assigned); the clone must keep it null so a dub with no
        // overrides stays byte-identical to the single-voice render.
        var sub = new SubtitleData { Text = "x", AssignedVoiceId = null };

        sub.Clone().AssignedVoiceId.Should().BeNull();
    }

    [Fact]
    public void EnabledTranslated_Toggle_NotifiesDisplayTextAndUseTranslated()
    {
        // HC-17: EnabledTranslated became an INPC property (was a plain field). Toggling the translation display now
        // notifies the derived properties the sidebar binds to (DisplayText/UseTranslated), so the bound rows update
        // without the SubConfig setter forcing a full ListCollectionView.Refresh().
        var sub = new SubtitleData
        {
            Text = "hello",
            TranslatedText = "привет", // "привет" → IsTranslated == true
        };
        sub.EnabledTranslated.Should().BeTrue();
        sub.UseTranslated.Should().BeTrue();
        sub.DisplayText.Should().Be("привет");

        var changed = new List<string?>();
        sub.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        sub.EnabledTranslated = false;

        // Falls back to the source text and notifies the properties bound by the sidebar rows.
        sub.UseTranslated.Should().BeFalse();
        sub.DisplayText.Should().Be("hello");
        changed.Should().Contain(nameof(SubtitleData.EnabledTranslated));
        changed.Should().Contain(nameof(SubtitleData.UseTranslated));
        changed.Should().Contain(nameof(SubtitleData.DisplayText));
    }

    [Fact]
    public void EnabledTranslated_Toggle_WithoutTranslation_DoesNotNotifyUseTranslated()
    {
        // No translation available: DisplayText is always the source text and UseTranslated stays false regardless of
        // the toggle, so UseTranslated must not be notified (mirrors the TranslatedText setter's guard).
        var sub = new SubtitleData { Text = "hello" }; // TranslatedText null → IsTranslated false
        sub.UseTranslated.Should().BeFalse();

        var changed = new List<string?>();
        sub.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        sub.EnabledTranslated = false;

        sub.UseTranslated.Should().BeFalse();
        sub.DisplayText.Should().Be("hello");
        changed.Should().NotContain(nameof(SubtitleData.UseTranslated));
    }

    [Fact]
    public void EnabledTranslated_SetSameValue_DoesNotNotify()
    {
        // Idempotent set (Set() returns false) must raise nothing, so a redundant toggle does no per-cue UI work.
        var sub = new SubtitleData { Text = "hello", TranslatedText = "x" };
        sub.EnabledTranslated.Should().BeTrue();

        var changed = new List<string?>();
        sub.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        sub.EnabledTranslated = true; // unchanged

        changed.Should().BeEmpty();
    }

    [Fact]
    public void Clone_CopiesAllPerCueMetadataTogether()
    {
        // Guard against a future Clone refactor dropping one of the independent per-cue metadata fields.
        var sub = new SubtitleData
        {
            Text = "y",
            Language = Language.English,
            SpeakerId = "SPEAKER_01",
            AssignedVoiceId = "ru-preset-2",
        };

        SubtitleData clone = sub.Clone();

        clone.Language.Should().Be(Language.English);
        clone.SpeakerId.Should().Be("SPEAKER_01");
        clone.AssignedVoiceId.Should().Be("ru-preset-2");
    }
}
