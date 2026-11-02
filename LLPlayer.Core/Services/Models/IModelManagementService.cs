namespace LLPlayer.Core.Services.Models;

public enum ModelKind { Whisper, Tesseract, FasterWhisperEngine }

public sealed record ModelEntry(string Id, ModelKind Kind, string DisplayName, string FileName,
    string Directory, bool Downloaded, long Size, bool Available = true, string? UnavailableReason = null, bool Present = false)
{
    public string FullPath => Path.Combine(Directory, FileName);
    public string Status => !Available ? UnavailableReason ?? "Unavailable" : Downloaded ? $"Downloaded ({Size:N0} bytes)" : "Not downloaded";
    public override string ToString() => $"{DisplayName} — {Status}";
}

public sealed record ModelStoragePaths(string WhisperModels, string TesseractModels, string WhisperEngines);
public sealed record ModelDownloadProgress(long BytesReceived, long? TotalBytes, string Stage);

public interface IModelManagementService
{
    IReadOnlyList<ModelEntry> Discover(ModelKind kind);
    string GetDirectory(ModelKind kind);
    Task DownloadAsync(string id, IProgress<ModelDownloadProgress>? progress, CancellationToken cancellationToken);
    Task DeleteAsync(string id, CancellationToken cancellationToken);
}

/// <summary>Extract into an empty operation-owned directory, validating all entry paths before any writes.</summary>
public interface IEngineArchiveExtractor
{
    Task ExtractAsync(string archivePath, string destinationDirectory, CancellationToken cancellationToken);
}
