using System.Collections.Generic;
using System.IO;
using FlyleafLib.MediaPlayer.Dubbing;

namespace FlyleafLib;

#nullable enable

/// <summary>
/// AI Dubbing configuration (TTS / voice synthesis), held under <c>Config.Subtitles</c> like the ASR
/// engine configs. Additive and absent-defaulting; the default backend is the local CosyVoice2
/// sidecar. Engine venv and model weights live beside the exe and are never tracked (see
/// dubbing-contract). A run-scoped sidecar is built from an immutable snapshot of this config.
/// </summary>
public class DubbingConfig : NotifyPropertyChanged
{
    /// <summary>Provisioned dub Python venv (uv-created). Never committed.</summary>
    public static string DefaultEngineDir { get; } = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DubEngine");
    /// <summary>Downloaded TTS model weights. Never committed.</summary>
    public static string ModelsDirectory { get; } = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dubmodels");
    /// <summary>Our committed sidecar entrypoint (GPLv3 source artifact).</summary>
    public static string DefaultServerScript { get; } = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dub_sidecar", "server.py");

    public TtsServiceType TtsServiceType { get; set => Set(ref field, value); } = TtsServiceType.LocalCosyVoice;

    /// <summary>Override the auto-provisioned venv python with a user-supplied path.</summary>
    public bool UseManualEngine { get; set => Set(ref field, value); }
    public string? ManualVenvPython { get; set => Set(ref field, value); }

    public string Model { get; set => Set(ref field, value); } = "cosyvoice2-0.5b";

    /// <summary>MVP single preset Russian voice; later overridable per speaker from the voice bank.
    /// Trimmed on set (like the picker trims custom ids) so a hand-edited config value with surrounding
    /// whitespace still matches the trimmed voice-picker entries and the engine's voice ids — otherwise the
    /// raw bound <c>SelectedValue</c> would not equal any (trimmed) picker entry and the ComboBox would blank.</summary>
    public string DefaultVoiceId { get; set => Set(ref field, value?.Trim() ?? ""); } = "ru-preset-1";

    /// <summary>User-declared extra voice ids merged into the voice picker (e.g. presets the user added to the
    /// local sidecar's <c>VOICES</c>), so a custom voice can be selected without hand-editing this file.
    /// Default empty → byte-identical (the picker shows only the built-in bank). The id is passed to the
    /// engine verbatim at synth time; LLPlayer does not validate it against a running engine.</summary>
    public List<string> CustomVoiceIds { get; set => Set(ref field, value ?? new()); } = new();

    /// <summary>Original-audio level under the dub during dubbed spans, 0..100. Drives the duck depth.
    /// Clamped to its valid range on set so a hand-edited config or UI echo cannot push it out of bounds.</summary>
    public int DuckingPercent { get; set => Set(ref field, Math.Clamp(value, 0, 100)); } = 15;

    /// <summary>Isochrony: capped pitch-preserving time-stretch (ffmpeg atempo) range.</summary>
    public double AtempoMin { get; set => Set(ref field, value); } = 0.9;
    public double AtempoMax { get; set => Set(ref field, value); } = 1.15;

    /// <summary>Mandatory Russian stress/homograph normalization before synthesis; graceful-degrades.</summary>
    public bool StressNormalization { get; set => Set(ref field, value); } = true;

    /// <summary>Dub container. FLAC avoids AAC encoder priming (A/V sync) and is the only format the
    /// sidecar currently encodes — a non-FLAC value degrades to a FLAC bitstream, so the picker UI
    /// offers FLAC only until a real AAC/m4a path exists.</summary>
    public string OutputFormat { get; set => Set(ref field, value); } = DubbingOutputPathBuilder.DefaultExtension;
}
