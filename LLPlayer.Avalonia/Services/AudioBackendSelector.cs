using FlyleafLib;

namespace LLPlayer.Avalonia.Services;

/// <summary>
/// Chooses the FlyleafLib audio output for the Linux app. Integration point for the OpenAL backend (F-13 audio work):
/// replace the body of <see cref="Select"/> with the audio backend factory; everything else (assigning it before
/// <c>Engine.Start</c>) is already wired.
/// </summary>
public static class AudioBackendSelector
{
    /// <summary>The backend to use. Until the OpenAL backend lands this is the silent real-time NullAudioBackend.</summary>
    public static IAudioBackend Select() => NullAudioBackend.Instance;

    /// <summary>Assigns <see cref="Select"/>'s backend to <see cref="AudioEngine.Backend"/>. Call before Engine.Start.</summary>
    public static void Apply() => AudioEngine.Backend = Select();
}
