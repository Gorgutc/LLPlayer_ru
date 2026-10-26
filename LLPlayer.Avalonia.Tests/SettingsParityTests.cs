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
        var (main, _, shell, _, _) = TestVm.Create();
        using var owned = main;
        main.WordClickAction = action;
        main.OnWordClicked("word", "the whole cue", 0, 0, 0);
        Assert.Equal([expected], shell.Clipboard);
        Assert.False(main.WordPopup.IsOpen);
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
    public void Word_manager_uses_the_configured_deck_and_requires_confirmation_to_clear()
    {
        using TempLibrary temp = new();
        Assert.True(temp.Library.TryAdd(Word("first")));
        var manager = new WordManagerViewModel(temp.Library, defaultDeck: "Mining");
        Assert.Equal("Mining", manager.Deck);
        Assert.Equal("1 words", manager.CountText);
        manager.Selected = manager.Words[0];
        Assert.Equal("WordClick", manager.SourceText);
        Assert.NotEqual("", manager.SavedText);
        Assert.False(manager.ClearConfirming);

        manager.ClearCommand.Execute(null);
        Assert.True(manager.ClearConfirming);
        Assert.Equal("Confirm clear", manager.ClearButtonText);
        Assert.Single(temp.Library.GetAll());

        manager.ClearCommand.Execute(null);
        Assert.False(manager.ClearConfirming);
        Assert.Equal("Clear all words", manager.ClearButtonText);
        Assert.Empty(temp.Library.GetAll());
    }

    [AvaloniaFact]
    public void Word_manager_window_shows_count_metadata_and_two_step_clear()
    {
        using TempLibrary temp = new();
        Assert.True(temp.Library.TryAdd(Word("first")));
        var manager = new WordManagerViewModel(temp.Library, defaultDeck: "Mining");
        var window = new WordManagerWindow(manager); window.Show(); Dispatcher.UIThread.RunJobs();
        try
        {
            Assert.Contains(window.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text == "1 words");
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
