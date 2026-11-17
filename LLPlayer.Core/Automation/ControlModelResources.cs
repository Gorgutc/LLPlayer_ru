using FlyleafLib;
using LLPlayer.ControlApi;
using LLPlayer.Core.Services;
using LLPlayer.Core.Services.Models;

namespace LLPlayer.Core.Automation;

/// <summary>Model catalog and explicit mutations under the same lease as host recognition work.</summary>
public sealed class ControlModelResources(Func<Config> captureConfig, ModelStoragePaths defaultPaths,
    LanguageResourceCoordinator resources, IEngineArchiveExtractor? extractor = null) : IDisposable
{
    readonly HttpClient downloads = new() { Timeout = TimeSpan.FromMinutes(30) };
    readonly SemaphoreSlim mutation = new(1, 1);

    public IReadOnlyList<ModelEntry> List(ModelKind kind, string? whisperDirectory = null)
        => CreateManager(whisperDirectory).Discover(kind);
    public string GetDirectory(ModelKind kind, string? whisperDirectory = null)
        => CreateManager(whisperDirectory).GetDirectory(kind);

    public Task DownloadAsync(string id, IProgress<ModelDownloadProgress>? progress, CancellationToken token,
        string? whisperDirectory = null) =>
        MutateAsync(manager => manager.DownloadAsync(id, progress, token), token, whisperDirectory);

    public Task RemoveAsync(string id, CancellationToken token, string? whisperDirectory = null) =>
        MutateAsync(manager => manager.DeleteAsync(id, token), token, whisperDirectory);

    async Task MutateAsync(Func<ModelManagementService, Task> action, CancellationToken token, string? whisperDirectory)
    {
        if (!await mutation.WaitAsync(0, token).ConfigureAwait(false))
            throw new InvalidOperationException("Another model operation is active.");
        try { await action(CreateManager(whisperDirectory)).ConfigureAwait(false); }
        finally { mutation.Release(); }
    }

    ModelManagementService CreateManager(string? whisperDirectory)
    {
        if (whisperDirectory != null && !Path.IsPathFullyQualified(whisperDirectory))
            throw new ArgumentException("Use an absolute Whisper model directory.", nameof(whisperDirectory));
        string? directory = whisperDirectory ?? captureConfig().Subtitles.WhisperCppConfig.ModelDirectory;
        var paths = !string.IsNullOrWhiteSpace(directory) && Path.IsPathFullyQualified(directory)
            ? defaultPaths with { WhisperModels = directory }
            : defaultPaths;
        return new ModelManagementService(downloads, paths, IsSelected, CommitAsync, extractor);
    }

    bool IsSelected(ModelEntry model)
    {
        if (!model.Present) return false;
        Config.SubtitlesConfig current = captureConfig().Subtitles;
        return model.Kind switch
        {
            ModelKind.Whisper => current.WhisperCppConfig.Model != null &&
                (BatchWorkflowPathEquals(current.WhisperCppConfig.GetModelFilePath(), model.FullPath) ||
                    (string.IsNullOrWhiteSpace(current.WhisperCppConfig.ModelDirectory) &&
                        BatchWorkflowPathEquals(Path.Combine(defaultPaths.WhisperModels, current.WhisperCppConfig.Model.ModelFileName), model.FullPath))),
            ModelKind.FasterWhisperEngine => current.ASREngine == SubASREngineType.FasterWhisper,
            _ => false,
        };
    }

    static bool BatchWorkflowPathEquals(string first, string second) =>
        LLPlayer.Core.Batch.BatchWorkflow.PathComparer.Equals(ResolveFile(first), ResolveFile(second));

    static string ResolveFile(string path)
    {
        string full = Path.GetFullPath(path);
        string directory = ControlPaths.ResolveDirectory(Path.GetDirectoryName(full)!);
        var file = new FileInfo(Path.Combine(directory, Path.GetFileName(full)));
        return file.LinkTarget != null && file.ResolveLinkTarget(returnFinalTarget: true) is { } target
            ? target.FullName : file.FullName;
    }

    async Task CommitAsync(ModelEntry model, Func<Task> commit, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using IDisposable lease = await resources.WaitForMutationAsync(token).ConfigureAwait(false);
        await commit().ConfigureAwait(false);
    }

    public void Dispose()
    {
        downloads.Dispose();
        mutation.Dispose();
    }
}
