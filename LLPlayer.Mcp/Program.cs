using System.Text.Json;
using System.Text.Json.Nodes;
using LLPlayer.ControlApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace LLPlayer.Mcp;

internal static class Program
{
    private static readonly ControlScope DefaultScope = new(ControlProtocol.DefaultProfileId);
    private static readonly JsonElement ResultSchema = ControlProtocol.Encode(new { type = "object" });
    private static readonly JsonElement ScopeSchema = ScopeInput();
    private static readonly JsonElement JobSchema = JobInput();

    public static async Task Main()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddMcpServer(options =>
        {
            options.ServerInfo = new() { Name = "LLPlayer", Version = "1.0" };
        }).WithStdioServerTransport().WithListToolsHandler(ListAsync).WithCallToolHandler(CallAsync);
        using var host = builder.Build();
        await host.RunAsync();
    }

    private static async ValueTask<ListToolsResult> ListAsync(
        RequestContext<ListToolsRequestParams> request, CancellationToken ct)
    {
        var tools = new List<Tool>
        {
            CreateTool("llplayer_capabilities", "Read current LLPlayer capabilities and prerequisites.", ScopeSchema),
            CreateTool("llplayer_state", "Read the active profile, attached windows and recent jobs.", ScopeSchema),
            CreateTool("llplayer_jobs_list", "List jobs with an optional pagination cursor.", JobListInput()),
            CreateTool("llplayer_job_get", "Read the stage, progress, result or error of a job.", JobSchema),
            CreateTool("llplayer_job_cancel", "Request cancellation of a job.", JobSchema, true),
            CreateTool("llplayer_job_retry", "Explicitly retry a failed job.", JobSchema, true),
            CreateTool("llplayer_job_resume", "Explicitly continue an interrupted job.", JobSchema, true),
            CreateTool("llplayer_host_stop", "Drain jobs and stop the running companion.", EmptyInput(), true),
            CreateTool("llplayer_host_restart", "Drain jobs and request a new companion instance.", EmptyInput(), true)
        };
        try
        {
            using var client = await ConnectAsync(ct);
            var description = await client.DescribeAsync(DefaultScope, ct);
            foreach (var capability in description.Operations)
            {
                var operation = capability.Operation;
                tools.Add(CreateTool(Name(operation.Id), operation.Description,
                    ScopeInput(capability.InputSchema, operation.Mutates), operation.Mutates));
            }
        }
        catch (Exception exception) when (exception is ControlException or HttpRequestException)
        {
            // Base tools remain discoverable while the host is starting or unavailable.
        }
        return new ListToolsResult { Tools = tools };
    }

    private static async ValueTask<CallToolResult> CallAsync(
        RequestContext<CallToolRequestParams> request, CancellationToken ct)
    {
        try
        {
            var call = request.Params ?? throw new JsonException();
            var arguments = ControlProtocol.Encode(call.Arguments ?? new Dictionary<string, JsonElement>());
            using var client = call.Name is "llplayer_host_stop" or "llplayer_host_restart"
                ? await ConnectExistingAsync(ct) : await ConnectAsync(ct);
            var scope = new ControlScope(ControlProtocol.DefaultProfileId, OptionalGuid(arguments, "sessionId"));
            return call.Name switch
            {
                "llplayer_capabilities" => Reply(await client.DescribeAsync(scope, ct)),
                "llplayer_state" => Reply(await client.GetStateAsync(scope, ct)),
                "llplayer_jobs_list" => Reply(await client.ListAsync(scope,
                    Field(arguments, "cursor")?.GetString(), Field(arguments, "limit")?.GetInt32() ?? 50, ct)),
                "llplayer_job_get" => Reply(await client.GetAsync(scope, RequiredGuid(arguments, "jobId"), ct)
                    is { } job ? new(Value: ControlProtocol.Encode(job))
                    : ControlResult.Fail("job_not_found", "No job exists in this scope.")),
                "llplayer_job_cancel" => Reply(await client.CancelAsync(JobAction(arguments, scope), ct)),
                "llplayer_job_retry" => Reply(await client.RetryAsync(JobAction(arguments, scope), ct)),
                "llplayer_job_resume" => Reply(await client.ResumeAsync(JobAction(arguments, scope), ct)),
                "llplayer_host_stop" => await HostLifecycleAsync(client.StopHostAsync, ct),
                "llplayer_host_restart" => await HostLifecycleAsync(client.RequestHostRestartAsync, ct),
                _ => Reply(await InvokeAsync(client, call.Name, scope, arguments, ct))
            };
        }
        catch (ControlException exception)
        {
            return Reply(new ControlResult(Error: exception.Error));
        }
        catch (HttpRequestException)
        {
            return Reply(ControlResult.Fail("host_unavailable", "The local API could not be reached or rejected the request.", true));
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException)
        {
            return Reply(ControlResult.Fail("invalid_arguments", "Supply values matching the tool's input schema."));
        }
    }

    private static async Task<ControlResult> InvokeAsync(
        ControlClient client, string name, ControlScope scope, JsonElement arguments, CancellationToken ct)
    {
        var capabilities = await client.DescribeAsync(scope, ct);
        var selected = capabilities.Operations.FirstOrDefault(item => Name(item.Operation.Id) == name);
        if (selected is null)
            return ControlResult.Fail("unknown_operation", "The tool is not in this host's catalog.");

        var input = Field(arguments, "input") ?? throw new JsonException();
        var requestId = selected.Operation.Mutates ? RequiredGuid(arguments, "requestId")
            : OptionalGuid(arguments, "requestId") ?? Guid.NewGuid();
        var revision = Field(arguments, "expectedRevision")?.GetInt64();
        return await client.InvokeAsync(new(requestId, selected.Operation.Id, scope, revision, input), ct);
    }

    private static Task<ControlClient> ConnectAsync(CancellationToken ct)
    {
        var (config, state) = ControlPaths.DefaultAppDirectories();
        return ControlClient.ConnectOrStartAsync(config, state, ct);
    }

    private static Task<ControlClient> ConnectExistingAsync(CancellationToken ct)
    {
        var (config, state) = ControlPaths.DefaultAppDirectories();
        return ControlClient.ConnectExistingAsync(config, state, ct);
    }

    private static async Task<CallToolResult> HostLifecycleAsync(
        Func<CancellationToken, Task> command, CancellationToken ct)
    {
        await command(ct);
        return Reply(new { accepted = true });
    }

    private static JsonElement EmptyInput() => ObjectSchema(new JsonObject());

    private static string Name(string operationId) => "llplayer_" + operationId.Replace('.', '_');

    private static Tool CreateTool(string name, string description, JsonElement schema, bool mutates = false)
        => new()
        {
            Name = name,
            Description = description,
            InputSchema = schema,
            OutputSchema = ResultSchema,
            Annotations = new() { ReadOnlyHint = !mutates }
        };

    private static JsonElement ScopeInput(JsonElement? input = null, bool mutates = false)
    {
        var fields = new JsonObject { ["sessionId"] = new JsonObject { ["type"] = "string", ["format"] = "uuid" } };
        if (input is not { } schema)
            return ObjectSchema(fields);
        fields["requestId"] = new JsonObject { ["type"] = "string", ["format"] = "uuid" };
        fields["expectedRevision"] = new JsonObject { ["type"] = "integer" };
        fields["input"] = JsonNode.Parse(schema.GetRawText());
        return mutates ? ObjectSchema(fields, "input", "requestId") : ObjectSchema(fields, "input");
    }

    private static JsonElement JobInput()
        => ObjectSchema(new JsonObject
        {
            ["sessionId"] = new JsonObject { ["type"] = "string", ["format"] = "uuid" },
            ["jobId"] = new JsonObject { ["type"] = "string", ["format"] = "uuid" },
            ["acknowledgeUnknownEffect"] = new JsonObject { ["type"] = "boolean" }
        }, "jobId");

    private static JsonElement JobListInput()
        => ObjectSchema(new JsonObject
        {
            ["sessionId"] = new JsonObject { ["type"] = "string", ["format"] = "uuid" },
            ["cursor"] = new JsonObject { ["type"] = "string" },
            ["limit"] = new JsonObject { ["type"] = "integer", ["minimum"] = 1, ["maximum"] = 100 }
        });

    private static JsonElement ObjectSchema(JsonObject properties, params string[] required)
        => ControlProtocol.Encode(new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["properties"] = properties,
            ["required"] = new JsonArray(required.Select(value => (JsonNode?)JsonValue.Create(value)).ToArray())
        });

    private static Guid RequiredGuid(JsonElement body, string name)
        => OptionalGuid(body, name) ?? throw new JsonException();

    private static Guid? OptionalGuid(JsonElement body, string name) => Field(body, name)?.GetGuid();

    private static JsonElement? Field(JsonElement body, string name)
        => body.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null ? value : null;

    private static ControlJobAction JobAction(JsonElement body, ControlScope scope)
        => new(RequiredGuid(body, "jobId"), scope, Field(body, "acknowledgeUnknownEffect")?.GetBoolean() ?? false);

    private static CallToolResult Reply<T>(T value)
    {
        var json = ControlProtocol.Encode(value);
        return new CallToolResult
        {
            IsError = value is ControlResult { Error: not null },
            StructuredContent = json,
            Content = [new TextContentBlock { Text = json.GetRawText() }]
        };
    }
}
