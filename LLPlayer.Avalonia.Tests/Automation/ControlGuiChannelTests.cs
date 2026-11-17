using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using LLPlayer.Avalonia.Services.Automation;
using LLPlayer.ControlApi;

namespace LLPlayer.Avalonia.Tests.Automation;

public sealed class ControlGuiChannelTests
{
    [Fact]
    public void Replayed_deliveries_are_acknowledged_and_cancel_is_delivered_once()
    {
        ControlGuiAcknowledgements acknowledgements = new();
        ControlRequest request = new(Guid.NewGuid(), "media.open",
            new(ControlProtocol.DefaultProfileId, Guid.NewGuid()), null,
            ControlProtocol.Encode(new ControlMediaOpen("video.mp4")));
        ControlGuiCommand start = new(Guid.NewGuid(), Guid.NewGuid(), request);
        ControlGuiCommand cancel = start with { DeliveryId = Guid.NewGuid(), Cancel = true };

        Assert.True(acknowledgements.Observe(start));
        Assert.False(acknowledgements.Observe(start));
        Assert.Equal(start.DeliveryId, acknowledgements.PendingDeliveryId);
        Assert.Equal(start.CommandId, cancel.CommandId);
        Assert.True(acknowledgements.Observe(cancel));
        Assert.False(acknowledgements.Observe(cancel));
        Assert.Equal(cancel.DeliveryId, acknowledgements.PendingDeliveryId);
        Assert.False(acknowledgements.Observe(null));
        Assert.Null(acknowledgements.PendingDeliveryId);
    }

    [AvaloniaFact]
    public async Task Native_playback_operations_execute_on_ui_dispatcher_and_wait_for_result()
    {
        Guid sessionId = Guid.NewGuid();
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        ControlGuiChannel channel = new(async (request, cancellationToken) =>
        {
            Assert.True(Dispatcher.UIThread.CheckAccess());
            Assert.Equal("playback.play", request.OperationId);
            entered.SetResult();
            await release.Task.WaitAsync(cancellationToken);
            return new(Value: ControlProtocol.Encode(new ControlGuiAction(true)));
        });
        ControlRequest request = new(Guid.NewGuid(), "playback.play", new(ControlProtocol.DefaultProfileId, sessionId),
            ExpectedRevision: null, ControlProtocol.Encode(new ControlGuiOperations.Empty()));

        Task<ControlResult> command = Task.Run(() => channel.DispatchAsync(sessionId, request, CancellationToken.None));
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(command.IsCompleted);
        }
        finally { release.SetResult(); }
        ControlResult result = await command.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Null(result.Error);
        Assert.True(result.Value!.Value.GetProperty("applied").GetBoolean());
    }

    [AvaloniaFact]
    public async Task Wrong_session_cannot_reach_the_native_player()
    {
        ControlGuiChannel channel = new((_, _) => throw new InvalidOperationException("The native handler must not run."));
        Guid sessionId = Guid.NewGuid();
        ControlRequest request = new(Guid.NewGuid(), "playback.play", new(ControlProtocol.DefaultProfileId, Guid.NewGuid()),
            ExpectedRevision: null, ControlProtocol.Encode(new ControlGuiOperations.Empty()));

        ControlResult result = await channel.DispatchAsync(sessionId, request, CancellationToken.None);
        Assert.Equal("session_mismatch", result.Error?.Code);
    }
}
