using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.TextFormatting;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AwesomeAssertions;
using LLPlayer.Avalonia.Controls;
using LLPlayer.Avalonia.Services;
using LLPlayer.Avalonia.ViewModels;
using LLPlayer.Avalonia.Views;

namespace LLPlayer.Avalonia.Tests;

/// <summary>
/// Word/phrase selection over the shared subtitle control: a press starts a potential drag, dragging selects
/// the word range with a visible highlight, and the release performs the existing word action for the phrase.
/// </summary>
public class SubtitleSelectionTests
{
    const string Cue = "Hello brave new world";

    static (MainWindow Window, MainWindowViewModel Vm, FakePlaybackController Player) CreateWindow(bool sidebar = false)
    {
        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        FakePlaybackController player = new();
        MainWindow window = new(new KeyMapper(), null) { Width = 1000, Height = 600 };
        AppPrefs prefs = new AppPrefs().Normalize();
        prefs.SidebarVisible = sidebar;
        MainWindowViewModel vm = new(player, prefs, _ => { }, new FakeTranslator(), window, null);
        window.Attach(vm, renderer: null);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();
        Dispatcher.UIThread.RunJobs();
        return (window, vm, player);
    }

    static (MainWindow Window, MainWindowViewModel Vm, FakePlaybackController Player, SubtitleText Text) CreateOverlay(string cue)
    {
        var (window, vm, player) = CreateWindow();
        player.IsOpened = true;
        player.PrimaryText = cue;
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();
        Dispatcher.UIThread.RunJobs();
        return (window, vm, player, window.FindControl<SubtitleText>("PrimarySubtitle")!);
    }

    static Point FindClickableWordPoint(Window window, SubtitleText text, string word)
    {
        Rect bounds = text.Bounds;
        for (double y = 1; y < bounds.Height - 1; y += 1)
        {
            for (double x = 1; x < bounds.Width - 1; x += 1)
            {
                Point local = new(x, y);
                if (!text.TryGetWordAt(local, out string found) || found != word)
                    continue;
                Point windowPoint = ToWindow(text, window, local);
                if (ReferenceEquals(window.InputHitTest(windowPoint), text))
                    return windowPoint;
            }
        }
        throw new Xunit.Sdk.XunitException($"A clickable '{word}' pixel must exist inside {text.Bounds}.");
    }

    static Point ToWindow(SubtitleText text, Window window, Point local)
        => text.TranslatePoint(local, window) ?? throw new Xunit.Sdk.XunitException("The point must translate to the window.");

    static void Press(Window window, Point point)
    {
        window.MouseMove(point);
        window.MouseDown(point, MouseButton.Left, RawInputModifiers.LeftMouseButton);
        Dispatcher.UIThread.RunJobs();
    }

    static void MoveDrag(Window window, Point point)
    {
        window.MouseMove(point, RawInputModifiers.LeftMouseButton);
        Dispatcher.UIThread.RunJobs();
    }

