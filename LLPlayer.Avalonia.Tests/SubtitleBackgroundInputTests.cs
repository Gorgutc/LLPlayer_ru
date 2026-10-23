using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LLPlayer.Avalonia.Controls;
using LLPlayer.Avalonia.Services;
using LLPlayer.Avalonia.ViewModels;
using LLPlayer.Avalonia.Views;
using static LLPlayer.Avalonia.Tests.SubtitleBackgroundTestSupport;

namespace LLPlayer.Avalonia.Tests;

public sealed class SubtitleBackgroundInputTests
{
    const string InputSubtitle = "alpha beta gamma delta epsilon\n\nI";

    [AvaloniaFact]
    public void Existing_pointer_oracle_observes_real_glyph_words_and_video_gestures_before_the_feature()
    {
        foreach (string region in Regions)
            foreach (bool doubleTap in new[] { false, true })
                AssertBaselineInput(ExerciseControl(0, 3, region, doubleTap, background: false), region, doubleTap);
    }

    [AvaloniaFact]
    public async Task Existing_MainWindow_oracle_opens_word_popup_dismisses_on_video_and_double_taps_fullscreen()
    {
        foreach (string region in new[] { "glyph", "off-text" })
            foreach (bool doubleTap in new[] { false, true })
                AssertMainWindowEffects(await ExerciseMainWindow(0, region, doubleTap, background: false), region, doubleTap);
    }

    [AvaloniaTheory]
    [InlineData(0, 0)]
    [InlineData(0, 8)]
    [InlineData(1, 0)]
    [InlineData(1, 8)]
    public void Background_preserves_actual_hit_targets_pointer_sources_taps_and_double_taps_for_glyph_and_empty_regions(
        int slot, double outline)
    {
        foreach (string region in Regions)
            foreach (bool doubleTap in new[] { false, true })
            {
                InputEvidence off = ExerciseControl(slot, outline, region, doubleTap, background: false);
                AssertBaselineInput(off, region, doubleTap);
                InputEvidence on = ExerciseControl(slot, outline, region, doubleTap, background: true);
                Assert.Equal(off.HitSource, on.HitSource);
                Assert.Equal(off.Events, on.Events);
                Assert.Equal(off.Words, on.Words);
            }
    }

    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task MainWindow_background_preserves_popup_dismissal_fullscreen_and_routed_input_for_each_region(int slot)
    {
        foreach (string region in Regions)
            foreach (bool doubleTap in new[] { false, true })
            {
                WindowEvidence off = await ExerciseMainWindow(slot, region, doubleTap, background: false);
                AssertMainWindowEffects(off, region, doubleTap);
                WindowEvidence on = await ExerciseMainWindow(slot, region, doubleTap, background: true);
                Assert.Equal(off, on);
            }
    }

    [AvaloniaFact]
    public void Overlay_background_does_not_activate_the_shared_sidebar_SubtitleText()
    {
        FakePlaybackController player = new() { IsOpened = true, PrimaryText = Subtitle };
        MainWindow window = new(new KeyMapper(), null) { Width = 1000, Height = 600 };
        MainWindowViewModel vm = new(player, new AppPrefs().Normalize(), _ => { }, new FakeTranslator(), window);
        window.Attach(vm, null);
        player.Cues[0].Add(new CueInfo(0, TimeSpan.Zero, TimeSpan.FromSeconds(3), "sidebar words"));
        player.RaiseCuesChanged(0);
        vm.ToggleSidebar();
        window.Show();
        try
        {
            Capture(window);
            SubtitleText overlay = window.FindControl<SubtitleText>("PrimarySubtitle")!;
            SetBackground(overlay, Brushes.Black);
            Capture(window);
            ListBox list = window.FindControl<ListBox>("CueList")!;
            SubtitleText sidebar = list.GetVisualDescendants().OfType<SubtitleText>().Single();
            Assert.Null(GetBackground(sidebar));
            Assert.Equal("sidebar words", sidebar.Text);
            Assert.True(sidebar.IsEffectivelyVisible);
        }
        finally { window.Close(); vm.Dispose(); }
    }

    static readonly string[] Regions = ["glyph", "space", "inter-line", "short-line-side", "off-text"];

