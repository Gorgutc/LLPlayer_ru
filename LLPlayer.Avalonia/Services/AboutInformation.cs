using System.Reflection;
using System.Runtime.InteropServices;

namespace LLPlayer.Avalonia.Services;

public sealed record AboutCredit(string Name, string Role, string Url);

public sealed record AboutInformation(string Version, string SourceRevision, string Runtime,
    string OperatingSystem, string ProcessArchitecture, string OSArchitecture,
    IReadOnlyList<AboutCredit> Credits)
{
    public const string ProjectUrl = "https://github.com/Gorgutc/LLPlayer_ru";
    public const string UpstreamUrl = "https://github.com/umlx5h/LLPlayer";
    public string CopyText => $"LLPlayer (Avalonia) {Version}\nSource: {SourceRevision}\nRuntime: {Runtime}\nOS: {OperatingSystem}\nProcess: {ProcessArchitecture}; OS architecture: {OSArchitecture}";

    public static AboutInformation FromAssembly(Assembly assembly)
    {
        string version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString() ?? "Unknown";
        int separator = version.IndexOf('+');
        string revision = separator >= 0 && separator < version.Length - 1 ? version[(separator + 1)..] : "Not embedded in this build";
        return new(version, revision, RuntimeInformation.FrameworkDescription, RuntimeInformation.OSDescription,
            RuntimeInformation.ProcessArchitecture.ToString(), RuntimeInformation.OSArchitecture.ToString(),
            [
                new("LLPlayer", "Language-learning player and upstream project", UpstreamUrl),
                new("Flyleaf", "Shared media engine", "https://github.com/SuRGeoNix/Flyleaf"),
                new("FFmpeg", "Media demuxing, decoding, filters and capture", "https://ffmpeg.org/"),
                new("Avalonia", "Shared Windows and Linux desktop UI", "https://avaloniaui.net/"),
                new("CommunityToolkit.Mvvm", "View models and commands", "https://github.com/CommunityToolkit/dotnet"),
                new("OpenAL Soft", "Portable audio output", "https://openal-soft.org/"),
                new("whisper.cpp / Whisper.net", "Local speech recognition", "https://github.com/sandrohanea/whisper.net"),
                new("Tesseract OCR", "Optical character recognition", "https://github.com/tesseract-ocr/tesseract"),
                new("LibNMeCab", "Japanese subtitle segmentation", "https://github.com/komutan/NMeCab"),
                new("yt-dlp", "Optional externally configured online-video helper", "https://github.com/yt-dlp/yt-dlp"),
            ]);
    }
}