    static void Drag(Window window, Point from, Point to)
    {
        Press(window, from);
        MoveDrag(window, to);
        window.MouseUp(to, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    static int ChangedPixels(WriteableBitmap before, WriteableBitmap after, int delta = 12)
    {
        using ILockedFramebuffer a = before.Lock();
        using ILockedFramebuffer b = after.Lock();
        int changed = 0;
        unsafe
        {
            for (int y = 0; y < a.Size.Height; y++)
            {
                byte* pa = (byte*)a.Address + y * a.RowBytes;
                byte* pb = (byte*)b.Address + y * b.RowBytes;
                for (int x = 0; x < a.Size.Width; x++)
                {
                    int ba = pa[x * 4], ga = pa[x * 4 + 1], ra = pa[x * 4 + 2];
                    int bb = pb[x * 4], gb = pb[x * 4 + 1], rb = pb[x * 4 + 2];
                    if (Math.Abs(ba - bb) > delta || Math.Abs(ga - gb) > delta || Math.Abs(ra - rb) > delta)
                        changed++;
                }
            }
        }
        return changed;
    }

    [AvaloniaFact]
    public void DragForward_RaisesOnePhraseClick()
    {
        var (window, _, _, text) = CreateOverlay(Cue);
        try
        {
            List<WordClickedEventArgs> events = [];
            text.WordClicked += (_, e) => events.Add(e);
            Point from = FindClickableWordPoint(window, text, "brave");
            Point to = FindClickableWordPoint(window, text, "world");
            Drag(window, from, to);

            events.Should().ContainSingle();
            events[0].Word.Should().Be("brave new world");
            events[0].IsWord.Should().BeFalse();
            events[0].Text.Should().Be(Cue);
            events[0].Slot.Should().Be(0);
        }
        finally
        {
            window.Close();
            Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        }
    }

    [AvaloniaFact]
    public void ReverseDrag_NormalizesThePhrase()
    {
        var (window, _, _, text) = CreateOverlay(Cue);
        try
        {
            List<WordClickedEventArgs> events = [];
            text.WordClicked += (_, e) => events.Add(e);
            Point from = FindClickableWordPoint(window, text, "world");
            Point to = FindClickableWordPoint(window, text, "brave");
            Drag(window, from, to);

            events.Should().ContainSingle();
            events[0].Word.Should().Be("brave new world");
            events[0].IsWord.Should().BeFalse();
        }
        finally
        {
            window.Close();
            Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        }
    }

    [AvaloniaFact]
    public void DragAcrossHardLineBreak_ReplacesBreakWithOneSpace()
    {
        var (window, _, _, text) = CreateOverlay("Hello brave\nnew world");
        try
        {
            List<WordClickedEventArgs> events = [];
            int presses = 0;
            text.WordClicked += (_, e) => events.Add(e);
            text.WordPressed += (_, _) => presses++;
            Point from = FindClickableWordPoint(window, text, "Hello");
            Point to = FindClickableWordPoint(window, text, "new");
            Drag(window, from, to);

            string diagnostics = $"presses={presses} events={events.Count} hitFrom={window.InputHitTest(from)?.GetType().Name} hitTo={window.InputHitTest(to)?.GetType().Name} bounds={text.Bounds} origin={text.TranslatePoint(default, window)}";
            events.Should().ContainSingle(diagnostics);
            events[0].Word.Should().Be("Hello brave new");
            events[0].IsWord.Should().BeFalse();
        }
        finally
        {
            window.Close();
            Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        }
    }

    [AvaloniaFact]
    public void SingleClick_RaisesOneWordClick()
    {
        var (window, _, _, text) = CreateOverlay(Cue);
        try
        {
            List<WordClickedEventArgs> events = [];
            text.WordClicked += (_, e) => events.Add(e);
            Point point = FindClickableWordPoint(window, text, "brave");
            Press(window, point);
            window.MouseUp(point, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            events.Should().ContainSingle();
            events[0].Word.Should().Be("brave");
            events[0].IsWord.Should().BeTrue();
        }
        finally
        {
            window.Close();
            Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        }
    }

    [AvaloniaFact]
    public void ReleaseOutsideWords_RaisesNothing()
    {
        var (window, _, _, text) = CreateOverlay(Cue);
        try
        {
            List<WordClickedEventArgs> events = [];
            text.WordClicked += (_, e) => events.Add(e);
            Point from = FindClickableWordPoint(window, text, "brave");
            Point outside = ToWindow(text, window, new Point(text.Bounds.Width - 2, text.Bounds.Height - 2));
            Press(window, from);
            MoveDrag(window, outside);
            window.MouseUp(outside, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            events.Should().BeEmpty();
        }
        finally
        {
            window.Close();
            Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        }
    }

    [AvaloniaFact]
    public void Dragging_ShowsVisibleHighlight_AndClearsItAfterRelease()
    {
        var (window, vm, _, text) = CreateOverlay(Cue);
        try
        {
            text.SelectionBrush.Should().NotBeNull("the overlay must bind the themed selection brush");
            WriteableBitmap baseline = window.CaptureRenderedFrame()!;
            Point from = FindClickableWordPoint(window, text, "brave");
            Point to = FindClickableWordPoint(window, text, "world");
            Point outside = ToWindow(text, window, new Point(1, 1));

            Press(window, from);
            MoveDrag(window, to);
            WriteableBitmap during = window.CaptureRenderedFrame()!;
            ChangedPixels(baseline, during).Should().BeGreaterThan(50,
                "the dragged word range must be visibly highlighted");

            window.MouseUp(outside, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            WriteableBitmap after = window.CaptureRenderedFrame()!;
            ChangedPixels(baseline, after).Should().BeLessThan(5, "the highlight must be cleared after release");
            vm.WordPopup.IsOpen.Should().BeFalse("a release outside words must not open the popup");
        }
        finally
        {
            window.Close();
            Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        }
    }

    [AvaloniaFact]
    public void PressOnOverlayWord_PausesBeforeRelease_AndReleaseOpensPhrasePopup()
    {
        var (window, vm, player, text) = CreateOverlay(Cue);
        try
        {
            player.IsPlaying = true;
            Dispatcher.UIThread.RunJobs();
            Point from = FindClickableWordPoint(window, text, "brave");
            Point to = FindClickableWordPoint(window, text, "world");

            Press(window, from);
            player.PauseCalls.Should().Be(1, "the press must pause playback immediately");
            MoveDrag(window, to);
            window.MouseUp(to, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            vm.WordPopup.IsOpen.Should().BeTrue();
            vm.WordPopup.Word.Should().Be("brave new world");
            vm.WordPopup.Context.Should().Be(Cue);
        }
        finally
        {
            window.Close();
            Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        }
    }

    [AvaloniaFact]
    public void TextChangeDuringDrag_CancelsSelectionAndRaisesNoClick()
    {
        var (window, vm, player, text) = CreateOverlay(Cue);
        try
        {
            List<WordClickedEventArgs> events = [];
            text.WordClicked += (_, e) => events.Add(e);
            Point from = FindClickableWordPoint(window, text, "brave");
            Point to = FindClickableWordPoint(window, text, "world");
            Press(window, from);
            MoveDrag(window, to);

            player.PrimaryText = "";
            Dispatcher.UIThread.RunJobs();
            window.MouseUp(to, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            events.Should().BeEmpty();
            vm.WordPopup.IsOpen.Should().BeFalse();
        }
        finally
        {
            window.Close();
            Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        }
    }
    [AvaloniaFact]
    public void SidebarRow_DragSelectsPhrase_AndOpensPopup()
    {
        var (window, vm, player) = CreateWindow(sidebar: true);
        try
        {
            player.Cues[0].Add(new CueInfo(0, TimeSpan.Zero, TimeSpan.FromSeconds(4), Cue));
            player.RaiseCuesChanged(0);
            Dispatcher.UIThread.RunJobs();
            window.CaptureRenderedFrame();
            Dispatcher.UIThread.RunJobs();

            SubtitleText? row = window.GetVisualDescendants()
                .OfType<SubtitleText>()
                .FirstOrDefault(item => item.Text == Cue);
            row.Should().NotBeNull();
            Point from = FindClickableWordPoint(window, row!, "brave");
            Point to = FindClickableWordPoint(window, row!, "world");
            Drag(window, from, to);

            vm.WordPopup.IsOpen.Should().BeTrue();
            vm.WordPopup.Word.Should().Be("brave new world");
            vm.WordPopup.Context.Should().Be(Cue);
        }
        finally
        {
            window.Close();
            Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        }
    }
}
