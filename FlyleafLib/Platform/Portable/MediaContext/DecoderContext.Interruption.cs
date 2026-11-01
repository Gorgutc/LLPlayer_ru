namespace FlyleafLib.MediaFramework.MediaContext;

public unsafe partial class DecoderContext
{
    // This gate only protects the interrupt value, its demuxer flags and write revision.
    // It never acquires the player, decoder, demuxer or host's mutation locks.
    readonly object interruptOwnership = new();
    long interruptWriteRevision;

    /// <summary>Requests interruption only if no existing writer already owns it. Zero grants no ownership.</summary>
    public long RequestOwnedInterrupt()
    {
        lock (interruptOwnership)
        {
            if (Interrupt)
                return 0;
            Interrupt = true;
            return interruptWriteRevision;
        }
    }

    /// <summary>Releases this request only if no later interrupt-property write has superseded it.</summary>
    public void ReleaseOwnedInterrupt(long revision)
    {
        if (revision == 0)
            return;
        lock (interruptOwnership)
        {
            if (interruptWriteRevision == revision)
                Interrupt = false;
        }
    }
}
