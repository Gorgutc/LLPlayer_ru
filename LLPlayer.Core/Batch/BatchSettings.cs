using System.Text.Json;
using System.Text;
using FlyleafLib;

namespace LLPlayer.Core.Batch;

public sealed class BatchSettings
{
    public string LastFolder { get; set; } = "";
    public bool Recursive { get; set; } = true;
    public bool OverwriteExisting { get; set; }
    public bool SerializeAsrAndTranslate { get; set; } = true;
    public bool RunOnCpuWhenActive { get; set; } = true;
    public int ActiveIdleThresholdSeconds { get; set; } = 45;
    public bool PreferRussianAudio { get; set; } = true;
    public bool GenerateDubbing { get; set; }
    public bool WatchFolder { get; set; }
    public BatchSettings Clone() => (BatchSettings)MemberwiseClone();
    public void Validate()
    {
        if (LastFolder == null || LastFolder.Contains('\0')) throw new ArgumentException("Invalid batch folder.");
        if (ActiveIdleThresholdSeconds is < 5 or > 600) throw new ArgumentException("Idle threshold must be between 5 and 600 seconds.");
    }
}

public sealed class BatchSettingsStore(string path)
{
    static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    public string? LoadWarning { get; private set; }
    public BatchSettings Load()
    {
        LoadWarning = null;
        if (!File.Exists(path)) return new();
        try
        {
            var settings = JsonSerializer.Deserialize<BatchSettings>(File.ReadAllText(path), Options) ?? throw new JsonException("Empty batch settings.");
            settings.Validate(); return settings;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or ArgumentException)
        { LoadWarning = $"Batch settings could not be loaded: {ex.Message} The original file was preserved."; return new(); }
    }
    public void Save(BatchSettings settings)
    {
        settings.Validate();
        string full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        AtomicFile.WriteAllText(full, JsonSerializer.Serialize(settings, Options));
    }

    /// <summary>First half of the bounded batch/voice save. Rollback only restores our own written bytes.</summary>
    public BatchSettingsSaveReceipt SaveReversible(BatchSettings settings)
    {
        settings.Validate();
        string full = Path.GetFullPath(path);
        byte[]? previous;
        try { previous = File.ReadAllBytes(full); }
        catch (FileNotFoundException) { previous = null; }
        catch (DirectoryNotFoundException) { previous = null; }
        string serialized = JsonSerializer.Serialize(settings, Options);
        byte[] candidate = Encoding.UTF8.GetBytes(serialized);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        AtomicFile.WriteAllText(full, serialized);
        return new(full, previous, candidate);
    }
}

/// <summary>Compensating persistence, not a crash-atomic transaction across independent settings owners.</summary>
public sealed class BatchSettingsSaveReceipt
{
    readonly string path;
    readonly byte[]? previous;
    readonly byte[] candidate;
    internal BatchSettingsSaveReceipt(string path, byte[]? previous, byte[] candidate)
    { this.path = path; this.previous = previous; this.candidate = candidate; }
    public void Rollback()
    {
        void CheckOwnership()
        {
            if (!File.Exists(path) || !File.ReadAllBytes(path).AsSpan().SequenceEqual(candidate))
                throw new IOException("Batch settings changed after this save. The newer file was preserved; rollback was not applied.");
        }
        CheckOwnership();
        if (previous == null) { CheckOwnership(); File.Delete(path); return; }
        string temp = Path.Combine(Path.GetDirectoryName(path)!, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllBytes(temp, previous);
            CheckOwnership();
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