    static void AssertBaselineInput(InputEvidence evidence, string region, bool doubleTap)
    {
        Assert.Contains(evidence.Events, item => item.StartsWith("pressed:", StringComparison.Ordinal));
        if (region == "glyph")
        {
            Assert.Contains("SubtitleText", evidence.HitSource);
            Assert.NotEmpty(evidence.Words);
            Assert.Contains(evidence.Words, word => word.Contains(":alpha:", StringComparison.Ordinal));
        }
        // A handled word press intentionally suppresses Avalonia's tap recognizer. Real video/off-text
        // routing is the positive gesture oracle; every subtitle region still compares its exact OFF/ON events.
        if (region != "off-text") return;
        Assert.Empty(evidence.Words);
        if (doubleTap) Assert.Contains(evidence.Events, item => item.StartsWith("double:", StringComparison.Ordinal));
        else Assert.Contains(evidence.Events, item => item.StartsWith("tap:", StringComparison.Ordinal));
    }

    static void AssertMainWindowEffects(WindowEvidence evidence, string region, bool doubleTap)
    {
        Assert.NotEmpty(evidence.Events);
        if (region == "glyph")
        {
            Assert.True(evidence.PopupOpen, "An actual subtitle glyph must keep/open its word popup.");
            Assert.False(evidence.FullScreen, "Double clicking subtitle glyphs must retain the existing fullscreen exclusion.");
        }
        if (region == "off-text")
        {
            Assert.False(evidence.PopupOpen, "An actual video tap must dismiss the pre-opened popup.");
            Assert.Equal(doubleTap, evidence.FullScreen);
            Assert.Contains(doubleTap ? "double:" : "tap:", evidence.Events);
        }
    }

    static InputEvidence ExerciseControl(int slot, double outline, string region, bool doubleTap, bool background)
    {
        SubtitleText text = CreateText(slot, outline);
        text.Text = InputSubtitle;
        Window window = CreateWindow(text);
        window.Show();
        try
        {
            Frame frame = Capture(window);
            Point point = Sample(window, text, frame, region);
            // Locate independently from visible glyphs/known layout, before the new brush exists.
            if (background) SetBackground(text, Brushes.Black);
            Capture(window); // The composition hit-test tree must include the rendered state under test.
            string target = Source(window.InputHitTest(point));
            List<string> events = [];
            List<string> words = [];
            Observe(window, events);
            text.WordClicked += (_, e) => words.Add($"{e.Slot}:{e.Word}:{e.Text}");
            Tap(window, point, doubleTap);
            return new InputEvidence(target, events.ToArray(), words.ToArray());
        }
        finally { window.Close(); }
    }

    static async Task<WindowEvidence> ExerciseMainWindow(int slot, string region, bool doubleTap, bool background)
    {
        FakePlaybackController player = new() { IsOpened = true, IsPlaying = true };
        MainWindow window = new(new KeyMapper(), null) { Width = 1000, Height = 600 };
        MainWindowViewModel vm = new(player, new AppPrefs().Normalize(), _ => { }, new FakeTranslator(), window);
        window.Attach(vm, null);
        if (slot == 0) player.PrimaryText = InputSubtitle;
        else player.SecondaryText = InputSubtitle;
        window.Show();
        try
        {
            SubtitleText text = window.FindControl<SubtitleText>(slot == 0 ? "PrimarySubtitle" : "SecondarySubtitle")!;
            text.Width = 246;
            text.FontSize = 28;
            text.FontFamily = Inter;
            text.Fill = Ink;
            text.OutlineThickness = 3;
            Frame frame = Capture(window);
            Point point = Sample(window, text, frame, region);
            if (background) SetBackground(text, Brushes.Black);
            await vm.WordPopup.OpenAsync("existing", "A pre-opened word popup", slot, 100, 80);
            Capture(window);
            Assert.True(vm.WordPopup.IsOpen);
            string target = Source(window.InputHitTest(point));
            List<string> events = [];
            Observe(window, events);
            Tap(window, point, doubleTap);
            return new WindowEvidence(target, string.Join("|", events), vm.WordPopup.IsOpen, vm.IsFullScreen,
                vm.WordPopup.Word, player.PauseCalls, player.ToggleCalls);
        }
        finally
        {
            window.Close();
            vm.Dispose();
            await vm.WordPopup.DisposeAsync();
        }
    }

