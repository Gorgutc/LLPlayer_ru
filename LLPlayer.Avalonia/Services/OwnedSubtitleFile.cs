namespace LLPlayer.Avalonia.Services;

public delegate Task<bool> SubtitlePrimaryLoadTarget(OwnedSubtitleFile subtitle, string language, CancellationToken cancellationToken);

/// <summary>
/// One explicit Load action's unique temporary child. Cleanup touches only its one known file and empty directory;
/// it never enumerates or recursively removes a shared temp directory. Successful native loads transfer ownership.
/// </summary>
public sealed class OwnedSubtitleFile
{
    readonly string directory;
    public string FilePath { get; }
    internal bool RetainedByPlayer { get; set; }
    OwnedSubtitleFile(string directory, string filePath) { this.directory = directory; FilePath = filePath; }

    public static async Task<OwnedSubtitleFile> CreateAsync(string fileName, byte[] utf8Bytes, bool bom,
        CancellationToken cancellationToken, string? temporaryRoot = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string root = Path.GetFullPath(temporaryRoot ?? Path.Combine(Path.GetTempPath(), "LLPlayer", "OpenSubtitles"));
        Directory.CreateDirectory(root);
        if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("The subtitle temporary folder must not be a symbolic link or junction.");
        string child = Path.Combine(root, Guid.NewGuid().ToString("N"));
        if (Directory.Exists(child)) throw new IOException("The subtitle temporary folder already exists.");
        Directory.CreateDirectory(child);
        OwnedSubtitleFile owned = new(child, Path.Combine(child, OpenSubtitlesClient.SafeFileName(fileName)));
        try
        {
            await WriteAsync(owned.FilePath, utf8Bytes, bom, cancellationToken).ConfigureAwait(false);
            return owned;
        }
        catch
        {
            // A partial write belongs to this lease too; leave any cleanup failure visible in the exception.
            if (!owned.TryDelete(out string? error)) throw new IOException("The subtitle load failed and its temporary file could not be removed: " + error);
            throw;
        }
    }
    static async Task WriteAsync(string path, byte[] bytes, bool bom, CancellationToken cancellationToken)
    {
        using FileStream file = new(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 16384, useAsync: true);
        if (bom) await file.WriteAsync(System.Text.Encoding.UTF8.GetPreamble(), cancellationToken).ConfigureAwait(false);
        await file.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
    }
    /// <summary>Returns actionable cleanup errors so app shutdown can report/log a locked owned child and retry.</summary>
    public bool TryDelete(out string? error)
    {
        error = null;
        try
        {
            string child = Path.GetFullPath(directory);
            string file = Path.GetFullPath(FilePath);
            StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (!file.StartsWith(child + Path.DirectorySeparatorChar, comparison)
                || Directory.Exists(child) && (File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("The owned subtitle temporary path changed; cleanup was refused.");
            if (File.Exists(file)) File.Delete(file);
            if (Directory.Exists(child)) Directory.Delete(child, recursive: false);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { error = FilePath + ": " + ex.Message; return false; }
    }
}
