using System.Reflection;
using LLPlayer.Core.Services;

namespace FlyleafLib.Core;

public class RecognitionIdleAdmissionTests
{
    [Fact]
    public async Task Explicit_settings_mutation_rejects_a_stalled_queue_immediately_and_never_runs_later()
    {
        MethodInfo method = IdleMutation();
        await using RecognitionActions actions = new(new Backend());
        using ManualResetEventSlim release = new();
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task blocked = actions.MutateAsync(token =>
        {
            entered.TrySetResult();
            if (!release.Wait(TimeSpan.FromSeconds(10), token)) throw new TimeoutException("Test queue barrier was not released.");
        });
        int edits = 0;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.True(actions.IsBusy);
            var rejection = Assert.Throws<TargetInvocationException>(() => method.Invoke(actions,
                new object[] { (Action<CancellationToken>)(_ => Interlocked.Increment(ref edits)) }));
            Assert.IsType<InvalidOperationException>(rejection.InnerException);
            Assert.Equal(0, edits); Assert.False(blocked.IsCompleted);
        }
        finally { release.Set(); await blocked; }

        await actions.MutateAsync(() => { }); // Drain any work incorrectly accepted behind the barrier.
        Assert.Equal(0, edits);
        await Assert.IsAssignableFrom<Task>(method.Invoke(actions,
            new object[] { (Action<CancellationToken>)(_ => Interlocked.Increment(ref edits)) }));
        Assert.Equal(1, edits);
    }

    [Fact]
    public async Task Explicit_settings_mutation_rejects_native_recognition_busy_even_with_an_empty_queue()
    {
        MethodInfo method = IdleMutation();
        Backend backend = new() { Busy = true };
        await using RecognitionActions actions = new(backend);
        int edits = 0;
        var rejection = Assert.Throws<TargetInvocationException>(() => method.Invoke(actions,
            new object[] { (Action<CancellationToken>)(_ => edits++) }));
        Assert.IsType<InvalidOperationException>(rejection.InnerException);
        backend.Busy = false;
        await actions.MutateAsync(() => { });
        Assert.Equal(0, edits);
    }

    static MethodInfo IdleMutation()
    {
        MethodInfo? method = typeof(RecognitionActions).GetMethod("MutateIfIdleAsync", [typeof(Action<CancellationToken>)]);
        Assert.NotNull(method);
        return method;
    }

    sealed class Backend : IRecognitionBackend
    {
        public bool Busy { get; set; }
        public bool HasAudio => true;
        public bool IsLive => false;
        public bool IsBusy => Busy;
        public bool IsPaused => false;
        public void Start(int slot) { }
        public void Pause() { }
        public void Resume() { }
        public void Cancel(int slot) { }
        public void Ocr(int slot) { }
        public void Apply(RecognitionSettings settings) { }
    }
}
