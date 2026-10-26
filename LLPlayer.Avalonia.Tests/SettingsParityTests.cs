using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FlyleafLib;
using FlyleafLib.MediaPlayer.AI;
using LLPlayer.Avalonia.Services;
using LLPlayer.Avalonia.ViewModels;
using LLPlayer.Avalonia.Views;
using LLPlayer.Core.Services;

namespace LLPlayer.Avalonia.Tests;

public class SettingsParityTests
{
    [Theory]
    [InlineData("  Clipboard ", "Clipboard")]
    [InlineData("clipboardall", "ClipboardAll")]
    [InlineData(null, AppPrefs.DefaultWordClickAction)]
    public void Word_click_action_normalizes_to_a_ported_behavior(string? raw, string expected)
    {
        Assert.True(AppPrefs.TryNormalizeWordClickAction(raw, out string normalized));
        Assert.Equal(expected, normalized);
    }

    [Fact]
    public void Unknown_persisted_word_click_action_falls_back_to_translation()
    {
        Assert.False(AppPrefs.TryNormalizeWordClickAction("Bogus", out _));
        AppPrefs prefs = new() { WordClickAction = "Bogus" };
        Assert.Equal(AppPrefs.DefaultWordClickAction, prefs.Normalize().WordClickAction);
    }

    [Theory]
    [InlineData(null, "LLPlayer")]
    [InlineData("", "LLPlayer")]
    [InlineData("  Mining  ", "Mining")]
    [InlineData("bad\tdeck", "LLPlayer")]
    public void Anki_deck_normalizes_blank_and_rejects_control_characters(string? raw, string expected)
    {
        bool ok = AppPrefs.TryNormalizeAnkiDeck(raw, out string normalized);
        Assert.Equal(raw != "bad\tdeck", ok);
        Assert.Equal(expected, normalized);
        AppPrefs prefs = new() { AnkiDeck = raw ?? "" };
        Assert.Equal(expected, prefs.Normalize().AnkiDeck);
    }

    [Fact]
    public void Absent_and_unknown_word_anki_prefs_load_with_safe_defaults()
    {
        using TempDir dir = new();
        string path = Path.Combine(dir.Path, "prefs.json");
        File.WriteAllText(path, "{}");
        AppPrefs empty = new PrefsStore(path).Load();
        Assert.Equal(AppPrefs.DefaultWordClickAction, empty.WordClickAction);
        Assert.Equal(AppPrefs.DefaultAnkiDeck, empty.AnkiDeck);

        File.WriteAllText(path, "{\"WordClickAction\":\"Bogus\",\"AnkiDeck\":\"   \"}");
        AppPrefs unknown = new PrefsStore(path).Load();
        Assert.Equal(AppPrefs.DefaultWordClickAction, unknown.WordClickAction);
        Assert.Equal(AppPrefs.DefaultAnkiDeck, unknown.AnkiDeck);

        File.WriteAllText(path, "{\"AnkiDeck\":\""
            + new string('x', AppPrefs.MaxAnkiDeckLength + 1) + "\"}");
        AppPrefs overlong = new PrefsStore(path).Load();
        Assert.Equal(AppPrefs.DefaultAnkiDeck, overlong.AnkiDeck);
    }

    [Fact]
    public void Word_and_anki_settings_round_trip_through_the_settings_hub_and_apply_live()
    {
        var (main, _, _, prefs, saved) = TestVm.Create();
        using var owned = main;
        SettingsViewModel draft = main.CreateSettings();
        draft.WordClickAction = "ClipboardAll";
        draft.AnkiDeck = "  Mining deck  ";
        draft.SaveCommand.Execute(null);

        Assert.Null(draft.Error);
        AppPrefs committed = Assert.Single(saved);
        Assert.Equal("ClipboardAll", committed.WordClickAction);
        Assert.Equal("Mining deck", committed.AnkiDeck);
        Assert.Equal("ClipboardAll", prefs.WordClickAction);
        Assert.Equal("Mining deck", prefs.AnkiDeck);
        Assert.Equal("ClipboardAll", main.WordClickAction);
        Assert.Equal("Mining deck", main.AnkiDeck);
    }

