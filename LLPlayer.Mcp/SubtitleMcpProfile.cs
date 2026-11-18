using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Schema;
using LLPlayer.ControlApi;
using ModelContextProtocol.Protocol;

namespace LLPlayer.Mcp;

internal sealed class SubtitleMcpProfile(string root)
{
    private const string Probe = "media.probe";
    private const string Recognize = "recognition.start";
    private const string Read = "translation.document.read";
    private const string Apply = "translation.document.apply";
    private const string Commit = "translation.document.commit";
    private const string Export = "export.subtitles";
    private const string Workflow = "workflow.create_subtitles";
    private const string GetJob = "llplayer_subtitle_job_get";
    private static readonly ControlScope Scope = new(ControlProtocol.DefaultProfileId);
    private static readonly JsonSchemaExporterOptions SchemaOptions = new() { TreatNullObliviousAsNonNullable = true };
    private static readonly IReadOnlyDictionary<string, Operation> Operations = new Operation[]
    {
        new(Probe, typeof(ProbeInput), "Probe a media file inside the approved library and return its source revision."),
        new(Recognize, typeof(RecognitionInput), "Recognize the probed media and prepare a draft for external Russian translation.", true),
        new(Read, typeof(DraftReadInput), "Read a page of the Russian translation draft created by this bridge."),
        new(Apply, typeof(DraftApplyInput), "Submit translations with the original cue IDs.", true),
        new(Commit, typeof(DraftInput), "Commit the complete Russian translation at its current revision.", true),
        new(Export, typeof(DraftInput), "Save the committed Russian SRT beside its source video without overwriting.", true),
        new(Workflow, typeof(WorkflowInput), "Use the configured translator and save a Russian SRT beside each probed video without overwriting.", true),
    }.ToDictionary(operation => ToolName(operation.Id), StringComparer.Ordinal);

    private readonly ConcurrentDictionary<Guid, string> jobs = new();

