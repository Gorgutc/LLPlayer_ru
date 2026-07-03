using AwesomeAssertions;
using FlyleafLib.MediaPlayer;
using FlyleafLib.MediaPlayer.Translation;

namespace FlyleafLib;

// HC-08: TranslateLanguage (a [JsonIgnore] derived Language) must be non-null even when TranslateTargetLanguage is
// left at its default (EnglishAmerican). TranslateTargetLanguage's field initializer assigns its backing field
// directly (not through the setter), so the setter side-effect that assigns TranslateLanguage never fires for a
// config left at the default -> before the fix TranslateLanguage stayed null for the whole session, NRE-ing
// consumers (WordPopup / SubtitleConverters / Player.Playback). The fix seeds the backing field to the default
// target's language while the setter still updates it on change.
public class SubtitlesConfigTranslateLanguageTests
{
    [Fact]
    public void TranslateLanguage_AtDefaultTarget_IsNotNull()
    {
        // RED-without-fix: the default target (EnglishAmerican) never fires the setter, so TranslateLanguage was null.
        Config.SubtitlesConfig config = new();

        config.TranslateTargetLanguage.Should().Be(TargetLanguage.EnglishAmerican);
        config.TranslateLanguage.Should().NotBeNull();
        config.TranslateLanguage.ISO6391.Should().Be("en"); // EnglishAmerican -> "en"
    }

    [Fact]
    public void TranslateLanguage_TracksTargetChange()
    {
        // Changing away from the default (EnglishAmerican -> "en") fires the setter side-effect, which keeps the
        // derived language in sync: "en" -> "ru". Independent of the HC-08 seed (which only covers the default),
        // so this characterizes the pre-existing setter path and asserts the observable state transition.
        Config.SubtitlesConfig config = new();

        config.TranslateTargetLanguage = TargetLanguage.Russian;

        config.TranslateLanguage.Should().NotBeNull();
        config.TranslateLanguage.ISO6391.Should().Be("ru"); // != the default "en" — proves the transition
    }
}
