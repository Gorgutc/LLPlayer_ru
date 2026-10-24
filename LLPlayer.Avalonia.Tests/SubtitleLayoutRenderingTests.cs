using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using AwesomeAssertions;
using LLPlayer.Avalonia.Controls;
using LLPlayer.Avalonia.Services;
using LLPlayer.Avalonia.ViewModels;
using LLPlayer.Avalonia.Views;
using Avalonia.Threading;

namespace LLPlayer.Avalonia.Tests;

/// <summary>
/// Text-layout behavior of the shared overlay control: horizontal line alignment, line-break flattening and the
/// word-hit geometry that must keep matching whatever is drawn. These run headlessly on the real control.
/// </summary>
public class SubtitleLayoutRenderingTests
{
    static SubtitleText Create(
        string text = "ab cd",
        double width = 320,
        TextAlignment alignment = TextAlignment.Center,
        bool ignoreLineBreaks = false)
        => new()
        {
            Text = text,
            Width = width,
            FontSize = 20,
            OutlineThickness = 3,
            Fill = Brushes.White,
            Outline = Brushes.Black,
            TextAlignment = alignment,
            IgnoreLineBreaks = ignoreLineBreaks,
        };

    static void Layout(SubtitleText control)
    {
        // No window/visual parent is needed: the shared control lays out from its own width and the app's text shaper.
        control.Measure(new Size(control.Width, double.PositiveInfinity));
        control.Arrange(new Rect(0, 0, control.Width, control.DesiredSize.Height));
        Dispatcher.UIThread.RunJobs();
    }

    static int FirstWordColumn(SubtitleText control, string expectedWord)
    {
        for (int x = 0; x < control.Bounds.Width; x += 2)
            if (control.TryGetWordAt(new Point(x, control.Bounds.Height / 2), out string word) && word == expectedWord)
                return x;
        return -1;
    }

    [AvaloniaFact]
    public void Alignment_moves_the_drawn_line_and_the_word_hits_together()
    {
        SubtitleText left = Create(alignment: TextAlignment.Left);
        SubtitleText center = Create(alignment: TextAlignment.Center);
        SubtitleText right = Create(alignment: TextAlignment.Right);
        Layout(left);
        Layout(center);
        Layout(right);

        int leftColumn = FirstWordColumn(left, "ab");
        int centerColumn = FirstWordColumn(center, "ab");
        int rightColumn = FirstWordColumn(right, "ab");

        leftColumn.Should().BeGreaterThanOrEqualTo(0);
        centerColumn.Should().BeGreaterThanOrEqualTo(0);
        rightColumn.Should().BeGreaterThanOrEqualTo(0);
        leftColumn.Should().BeLessThan((int)(left.Bounds.Width * 0.2));
        centerColumn.Should().BeInRange((int)(left.Bounds.Width * 0.3), (int)(left.Bounds.Width * 0.7));
        rightColumn.Should().BeGreaterThan((int)(left.Bounds.Width * 0.55));
        leftColumn.Should().BeLessThan(centerColumn);
        centerColumn.Should().BeLessThan(rightColumn);
    }

    [AvaloniaFact]
    public void Alignment_moves_the_rendered_glyphs_not_only_the_word_hits()
    {
        int leftFirst = FirstRenderedColumn(TextAlignment.Left);
        int centerFirst = FirstRenderedColumn(TextAlignment.Center);
        int rightFirst = FirstRenderedColumn(TextAlignment.Right);

        leftFirst.Should().BeGreaterThanOrEqualTo(0);
        centerFirst.Should().BeGreaterThan(leftFirst, "centered glyphs start right of left-aligned glyphs");
        rightFirst.Should().BeGreaterThan(centerFirst, "right-aligned glyphs start right of centered glyphs");
        leftFirst.Should().BeLessThan(40);
        rightFirst.Should().BeGreaterThan(180);
    }

    static int FirstRenderedColumn(TextAlignment alignment)
    {
        SubtitleText control = Create(alignment: alignment);
        control.Fill = Brushes.Black;
        control.OutlineThickness = 0;
        control.Width = 320;
        control.Height = 60;
        control.HorizontalAlignment = HorizontalAlignment.Left;
        control.VerticalAlignment = VerticalAlignment.Top;
        Window window = new() { Width = 360, Height = 120, Background = Brushes.White, Content = control };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);
            Dispatcher.UIThread.RunJobs();
            using WriteableBitmap frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("no frame");
            using ILockedFramebuffer framebuffer = frame.Lock();
            Point origin = control.TranslatePoint(default, window)!.Value;
            int originX = (int)Math.Round(origin.X), originY = (int)Math.Round(origin.Y);
            unsafe
            {
                for (int x = 0; x < (int)control.Width; x++)
                    for (int y = 0; y < (int)control.Height; y++)
                    {
                        int px = originX + x, py = originY + y;
                        if (px < 0 || px >= framebuffer.Size.Width || py < 0 || py >= framebuffer.Size.Height)
                            continue;
                        byte* pixel = (byte*)framebuffer.Address + py * framebuffer.RowBytes + px * 4;
                        if (pixel[0] + pixel[1] + pixel[2] < 384)
                            return x; // opaque black fill on a white window
                    }
            }
            return -1;
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Changing_alignment_invalidates_the_cached_line_geometry()
    {
        SubtitleText control = Create(alignment: TextAlignment.Left);
        Layout(control);
        int leftColumn = FirstWordColumn(control, "ab");
        leftColumn.Should().BeGreaterThanOrEqualTo(0);

        control.TextAlignment = TextAlignment.Right;
        Dispatcher.UIThread.RunJobs();
        Layout(control);

        int rightColumn = FirstWordColumn(control, "ab");
        rightColumn.Should().BeGreaterThan(leftColumn + 40, "the cached layout must be rebuilt for the new alignment");
    }

