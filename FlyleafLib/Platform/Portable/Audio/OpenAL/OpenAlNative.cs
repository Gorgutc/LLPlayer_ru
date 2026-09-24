using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace FlyleafLib;

#nullable enable

/// <summary>
/// F-13 (Linux port): minimal OpenAL binding over the system OpenAL Soft library (<c>libopenal.so.1</c>, then
/// <c>libopenal.so</c>).
/// <para>
/// The library is loaded with <see cref="NativeLibrary.TryLoad(string, out nint)"/> and every entry point is bound as an
/// unmanaged function pointer from <see cref="NativeLibrary.GetExport"/>. This keeps the binding scoped to this type:
/// no <c>DllImport</c> resolver is registered for the FlyleafLib assembly (<c>NativeLibrary.SetDllImportResolver</c>
/// can be set only once per assembly and would collide with any other binding in FlyleafLib), a missing library is a
/// clean "not available" result instead of a <see cref="DllNotFoundException"/> at the first call, and the library is
/// probed only when the host asks for the OpenAL backend. Extension entry points come from
/// <c>alcGetProcAddress</c> / <c>alGetProcAddress</c> after checking the extension.
/// </para>
/// </summary>
internal sealed unsafe class OpenAlNative : IOpenAl
{
    /// <summary>Probe order (Linux first; the portable TFM also builds on macOS/Windows hosts).</summary>
    internal static readonly string[] LibraryNames = ["libopenal.so.1", "libopenal.so", "libopenal.1.dylib", "libopenal.dylib", "soft_oal.dll", "OpenAL32.dll"];

