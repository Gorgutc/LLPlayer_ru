using System.Text.Json;
using FlyleafLib;

namespace LLPlayer.Avalonia.Services;

/// <summary>Explicit Windows bridge path only. Loading these preferences never probes or starts PDIC.</summary>
public sealed class PdicSettings
{
    public string ExecutablePath { get; set; } = "";
    public PdicSettings Clone() => new() { ExecutablePath = ExecutablePath };
    public void Validate()
    {
        string path = ExecutablePath;
        bool windowsAbsolute = path is { Length: >= 3 } && char.IsAsciiLetter(path[0]) && path[1] == ':'
            && (path[2] == '\\' || path[2] == '/');
        windowsAbsolute |= path?.StartsWith("\\\\", StringComparison.Ordinal) == true;
        if (path == null || path.Length > 32768 || path.Any(char.IsControl)
            || (path.Length > 0 && ((!windowsAbsolute && !Path.IsPathFullyQualified(path))
                || !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))))
            throw new ArgumentException("Choose an absolute path to pdic-pipe.exe, or leave it empty to disable PDIC.");
    }
}

public sealed class PdicSettingsStore(string path)
{
    public string? LoadWarning { get; private set; }
    public PdicSettings Load()
    {
        LoadWarning = null;
        try
        {
            try { File.GetAttributes(path); }
            catch (FileNotFoundException) { return new(); }
            catch (DirectoryNotFoundException) { return new(); }
            var settings = JsonSerializer.Deserialize<PdicSettings>(File.ReadAllText(path)) ?? throw new JsonException();
            settings.Validate();
            return settings;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException or NotSupportedException)
        {
            LoadWarning = "PDIC settings could not be read. The original file was preserved; save explicitly to replace it.";
            return new();
        }
    }
    public void Save(PdicSettings settings)
    {
        settings.Validate();
        string full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        AtomicFile.WriteAllText(full, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        LoadWarning = null;
    }
}