    [AvaloniaFact]
    public void Ignore_line_breaks_flattens_multiline_text_and_keeps_hits_on_one_line()
    {
        SubtitleText multiline = Create(text: "ab\ncd", ignoreLineBreaks: false);
        SubtitleText flattened = Create(text: "ab\ncd", ignoreLineBreaks: true);
        Layout(multiline);
        Layout(flattened);

        flattened.Bounds.Height.Should().BeLessThan(multiline.Bounds.Height, "the flattened line wraps as one line");

        int flattenedColumn = FirstWordColumn(flattened, "cd");
        flattenedColumn.Should().BeGreaterThanOrEqualTo(0, "the flattened single line must expose the second word");
        flattened.TryGetWordAt(new Point(flattenedColumn, flattened.Bounds.Height / 2), out string flatWord)
            .Should().BeTrue();
        flatWord.Should().Be("cd");

        int secondLineY = (int)(multiline.Bounds.Height * 0.75);
        multiline.TryGetWordAt(new Point(FindWordColumnAt(multiline, "cd", secondLineY), secondLineY), out string secondWord).Should().BeTrue();
        secondWord.Should().Be("cd");

        static int FindWordColumnAt(SubtitleText control, string expected, int y)
        {
            for (int x = 0; x < control.Bounds.Width; x += 2)
                if (control.TryGetWordAt(new Point(x, y), out string word) && word == expected)
                    return x;
            return -1;
        }
    }

    [AvaloniaFact]
    public void Toggling_line_break_handling_invalidates_the_cached_layout()
    {
        SubtitleText control = Create(text: "ab\ncd", ignoreLineBreaks: false);
        Layout(control);
        double multilineHeight = control.Bounds.Height;

        control.IgnoreLineBreaks = true;
        Dispatcher.UIThread.RunJobs();
        Layout(control);
        control.Bounds.Height.Should().BeLessThan(multilineHeight);
    }

    [AvaloniaTheory]
    [InlineData("- Hello.\n- How are you?")]
    [InlineData("Hello\n\nWorld")]
    [InlineData("a\r\n\r\nb")]
    [InlineData("plain")]
    public void Ignore_line_breaks_matches_the_canonical_flatten_semantics(string sample)
    {
        string canonical = FlyleafLib.SubtitleTextUtil.FlattenText(sample);
        SubtitleText ignored = MeasureOnly(sample, ignoreLineBreaks: true);
        SubtitleText reference = MeasureOnly(canonical, ignoreLineBreaks: false);

        ignored.DesiredSize.Width.Should().BeApproximately(reference.DesiredSize.Width, 0.5,
            "the flattened text must equal the engine helper's output (dash lists stay multiline, blank series collapse)");
        ignored.DesiredSize.Height.Should().BeApproximately(reference.DesiredSize.Height, 0.5);

        static SubtitleText MeasureOnly(string text, bool ignoreLineBreaks)
        {
            // An unbounded width makes DesiredSize.Width reflect the content (a finite measure would return the
            // available width verbatim), so both the width and the height assertions compare real text geometry.
            SubtitleText control = new() { Text = text, FontSize = 20, OutlineThickness = 0, IgnoreLineBreaks = ignoreLineBreaks };
            control.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return control;
        }
    }

    [AvaloniaFact]
    public void Empty_text_with_layout_options_renders_nothing()
    {
        SubtitleText control = Create(text: "", alignment: TextAlignment.Right, ignoreLineBreaks: true);
        Layout(control);
        control.Bounds.Width.Should().Be(320);
        control.TryGetWordAt(new Point(10, 10), out _).Should().BeFalse();
    }

    [AvaloniaFact]
    public void Settings_preview_shows_alignment_and_line_break_options()
    {
        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        var window = new MainWindow { Width = 1000, Height = 700 };
        MainWindowViewModel vm = new(new FakePlaybackController(), new AppPrefs().Normalize(), _ => { }, new FakeTranslator(), window);
        window.Attach(vm, renderer: null);
        SettingsViewModel draft = vm.CreateSettings();
        var previewWindow = new SettingsWindow(draft) { Width = 760, Height = 900 };
        try
        {
            previewWindow.Show();
            Dispatcher.UIThread.RunJobs();
            SubtitleText preview = previewWindow.FindControl<SubtitleText>("SubtitleFontPreview")!;
            preview.TextAlignment.Should().Be(TextAlignment.Center);
            preview.IgnoreLineBreaks.Should().BeFalse();
            double multilineHeight = MeasuredHeight(preview);

            draft.SubtitleLineAlignment = "Left";
            draft.SubtitleIgnoreLineBreak = true;
            Dispatcher.UIThread.RunJobs();
            preview.TextAlignment.Should().Be(TextAlignment.Left);
            preview.IgnoreLineBreaks.Should().BeTrue();
            MeasuredHeight(preview).Should().BeLessThan(multilineHeight,
                "the two-line preview visibly flattens when cue line breaks are ignored");

            static double MeasuredHeight(SubtitleText control)
            {
                control.Measure(new Size(500, double.PositiveInfinity));
                return control.DesiredSize.Height;
            }
        }
        finally
        {
            previewWindow.Close();
            window.Close();
            Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        }
    }
}
