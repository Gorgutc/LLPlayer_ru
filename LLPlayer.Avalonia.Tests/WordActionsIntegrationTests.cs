using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FlyleafLib.MediaPlayer.AI;
using LLPlayer.Avalonia.Controls;
using LLPlayer.Avalonia.Services;
using LLPlayer.Avalonia.ViewModels;
using LLPlayer.Avalonia.Views;
using LLPlayer.Core.Services;

namespace LLPlayer.Avalonia.Tests;

public class WordActionsIntegrationTests
{
    static WordSelectionSnapshot Selection(string word, KeyModifiers modifiers = KeyModifiers.None, bool isWord = true)
        => new(word, "Cue " + word, 0, isWord, false, "en", "de", WordSelectionOrigin.Overlay, MouseButton.Left, modifiers);

    [AvaloniaFact]
    public async Task Browser_refusal_reports_safe_status_and_dispose_drains_deferred_failure_without_late_toast()
    {
        var (main, _, shell, _, _) = TestVm.Create();
        shell.BrowserHandler = _ => Task.FromResult(false);
        WordMenuInvocation invocation = main.CreateWordMenu(Selection("first")).First(item => item.Action.Kind == WordMenuActionKind.Search);
        await main.InvokeWordMenuAsync(invocation);
        ToastItem toast = Assert.Single(main.Toasts);
        Assert.Equal("Search unavailable", toast.Title);
        Assert.Equal("The browser could not open this search.", toast.Message);

        TaskCompletionSource<bool> deferred = new();
        shell.BrowserHandler = _ => deferred.Task;
        Task pending = main.InvokeWordMenuAsync(invocation);
        main.TrackBrowserOperation(pending);
        Task drain = main.DrainBrowserOperationsAsync();
        Assert.False(drain.IsCompleted);
        main.Dispose();
        deferred.SetException(new IOException("Synthetic private transport details"));
        await drain;
        Assert.Single(main.Toasts);
        Assert.Equal(2, shell.BrowserRequests.Count);
        Assert.DoesNotContain(main.Toasts, item => item.Message?.Contains("private", StringComparison.Ordinal) == true);
    }

    [AvaloniaFact]
    public async Task Sentence_only_search_keeps_last_word_search_and_disabling_it_selects_a_valid_fallback()
    {
        AppPrefs prefs = new AppPrefs
        {
            WordLastSearchOnSelected = true,
            WordMenuActions =
            [
                new() { Kind = WordMenuActionKind.Search, Title = "First", Url = "https://first.test/?q=%w" },
                new() { Kind = WordMenuActionKind.Search, Title = "Sentence", Url = "https://sentence.test/?q=%s" },
                new() { Kind = WordMenuActionKind.Search, Title = "Chosen", Url = "https://chosen.test/?q=%lw" },
            ],
        }.Normalize();
        var (main, _, shell, _, _) = TestVm.Create(prefs: prefs); using var owned = main;
        IReadOnlyList<WordMenuInvocation> menu = main.CreateWordMenu(Selection("Original"));
        await main.InvokeWordMenuAsync(menu[2]);
        await main.InvokeWordMenuAsync(menu[1]);
        main.OnWordClicked(Selection("NEXT", KeyModifiers.Control), 0, 0);
        await main.DrainBrowserOperationsAsync();
        Assert.Equal(new[] { "https://chosen.test/?q=original", "https://sentence.test/?q=Cue%20Original", "https://chosen.test/?q=next" }, shell.BrowserRequests.Select(uri => uri.AbsoluteUri));
        SettingsViewModel draft = main.CreateSettings(); draft.WordMenuActions[2].IsEnabled = false; draft.SaveCommand.Execute(null);
        main.OnWordClicked(Selection("Fallback", KeyModifiers.Control), 0, 0);
        await main.DrainBrowserOperationsAsync();
        Assert.Equal("https://first.test/?q=Fallback", shell.BrowserRequests.Last().AbsoluteUri);
        int requests = shell.BrowserRequests.Count;
        await main.InvokeWordMenuAsync(new WordMenuInvocation(draft.WordMenuActions[2], Selection("Disabled")));
        Assert.Equal(requests, shell.BrowserRequests.Count);
    }

