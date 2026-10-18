using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using AwesomeAssertions;
using FlyleafLib.MediaPlayer;
using LLPlayer.Avalonia.Controls;
using LLPlayer.Avalonia.Services;
using LLPlayer.Avalonia.ViewModels;
using LLPlayer.Avalonia.Views;

namespace LLPlayer.Avalonia.Tests;

public class BitmapSubtitleOverlayTests
{
    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(1)]
    public void BitmapOnlyCue_HidesAndReturns_WhenSubtitlesAreToggled(int slot)
    {
        FakePlaybackController player = new() { IsOpened = true };
        MainWindow window = new(new KeyMapper(), null) { Width = 1000, Height = 600 };
        MainWindowViewModel vm = new(player, new AppPrefs().Normalize(), _ => { }, new FakeTranslator(), window);
        window.Attach(vm, renderer: null);
        window.Show();
        try
        {
            // An image-only cue has no OCR/text. Text visibility cannot govern its bitmap.
            byte[] pixels = new byte[20 * 20 * 4];
            for (int offset = 0; offset < pixels.Length; offset += 4)
            {
                pixels[offset] = 255;
                pixels[offset + 2] = 255;
                pixels[offset + 3] = 255;
            }
            SubtitleImage image = new(new BgraBitmap(pixels, 20, 20), 40, 40, 20, 20, 100, 100);
            if (slot == 0) player.PrimaryImage = image;
            else player.SecondaryImage = image;
            Dispatcher.UIThread.RunJobs();

            SubtitleImageView overlay = window.FindControl<SubtitleImageView>(slot == 0 ? "PrimaryImageView" : "SecondaryImageView")!;
            Control video = window.FindControl<VideoView>("Video")!;
            Point center = video.TranslatePoint(new Point(video.Bounds.Width / 2, video.Bounds.Height / 2), window)!.Value;
            PixelAt(window, center).Should().Be(Colors.Magenta, "the visible image-only cue must actually be drawn");

            vm.ToggleSubtitlesCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            PixelAt(window, center).Should().Be(Colors.Black, "turning subtitles off must remove retained bitmap pixels");
            overlay.IsVisible.Should().BeFalse();
            overlay.Image.Should().BeSameAs(image, "hiding a cue must not discard it");

            vm.ToggleSubtitlesCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            overlay.IsVisible.Should().BeTrue();
            PixelAt(window, center).Should().Be(Colors.Magenta, "turning subtitles on must redraw the retained cue without a new engine event");
        }
        finally
        {
            window.Close();
        }
    }

    static Color PixelAt(TopLevel window, Point point)
    {
        using WriteableBitmap frame = window.CaptureRenderedFrame()!;
        using ILockedFramebuffer fb = frame.Lock();
        unsafe
        {
            byte* pixel = (byte*)fb.Address + (int)point.Y * fb.RowBytes + (int)point.X * 4;
            return Color.FromRgb(pixel[2], pixel[1], pixel[0]);
        }
    }
}
