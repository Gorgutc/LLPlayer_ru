// F-13 portable (Linux) TFM only: minimal value types with the same names/shape as the WPF (WindowsBase /
// PresentationFramework) types that appear in FlyleafLib's shared public API (VPConfig.ZoomCenter,
// SubsBitmapPosition.Margin). They keep shared engine code compiling unchanged without WPF; they carry data only.
namespace System.Windows;

/// <summary>A 2D point (same shape as WPF's <c>System.Windows.Point</c>).</summary>
public struct Point : IEquatable<Point>
{
    public double X { get; set; }
    public double Y { get; set; }

    public Point(double x, double y) { X = x; Y = y; }

    public readonly bool Equals(Point other) => X == other.X && Y == other.Y;
    public override readonly bool Equals(object obj) => obj is Point p && Equals(p);
    public override readonly int GetHashCode() => HashCode.Combine(X, Y);
    public static bool operator ==(Point a, Point b) => a.Equals(b);
    public static bool operator !=(Point a, Point b) => !a.Equals(b);
    public override readonly string ToString() => FormattableString.Invariant($"{X},{Y}");
}

/// <summary>A frame thickness / margin (same shape as WPF's <c>System.Windows.Thickness</c>).</summary>
public struct Thickness : IEquatable<Thickness>
{
    public double Left   { get; set; }
    public double Top    { get; set; }
    public double Right  { get; set; }
    public double Bottom { get; set; }

    public Thickness(double uniformLength) { Left = Top = Right = Bottom = uniformLength; }
    public Thickness(double left, double top, double right, double bottom) { Left = left; Top = top; Right = right; Bottom = bottom; }

    public readonly bool Equals(Thickness other) => Left == other.Left && Top == other.Top && Right == other.Right && Bottom == other.Bottom;
    public override readonly bool Equals(object obj) => obj is Thickness t && Equals(t);
    public override readonly int GetHashCode() => HashCode.Combine(Left, Top, Right, Bottom);
    public static bool operator ==(Thickness a, Thickness b) => a.Equals(b);
    public static bool operator !=(Thickness a, Thickness b) => !a.Equals(b);
    public override readonly string ToString() => FormattableString.Invariant($"{Left},{Top},{Right},{Bottom}");
}
