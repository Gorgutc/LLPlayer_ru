using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using FlyleafLib;
using FlyleafLib.MediaFramework.MediaRenderer;

namespace LLPlayer.Avalonia.Controls;

/// <summary>
/// Video surface of the portable (software) FlyleafLib renderer.
/// <para>
/// <see cref="PresentFrame"/> (renderer threads) copies the BGRA frame into the back buffer of two
/// <see cref="WriteableBitmap"/>s (re-created only when the frame size changes: no per-frame allocation), swaps the
/// buffers and posts one coalesced <see cref="Visual.InvalidateVisual"/> to the UI thread. <see cref="Render"/> fills
/// the control with the background colour (letterbox) and draws the front buffer into
/// <see cref="Renderer.Viewport"/> (control device pixels, converted to DIPs), applying the renderer's rotation and
/// flips. The control reports its size in device pixels with <see cref="Renderer.SetControlSize"/> on resize / DPI
/// change.
/// </para>
/// </summary>
public sealed class VideoView : Control, IVideoSurface
{
    public static readonly StyledProperty<IBrush> BackgroundProperty =
        AvaloniaProperty.Register<VideoView, IBrush>(nameof(Background), Brushes.Black);

    readonly Lock swapLock = new();     // guards front/back/hasFrame
    readonly Lock writeLock = new();    // serializes writers (PresentFrame / ClearFrame)
    readonly List<WriteableBitmap> retired = [];
    WriteableBitmap? front, back;
    bool hasFrame;
    int invalidatePending;
    long framesPresented;
    Renderer? renderer;
    PixelSize reportedSize;
    TopLevel? topLevel;

    static VideoView()
    {
        AffectsRender<VideoView>(BackgroundProperty);
        ClipToBoundsProperty.OverrideDefaultValue<VideoView>(true);
    }

    public IBrush Background
    {
        get => GetValue(BackgroundProperty);
        set => SetValue(BackgroundProperty, value);
    }

    /// <summary>Number of frames copied so far (diagnostics / tests).</summary>
    public long FramesPresented => Interlocked.Read(ref framesPresented);

    /// <summary>Size of the last presented frame.</summary>
    public PixelSize FrameSize { get; private set; }

    /// <summary>
    /// The player's renderer. Setting it attaches this control as its <see cref="Renderer.Surface"/> and reports the
    /// control size; setting null (or another renderer) detaches the previous one.
    /// </summary>
    public Renderer? Renderer
    {
        get => renderer;
        set
        {
            if (ReferenceEquals(renderer, value))
                return;

            if (renderer != null && ReferenceEquals(renderer.Surface, this))
            {
                renderer.ViewportChanged -= OnViewportChanged;
                renderer.Surface = null;
            }

            renderer = value;
            reportedSize = default;
            if (renderer != null)
            {
                renderer.ViewportChanged += OnViewportChanged;
                ReportSize();
                renderer.Surface = this;
            }
            InvalidateVisual();
        }
    }

    public void PresentFrame(ReadOnlySpan<byte> bgra, int width, int height, int stride)
    {
        if (width <= 0 || height <= 0 || stride < width * 4 || bgra.Length < (long)stride * (height - 1) + width * 4)
            return;

        lock (writeLock)
        {
            WriteableBitmap target;
            lock (swapLock)
            {
                if (back == null || back.PixelSize.Width != width || back.PixelSize.Height != height)
                {
                    if (back != null)
                        retired.Add(back);
                    back = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
                }
                target = back;
            }

            using (ILockedFramebuffer fb = target.Lock())
            {
                int rowBytes = width * 4;
                for (int y = 0; y < height; y++)
                {
                    Span<byte> dst;
                    unsafe
                    {
                        dst = new Span<byte>((byte*)fb.Address + (long)y * fb.RowBytes, rowBytes);
                    }
                    bgra.Slice(y * stride, rowBytes).CopyTo(dst);
                }
            }

            lock (swapLock)
            {
                (front, back) = (target, front);
                if (back != null && (back.PixelSize.Width != width || back.PixelSize.Height != height))
                {
                    retired.Add(back);
                    back = null;
                }
                hasFrame = true;
            }

            FrameSize = new PixelSize(width, height);
            Interlocked.Increment(ref framesPresented);
        }

        RequestInvalidate();
    }

    public void ClearFrame()
    {
        lock (swapLock)
            hasFrame = false;
        RequestInvalidate();
    }