    [AvaloniaFact]
    public void Word_draft_is_detached_reset_is_not_save_and_success_merges_fresh_unowned_prefs()
    {
        var (main, _, shell, prefs, saved) = TestVm.Create(prefs: new AppPrefs { AnkiDeck = "Existing deck" }.Normalize());
        using var owned = main;
        SettingsViewModel discarded = main.CreateSettings();
        discarded.WordMenuActions[0].Title = "Discarded";
        discarded.WordCopyOnSelected = true;
        discarded.CloseCommand.Execute(null);
        Assert.Equal("Copy", prefs.WordMenuActions![0].Title);
        Assert.False(prefs.WordCopyOnSelected);
        Assert.Empty(saved);

        SettingsViewModel draft = main.CreateSettings();
        draft.WordMenuActions.Clear();
        draft.ResetWordActionsCommand.Execute(null);
        Assert.Empty(saved);
        Assert.Equal("Existing deck", draft.AnkiDeck);
        draft.WordMenuActions[0].Title = "First";
        draft.CloneWordActionCommand.Execute(draft.WordMenuActions[0]);
        draft.WordMenuActions[1].Title = "Independent clone";
        draft.MoveWordActionDownCommand.Execute(draft.WordMenuActions[0]);
        draft.DeleteWordActionCommand.Execute(draft.WordMenuActions.Last());
        draft.WordCopyOnSelected = true;
        draft.WordLastSearchModifier = WordSearchModifier.Alt;
        // A different owner updates the current record after this detached Settings draft was created.
        prefs.Volume = 37; prefs.Mute = true; prefs.LastFolder = "fresh folder"; prefs.RecentFiles = ["fresh media"];
        draft.SaveCommand.Execute(null);

        Assert.Null(draft.Error);
        AppPrefs candidate = Assert.Single(saved);
        Assert.Equal("Existing deck", candidate.AnkiDeck);
        Assert.Equal(37, candidate.Volume); Assert.True(candidate.Mute);
        Assert.Equal("fresh folder", candidate.LastFolder); Assert.Equal(new[] { "fresh media" }, candidate.RecentFiles);
        Assert.Equal("Independent clone", candidate.WordMenuActions![0].Title);
        Assert.Equal("First", candidate.WordMenuActions[1].Title);
        Assert.NotSame(candidate.WordMenuActions[0], candidate.WordMenuActions[1]);
        Assert.True(prefs.WordCopyOnSelected);
        Assert.Equal(WordSearchModifier.Alt, prefs.WordLastSearchModifier);
        Assert.Empty(shell.Clipboard); Assert.Empty(shell.BrowserRequests);
    }

    [AvaloniaFact]
    public void Invalid_word_draft_and_failed_write_leave_live_state_then_retry_commits_once()
    {
        AppPrefs prefs = new AppPrefs { AnkiDeck = "Original" }.Normalize();
        FakeShell shell = new(); FakePlaybackController player = new();
        bool fail = true; List<AppPrefs> writes = [];
        using MainWindowViewModel main = new(player, prefs, candidate =>
        {
            if (fail) throw new IOException("Synthetic write failure");
            writes.Add(candidate);
        }, new FakeTranslator(), shell);
        SettingsViewModel draft = main.CreateSettings();
        draft.WordCopyOnSelected = true; draft.AnkiDeck = "  Next deck  ";
        draft.WordMenuActions[2].Url = "file:///unsafe";
        draft.SaveCommand.Execute(null);
        Assert.NotNull(draft.Error); Assert.Empty(writes); Assert.False(prefs.WordCopyOnSelected); Assert.Equal("Original", prefs.AnkiDeck);
        draft.WordMenuActions[2].Url = "https://example.test/?q=%w";
        draft.SaveCommand.Execute(null);
        Assert.Contains("Synthetic write failure", draft.Error!);
        Assert.Empty(writes); Assert.False(prefs.WordCopyOnSelected); Assert.Equal("Original", main.AnkiDeck);
        Assert.True(draft.WordCopyOnSelected); Assert.Equal("https://example.test/?q=%w", draft.WordMenuActions[2].Url);
        player.PrimaryText = "No auto-copy after failed Save";
        Assert.Empty(shell.Clipboard); Assert.Empty(shell.BrowserRequests);
        fail = false;
        draft.SaveCommand.Execute(null);
        Assert.Null(draft.Error); Assert.Single(writes); Assert.True(prefs.WordCopyOnSelected); Assert.Equal("Next deck", main.AnkiDeck);
    }

