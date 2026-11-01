using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using LLPlayer.Avalonia.Controls;
using LLPlayer.Avalonia.Services;
using LLPlayer.Avalonia.ViewModels;
using LLPlayer.Avalonia.Views;

namespace LLPlayer.Avalonia.Tests;

public class WordActionsSettingsTests
{
    [AvaloniaFact]
    public void Translation_selection_copies_once_only_when_committed_opt_in_is_enabled()
    {
        // Removing the committed opt-in copy branch must fail this assertion, while lookup still opens.
        AppPrefs prefs = Load("{\"WordCopyOnSelected\":true}");
        var (main, _, shell, _, _) = TestVm.Create(prefs: prefs);
        using var owned = main;

        main.OnWordClicked("brave", "Hello brave new world", 0, 20, 30);

        Assert.Equal(new[] { "brave" }, shell.Clipboard);
        Assert.True(main.WordPopup.IsOpen);
        Assert.Equal("brave", main.WordPopup.Word);
    }

    [AvaloniaFact]
    public void Enabled_primary_auto_copy_observes_new_text_without_copying_on_construction()
    {
        // A missing text-change hook fails; subscribing at construction must not copy existing/empty text.
        AppPrefs prefs = Load("{\"SubsAutoTextCopy\":true,\"SubsAutoTextCopyTarget\":\"Primary\"}");
        var (main, player, shell, _, _) = TestVm.Create(prefs: prefs);
        using var owned = main;
        Assert.Empty(shell.Clipboard);

        player.PrimaryText = "A fresh subtitle";
        player.PrimaryText = "A fresh subtitle";

        Assert.Equal(new[] { "A fresh subtitle" }, shell.Clipboard);
    }

    [AvaloniaFact]
    public void Control_word_click_searches_once_and_retains_normal_translation_popup()
    {
        // Losing release modifiers or last-search routing produces no browser request.
        AppPrefs prefs = Load("{\"WordLastSearchOnSelected\":true,\"WordLastSearchModifier\":\"Control\"}");
        RecordingShell shell = new();
        FakePlaybackController player = new();
        using MainWindowViewModel main = new(player, prefs, _ => { }, new FakeTranslator(), shell);
        MainWindow window = new(new KeyMapper(), null) { Width = 1000, Height = 600 };
        window.Attach(main, null);
        window.Show();
        try
        {
            player.IsOpened = true;
            player.PrimaryText = "Hello brave new world";
            Dispatcher.UIThread.RunJobs();
            window.CaptureRenderedFrame()?.Dispose();
            Dispatcher.UIThread.RunJobs();
            SubtitleText text = window.FindControl<SubtitleText>("PrimarySubtitle")!;
            Point point = FindWordPoint(window, text, "brave");
            window.MouseMove(point);
            window.MouseDown(point, MouseButton.Left, RawInputModifiers.LeftMouseButton | RawInputModifiers.Control);
            window.MouseUp(point, MouseButton.Left, RawInputModifiers.Control);
            Dispatcher.UIThread.RunJobs();

            Uri opened = Assert.Single(shell.BrowserRequests);
            Assert.Equal("https://www.google.com/search?q=brave", opened.AbsoluteUri);
            Assert.True(main.WordPopup.IsOpen);
            Assert.Equal("brave", main.WordPopup.Word);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Tools_exposes_reachable_word_menu_editor_at_minimum_size()
    {
        // Missing editor wiring prevents changing menu order/titles from the existing Settings hub.
        var (main, _, _, _, _) = TestVm.Create();
        using var owned = main;
        SettingsWindow window = new(main.CreateSettings()) { Width = 560, Height = 440 };
        window.Show();
        try
        {
            window.FindControl<ListBox>("SettingsNavigation")!.SelectedIndex = 2;
            Dispatcher.UIThread.RunJobs();
            Control? editor = window.FindControl<Control>("WordMenuActionsEditor");
            Assert.NotNull(editor);
            editor.BringIntoView();
            Dispatcher.UIThread.RunJobs();
            Assert.True(editor.IsEffectivelyVisible);
            Assert.True(editor.Bounds.Width > 0 && editor.Bounds.Height > 0);
            Assert.True(window.FindControl<Button>("SaveButton")!.IsEffectivelyVisible);
            Assert.True(window.FindControl<Button>("CloseButton")!.IsEffectivelyVisible);
        }
        finally { window.Close(); }
    }

    static AppPrefs Load(string json)
        => JsonSerializer.Deserialize<AppPrefs>(json, PrefsStore.JsonOptions)!.Normalize();

    static Point FindWordPoint(Window window, SubtitleText text, string word)
    {
        for (double y = 1; y < text.Bounds.Height - 1; y++)
        for (double x = 1; x < text.Bounds.Width - 1; x++)
        {
            Point local = new(x, y);
            if (!text.TryGetWordAt(local, out string found) || found != word) continue;
            Point? point = text.TranslatePoint(local, window);
            if (point is { } hit && ReferenceEquals(window.InputHitTest(hit), text)) return hit;
        }
        throw new Xunit.Sdk.XunitException($"A hit-tested pixel for '{word}' must exist inside {text.Bounds}.");
    }

    // The browser is an external effect. The extra public method compiles before the interface grows.
    sealed class RecordingShell : IAppShell
    {
        public bool IsFullScreen { get; set; }
        public List<Uri> BrowserRequests { get; } = [];
        public Task<bool> OpenBrowserAsync(Uri uri) { BrowserRequests.Add(uri); return Task.FromResult(true); }
        public Task<string?> PickMediaFileAsync(string? initialFolder) => Task.FromResult<string?>(null);
        public Task<string?> PickSubtitlesFileAsync(string? initialFolder) => Task.FromResult<string?>(null);
        public void ShowCheatSheet() { }
        public void SetClipboardText(string text) { }
        public void ApplyTheme(string theme) { }
        public void Exit() { }
    }
}
