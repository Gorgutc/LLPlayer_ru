using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using AwesomeAssertions;
using LLPlayer.Avalonia.Services;
using LLPlayer.Avalonia.Views;
using LLPlayer.Avalonia.ViewModels;
using FlyleafLib.Plugins;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;

namespace LLPlayer.Avalonia.Tests;

public class OnlineVideoTests
{
    static string Executable(TempDir state, string name = "tool space ü.exe")
    {
        string path = Path.Combine(state.Path, name);
        File.WriteAllText(path, "test fixture only; never executed");
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        return path;
    }

    [Fact]
    public void MissingSettings_LeaveBothToolsUnset_AndDoNotWrite()
    {
        using var state = new TempDir();
        string path = Path.Combine(state.Path, "online.json");
        var store = new OnlineVideoSettingsStore(path);
        var settings = store.Load();
        Assert.Equal("", settings.YtDlpPath);
        Assert.Equal("", settings.DenoPath);
        Assert.Null(store.LoadWarning);
        Assert.False(File.Exists(path));
    }

    [Theory]
    [InlineData("{broken")]
    [InlineData("null")]
    [InlineData("{\"YtDlpPath\":null}")]
    public void CorruptSettings_ArePreservedUntilExplicitSave(string content)
    {
        using var state = new TempDir();
        string path = Path.Combine(state.Path, "online.json");
        File.WriteAllText(path, content);
        var store = new OnlineVideoSettingsStore(path);
        Assert.Equal("", store.Load().YtDlpPath);
        Assert.NotNull(store.LoadWarning);
        Assert.Equal(content, File.ReadAllText(path));
        store.Save(new());
        Assert.Null(store.LoadWarning);
        Assert.Equal("", store.Load().DenoPath);
    }

