using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LLPlayer.Avalonia.Controls;
using LLPlayer.Avalonia.Services;
using LLPlayer.Avalonia.ViewModels;
using LLPlayer.Avalonia.Views;

namespace LLPlayer.Avalonia.Tests;

/// <summary>Cross-block parity requirements exercised first against the pre-implementation application.</summary>
public class TripleParityBaselineTests
{
    [AvaloniaFact]
    public void Settings_hub_exposes_the_playback_settings_dialog_entry()
    {
        using var main = new MainWindowViewModel(new FakePlaybackController(), new AppPrefs(), _ => { }, new FakeTranslator(), new FakeShell());
        var window = new SettingsWindow(main.CreateSettings());
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var links = window.GetVisualDescendants().OfType<Button>()
                .Where(button => (button.Content as string)?.Contains("Playback", StringComparison.OrdinalIgnoreCase) == true);
            Button link = Assert.Single(links);
            Assert.NotNull(link.Command);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Middle_click_selects_the_entire_cue_once_instead_of_ignoring_the_pointer()
    {
        const string cue = "Hello brave new world";
        var text = new SubtitleText { Text = cue, FontSize = 30, Slot = 1 };
        var window = new Window { Width = 600, Height = 150, Content = text };
        List<WordClickedEventArgs> selections = [];
        text.WordClicked += (_, selected) => selections.Add(selected);
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs(); window.CaptureRenderedFrame()?.Dispose(); Dispatcher.UIThread.RunJobs();
            Point point = FindWord(window, text, "brave");
            window.MouseMove(point);
            window.MouseDown(point, MouseButton.Middle, RawInputModifiers.MiddleMouseButton);
            window.MouseUp(point, MouseButton.Middle);
            Dispatcher.UIThread.RunJobs();
            WordClickedEventArgs selected = Assert.Single(selections);
            Assert.Equal(cue, selected.Word);
            Assert.Equal(cue, selected.Text);
            Assert.False(selected.IsWord);
            Assert.Equal(MouseButton.Middle, selected.Button);
            Assert.Equal(1, selected.Slot);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Bitmap_cues_have_a_sidebar_snapshot_marker_and_preview_control()
    {
        // Use reflection for the additive snapshot seam so this test compiles before that seam exists.
        var marker = typeof(CueInfo).GetProperty("IsBitmap");
        Assert.NotNull(marker);
        Assert.Equal(typeof(bool), marker.PropertyType);
        CueInfo cue = new(0, TimeSpan.Zero, TimeSpan.FromSeconds(2), "");
        marker.SetValue(cue, true);
        FakePlaybackController player = new() { IsOpened = true };
        player.Cues[0].Add(cue);
        using var main = new MainWindowViewModel(player, new AppPrefs { SidebarVisible = true }, _ => { }, new FakeTranslator(), new FakeShell());
        var window = new MainWindow(new KeyMapper(), null) { Width = 1000, Height = 600 };
        window.Attach(main, null); window.Show();
        try
        {
            main.Sidebar.Reload(); Dispatcher.UIThread.RunJobs(); window.CaptureRenderedFrame()?.Dispose(); Dispatcher.UIThread.RunJobs();
            CueItemViewModel row = Assert.Single(main.Sidebar.Cues);
            Assert.Equal(true, marker.GetValue(row.Cue));
            Assert.NotNull(typeof(MainWindow).Assembly.GetType("LLPlayer.Avalonia.Controls.BitmapCuePreview"));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Main_window_exposes_playlist_navigation()
    {
        using var main = new MainWindowViewModel(new FakePlaybackController(), new AppPrefs(), _ => { }, new FakeTranslator(), new FakeShell());
        var window = new MainWindow(new KeyMapper(), null) { Width = 1000, Height = 600 };
        window.Attach(main, null); window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            Menu menu = window.FindControl<Menu>("MainMenu")!;
            Assert.Contains(MenuItems(menu.Items), item => (item.Header as string)?.Contains("Playlist", StringComparison.OrdinalIgnoreCase) == true);
        }
        finally { window.Close(); }
    }

    [Fact]
    public void Online_extractor_extra_preferences_survive_an_explicit_settings_save()
    {
        using var temporary = new TempDir();
        string path = Path.Combine(temporary.Path, "online.json");
        File.WriteAllText(path, "{\"YtDlpPath\":\"\",\"DenoPath\":\"\",\"ExtraArguments\":\"--cookies-from-browser firefox\"}");
        var store = new OnlineVideoSettingsStore(path);
        OnlineVideoSettings settings = store.Load();
        Assert.Null(store.LoadWarning);
        store.Save(settings);
        using JsonDocument saved = JsonDocument.Parse(File.ReadAllText(path));
        Assert.True(saved.RootElement.TryGetProperty("ExtraArguments", out JsonElement arguments), "Saved online settings must preserve the configured extractor arguments.");
        Assert.Equal("--cookies-from-browser firefox", arguments.GetString());
    }

    static IEnumerable<MenuItem> MenuItems(IEnumerable<object?> items)
    {
        foreach (MenuItem item in items.OfType<MenuItem>())
        {
            yield return item;
            foreach (MenuItem child in MenuItems(item.Items)) yield return child;
        }
    }

    static Point FindWord(Window window, SubtitleText text, string word)
    {
        for (double y = 1; y < text.Bounds.Height - 1; y++)
        for (double x = 1; x < text.Bounds.Width - 1; x++)
        {
            Point local = new(x, y);
            if (!text.TryGetWordAt(local, out string found) || found != word) continue;
            if (text.TranslatePoint(local, window) is { } hit && ReferenceEquals(window.InputHitTest(hit), text)) return hit;
        }
        throw new Xunit.Sdk.XunitException("A hit-tested glyph pixel must exist for " + word);
    }
}
