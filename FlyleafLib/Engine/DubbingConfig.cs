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

    /// <summary>MVP single preset Russian voice; later overridable per speaker from the voice bank.</summary>
    public string DefaultVoiceId { get; set => Set(ref field, value); } = "ru-preset-1";

    /// <summary>Original-audio level under the dub during dubbed spans, 0..100. Drives the duck depth.</summary>
    public int DuckingPercent { get; set => Set(ref field, value); } = 15;

    /// <summary>Isochrony: capped pitch-preserving time-stretch (ffmpeg atempo) range.</summary>
    public double AtempoMin { get; set => Set(ref field, value); } = 0.9;
    public double AtempoMax { get; set => Set(ref field, value); } = 1.15;

    /// <summary>Mandatory Russian stress/homograph normalization before synthesis; graceful-degrades.</summary>
    public bool StressNormalization { get; set => Set(ref field, value); } = true;

    /// <summary>Dub container. FLAC avoids AAC encoder priming (A/V sync); m4a is allowed.</summary>
    public string OutputFormat { get; set => Set(ref field, value); } = DubbingOutputPathBuilder.DefaultExtension;
}
