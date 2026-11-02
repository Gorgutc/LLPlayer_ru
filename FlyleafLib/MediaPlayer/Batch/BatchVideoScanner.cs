using System.Linq;

namespace FlyleafLib.MediaPlayer.Batch;

public static class BatchVideoScanner
{
    public static IReadOnlyList<string> Scan(string folderPath, bool recursive) => Scan(folderPath, recursive, CancellationToken.None);

    public static IReadOnlyList<string> Scan(string folderPath, bool recursive, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);

        if (!Directory.Exists(folderPath))
            throw new DirectoryNotFoundException(folderPath);

        EnumerationOptions options = new()
        {
            RecurseSubdirectories = recursive,
            IgnoreInaccessible = true,
            MatchCasing = MatchCasing.CaseInsensitive
        };

        return ScanPaths(Directory.EnumerateFiles(folderPath, "*", options), cancellationToken);
    }

    internal static IReadOnlyList<string> ScanPaths(IEnumerable<string> paths, CancellationToken cancellationToken)
    {
        List<string> collected = [];
        using var entries = paths.GetEnumerator();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!entries.MoveNext()) break;
            cancellationToken.ThrowIfCancellationRequested();
            collected.Add(entries.Current);
        }
        cancellationToken.ThrowIfCancellationRequested();
        return Utils.GetMoviesSorted(collected);
    }
}
