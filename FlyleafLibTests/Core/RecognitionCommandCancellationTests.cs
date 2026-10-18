using System.Reflection;
using System.Runtime.CompilerServices;
using FlyleafLib.MediaFramework.MediaStream;
using FlyleafLib.MediaPlayer;
using LLPlayer.Core.Services;
using static FlyleafLib.MediaFramework.MediaContext.DecoderContext;

namespace FlyleafLibTests.Core;

public class RecognitionCommandCancellationTests
{
    static readonly TimeSpan Limit = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Media_invalidation_releases_lost_subtitle_completion_and_recovers_queue()
    {
        var backend = new HeldSubtitleBackend();
        var actions = new RecognitionActions(backend);
        Task pending = actions.OcrAsync(0);
        Task? next = null;
        try
        {
            await backend.Started.Task.WaitAsync(Limit, TestContext.Current.CancellationToken);
            bool opened = false;
            next = actions.MutateAsync(() => opened = true, mediaChange: true);
            await next.WaitAsync(Limit, TestContext.Current.CancellationToken);
            await Assert.ThrowsAsync<RecognitionActionCanceledException>(() => pending);
            Assert.True(opened);
            Assert.False(actions.IsDispatching);
        }
        finally
        {
            backend.Complete.TrySetResult();
            await Settle(pending);
            if (next != null) await Settle(next);
            await actions.DisposeAsync();
        }
    }

    [Fact]
    public async Task Shutdown_cancels_lost_subtitle_completion_before_draining_queue()
    {
        var backend = new HeldSubtitleBackend();
        var actions = new RecognitionActions(backend);
        Task pending = actions.OcrAsync(0);
        try
        {
            await backend.Started.Task.WaitAsync(Limit, TestContext.Current.CancellationToken);
            await actions.DisposeAsync().AsTask().WaitAsync(Limit, TestContext.Current.CancellationToken);
            await Assert.ThrowsAsync<RecognitionActionCanceledException>(() => pending);
            Assert.False(actions.IsDispatching);
        }
        finally
        {
            backend.Complete.TrySetResult();
            await Settle(pending);
            await actions.DisposeAsync();
        }
    }