    [AvaloniaFact]
    public void Auto_copy_tracks_only_matching_nonempty_text_notifications_and_stops_after_disable_or_dispose()
    {
        var (main, player, shell, _, _) = TestVm.Create();
        player.PrimaryText = "Before enable"; player.SecondaryText = "Other slot";
        SettingsViewModel draft = main.CreateSettings(); draft.SubsAutoTextCopy = true; draft.SubsAutoTextCopyTarget = SubtitleAutoCopyTarget.All;
        draft.SaveCommand.Execute(null);
        Assert.Empty(shell.Clipboard);
        player.SubtitlesVisible = false; player.SubtitlesVisible = true;
        Assert.Empty(shell.Clipboard);
        player.PrimaryText = "First";
        Assert.Equal("First" + Environment.NewLine + "( Other slot )", Assert.Single(shell.Clipboard));
        player.PrimaryText = "";
        Assert.Single(shell.Clipboard);
        player.SecondaryText = "Second";
        Assert.Equal(new[] { "First" + Environment.NewLine + "( Other slot )", "Second" }, shell.Clipboard);
        player.SecondaryText = "Second";
        Assert.Equal(2, shell.Clipboard.Count);
        SettingsViewModel primary = main.CreateSettings(); primary.SubsAutoTextCopyTarget = SubtitleAutoCopyTarget.Primary; primary.SaveCommand.Execute(null);
        player.SecondaryText = "Ignored secondary"; Assert.Equal(2, shell.Clipboard.Count);
        player.PrimaryText = "Third"; Assert.Equal("Third", shell.Clipboard.Last());
        SettingsViewModel disable = main.CreateSettings(); disable.SubsAutoTextCopy = false; disable.SaveCommand.Execute(null);
        player.PrimaryText = "Disabled"; Assert.Equal(3, shell.Clipboard.Count);
        main.Dispose(); player.PrimaryText = "Disposed"; Assert.Equal(3, shell.Clipboard.Count);
    }

    [AvaloniaFact]
    public async Task Deferred_lookup_A_and_menu_B_keep_their_own_snapshots_after_popup_C()
    {
        TaskCompletionSource<WordTranslationResult> held = new();
        FakeTranslator translator = new((word, _, _) => word == "A" ? held.Task : Task.FromResult(new WordTranslationResult(WordTranslationStatus.Translated, word)));
        var (main, _, shell, _, _) = TestVm.Create(translator: translator);
        using var owned = main;
        string dir = Path.Combine(Path.GetTempPath(), "word-menu-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            WordLibrary library = new(Path.Combine(dir, "words.json")); library.Load(); main.WordPopup.Library = library;
            main.OnWordClicked(Selection("A"), 0, 0);
            IReadOnlyList<WordMenuInvocation> menu = main.CreateWordMenu(Selection("B") with { Button = MouseButton.Right });
            Assert.Equal("A", main.WordPopup.Word); Assert.Empty(shell.Clipboard); Assert.Single(translator.Calls);
            held.SetResult(new(WordTranslationStatus.Translated, "Translated A"));
            await Task.Yield(); Dispatcher.UIThread.RunJobs();
            Assert.True(main.WordPopup.CanSave);
            main.WordPopup.SaveCommand.Execute(null);
            SavedWord saved = Assert.Single(library.GetAll()); Assert.Equal("A", saved.Term); Assert.Equal("Cue A", saved.Example);
            main.OnWordClicked(Selection("C"), 0, 0);
            await main.InvokeWordMenuAsync(menu.First(item => item.Action.Kind == WordMenuActionKind.Clipboard));
            Assert.Equal(new[] { "B" }, shell.Clipboard);
            await main.InvokeWordMenuAsync(menu.First(item => item.Action.Kind == WordMenuActionKind.Search));
            Assert.Equal("https://www.google.com/search?q=B", Assert.Single(shell.BrowserRequests).AbsoluteUri);
            Assert.Equal("C", main.WordPopup.Word);
        }
        finally { Directory.Delete(dir, true); }
    }

    [AvaloniaTheory]
    [InlineData(KeyModifiers.Control, 1)]
    [InlineData(KeyModifiers.Control | KeyModifiers.Shift, 0)]
    [InlineData(KeyModifiers.Control | KeyModifiers.Alt, 0)]
    [InlineData(KeyModifiers.Meta, 0)]
    [InlineData(KeyModifiers.None, 0)]
    public async Task Accepted_selection_requires_exact_modifier_and_direct_copy_never_duplicates(KeyModifiers modifiers, int searches)
    {
        AppPrefs prefs = new AppPrefs { WordCopyOnSelected = true, WordLastSearchOnSelected = true }.Normalize();
        var (main, _, shell, _, _) = TestVm.Create(prefs: prefs); using var owned = main;
        main.OnWordClicked(Selection("selected", modifiers, isWord: false), 0, 0);
        await main.DrainBrowserOperationsAsync();
        Assert.Equal(new[] { "selected" }, shell.Clipboard); Assert.Equal(searches, shell.BrowserRequests.Count); Assert.True(main.WordPopup.IsOpen);
        SettingsViewModel draft = main.CreateSettings(); draft.WordClickAction = "Clipboard"; draft.SaveCommand.Execute(null);
        shell.Clipboard.Clear(); shell.BrowserRequests.Clear(); main.WordPopup.Close();
        main.OnWordClicked(Selection("direct", KeyModifiers.Control), 0, 0);
        Assert.Equal(new[] { "direct" }, shell.Clipboard); Assert.Empty(shell.BrowserRequests); Assert.False(main.WordPopup.IsOpen);
    }

