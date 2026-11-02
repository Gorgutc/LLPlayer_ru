using FlyleafLib;
using FlyleafLib.MediaPlayer;
using FlyleafLib.MediaPlayer.Dubbing;

namespace FlyleafLibTests.MediaPlayer.Dubbing;

public class DubbingRendererLifecycleTests
{
    [Fact]
    public async Task One_backend_per_run_preserves_snapshot_and_per_line_voice()
    {
        string dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var backend = new FakeBackend();
        var config = new DubbingConfig { DefaultVoiceId = "ru-preset-2", DuckingPercent = 0 };
        var renderer = new DubbingRenderer(config, () => backend);
        config.DefaultVoiceId = "changed-after-start";
        try
        {
            await renderer.RenderAsync([Cue(null), Cue("custom")], "movie.mkv", Path.Combine(dir, "movie.ru.dub.flac"), null, TestContext.Current.CancellationToken);
            await renderer.RenderAsync([Cue(null)], "other.mkv", Path.Combine(dir, "other.ru.dub.flac"), null, TestContext.Current.CancellationToken);
            await renderer.DisposeAsync();
            await renderer.DisposeAsync();
            Assert.Equal(1, backend.Initializations);
            Assert.Equal(new[] { "ru-preset-2", "custom", "ru-preset-2" }, backend.Voices);
            Assert.All(backend.Assembled, request => Assert.Equal(0, request.DuckingPercent));
            Assert.Equal(1, backend.Disposals);
            Assert.Equal(2, Directory.GetFiles(dir).Length);
        }
        finally { await renderer.DisposeAsync(); Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task Failed_assembly_cleans_late_output_after_backend_disposal()
    {
        string output = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".ru.dub.flac");
        File.WriteAllText(output, "previous usable dub");
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var backend = new FakeBackend { CancelAssembly = true, CancelOnAssembly = cancellation };
        var renderer = new DubbingRenderer(new(), () => backend);
        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => renderer.RenderAsync([Cue(null)], "movie.mkv", output, null, cancellation.Token));
            await renderer.DisposeAsync();
            Assert.Equal("previous usable dub", File.ReadAllText(output));
            Assert.All(backend.Assembled, request => Assert.False(File.Exists(request.OutputPath)));
            Assert.Equal(1, backend.Disposals);
        }
        finally { await renderer.DisposeAsync(); File.Delete(output); }
    }

    [Fact]
    public void Default_python_path_follows_host_platform_and_manual_override_wins()
    {
        var snapshot = DubSidecarHost.DubbingSnapshot.From(new());
        Assert.Equal(Path.Combine(DubbingConfig.DefaultEngineDir,
            OperatingSystem.IsWindows() ? "Scripts" : "bin",
            OperatingSystem.IsWindows() ? "python.exe" : "python"), snapshot.PythonPath);
        var manual = DubSidecarHost.DubbingSnapshot.From(new DubbingConfig { UseManualEngine = true, ManualVenvPython = "chosen-python" });
        Assert.Equal("chosen-python", manual.PythonPath);
    }

    [Fact]
    public async Task Unready_backend_is_disposed_and_never_synthesizes()
    {
        var backend = new FakeBackend { Ready = false };
        await using var renderer = new DubbingRenderer(new(), () => backend);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            renderer.RenderAsync([Cue(null)], "movie.mkv", "unused.ru.dub.flac", null, TestContext.Current.CancellationToken));
        Assert.Equal(1, backend.Disposals);
        Assert.Empty(backend.Voices);
    }

    static SubtitleData Cue(string? voice) => new()
    { Text = "line", StartTime = TimeSpan.Zero, EndTime = TimeSpan.FromSeconds(1), AssignedVoiceId = voice };

    sealed class FakeBackend : IDubbingTtsService
    {
        public int Initializations, Disposals;
        public List<string> Voices { get; } = [];
        public List<AssembleRequest> Assembled { get; } = [];
        public bool CancelAssembly { get; init; }
        public CancellationTokenSource? CancelOnAssembly { get; init; }
        public bool Ready { get; init; } = true;
        public Task<bool> TryInitializeAsync(CancellationToken token) { Initializations++; return Task.FromResult(Ready); }
        public Task<TtsResult> SynthesizeAsync(TtsRequest request, CancellationToken token)
        { Voices.Add(request.VoiceId); return Task.FromResult(new TtsResult("clip.wav", 1000)); }
        public Task<IReadOnlyList<TtsVoice>> GetVoicesAsync(CancellationToken token) => Task.FromResult(VoiceBankResolver.BuiltIn);
        public Task AssembleAsync(AssembleRequest request, CancellationToken token)
        {
            Assembled.Add(request);
            if (CancelAssembly)
            {
                CancelOnAssembly?.Cancel();
                token.ThrowIfCancellationRequested();
                throw new OperationCanceledException(token);
            }
            File.WriteAllText(request.OutputPath, "completed dub");
            return Task.CompletedTask;
        }
        public ValueTask DisposeAsync()
        {
            Disposals++;
            if (CancelAssembly)
                foreach (var request in Assembled) File.WriteAllText(request.OutputPath, "late completed output");
            return ValueTask.CompletedTask;
        }
    }
}