    [Fact]
    public void Settings_hub_rejects_unknown_word_actions_and_overlong_decks()
    {
        var (main, _, _, _, saved) = TestVm.Create();
        using var owned = main;
        SettingsViewModel invalidAction = main.CreateSettings();
        invalidAction.WordClickAction = "Bogus";
        invalidAction.SaveCommand.Execute(null);
        Assert.NotNull(invalidAction.Error);
        Assert.Empty(saved);

        SettingsViewModel invalidDeck = main.CreateSettings();
        invalidDeck.AnkiDeck = new string('x', AppPrefs.MaxAnkiDeckLength + 1);
        invalidDeck.SaveCommand.Execute(null);
        Assert.NotNull(invalidDeck.Error);
        Assert.Contains("deck", invalidDeck.Error!, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(saved);
    }

    [Theory]
    [InlineData("Clipboard", "word")]
    [InlineData("ClipboardAll", "the whole cue")]
    public void Word_click_action_copies_the_word_or_the_cue_instead_of_opening_the_popup(string action, string expected)
    {
        var (main, player, shell, _, _) = TestVm.Create();
        using var owned = main;
        player.IsPlaying = true;
        main.WordClickAction = action;
        main.OnWordClicked("word", "the whole cue", 0, 0, 0);
        Assert.Equal([expected], shell.Clipboard);
        Assert.False(main.WordPopup.IsOpen);
        Assert.False(player.IsPlaying);
    }

    [Theory]
    [InlineData("Clipboard", "")]
    [InlineData("ClipboardAll", " ")]
    public void Blank_word_click_copy_does_not_clear_the_clipboard(string action, string blank)
    {
        var (main, player, shell, _, _) = TestVm.Create();
        using var owned = main;
        player.IsPlaying = true;
        main.WordClickAction = action;
        main.OnWordClicked(blank, blank, 0, 0, 0);
        Assert.Empty(shell.Clipboard);
        Assert.False(player.IsPlaying);
    }

    [Fact]
    public void Word_click_translation_action_still_opens_the_popup()
    {
        var (main, _, shell, _, _) = TestVm.Create();
        using var owned = main;
        main.WordClickAction = "Translation";
        main.OnWordClicked("word", "cue", 0, 0, 0);
        Assert.Empty(shell.Clipboard);
        Assert.True(main.WordPopup.IsOpen);
    }

    [Fact]
    public async Task Word_manager_passes_the_configured_deck_to_apkg_and_anki_connect_exports()
    {
        using TempLibrary temp = new();
        Assert.True(temp.Library.TryAdd(Word("word")));
        string? packageDeck = null;
        string? sentDeck = null;
        var manager = new WordManagerViewModel(temp.Library,
            (words, deck, token) => { sentDeck = deck; return Task.FromResult(new AnkiAddNotesOutcome(0, 0, null)); },
            (words, deck) => { packageDeck = deck; return [1, 2, 3]; },
            defaultDeck: "Mining");
        manager.PickExportPath = _ => Task.FromResult<string?>(Path.Combine(temp.Dir, "deck.apkg"));

        await manager.Start("apkg");
        Assert.Equal("Mining", packageDeck);
        await manager.Start("anki");
        Assert.Equal("Mining", sentDeck);
    }

    [Fact]
    public async Task Clear_confirmation_disarms_on_delete_export_and_empty_library()
    {
        using TempLibrary temp = new();
        Assert.True(temp.Library.TryAdd(Word("alpha")));
        Assert.True(temp.Library.TryAdd(Word("beta")));
        var manager = new WordManagerViewModel(temp.Library);
        manager.Selected = manager.Words[0];

        manager.ClearCommand.Execute(null);
        Assert.True(manager.ClearConfirming);
        manager.DeleteCommand.Execute(null);
        Assert.False(manager.ClearConfirming);
        Assert.Single(temp.Library.GetAll());

        manager.ClearCommand.Execute(null);
        Assert.True(manager.ClearConfirming);
        manager.PickExportPath = _ => Task.FromResult<string?>(null);
        await manager.Start("apkg");
        Assert.False(manager.ClearConfirming);

        manager.ClearCommand.Execute(null);
        manager.ClearCommand.Execute(null);
        Assert.Empty(temp.Library.GetAll());
        manager.ClearCommand.Execute(null);
        Assert.False(manager.ClearConfirming);
        Assert.Contains("empty", manager.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Word_manager_count_pluralizes_and_distinguishes_a_filtered_list()
    {
        using TempLibrary temp = new();
        Assert.True(temp.Library.TryAdd(Word("alpha")));
        Assert.True(temp.Library.TryAdd(Word("beta")));
        var manager = new WordManagerViewModel(temp.Library);
        Assert.Equal("2 words", manager.CountText);
        manager.Filter = "alpha";
        Assert.Equal("1 of 2 words", manager.CountText);
        manager.Filter = "a";
        Assert.Equal("2 of 2 words", manager.CountText);
        manager.Filter = "";
        Assert.Equal("2 words", manager.CountText);
    }

    [Fact]
    public void Word_manager_uses_the_configured_deck_and_requires_confirmation_to_clear()
    {
        using TempLibrary temp = new();
        Assert.True(temp.Library.TryAdd(Word("first")));
        var manager = new WordManagerViewModel(temp.Library, defaultDeck: "Mining");
        Assert.Equal("Mining", manager.Deck);
        Assert.Equal("1 word", manager.CountText);
        manager.Selected = manager.Words[0];
        manager.Filter = "first";
        Assert.Equal("Word click", manager.SourceText);
        Assert.NotEqual("", manager.SavedText);
        Assert.False(manager.ClearConfirming);

        manager.Filter = "";
        manager.ClearCommand.Execute(null);
        Assert.True(manager.ClearConfirming);
        Assert.Equal("Confirm clear", manager.ClearButtonText);
        Assert.Single(temp.Library.GetAll());
        manager.Filter = "fir";
        Assert.False(manager.ClearConfirming);
        Assert.Single(temp.Library.GetAll());
        manager.ClearCommand.Execute(null);
        manager.ClearCommand.Execute(null);
        Assert.Empty(temp.Library.GetAll());

    }

    [AvaloniaFact]
    public void Hub_word_and_anki_editors_drive_the_committed_prefs()
    {
        var (main, _, _, _, saved) = TestVm.Create();
        using var owned = main;
        SettingsViewModel model = main.CreateSettings();
        var window = new SettingsWindow(model); window.Show(); Dispatcher.UIThread.RunJobs();
        try
        {
            window.FindControl<ListBox>("SettingsNavigation")!.SelectedIndex = 2;
            Dispatcher.UIThread.RunJobs();
            ComboBox click = window.FindControl<ComboBox>("WordClickActionEditor")!;
            TextBox deck = window.FindControl<TextBox>("AnkiDeckEditor")!;
            click.SelectedItem = "ClipboardAll";
            deck.Text = "Hub deck";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("ClipboardAll", model.WordClickAction);
            Assert.Equal("Hub deck", model.AnkiDeck);
            window.FindControl<Button>("SaveButton")!.Command!.Execute(null);
            Dispatcher.UIThread.RunJobs();
            AppPrefs committed = Assert.Single(saved);
            Assert.Equal("ClipboardAll", committed.WordClickAction);
            Assert.Equal("Hub deck", committed.AnkiDeck);
        }
        finally { window.Close(); }
    }

    [Fact]
    public void Saving_settings_does_not_clobber_a_session_deck_typed_in_an_open_manager()
    {
        var (main, _, _, _, _) = TestVm.Create();
        using var owned = main;
        using TempLibrary temp = new();
        main.WordLibrary = temp.Library;
        main.AnkiDeck = "Settings deck";
        main.ShowWordManagerCommand.Execute(null);
        Assert.NotNull(main.WordManager);
        main.WordManager!.Deck = "Session deck";

        SettingsViewModel draft = main.CreateSettings();
        draft.AnkiDeck = "New settings deck";
        draft.SaveCommand.Execute(null);
        Assert.Null(draft.Error);
        Assert.Equal("New settings deck", main.AnkiDeck);
        Assert.Equal("Session deck", main.WordManager.Deck);

        main.WordManagerClosed();
        main.ShowWordManagerCommand.Execute(null);
        Assert.Equal("New settings deck", main.WordManager!.Deck);
    }

    [AvaloniaFact]
    public void Word_manager_window_shows_count_metadata_and_two_step_clear()
    {
        using TempLibrary temp = new();
        Assert.True(temp.Library.TryAdd(Word("first")));
        var manager = new WordManagerViewModel(temp.Library, defaultDeck: "Mining");
        manager.Selected = manager.Words[0];
        var window = new WordManagerWindow(manager); window.Show(); Dispatcher.UIThread.RunJobs();
        try
        {
            List<TextBlock> texts = [.. window.GetLogicalDescendants().OfType<TextBlock>()];
            Assert.Contains(texts, t => t.Text == "1 word");
            Assert.Contains(texts, t => t.Text == "Source: Word click");
        Assert.Contains(texts, t => t.Text == "Source language: en");
            Assert.Contains(texts, t => t.Text != null && t.Text.StartsWith("Saved at: ", StringComparison.Ordinal));
            Button clear = window.FindControl<Button>("ClearWordsButton")!;
            Assert.Equal("Clear all words", clear.Content);
            clear.Command!.Execute(null); Dispatcher.UIThread.RunJobs();
            Assert.Equal("Confirm clear", clear.Content);
            Assert.Single(temp.Library.GetAll());
            clear.Command.Execute(null); Dispatcher.UIThread.RunJobs();
            Assert.Empty(temp.Library.GetAll());
        }
        finally { window.Close(); }
    }

    sealed class TempLibrary : IDisposable
    {
        public string Dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        public WordLibrary Library;
        public TempLibrary() { Directory.CreateDirectory(Dir); Library = new(Path.Combine(Dir, "words.json")); Library.Load(); }
        public void Dispose() => Directory.Delete(Dir, true);
    }
    static SavedWord Word(string term) => new(term, "reading", "translation", "definition", "example", "en", "ru", WordSource.WordClick, "2026-10-26T10:00:00Z");
}
