using Flyleaf.FFmpeg;
using FlyleafLib.MediaPlayer;

namespace FlyleafLib.Platform;

[Collection("PortableEngine")]
public class SubtitleBitmapCopyTests
{
    [Fact]
    public void Disposed_or_empty_native_subtitle_is_rejected_without_dereferencing_rects()
    {
        using SubtitleBitmapData empty = new(default);
        Assert.False(empty.TryCopyBitmap(100, TestContext.Current.CancellationToken, out _, out _, out _));
        empty.Dispose();
        Assert.False(empty.TryCopyBitmap(100, TestContext.Current.CancellationToken, out _, out _, out _));
    }

    [Fact]
    public unsafe void Padded_paletted_rows_copy_tightly_and_preserve_straight_alpha_and_owned_bytes()
    {
        PortableVideoTestMedia.RequireEngine();
        using SubtitleBitmapData bitmap = NativeBitmap();
        Assert.True(bitmap.TryCopyBitmap(4, TestContext.Current.CancellationToken, out byte[] bytes, out int width, out int height));
        Assert.Equal(2, width); Assert.Equal(2, height);
        Assert.Equal(new byte[] { 0x30,0x20,0x10,0x80, 0,0,0,0, 0,0,0,0, 0x30,0x20,0x10,0x80 }, bytes);
        ((uint*)bitmap.Sub.rects[0]->data[1])[1] = 0xffffffff;
        Assert.Equal(0x30, bytes[0]);
        Assert.False(bitmap.TryCopyBitmap(3, TestContext.Current.CancellationToken, out _, out _, out _));
        using CancellationTokenSource canceled = new(); canceled.Cancel();
        Assert.Throws<OperationCanceledException>(() => bitmap.TryCopyBitmap(4, canceled.Token, out _, out _, out _));
        bitmap.Dispose();
        Assert.False(bitmap.TryCopyBitmap(4, TestContext.Current.CancellationToken, out _, out _, out _));
    }

    static unsafe SubtitleBitmapData NativeBitmap()
    {
        var rect = (AVSubtitleRect*)Raw.av_mallocz((nuint)sizeof(AVSubtitleRect));
        var rects = (AVSubtitleRect**)Raw.av_mallocz((nuint)sizeof(nint));
        rects[0] = rect; rect->type = AVSubtitleType.Bitmap; rect->w = 2; rect->h = 2; rect->nb_colors = 2; rect->linesize[0] = 4;
        rect->data[0] = (nint)Raw.av_mallocz(8); rect->data[1] = (nint)Raw.av_mallocz(256 * sizeof(uint));
        ((byte*)rect->data[0])[0] = 1; ((byte*)rect->data[0])[5] = 1;
        ((uint*)rect->data[1])[1] = 0x80102030;
        return new(new AVSubtitle { num_rects = 1, rects = rects });
    }
}
