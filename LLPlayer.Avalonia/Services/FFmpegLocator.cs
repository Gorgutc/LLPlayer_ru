namespace LLPlayer.Avalonia.Services;

/// <summary>Where the FFmpeg folder came from (for the error window / logs).</summary>
public enum FFmpegDirSource
{
    CommandLine,
    Environment,
    AppFolder,
}

public sealed record FFmpegLocation(string Directory, FFmpegDirSource Source, IReadOnlyList<string> MissingLibraries)
{
    public bool IsValid => MissingLibraries.Count == 0;
}

/// <summary>
/// Resolves the folder with the FFmpeg 8 shared libraries: <c>--ffmpeg-dir</c>, else <c>$LLPLAYER_FFMPEG_DIR</c>,
/// else <c>&lt;AppContext.BaseDirectory&gt;/FFmpeg</c>, and checks that the libraries FlyleafLib loads are present.
/// </summary>
public static class FFmpegLocator
{
    public const string EnvironmentVariable = "LLPLAYER_FFMPEG_DIR";

    /// <summary>FFmpeg 8.x sonames loaded by FlyleafLib (libavdevice is optional).</summary>
    public static readonly IReadOnlyList<string> RequiredLibraries =
    [
        "libavutil.so.60",
        "libavcodec.so.62",
        "libavformat.so.62",
        "libavfilter.so.11",
        "libswscale.so.9",
        "libswresample.so.6",
    ];

    public static FFmpegLocation Locate(string? commandLineDir, Func<string, string?>? getEnvironmentVariable = null, string? baseDirectory = null)
    {
        getEnvironmentVariable ??= Environment.GetEnvironmentVariable;

        string dir;
        FFmpegDirSource source;
        if (!string.IsNullOrWhiteSpace(commandLineDir))
        {
            dir = commandLineDir;
            source = FFmpegDirSource.CommandLine;
        }
        else if (getEnvironmentVariable(EnvironmentVariable) is { Length: > 0 } envDir && !string.IsNullOrWhiteSpace(envDir))
        {
            dir = envDir;
            source = FFmpegDirSource.Environment;
        }
        else
        {
            dir = Path.Combine(baseDirectory ?? AppContext.BaseDirectory, "FFmpeg");
            source = FFmpegDirSource.AppFolder;
        }

        dir = Path.GetFullPath(dir);
        return new FFmpegLocation(dir, source, FindMissing(dir));
    }

    public static IReadOnlyList<string> FindMissing(string directory)
    {
        if (!Directory.Exists(directory))
            return RequiredLibraries;

        return RequiredLibraries.Where(lib => !File.Exists(Path.Combine(directory, lib))).ToList();
    }
}
