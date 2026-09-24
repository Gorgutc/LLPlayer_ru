using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;

using WPoint = System.Windows.Point;

namespace FlyleafLib.MediaFramework.MediaRenderer;

// F-13 portable (Linux) counterparts of the renderer configuration types declared in the (excluded) Direct3D11
// Renderer.VP.cs / Renderer.VP.D3.cs. Viewport/zoom/pan/rotation semantics are identical to the Windows build.

/// <summary>Output viewport in host-control pixels (same fields as Vortice.Mathematics.Viewport).</summary>
public struct Viewport : IEquatable<Viewport>
{
    public float X;
    public float Y;
    public float Width;
    public float Height;
    public float MinDepth;
    public float MaxDepth;

    public Viewport(float x, float y, float width, float height)
    {
        X = x; Y = y; Width = width; Height = height; MinDepth = 0; MaxDepth = 1;
    }

    public readonly bool Equals(Viewport other) => X == other.X && Y == other.Y && Width == other.Width && Height == other.Height && MinDepth == other.MinDepth && MaxDepth == other.MaxDepth;
    public override readonly bool Equals(object obj) => obj is Viewport v && Equals(v);
    public override readonly int GetHashCode() => HashCode.Combine(X, Y, Width, Height, MinDepth, MaxDepth);
    public static bool operator ==(Viewport a, Viewport b) => a.Equals(b);
    public static bool operator !=(Viewport a, Viewport b) => !a.Equals(b);
    public override readonly string ToString() => $"[X: {X}, Y: {Y}, Width: {Width}, Height: {Height}]";
}

[Flags]
enum VPRequestType
{
    Empty           = 0,

    BackColor       = 1 << 0,
    ReConfigVP      = 1 << 1,   // User VP Switch + FL PS Update (e.g. w/o Filters)

    RotationFlip    = 1 << 2,   // Both - Flyleaf (for Flip)
    Resize          = 1 << 3,
    Crop            = 1 << 4,
    AspectRatio     = 1 << 5,
    Viewport        = 1 << 6,

    Deinterlace     = 1 << 7,   // D3D11
    HDRtoSDR        = 1 << 8,   // Flyleaf
    UpdatePS        = 1 << 9,   // Flyleaf
    UpdateVS        = 1 << 10,  // Flyleaf
}

/// <summary>JSON representation of <see cref="System.Drawing.Color"/> as "#AARRGGBB".</summary>
public sealed class ArgbColorJsonConverter : JsonConverter<System.Drawing.Color>
{
    public override System.Drawing.Color Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        string s = reader.GetString();
        if (string.IsNullOrEmpty(s) || s[0] != '#' || !uint.TryParse(s.AsSpan(1), System.Globalization.NumberStyles.HexNumber, null, out uint argb))
            return System.Drawing.Color.Black;

        if (s.Length == 7) // #RRGGBB
            argb |= 0xFF000000;

        return System.Drawing.Color.FromArgb(unchecked((int)argb));
    }

    public override void Write(Utf8JsonWriter writer, System.Drawing.Color value, JsonSerializerOptions options)
        => writer.WriteStringValue($"#{value.A:X2}{value.R:X2}{value.G:X2}{value.B:X2}");
}

public class VPConfig : NotifyPropertyChanged
{
    internal IVP vp;

    // === VP / Swap Chain ===

    public SwapChainFormat  SwapChainFormat         { get; set; } = SwapChainFormat.BGRA;

    /// <summary>
    /// Whether VSync should be enabled (0: Disabled, 1: Enabled) - informational on the portable build
    /// </summary>
    public uint             VSync                   { get; set; } = 1;

    /// <summary>
    /// Background color of the player's control (the host paints it around <see cref="Renderer.Viewport"/>)
    /// </summary>
    [JsonConverter(typeof(ArgbColorJsonConverter))]
    public System.Drawing.Color
                            BackColor               { get => backColor;  set { if (Set(ref backColor, value)) vp?.VPRequest(VPRequestType.BackColor); } }
    internal System.Drawing.Color backColor = System.Drawing.Color.FromArgb(255, 0, 0, 0);

    // === Viewport ===

    /// <summary>
    /// Video aspect ratio
    /// </summary>
    public AspectRatio      AspectRatio             { get => aspectRatio;          set { if (Set(ref aspectRatio, value)) vp?.VPRequest(VPRequestType.AspectRatio); } }
    AspectRatio aspectRatio = AspectRatio.Keep;
    public void ToggleKeepRatio()
    {
        if (AspectRatio == AspectRatio.Keep)
            AspectRatio = AspectRatio.Fill;
        else if (AspectRatio == AspectRatio.Fill)
            AspectRatio = AspectRatio.Keep;
    }

    /// <summary>
    /// Custom aspect ratio (AspectRatio must be set to Custom to have an effect)
    /// </summary>
    public AspectRatio      AspectRatioCustom       { get => aspectRatioCustom;    set { if (Set(ref aspectRatioCustom, value) && AspectRatio == AspectRatio.Custom) { aspectRatio = AspectRatio.Fill; AspectRatio = AspectRatio.Custom; } } }
    AspectRatio aspectRatioCustom = new(16, 9);

    /// <summary>
    /// Cropping rectagle to crop the output frame (based on frame size)
    /// </summary>
    [JsonIgnore]
    public CropRect         Crop                    { get => crop;                  set { if (!Set(ref crop,        value)) return; HasUserCrop = crop != CropRect.Empty; vp?.VPRequest(VPRequestType.Crop); } }
    internal CropRect crop = CropRect.Empty;
    internal bool HasUserCrop = false;