    [AvaloniaTheory]
    [InlineData("Dark")]
    [InlineData("Light")]
    public void Real_editor_commands_are_reachable_at_minimum_size_and_save_ordered_rows(string theme)
    {
        ThemeService.Apply(theme);
        var (main, _, _, prefs, saved) = TestVm.Create(); using var owned = main;
        SettingsViewModel draft = main.CreateSettings(); SettingsWindow window = new(draft) { Width = 560, Height = 440 };
        window.Show();
        try
        {
            window.FindControl<ListBox>("SettingsNavigation")!.SelectedIndex = 2; Dispatcher.UIThread.RunJobs();
            Button add = window.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Add copy"));
            Click(window, add); Assert.Equal(6, draft.WordMenuActions.Count);
            ItemsControl rows = window.FindControl<ItemsControl>("WordMenuActionsEditor")!;
            Control last = rows.GetRealizedContainers().Last();
            TextBox title = last.GetVisualDescendants().OfType<TextBox>().First(); title.Text = "Custom copy";
            Dispatcher.UIThread.RunJobs();
            Click(window, last.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Clone")));
            Assert.Equal(7, draft.WordMenuActions.Count); Assert.NotSame(draft.WordMenuActions[5], draft.WordMenuActions[6]);
            last = rows.GetRealizedContainers().Last();
            Click(window, last.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Up")));
            Assert.Equal("Custom copy", draft.WordMenuActions[5].Title);
            CheckBox copy = window.FindControl<CheckBox>("WordCopyOnSelectedEditor")!; copy.IsChecked = true;
            Button save = window.FindControl<Button>("SaveButton")!;
            save.BringIntoView(); Dispatcher.UIThread.RunJobs(); Assert.True(save.Focus());
            AssertContained(save, window);
            AssertContained(window.FindControl<Button>("CloseButton")!, window);
            foreach (string name in new[] { "WordCopyOnSelectedEditor", "WordLastSearchOnSelectedEditor", "WordSearchModifierEditor", "SubsAutoTextCopyEditor", "SubsAutoTextCopyTargetEditor", "ResetWordActionsButton" })
            {
                Control editor = window.FindControl<Control>(name)!;
                editor.BringIntoView(); Dispatcher.UIThread.RunJobs();
                AssertContained(editor, window); AssertInsideScroll(editor);
                Assert.True(editor.Focus(), name + " must be reachable by keyboard");
                AssertContained(save, window); AssertContained(window.FindControl<Button>("CloseButton")!, window);
            }
            string? frames = Environment.GetEnvironmentVariable("LLPLAYER_SETTINGS_ARTIFACTS");
            if (frames != null)
            {
                Directory.CreateDirectory(frames);
                window.CaptureRenderedFrame()!.Save(Path.Combine(frames, "word-settings-" + theme.ToLowerInvariant() + "-minimum.png"), PngBitmapEncoderOptions.Default);
            }
            Click(window, save);
            Assert.False(window.IsVisible, draft.Error); Assert.Single(saved); Assert.True(prefs.WordCopyOnSelected);
            Assert.Equal("Custom copy", prefs.WordMenuActions![5].Title);
            Assert.Equal("Custom copy", prefs.WordMenuActions[6].Title);
        }
        finally { window.Close(); ThemeService.Apply("Dark"); }
    }

    static void Click(Window window, Control control)
    {
        control.BringIntoView(); Dispatcher.UIThread.RunJobs();
        AssertContained(control, window); AssertInsideScroll(control);
        Point point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Dispatcher.UIThread.RunJobs();
    }

    static void AssertContained(Control control, Window window)
    {
        Assert.True(control.IsEffectivelyVisible); Assert.True(control.Bounds.Width > 0 && control.Bounds.Height > 0);
        Point point = control.TranslatePoint(default, window)!.Value;
        Assert.True(point.X >= -1 && point.Y >= -1 && point.X + control.Bounds.Width <= window.Bounds.Width + 1
            && point.Y + control.Bounds.Height <= window.Bounds.Height + 1, $"{control.Name ?? control.GetType().Name} clipped at {point} / {control.Bounds.Size}");
    }
    static void AssertInsideScroll(Control control)
    {
        if (control.FindAncestorOfType<ScrollViewer>() is not { } scroll) return;
        Point point = control.TranslatePoint(default, scroll)!.Value;
        Assert.True(point.Y >= -1 && point.Y + control.Bounds.Height <= scroll.Viewport.Height + 1,
            $"{control.Name ?? control.GetType().Name} clipped by scroll viewport at {point}");
    }
}
