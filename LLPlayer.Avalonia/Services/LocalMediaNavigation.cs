using FlyleafLib;

namespace LLPlayer.Avalonia.Services;

/// <summary>On-demand local title navigation; admission itself never probes the filesystem.</summary>
public static class LocalMediaNavigation
{
    /// <summary>Accepts an absolute filesystem path or a file URI, with no relative-path or network-URL fallback.</summary>
    public static bool TryGetLocalPath(string? source, out string path)
    {
        path = "";
        if (string.IsNullOrWhiteSpace(source) || source.Contains('\0')) return false;
        try
        {
            string candidate;
            if (Path.IsPathFullyQualified(source))
                candidate = source;
            else if (Uri.TryCreate(source, UriKind.Absolute, out Uri? uri) && uri.IsFile
                && uri.Query.Length == 0 && uri.Fragment.Length == 0)
                candidate = uri.LocalPath;
            else
                return false;

            if (!Path.IsPathFullyQualified(candidate)) return false;
            path = Path.GetFullPath(candidate);
            return true;
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    /// <summary>WPF title actions: same extension ignoring case, natural filename order, and no wrapping.</summary>
    public static string? GetSibling(string source, bool next)
    {
        if (!TryGetLocalPath(source, out string current))
            throw new ArgumentException("Open a local media file before navigating to another file.", nameof(source));
        if (!File.Exists(current)) throw new FileNotFoundException("The current media file could not be found.", current);
        string? directory = Path.GetDirectoryName(current);
        string extension = Path.GetExtension(current);
        if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(extension))
            throw new InvalidOperationException("The current media file needs a folder and extension for local navigation.");

        // Enumerate without a glob: a Linux *.mkv glob would otherwise omit .MKV despite the shared policy.
        List<string> files = Directory.EnumerateFiles(directory)
            .Where(file => string.Equals(Path.GetExtension(file), extension, StringComparison.OrdinalIgnoreCase))
            .OrderBy(file => Path.GetFileName(file)!, new Utils.NaturalStringComparer())
            .ThenBy(file => Path.GetFileName(file)!, StringComparer.Ordinal)
            .ToList();
        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        int index = files.FindIndex(file => string.Equals(file, current, comparison));
        if (index < 0) throw new FileNotFoundException("The current media file is no longer in its folder.", current);
        int target = index + (next ? 1 : -1);
        return target >= 0 && target < files.Count ? files[target] : null;
    }
}
