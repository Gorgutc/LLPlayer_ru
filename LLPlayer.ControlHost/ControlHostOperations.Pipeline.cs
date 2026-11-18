using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using FlyleafLib;
using FlyleafLib.MediaPlayer;
using FlyleafLib.MediaPlayer.Batch;
using FlyleafLib.MediaPlayer.Translation;
using LLPlayer.ControlApi;
using LLPlayer.Core.Automation;

namespace LLPlayer.ControlHost;

internal sealed partial class ControlHostOperations
{
    private async Task<ControlJobOutcome> RunPipelineAsync(ControlJobContext context, PipelineRequest request,
        CancellationToken ct)
    {
        if (request.Files.Count is < 1 or > 64 || request.Files.Any(file => file is null || file.StreamIndex is < 0))
            throw new ControlJobException("invalid_arguments", "Supply between 1 and 64 media files with valid stream indices.");
        var files = request.Files.Select((file, index) =>
        {
            var source = PhysicalSourcePath(Absolute(file.MediaPath), file.RequirePhysicalSource);
            var output = file.OutputPath is { } path ? PhysicalFile(Absolute(path), output: true)
                : InferPipelineSidecar(request, source, file.RequirePhysicalSource);
            return new { MediaPath = source, OutputPath = output, file.StreamIndex, file.SourceRevision, file.RequirePhysicalSource,
                FileIdentity = file.OutputPath is null ? $"sidecar_media:{index}" : null };
        }).ToArray();
        var paths = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        if (files.Select(file => file.OutputPath).Distinct(paths).Count() != files.Length ||
            files.Select(file => file.OutputPath).Intersect(files.Select(file => file.MediaPath), paths).Any())
            throw new ControlJobException("output_conflict", "Outputs must be distinct and separate from all input files.");
        var saved = settings.CaptureWorkflowSettings();
        context.VerifyIdentity("recognition_settings", saved.RecognitionIdentity);
        ValidateTranslationSettings(context, saved.TranslationEnabled, saved.TranslationIdentity);
        context.Report("checking_inputs");
        for (var index = 0; index < files.Length; index++)
        {
            var file = files[index];
            if (file.FileIdentity is not null)
                VerifySidecarPaths(context, index, file.MediaPath, file.OutputPath);
            await context.VerifyInputAsync(CheckedSourcePath(file.MediaPath, file.RequirePhysicalSource),
                ct, file.SourceRevision, file.FileIdentity);
        }

        var checkpoint = context.Checkpoint?.Deserialize<PipelineCheckpoint>(ControlProtocol.Json)
            ?? new PipelineCheckpoint(0, null, null, []);
        foreach (var completed in checkpoint.Outputs)
        {
            if (!File.Exists(completed.Path))
                throw ChangedOutput();
            await using var file = File.OpenRead(completed.Path);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(file, ct));
            if (!hash.Equals(completed.Sha256, StringComparison.OrdinalIgnoreCase))
                throw ChangedOutput();
        }
        for (var index = checkpoint.Next; index < files.Length; index++)
        {
            var file = files[index];
            if (checkpoint.Original is null)
            {
                if (file.FileIdentity is not null && File.Exists(file.OutputPath))
                    throw new ControlJobException("output_exists", "The Russian SRT sidecar already exists.");
                context.Report($"recognizing_file_{index + 1}", (double)index / files.Length);
                var result = await runtime.Workflows.TranscribeAsync(CheckedSourcePath(file.MediaPath, file.RequirePhysicalSource),
                    file.StreamIndex, request.Engine,
                    TimeSpan.Zero, null, default, null, ct,
                    waiting => context.Report(waiting ? "waiting_for_gpu" : "recognizing",
                        status: waiting ? ControlJobStatus.WaitingForResource : ControlJobStatus.Running), saved.Config);
                await context.VerifyInputAsync(CheckedSourcePath(file.MediaPath, file.RequirePhysicalSource),
                    ct, identityKey: file.FileIdentity);
                var original = ControlCueDocument.Capture(Guid.NewGuid(), $"{file.MediaPath}|{file.StreamIndex}|{saved.RecognitionIdentity}",
                    result.SourceLanguage.ISO6391, result.Subtitles);
                checkpoint = checkpoint with { Original = original };
                context.SaveCheckpoint(ControlProtocol.Encode(checkpoint));
            }
            if (checkpoint.Translated is null)
            {
                var translated = await TranslateDocumentAsync(context, checkpoint.Original, request.TargetLanguage,
                    request.SourceLanguage, saved.Config, ct);
                checkpoint = checkpoint with { Translated = translated };
                context.SaveCheckpoint(ControlProtocol.Encode(checkpoint));
            }

            context.Report($"exporting_file_{index + 1}", (double)index / files.Length);
            var output = ControlSubtitleExport.Prepare(checkpoint.Translated, request.Format, ControlSubtitleText.TranslatedOnly,
                request.Utf8Bom, request.SpeakerLabels);
            if (file.FileIdentity is not null || file.RequirePhysicalSource)
            {
                var currentSource = PhysicalSourcePath(Absolute(request.Files[index].MediaPath), file.RequirePhysicalSource);
                if (file.FileIdentity is not null)
                    VerifySidecarPaths(context, index, currentSource, RussianSidecarPath(currentSource, file.RequirePhysicalSource));
                await context.VerifyInputAsync(currentSource, ct, file.SourceRevision, file.FileIdentity);
            }
            Action<string>? validatePublication = file.RequirePhysicalSource && file.FileIdentity is not null
                ? stage => ValidateGuardedSidecar(file.MediaPath, file.OutputPath, stage) : null;
            await output.PublishAsync(context, file.OutputPath, request.Overwrite, ct, $"subtitle:{index}", validatePublication);
            var outputs = checkpoint.Outputs.Append(new PipelineOutput(file.OutputPath, output.ComputeSha256(),
                checkpoint.Translated.Cues.Length)).ToArray();
            checkpoint = new(index + 1, null, null, outputs);
            context.SaveCheckpoint(ControlProtocol.Encode(checkpoint));
        }
        return Done(checkpoint.Outputs);
    }

    private static ControlJobException ChangedOutput()
        => new("output_changed", "A completed output differs from its saved receipt.", ControlJobStatus.Interrupted);

    private static string InferPipelineSidecar(PipelineRequest request, string source, bool requirePhysicalSource)
    {
        if (request.TargetLanguage != TargetLanguage.Russian || request.Format != SubtitleExportFormat.Srt
            || request.Overwrite)
            throw new ControlJobException("invalid_arguments",
                "Omit outputPath only for Russian SRT output with overwrite disabled.");
        return RussianSidecarPath(source, requirePhysicalSource);
    }

    private static string RussianSidecarPath(string source, bool requirePhysicalSource)
    {
        var sidecar = SubtitleOutputPathBuilder.BuildRussianSrtPath(source);
        return requirePhysicalSource ? CheckedOutputPath(sidecar) : PhysicalFile(sidecar, output: true);
    }

    private static void ValidateGuardedSidecar(string source, string destination, string stage)
    {
        CheckedSourcePath(source, true);
        if (!string.Equals(Path.GetDirectoryName(destination), Path.GetDirectoryName(stage), StringComparison.Ordinal))
            throw new ControlJobException("file_effect_conflict", "The staging file left the physical output directory.");
        CheckedOutputPath(destination);
        CheckedOutputPath(stage);
    }

    private static string CheckedOutputPath(string path)
    {
        try
        {
            if (ControlPaths.IsPhysicalPath(path, allowMissingLeaf: true))
                return path;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
        }
        throw new ControlJobException("output_conflict", "The guarded subtitle output must use a physical path.");
    }

    private static void VerifySidecarPaths(ControlJobContext context, int index, string source, string output)
    {
        // A Windows directory can be case-sensitive. Retain the exact resolved names for each item.
        context.VerifyIdentity($"sidecar_source:{index}", source);
        context.VerifyIdentity($"sidecar_destination:{index}", output);
    }

    private static string PhysicalSourcePath(string path, bool requirePhysicalSource)
        => requirePhysicalSource ? CheckedSourcePath(path, true) : PhysicalFile(path);

    private static string CheckedSourcePath(string path, bool requirePhysicalSource)
    {
        if (!requirePhysicalSource)
            return path;
        try
        {
            if (ControlPaths.IsPhysicalPath(path) && string.Equals(path, PhysicalFile(path), StringComparison.Ordinal))
                return path;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw new ControlException(new("source_unavailable", "The source file is unavailable.", true));
        }
        throw new ControlException(new("source_path_changed", "The source path contains a link or reparse point."));
    }

    private static string PhysicalFile(string path, bool output = false)
    {
        var directory = Path.GetDirectoryName(path)
            ?? throw new ControlJobException("invalid_path", "A file path is required.");
        var file = new FileInfo(Path.Combine(ControlPaths.ResolveDirectory(directory), Path.GetFileName(path)));
        if (output && file.LinkTarget is not null)
            throw new ControlJobException("output_conflict", "The output filename must not be a symbolic link.");
        return file.LinkTarget is not null && file.ResolveLinkTarget(true) is FileInfo target
            ? target.FullName : file.FullName;
    }

    internal sealed record PipelineFile(string MediaPath, string? OutputPath = null, int? StreamIndex = null,
        string? SourceRevision = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool RequirePhysicalSource = false);
    internal sealed record PipelineRequest(IReadOnlyList<PipelineFile> Files, TargetLanguage TargetLanguage,
        SubtitleExportFormat Format = SubtitleExportFormat.Srt, SubASREngineType? Engine = null,
        string? SourceLanguage = null, bool Overwrite = false, bool Utf8Bom = true, bool SpeakerLabels = false);
    private sealed record PipelineCheckpoint(int Next, ControlCueDocument? Original, ControlCueDocument? Translated,
        IReadOnlyList<PipelineOutput> Outputs);
    private sealed record PipelineOutput(string Path, string Sha256, int Cues);
}
