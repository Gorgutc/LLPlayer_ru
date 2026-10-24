using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace LLPlayer.Avalonia.Controls;

/// <summary>Horizontal placement of the A-B repeat markers in an overlay of <paramref name="Width"/> DIPs.</summary>
public readonly record struct AbLoopMarkerLayout(double? XA, double? XB, double? BandLeft, double? BandRight);

/// <summary>
/// F-12 A-B repeat overlay for the seek bar: one vertical line per set point and a translucent band between them.
/// It is drawn above the slider but never hit-tests (<see cref="Control.IsHitTestVisible"/> stays false in XAML), so the
/// slider keeps owning all seeking. The engine only keeps A &lt; B for a valid window; an inverted pair keeps both
/// markers but no band.
/// </summary>
public sealed class AbLoopMarkers : Control
{
    public static readonly StyledProperty<double> ValueAProperty =
        AvaloniaProperty.Register<AbLoopMarkers, double>(nameof(ValueA), double.NaN);

    public static readonly StyledProperty<double> ValueBProperty =
        AvaloniaProperty.Register<AbLoopMarkers, double>(nameof(ValueB), double.NaN);

    /// <summary>Media duration in seconds (the seek bar maximum).</summary>
    public static readonly StyledProperty<double> MaximumProperty =
        AvaloniaProperty.Register<AbLoopMarkers, double>(nameof(Maximum), double.NaN);

    /// <summary>Half of the seek thumb width: the thumb centre only travels between the two insets.</summary>
    public static readonly StyledProperty<double> InsetProperty =
        AvaloniaProperty.Register<AbLoopMarkers, double>(nameof(Inset), 6d);

    public static readonly StyledProperty<IBrush?> LineBrushProperty =
        AvaloniaProperty.Register<AbLoopMarkers, IBrush?>(nameof(LineBrush));

    public static readonly StyledProperty<IBrush?> BandBrushProperty =
        AvaloniaProperty.Register<AbLoopMarkers, IBrush?>(nameof(BandBrush));

    public static readonly StyledProperty<double> BandOpacityProperty =
        AvaloniaProperty.Register<AbLoopMarkers, double>(nameof(BandOpacity), 0.18);

    public static readonly StyledProperty<double> LineThicknessProperty =
        AvaloniaProperty.Register<AbLoopMarkers, double>(nameof(LineThickness), 2d);

    static AbLoopMarkers()
    {
        AffectsRender<AbLoopMarkers>(ValueAProperty, ValueBProperty, MaximumProperty, InsetProperty,
            LineBrushProperty, BandBrushProperty, BandOpacityProperty, LineThicknessProperty);
    }

    /// <summary>Point A in seconds; NaN when unset.</summary>
    public double ValueA { get => GetValue(ValueAProperty); set => SetValue(ValueAProperty, value); }
    /// <summary>Point B in seconds; NaN when unset.</summary>
    public double ValueB { get => GetValue(ValueBProperty); set => SetValue(ValueBProperty, value); }
    /// <summary>Media duration in seconds; NaN/0 hides the markers.</summary>
    public double Maximum { get => GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public double Inset { get => GetValue(InsetProperty); set => SetValue(InsetProperty, value); }
    public IBrush? LineBrush { get => GetValue(LineBrushProperty); set => SetValue(LineBrushProperty, value); }
    public IBrush? BandBrush { get => GetValue(BandBrushProperty); set => SetValue(BandBrushProperty, value); }
    public double BandOpacity { get => GetValue(BandOpacityProperty); set => SetValue(BandOpacityProperty, value); }
    public double LineThickness { get => GetValue(LineThicknessProperty); set => SetValue(LineThicknessProperty, value); }

    /// <summary>
    /// Pure placement of the markers in a <paramref name="width"/>-wide overlay: the thumb centre maps seconds onto
    /// [<paramref name="inset"/>, width - inset]. Unusable input (no duration, no set point, a track narrower than the
    /// insets) yields an empty layout, which draws nothing.
    /// </summary>
    public static AbLoopMarkerLayout Layout(double valueA, double valueB, double maximum, double width, double inset)
    {
        if (!double.IsFinite(maximum) || maximum <= 0 || !double.IsFinite(width) || width <= 0)
            return default;
        double insetClamped = double.IsFinite(inset) ? Math.Clamp(inset, 0, width / 2) : 0;
        double track = width - 2 * insetClamped;
        if (track <= 0)
            return default;

        double? xA = ToX(valueA);
        double? xB = ToX(valueB);
        if (xA == null && xB == null)
            return default;

        double? bandLeft = xA != null && xB != null && valueA < valueB ? Math.Min(xA.Value, xB.Value) : null;
        double? bandRight = bandLeft == null ? null : Math.Max(xA!.Value, xB!.Value);
        return new AbLoopMarkerLayout(xA, xB, bandLeft, bandRight);

        double? ToX(double seconds)
        {
            if (!double.IsFinite(seconds))
                return null;
            double clamped = Math.Clamp(seconds, 0, maximum);
            return insetClamped + clamped / maximum * track;
        }
    }

    public override void Render(DrawingContext context)
    {
        AbLoopMarkerLayout layout = Layout(ValueA, ValueB, Maximum, Bounds.Width, Inset);
        if (layout.XA == null && layout.XB == null)
            return;

        double height = Bounds.Height;
        double bandOpacity = double.IsFinite(BandOpacity) ? Math.Clamp(BandOpacity, 0, 1) : 0;
        if (layout is { BandLeft: { } left, BandRight: { } right } && BandBrush is { } band && bandOpacity > 0)
            using (context.PushOpacity(bandOpacity))
                context.DrawRectangle(band, null, new Rect(left, 0, Math.Max(0, right - left), height));

        if (LineBrush is not { } line)
            return;

        double thickness = double.IsFinite(LineThickness) && LineThickness > 0 ? LineThickness : 2;
        if (layout.XA is { } centreA)
            DrawLine(centreA);
        if (layout.XB is { } centreB)
            DrawLine(centreB);

        void DrawLine(double centre)
            => context.DrawRectangle(line, null, new Rect(centre - thickness / 2, 0, thickness, height));
    }
}
