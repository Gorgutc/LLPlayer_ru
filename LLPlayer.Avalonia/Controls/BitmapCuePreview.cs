using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using LLPlayer.Avalonia.Services;
using LLPlayer.Avalonia.ViewModels;

namespace LLPlayer.Avalonia.Controls;

/// <summary>Lazy, realized-row-only preview. Each control owns at most one bitmap and one cancellable conversion.</summary>
public sealed class BitmapCuePreview : Control
{
    public static readonly StyledProperty<CueItemViewModel?> RowProperty =
        AvaloniaProperty.Register<BitmapCuePreview, CueItemViewModel?>(nameof(Row));
    public static readonly StyledProperty<IPlaybackController?> PlayerProperty =
        AvaloniaProperty.Register<BitmapCuePreview, IPlaybackController?>(nameof(Player));
    public CueItemViewModel? Row { get => GetValue(RowProperty); set => SetValue(RowProperty, value); }
    public IPlaybackController? Player { get => GetValue(PlayerProperty); set => SetValue(PlayerProperty, value); }
    WriteableBitmap? bitmap;
    CancellationTokenSource? lifetime;
    Task pending = Task.CompletedTask;
    bool attached;
    internal bool HasPreview => bitmap != null;
    internal Task Pending => pending;

    static BitmapCuePreview() => IsHitTestVisibleProperty.OverrideDefaultValue<BitmapCuePreview>(false);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == RowProperty)
        {
            if (change.GetOldValue<CueItemViewModel?>() is { } old) old.PropertyChanged -= RowChanged;
            if (Row is { } row) row.PropertyChanged += RowChanged;
        }
        if (change.Property == RowProperty || change.Property == PlayerProperty) Reload();
    }
    void RowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(CueItemViewModel.Cue) or nameof(CueItemViewModel.IsMasked)) Reload();
    }
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        attached = true;
        if (Row is { } row) { row.PropertyChanged -= RowChanged; row.PropertyChanged += RowChanged; }
        Reload();
    }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        attached = false;
        if (Row is { } row) row.PropertyChanged -= RowChanged;
        Reset();
        base.OnDetachedFromVisualTree(e);
    }
    void Reset()
    {
        CancellationTokenSource? previous = lifetime;
        lifetime = null;
        previous?.Cancel();
        // The worker owns disposal until it has observed cancellation.
        bitmap?.Dispose(); bitmap = null;
        InvalidateMeasure(); InvalidateVisual();
    }
    void Reload()
    {
        Reset();
        if (!attached || Row is not { ShowBitmap: true } row || Player is not { } player) return;
        CancellationTokenSource mine = lifetime = new();
        pending = LoadAsync(row, player, row.Cue, mine);
    }
    async Task LoadAsync(CueItemViewModel row, IPlaybackController player, CueInfo cue, CancellationTokenSource mine)
    {
        try
        {
            var image = await player.GetCueBitmapAsync(cue, row.Slot, mine.Token);
            if (mine.IsCancellationRequested || !attached || !ReferenceEquals(Row, row)
                || !ReferenceEquals(Player, player) || !ReferenceEquals(lifetime, mine)
                || row.Cue != cue || !row.ShowBitmap || !player.IsCueCurrent(cue, row.Slot) || image == null) return;
            if (image.Width <= 0 || image.Height <= 0 || (long)image.Width * image.Height > 2_097_152) return;
            WriteableBitmap owned = new(new PixelSize(image.Width, image.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
            try
            {
                using (ILockedFramebuffer fb = owned.Lock())
                    for (int y = 0; y < image.Height; y++)
                        unsafe
                        {
                            image.Data.AsSpan(y * image.Stride, image.Width * 4)
                                .CopyTo(new Span<byte>((byte*)fb.Address + (long)y * fb.RowBytes, image.Width * 4));
                        }
                bitmap = owned;
            }
            catch { owned.Dispose(); throw; }
            InvalidateMeasure(); InvalidateVisual();
        }
        catch (OperationCanceledException) { }
        catch (Exception) { /* A retired native cue or unavailable preview must not break playback/the sidebar. */ }
        finally
        {
            if (ReferenceEquals(lifetime, mine)) lifetime = null;
            mine.Dispose();
        }
    }
    protected override Size MeasureOverride(Size availableSize)
    {
        if (bitmap == null) return new Size(0, 0);
        double scale = Math.Min(1, double.IsFinite(availableSize.Width) ? availableSize.Width / bitmap.PixelSize.Width : 1);
        return new Size(bitmap.PixelSize.Width * scale, bitmap.PixelSize.Height * scale);
    }
    public override void Render(DrawingContext context)
    {
        if (bitmap == null || Bounds.Width <= 0 || Bounds.Height <= 0) return;
        double scale = Math.Min(1, Math.Min(Bounds.Width / bitmap.PixelSize.Width, Bounds.Height / bitmap.PixelSize.Height));
        context.DrawImage(bitmap, new Rect(bitmap.Size), new Rect(0, 0, bitmap.PixelSize.Width * scale, bitmap.PixelSize.Height * scale));
    }
}
