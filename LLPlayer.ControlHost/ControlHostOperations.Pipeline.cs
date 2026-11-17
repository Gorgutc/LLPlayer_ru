using System.Security.Cryptography;
using System.Text.Json;
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
        var files = request.Files.Select(file =>
            new PipelineFile(PhysicalFile(Absolute(file.MediaPath)), PhysicalFile(Absolute(file.OutputPath), output: true),
                file.StreamIndex, file.SourceRevision)).ToArray();
        var paths = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        if (files.Select(file => file.OutputPath).Distinct(paths).Count() != files.Length ||
            files.Select(file => file.OutputPath).Intersect(files.Select(file => file.MediaPath), paths).Any())
            throw new ControlJobException("output_conflict", "Outputs must be distinct and separate from all input files.");
        var saved = settings.CaptureWorkflowSettings();
        context.VerifyIdentity("recognition_settings", saved.RecognitionIdentity);
        ValidateTranslationSettings(context, saved.TranslationEnabled, saved.TranslationIdentity);
        context.Report("checking_inputs");
        foreach (var file in files)
            await context.VerifyInputAsync(file.MediaPath, ct, file.SourceRevision);

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
                context.Report($"recognizing_file_{index + 1}", (double)index / files.Length);
                var result = await runtime.Workflows.TranscribeAsync(file.MediaPath, file.StreamIndex, request.Engine,
                    TimeSpan.Zero, null, default, null, ct,
                    waiting => context.Report(waiting ? "waiting_for_gpu" : "recognizing",
                        status: waiting ? ControlJobStatus.WaitingForResource : ControlJobStatus.Running), saved.Config);
                await context.VerifyInputAsync(file.MediaPath, ct);
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
            await output.PublishAsync(context, file.OutputPath, request.Overwrite, ct, $"subtitle:{index}");
            var outputs = checkpoint.Outputs.Append(new PipelineOutput(file.OutputPath, output.ComputeSha256(),
                checkpoint.Translated.Cues.Length)).ToArray();
            checkpoint = new(index + 1, null, null, outputs);
            context.SaveCheckpoint(ControlProtocol.Encode(checkpoint));
        }
        return Done(checkpoint.Outputs);
    }

    private static ControlJobException ChangedOutput()
        => new("output_changed", "A completed output differs from its saved receipt.", ControlJobStatus.Interrupted);

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

    internal sealed record PipelineFile(string MediaPath, string OutputPath, int? StreamIndex = null,
        string? SourceRevision = null);
    internal sealed record PipelineRequest(IReadOnlyList<PipelineFile> Files, TargetLanguage TargetLanguage,
        SubtitleExportFormat Format = SubtitleExportFormat.Srt, SubASREngineType? Engine = null,
        string? SourceLanguage = null, bool Overwrite = false, bool Utf8Bom = true, bool SpeakerLabels = false);
    private sealed record PipelineCheckpoint(int Next, ControlCueDocument? Original, ControlCueDocument? Translated,
        IReadOnlyList<PipelineOutput> Outputs);
    private sealed record PipelineOutput(string Path, string Sha256, int Cues);
}