    static readonly Lazy<(OpenAlNative? api, string? error)> instance = new(Load, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Loads (once per process) and returns the OpenAL binding, or the reason it is unavailable.</summary>
    public static bool TryGet([NotNullWhen(true)] out OpenAlNative? api, out string? error)
    {
        (api, error) = instance.Value;
        return api != null;
    }

    /// <summary>File name of the loaded library (for logs).</summary>
    public string LibraryName { get; }

    // ALC
    readonly delegate* unmanaged<byte*, nint>                       alcOpenDevice;
    readonly delegate* unmanaged<nint, byte>                        alcCloseDevice;
    readonly delegate* unmanaged<nint, int*, nint>                  alcCreateContext;
    readonly delegate* unmanaged<nint, void>                        alcDestroyContext;
    readonly delegate* unmanaged<nint, byte>                        alcMakeContextCurrent;
    readonly delegate* unmanaged<nint, int>                         alcGetError;
    readonly delegate* unmanaged<nint, int, byte*>                  alcGetString;
    readonly delegate* unmanaged<nint, byte*, byte>                 alcIsExtensionPresent;
    readonly delegate* unmanaged<nint, byte*, nint>                 alcGetProcAddress;
    readonly delegate* unmanaged<nint, int, int, int*, void>        alcGetIntegerv;
    readonly delegate* unmanaged<nint, byte>                        alcSetThreadContext;        // extension (may be null)
    readonly delegate* unmanaged<nint, int, int, long*, void>       alcGetInteger64vSOFT;       // extension (may be null)

    // AL
    readonly delegate* unmanaged<int>                               alGetError;
    readonly delegate* unmanaged<byte*, byte>                       alIsExtensionPresent;
    readonly delegate* unmanaged<byte*, nint>                       alGetProcAddress;
    readonly delegate* unmanaged<int, uint*, void>                  alGenSources;
    readonly delegate* unmanaged<int, uint*, void>                  alDeleteSources;
    readonly delegate* unmanaged<int, uint*, void>                  alGenBuffers;
    readonly delegate* unmanaged<int, uint*, void>                  alDeleteBuffers;
    readonly delegate* unmanaged<uint, int, void*, int, int, void>  alBufferData;
    readonly delegate* unmanaged<uint, int, uint*, void>            alSourceQueueBuffers;
    readonly delegate* unmanaged<uint, int, uint*, void>            alSourceUnqueueBuffers;
    readonly delegate* unmanaged<uint, int, int*, void>             alGetSourcei;
    readonly delegate* unmanaged<uint, void>                        alSourcePlay;
    readonly delegate* unmanaged<uint, void>                        alSourcePause;
    readonly delegate* unmanaged<uint, void>                        alSourceStop;
    readonly delegate* unmanaged<uint, void>                        alSourceRewind;
    readonly delegate* unmanaged<uint, int, float, void>            alSourcef;
    readonly delegate* unmanaged<uint, int, int, void>              alSourcei;
    readonly delegate* unmanaged<int, float, void>                  alListenerf;

    // AL_SOFT_source_latency (resolved lazily: alGetProcAddress; 0 = not resolved yet, -1 = unavailable)
    nint alGetSourcei64vSOFT;

    OpenAlNative(nint lib, string libraryName)
    {
        LibraryName = libraryName;

        alcOpenDevice           = (delegate* unmanaged<byte*, nint>)                    NativeLibrary.GetExport(lib, "alcOpenDevice");
        alcCloseDevice          = (delegate* unmanaged<nint, byte>)                     NativeLibrary.GetExport(lib, "alcCloseDevice");
        alcCreateContext        = (delegate* unmanaged<nint, int*, nint>)               NativeLibrary.GetExport(lib, "alcCreateContext");
        alcDestroyContext       = (delegate* unmanaged<nint, void>)                     NativeLibrary.GetExport(lib, "alcDestroyContext");
        alcMakeContextCurrent   = (delegate* unmanaged<nint, byte>)                     NativeLibrary.GetExport(lib, "alcMakeContextCurrent");
        alcGetError             = (delegate* unmanaged<nint, int>)                      NativeLibrary.GetExport(lib, "alcGetError");
        alcGetString            = (delegate* unmanaged<nint, int, byte*>)               NativeLibrary.GetExport(lib, "alcGetString");
        alcIsExtensionPresent   = (delegate* unmanaged<nint, byte*, byte>)              NativeLibrary.GetExport(lib, "alcIsExtensionPresent");
        alcGetProcAddress       = (delegate* unmanaged<nint, byte*, nint>)              NativeLibrary.GetExport(lib, "alcGetProcAddress");
        alcGetIntegerv          = (delegate* unmanaged<nint, int, int, int*, void>)     NativeLibrary.GetExport(lib, "alcGetIntegerv");

        alGetError              = (delegate* unmanaged<int>)                            NativeLibrary.GetExport(lib, "alGetError");
        alIsExtensionPresent    = (delegate* unmanaged<byte*, byte>)                    NativeLibrary.GetExport(lib, "alIsExtensionPresent");
        alGetProcAddress        = (delegate* unmanaged<byte*, nint>)                    NativeLibrary.GetExport(lib, "alGetProcAddress");
        alGenSources            = (delegate* unmanaged<int, uint*, void>)               NativeLibrary.GetExport(lib, "alGenSources");
        alDeleteSources         = (delegate* unmanaged<int, uint*, void>)               NativeLibrary.GetExport(lib, "alDeleteSources");
        alGenBuffers            = (delegate* unmanaged<int, uint*, void>)               NativeLibrary.GetExport(lib, "alGenBuffers");
        alDeleteBuffers         = (delegate* unmanaged<int, uint*, void>)               NativeLibrary.GetExport(lib, "alDeleteBuffers");
        alBufferData            = (delegate* unmanaged<uint, int, void*, int, int, void>)NativeLibrary.GetExport(lib, "alBufferData");
        alSourceQueueBuffers    = (delegate* unmanaged<uint, int, uint*, void>)         NativeLibrary.GetExport(lib, "alSourceQueueBuffers");
        alSourceUnqueueBuffers  = (delegate* unmanaged<uint, int, uint*, void>)         NativeLibrary.GetExport(lib, "alSourceUnqueueBuffers");
        alGetSourcei            = (delegate* unmanaged<uint, int, int*, void>)          NativeLibrary.GetExport(lib, "alGetSourcei");
        alSourcePlay            = (delegate* unmanaged<uint, void>)                     NativeLibrary.GetExport(lib, "alSourcePlay");
        alSourcePause           = (delegate* unmanaged<uint, void>)                     NativeLibrary.GetExport(lib, "alSourcePause");
        alSourceStop            = (delegate* unmanaged<uint, void>)                     NativeLibrary.GetExport(lib, "alSourceStop");
        alSourceRewind          = (delegate* unmanaged<uint, void>)                     NativeLibrary.GetExport(lib, "alSourceRewind");
        alSourcef               = (delegate* unmanaged<uint, int, float, void>)         NativeLibrary.GetExport(lib, "alSourcef");
        alSourcei               = (delegate* unmanaged<uint, int, int, void>)           NativeLibrary.GetExport(lib, "alSourcei");
        alListenerf             = (delegate* unmanaged<int, float, void>)               NativeLibrary.GetExport(lib, "alListenerf");

        if (IsAlcExtensionPresent(0, "ALC_EXT_thread_local_context"))
            alcSetThreadContext = (delegate* unmanaged<nint, byte>)AlcProc(0, "alcSetThreadContext");

        // Device-level extension, but OpenAL Soft exports the function itself; presence is re-checked per device.
        alcGetInteger64vSOFT = (delegate* unmanaged<nint, int, int, long*, void>)AlcProc(0, "alcGetInteger64vSOFT");
    }

    static (OpenAlNative? api, string? error) Load()
    {
        List<string> errors = [];
        foreach (string name in LibraryNames)
        {
            if (!NativeLibrary.TryLoad(name, out nint lib))
                continue;

            try
            {
                return (new OpenAlNative(lib, name), null);
            }
            catch (EntryPointNotFoundException e)
            {
                errors.Add($"{name}: {e.Message}");
                NativeLibrary.Free(lib);
            }
        }

        return (null, errors.Count > 0
            ? $"OpenAL library is incomplete ({string.Join("; ", errors)})"
            : $"OpenAL library not found (tried {string.Join(", ", LibraryNames)})");
    }

    nint AlcProc(nint device, string name)
    {
        byte[] n = Utf8z(name);
        fixed (byte* p = n)
            return alcGetProcAddress(device, p);
    }

    static byte[] Utf8z(string s)
    {
        byte[] bytes = new byte[Encoding.UTF8.GetByteCount(s) + 1];
        Encoding.UTF8.GetBytes(s, bytes);
        return bytes;
    }

    static string? FromUtf8z(byte* p)
        => p == null ? null : Marshal.PtrToStringUTF8((nint)p);

    /// <summary>
    /// Parses an ALC string list: UTF-8 strings separated by NUL and terminated by an empty string (double NUL).
    /// Also stops at the end of <paramref name="data"/>; empty entries are never returned.
    /// </summary>
    internal static List<string> ParseStringList(ReadOnlySpan<byte> data)
    {
        List<string> list = [];
        while (!data.IsEmpty)
        {
            int len = data.IndexOf((byte)0);
            if (len == 0)
                break; // double NUL: end of list

            if (len < 0)
                len = data.Length; // unterminated tail (defensive)

            list.Add(Encoding.UTF8.GetString(data[..len]));
            data = len < data.Length ? data[(len + 1)..] : [];
        }

        return list;
    }

    // === IOpenAl ==================================================================================================

    public bool IsAlcExtensionPresent(nint device, string name)
    {
        byte[] n = Utf8z(name);
        fixed (byte* p = n)
            return alcIsExtensionPresent(device, p) != 0;
    }

    public string? AlcGetString(nint device, int param)
        => FromUtf8z(alcGetString(device, param));

    public IReadOnlyList<string> AlcGetStringList(nint device, int param)
    {
        byte* p = alcGetString(device, param);
        if (p == null || p[0] == 0) // no list / empty list (never read past a lone terminator)
            return [];

        int len = 0;
        while (p[len] != 0 || p[len + 1] != 0) // find the double NUL (the list always ends with one)
            len++;

        return ParseStringList(new ReadOnlySpan<byte>(p, len + 2));
    }

    public nint AlcOpenDevice(string? name)
    {
        if (name == null)
            return alcOpenDevice(null);

        byte[] n = Utf8z(name);
        fixed (byte* p = n)
            return alcOpenDevice(p);
    }

    public bool AlcCloseDevice(nint device)
        => alcCloseDevice(device) != 0;

    public nint AlcCreateContext(nint device, ReadOnlySpan<int> attributes)
    {
        fixed (int* p = attributes)
            return alcCreateContext(device, attributes.IsEmpty ? null : p);
    }

    public void AlcDestroyContext(nint context)
        => alcDestroyContext(context);

    public int AlcGetError(nint device)
        => alcGetError(device);

    public bool AlcIsConnected(nint device)
    {
        if (!IsAlcExtensionPresent(device, "ALC_EXT_disconnect"))
            return true;

        int connected = 1;
        alcGetIntegerv(device, Al.ALC_CONNECTED, 1, &connected);
        return connected != 0;
    }

    public long AlcGetDeviceLatencyNs(nint device)
    {
        if (alcGetInteger64vSOFT == null || !IsAlcExtensionPresent(device, "ALC_SOFT_device_clock"))
            return -1;

        long latency = -1;
        alcGetInteger64vSOFT(device, Al.ALC_DEVICE_LATENCY_SOFT, 1, &latency);
        return latency;
    }

    public bool SupportsThreadLocalContext
        => alcSetThreadContext != null;

    public bool AlcSetThreadContext(nint context)
        => alcSetThreadContext != null && alcSetThreadContext(context) != 0;

    public bool AlcMakeContextCurrent(nint context)
        => alcMakeContextCurrent(context) != 0;

    public bool IsAlExtensionPresent(string name)
    {
        byte[] n = Utf8z(name);
        fixed (byte* p = n)
            return alIsExtensionPresent(p) != 0;
    }

    public int AlGetError()
        => alGetError();

    public uint AlGenSource()
    {
        uint id = 0;
        alGenSources(1, &id);
        return id;
    }

    public void AlDeleteSource(uint source)
        => alDeleteSources(1, &source);

    public uint AlGenBuffer()
    {
        uint id = 0;
        alGenBuffers(1, &id);
        return id;
    }

    public void AlDeleteBuffer(uint buffer)
        => alDeleteBuffers(1, &buffer);

    public void AlBufferData(uint buffer, int format, nint data, int size, int frequency)
        => alBufferData(buffer, format, (void*)data, size, frequency);

    public void AlSourceQueueBuffer(uint source, uint buffer)
        => alSourceQueueBuffers(source, 1, &buffer);

    public void AlSourceUnqueueBuffers(uint source, Span<uint> buffers)
    {
        if (buffers.IsEmpty)
            return;

        fixed (uint* p = buffers)
            alSourceUnqueueBuffers(source, buffers.Length, p);
    }

    public int AlGetSourcei(uint source, int param)
    {
        int value = 0;
        alGetSourcei(source, param, &value);
        return value;
    }

    public bool TryGetSourceLatencyNs(uint source, out long latencyNs)
    {
        latencyNs = 0;

        nint fn = Volatile.Read(ref alGetSourcei64vSOFT);
        if (fn == 0)
        {
            byte[] name = Utf8z("alGetSourcei64vSOFT");
            fixed (byte* p = name)
                fn = IsAlExtensionPresent("AL_SOFT_source_latency") ? alGetProcAddress(p) : 0;

            if (fn == 0)
                fn = -1;

            Volatile.Write(ref alGetSourcei64vSOFT, fn);
        }

        if (fn == -1)
            return false;

        long* values = stackalloc long[2]; // [0] = sample offset (32.32 fixed point), [1] = latency (ns)
        values[0] = values[1] = 0;
        ((delegate* unmanaged<uint, int, long*, void>)fn)(source, Al.SAMPLE_OFFSET_LATENCY_SOFT, values);
        latencyNs = values[1];
        return true;
    }

    public void AlSourcePlay(uint source)
        => alSourcePlay(source);

    public void AlSourcePause(uint source)
        => alSourcePause(source);

    public void AlSourceStop(uint source)
        => alSourceStop(source);

    public void AlSourceRewind(uint source)
        => alSourceRewind(source);

    public void AlSourcef(uint source, int param, float value)
        => alSourcef(source, param, value);

    public void AlSourcei(uint source, int param, int value)
        => alSourcei(source, param, value);

    public void AlListenerf(int param, float value)
        => alListenerf(param, value);
}