    public static SubtitleMcpProfile? FromEnvironment()
    {
        var configured = Environment.GetEnvironmentVariable("LLPLAYER_SUBTITLE_ROOT");
        if (configured is null)
            return null;
        if (!Path.IsPathFullyQualified(configured))
            throw new InvalidOperationException("LLPLAYER_SUBTITLE_ROOT must name an existing absolute directory.");
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(configured));
        RequirePhysicalPath(root);
        if (!Directory.Exists(root))
            throw new InvalidOperationException("LLPLAYER_SUBTITLE_ROOT must name an existing absolute directory.");
        return new(root);
    }

    public ListToolsResult ListTools()
    {
        var tools = Operations.Values.Select(operation => Program.CreateTool(
            ToolName(operation.Id), operation.Description,
            Program.ScopeInput(Schema(operation.InputType), operation.Mutates, includeSession: false),
            operation.Mutates)).ToList();
        tools.Add(Program.CreateTool(GetJob, "Read a job accepted by this subtitle bridge.", Schema(typeof(JobInput))));
        return new() { Tools = tools };
    }

    public async Task<ControlResult> CallAsync(string name, JsonElement arguments,
        Func<CancellationToken, Task<ControlClient>> connect, CancellationToken ct)
    {
        if (name == GetJob)
        {
            var jobId = Decode<JobInput>(arguments).JobId;
            RequireJob(jobId);
            using var client = await connect(ct);
            var job = await client.GetAsync(Scope, jobId, ct);
            return job is null ? JobNotFound() : new(Value: ControlProtocol.Encode(job));
        }
        if (!Operations.TryGetValue(name, out var operation))
            return ControlResult.Fail("unknown_operation", "The tool is unavailable in the subtitle-only profile.");

        var invocation = Decode<Invocation>(arguments);
        var requestId = invocation.RequestId ?? Guid.Empty;
        if (operation.Mutates && requestId == Guid.Empty)
            throw new JsonException();

        var input = Prepare(operation.Id, invocation.Input);
        using var host = await connect(ct);
        var result = await host.InvokeAsync(new(operation.Mutates ? ScopeRequestId(requestId) : Guid.NewGuid(),
            operation.Id, Scope, invocation.ExpectedRevision, ControlProtocol.Encode(input)), ct);
        if (result.Error is null && result.JobId is { } accepted && operation.Id is Recognize or Export or Workflow)
            jobs.TryAdd(accepted, operation.Id);
        return result;
    }

    private object Prepare(string operation, JsonElement input)
    {
        switch (operation)
        {
            case Probe:
                var probe = Decode<ProbeInput>(input);
                return new { mediaPath = ResolveMedia(probe.MediaPath), probe.StreamIndex, requirePhysicalSource = true };
            case Recognize:
                var recognition = Decode<RecognitionInput>(input);
                return new
                {
                    mediaPath = ResolveMedia(recognition.MediaPath),
                    sourceRevision = SourceRevision(recognition.SourceRevision),
                    recognition.StreamIndex,
                    recognition.SourceLanguage,
                    recognition.Engine,
                    externalTargetLanguage = "ru",
                    requirePhysicalSource = true,
                };
            case Workflow:
                var workflow = Decode<WorkflowInput>(input);
                if (workflow.Files.Count is < 1 or > 64 || workflow.Files.Any(file => file is null))
                    throw new JsonException();
                return new
                {
                    files = workflow.Files.Select(file => new
                    {
                        mediaPath = ResolveMedia(file.MediaPath),
                        sourceRevision = SourceRevision(file.SourceRevision),
                        file.StreamIndex,
                        requirePhysicalSource = true,
                    }).ToArray(),
                    sourceLanguage = workflow.SourceLanguage,
                    engine = workflow.Engine,
                    targetLanguage = "russian",
                    format = "srt",
                };
            case Read:
                return RequireRecognition(Decode<DraftReadInput>(input));
            case Apply:
                return RequireRecognition(Decode<DraftApplyInput>(input));
            case Commit:
                return RequireRecognition(Decode<DraftInput>(input));
            case Export:
                var document = RequireRecognition(Decode<DraftInput>(input));
                return new { document.JobId, document.Revision, format = "srt", text = "translatedOnly" };
            default:
                throw new InvalidOperationException();
        }
    }

    private T RequireRecognition<T>(T input) where T : DraftInput
    {
        RequireJob(input.JobId, recognitionOnly: true);
        return input;
    }

    private void RequireJob(Guid id, bool recognitionOnly = false)
    {
        if (!jobs.TryGetValue(id, out var operation) || (recognitionOnly && operation != Recognize))
            throw new ControlException(JobNotFound().Error!);
    }

    private static ControlResult JobNotFound()
        => ControlResult.Fail("job_not_found", "This subtitle bridge did not accept the job. Retry the original request ID after reconnecting.");

    private string ResolveMedia(string path)
    {
        if (!Path.IsPathFullyQualified(path) || (OperatingSystem.IsWindows() && Path.GetFileName(path).Contains(':')))
            throw new ControlException(new("invalid_path", "An absolute media file path is required."));
        try
        {
            var full = Path.GetFullPath(path);
            if (!IsWithin(root, full))
                throw OutsideRoot();
            RequirePhysicalPath(full);
            if (File.Exists(full))
                return full;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Filesystem exceptions can contain paths outside the approved library.
        }
        throw new ControlException(new("source_unavailable", "The media file is unavailable."));
    }

    private static void RequirePhysicalPath(string path)
    {
        if (!ControlPaths.IsPhysicalPath(path))
            throw OutsideRoot();
    }

    private static bool IsWithin(string directory, string path)
    {
        // NTFS can contain distinct names that differ only in case.
        var prefix = Path.EndsInDirectorySeparator(directory) ? directory : directory + Path.DirectorySeparatorChar;
        return path.Length > prefix.Length && path.StartsWith(prefix, StringComparison.Ordinal);
    }

    private static ControlException OutsideRoot() => new(new("source_outside_root",
        "Select a media file inside the approved library without links or reparse points."));

    private static string ToolName(string operation) => "llplayer_subtitle_" + operation.Replace('.', '_');

    private static string SourceRevision(string revision)
        => revision.Length == 64 && revision.All(char.IsAsciiHexDigit) ? revision.ToUpperInvariant() : throw new JsonException();

    private Guid ScopeRequestId(Guid requestId)
    {
        // Keep retry IDs stable across reconnects and specific to the exact approved root.
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes($"llplayer-subtitle-profile-v1\n{root}\n{requestId:D}"));
        return new(digest.AsSpan(0, 16));
    }

    private static JsonElement Schema(Type type)
        => ControlProtocol.Encode(McpInputSchema.Project(ControlProtocol.Json.GetJsonSchemaAsNode(type, SchemaOptions).AsObject()));

    private static T Decode<T>(JsonElement input) where T : class
        => input.Deserialize<T>(ControlProtocol.Json) ?? throw new JsonException();

    private sealed record Operation(string Id, Type InputType, string Description, bool Mutates = false);
    private sealed record Invocation(JsonElement Input, Guid? RequestId = null, long? ExpectedRevision = null);
    private sealed record JobInput(Guid JobId);
    private sealed record ProbeInput(string MediaPath, int? StreamIndex = null);
    private sealed record RecognitionInput(string MediaPath, string SourceRevision, int? StreamIndex = null,
        string? SourceLanguage = null, string? Engine = null);
    private sealed record WorkflowFile(string MediaPath, string SourceRevision, int? StreamIndex = null);
    private sealed record WorkflowInput(IReadOnlyList<WorkflowFile> Files, string? SourceLanguage = null, string? Engine = null);
    private record DraftInput(Guid JobId, string Revision);
    private sealed record DraftReadInput(Guid JobId, string Revision, int Offset = 0, int Limit = 64) : DraftInput(JobId, Revision);
    private sealed record DraftApplyInput(Guid JobId, string Revision, IReadOnlyList<CueTranslation> Cues) : DraftInput(JobId, Revision);
    private sealed record CueTranslation(Guid CueId, string Text);
}