    static Point Sample(Window window, SubtitleText text, Frame frame, string region)
    {
        Point origin = text.TranslatePoint(default, window)!.Value;
        Vector offset = new(origin.X, origin.Y);
        double pad = text.OutlineThickness;
        using TextLayout expected = ExpectedLayout(text);
        Assert.True(expected.TextLines.Count >= 3);
        Rect first = expected.HitTestTextPosition(0);
        if (region == "glyph")
        {
            Rect last = expected.HitTestTextPosition(4);
            Rect alpha = new(first.X + pad, first.Y + pad, last.Right - first.X, first.Height);
            return FindInk(frame, alpha.Translate(offset));
        }
        if (region == "space")
        {
            Rect space = expected.HitTestTextPosition(5);
            return new Point(origin.X + pad + space.Center.X, origin.Y + pad + space.Center.Y);
        }
        if (region == "inter-line")
        {
            // Adjacent glyph outlines can close the small line gap at thickness 8. This fixture contains
            // an explicit empty line, so its independent linebox center supplies real clear inter-line pixels.
            TextLine empty = expected.TextLines[^2];
            Assert.Equal(InputSubtitle.LastIndexOf('\n'), empty.FirstTextSourceIndex);
            double emptyTop = expected.TextLines.Take(expected.TextLines.Count - 2).Sum(line => line.Height);
            double center = origin.Y + pad + emptyTop + empty.Height / 2;
            Rect strip = new(origin.X + text.Bounds.Width / 2 - 18, center - 2, 36, 4);
            return FindClear(frame, strip);
        }
        if (region == "short-line-side")
        {
            Rect block = ExpectedBlock(text, expected);
            double lastTop = expected.TextLines.Take(expected.TextLines.Count - 1).Sum(line => line.Height);
            return new Point(origin.X + block.Left + 4, origin.Y + pad + lastTop + expected.TextLines[^1].Height / 2);
        }
        Assert.Equal("off-text", region);
        return new Point(origin.X + text.Bounds.Width / 2, Math.Max(40, origin.Y - 18));
    }

    static Point FindClear(Frame frame, Rect region)
    {
        for (int y = (int)Math.Ceiling(region.Top); y < region.Bottom; y++)
            for (int x = (int)Math.Ceiling(region.Left); x < region.Right; x++)
            {
                Color c = frame.Pixel(x, y);
                // MainWindow's video is black; the isolated fixture uses the known blue-grey backdrop.
                if (c == Backdrop || c == Colors.Black) return new Point(x + .5, y + .5);
            }
        Assert.Fail($"The fixture must have a clear inter-line pixel within {region}.");
        return default;
    }

    static void Observe(Window window, List<string> events)
    {
        window.AddHandler(InputElement.PointerPressedEvent,
            (_, e) => events.Add($"pressed:{e.ClickCount}:{Source(e.Source)}"), RoutingStrategies.Tunnel, handledEventsToo: true);
        window.AddHandler(InputElement.TappedEvent,
            (_, e) => events.Add($"tap:{Source(e.Source)}"), RoutingStrategies.Bubble, handledEventsToo: true);
        window.AddHandler(InputElement.DoubleTappedEvent,
            (_, e) => events.Add($"double:{Source(e.Source)}"), RoutingStrategies.Bubble, handledEventsToo: true);
    }

    static void Tap(Window window, Point point, bool doubleTap)
    {
        window.MouseMove(point);
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        if (doubleTap)
        {
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
        }
        Dispatcher.UIThread.RunJobs();
    }

    static string Source(object? value)
        => value is Control control ? $"{value.GetType().Name}#{control.Name}" : value?.GetType().Name ?? "null";

    sealed record InputEvidence(string HitSource, string[] Events, string[] Words);
    sealed record WindowEvidence(string HitSource, string Events, bool PopupOpen, bool FullScreen, string? PopupWord, int PauseCalls, int ToggleCalls);
}
