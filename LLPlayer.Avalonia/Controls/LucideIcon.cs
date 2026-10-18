using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

namespace LLPlayer.Avalonia.Controls;

/// <summary>
/// Draws a Lucide icon (24x24 stroke geometry from Themes/Icons.axaml) at <see cref="Size"/> DIPs with the inherited
/// <see cref="Foreground"/>, a 2-unit round-capped stroke scaled with the icon (lucide-react defaults used by shadcn/ui).
/// </summary>
public sealed class LucideIcon : Control
{
    public static readonly StyledProperty<Geometry?> DataProperty =
        AvaloniaProperty.Register<LucideIcon, Geometry?>(nameof(Data));

    public static readonly StyledProperty<double> SizeProperty =
        AvaloniaProperty.Register<LucideIcon, double>(nameof(Size), 16);

    public static readonly StyledProperty<double> StrokeWidthProperty =
        AvaloniaProperty.Register<LucideIcon, double>(nameof(StrokeWidth), 2);

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        TemplatedControl.ForegroundProperty.AddOwner<LucideIcon>();

    /// <summary>Fill the geometry too (solid play/pause glyphs).</summary>
    public static readonly StyledProperty<bool> FillProperty =
        AvaloniaProperty.Register<LucideIcon, bool>(nameof(Fill));

    static LucideIcon()
    {
        AffectsRender<LucideIcon>(DataProperty, StrokeWidthProperty, ForegroundProperty, FillProperty);
        AffectsMeasure<LucideIcon>(SizeProperty);
    }

    public Geometry? Data
    {
        get => GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    public double Size
    {
        get => GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    public double StrokeWidth
    {
        get => GetValue(StrokeWidthProperty);
        set => SetValue(StrokeWidthProperty, value);
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public bool Fill
    {
        get => GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) => new(Size, Size);

    public override void Render(DrawingContext context)
    {
        Geometry? data = Data;
        IBrush? brush = Foreground;
        if (data == null || brush == null)
            return;

        double scale = Size / 24.0;
        double ox = (Bounds.Width - Size) / 2;
        double oy = (Bounds.Height - Size) / 2;
        Pen pen = new(brush, StrokeWidth, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);

        using (context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(ox, oy)))
            context.DrawGeometry(Fill ? brush : null, pen, data);
    }
}
