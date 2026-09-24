namespace FlyleafLib;

#nullable enable

/// <summary>
/// F-13 (Linux port): picks the audio output backend for a portable host.
/// <para>
/// Usage (host start-up, before <see cref="Engine.Start"/>, because <see cref="AudioEngine"/> enumerates the devices of
/// <see cref="AudioEngine.Backend"/> when the engine starts and every player creates its sinks from it):
/// <code>
/// AudioEngine.Backend = AudioBackendFactory.CreateDefault();
/// Engine.Start(engineConfig);
/// </code>
/// </para>
/// <para>
/// Selection (<see cref="BackendVariable"/> = <c>LLPLAYER_AUDIO_BACKEND</c>, case-insensitive):
/// <list type="bullet">
/// <item>unset / empty / <c>auto</c>: <see cref="OpenAlAudioBackend"/> when the OpenAL library loads and its default device
/// opens, otherwise <see cref="NullAudioBackend"/> (silent, real-time clock) with a logged warning.</item>
/// <item><c>openal</c>: OpenAL whenever the library loads, without probing a device (a device may appear later; a sink that
/// cannot open its device disables audio for that player, as on Windows). Falls back to Null only when the library
/// itself is missing.</item>
/// <item><c>null</c>: always <see cref="NullAudioBackend"/> (headless runs, CI).</item>
/// <item>anything else: warning, then <c>auto</c>.</item>
/// </list>
/// </para>
/// </summary>
public static class AudioBackendFactory
{
    /// <summary>Environment variable that forces the backend: <c>null</c>, <c>openal</c> or <c>auto</c>.</summary>
    public const string BackendVariable = "LLPLAYER_AUDIO_BACKEND";

    /// <summary>
    /// Selects the backend as described on <see cref="AudioBackendFactory"/>. Messages go to <paramref name="log"/> when
    /// given, otherwise to the engine log once the engine is loaded and to <see cref="System.Diagnostics.Trace"/>.
    /// </summary>
    public static IAudioBackend CreateDefault(Action<string>? log = null)
        => Create(
            Environment.GetEnvironmentVariable(BackendVariable),
            () => OpenAlNative.TryGet(out var api, out string? error) ? (api, null) : (null, error),
            log ?? DefaultLog);

    internal static IAudioBackend Create(string? requested, Func<(IOpenAl? api, string? error)> loadOpenAl, Action<string> log)
    {
        string choice = requested?.Trim().ToLowerInvariant() ?? "";

        switch (choice)
        {
            case "null":
                log($"Audio backend: Null ({BackendVariable}=null)");
                return NullAudioBackend.Instance;

            case "":
            case "auto":
            case "openal":
                break;

            default:
                log($"Audio backend: unknown {BackendVariable} value '{requested}' (expected null, openal or auto); using auto");
                choice = "auto";
                break;
        }

        var (api, error) = loadOpenAl();
        if (api == null)
        {
            log($"Audio backend: Null (no sound output) because OpenAL is unavailable: {error}");
            return NullAudioBackend.Instance;
        }

        OpenAlAudioBackend backend = new(api);
        if (choice == "openal")
        {
            log($"Audio backend: OpenAL ({BackendVariable}=openal)");
            return backend;
        }

        bool canOpen;
        try
        {
            canOpen = backend.CanOpenDefaultDevice();
        }
        catch (Exception e)
        {
            log($"Audio backend: Null (no sound output) because probing the OpenAL default device failed: {e.Message}");
            return NullAudioBackend.Instance;
        }

        if (!canOpen)
        {
            log("Audio backend: Null (no sound output) because the OpenAL default device cannot be opened (no sound card / sound server?)");
            return NullAudioBackend.Instance;
        }

        log("Audio backend: OpenAL");
        return backend;
    }

    static void DefaultLog(string message)
    {
        System.Diagnostics.Trace.WriteLine(message);
        if (Engine.IsLoaded)
            Engine.Log?.Info(message);
    }
}
