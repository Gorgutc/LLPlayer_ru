using Avalonia.Headless.XUnit;
using CommunityToolkit.Mvvm.Input;
using LLPlayer.Avalonia.Services;
using LLPlayer.Avalonia.ViewModels;

namespace LLPlayer.Avalonia.Tests;

public sealed class LocalMediaActionsTests
{
    [AvaloniaFact]
    public void Next_and_previous_open_natural_same_extension_siblings_through_normal_media_admission()
    {
        using TempDir directory = new();
        string first = AddFile(directory, "Part 1.mkv");
        string middle = AddFile(directory, "Part 2.MKV");
        string last = AddFile(directory, "Part 10.mkv");
        AddFile(directory, "Part 3.mp4");
        var (vm, player, shell, prefs) = Create();
        using (vm)
        {
            player.Url = middle;
            Command(vm, "OpenNextLocalMediaCommand").Execute(null);
            Command(vm, "OpenPreviousLocalMediaCommand").Execute(null);

            Assert.Equal([last, first], player.Opened);
            Assert.Equal(directory.Path, prefs.LastFolder);
            Assert.Empty(shell.Revealed);
            Assert.Empty(shell.Clipboard);
        }
    }

    [AvaloniaFact]
    public void End_of_folder_never_wraps_or_opens_another_extension()
    {
        using TempDir directory = new();
        string only = AddFile(directory, "Only.mkv");
        AddFile(directory, "Other.mp4");
        var (vm, player, _, _) = Create();
        using (vm)
        {
            player.Url = only;
            Command(vm, "OpenNextLocalMediaCommand").Execute(null);
            Command(vm, "OpenPreviousLocalMediaCommand").Execute(null);
            Assert.Empty(player.Opened);
        }
    }

    [AvaloniaFact]
    public async Task Title_actions_use_the_selected_media_path_instead_of_the_collection_url()
    {
        using TempDir directory = new();
        string current = AddFile(directory, "Part 1.mkv");
        string next = AddFile(directory, "Part 2.mkv");
        var (vm, player, shell, _) = Create();
        using (vm)
        {
            player.Url = "https://example.test/collection";
            player.SelectedMediaPath = current;
            Command(vm, "CopyMediaPathCommand").Execute(null);
            await AsyncCommand(vm, "OpenMediaLocationCommand").ExecuteAsync(null);
            Command(vm, "OpenNextLocalMediaCommand").Execute(null);

            Assert.Equal([current], shell.Clipboard);
            Assert.Equal([current], shell.Revealed);
            Assert.Equal([next], player.Opened);
        }
    }

    [AvaloniaFact]
    public void Missing_or_network_source_does_not_open_media_and_reports_an_actionable_error()
    {
        using TempDir directory = new();
        var (vm, player, _, _) = Create();
        using (vm)
        {
            player.Url = Path.Combine(directory.Path, "missing.mkv");
            Command(vm, "OpenNextLocalMediaCommand").Execute(null);
            Assert.Contains(vm.Toasts, toast => toast.Variant == ToastVariant.Destructive);
            player.Url = "https://example.test/video.mp4";
            Command(vm, "OpenPreviousLocalMediaCommand").Execute(null);
            Assert.Empty(player.Opened);
        }
    }

    [AvaloniaFact]
    public async Task Location_admits_existing_local_file_uri_and_captures_the_source_before_await()
    {
        using TempDir directory = new();
        string path = AddFile(directory, "Part #1.mkv");
        var (vm, player, shell, _) = Create();
        using (vm)
        {
            player.Url = new Uri(path).AbsoluteUri;
            TaskCompletionSource<bool> reveal = new(TaskCreationOptions.RunContinuationsAsynchronously);
            shell.OnReveal = _ => reveal.Task;
            Task action = AsyncCommand(vm, "OpenMediaLocationCommand").ExecuteAsync(null);
            Assert.False(action.IsCompleted);
            player.Url = "https://example.test/replacement";
            reveal.SetResult(true);
            await action;

            Assert.Equal([path], shell.Revealed);
            Assert.Empty(shell.BrowserUris);
            Assert.Empty(player.Opened);
            Assert.Empty(shell.Clipboard);
        }
    }

    [AvaloniaTheory]
    [InlineData("https://user:secret@example.test/video.mkv")]
    [InlineData("http://user@example.test/video.mkv")]
    [InlineData("rtsp://example.test/video.mkv")]
    [InlineData("javascript:alert(1)")]
    [InlineData("https://")]
    [InlineData("https:// example.test/video.mkv")]
    [InlineData("https://example.test/video\n.mkv")]
    [InlineData("https://example.test/video\".mkv")]
    [InlineData("https://example.test\\video.mkv")]
    [InlineData("relative/video.mkv")]
    public async Task Location_rejects_credentials_non_http_malformed_or_relative_sources(string source)
    {
        var (vm, player, shell, _) = Create();
        using (vm)
        {
            player.Url = source;
            await AsyncCommand(vm, "OpenMediaLocationCommand").ExecuteAsync(null);
            Assert.Empty(shell.Revealed);
            Assert.Empty(shell.BrowserUris);
            Assert.Empty(player.Opened);
            Assert.Contains(vm.Toasts, toast => toast.Variant == ToastVariant.Destructive);
        }
    }

