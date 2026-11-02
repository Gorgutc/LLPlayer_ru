using System.Text.Json;
using FlyleafLib;
using FlyleafLib.Plugins;

namespace LLPlayer.Avalonia.Services;

/// <summary>Explicit external tools only. Reading settings never starts a process or searches PATH.</summary>
public sealed class OnlineVideoSettings
{
    public string YtDlpPath { get; set; } = "";
    public string DenoPath { get; set; } = "";
    public string ExtraArguments { get; set; } = "";
    public int MaxVideoHeight { get; set; } = 720;
    public bool PreferVideoWithAudio { get; set; }
    public OnlineVideoSettings Clone() => new()
    {
        YtDlpPath = YtDlpPath, DenoPath = DenoPath, ExtraArguments = ExtraArguments,
        MaxVideoHeight = MaxVideoHeight, PreferVideoWithAudio = PreferVideoWithAudio
    };

    public void Validate()
    {
        ValidatePath(YtDlpPath, "yt-dlp");
        ValidatePath(DenoPath, "Deno");
        ValidateOptions();
    }

    public void ValidateOptions()
    {
        if (MaxVideoHeight is < 0 or > 32768)
            throw new ArgumentException("Maximum video height must be between 0 (engine limit) and 32768 pixels.");
        OnlineVideoArguments.Parse(ExtraArguments);
    }

    static void ValidatePath(string? path, string name)
    {
        if (path != "" && (path == null || !OnlineVideoRequest.IsExecutablePath(path)))
            throw new ArgumentException($"{name} must be an absolute path to an existing executable, or empty to disable online video.");
    }

    public void RequireTools()
    {
        Validate();
        if (string.IsNullOrEmpty(YtDlpPath) || string.IsNullOrEmpty(DenoPath))
            throw new ArgumentException("Save paths to both yt-dlp and Deno before opening an online video.");
    }

    public void Apply(Config config)
    {
        if (config.Plugins.TryGetValue(OnlineVideoRequest.PluginName, out var options))
        {
            options[OnlineVideoRequest.YtDlpPath] = YtDlpPath;
            options[OnlineVideoRequest.DenoPath] = DenoPath;
            options["ExtraArguments"] = ExtraArguments;
            options["MaxVideoHeight"] = MaxVideoHeight.ToString(System.Globalization.CultureInfo.InvariantCulture);
            options["PreferVideoWithAudio"] = PreferVideoWithAudio.ToString();
        }
    }
}

public sealed class OnlineVideoSettingsStore(string path)
{
    public string? LoadWarning { get; private set; }
    public OnlineVideoSettings Load()
    {
        LoadWarning = null;
        try
        {
            try { File.GetAttributes(path); }
            catch (FileNotFoundException) { return new(); }
            catch (DirectoryNotFoundException) { return new(); }
            // Paths may become unavailable after a successful save (removed drive/tool). Keep the saved draft
            // editable; admission revalidates the executable immediately before an explicit online request.
            var settings = JsonSerializer.Deserialize<OnlineVideoSettings>(File.ReadAllText(path)) ?? throw new JsonException();
            if (settings.YtDlpPath == null || settings.DenoPath == null) throw new JsonException();
            settings.ValidateOptions();
            return settings;
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            LoadWarning = "Online video settings could not be read. Tools are disabled; the original file was preserved. Save explicitly to replace it.";
            return new();
        }
    }

    public void Save(OnlineVideoSettings settings)
    {
        settings.Validate();
        string full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        AtomicFile.WriteAllText(full, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        LoadWarning = null;
    }
}
