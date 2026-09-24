using FlyleafLib;

namespace LLPlayer.Avalonia.Services;

/// <summary>
/// Chooses the FlyleafLib audio output for the Linux app: FlyleafLib's <see cref="AudioBackendFactory"/> (OpenAL Soft
/// when <c>libopenal.so.1</c> loads and its default device opens, else the silent real-time
/// <see cref="NullAudioBackend"/>; <c>LLPLAYER_AUDIO_BACKEND=null|openal|auto</c> forces the choice).
/// </summary>
public static class AudioBackendSelector
{
    /// <summary>The backend selected by <see cref="AudioBackendFactory.CreateDefault"/>; its messages go to <paramref name="log"/>.</summary>
    public static IAudioBackend Select(Action<string>? log = null) => AudioBackendFactory.CreateDefault(log);

    /// <summary>
    /// Assigns <see cref="Select"/>'s backend to <see cref="AudioEngine.Backend"/>. Must run before <c>Engine.Start</c>
    /// (the engine enumerates the backend's devices on start). Returns the selection messages: the engine log does
    /// not exist yet, so the caller writes them once the engine has started (see <see cref="EngineBootstrap.Start"/>).
    /// </summary>
    public static IReadOnlyList<string> Apply()
    {
        List<string> messages = [];
        AudioEngine.Backend = Select(messages.Add);
        return messages;
    }

    /// <summary>
    /// Whether a selection message reports a fallback to silence that the user did not ask for (logged as a warning:
    /// the default engine log level is Warn).
    /// </summary>
    public static bool IsWarning(string message)
        => message.Contains(" because ", StringComparison.Ordinal) || message.Contains("unknown ", StringComparison.Ordinal);
}
