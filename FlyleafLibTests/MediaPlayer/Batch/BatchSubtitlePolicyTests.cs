using AwesomeAssertions;
using FlyleafLib.MediaFramework.MediaStream;
using FlyleafLib.MediaPlayer.Batch;

namespace FlyleafLib.MediaPlayer;

public class BatchSubtitlePolicyTests
{
    [Fact]
    public void ResolveInitialSourceLanguage_UsesSelectedRussianTrackBeforeWhisperFallback()
    {
        Config config = new(true);
        config.Subtitles.WhisperConfig.LanguageDetection = false;
        config.Subtitles.WhisperConfig.Language = "en";

        MediaAudioProbeResult audio = new(
            "movie.mkv",
            StreamIndex: 1,
            MediaType.Audio,
            Language.Russian,
            TimeSpan.FromMinutes(10),
            [new MediaAudioTrack(1, Language.Russian, null, null, 2)],
            LanguageTagged: true);

        BatchAsrTranscriber.ResolveInitialSourceLanguage(config, audio)
            .Should().Be(Language.Russian);
    }

    [Fact]
    public void ResolveInitialSourceLanguage_KeepsEnglishForEnglishOnlyAsrModel()
    {
        Config config = new(true);
        config.Subtitles.ASREngine = SubASREngineType.FasterWhisper;
        config.Subtitles.FasterWhisperConfig.Model = "tiny.en";

        MediaAudioProbeResult audio = new(
            "movie.mkv",
            StreamIndex: 1,
            MediaType.Audio,
            Language.Russian,
            TimeSpan.FromMinutes(10),
            [new MediaAudioTrack(1, Language.Russian, null, null, 2)],
            LanguageTagged: true);

        BatchAsrTranscriber.ResolveInitialSourceLanguage(config, audio)
            .Should().Be(Language.English);
    }

    [Fact]
    public void ResolveReportedSourceLanguage_KeepsSelectedRussianTrackOverAsrManualFallback()
    {
        BatchAsrTranscriber.ResolveReportedSourceLanguage(
                current: Language.Russian,
                selectedAudioLanguage: Language.Russian,
                asrReportedLanguage: Language.English)
            .Should().Be(Language.Russian);
    }

    [Fact]
    public void ResolveReportedSourceLanguage_KeepsEnglishOnlyModelOverSelectedRussianTrack()
    {
        BatchAsrTranscriber.ResolveReportedSourceLanguage(
                current: Language.English,
                selectedAudioLanguage: Language.Russian,
                asrReportedLanguage: Language.English)
            .Should().Be(Language.English);
    }

    [Fact]
    public void ScanPolicy_IncludesExistingSrtWithoutDub_WhenDubbingIsEnabled()
    {
        BatchSubtitleJob job = new("movie.mkv");

        BatchSubtitleScanPolicy.ApplyOutputState(
            job,
            hasTranslation: true,
            hasDub: false,
            overwriteExisting: false,
            generateDubbing: true);

        job.Status.Should().Be(BatchSubtitleStatus.Pending);
        job.Include.Should().BeTrue();
        job.CompletedAt.Should().BeNull();
    }

    [Fact]
    public void ScanPolicy_KeepsDubOnlyJobIncluded_WhenTranslationIsMissing()
    {
        BatchSubtitleJob job = new("movie.mkv");

        BatchSubtitleScanPolicy.ApplyOutputState(
            job,
            hasTranslation: false,
            hasDub: true,
            overwriteExisting: false,
            generateDubbing: true);

        job.Status.Should().Be(BatchSubtitleStatus.Pending);
        job.Include.Should().BeTrue();
    }

    [Fact]
    public void ScanPolicy_ExcludesExistingSrtAndDub_WhenDubbingIsEnabled()
    {
        BatchSubtitleJob job = new("movie.mkv");

        BatchSubtitleScanPolicy.ApplyOutputState(
            job,
            hasTranslation: true,
            hasDub: true,
            overwriteExisting: false,
            generateDubbing: true);

        job.Status.Should().Be(BatchSubtitleStatus.Completed);
        job.Include.Should().BeFalse();
        job.CompletedAt.Should().NotBeNull();
    }
}
