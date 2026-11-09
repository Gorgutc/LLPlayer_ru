#nullable enable
namespace FlyleafLib.MediaFramework.MediaDecoder;

/// <summary>Owns only compressed references needed to reconstruct the currently decoded hardware GOP.</summary>
internal sealed unsafe class PortableHardwareRecoveryBuffer(int maxPackets = 2048, long maxBytes = 64 * 1024 * 1024) : IDisposable
{
    readonly List<nint> packets = [];
    internal IReadOnlyList<nint> Packets => packets;
    internal long Bytes { get; private set; }
    internal int DecodedFrames { get; private set; }

    internal bool TryRetain(AVPacket* packet, out string? reason)
    {
        long size = PacketBytes(packet);
        if (packets.Count >= maxPackets || size > maxBytes - Bytes)
        {
            reason = "Hardware recovery history reached its bounded packet/byte limit; continuing in software.";
            return false;
        }
        // Reserve managed storage before acquiring another native owner.
        try { packets.EnsureCapacity(packets.Count + 1); }
        catch (OutOfMemoryException)
        {
            reason = "The hardware decoder could not allocate recovery history; continuing in software.";
            return false;
        }
        AVPacket* retained = av_packet_clone(packet);
        if (retained == null)
        {
            reason = "The hardware decoder could not retain its recovery packet; continuing in software.";
            return false;
        }
        packets.Add((nint)retained);
        Bytes += size;
        reason = null;
        return true;
    }

    internal void FrameReceived(AVFrame* frame)
    {
        DecodedFrames++;
        if ((frame->flags & FrameFlags.Key) == 0)
            return;
        long pts = frame->best_effort_timestamp != AV_NOPTS_VALUE ? frame->best_effort_timestamp : frame->pts;
        if (pts == AV_NOPTS_VALUE)
            return;

        // Keep the previous GOP until its delayed pictures have actually left the decoder. Only an
        // unambiguous real key picture matching a retained packet permits retiring the previous prefix.
        int keyIndex = -1;
        for (int index = 0; index < packets.Count; index++)
        {
            AVPacket* packet = (AVPacket*)packets[index];
            if ((packet->flags & PktFlags.Key) == 0 || packet->pts != pts)
                continue;
            if (keyIndex != -1)
                return;
            keyIndex = index;
        }
        if (keyIndex <= 0)
            return;
        for (int index = 0; index < keyIndex; index++)
            Release((AVPacket*)packets[index]);
        packets.RemoveRange(0, keyIndex);
        DecodedFrames = 1;
    }

    void Release(AVPacket* packet)
    {
        Bytes -= PacketBytes(packet);
        av_packet_free(&packet);
    }

    static long PacketBytes(AVPacket* packet)
    {
        long bytes = Math.Max(packet->size, 0);
        for (int index = 0; index < packet->side_data_elems; index++)
        {
            nuint size = packet->side_data[index].size;
            if (size > (nuint)(long.MaxValue - bytes))
                return long.MaxValue; // Reject before cloning, including a malformed overflowing native size.
            bytes += (long)size;
        }
        return bytes;
    }

    public void Dispose()
    {
        foreach (nint pointer in packets)
            Release((AVPacket*)pointer);
        packets.Clear();
        DecodedFrames = 0;
    }
}
