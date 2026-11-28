using FlyleafLib.MediaPlayer;
using FlyleafLib.MediaPlayer.AsrOmissions;

namespace FlyleafLibTests.MediaPlayer;

public sealed class FasterWhisperOmissionCaptureTests
{
    [Fact]
    public void Default_twenty_second_chunk_has_proper_overlapping_confirmation_windows()
    {
        const int samples = 20 * 16_000;
        var tiles = FasterWhisperOmissionCapture.PlanTiles(samples);
        Assert.Collection(tiles, first => Assert.Equal(0, first.StartSample), second =>
        {
            Assert.InRange(second.StartSample, 1, tiles[0].SampleCount - 1);
            Assert.Equal(samples, second.StartSample + second.SampleCount);
        });
        Assert.All(tiles, tile => Assert.InRange(tile.SampleCount, 1, samples - 1));
        Assert.Empty(FasterWhisperOmissionCapture.PlanTiles(16_000));
    }

    [Fact]
    public void Supplement_preserves_decoder_flags_and_replaces_disposable_run_arguments()
    {
        string[] baseline = ["--model", "large-v3", "--vad_threshold", "0.35", "--initial_prompt=auto",
            "--output_dir", "old scratch", "--output_format", "srt", "json", "--device", "cuda",
            "--language", "en", "--word_timestamps", "True"];
        var args = FasterWhisperOmissionCapture.MakeArguments(baseline, "new scratch", "copied pcm.wav", "cpu", "ja");

        Assert.Equal(new[] { "--model", "large-v3", "--vad_threshold", "0.35", "--word_timestamps", "True",
            "--output_dir", "new scratch", "--output_format", "json", "--language", "ja", "--device", "cpu",
            "--initial_prompt", "None", "copied pcm.wav" }, args);
        Assert.Null(FasterWhisperOmissionCapture.MakeArguments(["--clip_timestamps", "1,2"], "out", "in", "cpu", "en"));
    }

    [Fact]
    public void Point_timed_words_in_a_gap_can_still_require_confirmation()
    {
        AsrOmissionCue donor = new("確認したよ", 500, 1500,
            [new("確認", 500, 500), new("した", 900, 900), new("よ", 1500, 1500)]);
        Assert.True(FasterWhisperOmissionCapture.MayNeedConfirmation([], [donor]));
        Assert.False(FasterWhisperOmissionCapture.MayNeedConfirmation([new("already timed", 0, 2000, [])], [donor]));
    }

    [Fact]
    public void Invalid_primary_clocks_cannot_be_counted_as_analyzed()
    {
        AsrOmissionCue cue = new("first", 0, 1000, []);
        AsrOmissionChunk chunk = new(0, 2000, [cue], []);
        Assert.True(AsrOmissionDetector.ValidBaseline(chunk));
        Assert.False(AsrOmissionDetector.ValidBaseline(chunk with { Baseline = [cue, new("overlap", 900, 1500, [])] }));
        Assert.False(AsrOmissionDetector.ValidBaseline(chunk with { Baseline = [cue, new("past PCM", 1500, 2100, [])] }));
    }
}
