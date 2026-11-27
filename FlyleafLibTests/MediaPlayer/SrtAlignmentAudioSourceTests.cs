using System.Diagnostics;
using CliWrap.EventStream;
using FlyleafLib.Plugins;

namespace FlyleafLib.MediaPlayer;

public sealed class SrtAlignmentAudioSourceTests : IDisposable
{
    readonly string directory = Path.Combine(Path.GetTempPath(), $"llplayer-alignment-{Guid.NewGuid():N}");

    [Fact]
    public void RetryPcmUsesTheFirstCompleteSampleInsideTheWindowAsItsOrigin()
    {
        TimeSpan sourceStart = TimeSpan.FromSeconds(10) + TimeSpan.FromTicks(100);
        var window = new SrtAlignmentAudioWindow(sourceStart + TimeSpan.FromTicks(900),
            sourceStart + TimeSpan.FromTicks(4_050));
        using var wave = new MemoryStream();
        wave.Write(new byte[44]);
        wave.Write(new byte[] { 0, 0, 11, 0, 22, 0, 33, 0, 44, 0, 55, 0, 66, 0, 77, 0 });

        Assert.True(AudioReader.TryTrimAlignmentWave(wave, sourceStart, window, out TimeSpan start));

        Assert.Equal(sourceStart + TimeSpan.FromTicks(1_250), start);
        Assert.Equal(new byte[] { 22, 0, 33, 0, 44, 0, 55, 0 }, wave.ToArray()[44..]);
    }

    [Fact]
    public void RetryPcmDoesNotInventSamplesBeforeTheSelectedAudioStarts()
    {
        TimeSpan sourceStart = TimeSpan.FromSeconds(5);
        var window = new SrtAlignmentAudioWindow(sourceStart - TimeSpan.FromSeconds(2), sourceStart);
        using var wave = new MemoryStream();
        wave.Write(new byte[48]);

        Assert.False(AudioReader.TryTrimAlignmentWave(wave, sourceStart, window, out _));
    }

    [Fact]
    public void SnapshotPinsLocalModelLanguageAndDeviceWithoutInheritingArguments()
    {
        string model = Path.Combine(directory, "faster-whisper-large-v3");
        Directory.CreateDirectory(model);
        foreach (string file in new[] { "model.bin", "config.json", "tokenizer.json", "vocabulary.json" })
            File.WriteAllText(Path.Combine(model, file), "fixture");
        string executable = Path.Combine(directory, "engine.exe");
        File.WriteAllText(executable, "fixture");
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        FlyleafLib.Utils.IsTesting = true;
        Config source = new(true);
        var fasterWhisper = source.Subtitles.FasterWhisperConfig;
        fasterWhisper.UseManualEngine = true;
        fasterWhisper.ManualEnginePath = executable;
        fasterWhisper.Model = "large-v3";
        fasterWhisper.UseManualModel = true;
        fasterWhisper.ManualModelDir = directory;
        fasterWhisper.Prompt = "a text hint";
        fasterWhisper.ExtraArguments = "--task translate --language de --device auto";
        source.Subtitles.WhisperConfig.Translate = true;
        var request = new SrtAlignmentAudioRequest("example.mkv", 2, "en", SrtAlignmentDevice.Cuda);

        var snapshot = SrtAlignmentAudioSource.CreateRunSettings(source, request);

        Assert.Equal(directory, snapshot.Subtitles.FasterWhisperConfig.ManualModelDir);
        Assert.Equal("en", snapshot.Subtitles.WhisperConfig.Language);
        Assert.NotSame(fasterWhisper, snapshot.Subtitles.FasterWhisperConfig);
        Assert.Empty(snapshot.Subtitles.FasterWhisperConfig.Prompt);
        Assert.Equal("--device cuda --compute_type float16 --task transcribe", snapshot.Subtitles.FasterWhisperConfig.ExtraArguments);
        Config cpu = SrtAlignmentAudioSource.CreateRunSettings(source, request with { Device = SrtAlignmentDevice.Cpu });
        Assert.Equal("--device cpu --compute_type float32 --task transcribe", cpu.Subtitles.FasterWhisperConfig.ExtraArguments);
        Assert.False(snapshot.Subtitles.WhisperConfig.Translate);
        Assert.True(source.Subtitles.WhisperConfig.Translate);
    }

    [Fact]
    public void PartialModelCannotStartOfflineTranscription()
    {
        string modelPath = Path.Combine(directory, "faster-whisper-tiny");
        Directory.CreateDirectory(modelPath);
        File.WriteAllText(Path.Combine(modelPath, "model.bin"), "fixture");

        Assert.Throws<FileNotFoundException>(() => SrtAlignmentAudioSource.FindLocalModel(directory, "tiny"));
    }

