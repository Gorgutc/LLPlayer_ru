using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using FlyleafLib.MediaPlayer;
using LLPlayer.Avalonia.Controls;
using LLPlayer.Avalonia.Services;
using LLPlayer.Avalonia.ViewModels;

namespace LLPlayer.Avalonia.Tests;

public class BitmapCuePreviewTests
{
    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task Preview_is_lazy_masked_and_released_when_OCR_replaces_the_image(int slot)
    {
        BitmapPlayer player = new();
        player.Cues[slot].Add(new(0, TimeSpan.Zero, TimeSpan.FromSeconds(1), "", IsBitmap: true));
        CueItemViewModel row = new(player.GetCues(slot)[0], slot) { IsMasked = true };
        BitmapCuePreview preview = new() { Row = row, Player = player };
        Assert.Equal(0, player.Reads);
        Window window = new() { Content = preview, Width = 400, Height = 120 }; window.Show();
        try
        {
            await preview.Pending; Assert.Equal(0, player.Reads); Assert.False(preview.HasPreview);
            row.IsMasked = false;
            await preview.Pending; Assert.Equal(1, player.Reads); Assert.True(preview.HasPreview); Assert.Equal(slot, player.LastSlot);
            row.UpdateCue(row.Cue with { Text = "Recognized cue", OriginalText = "Recognized cue" });
            await preview.Pending; Assert.Equal(1, player.Reads); Assert.False(preview.HasPreview);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Detaching_cancels_ownership_and_rejects_a_late_conversion_even_when_provider_ignores_cancellation()
    {
        TaskCompletionSource<BgraBitmap?> late = new(TaskCreationOptions.RunContinuationsAsynchronously);
        BitmapPlayer player = new() { Next = late.Task };
        player.Cues[1].Add(new(0, TimeSpan.Zero, TimeSpan.FromSeconds(1), "", IsBitmap: true));
        BitmapCuePreview preview = new() { Row = new(player.GetCues(1)[0], 1), Player = player };
        Window window = new() { Content = preview, Width = 400, Height = 120 }; window.Show();
        Task pending = preview.Pending;
        window.Content = null;
        Assert.True(player.LastCancellation.IsCancellationRequested);
        late.SetResult(Image()); await pending;
        Assert.False(preview.HasPreview);
        window.Close();
    }

    static BgraBitmap Image() => new([0xff, 0xff, 0xff, 0xff], 1, 1);
    sealed class BitmapPlayer : FakePlaybackController, IPlaybackController
    {
        public int Reads, LastSlot;
        public Task<BgraBitmap?>? Next;
        public CancellationToken LastCancellation;
        public Task<BgraBitmap?> GetCueBitmapAsync(CueInfo cue, int slot, CancellationToken cancellationToken = default)
        { Reads++; LastSlot = slot; LastCancellation = cancellationToken; return Next ?? Task.FromResult<BgraBitmap?>(Image()); }
    }
}
