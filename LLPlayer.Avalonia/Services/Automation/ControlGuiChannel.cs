using Avalonia.Threading;
using LLPlayer.ControlApi;

namespace LLPlayer.Avalonia.Services.Automation;

internal sealed class ControlGuiChannel(Func<ControlRequest, CancellationToken, Task<ControlResult>> execute,
    Action<long?>? requestApplicationExit = null)
{
    public Task<ControlResult> DispatchAsync(Guid sessionId, ControlRequest request, CancellationToken cancellationToken)
    {
        if (request.Scope.SessionId != sessionId)
            return Task.FromResult(ControlResult.Fail("session_mismatch", "The command targets a different GUI session."));
        return Dispatcher.UIThread.InvokeAsync(() => execute(request, cancellationToken));
    }

    public void AcknowledgeResult(ControlRequest request, ControlResult result)
    {
        if (request.OperationId == "app.exit" && result.Error == null && requestApplicationExit != null)
            Dispatcher.UIThread.Post(() => requestApplicationExit(request.ExpectedRevision));
    }
}

internal sealed class ControlGuiAcknowledgements
{
    public Guid? PendingDeliveryId { get; private set; }

    public bool Observe(ControlGuiCommand? delivery)
    {
        Guid? previous = PendingDeliveryId;
        PendingDeliveryId = delivery?.DeliveryId;
        return delivery != null && PendingDeliveryId != previous;
    }
}