    /// <summary>
    /// Pan X Offset to change the X location
    /// </summary>
    [JsonIgnore]
    public double           PanXOffset              { get => panXOffset;            set { if (Set(ref panXOffset,   Math.Clamp(value, -10, 10))) vp?.VPRequest(VPRequestType.Viewport); } }
    internal double panXOffset;

    /// <summary>
    /// Pan Y Offset to change the Y location
    /// </summary>
    [JsonIgnore]
    public double           PanYOffset              { get => panYOffset;            set { if (Set(ref panYOffset,   Math.Clamp(value, -10, 10))) vp?.VPRequest(VPRequestType.Viewport); } }
    internal double panYOffset;

    /// <summary>
    /// Pan rotation angle (0, 90, 180, 270). The host applies the rotation when drawing (see <see cref="Renderer.Rotation"/>).
    /// </summary>
    [JsonIgnore]
    public uint             Rotation                { get => rotation;              set { if (Set(ref rotation,     value)) vp?.VPRequest(VPRequestType.RotationFlip); } }
    internal uint rotation;

    [JsonIgnore]
    public bool             HFlip                   { get => hflip;                 set { if (Set(ref hflip,        value)) vp?.VPRequest(VPRequestType.RotationFlip); } }
    internal bool hflip;

    [JsonIgnore]
    public bool             VFlip                   { get => vflip;                 set { if (Set(ref vflip,        value)) vp?.VPRequest(VPRequestType.RotationFlip); } }
    internal bool vflip;

    [JsonIgnore]
    public double           Zoom                    { get => SnapToInt(zoom * 100); set { if (Set(ref zoom, SnapToInt(value / 100))) vp?.VPRequest(VPRequestType.Viewport); } }
    internal double zoom = 1;

    [JsonIgnore]
    public WPoint
                            ZoomCenter              { get => zoomCenter;            set { if (Set(ref zoomCenter,   value)) vp?.VPRequest(VPRequestType.Viewport); } }
    internal WPoint zoomCenter = new(0.5, 0.5);

    public int              ZoomOffset              { get => zoomOffset;            set { Set(ref zoomOffset,       value); } }
    int zoomOffset = 10;

    public void ResetViewport(uint rotation = 0)
        => SetViewport(0, 0, rotation, 100, new(0.5, 0.5), CropRect.Empty, AspectRatio.Keep, false, false);

    public void SetViewport(int panX, int panY, uint rotation, double zoom, WPoint p, CropRect crop, AspectRatio ratio, bool hflip, bool vflip)
    {
        AspectRatio = ratio;
        Zoom        = zoom;
        ZoomCenter  = p;
        PanXOffset  = panX;
        PanYOffset  = panY;
        Crop        = crop;
        Rotation    = rotation;
        HFlip       = hflip;
        VFlip       = vflip;

        vp?.VPRequest(VPRequestType.Crop | VPRequestType.RotationFlip);
    }

    public void RotateRight()   => Rotation = (rotation + 90) % 360;
    public void RotateLeft()    => Rotation = rotation < 90 ? 360 + rotation - 90 : rotation - 90;

    public void ZoomIn()        => Zoom += ZoomOffset;
    public void ZoomOut()       => Zoom = Math.Max(Zoom - ZoomOffset, 0);
    public void ZoomIn (WPoint p) { if (vp == null) return; SetZoomWithCenterPoint(p, zoom + ZoomOffset / 100.0); }
    public void ZoomOut(WPoint p) { if (vp == null) return; double zoom = this.zoom - ZoomOffset / 100.0; if (zoom < 0.001) return; SetZoomWithCenterPoint(p, zoom); }
    public void SetZoomAndCenter(double zoom, WPoint p)
    {
        Zoom        = zoom;
        ZoomCenter  = p;
        vp?.VPRequest(VPRequestType.Viewport);
    }
    internal void SetZoomWithCenterPoint(Point p, double zoom)
    {
        // Same math as the Windows build (Renderer.VP.cs): the point under the cursor stays in place after zooming.
        zoom = SnapToInt(zoom);
        Viewport view = vp.Viewport;

        if (!(p.X >= view.X && p.X < view.X + view.Width && p.Y >= view.Y && p.Y < view.Y + view.Height)) // Point out of view
        {
            Zoom = zoom * 100;
            return;
        }

        Point viewport = new(view.X, view.Y);
        RemoveViewportOffsets(ref viewport);
        RemoveViewportOffsets(ref p);

        // Finds the required center point so that p will have the same pixel after zoom
        Point zoomCenter = new(
            GetCenterPoint(zoom, ((p.X - viewport.X) / (this.zoom / zoom)) - p.X) / (view.Width  / this.zoom),
            GetCenterPoint(zoom, ((p.Y - viewport.Y) / (this.zoom / zoom)) - p.Y) / (view.Height / this.zoom));

        SetZoomAndCenter(zoom * 100, zoomCenter);

        void RemoveViewportOffsets(ref Point p)
        {
            p.X -= (vp.SideXPixels / 2 + (panXOffset * vp.ControlWidth));
            p.Y -= (vp.SideYPixels / 2 + (panYOffset * vp.ControlHeight));
        }

        double GetCenterPoint(double zoom, double offset)
            => zoom == 1 ? offset : offset / SnapToInt(zoom - 1);
    }
}

internal interface IVP
{
    public int          ControlWidth    { get; }
    public int          ControlHeight   { get; }
    public int          SideXPixels     { get; }
    public int          SideYPixels     { get; }
    public Viewport     Viewport        { get; }

    void VPRequest(VPRequestType request);
    void UpdateSize(int width, int height);
    void MonitorChanged(GPUOutput monitor);
}
