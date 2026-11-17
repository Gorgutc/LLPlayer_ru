using System.Collections.Immutable;
using Avalonia.Headless.XUnit;
using FlyleafLib.MediaPlayer;
using LLPlayer.Avalonia.Services;
using LLPlayer.ControlApi;
using LLPlayer.Core.Automation;

namespace LLPlayer.Avalonia.Tests.Automation;

[Collection(FlyleafGlobalsCollection.Name)]
public sealed class ControlRecognitionProjectionTests
{
    [AvaloniaFact]
    public async Task Configured_control_host_rejects_process_local_stream_without_local_recognition()
    {
        using TempDir state = new();
        using Player player = PlaybackControlOpenTests.CreatePlayer(state, out AvaloniaUIDispatcher dispatcher, out CollectionSyncBridge bridge);
        await using var controller = new FlyleafPlaybackController(player, dispatcher, bridge);
        string? media = Environment.GetEnvironmentVariable("LLPLAYER_TEST_MEDIA");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(media), "Requires the canonical local video fixture.");
        player.Config.Subtitles.WhisperCppConfig.ModelDirectory = Path.Combine(state.Path, "missing-model-directory");
        using FileStream stream = File.OpenRead(media!);
        Assert.Null(player.Open(stream).Error);
        Assert.True(player.Audio.IsOpened);

        var host = new ControlledRecognition();
        controller.ConfigureControlRecognition(new(state.Path, state.Path),
            _ => Task.FromResult(new FlyleafPlaybackController.ControlRecognitionConnection(host, host)));

