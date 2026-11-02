using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using FlyleafLib;
using Whisper.net.Ggml;

namespace LLPlayer.Core.Services.Models;

/// <summary>Explicit resource acquisition; construction and discovery never create folders or contact a provider.</summary>
public sealed class ModelManagementService : IModelManagementService
{
    public const string EngineName = "Faster-Whisper-XXL";
    public const string EngineArchiveUrl = "https://github.com/umlx5h/LLPlayer/releases/download/v0.0.1/Faster-Whisper-XXL_r245.4_windows.7z";
    const string ReceiptFile = ".llplayer-managed-engine.json";
    readonly HttpClient httpClient;
    readonly ModelStoragePaths paths;
    readonly Func<ModelEntry, bool> isInUse;
    readonly Func<ModelEntry, Func<Task>, CancellationToken, Task> mutateIfIdle;
    readonly IEngineArchiveExtractor? extractor;
    readonly bool windows;
    readonly TimeSpan downloadTimeout;
    readonly SemaphoreSlim operation = new(1, 1);
    // Deterministic filesystem-race injection for the existing friend test assembly, never a host extension.
    internal Action<string>? EngineRemovalStaged { get; set; }

    public ModelManagementService(HttpClient httpClient, ModelStoragePaths paths,
        Func<ModelEntry, bool>? isInUse = null,
        Func<ModelEntry, Func<Task>, CancellationToken, Task>? mutateIfIdle = null,
        IEngineArchiveExtractor? extractor = null, bool? isWindows = null, TimeSpan? downloadTimeout = null)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        this.paths = paths ?? throw new ArgumentNullException(nameof(paths));
        foreach (string directory in new[] { paths.WhisperModels, paths.TesseractModels, paths.WhisperEngines })
            if (!Path.IsPathFullyQualified(directory)) throw new ArgumentException("Model directories must be absolute paths.", nameof(paths));
        this.isInUse = isInUse ?? (_ => false);
        this.mutateIfIdle = mutateIfIdle ?? ((_, action, _) => action());
        this.extractor = extractor;
        windows = isWindows ?? OperatingSystem.IsWindows();
        this.downloadTimeout = downloadTimeout ?? TimeSpan.FromHours(1);
        if (this.downloadTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(downloadTimeout));
    }

    public string GetDirectory(ModelKind kind) => Path.GetFullPath(kind switch
    {
        ModelKind.Whisper => paths.WhisperModels,
        ModelKind.Tesseract => paths.TesseractModels,
        ModelKind.FasterWhisperEngine => paths.WhisperEngines,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    });

    public IReadOnlyList<ModelEntry> Discover(ModelKind kind)
    {
        string directory = GetDirectory(kind);
        IEnumerable<(string Id, string Label, string File)> catalog = kind switch
        {
            ModelKind.Whisper => WhisperCppModelLoader.LoadAllModels(() => { }, _ => null)
                .Select(m => ($"whisper:{m.Model}:{m.Quantization}", m.ToString(), m.ModelFileName)),
            ModelKind.Tesseract => Enum.GetValues<TesseractOCR.Enums.Language>()
                .Where(TesseractModel.TesseractLangToISO6391.ContainsKey)
                .Select(l => new TesseractModel { Lang = l }).OrderBy(m => m.Lang.ToString())
                .Select(m => ($"tesseract:{m.LangCode}", $"{m.Lang} ({m.LangCode})", m.ModelFileName)),
            ModelKind.FasterWhisperEngine => [("engine:xxl-r245.4", "Faster-Whisper-XXL r245.4 (Windows)", EngineName)],
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        return catalog.Select(item =>
        {
            string path = Path.Combine(directory, item.File);
            bool engine = kind == ModelKind.FasterWhisperEngine;
            bool present = File.Exists(path) || Directory.Exists(path);
            string probe = engine ? Path.Combine(path, "faster-whisper-xxl.exe") : path;
            long size = File.Exists(probe) ? new FileInfo(probe).Length : 0;
            bool available = !engine || (windows && extractor != null);
            string? reason = !engine ? null : !windows ? "Automatic engine installation is Windows only. On Linux, choose an existing compatible executable in Recognition settings."
                : extractor == null ? "Windows archive extraction is unavailable in this build." : null;
            return new ModelEntry(item.Id, kind, item.Label, item.File, directory, size > 0, size, available, reason, present);
        }).ToArray();
    }

    ModelEntry Resolve(string id)
    {
        foreach (ModelKind kind in Enum.GetValues<ModelKind>())
        {
            ModelEntry? entry = Discover(kind).FirstOrDefault(row => row.Id == id);
            if (entry != null) return entry;
        }
        throw new ArgumentException("Unknown model catalog entry.", nameof(id));
    }

    public async Task DownloadAsync(string id, IProgress<ModelDownloadProgress>? progress, CancellationToken cancellationToken)
    {
        if (!await operation.WaitAsync(0, cancellationToken)) throw new InvalidOperationException("Another model operation is active.");
        string? partial = null, stage = null;
        bool ownsPartial = false;
        Exception? operationError = null;
        try
        {
            ModelEntry row = Resolve(id);
            if (!row.Available) throw new PlatformNotSupportedException(row.UnavailableReason);
            AssertSafePath(row.FullPath);
            AssertAbsent(row.FullPath);
            Directory.CreateDirectory(row.Directory);
            AssertSafePath(row.Directory);
            partial = Path.Combine(row.Directory, $".llplayer-download-{Guid.NewGuid():N}.partial");
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(downloadTimeout);
            CancellationToken token = timeout.Token;
            try
            {
                long copied;
                if (row.Kind == ModelKind.Whisper)
                {
                    string[] parts = row.Id.Split(':');
                    await using Stream source = await new WhisperGgmlDownloader(httpClient).GetGgmlModelAsync(
                        Enum.Parse<GgmlType>(parts[1]), Enum.Parse<QuantizationType>(parts[2]), token);
                    copied = await CopyAsync(source, partial, null, progress, () => ownsPartial = true, token);
                }
                else
                {
                    string sourceUrl = row.Kind == ModelKind.Tesseract
                        ? $"https://github.com/tesseract-ocr/tessdata/raw/refs/heads/main/{row.FileName}"
                        : EngineArchiveUrl;
                    using HttpResponseMessage response = await httpClient.GetAsync(sourceUrl, HttpCompletionOption.ResponseHeadersRead, token);
                    response.EnsureSuccessStatusCode();
                    long? length = response.Content.Headers.ContentLength;
                    await using Stream source = await response.Content.ReadAsStreamAsync(token);
                    copied = await CopyAsync(source, partial, length, progress, () => ownsPartial = true, token);
                }
                if (copied == 0) throw new IOException("The server returned an empty resource.");
                if (row.Kind == ModelKind.FasterWhisperEngine)
                {
                    stage = Path.Combine(row.Directory, $".llplayer-extract-{Guid.NewGuid():N}");
                    Directory.CreateDirectory(stage);
                    progress?.Report(new(copied, copied, "Extracting engine"));
                    await extractor!.ExtractAsync(partial, stage, token);
                    token.ThrowIfCancellationRequested();
                    string engine = Path.Combine(stage, EngineName);
                    AssertSafeTree(stage);
                    if (!File.Exists(Path.Combine(engine, "faster-whisper-xxl.exe")) || new FileInfo(Path.Combine(engine, "faster-whisper-xxl.exe")).Length == 0)
                        throw new IOException("The engine archive does not contain the expected executable.");
                    string license = Path.Combine(stage, "license.txt");
                    if (File.Exists(license)) File.Move(license, Path.Combine(engine, "license.txt"));
                    Dictionary<string, string> hashes = await FingerprintsAsync(engine, token);
                    await File.WriteAllTextAsync(Path.Combine(engine, ReceiptFile), JsonSerializer.Serialize(hashes), token);
                    await CommitAsync(row, () => { Directory.Move(engine, row.FullPath); return Task.CompletedTask; }, token);
                }
                else
                    await CommitAsync(row, () => { File.Move(partial, row.FullPath); return Task.CompletedTask; }, token);
                progress?.Report(new(copied, copied, "Downloaded"));
            }
            catch (OperationCanceledException e) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException("Model download timed out. Try again or choose another model.", e);
            }
        }
        catch (Exception e) { operationError = e; throw; }
        finally
        {
            try { Cleanup(ownsPartial ? partial : null, stage, operationError); }
            finally { operation.Release(); }
        }
    }

    static async Task<long> CopyAsync(Stream source, string partial, long? length, IProgress<ModelDownloadProgress>? progress, Action markOwned, CancellationToken token)
    {
        progress?.Report(new(0, length, "Downloading"));
        await using FileStream destination = new(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, true);
        markOwned();
        long copied = await StreamDownloadPump.CopyWithProgressAsync(source, destination,
            bytes => progress?.Report(new(bytes, length, "Downloading")), TimeSpan.FromMilliseconds(50), token);
        if (length.HasValue && copied != length.Value) throw new IOException("The download was incomplete; no resource was installed.");
        await destination.FlushAsync(token);
        progress?.Report(new(copied, length, "Downloading"));
        return copied;
    }

    Task CommitAsync(ModelEntry row, Func<Task> commit, CancellationToken token) => mutateIfIdle(row, async () =>
    {
        token.ThrowIfCancellationRequested();
        AssertSafePath(row.FullPath);
        if (isInUse(row)) throw new InvalidOperationException("This resource is selected or in use by recognition or background work. Stop that work and change the selection before modifying it.");
        AssertAbsent(row.FullPath);
        await commit();
    }, token);

    public async Task DeleteAsync(string id, CancellationToken cancellationToken)
    {
        if (!await operation.WaitAsync(0, cancellationToken)) throw new InvalidOperationException("Another model operation is active.");
        try
        {
            ModelEntry row = Resolve(id);
            if (row.Kind == ModelKind.FasterWhisperEngine && !windows) throw new PlatformNotSupportedException(row.UnavailableReason);
            await mutateIfIdle(row, async () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                AssertSafePath(row.FullPath);
                if (isInUse(row)) throw new InvalidOperationException("This resource is selected or in use. Stop recognition/background work and change the selection before deleting it.");
                if (row.Kind == ModelKind.FasterWhisperEngine)
                {
                    if (!Directory.Exists(row.FullPath)) return;
                    AssertSafeTree(row.FullPath);
                    string receipt = Path.Combine(row.FullPath, ReceiptFile);
                    if (!File.Exists(receipt) || new FileInfo(receipt).Length > 16 * 1024 * 1024)
                        throw new IOException("This engine was installed outside this manager. Open its folder to manage it manually.");
                    string receiptText = File.ReadAllText(receipt);
                    Dictionary<string, string>? expected = JsonSerializer.Deserialize<Dictionary<string, string>>(receiptText);
                    Dictionary<string, string> actual = await FingerprintsAsync(row.FullPath, cancellationToken);
                    if (expected == null || expected.Count != actual.Count || expected.Any(pair => !actual.TryGetValue(pair.Key, out string? hash) || pair.Value != hash))
                        throw new IOException("The engine folder contains changed or additional files. They were preserved; open the folder to manage them manually.");
                    await RemoveManagedEngineAsync(row, expected, receiptText, cancellationToken);
                }
                else if (File.Exists(row.FullPath))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    AssertSafePath(row.FullPath);
                    File.Delete(row.FullPath);
                }
            }, cancellationToken);
        }
        finally { operation.Release(); }
    }

    async Task RemoveManagedEngineAsync(ModelEntry row, Dictionary<string, string> expected, string receiptText, CancellationToken token)
    {
        string removal = Path.Combine(row.Directory, $".llplayer-remove-{Guid.NewGuid():N}");
        AssertDirectEngineChild(row.FullPath);
        AssertDirectEngineChild(removal);
        AssertAbsent(removal);
        token.ThrowIfCancellationRequested();
        Directory.Move(row.FullPath, removal);
        try
        {
            EngineRemovalStaged?.Invoke(removal);
            // Revalidate after the rename, then remove only the manifest's entries. Foreign additions never
            // become recursive-delete targets, including empty directories added during a long hash scan.
            Dictionary<string, string> staged = await FingerprintsAsync(removal, token);
            if (expected.Count != staged.Count || expected.Any(pair => !staged.TryGetValue(pair.Key, out string? hash) || pair.Value != hash))
                throw new IOException("The engine changed while removal was being prepared.");
            foreach (var item in expected.Where(pair => !pair.Key.EndsWith('/')).OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                string file = Path.Combine(removal, item.Key.Replace('/', Path.DirectorySeparatorChar));
                AssertSafePath(file);
                if (await HashFileAsync(file, token) != item.Value)
                    throw new IOException($"An engine file changed during removal: {item.Key}");
                token.ThrowIfCancellationRequested();
                AssertSafePath(file);
                File.Delete(file);
            }
            string receipt = Path.Combine(removal, ReceiptFile);
            token.ThrowIfCancellationRequested();
            AssertSafePath(receipt);
            if (new FileInfo(receipt).Length > 16 * 1024 * 1024 || File.ReadAllText(receipt) != receiptText)
                throw new IOException("The engine ownership receipt changed during removal.");
            File.Delete(receipt);
            foreach (string directory in expected.Keys.Where(name => name.EndsWith('/')).OrderByDescending(name => name.Length))
            {
                string child = Path.Combine(removal, directory.TrimEnd('/').Replace('/', Path.DirectorySeparatorChar));
                token.ThrowIfCancellationRequested();
                AssertSafePath(child);
                Directory.Delete(child, false);
            }
            token.ThrowIfCancellationRequested();
            AssertDirectEngineChild(removal);
            Directory.Delete(removal, false);
        }
        catch (Exception error)
        {
            // Earlier allowlisted files may already be removed. Restore the preserved remainder without
            // overwriting any new installation at the original path; report the actual recovery location.
            string recovery = removal;
            try
            {
                AssertDirectEngineChild(removal);
                AssertDirectEngineChild(row.FullPath);
                if (!Path.Exists(row.FullPath) && Directory.Exists(removal))
                {
                    Directory.Move(removal, row.FullPath);
                    recovery = row.FullPath;
                }
            }
            catch (Exception restoreError)
            {
                throw new IOException($"Engine removal stopped; preserved files remain at '{recovery}'. Some managed files may already have been removed. The original location could not be restored.", new AggregateException(error, restoreError));
            }
            throw new IOException($"Engine removal stopped; preserved files remain at '{recovery}'. Some managed files may already have been removed.", error);
        }
    }

    void AssertDirectEngineChild(string path)
    {
        string full = Path.GetFullPath(path);
        string root = GetDirectory(ModelKind.FasterWhisperEngine).TrimEnd(Path.DirectorySeparatorChar);
        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!string.Equals(Path.GetDirectoryName(full)?.TrimEnd(Path.DirectorySeparatorChar), root, comparison))
            throw new IOException("Engine removal path is outside the exact configured engine directory.");
        AssertSafePath(full);
    }

    static void AssertAbsent(string path)
    {
        if (File.Exists(path) || Directory.Exists(path)) throw new IOException("A resource already exists at the destination. It was preserved; delete it explicitly before downloading.");
    }

    internal static void AssertSafePath(string path)
    {
        string? candidate = Path.GetFullPath(path);
        while (candidate != null)
        {
            if (Path.Exists(candidate) && (File.GetAttributes(candidate) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Model operations do not follow symbolic links or junctions.");
            candidate = Path.GetDirectoryName(candidate);
        }
    }

    static void AssertSafeTree(string path)
    {
        AssertSafePath(path);
        foreach (string child in Directory.EnumerateFileSystemEntries(path))
        {
            AssertSafePath(child);
            if (Directory.Exists(child)) AssertSafeTree(child);
        }
    }

    static async Task<Dictionary<string, string>> FingerprintsAsync(string path, CancellationToken token)
    {
        await Task.Run(() => AssertSafeTree(path), token);
        Dictionary<string, string> result = new(StringComparer.Ordinal);
        // Empty folders are owner state too; a foreign empty directory must block recursive removal.
        foreach (string directory in Directory.EnumerateDirectories(path, "*", SearchOption.AllDirectories))
        {
            token.ThrowIfCancellationRequested();
            result.Add(Path.GetRelativePath(path, directory).Replace('\\', '/') + "/", "directory");
        }
        foreach (string file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(path, file).Replace('\\', '/');
            if (relative == ReceiptFile) continue;
            result.Add(relative, await HashFileAsync(file, token));
        }
        return result;
    }

    static async Task<string> HashFileAsync(string path, CancellationToken token)
    {
        await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, true);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, token));
    }

    static void Cleanup(string? partial, string? stage, Exception? original)
    {
        List<Exception> failures = [];
        try { if (partial != null && File.Exists(partial)) { AssertSafePath(partial); File.Delete(partial); } }
        catch (Exception e) { failures.Add(e); }
        try { if (stage != null && Directory.Exists(stage)) { AssertSafeTree(stage); Directory.Delete(stage, true); } }
        catch (Exception e) { failures.Add(e); }
        if (failures.Count != 0)
        {
            if (original != null) failures.Insert(0, original);
            throw new IOException("Could not remove operation-owned temporary model files.", new AggregateException(failures));
        }
    }
}