    [Fact]
    public async Task Normal_subtitle_command_keeps_queue_owned_until_completion()
    {
        var backend = new HeldSubtitleBackend();
        await using var actions = new RecognitionActions(backend);
        Task pending = actions.OcrAsync(0);
        try
        {
            await backend.Started.Task.WaitAsync(Limit, TestContext.Current.CancellationToken);
            Assert.False(pending.IsCompleted);
            Assert.True(actions.IsDispatching);
        }
        finally { backend.Complete.TrySetResult(); }
        await pending.WaitAsync(Limit, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Invalidation_during_command_admission_cancels_the_subsequent_wait()
    {
        using ManualResetEventSlim admissionEntered = new(), releaseAdmission = new();
        var backend = new HeldSubtitleBackend
        {
            Admitted = _ => { admissionEntered.Set(); releaseAdmission.Wait(TestContext.Current.CancellationToken); }
        };
        var actions = new RecognitionActions(backend);
        Task pending = actions.OcrAsync(0);
        try
        {
            Assert.True(admissionEntered.Wait(Limit, TestContext.Current.CancellationToken));
            actions.Invalidate();
            releaseAdmission.Set();
            await Assert.ThrowsAsync<RecognitionActionCanceledException>(() => pending.WaitAsync(Limit, TestContext.Current.CancellationToken));
            await actions.MutateAsync(() => { }).WaitAsync(Limit, TestContext.Current.CancellationToken);
        }
        finally
        {
            releaseAdmission.Set(); backend.Complete.TrySetResult();
            await Settle(pending); await actions.DisposeAsync();
        }
    }

    [Fact]
    public async Task Shutdown_keeps_the_token_alive_until_cancellation_callbacks_finish()
    {
        using ManualResetEventSlim callbackEntered = new(), releaseCallback = new();
        CancellationToken captured = default;
        var backend = new HeldSubtitleBackend
        {
            Admitted = token =>
            {
                captured = token;
                token.Register(() => { callbackEntered.Set(); releaseCallback.Wait(TestContext.Current.CancellationToken); });
            }
        };
        var actions = new RecognitionActions(backend);
        Task pending = actions.OcrAsync(0);
        try
        {
            await backend.Started.Task.WaitAsync(Limit, TestContext.Current.CancellationToken);
            Task shutdown = actions.DisposeAsync().AsTask();
            Assert.True(callbackEntered.Wait(Limit, TestContext.Current.CancellationToken));
            Assert.False(shutdown.IsCompleted);
            Assert.NotNull(captured.WaitHandle);
            releaseCallback.Set();
            await shutdown.WaitAsync(Limit, TestContext.Current.CancellationToken);
            await Assert.ThrowsAsync<RecognitionActionCanceledException>(() => pending);
        }
        finally
        {
            releaseCallback.Set(); backend.Complete.TrySetResult();
            await Settle(pending); await actions.DisposeAsync();
        }
    }

    [Fact]
    public async Task Subtitle_wait_ignores_unrelated_events_until_matching_completion()
    {
        var player = (Player)RuntimeHelpers.GetUninitializedObject(typeof(Player));
        var stream = (SubtitlesStream)RuntimeHelpers.GetUninitializedObject(typeof(SubtitlesStream));
        var other = (SubtitlesStream)RuntimeHelpers.GetUninitializedObject(typeof(SubtitlesStream));
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task pending = Task.Run(() => PlayerRecognitionBackend.RunSubtitleCommand(player, stream, () => started.SetResult()), TestContext.Current.CancellationToken);
        try
        {
            await started.Task.WaitAsync(Limit, TestContext.Current.CancellationToken);
            Raise(player, nameof(Player.OpenSubtitlesStreamCompleted), new OpenSubtitlesStreamCompletedArgs(other));
            Raise(player, nameof(Player.OpenCompleted), new FlyleafLib.MediaPlayer.OpenCompletedArgs("other.srt", isSubtitles: true));
            Assert.False(pending.IsCompleted);
            Raise(player, nameof(Player.OpenSubtitlesStreamCompleted), new OpenSubtitlesStreamCompletedArgs(stream));
            await pending.WaitAsync(Limit, TestContext.Current.CancellationToken);
        }
        finally
        {
            Raise(player, nameof(Player.OpenSubtitlesStreamCompleted), new OpenSubtitlesStreamCompletedArgs(stream));
            await Settle(pending);
        }
    }

    [Fact]
    public async Task Media_completion_releases_a_subtitle_open_dropped_by_the_engine()
    {
        // Only the Player event boundary is needed: avoid constructing its native media engine.
        var player = (Player)RuntimeHelpers.GetUninitializedObject(typeof(Player));
        var stream = (SubtitlesStream)RuntimeHelpers.GetUninitializedObject(typeof(SubtitlesStream));
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task pending = Task.Run(() => PlayerRecognitionBackend.RunSubtitleCommand(player, stream, () => started.SetResult()), TestContext.Current.CancellationToken);
        try
        {
            await started.Task.WaitAsync(Limit, TestContext.Current.CancellationToken);
            Raise(player, nameof(Player.OpenCompleted), new FlyleafLib.MediaPlayer.OpenCompletedArgs("replacement.mp4"));
            await Assert.ThrowsAsync<RecognitionActionCanceledException>(() => pending.WaitAsync(Limit, TestContext.Current.CancellationToken));
        }
        finally
        {
            Raise(player, nameof(Player.OpenSubtitlesStreamCompleted), new OpenSubtitlesStreamCompletedArgs(stream));
            await Settle(pending);
        }
    }

    static void Raise<T>(Player player, string name, T args)
        => ((EventHandler<T>?)typeof(Player).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(player))?.Invoke(player, args);

    static async Task Settle(Task task)
    {
        try { await task.WaitAsync(Limit); }
        catch (RecognitionActionCanceledException) { }
    }

    sealed class HeldSubtitleBackend : IRecognitionBackend
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Complete { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool HasAudio => true;
        public bool IsLive => false;
        public bool IsBusy => false;
        public bool IsPaused => false;
        public Action<CancellationToken>? Admitted { get; init; }
        public void Start(int slot) { }
        public void Pause() { }
        public void Resume() { }
        public void Cancel(int slot) { }
        public void Apply(RecognitionSettings settings) { }
        public void Ocr(int slot) => Ocr(slot, CancellationToken.None);
        public void Ocr(int slot, CancellationToken cancellationToken)
        {
            Admitted?.Invoke(cancellationToken);
            Started.SetResult();
            Complete.Task.WaitAsync(cancellationToken).GetAwaiter().GetResult();
        }
    }
}
