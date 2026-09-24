using System.Runtime.InteropServices;

namespace FlyleafLib.Platform.Audio;

/// <summary>
/// In-memory model of the OpenAL streaming-source behaviour <see cref="OpenAlAudioSink"/> relies on (OpenAL 1.1 spec +
/// OpenAL Soft specifics), driven by a manual clock (<see cref="Advance"/>), so buffer accounting can be tested without
/// libopenal or a sound device:
/// <list type="bullet">
/// <item>AL_SAMPLE_OFFSET is relative to the head of the queue (processed-but-not-unqueued buffers included) and 0 unless
/// PLAYING/PAUSED;</item>
/// <item>a PLAYING source that consumes its whole queue becomes STOPPED (underrun); on a STOPPED source every queued
/// buffer (even one queued after it stopped) counts as processed; alSourcePlay on a STOPPED/INITIAL source restarts
/// at the queue head, on a PLAYING source it restarts from the beginning too;</item>
/// <item>unqueueing more buffers than processed, deleting/refilling a queued buffer, or calling AL with no (or the wrong)
/// current context is an error. Context misuse throws <see cref="InvalidOperationException"/> immediately
/// (<see cref="ContextViolations"/>) so a missing context switch/serialization fails the test.</item>
/// </list>
/// Thread-safe (one lock); the current context is tracked per thread (thread-local mode) or process-wide.
/// </summary>
internal sealed class FakeOpenAl : IOpenAl
{
    public const int AL_INVALID_NAME        = 0xA001;
    public const int AL_INVALID_VALUE       = 0xA003;
    public const int AL_INVALID_OPERATION   = 0xA004;

    public sealed class Device
    {
        public nint     Handle;
        public string?  Name;
        public bool     Closed;
        public bool     Connected = true;
    }

    public sealed class Context
    {
        public nint     Handle;
        public Device   Device = null!;
        public int[]    Attributes = [];
        public bool     Destroyed;
        public float    ListenerGain = 1;
        public int      Error;
    }

    public sealed class Buffer
    {
        public uint     Id;
        public Context  Context = null!;
        public int      Samples;
        public byte[]   Data = [];
        public int      Frequency;
        public int      Format;
        public bool     Deleted;
        public uint     QueuedOn;   // source id (0 = not queued)
    }

    public sealed class Source
    {
        public uint     Id;
        public Context  Context = null!;
        public int      State = Al.INITIAL;
        public readonly List<Buffer> Queue = [];
        public long     Position;   // samples from the head of Queue (PLAYING/PAUSED only)
        public readonly Dictionary<int, float>  Floats = [];
        public readonly Dictionary<int, int>    Ints = [];
        public bool     Deleted;
        public int      PlayCalls;
    }

    readonly object locker = new();
    readonly Dictionary<nint, Device>   devices = [];
    readonly Dictionary<nint, Context>  contexts = [];
    readonly Dictionary<uint, Source>   sources = [];
    readonly Dictionary<uint, Buffer>   buffers = [];
    readonly ThreadLocal<nint>          threadContext = new();
    nint    globalContext;
    nint    nextHandle = 0x1000;
    uint    nextName = 1;

    // === Configuration ===========================================================================================

    public bool                 ThreadLocalContexts { get; set; } = true;
    public HashSet<string>      AlcExtensions { get; } = ["ALC_ENUMERATE_ALL_EXT", "ALC_ENUMERATION_EXT"];
    public HashSet<string>      AlExtensions { get; } = ["AL_SOFT_direct_channels", "AL_SOFT_source_latency"];
    public List<string>         AllDevices { get; } = ["Fake Speakers", "Fake Headphones"];
    public List<string>         BasicDevices { get; } = ["OpenAL Soft"];
    public string?              DefaultAllDevice { get; set; } = "Fake Speakers";
    public string?              DefaultBasicDevice { get; set; } = "OpenAL Soft";
    public bool                 FailOpenDevice { get; set; }
    public bool                 FailCreateContext { get; set; }
    public bool                 FailSetContext { get; set; }
    public long                 SourceLatencyNs { get; set; } = 20_000_000;
    public long                 DeviceLatencyNs { get; set; } = -1;
    public Action?              BeforeQueueBuffer { get; set; }

    // === Observations ============================================================================================

