using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using LLPlayer.Avalonia.Services;

namespace LLPlayer.Avalonia.Controls;

/// <summary>
/// Bitmap subtitle (PGS / VobSub / DVB, FlyleafLib <c>BgraBitmap</c>) drawn at its position inside the video picture:
/// the cue's X/Y/size are in video pixels and are mapped through <see cref="VideoView"/>'s destination rectangle.
/// Must share the layout slot of the VideoView it follows.
/// </summary>
public sealed class SubtitleImageView : Control
{
    public static readonly StyledProperty<SubtitleImage?> ImageProperty =
        AvaloniaProperty.Register<SubtitleImageView, SubtitleImage?>(nameof(Image));

    public static readonly StyledProperty<VideoView?> VideoProperty =
        AvaloniaProperty.Register<SubtitleImageView, VideoView?>(nameof(Video));

    WriteableBitmap? bitmap;

    static SubtitleImageView()
    {
        AffectsRender<SubtitleImageView>(ImageProperty, VideoProperty);
        IsHitTestVisibleProperty.OverrideDefaultValue<SubtitleImageView>(false);
    }

    public SubtitleImage? Image
    {
        get => GetValue(ImageProperty);
        set => SetValue(ImageProperty, value);
    }

    public VideoView? Video
    {
        get => GetValue(VideoProperty);
        set => SetValue(VideoProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ImageProperty)
        {
            bitmap?.Dispose();
            bitmap = Image is { } img ? ToBitmap(img) : null;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        bitmap?.Dispose();
        bitmap = null;
    }

    public override void Render(DrawingContext context)
    {
        SubtitleImage? img = Image;
        if (img == null || bitmap == null || img.VideoWidth <= 0 || img.VideoHeight <= 0)
            return;

        Rect bounds = new(Bounds.Size);
        Rect video = Video?.GetDestinationRect(bounds, new PixelSize(img.VideoWidth, img.VideoHeight)) ?? bounds;
        if (video.Width <= 0 || video.Height <= 0)
            return;

        double sx = video.Width / img.VideoWidth, sy = video.Height / img.VideoHeight;
        Rect dest = new(video.X + img.X * sx, video.Y + img.Y * sy, img.Width * sx, img.Height * sy);
        context.DrawImage(bitmap, new Rect(0, 0, bitmap.PixelSize.Width, bitmap.PixelSize.Height), dest);
    }

    static WriteableBitmap? ToBitmap(SubtitleImage img)
    {
        var src = img.Bitmap;
        if (src.Width <= 0 || src.Height <= 0)
            return null;

        WriteableBitmap wb = new(new PixelSize(src.Width, src.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        using ILockedFramebuffer fb = wb.Lock();
        for (int y = 0; y < src.Height; y++)
        {
            unsafe
            {
                new ReadOnlySpan<byte>(src.Data, y * src.Stride, src.Width * 4)
                    .CopyTo(new Span<byte>((byte*)fb.Address + (long)y * fb.RowBytes, src.Width * 4));
            }
        }
        return wb;
    }
}
