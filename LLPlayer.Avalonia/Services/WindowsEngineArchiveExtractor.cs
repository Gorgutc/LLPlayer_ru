using FlyleafLib;
using LLPlayer.Core.Services.Models;
using SevenZip;

namespace LLPlayer.Avalonia.Services;

/// <summary>The existing WPF 7z implementation, restricted to Windows and an operation-owned staging directory.</summary>
public sealed class WindowsEngineArchiveExtractor : IEngineArchiveExtractor
{
    public async Task ExtractAsync(string archivePath, string destinationDirectory, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("The packaged XXL engine is Windows only.");
        cancellationToken.ThrowIfCancellationRequested();
        if (!Directory.Exists(destinationDirectory) || Directory.EnumerateFileSystemEntries(destinationDirectory).Any())
            throw new IOException("Engine extraction requires an empty owned staging directory.");
        SevenZipBase.SetLibraryPath(Path.Combine(AppContext.BaseDirectory, "lib", "7z.dll"));
        using SevenZipExtractor archive = new(archivePath);
        EngineArchivePolicy.ValidateEntryNames(archive.ArchiveFileData.Select(entry => entry.FileName), destinationDirectory);
        if (archive.ArchiveFileData.Any(entry => (entry.Attributes & (uint)FileAttributes.ReparsePoint) != 0
            || ((entry.Attributes >> 16) & 0xF000) == 0xA000 || entry.Encrypted))
            throw new IOException("Linked or encrypted engine archive entries are not supported.");
        // SevenZipSharp has no cancellation-aware async extraction. Drain it before checking cancellation/cleanup.
        await archive.ExtractArchiveAsync(destinationDirectory);
        cancellationToken.ThrowIfCancellationRequested();
    }

}
