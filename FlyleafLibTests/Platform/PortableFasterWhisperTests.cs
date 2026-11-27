using AwesomeAssertions;
using FlyleafLib.MediaPlayer;
using FlyleafLib.MediaPlayer.Batch;

namespace FlyleafLib.Platform;

public sealed class PortableFasterWhisperTests : IDisposable
{
    readonly string directory = Path.Combine(Path.GetTempPath(), $"llplayer-faster-{Guid.NewGuid():N}");

    Config CreateConfig(string body, bool executable = true)
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "This process fixture requires a Linux shell and Unix executable permissions.");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "faster-whisper-xxl");
        File.WriteAllText(path, "#!/bin/sh\n" + body + "\n");
        if (OperatingSystem.IsLinux())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite |
                (executable ? UnixFileMode.UserExecute : 0));
        Config config = new(true);
        config.Subtitles.ASREngine = SubASREngineType.FasterWhisper;
        config.Subtitles.FasterWhisperConfig.UseManualEngine = true;
        config.Subtitles.FasterWhisperConfig.ManualEnginePath = path;
        config.Subtitles.WordTimestamps = false;
        return config;
    }

    [Fact]
    public void DefaultCommand_UsesLinuxExecutableName()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "The Linux default executable is checked on Linux; Windows XXL integration is a separate slice.");
        var command = FasterWhisperASRService.BuildCommand(new FasterWhisperConfig(), new WhisperConfig());

        command.TargetFilePath.Should().Be(Path.Combine(WhisperConfig.EnginesDirectory, "Faster-Whisper-XXL", "faster-whisper-xxl"));
    }

    [Fact]
    public void NonExecutableManualEngine_IsRejectedByInteractiveAndBatchWithPath()
    {
        Config config = CreateConfig("exit 0", executable: false);
        SubtitlesASR asr = new(new SubtitlesManager(config, 2), config);

        asr.CanExecute(out string error, out _).Should().BeFalse();
        error.Should().Contain(config.Subtitles.FasterWhisperConfig.ManualEnginePath!).And.Contain("chmod +x");
        Action create = () => new BatchAsrTranscriber(config);
        create.Should().Throw<InvalidOperationException>()
            .Where(e => e.Message.Contains("chmod +x") && e.Message.Contains(config.Subtitles.FasterWhisperConfig.ManualEnginePath!));
    }

    [Fact]
    public async Task ManualCli_ReceivesArgumentsAndProducesTimedDetectedLanguageCues()
    {
        // Controlled CLI fixture checks our process boundary, not compatibility of an uninstalled XXL binary.
        Config config = CreateConfig("""
            printf '%s\n' "$@" > "$(dirname "$0")/args.txt"
            printf "Detected language 'English' with probability 0.99\n"
            printf '[00:01.250 --> 00:02.500] Hello from short timing.\n'
            printf '[01:02:03.000 --> 01:02:04.500] Hello from long timing.\n'
            printf 'Operation finished in: 1s\n'
            """);
        await using FasterWhisperASRService service = new(config);
        using MemoryStream wave = new([1, 2, 3]);
        var cues = await Collect(service, wave, TestContext.Current.CancellationToken);

        cues.Should().HaveCount(2);
        cues[0].Should().Be(("Hello from short timing.", TimeSpan.FromMilliseconds(1250), TimeSpan.FromMilliseconds(2500), "en"));
        cues[1].Should().Be(("Hello from long timing.", new TimeSpan(0, 1, 2, 3), new TimeSpan(0, 1, 2, 4, 500), "en"));
        string[] args = File.ReadAllLines(Path.Combine(directory, "args.txt"));
        args.Should().ContainInOrder("--output_format", "srt", "--verbose", "True", "--beep_off", "--model", "tiny");
        args.Should().ContainInOrder("--device", "cpu");
        File.Exists(args[^1]).Should().BeFalse("temporary input is removed after the process finishes");
    }

    [Fact]
    public async Task NonzeroCliExit_WithSuccessSentinel_IsStillFailure()
    {
        Config config = CreateConfig("printf 'Operation finished in: 1s\\n'\nexit 7");
        await using FasterWhisperASRService service = new(config);
        using MemoryStream wave = new([1, 2, 3]);
        Func<Task> run = async () => await Collect(service, wave, TestContext.Current.CancellationToken);

        await run.Should().ThrowAsync<InvalidOperationException>().WithMessage("*exit code 7*");
    }

    [Fact]
    public async Task CliErrorWithoutSentinel_PreservesOutputForDiagnostics()
    {
        Config config = CreateConfig("printf 'fixture model failure\\n' >&2\nexit 1");
        await using FasterWhisperASRService service = new(config);
        using MemoryStream wave = new([1, 2, 3]);
        Func<Task> run = async () => await Collect(service, wave, TestContext.Current.CancellationToken);

        var failure = await run.Should().ThrowAsync<InvalidOperationException>();
        failure.Which.Data["whisper_output"].Should().BeOfType<string>().Which.Should().Contain("fixture model failure");
    }

    [Fact]
    public async Task CliCancellation_StopsProcessAndRemovesTemporaryInput()
    {
        Config config = CreateConfig("""
            printf '%s\n' "$@" > "$(dirname "$0")/args.tmp"
            mv "$(dirname "$0")/args.tmp" "$(dirname "$0")/args.txt"
            while :; do sleep 1; done
            """);
        await using FasterWhisperASRService service = new(config);
        using MemoryStream wave = new([1, 2, 3]);
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        Task run = Collect(service, wave, cancellation.Token);
        try
        {
            string argsPath = Path.Combine(directory, "args.txt");
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            while (!File.Exists(argsPath))
                await Task.Delay(10, timeout.Token);
            string input = File.ReadAllLines(argsPath)[^1];
            File.Exists(input).Should().BeTrue("the fixture process must have started before cancellation");
            cancellation.Cancel();
            Func<Task> finish = () => run.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);

            await finish.Should().ThrowAsync<OperationCanceledException>();
            run.IsCanceled.Should().BeTrue("the ASR operation must cancel, rather than only the test's wait timing out");
            File.Exists(input).Should().BeFalse("cancellation must run the temp-file cleanup");
        }
        finally
        {
            cancellation.Cancel();
            try { await run.WaitAsync(TimeSpan.FromSeconds(15), CancellationToken.None); }
            catch (OperationCanceledException) when (run.IsCanceled) { }
        }
    }

    [Fact]
    public async Task Align_UsesExactDelayedAudioStreamAndAddsItsMediaOffset()
    {
        Config config = CreateAlignmentConfig("""
            for wave do :; done
            printf '%s\n' '{"segments":[{"start":0.1,"end":0.25,"text":" two words","words":[{"word":" two","start":0.1,"end":0.2},{"word":" words","start":0.21,"end":0.25}]}]}' > "${wave%.*}.json"
            printf '[00:00.100 --> 00:00.250] two words\nOperation finished in: 1s\n'
            """);
        PortableVideoTestMedia.RequireEngine();
        var request = new SrtAlignmentAudioRequest(PortableVideoTestMedia.AlignmentTwoAudioTracks, 2, "en", SrtAlignmentDevice.Cpu);

        var words = await SrtAlignmentAudioSource.ReadWordsAsync(config, request, TestContext.Current.CancellationToken);

        Assert.Collection(words,
            first => Assert.InRange(first.Start.TotalSeconds, 1.33, 1.37),
            second => Assert.InRange(second.End.TotalSeconds, 1.48, 1.52),
            third => Assert.InRange(third.Start.TotalSeconds, 2.35, 2.39),
            fourth => Assert.InRange(fourth.End.TotalSeconds, 2.50, 2.54),
            fifth => Assert.InRange(fifth.Start.TotalSeconds, 3.38, 3.42),
            sixth => Assert.InRange(sixth.End.TotalSeconds, 3.53, 3.57));
        // Stream 0 is video in this fixture, so the request must fail before transcription.
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => SrtAlignmentAudioSource.ReadWordsAsync(config,
            request with { StreamIndex = 0 }, TestContext.Current.CancellationToken));
        Assert.Contains("selected audio stream", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1.0, 1.85, 1.35, 19_244)]
    [InlineData(2.137, 3.5, 2.237, 43_660)]
    public async Task Align_RetrySuppliesOneClippedWaveAndUsesItsActualMediaOrigin(
        double start, double end, double firstWordStart, int expectedWaveBytes)
    {
        Config config = CreateAlignmentConfig("""
            for wave do :; done
            wc -c < "$wave" >> "$(dirname "$0")/sizes.txt"
            printf '%s\n' '{"segments":[{"start":0.1,"end":0.25,"text":" two words","words":[{"word":" two","start":0.1,"end":0.2},{"word":" words","start":0.21,"end":0.25}]}]}' > "${wave%.*}.json"
            printf '[00:00.100 --> 00:00.250] two words\nOperation finished in: 1s\n'
            """);
        PortableVideoTestMedia.RequireEngine();
        var request = new SrtAlignmentAudioRequest(PortableVideoTestMedia.AlignmentTwoAudioTracks, 2, "en", SrtAlignmentDevice.Cpu)
        {
            Window = new(TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(end))
        };

        var words = await SrtAlignmentAudioSource.ReadWordsAsync(config, request, TestContext.Current.CancellationToken);

        Assert.Collection(words,
            first => Assert.InRange(first.Start.TotalSeconds, firstWordStart - .001, firstWordStart + .001),
            second => Assert.InRange(second.End.TotalSeconds, firstWordStart + .149, firstWordStart + .151));
        string count = Assert.Single(File.ReadAllLines(Path.Combine(directory, "sizes.txt")));
        Assert.InRange(int.Parse(count), expectedWaveBytes - 4, expectedWaveBytes);
    }

    [Fact]
    public async Task Align_RetryRejectsDriftAccumulatedFromFramesBelowTheDiscontinuityTolerance()
    {
        Config config = CreateAlignmentConfig("exit 90");
        PortableVideoTestMedia.RequireEngine();
        var request = new SrtAlignmentAudioRequest(PortableVideoTestMedia.AlignmentDriftingAudio, 1, "en", SrtAlignmentDevice.Cpu)
        {
            Window = new(TimeSpan.Zero, TimeSpan.FromSeconds(3.5))
        };

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            SrtAlignmentAudioSource.ReadWordsAsync(config, request, TestContext.Current.CancellationToken));

        Assert.Contains("drifts within the alignment retry window", error.Message, StringComparison.Ordinal);
    }

    Config CreateAlignmentConfig(string body)
    {
        Config config = CreateConfig(body);
        config.Subtitles.FasterWhisperConfig.UseManualModel = true;
        config.Subtitles.FasterWhisperConfig.ManualModelDir = directory;
        config.Subtitles.ASRChunkSeconds = 1;
        config.Subtitles.ASRSplitOnSilence = false;
        string model = Path.Combine(directory, "faster-whisper-tiny");
        Directory.CreateDirectory(model);
        foreach (string file in new[] { "model.bin", "config.json", "tokenizer.json", "vocabulary.json" })
            File.WriteAllText(Path.Combine(model, file), "fixture");
        return config;
    }

    static async Task<List<(string, TimeSpan, TimeSpan, string)>> Collect(
        FasterWhisperASRService service, MemoryStream wave, CancellationToken token)
    {
        List<(string, TimeSpan, TimeSpan, string)> result = [];
        await foreach (var cue in service.Do(wave, token))
            result.Add((cue.text, cue.start, cue.end, cue.language));
        return result;
    }

    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
}