    public int  OpenDeviceCalls         { get; private set; }
    public int  CloseDeviceCalls        { get; private set; }
    public int  DestroyContextCalls     { get; private set; }
    public int  GenBufferCalls          { get; private set; }
    public int  MakeContextCurrentCalls { get; private set; }
    public int  SetThreadContextCalls   { get; private set; }
    public int  ContextViolations       { get; private set; }
    public List<string> OpenedDeviceNames { get; } = [];
    public List<string> Calls { get; } = [];

    public IReadOnlyCollection<Device>  Devices     { get { lock (locker) return [.. devices.Values]; } }
    public IReadOnlyCollection<Context> Contexts    { get { lock (locker) return [.. contexts.Values]; } }
    public IReadOnlyCollection<Source>  Sources     { get { lock (locker) return [.. sources.Values]; } }
    public int LiveBuffers { get { lock (locker) return buffers.Values.Count(b => !b.Deleted); } }

    public Source SingleSource { get { lock (locker) return sources.Values.Single(); } }

    // === Manual clock ============================================================================================

    /// <summary>Plays <paramref name="samples"/> sample frames on every PLAYING source (underrun stops it).</summary>
    public void Advance(long samples)
    {
        lock (locker)
            foreach (var src in sources.Values)
            {
                if (src.State != Al.PLAYING)
                    continue;

                long total = src.Queue.Sum(b => (long)b.Samples);
                src.Position = Math.Min(total, src.Position + samples);
                if (src.Position >= total)
                {
                    src.State       = Al.STOPPED;
                    src.Position    = 0;
                }
            }
    }

    public void Disconnect()
    {
        lock (locker)
            foreach (var d in devices.Values)
                d.Connected = false;
    }

    // === helpers =================================================================================================

    Context Current()
    {
        nint handle = ThreadLocalContexts && threadContext.Value != 0 ? threadContext.Value : globalContext;
        if (handle == 0 || !contexts.TryGetValue(handle, out var ctx) || ctx.Destroyed)
        {
            ContextViolations++;
            throw new InvalidOperationException("AL call without a valid current context");
        }

        return ctx;
    }

    Source Src(uint id)
    {
        var ctx = Current();
        if (!sources.TryGetValue(id, out var src) || src.Deleted)
        {
            ctx.Error = AL_INVALID_NAME;
            return new Source { Context = ctx, Deleted = true };
        }

        if (src.Context != ctx)
        {
            ContextViolations++;
            throw new InvalidOperationException($"source {id} used while another context is current");
        }

        return src;
    }

    static int Processed(Source src)
    {
        if (src.State == Al.INITIAL)
            return 0;

        if (src.State == Al.STOPPED)
            return src.Queue.Count;

        int processed = 0;
        long end = 0;
        foreach (var b in src.Queue)
        {
            end += b.Samples;
            if (src.Position >= end)
                processed++;
            else
                break;
        }

        return processed;
    }

    // === IOpenAl: ALC ============================================================================================

    public bool IsAlcExtensionPresent(nint device, string name)
    {
        lock (locker)
            return AlcExtensions.Contains(name);
    }

    public string? AlcGetString(nint device, int param)
    {
        lock (locker)
            return param switch
            {
                Al.ALC_DEFAULT_ALL_DEVICES_SPECIFIER => DefaultAllDevice,
                Al.ALC_DEFAULT_DEVICE_SPECIFIER      => DefaultBasicDevice,
                Al.ALC_DEVICE_SPECIFIER when devices.TryGetValue(device, out var d) => d.Name ?? DefaultAllDevice,
                _ => null
            };
    }

    public IReadOnlyList<string> AlcGetStringList(nint device, int param)
    {
        lock (locker)
            return param switch
            {
                Al.ALC_ALL_DEVICES_SPECIFIER => [.. AllDevices],
                Al.ALC_DEVICE_SPECIFIER      => [.. BasicDevices],
                _ => []
            };
    }

    public nint AlcOpenDevice(string? name)
    {
        lock (locker)
        {
            OpenDeviceCalls++;
            OpenedDeviceNames.Add(name ?? "<default>");
            if (FailOpenDevice || (name != null && !AllDevices.Contains(name) && !BasicDevices.Contains(name)))
                return 0;

            Device d = new() { Handle = nextHandle++, Name = name };
            devices[d.Handle] = d;
            return d.Handle;
        }
    }

    public bool AlcCloseDevice(nint device)
    {
        lock (locker)
        {
            CloseDeviceCalls++;
            if (!devices.TryGetValue(device, out var d) || d.Closed)
                return false;

            if (contexts.Values.Any(c => c.Device == d && !c.Destroyed))
                return false; // ALC_INVALID_DEVICE: contexts still exist

            d.Closed = true;
            return true;
        }
    }

