using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using LLPlayer.Avalonia.Services;

namespace LLPlayer.Avalonia.Controls;

/// <summary>
/// F-12 audio waveform envelope drawn over the seek bar. It is display-only (never hit-tests) and renders nothing
/// while the engine has no peaks, so the OFF path performs no decode work and paints no pixels. The geometry is a
/// symmetric filled envelope built from one column per device-independent pixel (see <see cref="WaveformEnvelopeLayout"/>).
/// </summary>
public sealed class WaveformEnvelope : Control
{
    public static readonly StyledProperty<float[]?> PeaksProperty =
        AvaloniaProperty.Register<WaveformEnvelope, float[]?>(nameof(Peaks));

    public static readonly StyledProperty<IBrush?> FillProperty =
        AvaloniaProperty.Register<WaveformEnvelope, IBrush?>(nameof(Fill));

    static WaveformEnvelope()
    {
        AffectsRender<WaveformEnvelope>(PeaksProperty, FillProperty);
    }

    public float[]? Peaks
    {
        get => GetValue(PeaksProperty);
        set => SetValue(PeaksProperty, value);
    }

    public IBrush? Fill
    {
        get => GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        IBrush? fill = Fill;
        double width = Bounds.Width;
        double height = Bounds.Height;
        if (fill is null || width <= 0 || height <= 0)
            return;

        double[] amps = WaveformEnvelopeLayout.Compute(Peaks, width, height);
        if (amps.Length == 0)
            return;

        double center = height / 2.0;
        int cols = amps.Length;
        StreamGeometry geometry = new();
        using (StreamGeometryContext ctx = geometry.Open())
        {
            // Top edge left -> right, then bottom edge right -> left: a closed symmetric envelope (WPF parity).
            ctx.BeginFigure(new Point(0, center - amps[0]), isFilled: true);
            for (int c = 1; c < cols; c++)
            {
                double x = cols == 1 ? 0 : (double)c / (cols - 1) * width;
                ctx.LineTo(new Point(x, center - amps[c]));
            }

            for (int c = cols - 1; c >= 0; c--)
            {
                double x = cols == 1 ? 0 : (double)c / (cols - 1) * width;
                ctx.LineTo(new Point(x, center + amps[c]));
            }

            ctx.EndFigure(isClosed: true);
        }

        context.DrawGeometry(fill, null, geometry);
    }
}
