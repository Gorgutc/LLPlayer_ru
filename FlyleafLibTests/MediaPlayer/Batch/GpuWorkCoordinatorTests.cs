using FlyleafLib;
using FlyleafLib.MediaPlayer;

namespace FlyleafLibTests.MediaPlayer.Batch;

public sealed class GpuWorkCoordinatorTests
{
    [Fact]
    public async Task Canceling_a_waiter_does_not_release_its_predecessor()
    {
        using var request = new CancellationTokenSource();
        CancellationToken test = TestContext.Current.CancellationToken;
        var coordinator = new GpuWorkCoordinator();
        GpuWorkLease owner = await coordinator.AcquireAsync("batch", test);
        Task<GpuWorkLease> canceled = coordinator.AcquireAsync("ASR", request.Token).AsTask();
        Assert.False(canceled.IsCompleted);
        await request.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceled);

        Task<GpuWorkLease> waiting = coordinator.AcquireAsync("next batch", test).AsTask();
        Assert.False(waiting.IsCompleted);
        owner.Complete();
        owner.Complete();
        (await waiting.WaitAsync(test)).Complete();
    }

    [Fact]
    public async Task Handoff_waits_for_successful_retry_and_concurrent_retries_do_not_repeat_cleanup()
    {
        CancellationToken test = TestContext.Current.CancellationToken;
        var coordinator = new GpuWorkCoordinator();
        GpuWorkLease owner = await coordinator.AcquireAsync("dubbing batch", test);
        var finishing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int attempts = 0;
        owner.RequireDrain(async () =>
        {
            if (++attempts == 1) throw new IOException("Process exit not confirmed.");
            await finishing.Task.WaitAsync(test);
        });

        owner.Complete();
        Task<GpuWorkLease> waiting = coordinator.AcquireAsync("ASR", test).AsTask();
        Assert.Equal("dubbing batch", coordinator.PendingDrain);
        await Assert.ThrowsAsync<IOException>(() => coordinator.RetryDrainAsync());
        Assert.False(waiting.IsCompleted);
        Task<bool> retry = coordinator.RetryDrainAsync();
        Task<bool> sameRetry = coordinator.RetryDrainAsync();
        Assert.False(waiting.IsCompleted);
        Assert.Equal(2, attempts);
        finishing.SetResult();
        Assert.True(await retry.WaitAsync(test));
        Assert.True(await sameRetry.WaitAsync(test));
        Assert.Equal(2, attempts);
        Assert.Null(coordinator.PendingDrain);
        (await waiting.WaitAsync(test)).Complete();
    }

    [Theory]
    [InlineData("--device cpu", false)]
    [InlineData("--device=cpu --compute_type int8", false)]
    [InlineData("--initial_prompt \"the words --device cuda\" --device cpu", false)]
    [InlineData("--initial_prompt \"the words --device cpu\"", true)]
    [InlineData("--initial_prompt \"--device=cpu\"", true)]
    [InlineData("--device cpu --device cuda", true)]
    [InlineData("--device cpu --dev auto", true)]
    [InlineData("--device cpu \"--de\" cuda", true)]
    [InlineData("--device cpu --initial_prompt --device=cuda", true)]
    [InlineData("--model foo\0 --device cpu", true)]
    [InlineData("--device cpu \"--device=cuda\"", true)]
    [InlineData(@"--device cpu --model_dir C:\Whisper\ --device cuda", true)]
    [InlineData("--device auto", true)]
    [InlineData("--device cuda", true)]
    [InlineData("--compute_type int8", true)]
    [InlineData("--device cpu --initial_prompt \"unterminated", true)]
    [InlineData("-- --device cpu", true)]
    public void Only_an_unambiguous_effective_cpu_option_bypasses_admission(string arguments, bool expected)
    {
        Utils.IsTesting = true;
        var config = new Config(true);
        config.Subtitles.ASREngine = SubASREngineType.FasterWhisper;
        config.Subtitles.FasterWhisperConfig.ExtraArguments = arguments;
        Assert.Equal(expected, FasterWhisperDevicePolicy.RequiresGpuAdmission(config));
    }
}