    [Fact]
    public void WordEvidenceChecksSegmentIdentityAndShiftsToMediaTime()
    {
        const string json = """
            {"segments":[{"start":0.3,"end":1.15,"text":" Do align!",
              "words":[{"word":" Do","start":0.3,"end":0.55},{"word":" align!","start":0.55,"end":1.15}]}]}
            """;
        string path = WriteJson(json);

        var words = SrtAlignmentAudioSource.ReadWordEvidence(path,
            [("Do align!", TimeSpan.FromSeconds(.3), TimeSpan.FromSeconds(1.15), "en")]);
        var absolute = SrtAlignmentAudioSource.ToMediaWords(words[0], TimeSpan.FromSeconds(18), TimeSpan.FromSeconds(2));

        Assert.Equal(new WordTiming(" Do", TimeSpan.FromSeconds(18.3), TimeSpan.FromSeconds(18.55)), absolute[0]);
        Assert.Equal(new WordTiming(" align!", TimeSpan.FromSeconds(18.55), TimeSpan.FromSeconds(19.15)), absolute[1]);
        Assert.Throws<InvalidDataException>(() => SrtAlignmentAudioSource.ReadWordEvidence(path,
            [("Other words", TimeSpan.FromSeconds(.3), TimeSpan.FromSeconds(1.15), "en")]));
    }

    [Fact]
    public void InvalidWordsCannotLeaveChunk()
    {
        Assert.Throws<InvalidDataException>(() => SrtAlignmentAudioSource.ToMediaWords(
            [new WordTiming("backward", TimeSpan.FromSeconds(1), TimeSpan.Zero)],
            TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(2)));
        Assert.Throws<InvalidDataException>(() => SrtAlignmentAudioSource.ToMediaWords(
            [new WordTiming("outside", TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3))],
            TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public void MissingWordOutputIsInvalidEvidence()
    {
        Directory.CreateDirectory(directory);
        var error = Assert.Throws<InvalidDataException>(() =>
            SrtAlignmentAudioSource.ReadWordEvidence(Path.Combine(directory, "missing.json"),
                [("spoken words", TimeSpan.Zero, TimeSpan.FromSeconds(1), "en")]));

        Assert.IsType<FileNotFoundException>(error.InnerException);
    }

    [Fact]
    public void ZeroDurationWordDoesNotDiscardOtherTimingEvidence()
    {
        var words = SrtAlignmentAudioSource.ToMediaWords(
            [new("heard", TimeSpan.Zero, TimeSpan.FromSeconds(1)),
             new("instant", TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1))],
            TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(2));

        Assert.Equal(new WordTiming("heard", TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(4)), Assert.Single(words));
    }

    [Fact]
    public void ExitCodeOverridesSuccessMarker()
    {
        Assert.True(FasterWhisperASRService.IsSuccessfulAlignmentExit(0, "Operation finished in: 1s"));
        Assert.False(FasterWhisperASRService.IsSuccessfulAlignmentExit(7, "Operation finished in: 1s"));
        Assert.False(FasterWhisperASRService.IsSuccessfulAlignmentExit(null, "Operation finished in: 1s"));
        Assert.False(FasterWhisperASRService.IsSuccessfulAlignmentExit(0, "incomplete"));
    }

    [Fact]
    public async Task ContainedProcessReportsNonzeroExitAfterSuccessMarker()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows() || OperatingSystem.IsLinux(), "Requires a supported engine process owner.");
        var start = new ProcessStartInfo
        {
            FileName = OperatingSystem.IsWindows()
                ? Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe") : "/bin/sh",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        if (OperatingSystem.IsWindows())
        {
            foreach (string argument in new[] { "-NoProfile", "-Command", "[Console]::WriteLine('Operation finished in: 1s'); exit 7" })
                start.ArgumentList.Add(argument);
        }
        else
        {
            start.ArgumentList.Add("-c");
            start.ArgumentList.Add("printf 'Operation finished in: 1s\\n'; exit 7");
        }

        OnlineVideoProcess process = OnlineVideoProcess.StartEngine(start);
        try
        {
            string? lastLine = null;
            int? exitCode = null;
            await foreach (var item in process.ReadEngineEventsAsync(TestContext.Current.CancellationToken))
            {
                if (item is StandardOutputCommandEvent output)
                    lastLine = output.Text;
                if (item is ExitedCommandEvent exit)
                    exitCode = exit.ExitCode;
            }

            Assert.StartsWith("Operation finished in:", lastLine);
            Assert.Equal(7, exitCode);
            Assert.False(FasterWhisperASRService.IsSuccessfulAlignmentExit(exitCode, lastLine));
        }
        finally
        {
            await process.DisposeEngineAsync();
        }
    }

    string WriteJson(string input)
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "fixture.json");
        File.WriteAllText(path, input);
        return path;
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }
}
