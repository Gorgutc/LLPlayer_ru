namespace FlyleafLib;

#nullable enable

/// <summary>
/// F-13 (Linux port): makes an OpenAL context current for the AL calls issued inside a <c>using</c> block.
/// <para>
/// AL calls act on "the current context". With ALC_EXT_thread_local_context (OpenAL Soft) the context is set for the
/// calling thread only (<c>alcSetThreadContext</c>) and cleared again on exit, so sinks of different players on different
/// threads never interfere and no process-wide lock is needed. Without it the current context is process-wide
/// (<c>alcMakeContextCurrent</c>), so every scope holds one process-wide lock (<see cref="ProcessWideLock"/>) for its
/// whole duration: several players (each with its own context) may exist, and their AL calls must not interleave.
/// </para>
/// <para>Lock order: a sink takes its own lock first and then enters a scope; never the other way round.</para>
/// </summary>
internal ref struct OpenAlContextScope
{
    /// <summary>Serializes all AL calls when thread-local contexts are unavailable.</summary>
    internal static readonly Lock ProcessWideLock = new();

    readonly IOpenAl    al;
    readonly bool       threadLocal;
    bool                active;

    public OpenAlContextScope(IOpenAl al, nint context)
    {
        this.al     = al;
        threadLocal = al.SupportsThreadLocalContext;

        if (threadLocal)
        {
            if (!al.AlcSetThreadContext(context))
                throw new InvalidOperationException("OpenAL: alcSetThreadContext failed");
        }
        else
        {
            ProcessWideLock.Enter();
            if (!al.AlcMakeContextCurrent(context))
            {
                ProcessWideLock.Exit();
                throw new InvalidOperationException("OpenAL: alcMakeContextCurrent failed");
            }
        }

        active = true;
    }

    public void Dispose()
    {
        if (!active)
            return;

        active = false;
        if (threadLocal)
            al.AlcSetThreadContext(0); // do not leave a (soon destroyed) context referenced by this thread
        else
            ProcessWideLock.Exit();
    }
}
