namespace FlyleafLib;

// F-13 portable (Linux) TFM counterparts of the DXGI-bound declarations of Engine/Globals.cs.

/// <summary>Output pixel format of the video surface (numeric values match DXGI_FORMAT like the Windows build).</summary>
public enum SwapChainFormat : uint
{
    BGRA        = 87,   // DXGI_FORMAT_B8G8R8A8_UNORM
    RGBA        = 28,   // DXGI_FORMAT_R8G8B8A8_UNORM
    RGBA10bit   = 24    // DXGI_FORMAT_R10G10B10A2_UNORM
}

/// <summary>Display rotation of a <see cref="GPUOutput"/> (numeric values match DXGI_MODE_ROTATION).</summary>
public enum ModeRotation
{
    Unspecified = 0,
    Identity    = 1,
    Rotate90    = 2,
    Rotate180   = 3,
    Rotate270   = 4
}

/// <summary>A display output. The portable build has no DXGI enumeration; hosts may report their monitor here.</summary>
public class GPUOutput
{
    public nint             Hwnd            { get; set; }
    public string           DeviceName      { get; set; }
    public int              Left            { get; set; }
    public int              Top             { get; set; }
    public int              Right           { get; set; }
    public int              Bottom          { get; set; }
    public int              Width           => Right- Left;
    public int              Height          => Bottom- Top;
    public bool             IsAttached      { get; set; }
    public ModeRotation     Rotation        { get; set; }
    public float            MaxLuminance    { get; set; }

    public override string ToString()
    {
        int gcd = GCD(Width, Height);
        return $"{DeviceName,-20} [Top: {Top,-4}, Left: {Left,-4}, Width: {Width,-4}, Height: {Height,-4}, Ratio: " + (gcd > 0 ? $"{Width / gcd}:{Height / gcd}]" : "]");
    }
}

/// <summary>
/// A video adapter. The portable build renders in software (FFmpeg swscale), so <see cref="VideoEngine.GPUAdapters"/>
/// holds a single <c>Software</c> entry.
/// </summary>
public class GPUAdapter
{
    public nuint            SystemMemory    { get; internal set; }
    public nuint            VideoMemory     { get; internal set; }
    public nuint            SharedMemory    { get; internal set; }

    public uint             Id              { get; internal set; }
    public GPUVendor        Vendor          { get; internal set; }
    public string           Description     { get; internal set; }
    public long             Luid            { get; internal set; }

    public List<GPUOutput>  GetGPUOutputs()    => [];

    public override string  ToString()
        => (Vendor + " " + Description).PadRight(40) + $"[ID: {Id,-6}, LUID: {Luid,-6}]";
}
