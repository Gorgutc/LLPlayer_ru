namespace FlyleafLib.MediaPlayer.Dubbing;

#nullable enable

/// <summary>
/// TTS / voice-synthesis backend for AI dubbing. Local engines are the default and run in a
/// separate-process sidecar (mere aggregation under the GPLv3 app). Cloud engines are an opt-in
/// slot requiring the user's own API key and default to preset voices only.
/// </summary>
public enum TtsServiceType
{
    /// <summary>Bundled default: CosyVoice2-0.5B (Apache-2.0) via the local Python sidecar.</summary>
    LocalCosyVoice,

    // Cloud slot (Phase 6) — user supplies their own paid key; preset voices only.
    ElevenLabs,
    AzureSpeech,
    Cartesia,

    // Advanced, user-installed only (non-commercial weights, never bundled).
    XttsV2Local,
    F5Local
}
