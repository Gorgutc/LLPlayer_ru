using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Schema;
using LLPlayer.ControlApi;

namespace LLPlayer.Core.Automation;

public sealed record ControlInvocation(ControlRequest Request);

public sealed class ControlOperationCatalog
{
    private static readonly JsonSchemaExporterOptions SchemaOptions = new()
    {
        TreatNullObliviousAsNonNullable = true
    };

    private readonly Dictionary<string, Entry> operations = new(StringComparer.Ordinal);

    public void Add<TArgs, TResult>(
        ControlOperation operation,
        Func<ControlInvocation, TArgs, CancellationToken, Task<TResult>> handler,
        Func<ControlScope, string?>? availability = null)
        where TArgs : class
        => Add(operation, typeof(TArgs), typeof(TResult), async (request, args, cancellationToken) =>
        {
            var result = await handler(new(request), (TArgs)args, cancellationToken).ConfigureAwait(false);
            return result is ControlResult response ? response : new(Value: ControlProtocol.Encode(result));
        }, availability);

    public void Add(ControlOperation operation, Type arguments, Type result,
        Func<ControlRequest, object, CancellationToken, Task<ControlResult>> handler,
        Func<ControlScope, string?>? availability = null, bool requireAvailabilityAtInvocation = true)
    {
        if (string.IsNullOrWhiteSpace(operation.Id) || string.IsNullOrWhiteSpace(operation.Family))
            throw new ArgumentException("An operation ID and family are required.", nameof(operation));

        operations.Add(operation.Id, new(
            operation, arguments, DescribeType(arguments), DescribeType(result), handler, availability,
            requireAvailabilityAtInvocation));
    }

    public ControlCapabilities Describe(ControlScope scope)
        => new(ControlProtocol.Version, operations.Values.OrderBy(entry => entry.Operation.Id, StringComparer.Ordinal)
            .Select(entry =>
            {
                var reason = GetUnavailableReason(entry, scope);
                return new ControlCapability(entry.Operation, entry.ArgumentsSchema, entry.ResultSchema,
                    reason != "unsupported_os", reason is null, reason);
            }).ToArray());

    public async Task<ControlResult> InvokeAsync(ControlRequest request, CancellationToken cancellationToken = default)
    {
        if (request.RequestId == Guid.Empty || string.IsNullOrWhiteSpace(request.OperationId)
            || request.Scope is null || string.IsNullOrWhiteSpace(request.Scope.ProfileId))
            return ControlResult.Fail("invalid_request", "A request ID, operation ID, and profile are required.");
        if (!operations.TryGetValue(request.OperationId, out var entry))
            return ControlResult.Fail("unknown_operation", "The operation is not in this host's catalog.");
        if (entry.RequireAvailabilityAtInvocation && GetUnavailableReason(entry, request.Scope) is { } reason)
            return ControlResult.Fail(reason, "The operation is unavailable in the requested scope.");

        object? arguments;
        try
        {
            arguments = request.Arguments.ValueKind == JsonValueKind.Object
                ? request.Arguments.Deserialize(entry.ArgumentsType, ControlProtocol.Json) : null;
            if (arguments is null || !Validator.TryValidateObject(arguments, new(arguments), null, true))
                return InvalidArguments();
        }
        catch (JsonException)
        {
            return InvalidArguments();
        }

        try
        {
            return await entry.Handler(request, arguments, cancellationToken).ConfigureAwait(false);
        }
        catch (ControlException exception)
        {
            return new(Error: exception.Error);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Engine, provider and validation exceptions can contain credentials or private details.
            return ControlResult.Fail("operation_failed", "The operation failed. See its stage for more information.");
        }
    }

    private static string? GetUnavailableReason(Entry entry, ControlScope scope)
        => entry.Operation.WindowsOnly && !OperatingSystem.IsWindows()
            ? "unsupported_os" : entry.Availability?.Invoke(scope);

    private static JsonElement DescribeType(Type type)
        => ControlProtocol.Encode(ControlProtocol.Json.GetJsonSchemaAsNode(type, SchemaOptions));

    private static ControlResult InvalidArguments()
        => ControlResult.Fail("invalid_arguments", "Arguments must match the advertised operation schema.");

    private sealed record Entry(
        ControlOperation Operation, Type ArgumentsType, JsonElement ArgumentsSchema, JsonElement ResultSchema,
        Func<ControlRequest, object, CancellationToken, Task<ControlResult>> Handler,
        Func<ControlScope, string?>? Availability, bool RequireAvailabilityAtInvocation);
}