    [AvaloniaTheory]
    [InlineData("https://example.test/watch?v=2&chapter=3#time")]
    [InlineData("http://example.test/lecture")]
    public async Task Location_opens_the_selected_public_http_page_in_the_browser(string selectedPage)
    {
        var (vm, player, shell, _) = Create();
        using (vm)
        {
            player.Url = "https://example.test/collection";
            player.SelectedMediaPath = selectedPage;
            await AsyncCommand(vm, "OpenMediaLocationCommand").ExecuteAsync(null);

            Assert.Equal(selectedPage, Assert.Single(shell.BrowserUris).OriginalString);
            Assert.Empty(shell.Revealed);
            Assert.Empty(player.Opened);
            Assert.Empty(shell.Clipboard);
            Assert.DoesNotContain(vm.Toasts, toast => toast.Variant == ToastVariant.Destructive);
        }
    }

    [AvaloniaFact]
    public async Task Location_unwraps_online_request_and_captures_its_page_before_the_browser_wait()
    {
        var (vm, player, shell, _) = Create();
        using (vm)
        {
            player.Url = "ytdlp:https://example.test/watch?v=2#";
            TaskCompletionSource<bool> browser = new(TaskCreationOptions.RunContinuationsAsynchronously);
            shell.OnBrowser = _ => browser.Task;
            Task action = AsyncCommand(vm, "OpenMediaLocationCommand").ExecuteAsync(null);
            Assert.False(action.IsCompleted);
            player.Url = "https://example.test/replacement";
            browser.SetResult(true);
            await action;

            Assert.Equal("https://example.test/watch?v=2", Assert.Single(shell.BrowserUris).OriginalString);
            Assert.Empty(shell.Revealed);
            Assert.Empty(player.Opened);
        }
    }

    [AvaloniaFact]
    public async Task Unavailable_browser_and_launch_errors_are_recoverable_toasts()
    {
        var (vm, player, shell, _) = Create();
        using (vm)
        {
            player.Url = "https://example.test/watch?v=2";
            shell.OnBrowser = _ => Task.FromResult(false);
            await AsyncCommand(vm, "OpenMediaLocationCommand").ExecuteAsync(null);
            Assert.Equal(ToastVariant.Destructive, Assert.Single(vm.Toasts).Variant);
            vm.Toasts.Clear();
            shell.OnBrowser = _ => Task.FromException<bool>(new IOException("test browser failure"));
            await AsyncCommand(vm, "OpenMediaLocationCommand").ExecuteAsync(null);

            Assert.Equal(2, shell.BrowserUris.Count);
            Assert.Contains(vm.Toasts, toast => toast.Variant == ToastVariant.Destructive
                && toast.Message!.Contains("test browser failure", StringComparison.Ordinal));
            Assert.Empty(shell.Revealed);
            Assert.Empty(player.Opened);
        }
    }

    [AvaloniaFact]
    public async Task Missing_local_file_and_shell_failures_remain_recoverable_toasts()
    {
        using TempDir directory = new();
        string path = AddFile(directory, "Part 1.mkv");
        var (vm, player, shell, _) = Create();
        using (vm)
        {
            player.Url = Path.Combine(directory.Path, "missing.mkv");
            await AsyncCommand(vm, "OpenMediaLocationCommand").ExecuteAsync(null);
            Assert.Empty(shell.Revealed);
            vm.Toasts.Clear();
            player.Url = path;
            shell.OnReveal = _ => Task.FromResult(false);
            await AsyncCommand(vm, "OpenMediaLocationCommand").ExecuteAsync(null);
            Assert.Equal(ToastVariant.Destructive, Assert.Single(vm.Toasts).Variant);
            vm.Toasts.Clear();
            shell.OnReveal = _ => Task.FromException<bool>(new IOException("test reveal failure"));
            await AsyncCommand(vm, "OpenMediaLocationCommand").ExecuteAsync(null);
            Assert.Equal([path, path], shell.Revealed);
            Assert.Contains(vm.Toasts, toast => toast.Variant == ToastVariant.Destructive && toast.Message!.Contains("test reveal failure", StringComparison.Ordinal));
            Assert.Empty(player.Opened);
        }
    }

    [AvaloniaFact]
    public void Copy_preserves_exact_path_or_network_url_and_unwraps_explicit_online_requests()
    {
        using TempDir directory = new();
        string missing = Path.Combine(directory.Path, "missing #1.mkv");
        var (vm, player, shell, _) = Create();
        using (vm)
        {
            player.Url = missing;
            Command(vm, "CopyMediaPathCommand").Execute(null);
            player.Url = "https://example.test/watch?v=1&part=two#offset";
            Command(vm, "CopyMediaPathCommand").Execute(null);
            player.Url = "ytdlp:https://example.test/watch?v=2#";
            Command(vm, "CopyMediaPathCommand").Execute(null);

            Assert.Equal([missing, "https://example.test/watch?v=1&part=two#offset", "https://example.test/watch?v=2"], shell.Clipboard);
            Assert.Empty(shell.Revealed);
            Assert.Empty(player.Opened);
        }
    }

