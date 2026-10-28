using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
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

    static (MainWindow Window, MainWindowViewModel Vm, FakePlaybackController Player) CreateWindow(bool sidebar = false,
        AppPrefs? preferences = null, FakeTranslator? translator = null, FakeShell? shell = null)
    {
        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        FakePlaybackController player = new();
        MainWindow window = new(new KeyMapper(), null) { Width = 1000, Height = 600 };
        AppPrefs prefs = (preferences ?? new AppPrefs()).Normalize();
        prefs.SidebarVisible = sidebar;
        MainWindowViewModel vm = new(player, prefs, _ => { }, translator ?? new FakeTranslator(), (IAppShell?)shell ?? window, null);
        window.Attach(vm, renderer: null);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame()?.Dispose();
        Dispatcher.UIThread.RunJobs();
        return (window, vm, player);
    }

    static (MainWindow Window, MainWindowViewModel Vm, FakePlaybackController Player, SubtitleText Text) CreateOverlay(string cue,
        AppPrefs? preferences = null, FakeTranslator? translator = null, FakeShell? shell = null)
    {
        var (window, vm, player) = CreateWindow(preferences: preferences, translator: translator, shell: shell);
        player.IsOpened = true;
        player.PrimaryText = cue;
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame()?.Dispose();
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
            const string replacement = "Other brave new place";
            player.PrimaryText = replacement;
            Dispatcher.UIThread.RunJobs();
            using WriteableBitmap replacementBaseline = window.CaptureRenderedFrame()!;
            player.PrimaryText = Cue;
            Dispatcher.UIThread.RunJobs();
            using WriteableBitmap baseline = window.CaptureRenderedFrame()!;

            List<WordClickedEventArgs> events = [];
            IPointer? pointer = null;
            text.WordClicked += (_, e) => events.Add(e);
            text.AddHandler(InputElement.PointerPressedEvent, (_, e) => pointer = e.Pointer,
                RoutingStrategies.Bubble, handledEventsToo: true);
            Point from = FindClickableWordPoint(window, text, "brave");
            Point to = FindClickableWordPoint(window, text, "world");
            Press(window, from);
            MoveDrag(window, to);
            pointer.Should().NotBeNull();
            pointer!.Captured.Should().BeSameAs(text);
            using WriteableBitmap during = window.CaptureRenderedFrame()!;
            ChangedPixels(baseline, during).Should().BeGreaterThan(50, "the selection must exist before replacement");

            player.PrimaryText = replacement;
            Dispatcher.UIThread.RunJobs();
            using WriteableBitmap after = window.CaptureRenderedFrame()!;
            pointer.Captured.Should().BeNull("a replacement cue must release the active selection capture");
            ChangedPixels(replacementBaseline, after).Should().BeLessThan(5,
                "a nonempty replacement must render without the old selection highlight");
            Point replacementWord = FindClickableWordPoint(window, text, "place");
            window.MouseUp(replacementWord, MouseButton.Left);
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

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void SidebarWordPress_PausesBeforeCompletedOrCancelledSelection(bool completeSelection)
    {
        FakeTranslator translator = new();
        FakeShell shell = new();
        var (window, vm, player) = CreateWindow(sidebar: true, translator: translator, shell: shell);
        try
        {
            player.IsOpened = true;
            player.Cues[0].Add(new CueInfo(0, TimeSpan.Zero, TimeSpan.FromSeconds(4), Cue));
            player.RaiseCuesChanged(0);
            player.IsPlaying = true;
            Dispatcher.UIThread.RunJobs();
            window.CaptureRenderedFrame()?.Dispose();
            SubtitleText row = window.GetVisualDescendants().OfType<SubtitleText>()
                .Single(item => item.DataContext is CueItemViewModel && item.Text == Cue);
            Point from = FindClickableWordPoint(window, row, "brave");
            Point to = FindClickableWordPoint(window, row, "world");

            Press(window, from);
            player.PauseCalls.Should().Be(1, "the sidebar must pause on press, before a release or cancellation");
            player.IsPlaying.Should().BeFalse();
            vm.WordPopup.IsOpen.Should().BeFalse("the action belongs to release");
            translator.Calls.Should().BeEmpty();
            shell.Clipboard.Should().BeEmpty();
            MoveDrag(window, to);
            Point release = completeSelection ? to : ToWindow(row, window, new Point(-4, -4));
            window.MouseUp(release, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            player.PauseCalls.Should().Be(1, "release must not pause an already paused player again");
            vm.WordPopup.IsOpen.Should().Be(completeSelection);
            if (completeSelection)
            {
                vm.WordPopup.Word.Should().Be("brave new world");
                translator.Calls.Should().Equal(("brave new world", 0));
            }
            else
                translator.Calls.Should().BeEmpty("an off-text release cancels without an action");
            shell.Clipboard.Should().BeEmpty();
        }
        finally
        {
            window.Close();
            Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        }
    }

    [AvaloniaFact]
    public void MaskedSidebarRow_RejectsPressAndReleaseFromStaleReadableText()
    {
        FakeTranslator translator = new();
        FakeShell shell = new();
        var (window, vm, player) = CreateWindow(sidebar: true,
            preferences: new AppPrefs { SidebarTextMask = true }, translator: translator, shell: shell);
        try
        {
            player.IsOpened = true;
            player.Cues[0].Add(new CueInfo(0, TimeSpan.Zero, TimeSpan.FromSeconds(4), Cue));
            player.RaiseCuesChanged(0);
            player.IsPlaying = true;
            Dispatcher.UIThread.RunJobs();
            window.CaptureRenderedFrame()?.Dispose();
            SubtitleText row = window.GetVisualDescendants().OfType<SubtitleText>()
                .Single(item => item.DataContext is CueItemViewModel { IsMasked: true });
            row.Text.Should().Be(new string('_', Cue.Length));

            // Exercise the host guard even when a control still has readable text from an earlier binding snapshot.
            // Testing only underscore hits would stop in SubtitleText before either host handler could run.
            row.Text = Cue;
            Dispatcher.UIThread.RunJobs();
            window.CaptureRenderedFrame()?.Dispose();
            List<WordClickedEventArgs> events = [];
            row.WordClicked += (_, e) => events.Add(e);
            Point from = FindClickableWordPoint(window, row, "brave");
            Point to = FindClickableWordPoint(window, row, "world");
            Press(window, from);
            player.PauseCalls.Should().Be(0, "masked rows must not affect playback on press");
            MoveDrag(window, to);
            window.MouseUp(to, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            events.Should().ContainSingle("the readable stale control must reach the masked host guard");
            vm.WordPopup.IsOpen.Should().BeFalse();
            player.IsPlaying.Should().BeTrue();
            player.PauseCalls.Should().Be(0);
            translator.Calls.Should().BeEmpty();
            shell.Clipboard.Should().BeEmpty();
        }
        finally
        {
            window.Close();
            Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        }
    }

    [AvaloniaFact]
    public void DragAcrossSoftWrappedLines_PreservesTheSourcePhrase()
    {
        const string wrappedCue = "Hello brave learners carefully choose a new world";
        var (window, _, _, text) = CreateOverlay(wrappedCue, new AppPrefs { SubtitleMaxWidth = 240 });
        try
        {
            List<WordClickedEventArgs> events = [];
            text.WordClicked += (_, e) => events.Add(e);
            Point from = FindClickableWordPoint(window, text, "Hello");
            Point to = FindClickableWordPoint(window, text, "world");
            to.Y.Should().BeGreaterThan(from.Y, "the fixture must actually wrap without hard line breaks");
            Drag(window, from, to);

            events.Should().ContainSingle();
            events[0].Word.Should().Be(wrappedCue);
            events[0].Text.Should().Be(wrappedCue);
            events[0].IsWord.Should().BeFalse();
        }
        finally
        {
            window.Close();
            Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        }
    }

    [AvaloniaTheory]
    [InlineData("Clipboard", "brave new world")]
    [InlineData("ClipboardAll", "Hello brave new world")]
    public void OverlayPhraseRelease_CopiesTheConfiguredPayload(string action, string expected)
    {
        FakeShell shell = new();
        FakeTranslator translator = new();
        var (window, vm, _, text) = CreateOverlay(Cue, translator: translator, shell: shell);
        try
        {
            vm.WordClickAction = action;
            Point from = FindClickableWordPoint(window, text, "brave");
            Point to = FindClickableWordPoint(window, text, "world");
            Press(window, from);
            MoveDrag(window, to);
            shell.Clipboard.Should().BeEmpty("copy must wait for release");
            window.MouseUp(to, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            shell.Clipboard.Should().Equal(expected);
            translator.Calls.Should().BeEmpty("copy actions must not open a provider lookup");
            vm.WordPopup.IsOpen.Should().BeFalse();
        }
        finally
        {
            window.Close();
            Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        }
    }

    [AvaloniaFact]
    public void SecondaryOverlayPhrase_PreservesSlotTranslationAndLanguageMetadata()
    {
        FakeTranslator translator = new();
        var (window, vm, player) = CreateWindow(translator: translator);
        try
        {
            player.IsOpened = true;
            player.SecondaryText = Cue;
            Dispatcher.UIThread.RunJobs();
            window.CaptureRenderedFrame()?.Dispose();
            SubtitleText text = window.FindControl<SubtitleText>("SecondarySubtitle")!;
            text.IsTranslated = true;
            text.SourceLanguage = "fr";
            text.TargetLanguage = "de";
            List<WordClickedEventArgs> events = [];
            text.WordClicked += (_, e) => events.Add(e);
            Point from = FindClickableWordPoint(window, text, "brave");
            Point to = FindClickableWordPoint(window, text, "world");
            Drag(window, from, to);

            events.Should().ContainSingle();
            events[0].Word.Should().Be("brave new world");
            events[0].Slot.Should().Be(1);
            events[0].IsTranslated.Should().BeTrue();
            events[0].SourceLanguage.Should().Be("fr");
            events[0].TargetLanguage.Should().Be("de");
            vm.WordPopup.Slot.Should().Be(1);
            vm.WordPopup.Word.Should().Be("brave new world");
            vm.WordPopup.IsOpen.Should().BeTrue();
            vm.WordPopup.CanSave.Should().BeTrue("the release must retain the language metadata");
            translator.Calls.Should().BeEmpty("already translated secondary text must not be translated again");
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
            window.CaptureRenderedFrame()?.Dispose();
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

    [AvaloniaFact]
    public void DragAcrossHardLineBreak_HighlightsSecondLine()
    {
        var (window, _, _, text) = CreateOverlay("Hello brave\nnew world");
        try
        {
            WriteableBitmap baseline = window.CaptureRenderedFrame()!;
            Point from = FindClickableWordPoint(window, text, "Hello");
            Point to = FindClickableWordPoint(window, text, "new");
            Press(window, from);
            MoveDrag(window, to);
            WriteableBitmap during = window.CaptureRenderedFrame()!;

            Point origin = text.TranslatePoint(default, window)!.Value;
            Rect lower = new(origin.X, origin.Y + text.Bounds.Height / 2, text.Bounds.Width, text.Bounds.Height / 2);
            using ILockedFramebuffer a = baseline.Lock();
            using ILockedFramebuffer b = during.Lock();
            int changed = 0;
            unsafe
            {
                for (int y = Math.Max(0, (int)lower.Top); y < Math.Min(a.Size.Height, (int)lower.Bottom); y++)
                {
                    byte* pa = (byte*)a.Address + y * a.RowBytes;
                    byte* pb = (byte*)b.Address + y * b.RowBytes;
                    for (int x = Math.Max(0, (int)lower.Left); x < Math.Min(a.Size.Width, (int)lower.Right); x++)
                    {
                        int ba = pa[x * 4], ga = pa[x * 4 + 1], ra = pa[x * 4 + 2];
                        int bb = pb[x * 4], gb = pb[x * 4 + 1], rb = pb[x * 4 + 2];
                        if (Math.Abs(ba - bb) > 12 || Math.Abs(ga - gb) > 12 || Math.Abs(ra - rb) > 12)
                            changed++;
                    }
                }
            }
            changed.Should().BeGreaterThan(20, "the second visual line must be highlighted for a cross-line selection");
        }
        finally
        {
            window.Close();
            Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        }
    }
}
