using System.Linq;

namespace FlyleafLib.MediaPlayer.Batch;

public static class BatchVideoScanner
{
    public static IReadOnlyList<string> Scan(string folderPath, bool recursive)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);

        if (!Directory.Exists(folderPath))
            throw new DirectoryNotFoundException(folderPath);

        EnumerationOptions options = new()
        {
            RecurseSubdirectories = recursive,
            IgnoreInaccessible = true,
            MatchCasing = MatchCasing.CaseInsensitive
        };

        return Utils.GetMoviesSorted(Directory.GetFiles(folderPath, "*", options).ToList());
    }
}