    [AvaloniaFact]
    public void Copy_reports_clipboard_failure_without_starting_media_or_external_operations()
    {
        var (vm, player, shell, _) = Create();
        using (vm)
        {
            player.Url = "https://example.test/video";
            shell.ClipboardError = new InvalidOperationException("clipboard unavailable");
            Command(vm, "CopyMediaPathCommand").Execute(null);
            Assert.Contains(vm.Toasts, toast => toast.Variant == ToastVariant.Destructive && toast.Message!.Contains("clipboard unavailable", StringComparison.Ordinal));
            Assert.Empty(shell.Revealed);
            Assert.Empty(player.Opened);
        }
    }

    [AvaloniaFact]
    public void Empty_source_never_copies_or_opens_media_and_reports_the_missing_source()
    {
        var (vm, player, shell, _) = Create();
        using (vm)
        {
            player.Url = null;
            Command(vm, "CopyMediaPathCommand").Execute(null);
            Assert.Equal(ToastVariant.Destructive, Assert.Single(vm.Toasts).Variant);
            vm.Toasts.Clear();
            Command(vm, "OpenNextLocalMediaCommand").Execute(null);
            Assert.Empty(shell.Clipboard);
            Assert.Empty(player.Opened);
            Assert.Contains(vm.Toasts, toast => toast.Variant == ToastVariant.Destructive);
        }
    }

    [AvaloniaFact]
    public async Task Closed_view_model_ignores_retained_title_actions()
    {
        using TempDir directory = new();
        string path = AddFile(directory, "Part 1.mkv");
        AddFile(directory, "Part 2.mkv");
        var (vm, player, shell, _) = Create();
        player.Url = path;
        vm.Dispose();

        Command(vm, "OpenNextLocalMediaCommand").Execute(null);
        Command(vm, "OpenPreviousLocalMediaCommand").Execute(null);
        Command(vm, "CopyMediaPathCommand").Execute(null);
        await AsyncCommand(vm, "OpenMediaLocationCommand").ExecuteAsync(null);

        Assert.Empty(player.Opened);
        Assert.Empty(shell.Clipboard);
        Assert.Empty(shell.Revealed);
        Assert.Empty(shell.BrowserUris);
    }

    static (MainWindowViewModel Vm, LocalMediaPlaybackController Player, LocalMediaShell Shell, AppPrefs Prefs) Create()
    {
        LocalMediaPlaybackController player = new();
        LocalMediaShell shell = new();
        AppPrefs prefs = new AppPrefs().Normalize();
        MainWindowViewModel vm = new(player, prefs, _ => { }, new FakeTranslator(), shell, new ManualTimeProvider());
        return (vm, player, shell, prefs);
    }

    static string AddFile(TempDir directory, string name)
    {
        string path = Path.Combine(directory.Path, name);
        File.WriteAllText(path, "fixture");
        return path;
    }

    // Reflection keeps these real command tests buildable before production code exists for the RED run.
    static IRelayCommand Command(MainWindowViewModel vm, string property)
        => Assert.IsAssignableFrom<IRelayCommand>(typeof(MainWindowViewModel).GetProperty(property)?.GetValue(vm));
    static IAsyncRelayCommand AsyncCommand(MainWindowViewModel vm, string property)
        => Assert.IsAssignableFrom<IAsyncRelayCommand>(Command(vm, property));

    sealed class LocalMediaPlaybackController : FakePlaybackController, IPlaybackController
    {
        public string? SelectedMediaPath { get; set; }
        public string? CurrentMediaPath => SelectedMediaPath ?? Url;
    }

    sealed class LocalMediaShell : IAppShell
    {
        public bool IsFullScreen { get; set; }
        public List<string> Revealed { get; } = [];
        public List<Uri> BrowserUris { get; } = [];
        public List<string> Clipboard { get; } = [];
        public Func<string, Task<bool>>? OnReveal { get; set; }
        public Func<Uri, Task<bool>>? OnBrowser { get; set; }
        public Exception? ClipboardError { get; set; }
        public Task<string?> PickMediaFileAsync(string? initialFolder) => Task.FromResult<string?>(null);
        public Task<string?> PickSubtitlesFileAsync(string? initialFolder) => Task.FromResult<string?>(null);
        public Task<bool> RevealMediaAsync(string path)
        {
            Revealed.Add(path);
            return OnReveal?.Invoke(path) ?? Task.FromResult(true);
        }
        public Task<bool> OpenBrowserAsync(Uri uri)
        {
            BrowserUris.Add(uri);
            return OnBrowser?.Invoke(uri) ?? Task.FromResult(true);
        }
        public void ShowCheatSheet() { }
        public void SetClipboardText(string text)
        {
            if (ClipboardError != null) throw ClipboardError;
            Clipboard.Add(text);
        }
        public void ApplyTheme(string theme) { }
        public void Exit() { }
    }
}