    public nint AlcCreateContext(nint device, ReadOnlySpan<int> attributes)
    {
        lock (locker)
        {
            if (FailCreateContext || !devices.TryGetValue(device, out var d) || d.Closed)
                return 0;

            Context c = new() { Handle = nextHandle++, Device = d, Attributes = attributes.ToArray() };
            contexts[c.Handle] = c;
            return c.Handle;
        }
    }

    public void AlcDestroyContext(nint context)
    {
        lock (locker)
        {
            DestroyContextCalls++;
            if (!contexts.TryGetValue(context, out var c) || c.Destroyed)
                return;

            if (globalContext == context)
                throw new InvalidOperationException("fake: a context must be un-current before it is destroyed (the sink clears it first)");

            c.Destroyed = true;
            foreach (var s in sources.Values.Where(s => s.Context == c))
                s.Deleted = true;
            foreach (var b in buffers.Values.Where(b => b.Context == c))
                b.Deleted = true;
        }
    }

    public int AlcGetError(nint device) => Al.ALC_NO_ERROR;

    public bool AlcIsConnected(nint device)
    {
        lock (locker)
            return !devices.TryGetValue(device, out var d) || d.Connected;
    }

    public long AlcGetDeviceLatencyNs(nint device)
    {
        lock (locker)
            return AlcExtensions.Contains("ALC_SOFT_device_clock") ? DeviceLatencyNs : -1;
    }

    public bool SupportsThreadLocalContext => ThreadLocalContexts;

    public bool AlcSetThreadContext(nint context)
    {
        lock (locker)
        {
            SetThreadContextCalls++;
            if (!ThreadLocalContexts)
                throw new InvalidOperationException("alcSetThreadContext is not available");

            if (FailSetContext && context != 0)
                return false;

            if (context != 0 && (!contexts.TryGetValue(context, out var c) || c.Destroyed))
                return false;

            threadContext.Value = context;
            return true;
        }
    }

    public bool AlcMakeContextCurrent(nint context)
    {
        lock (locker)
        {
            MakeContextCurrentCalls++;
            if (FailSetContext && context != 0)
                return false;

            if (context != 0 && (!contexts.TryGetValue(context, out var c) || c.Destroyed))
                return false;

            globalContext = context;
            return true;
        }
    }

    // === IOpenAl: AL =============================================================================================

    public bool IsAlExtensionPresent(string name)
    {
        lock (locker)
        {
            Current();
            return AlExtensions.Contains(name);
        }
    }

    public int AlGetError()
    {
        lock (locker)
        {
            var ctx = Current();
            int err = ctx.Error;
            ctx.Error = Al.NO_ERROR;
            return err;
        }
    }

    public uint AlGenSource()
    {
        lock (locker)
        {
            Source s = new() { Id = nextName++, Context = Current() };
            sources[s.Id] = s;
            return s.Id;
        }
    }

    public void AlDeleteSource(uint source)
    {
        lock (locker)
        {
            var s = Src(source);
            if (s.Deleted)
                return;

            foreach (var b in s.Queue)
                b.QueuedOn = 0;
            s.Queue.Clear();
            s.Deleted = true;
        }
    }

    public uint AlGenBuffer()
    {
        lock (locker)
        {
            GenBufferCalls++;
            Buffer b = new() { Id = nextName++, Context = Current() };
            buffers[b.Id] = b;
            return b.Id;
        }
    }

    public void AlDeleteBuffer(uint buffer)
    {
        lock (locker)
        {
            var ctx = Current();
            if (!buffers.TryGetValue(buffer, out var b) || b.Deleted)
                { ctx.Error = AL_INVALID_NAME; return; }
            if (b.QueuedOn != 0)
                { ctx.Error = AL_INVALID_OPERATION; return; }

            b.Deleted = true;
        }
    }

    public void AlBufferData(uint buffer, int format, nint data, int size, int frequency)
    {
        lock (locker)
        {
            var ctx = Current();
            if (!buffers.TryGetValue(buffer, out var b) || b.Deleted)
                { ctx.Error = AL_INVALID_NAME; return; }
            if (b.QueuedOn != 0)
                { ctx.Error = AL_INVALID_OPERATION; return; }

            int frame = format == Al.FORMAT_STEREO16 ? 4 : format == Al.FORMAT_MONO16 ? 2 : 0;
            if (frame == 0 || size % frame != 0 || frequency <= 0)
                { ctx.Error = AL_INVALID_VALUE; return; }

            b.Data      = new byte[size];
            Marshal.Copy(data, b.Data, 0, size);
            b.Samples   = size / frame;
            b.Format    = format;
            b.Frequency = frequency;
        }
    }