    public override void Render(DrawingContext context)
    {
        Rect bounds = new(Bounds.Size);
        context.FillRectangle(Background, bounds);

        WriteableBitmap? frame;
        List<WriteableBitmap>? toDispose = null;
        lock (swapLock)
        {
            frame = hasFrame ? front : null;
            if (retired.Count > 0)
            {
                toDispose = [.. retired];
                retired.Clear();
            }
        }

        if (toDispose != null)
        {
            // Disposed after this frame was recorded: the compositor keeps its own reference to what it draws.
            Dispatcher.UIThread.Post(() => toDispose.ForEach(b => b.Dispose()), DispatcherPriority.Background);
        }

        if (frame == null)
            return;

        Rect dest = GetDestinationRect(bounds, frame.PixelSize);
        if (dest.Width <= 0 || dest.Height <= 0)
            return;

        uint rotation = renderer?.Rotation ?? 0;
        bool hflip = renderer?.HFlip ?? false;
        bool vflip = renderer?.VFlip ?? false;
        bool swapped = rotation is 90 or 270;
        Rect drawRect = swapped
            ? new Rect(dest.Center.X - dest.Height / 2, dest.Center.Y - dest.Width / 2, dest.Height, dest.Width)
            : dest;

        Matrix transform = Matrix.CreateTranslation(-dest.Center.X, -dest.Center.Y)
                           * Matrix.CreateScale(hflip ? -1 : 1, vflip ? -1 : 1)
                           * Matrix.CreateRotation(Math.PI * rotation / 180.0)
                           * Matrix.CreateTranslation(dest.Center.X, dest.Center.Y);

        using (context.PushTransform(transform))
            context.DrawImage(frame, new Rect(0, 0, frame.PixelSize.Width, frame.PixelSize.Height), drawRect);
    }

    /// <summary>
    /// Where the frame goes inside the control (DIPs): the renderer's viewport when it has one, else an aspect-fit
    /// rectangle (no renderer attached, e.g. tests / first frame before the renderer computed its viewport).
    /// </summary>
    public Rect GetDestinationRect(Rect bounds, PixelSize frameSize)
    {
        double scaling = topLevel?.RenderScaling ?? 1;
        if (renderer != null)
        {
            Viewport vp = renderer.Viewport;
            if (vp.Width > 0 && vp.Height > 0)
                return new Rect(vp.X / scaling, vp.Y / scaling, vp.Width / scaling, vp.Height / scaling);
        }

        if (frameSize.Width <= 0 || frameSize.Height <= 0 || bounds.Width <= 0 || bounds.Height <= 0)
            return default;

        double scale = Math.Min(bounds.Width / frameSize.Width, bounds.Height / frameSize.Height);
        double w = frameSize.Width * scale, h = frameSize.Height * scale;
        return new Rect((bounds.Width - w) / 2, (bounds.Height - h) / 2, w, h);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        topLevel = TopLevel.GetTopLevel(this);
        if (topLevel != null)
            topLevel.ScalingChanged += OnScalingChanged;
        UpdateDpi();
        ReportSize();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (topLevel != null)
            topLevel.ScalingChanged -= OnScalingChanged;
        topLevel = null;
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        ReportSize();
    }

    void OnScalingChanged(object? sender, EventArgs e)
    {
        UpdateDpi();
        reportedSize = default;
        ReportSize();
        InvalidateVisual();
    }

    void OnViewportChanged(object? sender, EventArgs e) => RequestInvalidate();

    void UpdateDpi()
    {
        double scaling = topLevel?.RenderScaling ?? 1;
        Utils.NativeMethods.DpiX = Utils.NativeMethods.DpiY = 96 * scaling;
    }

    void ReportSize()
    {
        if (renderer == null)
            return;

        double scaling = topLevel?.RenderScaling ?? 1;
        PixelSize size = new(Math.Max(0, (int)Math.Round(Bounds.Width * scaling)), Math.Max(0, (int)Math.Round(Bounds.Height * scaling)));
        if (size == reportedSize || size.Width == 0 || size.Height == 0)
            return;

        reportedSize = size;
        renderer.SetControlSize(size.Width, size.Height);
    }

    void RequestInvalidate()
    {
        if (Interlocked.Exchange(ref invalidatePending, 1) == 1)
            return;

        Dispatcher.UIThread.Post(() =>
        {
            Volatile.Write(ref invalidatePending, 0);
            InvalidateVisual();
        }, DispatcherPriority.Render);
    }
}
