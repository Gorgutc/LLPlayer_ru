using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using LLPlayer.Avalonia.Services;
using LLPlayer.Avalonia.ViewModels;
using LLPlayer.Avalonia.Views;
using WpfKey = System.Windows.Input.Key;

namespace LLPlayer.Avalonia.Tests;

public sealed class ParityCommandButtonKeyTests
{
    [AvaloniaTheory]
    [InlineData(Key.Space)] [InlineData(Key.Enter)]
    public void TitleButtonOwnsOnePressReleaseWithoutCallingTheEngine(Key key)
    {
        using Fixture fixture = new();
        fixture.Focus("MediaTitleButton");
        fixture.Press(key);
        fixture.Press(key); // A held key repeats without another physical release.
        Assert.Empty(fixture.Player.Opened);
        fixture.Release(key);
        Assert.Equal(["picked.mp4"], fixture.Player.Opened);
        Assert.Empty(fixture.Player.KeysDown); Assert.Empty(fixture.Player.KeysUp);
        Assert.Equal(0, fixture.Player.ToggleCalls);
    }

    [AvaloniaTheory]
    [InlineData("PreviousPlaylistButton", Key.Space, 0)]
    [InlineData("PreviousPlaylistButton", Key.Enter, 0)]
    [InlineData("NextPlaylistButton", Key.Space, 2)]
    [InlineData("NextPlaylistButton", Key.Enter, 2)]
    public void AdjacentButtonsOwnOneDetachedTargetAndNeverTogglePlayback(string name, Key key, int index)
    {
        using Fixture fixture = new();
        object expected = fixture.Player.Rows[index].Token;
        fixture.Focus(name);
        fixture.Press(key); fixture.Press(key);
        Assert.Empty(fixture.Player.Selected);
        fixture.Release(key);
        Assert.Same(expected, Assert.Single(fixture.Player.Selected).Token);
        Assert.Empty(fixture.Player.KeysDown); Assert.Empty(fixture.Player.KeysUp);
        Assert.Equal(0, fixture.Player.ToggleCalls);
    }

    [AvaloniaTheory]
    [InlineData(Key.Space)] [InlineData(Key.Enter)]
    public void MovingFocusBetweenAdjacentButtonsRejectsTheOriginalRelease(Key key)
    {
        using Fixture fixture = new();
        fixture.Focus("PreviousPlaylistButton"); fixture.Press(key);
        fixture.Focus("NextPlaylistButton"); fixture.Release(key);
        Assert.Empty(fixture.Player.Selected); Assert.Empty(fixture.Player.KeysDown);
        Assert.Equal(0, fixture.Player.ToggleCalls);
        // The consumed old release must not poison a genuinely fresh button press.
        fixture.Press(key); fixture.Release(key);
        Assert.Same(fixture.Player.Rows[2].Token, Assert.Single(fixture.Player.Selected).Token);
    }

    [AvaloniaTheory]
    [InlineData(Key.Space)] [InlineData(Key.Enter)]
    public void ReplacedPlaylistWhileHeldDoesNotSelectTheNewAdjacentItem(Key key)
    {
        using Fixture fixture = new();
        fixture.Focus("NextPlaylistButton"); fixture.Press(key);
        fixture.Player.Replace(); fixture.Pump();
        fixture.Release(key);
        Assert.Empty(fixture.Player.Selected); Assert.Empty(fixture.Player.KeysDown);
        fixture.Press(key); fixture.Release(key);
        Assert.Same(fixture.Player.Rows[2].Token, Assert.Single(fixture.Player.Selected).Token);
    }

    [AvaloniaFact]
    public void VideoSpacePressFollowedByAButtonReleaseCannotActivateThatButton()
    {
        using Fixture fixture = new();
        fixture.Window.VideoSurface.Focus(); fixture.Press(Key.Space);
        fixture.Focus("NextPlaylistButton"); fixture.Release(Key.Space);
        Assert.Empty(fixture.Player.Selected);
        Assert.Equal([WpfKey.Space], fixture.Player.KeysDown);
        Assert.Equal([WpfKey.Space], fixture.Player.KeysUp);
        Assert.Equal(1, fixture.Player.ToggleCalls);
    }

    [AvaloniaFact]
    public void VideoHeldSpaceRepeatingAfterButtonFocusDoesNotCreateAButtonPress()
    {
        using Fixture fixture = new();
        fixture.Window.VideoSurface.Focus(); fixture.Press(Key.Space);
        fixture.Focus("NextPlaylistButton"); fixture.Press(Key.Space); fixture.Release(Key.Space);
        Assert.Empty(fixture.Player.Selected);
        Assert.Equal([WpfKey.Space], fixture.Player.KeysDown);
        Assert.Equal([WpfKey.Space], fixture.Player.KeysUp);
        Assert.Equal(1, fixture.Player.ToggleCalls);
    }

    [AvaloniaTheory]
    [InlineData(Key.Space)] [InlineData(Key.Enter)]
    public void DeactivationRetiresHeldOwnerAndRepeatUntilItsPhysicalRelease(Key key)
    {
        using Fixture fixture = new();
        fixture.Focus("NextPlaylistButton"); fixture.Press(key);
        DeliverActivation(fixture.Window, false);
        DeliverActivation(fixture.Window, true);
        fixture.Focus("NextPlaylistButton"); fixture.Press(key); fixture.Release(key);
        Assert.Empty(fixture.Player.Selected); Assert.Empty(fixture.Player.KeysDown);
        fixture.Press(key); fixture.Release(key);
        Assert.Same(fixture.Player.Rows[2].Token, Assert.Single(fixture.Player.Selected).Token);
    }

