namespace LLPlayer.ControlApi;

public interface IPlayerControlService
{
    Task<ControlCapabilities> DescribeAsync(ControlScope scope, CancellationToken cancellationToken = default);
    Task<ControlState> GetStateAsync(ControlScope scope, CancellationToken cancellationToken = default);
    Task<ControlResult> InvokeAsync(ControlRequest request, CancellationToken cancellationToken = default);
}