    public void AlSourceQueueBuffer(uint source, uint buffer)
    {
        BeforeQueueBuffer?.Invoke();

        lock (locker)
        {
            var s = Src(source);
            var ctx = s.Context;
            if (s.Deleted)
                return;
            if (!buffers.TryGetValue(buffer, out var b) || b.Deleted)
                { ctx.Error = AL_INVALID_NAME; return; }
            if (b.QueuedOn != 0)
                { ctx.Error = AL_INVALID_OPERATION; return; }

            b.QueuedOn = s.Id;
            s.Queue.Add(b);
            Calls.Add("queue");
        }
    }

    public void AlSourceUnqueueBuffers(uint source, Span<uint> ids)
    {
        lock (locker)
        {
            var s = Src(source);
            if (s.Deleted)
                return;

            if (ids.Length > Processed(s))
                { s.Context.Error = AL_INVALID_VALUE; return; }

            for (int i = 0; i < ids.Length; i++)
            {
                var b = s.Queue[0];
                s.Queue.RemoveAt(0);
                b.QueuedOn = 0;
                ids[i] = b.Id;
                if (s.State is Al.PLAYING or Al.PAUSED)
                    s.Position -= b.Samples;
            }
        }
    }

    public int AlGetSourcei(uint source, int param)
    {
        lock (locker)
        {
            var s = Src(source);
            if (s.Deleted)
                return 0;

            return param switch
            {
                Al.SOURCE_STATE         => s.State,
                Al.BUFFERS_QUEUED       => s.Queue.Count,
                Al.BUFFERS_PROCESSED    => Processed(s),
                Al.SAMPLE_OFFSET        => s.State is Al.PLAYING or Al.PAUSED ? (int)s.Position : 0,
                _                       => s.Ints.GetValueOrDefault(param)
            };
        }
    }

    public bool TryGetSourceLatencyNs(uint source, out long latencyNs)
    {
        lock (locker)
        {
            Src(source);
            latencyNs = SourceLatencyNs;
            return AlExtensions.Contains("AL_SOFT_source_latency");
        }
    }

    public void AlSourcePlay(uint source)
    {
        lock (locker)
        {
            var s = Src(source);
            if (s.Deleted)
                return;

            s.PlayCalls++;
            Calls.Add("play");
            if (s.State != Al.PAUSED)
                s.Position = 0; // INITIAL/STOPPED start at the queue head; PLAYING restarts (spec)

            s.State = s.Queue.Count == 0 ? Al.STOPPED : Al.PLAYING;
        }
    }

    public void AlSourcePause(uint source)
    {
        lock (locker)
        {
            var s = Src(source);
            Calls.Add("pause");
            if (!s.Deleted && s.State == Al.PLAYING)
                s.State = Al.PAUSED;
        }
    }

    public void AlSourceStop(uint source)
    {
        lock (locker)
        {
            var s = Src(source);
            Calls.Add("stop");
            if (s.Deleted)
                return;

            s.State     = Al.STOPPED;
            s.Position  = 0;
        }
    }

    public void AlSourceRewind(uint source)
    {
        lock (locker)
        {
            var s = Src(source);
            Calls.Add("rewind");
            if (s.Deleted)
                return;

            s.State     = Al.INITIAL;
            s.Position  = 0;
        }
    }

    public void AlSourcef(uint source, int param, float value)
    {
        lock (locker)
        {
            var s = Src(source);
            if (!s.Deleted)
                s.Floats[param] = value;
        }
    }

    public void AlSourcei(uint source, int param, int value)
    {
        lock (locker)
        {
            var s = Src(source);
            if (s.Deleted)
                return;

            if (param == Al.BUFFER)
            {
                if (s.State is Al.PLAYING or Al.PAUSED)
                    { s.Context.Error = AL_INVALID_OPERATION; return; }

                foreach (var b in s.Queue)
                    b.QueuedOn = 0;
                s.Queue.Clear();
                return;
            }

            s.Ints[param] = value;
        }
    }

    public void AlListenerf(int param, float value)
    {
        lock (locker)
        {
            var ctx = Current();
            if (param == Al.GAIN)
                ctx.ListenerGain = value;
        }
    }
}