    [Fact]
    public void UnreadableSettings_ArePreserved()
    {
        using var state = new TempDir();
        string path = Path.Combine(state.Path, "online.json");
        File.WriteAllText(path, "preserve");
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var store = new OnlineVideoSettingsStore(path);
            Assert.Equal("", store.Load().YtDlpPath);
            Assert.NotNull(store.LoadWarning);
        }
        Assert.Equal("preserve", File.ReadAllText(path));
    }

    [Fact]
    public void ExplicitSave_ValidatesBothPaths_AndRoundTripsUnicodeWithoutTemporaryFiles()
    {
        using var state = new TempDir();
        string path = Path.Combine(state.Path, "online.json");
        var store = new OnlineVideoSettingsStore(path);
        string tool = Executable(state);
        store.Save(new() { YtDlpPath = tool, DenoPath = tool });
        string original = File.ReadAllText(path);
        Assert.Equal(tool, store.Load().YtDlpPath);
        Assert.Equal(tool, store.Load().DenoPath);
        foreach (string bad in new[] { "relative.exe", state.Path, Path.Combine(state.Path, "absent.exe") })
        {
            Assert.Throws<ArgumentException>(() => store.Save(new() { YtDlpPath = tool, DenoPath = bad }));
            Assert.Equal(original, File.ReadAllText(path));
        }
        Assert.Empty(Directory.GetFiles(state.Path, "*.tmp"));
    }

    [Fact]
    public void FailedAtomicSave_LeavesOriginalTargetAndCleansTemporaryFile()
    {
        using var state = new TempDir();
        // An existing directory is an unreadable settings target on both supported OSes.
        string path = Path.Combine(state.Path, "online.json");
        Directory.CreateDirectory(path);
        string original = Path.Combine(path, "preserved");
        File.WriteAllText(original, "original");
        var store = new OnlineVideoSettingsStore(path);
        Exception? error = Record.Exception(() => store.Save(new()));
        Assert.True(error is IOException or UnauthorizedAccessException);
        Assert.Equal("original", File.ReadAllText(original));
        Assert.Empty(Directory.GetFiles(state.Path, "*.tmp"));
    }

    [Fact]
    public async Task DraftEditsAndFailedSave_DoNotAuthorizeOpenOrChangePersistedSettings()
    {
        using var state = new TempDir();
        string tool = Executable(state);
        var original = new OnlineVideoSettings();
        var opened = new List<string>();
        var model = new OnlineVideoViewModel(original, _ => Task.FromException(new IOException("failure")), opened.Add)
            { YtDlpPath = tool, DenoPath = tool, Url = "https://example.com/video" };
        model.OpenCommand.Execute(null);
        Assert.Empty(opened);
        Assert.Contains("Save paths", model.Message);
        await model.SaveCommand.ExecuteAsync(null);
        model.OpenCommand.Execute(null);
        Assert.Empty(opened);
        Assert.Equal("", original.YtDlpPath);
        Assert.Contains("Save paths", model.Message);
    }

    [Fact]
    public async Task SaveAndOpen_UsesSavedSnapshot_AndPreservesExactUrlEnvelope()
    {
        using var state = new TempDir();
        string tool = Executable(state);
        string path = Path.Combine(state.Path, "online.json");
        var store = new OnlineVideoSettingsStore(path);
        string? request = null;
        var model = new OnlineVideoViewModel(new(), draft => { store.Save(draft); return Task.CompletedTask; }, value => request = value)
            { YtDlpPath = tool, DenoPath = tool, Url = "https://example.com/caption.vtt?q=a&x=ü#fragment" };
        await model.SaveCommand.ExecuteAsync(null);
        model.YtDlpPath = "unsaved invalid path";
        model.OpenCommand.Execute(null);
        Assert.NotNull(request);
        Assert.True(OnlineVideoRequest.TryGetUrl(request, out string url));
        Assert.Equal(model.Url, url);
        Assert.Equal(tool, store.Load().YtDlpPath);
        File.Delete(tool);
        request = null;
        model.OpenCommand.Execute(null);
        Assert.Null(request);
        Assert.Contains("existing executable", model.Message);
    }

    [Theory]
    [InlineData("file:///tmp/a")]
    [InlineData("-hostile")]
    [InlineData("https://user:password@example.com/video")]
    public void InvalidOnlineUrl_DoesNotReachOpen(string url)
    {
        using var state = new TempDir();
        string tool = Executable(state);
        var opened = new List<string>();
        var model = new OnlineVideoViewModel(new() { YtDlpPath = tool, DenoPath = tool }, _ => Task.CompletedTask, opened.Add) { Url = url };
        model.OpenCommand.Execute(null);
        Assert.Empty(opened);
        Assert.Contains("absolute HTTP", model.Message);
    }

    [AvaloniaTheory]
    [InlineData("https://example.com/direct.mp4")]
    [InlineData("https://example.com/extensionless")]
    [InlineData("/media/local.mp4")]
    public void OrdinaryOpenWithoutHelpers_RetainsExistingRoute(string input)
    {
        var (vm, player, _, _, _) = TestVm.Create();
        using (vm) { vm.Open(input); Assert.Equal([input], player.Opened); }
    }

    [AvaloniaFact]
    public void CancelInvalidatesPendingSidecars_AndRepeatedUrlRemainsDistinct()
    {
        var (vm, player, _, _, _) = TestVm.Create();
        using (vm)
        {
            const string url = "https://example.com/video";
            vm.Open(url, ["first.srt"]);
            vm.CancelOpenCommand.Execute(null);
            Assert.Equal(1, player.CancelOpenCalls);
            vm.Open(url, ["latest.srt"]);
            player.CompleteOpen(0);
            Assert.Empty(player.OpenedSubtitles);
            player.CompleteOpen(1);
            Assert.Equal(["latest.srt"], player.OpenedSubtitles);
        }
    }

    [AvaloniaFact]
    public void OnlineWindow_CloseDiscardsUnsavedEdits_AndOpeningOverlayHasCancel()
    {
        var original = new OnlineVideoSettings();
        int saves = 0;
        var model = new OnlineVideoViewModel(original, _ => { saves++; return Task.CompletedTask; }, _ => { });
        var dialog = new OnlineVideoWindow(model);
        dialog.FindControl<TextBox>("YtDlpPathBox")!.Text = "unsaved";
        dialog.Close();
        Assert.Equal(0, saves);
        Assert.Equal("", original.YtDlpPath);
        var window = new MainWindow(new KeyMapper(), null);
        Assert.NotNull(window.FindControl<Button>("CancelOpenButton"));
        window.Close();
    }

    [AvaloniaFact]
    public void OpeningCancel_ReceivesRealPointerInput()
    {
        var player = new FakePlaybackController { IsOpening = true };
        var window = new MainWindow(new KeyMapper(), null) { Width = 1000, Height = 600 };
        using var vm = new MainWindowViewModel(player, new AppPrefs().Normalize(), _ => { }, new FakeTranslator(), window);
        window.Attach(vm, renderer: null);
        vm.Open("movie.mp4");
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var button = window.FindControl<Button>("CancelOpenButton")!;
            Assert.True(button.IsEffectivelyVisible);
            Point center = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
            window.MouseDown(center, MouseButton.Left);
            window.MouseUp(center, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, player.CancelOpenCalls);
            Assert.False(vm.IsOpening);
            player.IsOpening = false;
            player.RaiseExternalMediaOpening();
            player.IsOpening = true;
            Dispatcher.UIThread.RunJobs();
            Assert.True(vm.IsOpening);
            Assert.False(button.IsEffectivelyVisible);
            Assert.False(button.IsEffectivelyEnabled);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void NativeOpeningOverlayPreservesStatusAndHidesCancel()
    {
        var (vm, player, _, _, _) = TestVm.Create();
        using (vm)
        {
            player.IsOpening = true;
            player.RaiseExternalMediaOpening();
            Assert.False(vm.CancelOpenCommand.CanExecute(null));
            vm.CancelOpenCommand.Execute(null);
            Assert.True(vm.IsOpening);
            Assert.Equal(0, player.CancelOpenCalls);
        }
    }

    [AvaloniaFact]
    public void OnlyExplicitActionAndSelectedOnlineRecentAuthorizeExtraction()
    {
        var (vm, player, _, _, _) = TestVm.Create();
        using (vm)
        {
            Assert.True(OnlineVideoRequest.TryCreate("https://example.org/video#chapter", out var request));
            vm.Open(request);
            Assert.Empty(player.OnlineVideoRequests);
            vm.OpenOnlineVideo(request);
            player.CompleteOpen(1);
            Assert.Contains(request, vm.RecentFiles);
            vm.OpenRecentCommand.Execute(request);
            Assert.Equal([request, request], player.OnlineVideoRequests);
        }
    }

    [AvaloniaFact]
    public void RecentMenuShowsPageAddressAndKeepsItsOnlineCommandParameter()
    {
        const string page = "https://example.org/watch?id=3#part.vtt";
        Assert.True(OnlineVideoRequest.TryCreate(page, out var request));
        var (vm, player, _, _, _) = TestVm.Create();
        var window = new MainWindow(new KeyMapper(), null);
        using (vm)
        try
        {
            vm.OpenOnlineVideo(request);
            player.CompleteOpen(0);
            window.Attach(vm, renderer: null);
            window.Show();
            var menu = window.FindControl<MenuItem>("RecentMenu")!;
            var media = window.FindControl<Menu>("MainMenu")!.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "_Media"));
            media.IsSubMenuOpen = true;
            Dispatcher.UIThread.RunJobs();
            menu.IsSubMenuOpen = true;
            Dispatcher.UIThread.RunJobs();
            var item = Assert.IsType<MenuItem>(menu.ContainerFromIndex(0));
            Assert.Equal(page, item.Header);
            Assert.Equal(request, item.CommandParameter);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ExplicitOnlineAction_IsDiscoverable_WithoutChangingOrdinaryOpen()
    {
        var window = new MainWindow(new KeyMapper(), null);
        try
        {
            var menu = window.FindControl<Menu>("MainMenu")!;
            var file = menu.Items.OfType<MenuItem>().Single(item => item.Header?.ToString() == "_Media");
            file.Items.OfType<MenuItem>().Should().Contain(item => item.Header!.ToString() == "Open online URL…");
            file.Items.OfType<MenuItem>().Should().Contain(item => item.Header!.ToString() == "_Open media…");
        }
        finally { window.Close(); }
    }
}
