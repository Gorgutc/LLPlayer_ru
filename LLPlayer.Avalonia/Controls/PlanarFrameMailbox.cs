using FlyleafLib.MediaFramework.MediaRenderer;

namespace LLPlayer.Avalonia.Controls;

/// <summary>Bounded CPU ownership handoff; the consumer owns a slot until Release.</summary>
internal sealed class PlanarFrameMailbox
{
    readonly object gate = new();
    readonly Frame[] slots = [new(), new(), new()];
    Frame? pending;
    long epoch;
    int bufferAllocations;
    internal sealed class Frame
    {
        public byte[] Y = [], Uv = [], V = [];
        public int Width, Height;
        public PlanarVideoPixelFormat Format;
        public VideoPresentationParameters Presentation;
        public long Epoch;
        internal bool Reading;
    }

    public bool Publish(in PlanarVideoFrame frame, in VideoPresentationParameters presentation)
    {
        if (!frame.IsValid)
            return false;
        lock (gate)
        {
            Frame? target = pending;
            if (target == null)
                foreach (var candidate in slots)
                    if (!candidate.Reading) { target = candidate; break; }
            if (target == null)
                return false;
            Copy(frame.Plane0, ref target.Y);
            Copy(frame.Plane1, ref target.Uv);
            if (frame.Format == PlanarVideoPixelFormat.Yuv420P)
                Copy(frame.Plane2, ref target.V);
            target.Width = frame.Width;
            target.Height = frame.Height;
            target.Format = frame.Format;
            target.Presentation = presentation;
            target.Epoch = epoch;
            pending = target;
            return true;
        }
    }

    void Copy(in VideoPlaneData source, ref byte[] destination)
    {
        int length = checked(source.RowBytes * source.Height);
        if (destination.Length < length)
        {
            destination = new byte[length];
            bufferAllocations++;
        }
        for (int row = 0; row < source.Height; row++)
            source.GetRow(row).CopyTo(destination.AsSpan(row * source.RowBytes, source.RowBytes));
    }

    public Frame? Acquire()
    {
        lock (gate)
        {
            var frame = pending;
            pending = null;
            if (frame != null)
                frame.Reading = true;
            return frame;
        }
    }

    public void Release(Frame frame)
    {
        lock (gate)
            frame.Reading = false;
    }

    public void Clear()
    {
        lock (gate)
        {
            epoch++;
            pending = null;
        }
    }

    public bool IsCurrent(Frame frame) => frame.Epoch == Epoch;
    public long Epoch => Interlocked.Read(ref epoch);
    internal int BufferAllocations => Volatile.Read(ref bufferAllocations);
}