    [AvaloniaTheory]
    [InlineData(Key.Space)] [InlineData(Key.Enter)]
    public async Task OwnedChildPhysicalReleaseRetiresTheParentPressBeforeTheNextFreshButtonCycle(Key key)
    {
        using Fixture fixture = new();
        fixture.Focus("MediaTitleButton"); fixture.Press(key);
        fixture.Window.ShowSettings(fixture.Main.CreateSettings());
        fixture.Pump();
        var child = Assert.IsType<SettingsWindow>(Assert.Single(fixture.Window.OwnedWindows));
        DeliverActivation(fixture.Window, false);
        child.FindControl<Button>("CloseButton")!.Focus();
        child.KeyRelease(key, RawInputModifiers.None, key == Key.Space ? PhysicalKey.Space : PhysicalKey.Enter, null);
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(fixture.Player.Opened);
        Assert.True(child.IsVisible, "A parent-owned release must not invoke the focused child's native Close button.");
        child.Close(); await Task.Yield(); fixture.Pump();
        DeliverActivation(fixture.Window, true);
        fixture.Focus("MediaTitleButton"); fixture.Press(key); fixture.Release(key);
        Assert.Equal(["picked.mp4"], fixture.Player.Opened);
        Assert.Empty(fixture.Player.KeysDown); Assert.Empty(fixture.Player.KeysUp);
    }

    [AvaloniaTheory]
    [InlineData(Key.Space)] [InlineData(Key.Enter)]
    public async Task FreshChildButtonPressKeepsItsNativeActivation(Key key)
    {
        using Fixture fixture = new();
        fixture.Focus("MediaTitleButton");
        fixture.Window.ShowSettings(fixture.Main.CreateSettings()); fixture.Pump();
        var child = Assert.IsType<SettingsWindow>(Assert.Single(fixture.Window.OwnedWindows));
        child.FindControl<Button>("CloseButton")!.Focus();
        child.KeyPress(key, RawInputModifiers.None, key == Key.Space ? PhysicalKey.Space : PhysicalKey.Enter, null);
        Dispatcher.UIThread.RunJobs();
        if (child.IsVisible)
            child.KeyRelease(key, RawInputModifiers.None, key == Key.Space ? PhysicalKey.Space : PhysicalKey.Enter, null);
        else
            fixture.Release(key);
        await Task.Yield(); fixture.Pump();
        Assert.False(child.IsVisible);
        Assert.Empty(fixture.Player.Opened); Assert.Empty(fixture.Player.KeysDown); Assert.Empty(fixture.Player.KeysUp);
    }

    static void DeliverActivation(Window window, bool active)
    {
        PropertyInfo? property = typeof(global::Avalonia.Platform.IWindowBaseImpl).GetProperty(active ? "Activated" : "Deactivated",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(property);
        Assert.IsType<Action>(property.GetValue(window.PlatformImpl))();
        Dispatcher.UIThread.RunJobs();
    }

    sealed class Fixture : IDisposable
    {
        public KeyPlayer Player { get; } = new() { IsOpened = true, Title = "Current media", Url = "current.mp4" };
        public MainWindowViewModel Main { get; }
        public MainWindow Window { get; } = new(new KeyMapper(), null) { Width = 1000, Height = 650 };
        public Fixture()
        {
            Player.Replace();
            Main = new(Player, new AppPrefs(), _ => { }, new FakeTranslator(), new FakeShell { NextPick = "picked.mp4" });
            Window.Attach(Main, null); Window.Show(); Pump();
        }
        public void Focus(string name)
        {
            Button button = Window.FindControl<Button>(name)!;
            Assert.NotNull(button); Assert.True(button.IsEffectivelyVisible); Assert.True(button.IsEffectivelyEnabled);
            button.Focus(); Pump(); Assert.Same(button, Window.FocusManager!.GetFocusedElement());
        }
        public void Press(Key key)
        {
            Window.KeyPress(key, RawInputModifiers.None, key == Key.Space ? PhysicalKey.Space : PhysicalKey.Enter, null);
            Dispatcher.UIThread.RunJobs();
        }
        public void Release(Key key)
        {
            Window.KeyRelease(key, RawInputModifiers.None, key == Key.Space ? PhysicalKey.Space : PhysicalKey.Enter, null);
            Dispatcher.UIThread.RunJobs();
        }
        public void Pump() { Dispatcher.UIThread.RunJobs(); Window.CaptureRenderedFrame()?.Dispose(); Dispatcher.UIThread.RunJobs(); }
        public void Dispose() { Window.Close(); Main.Dispose(); }
    }

    sealed class KeyPlayer : FakePlaybackController, IPlaybackController
    {
        public event Action? PlaylistChanged;
        public List<PlaybackPlaylistItem> Rows { get; private set; } = [];
        public List<PlaybackPlaylistItem> Selected { get; } = [];
        public IReadOnlyList<PlaybackPlaylistItem> GetPlaylistItems() => Rows.ToArray();
        public Task SelectPlaylistItemAsync(PlaybackPlaylistItem item) { Selected.Add(item); return Task.CompletedTask; }
        public new bool KeyDown(WpfKey key)
        {
            base.KeyDown(key);
            if (key == WpfKey.Space) TogglePlayPause();
            return true;
        }
        public new bool KeyUp(WpfKey key) { base.KeyUp(key); return true; }
        public void Replace()
        {
            Rows = Enumerable.Range(0, 3).Select(index => new PlaybackPlaylistItem(index, "Item " + index, index == 1, new object())).ToList();
            PlaylistChanged?.Invoke();
        }
    }
}
