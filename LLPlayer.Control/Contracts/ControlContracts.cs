using System.Text.Json;
using System.Text.Json.Serialization;

namespace LLPlayer.ControlApi;

public static class ControlProtocol
{
    public const int Version = 1;
    public const string DefaultProfileId = "default";
    public const string GuiLaunchIdVariable = "LLPLAYER_CONTROL_GUI_LAUNCH_ID";

    public static JsonSerializerOptions Json { get; } = CreateOptions();

    public static JsonElement Encode<T>(T value) => JsonSerializer.SerializeToElement(value, Json);

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = false,
            NumberHandling = JsonNumberHandling.Strict,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            RespectNullableAnnotations = true,
            RespectRequiredConstructorParameters = true,
            MaxDepth = 32
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}

public sealed record ControlScope(string ProfileId, Guid? SessionId = null);
public sealed record ControlRequest(
    Guid RequestId, string OperationId, ControlScope Scope, long? ExpectedRevision, JsonElement Arguments);
public sealed record ControlError(string Code, string Message, bool Retryable = false);
public sealed record ControlResult(
    Guid? JobId = null, long? Revision = null, JsonElement? Value = null, ControlError? Error = null)
{
    public static ControlResult Fail(string code, string message, bool retryable = false)
        => new(Error: new(code, message, retryable));
}

public enum ControlOperationCompletion { Immediate, Job }
public enum ControlOperationContext { Host, Gui }

public sealed record ControlOperation(
    string Id, string Family, string Description, bool Mutates = false,
    ControlOperationCompletion Completion = ControlOperationCompletion.Immediate,
    ControlOperationContext Context = ControlOperationContext.Host,
    bool WindowsOnly = false);

public sealed record ControlCapability(
    ControlOperation Operation, JsonElement InputSchema, JsonElement ResultSchema,
    bool Supported, bool Available, string? Reason);

public sealed record ControlCapabilities(int ApiVersion, IReadOnlyList<ControlCapability> Operations);
public sealed record ControlGuiInfo(Guid SessionId, string Name, DateTimeOffset LastSeen);
public sealed record ControlState(Guid InstanceId, string ProfileId, IReadOnlyList<ControlGuiInfo> Sessions,
    IReadOnlyList<ControlJob> Jobs);

public sealed record ControlGuiRegistration(string ProfileId, string Name, int? ProcessId = null, Guid? LaunchId = null,
    bool? LaunchSourceOpened = null);
public sealed record ControlGuiSession(Guid SessionId, string OwnerToken);
public sealed record ControlGuiPoll(string OwnerToken, Guid? AcknowledgeDeliveryId = null);
public sealed record ControlGuiCommand(Guid CommandId, Guid DeliveryId, ControlRequest Request, bool Cancel = false);
public sealed record ControlGuiCompletion(string OwnerToken, Guid CommandId, ControlResult Result);
public sealed record ControlDiscovery(int ApiVersion, Guid InstanceId, int ProcessId, string ProfileId,
    string ConfigDirectory, string Address, string Credential);
public sealed record ControlHostIdentity(int ApiVersion, Guid InstanceId, int ProcessId, string StateDirectory);

public sealed class ControlException(ControlError error) : Exception(error.Message)
{
    public ControlError Error { get; } = error;
}
