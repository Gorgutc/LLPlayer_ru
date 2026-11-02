using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FlyleafLib.MediaPlayer;
using LLPlayer.Avalonia.Controls;
using LLPlayer.Avalonia.Services;
using LLPlayer.Avalonia.ViewModels;
using LLPlayer.Avalonia.Views;

namespace LLPlayer.Avalonia.Tests;

public class TripleParityIntegrationTests
{
    [AvaloniaFact]
    public void Lowering_the_engine_volume_maximum_preserves_mute_and_remembers_the_clamped_volume()
    {
        AppPrefs prefs = new() { Volume = 220, Mute = true };
        VolumePlayer player = new(); player.ChangeMaximum(300);
        List<(int Volume, bool Muted)> writes = [];
        using var main = new MainWindowViewModel(player, prefs, saved => writes.Add((saved.Volume, saved.Mute)), new FakeTranslator(), new FakeShell());
        var window = new MainWindow(new KeyMapper(), null) { Width = 1000, Height = 600 };
        window.Attach(main, null); window.Show(); Dispatcher.UIThread.RunJobs();
        try
        {
            Slider slider = window.FindControl<Slider>("VolumeSlider")!;
            Assert.Equal(300, slider.Maximum); Assert.Equal(220, slider.Value); Assert.True(player.Mute);

            // The adapter has already clamped native state when its first VolumeMax notification arrives.
            // Do not issue an earlier Volume notification that would pre-sync the bound slider.
            player.SetEngineVolumeSnapshot(100);
            Assert.Equal(100, player.Volume); Assert.True(player.Mute); Assert.Equal(220, main.Volume);
            player.ChangeMaximum(100); Dispatcher.UIThread.RunJobs();

            Assert.Equal(100, slider.Maximum); Assert.Equal(100, slider.Value);
            Assert.True(player.Mute); Assert.True(main.IsMuted);
            main.SavePrefs();
            Assert.Equal((100, true), Assert.Single(writes));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Engine_volume_and_maximum_notifications_refresh_the_slider_and_the_next_preferences_save()
    {
        AppPrefs prefs = new() { Volume = 25 };
        VolumePlayer player = new();
        List<(int Volume, bool Muted)> writes = [];
        using var main = new MainWindowViewModel(player, prefs, saved => writes.Add((saved.Volume, saved.Mute)), new FakeTranslator(), new FakeShell());
        var window = new MainWindow(new KeyMapper(), null) { Width = 1000, Height = 600 };
        window.Attach(main, null); window.Show(); Dispatcher.UIThread.RunJobs();
        try
        {
            player.ChangeMaximum(300); player.Volume = 220; player.Mute = true;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(300, window.FindControl<Slider>("VolumeSlider")!.Maximum);
            Assert.Equal(220, main.Volume); Assert.True(main.IsMuted);
            main.SavePrefs();
            Assert.Equal((220, true), Assert.Single(writes));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Real_owner_close_cancels_and_drains_the_subtitle_provider_before_player_shutdown()
    {
        BlockingSubtitleClient client = new();
        var download = new OpenSubtitlesDownloadViewModel(client, "Known media title")
        { ApiKey = "test-key", Username = "test-user", Password = "test-password" };
        var window = new MainWindow(new KeyMapper(), null) { Width = 1000, Height = 600 };
        using var main = new MainWindowViewModel(new FakePlaybackController(), new AppPrefs(), _ => { }, new FakeTranslator(), window);
        int shutdowns = 0;
        window.ShutdownRequested = () =>
        {
            Assert.True(client.Drained); Assert.True(client.Disposed); Assert.False(download.Busy);
            Assert.Equal("", download.Password); Assert.Empty(download.Results);
            shutdowns++; main.Dispose(); return Task.CompletedTask;
        };
        window.Attach(main, null); window.Show(); window.ShowSubtitleDownload(download); Dispatcher.UIThread.RunJobs();
        Task search = download.SearchAsync();
        try
        {
            await client.Started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            window.Close(); await Task.Yield(); Dispatcher.UIThread.RunJobs();
            await client.Canceled.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.Equal(0, shutdowns); Assert.True(window.IsVisible); Assert.False(client.Disposed);

            client.Release.TrySetResult();
            await search.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            await download.DisposeAsync(); await Task.Yield(); Dispatcher.UIThread.RunJobs();
            // Avalonia asks an active owned modal to close first; its drain veto leaves the owner available to retry.
            window.Close(); await Task.Yield(); Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, shutdowns); Assert.False(window.IsVisible);
        }
        finally
        {
            client.Release.TrySetResult();
            await download.DisposeAsync(); window.Close();
        }
    }

    [AvaloniaFact]
    public void Reopened_playlist_keeps_receiving_changes_from_the_main_owned_view_model()
    {
        PlaylistPlayer player = new() { IsOpened = true };
        player.PlaylistItems.Add(new PlaybackPlaylistItem(0, "First item", true, new object()));
        var window = new MainWindow(new KeyMapper(), null) { Width = 1000, Height = 600 };
        using var main = new MainWindowViewModel(player, new AppPrefs(), _ => { }, new FakeTranslator(), window);
        window.Attach(main, null); window.Show(); Dispatcher.UIThread.RunJobs();
        try
        {
            main.ShowPlaylistCommand.Execute(null); Dispatcher.UIThread.RunJobs();
            var firstWindow = Assert.IsType<PlaylistWindow>(Assert.Single(window.OwnedWindows));
            Assert.Single(main.Playlist.Items);
            firstWindow.Close(); Dispatcher.UIThread.RunJobs();

            player.PlaylistItems.Add(new PlaybackPlaylistItem(1, "Newly resolved item", false, new object()));
            player.NotifyPlaylistChanged(); Dispatcher.UIThread.RunJobs();
            main.ShowPlaylistCommand.Execute(null); Dispatcher.UIThread.RunJobs();
            var reopened = Assert.IsType<PlaylistWindow>(Assert.Single(window.OwnedWindows));
            Assert.NotSame(firstWindow, reopened); Assert.Same(main.Playlist, reopened.DataContext);
            Assert.Equal(["First item", "Newly resolved item"], main.Playlist.Items.Select(item => item.Title));
            Assert.Equal(2, reopened.FindControl<ListBox>("PlaylistItemsList")!.Items.Count);
            reopened.Close(); Dispatcher.UIThread.RunJobs();
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Both_real_video_subtitle_overlays_receive_source_and_translated_target_languages()
    {
        LanguagePlayer player = new() { IsOpened = true, PrimaryText = "Japanese source", SecondaryText = "Chinese source" };
        var window = new MainWindow(new KeyMapper(), null) { Width = 1000, Height = 600 };
        using var main = new MainWindowViewModel(player, new AppPrefs(), _ => { }, new FakeTranslator(), window);
        window.Attach(main, null); window.Show(); Dispatcher.UIThread.RunJobs();
        try
        {
            SubtitleText primary = window.FindControl<SubtitleText>("PrimarySubtitle")!;
            SubtitleText secondary = window.FindControl<SubtitleText>("SecondarySubtitle")!;
            Assert.Equal("ja", primary.SourceLanguage); Assert.Equal("zh", secondary.SourceLanguage);
            Assert.Equal("en", primary.TargetLanguage); Assert.Equal("en", secondary.TargetLanguage);
            Assert.True(primary.IsTranslated); Assert.True(secondary.IsTranslated);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Main_playlist_menu_opens_the_shared_snapshot_and_next_button_routes_its_exact_token()
    {
        PlaylistPlayer player = new() { IsOpened = true };
        object firstToken = new(), secondToken = new();
        player.PlaylistItems.AddRange([
            new PlaybackPlaylistItem(0, "First playable item", true, firstToken),
            new PlaybackPlaylistItem(2, "Third source item", false, secondToken),
        ]);
        var window = new MainWindow(new KeyMapper(), null) { Width = 1000, Height = 600 };
        using var main = new MainWindowViewModel(player, new AppPrefs(), _ => { }, new FakeTranslator(), window);
        window.Attach(main, null); window.Show(); Dispatcher.UIThread.RunJobs();
        try
        {
            Menu menu = window.FindControl<Menu>("MainMenu")!;
            MenuItem entry = Assert.Single(MenuItems(menu.Items), item => ReferenceEquals(item.Command, main.ShowPlaylistCommand));
            entry.Command!.Execute(entry.CommandParameter); Dispatcher.UIThread.RunJobs();
            var playlist = Assert.IsType<PlaylistWindow>(Assert.Single(window.OwnedWindows));
            Assert.Same(window, playlist.Owner); Assert.Same(main.Playlist, playlist.DataContext);
            Assert.Same(main.Playlist.Items, playlist.FindControl<ListBox>("PlaylistItemsList")!.ItemsSource);
            Assert.Contains("2 playable", main.Playlist.Summary); Assert.Contains("3 expected", main.Playlist.Summary);

            Click(playlist, playlist.FindControl<Button>("NextPlaylistButton")!);
            await Task.Yield(); Dispatcher.UIThread.RunJobs();
            Assert.Same(secondToken, Assert.Single(player.Selections).Token);
            Assert.Equal(2, main.Playlist.Items.Single(item => item.IsCurrent).Index);
            Assert.Empty(player.Seeks); Assert.Empty(player.SubtitleSeeks);

            main.ShowPlaylistCommand.Execute(null); Dispatcher.UIThread.RunJobs();
            Assert.Same(playlist, Assert.Single(window.OwnedWindows));
            playlist.Close(); Dispatcher.UIThread.RunJobs();
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Playback_link_discards_the_hub_draft_and_preserves_fresh_transport_word_and_recent_values()
    {
        AppPrefs prefs = new(); int appWrites = 0, playbackWrites = 0;
        var player = new FakePlaybackController();
        var window = new MainWindow(new KeyMapper(), null) { Width = 1000, Height = 600 };
        using var main = new MainWindowViewModel(player, prefs, _ => appWrites++, new FakeTranslator(), window);
        main.PlaybackSettingsDialog = () => new PlaybackSettingsViewModel(new(), new(), (_, _, _, _) =>
        { playbackWrites++; return Task.CompletedTask; });
        window.Attach(main, null); window.Show(); Dispatcher.UIThread.RunJobs();
        try
        {
            main.ShowSettingsCommand.Execute(null); Dispatcher.UIThread.RunJobs();
            var hub = Assert.IsType<SettingsWindow>(Assert.Single(window.OwnedWindows));
            var draft = Assert.IsType<SettingsViewModel>(hub.DataContext);
            draft.Theme = "Light"; draft.SubtitleFontSize = 80; draft.SidebarTextMask = true;
            prefs.Volume = 84; prefs.Mute = true; prefs.RecentFiles.Add("fresh-media.mkv");
            prefs.WordClickAction = "ClipboardAll";
            Button link = hub.FindControl<Button>("PlaybackSettingsLink")!;
            Assert.NotNull(link);
            ListBox navigation = hub.FindControl<ListBox>("SettingsNavigation")!;
            for (int section = 0; section < navigation.Items.Count && !link.IsEffectivelyVisible; section++)
            { navigation.SelectedIndex = section; Dispatcher.UIThread.RunJobs(); }
            Assert.True(link.IsEffectivelyVisible); link.BringIntoView(); Dispatcher.UIThread.RunJobs();
            Click(hub, link); await Task.Yield(); Dispatcher.UIThread.RunJobs();

            Assert.False(hub.IsVisible);
            var playback = Assert.IsType<PlaybackSettingsWindow>(Assert.Single(window.OwnedWindows));
            Assert.Same(window, playback.Owner); Assert.True(playback.IsDialog);
            Assert.Equal(0, appWrites); Assert.Equal(0, playbackWrites);
            Assert.Equal("Dark", prefs.Theme); Assert.Equal(34, prefs.SubtitleFontSize); Assert.False(prefs.SidebarTextMask);
            Assert.Equal(84, prefs.Volume); Assert.True(prefs.Mute);
            Assert.Equal(["fresh-media.mkv"], prefs.RecentFiles); Assert.Equal("ClipboardAll", prefs.WordClickAction);

            main.OpenSettingsTool(SettingsLink.Playback); Dispatcher.UIThread.RunJobs();
            Assert.Same(playback, Assert.Single(window.OwnedWindows));
            playback.Close(); await Task.Yield(); Dispatcher.UIThread.RunJobs();
            main.ShowSettingsCommand.Execute(null); Dispatcher.UIThread.RunJobs();
            var reopened = Assert.IsType<SettingsWindow>(Assert.Single(window.OwnedWindows));
            var fresh = Assert.IsType<SettingsViewModel>(reopened.DataContext);
            Assert.Equal("Dark", fresh.Theme); Assert.Equal(34, fresh.SubtitleFontSize); Assert.False(fresh.SidebarTextMask);
            reopened.Close(); await Task.Yield(); Dispatcher.UIThread.RunJobs();
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Secondary_bitmap_sidebar_preview_is_bound_to_its_cue_and_masks_future_pixels_before_conversion()
    {
        PreviewPlayer player = new() { IsOpened = true, SecondarySlotEnabled = true };
        player.Cues[1].Add(new CueInfo(0, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), "", IsBitmap: true));
        AppPrefs prefs = new() { SidebarVisible = true, SidebarShowSecondary = true, SidebarTextMask = true };
        using var main = new MainWindowViewModel(player, prefs, _ => { }, new FakeTranslator(), new FakeShell());
        var window = new MainWindow(new KeyMapper(), null) { Width = 1000, Height = 600 };
        window.Attach(main, null); window.Show();
        try
        {
            Pump(window);
            CueItemViewModel row = Assert.Single(main.Sidebar.Cues);
            Assert.Equal(1, row.Slot); Assert.True(row.IsMasked); Assert.False(row.ShowBitmap);
            Assert.Empty(player.PreviewRequests);

            player.SecondaryCueIndex = 0;
            Pump(window);
            ListBox list = window.FindControl<ListBox>("CueList")!;
            BitmapCuePreview preview = Assert.Single(list.GetVisualDescendants().OfType<BitmapCuePreview>());
            await preview.Pending; Pump(window);
            Assert.Same(row, preview.Row); Assert.Same(player, preview.Player);
            Assert.True(row.ShowBitmap); Assert.True(preview.HasPreview);
            Assert.True(preview.Bounds.Width > 0 && preview.Bounds.Height > 0);
            var requested = Assert.Single(player.PreviewRequests);
            Assert.Same(row.Cue, requested.Cue); Assert.Equal(1, requested.Slot);

            // Re-applying the spoiler mask removes retained pixels as well as suppressing a fresh decode.
            player.SecondaryCueIndex = -1;
            Pump(window); await preview.Pending; Pump(window);
            Assert.True(row.IsMasked); Assert.False(preview.HasPreview); Assert.Single(player.PreviewRequests);
            Assert.Empty(player.Seeks); Assert.Empty(player.SubtitleSeeks); Assert.Equal(0, player.ToggleCalls);
        }
        finally { window.Close(); }
    }

    static void Click(Window window, Control control)
    {
        Point point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Dispatcher.UIThread.RunJobs();
    }

    static void Pump(Window window)
    {
        Dispatcher.UIThread.RunJobs(); window.CaptureRenderedFrame()?.Dispose(); Dispatcher.UIThread.RunJobs();
    }

    static IEnumerable<MenuItem> MenuItems(IEnumerable<object?> items)
    {
        foreach (MenuItem item in items.OfType<MenuItem>())
        {
            yield return item;
            foreach (MenuItem child in MenuItems(item.Items)) yield return child;
        }
    }

    sealed class PlaylistPlayer : FakePlaybackController, IPlaybackController
    {
        public List<PlaybackPlaylistItem> PlaylistItems { get; } = [];
        public List<PlaybackPlaylistItem> Selections { get; } = [];
        public string PlaylistTitle => "A partial source playlist";
        public int PlaylistExpectedCount => 3;
        public event Action? PlaylistChanged;
        public void NotifyPlaylistChanged() => PlaylistChanged?.Invoke();
        public IReadOnlyList<PlaybackPlaylistItem> GetPlaylistItems() => [.. PlaylistItems];
        public Task SelectPlaylistItemAsync(PlaybackPlaylistItem selected)
        {
            Selections.Add(selected);
            for (int index = 0; index < PlaylistItems.Count; index++)
                PlaylistItems[index] = PlaylistItems[index] with { IsCurrent = ReferenceEquals(PlaylistItems[index].Token, selected.Token) };
            PlaylistChanged?.Invoke();
            return Task.CompletedTask;
        }
    }

    sealed class LanguagePlayer : FakePlaybackController, IPlaybackController
    {
        public new string? GetSubtitleLanguage(int slot) => slot == 0 ? "ja" : "zh";
        public string? GetSubtitleTargetLanguage() => "en";
        public bool PrimaryTextTranslated => true;
        public bool SecondaryTextTranslated => true;
    }

    sealed class VolumePlayer : FakePlaybackController, IPlaybackController
    {
        int maximum = 150;
        int? engineVolumeSnapshot;
        event PropertyChangedEventHandler? maximumChanged;
        event PropertyChangedEventHandler? INotifyPropertyChanged.PropertyChanged
        {
            add { PropertyChanged += value; maximumChanged += value; }
            remove { PropertyChanged -= value; maximumChanged -= value; }
        }
        public new int VolumeMax => maximum;
        public new int Volume
        {
            get => engineVolumeSnapshot ?? base.Volume;
            set { engineVolumeSnapshot = null; base.Volume = value; }
        }
        public void SetEngineVolumeSnapshot(int value) => engineVolumeSnapshot = value;
        public void ChangeMaximum(int value)
        {
            maximum = value;
            maximumChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(VolumeMax)));
        }
    }

    sealed class BlockingSubtitleClient : IOpenSubtitlesClient
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Canceled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Drained { get; private set; }
        public bool Disposed { get; private set; }
        public async Task<OpenSubtitlesSearchPage> SearchAsync(OpenSubtitlesCredentials credentials, string query,
            string language = "", int page = 1, CancellationToken cancellationToken = default)
        {
            using var registration = cancellationToken.Register(() => Canceled.TrySetResult());
            Started.TrySetResult();
            try
            {
                await Release.Task.WaitAsync(TestContext.Current.CancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                return new([], 1, 1);
            }
            finally { Drained = true; }
        }
        public Task<OpenSubtitlesDownload> DownloadAsync(OpenSubtitlesCredentials credentials, OpenSubtitlesResult result,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException("No download belongs to this fixture.");
        public void Dispose() => Disposed = true;
    }

    sealed class PreviewPlayer : FakePlaybackController, IPlaybackController
    {
        public List<(CueInfo Cue, int Slot)> PreviewRequests { get; } = [];
        public Task<BgraBitmap?> GetCueBitmapAsync(CueInfo cue, int slot, CancellationToken cancellationToken = default)
        {
            PreviewRequests.Add((cue, slot));
            byte[] pixels = new byte[16 * 8 * 4];
            for (int offset = 0; offset < pixels.Length; offset += 4)
            { pixels[offset] = 255; pixels[offset + 3] = 255; }
            return Task.FromResult<BgraBitmap?>(new BgraBitmap(pixels, 16, 8));
        }
    }
}
