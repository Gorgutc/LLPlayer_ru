using System.Text.Json;
using System.Text.Json.Serialization;
using FlyleafLib;
using FlyleafLib.MediaPlayer.Dubbing;

namespace LLPlayer.Core.Services;

/// <summary>Detached, additive preferences for the existing local dubbing engine. Batch owns its opt-in switch.</summary>
public sealed class DubbingSettings
{
    public DubbingConfig Engine { get; set; } = new();
    public bool PersistPerLineVoices { get; set; }

    internal static readonly JsonSerializerOptions JsonOptions = new()
    { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    public DubbingSettings Clone() => JsonSerializer.Deserialize<DubbingSettings>(
        JsonSerializer.Serialize(this, JsonOptions), JsonOptions)!;

    public static DubbingSettings FromConfig(Config.SubtitlesConfig source) => new DubbingSettings
    { Engine = source.DubbingConfig, PersistPerLineVoices = source.PersistPerLineVoices }.Clone();

    /// <summary>Overlay only this editor's changes onto freshly captured state (including another window's voice choice).</summary>
    public static DubbingSettings MergeChanges(DubbingSettings current, DubbingSettings baseline, DubbingSettings edited)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(edited);
        baseline.Validate();
        edited.Validate();
        DubbingSettings merged = current.Clone();
        DubbingConfig before = baseline.Engine, after = edited.Engine, target = merged.Engine;
        if (baseline.PersistPerLineVoices != edited.PersistPerLineVoices)
            merged.PersistPerLineVoices = edited.PersistPerLineVoices;
        if (before.TtsServiceType != after.TtsServiceType) target.TtsServiceType = after.TtsServiceType;
        if (before.UseManualEngine != after.UseManualEngine) target.UseManualEngine = after.UseManualEngine;
        if (before.ManualVenvPython != after.ManualVenvPython) target.ManualVenvPython = after.ManualVenvPython;
        if (before.Model != after.Model) target.Model = after.Model;
        if (before.DefaultVoiceId != after.DefaultVoiceId) target.DefaultVoiceId = after.DefaultVoiceId;
        if (!before.CustomVoiceIds.SequenceEqual(after.CustomVoiceIds, StringComparer.Ordinal))
            target.CustomVoiceIds = [.. after.CustomVoiceIds];
        if (before.DuckingPercent != after.DuckingPercent) target.DuckingPercent = after.DuckingPercent;
        if (before.AtempoMin != after.AtempoMin) target.AtempoMin = after.AtempoMin;
        if (before.AtempoMax != after.AtempoMax) target.AtempoMax = after.AtempoMax;
        if (before.StressNormalization != after.StressNormalization) target.StressNormalization = after.StressNormalization;
        if (before.OutputFormat != after.OutputFormat) target.OutputFormat = after.OutputFormat;
        merged.Validate();
        return merged;
    }

    public void Validate()
    {
        if (Engine == null || !Enum.IsDefined(Engine.TtsServiceType))
            throw new ArgumentException("Dubbing settings are incomplete.");
        if (string.IsNullOrWhiteSpace(Engine.Model))
            throw new ArgumentException("Choose the provisioned local dubbing model.");
        if (Engine.UseManualEngine && string.IsNullOrWhiteSpace(Engine.ManualVenvPython))
            throw new ArgumentException("Choose the Python executable in your existing dubbing environment.");
        if (!double.IsFinite(Engine.AtempoMin) || !double.IsFinite(Engine.AtempoMax))
            throw new ArgumentException("Time-stretch limits must be finite numbers between 0.5 and 2.0.");
    }

    public void Apply(Config.SubtitlesConfig target)
    {
        Validate();
        DubbingSettings copy = Clone();
        target.DubbingConfig = copy.Engine;
        target.PersistPerLineVoices = copy.PersistPerLineVoices;
    }
}

public sealed class DubbingSettingsStore(string path)
{
    public string? LoadWarning { get; private set; }
    public DubbingSettings Load()
    {
        LoadWarning = null;
        try
        {
            try { File.GetAttributes(path); }
            catch (FileNotFoundException) { return new(); }
            catch (DirectoryNotFoundException) { return new(); }
            DubbingSettings settings = JsonSerializer.Deserialize<DubbingSettings>(File.ReadAllText(path), DubbingSettings.JsonOptions)
                ?? throw new JsonException();
            settings.Validate();
            return settings;
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            LoadWarning = "Dubbing settings could not be read. Defaults are in use; the original file was preserved. Save explicitly to replace it.";
            return new();
        }
    }

    public void Save(DubbingSettings settings)
    {
        settings.Validate();
        string fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        AtomicFile.WriteAllText(fullPath, JsonSerializer.Serialize(settings, DubbingSettings.JsonOptions));
    }
}