        ControlException error = await Assert.ThrowsAsync<ControlException>(() => controller.StartRecognitionAsync(0));
        Assert.Equal("unsupported_source", error.Error.Code);
        Assert.Contains("process-local", error.Message);
        Assert.Equal(0, host.Starts);
        Assert.False(player.IsASRRunning);
    }

    [AvaloniaFact]
    public async Task Same_audio_slots_share_preview_and_detaching_one_keeps_the_host_job()
    {
        using TempDir state = new();
        using Player player = PlaybackControlOpenTests.CreatePlayer(state, out AvaloniaUIDispatcher dispatcher, out CollectionSyncBridge bridge);
        await using var controller = new FlyleafPlaybackController(player, dispatcher, bridge);
        string? media = Environment.GetEnvironmentVariable("LLPLAYER_TEST_MEDIA");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(media), "Requires the canonical local video fixture.");
        await controller.OpenControlMediaAsync(media!, false, CancellationToken.None);
        Assert.True(player.Audio.IsOpened);

        var host = new ControlledRecognition();
        controller.ConfigureControlRecognition(new(state.Path, state.Path),
            _ => Task.FromResult(new FlyleafPlaybackController.ControlRecognitionConnection(host, host)));
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        CancellationToken token = deadline.Token;
        Task primary = controller.StartRecognitionAsync(0);
        Task? secondary = null;
        try
        {
            await WaitForAsync(() => controller.GetCues(0).Count == 1, token);
            secondary = controller.StartRecognitionAsync(1);
            await WaitForAsync(() => controller.GetCues(1).Count == 1, token);
            Assert.Equal("first words", Assert.Single(controller.GetCues(1)).Text);
            Assert.Equal(1, host.Starts);
            await controller.CancelRecognitionAsync(0).WaitAsync(token);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => primary.WaitAsync(token));
            Assert.Equal(0, host.Cancels);
            Assert.False(controller.PrimarySlotEnabled);

            host.Finish();
            await secondary.WaitAsync(token);
            Assert.Equal(new[] { "first words", "last words" }, controller.GetCues(1).Select(cue => cue.Text));
            Assert.False(controller.PrimarySlotEnabled);
            Assert.Equal(1, host.Starts);
        }
        finally
        {
            controller.DetachControlRecognition();
            host.Finish();
            try { await Task.WhenAll(primary, secondary ?? Task.CompletedTask).WaitAsync(token); }
            catch (OperationCanceledException) { }
        }
    }

    [AvaloniaFact]
    public async Task Reopening_the_same_file_invalidates_the_previous_recognition_projection()
    {
        using TempDir state = new();
        using Player player = PlaybackControlOpenTests.CreatePlayer(state, out AvaloniaUIDispatcher dispatcher, out CollectionSyncBridge bridge);
        await using var controller = new FlyleafPlaybackController(player, dispatcher, bridge);
        string? media = Environment.GetEnvironmentVariable("LLPLAYER_TEST_MEDIA");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(media), "Requires the canonical local video fixture.");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        CancellationToken token = deadline.Token;
        await controller.OpenControlMediaAsync(media!, false, token);
        var host = new ControlledRecognition();
        controller.ConfigureControlRecognition(new(state.Path, state.Path),
            _ => Task.FromResult(new FlyleafPlaybackController.ControlRecognitionConnection(host, host)));
        Task recognition = controller.StartRecognitionAsync(0);
        try
        {
            await WaitForAsync(() => controller.GetCues(0).Count == 1, token);
            await controller.OpenControlMediaAsync(media!, false, token);
            host.Finish();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => recognition.WaitAsync(token));
            Assert.Empty(controller.GetCues(0));
            Assert.Equal(1, host.Starts);
        }
        finally
        {
            controller.DetachControlRecognition();
            host.Finish();
            try { await recognition.WaitAsync(token); }
            catch (OperationCanceledException) { }
        }
    }

    static async Task WaitForAsync(Func<bool> condition, CancellationToken token)
    {
        while (!condition()) await Task.Delay(25, token);
    }

    sealed class ControlledRecognition : IPlayerControlService, IControlJobService
    {
        readonly Guid jobId = Guid.NewGuid();
        readonly Guid documentId = Guid.NewGuid();
        readonly ControlCue first = new(Guid.NewGuid(), TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(700),
            "first words", null, "en", false, null, null, []);
        readonly ControlCue last = new(Guid.NewGuid(), TimeSpan.FromMilliseconds(850), TimeSpan.FromMilliseconds(1200),
            "last words", null, "en", false, null, null, []);
        bool completed;
        public int Starts { get; private set; }
        public int Cancels { get; private set; }
        public void Finish() => Volatile.Write(ref completed, true);

        public Task<ControlResult> InvokeAsync(ControlRequest request, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (request.OperationId == "recognition.start")
            {
                Starts++;
                return Task.FromResult(new ControlResult(JobId: jobId));
            }
            Assert.Equal("subtitles.document.read", request.OperationId);
            bool done = Volatile.Read(ref completed);
            ImmutableArray<ControlCue> cues = done ? [first, last] : [first];
            ControlDocumentPage page = new(documentId, done ? "second-revision" : "first-revision", "en", null, cues, cues.Length, null);
            return Task.FromResult(new ControlResult(Value: ControlProtocol.Encode(page)));
        }

        public Task<ControlJob?> GetAsync(ControlScope scope, Guid id, CancellationToken ct = default)
        {
            Assert.Equal(jobId, id);
            ct.ThrowIfCancellationRequested();
            bool done = Volatile.Read(ref completed);
            return Task.FromResult<ControlJob?>(new(jobId, scope, "recognition.start",
                done ? ControlJobStatus.Completed : ControlJobStatus.Running, done ? 2 : 1, DateTimeOffset.UtcNow));
        }

        public Task<ControlResult> CancelAsync(ControlJobAction action, CancellationToken ct = default)
        {
            Cancels++;
            return Task.FromResult(new ControlResult(JobId: jobId));
        }

        public Task<ControlCapabilities> DescribeAsync(ControlScope scope, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ControlState> GetStateAsync(ControlScope scope, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ControlJobPage> ListAsync(ControlScope scope, string? cursor = null, int limit = 50, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<ControlResult> RetryAsync(ControlJobAction action, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ControlResult> ResumeAsync(ControlJobAction action, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
