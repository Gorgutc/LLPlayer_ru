using System.Collections.ObjectModel;
using System.Reflection;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using FlyleafLib;
using FlyleafLib.MediaPlayer;
using LLPlayer.Avalonia.Services;
using LLPlayer.Avalonia.ViewModels;
using AvKey = Avalonia.Input.Key;
using WpfKey = System.Windows.Input.Key;

namespace LLPlayer.Avalonia.Tests;

public sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "llp-avalonia-tests-" + Guid.NewGuid().ToString("N"));

    public TempDir() => Directory.CreateDirectory(Path);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, true);
        }
        catch (IOException)
        {
        }
    }
}

public class PrefsTests
{
    [Fact]
    public void Prefs_RoundTrip()
    {
        using TempDir dir = new();
        PrefsStore store = new(Path.Combine(dir.Path, "sub", "prefs.json"));
        AppPrefs prefs = new()
        {
            Volume = 42, Mute = true, SubtitleFontSize = 40, SubtitlesVisible = false, SidebarVisible = true, SidebarWidth = 420,
            Theme = "Light", LastFolder = "/media/videos", WordTranslationService = "Off", TranslateTargetLanguage = "Russian",
        };
        prefs.AddRecent("/a.mp4");
        prefs.AddRecent("/b.mkv");

        store.Save(prefs);
        AppPrefs loaded = store.Load();

        loaded.Should().BeEquivalentTo(prefs);
        store.LoadWarning.Should().BeNull();
        File.Exists(store.Path + ".tmp").Should().BeFalse("saves are atomic (temp file renamed)");
    }

    [Fact]
    public void MissingFile_GivesDefaults()
    {
        using TempDir dir = new();
        AppPrefs prefs = new PrefsStore(Path.Combine(dir.Path, "none.json")).Load();
        prefs.Theme.Should().Be("Dark");
        prefs.Volume.Should().Be(100);
        prefs.SidebarVisible.Should().BeFalse();
        prefs.RecentFiles.Should().BeEmpty();
    }

    [Fact]
    public void CorruptFile_GivesDefaults_KeepsCopy_AndWarns()
    {
        using TempDir dir = new();
        string path = Path.Combine(dir.Path, "prefs.json");
        File.WriteAllText(path, "{ not json");
        PrefsStore store = new(path);

        AppPrefs prefs = store.Load();
        prefs.Volume.Should().Be(100);
        store.LoadWarning.Should().NotBeNull();
        File.ReadAllText(path + ".bad").Should().Be("{ not json");
    }

    [Fact]
    public void Normalize_ClampsHandEditedValues()
    {
        AppPrefs prefs = new() { Volume = -5, SubtitleFontSize = 500, SidebarWidth = double.NaN, Theme = "purple", RecentFiles = ["", "/a", "/a", "/b"] };
        prefs.Normalize();
        prefs.Volume.Should().Be(0);
        prefs.SubtitleFontSize.Should().Be(AppPrefs.MaxSubtitleFontSize);
        prefs.SidebarWidth.Should().Be(360);
        prefs.Theme.Should().Be("Dark");
        prefs.RecentFiles.Should().Equal("/a", "/b");
    }

    [Fact]
    public void Recent_IsMostRecentFirst_Deduplicated_AndBounded()
    {
        AppPrefs prefs = new();
        for (int i = 0; i < 15; i++)
            prefs.AddRecent($"/f{i}");
        prefs.AddRecent("/f10");
        prefs.RecentFiles.Should().HaveCount(AppPrefs.MaxRecentFiles);
        prefs.RecentFiles[0].Should().Be("/f10");
        prefs.RecentFiles.Should().OnlyHaveUniqueItems();
    }
}

public class StartupTests
{
    static Func<string, string?> Env(Dictionary<string, string> values) => k => values.TryGetValue(k, out string? v) ? v : null;

    [Fact]
    public void Paths_FollowXdg_WithOverride()
    {
        AppPaths defaults = AppPaths.FromEnvironment(Env([]), "/home/u", isWindows: false);
        defaults.ConfigDir.Should().Be("/home/u/.config/LLPlayer");
        defaults.StateDir.Should().Be("/home/u/.local/state/LLPlayer");
        defaults.CrashLogFile.Should().Be("/home/u/.local/state/LLPlayer/crash.log");
        defaults.PrefsFile.Should().Be("/home/u/.config/LLPlayer/LLPlayer.Avalonia.json");

        AppPaths xdg = AppPaths.FromEnvironment(Env(new() { ["XDG_CONFIG_HOME"] = "/cfg", ["XDG_STATE_HOME"] = "/st" }), "/home/u", isWindows: false);
        xdg.ConfigDir.Should().Be("/cfg/LLPlayer");
        xdg.StateDir.Should().Be("/st/LLPlayer");

        AppPaths relative = AppPaths.FromEnvironment(Env(new() { ["XDG_CONFIG_HOME"] = "rel", ["XDG_STATE_HOME"] = "rel" }), "/home/u", isWindows: false);
        relative.ConfigDir.Should().Be("/home/u/.config/LLPlayer", "relative XDG paths are invalid and ignored");
        relative.StateDir.Should().Be("/home/u/.local/state/LLPlayer");

        AppPaths overridden = AppPaths.FromEnvironment(Env(new() { ["LLPLAYER_CONFIG_DIR"] = "/tmp/llp", ["XDG_CONFIG_HOME"] = "/cfg" }), "/home/u", isWindows: false);
        overridden.ConfigDir.Should().Be("/tmp/llp");
    }

    [Fact]
    public void Paths_OnWindows_UseAppData_NotXdg()
    {
        // Owner decision (F-13 "one core + one UI"): the Avalonia app is platform-neutral — %APPDATA%\LLPlayer on Windows.
        // Rooted "/..." values keep the test host-independent (Path.IsPathRooted("C:\\x") is false on Linux).
        var env = new Dictionary<string, string>
        {
            ["APPDATA"] = "/Users/u/AppData/Roaming",
            ["LOCALAPPDATA"] = "/Users/u/AppData/Local",
            ["XDG_CONFIG_HOME"] = "/cfg",
            ["XDG_STATE_HOME"] = "/st",
        };
        AppPaths win = AppPaths.FromEnvironment(Env(env), "/Users/u", isWindows: true);
        win.ConfigDir.Should().Be(Path.Combine("/Users/u/AppData/Roaming", "LLPlayer"), "XDG variables do not apply on Windows");
        win.StateDir.Should().Be(Path.Combine("/Users/u/AppData/Local", "LLPlayer"));
        win.PrefsFile.Should().Be(Path.Combine("/Users/u/AppData/Roaming", "LLPlayer", "LLPlayer.Avalonia.json"));

        AppPaths fallback = AppPaths.FromEnvironment(Env([]), "/Users/u", isWindows: true);
        fallback.ConfigDir.Should().Be(Path.Combine("/Users/u", "AppData", "Roaming", "LLPlayer"));
        fallback.StateDir.Should().Be(Path.Combine("/Users/u", "AppData", "Local", "LLPlayer"));

        AppPaths overridden = AppPaths.FromEnvironment(Env(new(env) { ["LLPLAYER_CONFIG_DIR"] = "/tmp/llp" }), "/Users/u", isWindows: true);
        overridden.ConfigDir.Should().Be(Path.GetFullPath("/tmp/llp"));
    }

    [Fact]
    public void FFmpeg_RequiredLibraries_MatchPlatform()
    {
        FFmpegLocator.RequiredLibrariesFor(isWindows: false).Should().OnlyContain(l => l.StartsWith("lib") && l.Contains(".so."));
        FFmpegLocator.RequiredLibrariesFor(isWindows: true).Should().OnlyContain(l => l.EndsWith(".dll"));
        FFmpegLocator.RequiredLibrariesFor(isWindows: true).Should().HaveCount(FFmpegLocator.RequiredLibrariesFor(isWindows: false).Count);
        FFmpegLocator.RequiredLibraries.Should().Equal(FFmpegLocator.RequiredLibrariesFor(OperatingSystem.IsWindows()));

        // The Windows set is exactly the tracked FFmpeg/*.dll files (minus the optional avdevice).
        string repoFFmpeg = Path.Combine(RepoRoot(), "FFmpeg");
        foreach (string dll in FFmpegLocator.WindowsLibraries)
            File.Exists(Path.Combine(repoFFmpeg, dll)).Should().BeTrue($"{dll} is a tracked Windows FFmpeg library");

        using TempDir dir = new();
        foreach (string dll in FFmpegLocator.WindowsLibraries)
            File.WriteAllText(Path.Combine(dir.Path, dll), "");
        FFmpegLocator.Locate(dir.Path, Env([]), isWindows: true).IsValid.Should().BeTrue();
        FFmpegLocator.Locate(dir.Path, Env([]), isWindows: false).MissingLibraries.Should().BeEquivalentTo(FFmpegLocator.LinuxLibraries);
    }

    static string RepoRoot()
    {
        for (DirectoryInfo? d = new(AppContext.BaseDirectory); d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "LLPlayer.slnx")))
                return d.FullName;
        throw new InvalidOperationException("repository root (LLPlayer.slnx) not found");
    }

    [Fact]
    public void FFmpeg_Precedence_CommandLine_Environment_AppFolder()
    {
        using TempDir dir = new();
        string app = Path.Combine(dir.Path, "app");
        string env = Path.Combine(dir.Path, "env");
        string cli = Path.Combine(dir.Path, "cli");
        foreach (string d in new[] { Path.Combine(app, "FFmpeg"), env, cli })
        {
            Directory.CreateDirectory(d);
            foreach (string lib in FFmpegLocator.RequiredLibraries)
                File.WriteAllText(Path.Combine(d, lib), "");
        }

        FFmpegLocator.Locate(cli, Env(new() { ["LLPLAYER_FFMPEG_DIR"] = env }), app).Should().Match<FFmpegLocation>(l => l.Directory == cli && l.Source == FFmpegDirSource.CommandLine && l.IsValid);
        FFmpegLocator.Locate(null, Env(new() { ["LLPLAYER_FFMPEG_DIR"] = env }), app).Should().Match<FFmpegLocation>(l => l.Directory == env && l.Source == FFmpegDirSource.Environment);
        FFmpegLocator.Locate(null, Env([]), app).Should().Match<FFmpegLocation>(l => l.Directory == Path.Combine(app, "FFmpeg") && l.Source == FFmpegDirSource.AppFolder && l.IsValid);
    }

    [Fact]
    public void FFmpeg_MissingLibraries_AreReported()
    {
        using TempDir dir = new();
        File.WriteAllText(Path.Combine(dir.Path, "libavutil.so.60"), "");
        FFmpegLocation loc = FFmpegLocator.Locate(dir.Path, Env([]), isWindows: false);
        loc.IsValid.Should().BeFalse();
        loc.MissingLibraries.Should().Contain("libavcodec.so.62").And.NotContain("libavutil.so.60");

        FFmpegLocator.Locate(Path.Combine(dir.Path, "nope"), Env([]), isWindows: false).MissingLibraries.Should().BeEquivalentTo(FFmpegLocator.LinuxLibraries);
    }

    [Fact]
    public void Options_Parse()
    {
        AppOptions o = AppOptions.Parse(["--ffmpeg-dir", "/ff", "--theme", "LIGHT", "--sidebar", "--screenshot", "/tmp/s.png", "--screenshot-delay", "2.5", "movie.mkv", "--sub", "movie.srt", "--seek", "12"]);
        o.FFmpegDir.Should().Be("/ff");
        o.Theme.Should().Be("Light");
        o.ShowSidebar.Should().BeTrue();
        o.ScreenshotPath.Should().Be("/tmp/s.png");
        o.ScreenshotDelaySeconds.Should().Be(2.5);
        o.MediaPath.Should().Be("movie.mkv");
        o.SubtitlesPath.Should().Be("movie.srt");
        o.SeekSeconds.Should().Be(12);

        AppOptions.Parse(["https://example.com/v.m3u8"]).MediaPath.Should().Be("https://example.com/v.m3u8");
        FluentActions.Invoking(() => AppOptions.Parse(["--bogus"])).Should().Throw<AppOptionsException>();
        FluentActions.Invoking(() => AppOptions.Parse(["a", "b"])).Should().Throw<AppOptionsException>();
        FluentActions.Invoking(() => AppOptions.Parse(["--theme", "blue"])).Should().Throw<AppOptionsException>();
        FluentActions.Invoking(() => AppOptions.Parse(["--ffmpeg-dir"])).Should().Throw<AppOptionsException>();
    }

    [Fact]
    public void AppVersion_EqualsTheWpfAppVersion()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "LLPlayer.slnx")))
            dir = dir.Parent;
        dir.Should().NotBeNull();

        string wpf = File.ReadAllText(Path.Combine(dir!.FullName, "LLPlayer", "LLPlayer.csproj"));
        string wpfVersion = Regex.Match(wpf, "<Version>([^<]+)</Version>").Groups[1].Value;
        Version appVersion = typeof(App).Assembly.GetName().Version!;
        appVersion.ToString(3).Should().Be(wpfVersion);
    }
}

public class KeyboardTests
{
    [Fact]
    public void AvaloniaKeys_MapToWpfKeys_ByIdenticalNameAndValue()
    {
        int shared = 0;
        foreach (string name in Enum.GetNames<AvKey>())
        {
            AvKey av = Enum.Parse<AvKey>(name);
            if (!Enum.TryParse(name, out WpfKey wpf))
            {
                KeyMapper.ToWpf(av).Should().Be(WpfKey.None, $"{name} has no WPF equivalent");
                continue;
            }

            ((int)av).Should().Be((int)wpf, name);
            KeyMapper.ToWpf(av).Should().Be(wpf, name);
            shared++;
        }
        shared.Should().BeGreaterThan(150);
    }

    [Fact]
    public void ModifierState_AnswersBothSides()
    {
        KeyMapper keys = new();
        keys.Update(global::Avalonia.Input.KeyModifiers.Control | global::Avalonia.Input.KeyModifiers.Shift);
        keys.IsKeyDown(WpfKey.LeftCtrl).Should().BeTrue();
        keys.IsKeyDown(WpfKey.RightCtrl).Should().BeTrue();
        keys.IsKeyDown(WpfKey.LeftShift).Should().BeTrue();
        keys.IsKeyDown(WpfKey.LeftAlt).Should().BeFalse();
        keys.IsKeyDown(WpfKey.A).Should().BeFalse();
    }

    [Fact]
    public void AppShortcuts_UseWpfDefaultChords_AndDoNotShadowEngineBindings()
    {
        // Same default chords as the WPF app's AppActions.DefaultCustomActionsMap for the shared actions.
        (string Name, WpfKey Key, bool Ctrl, bool Shift)[] wpfDefaults =
        [
            ("SubsPositionUp", WpfKey.Up, false, true), ("SubsPositionDown", WpfKey.Down, false, true),
            ("SubsSizeIncrease", WpfKey.Right, false, true), ("SubsSizeDecrease", WpfKey.Left, false, true),
            ("SubsPrimaryTextCopy", WpfKey.C, true, false), ("ActivateSubsSearch", WpfKey.F, true, false),
            ("ToggleSidebar", WpfKey.B, true, false), ("OpenWindowCheatSheet", WpfKey.F1, false, false),
        ];
        foreach (var d in wpfDefaults)
            AppShortcutMap.Defaults.Should().Contain(s => s.ActionName == d.Name && s.Key == d.Key && s.Ctrl == d.Ctrl && s.Shift == d.Shift && !s.Alt, d.Name);

        KeysConfig engine = new();
        engine.LoadDefault();
        foreach (AppShortcut s in AppShortcutMap.Defaults)
            engine.Keys.Should().NotContain(b => b.Key == s.Key && b.Ctrl == s.Ctrl && b.Alt == s.Alt && b.Shift == s.Shift,
                $"app shortcut {s.ActionName} must not hide an engine binding");
    }

    [Fact]
    public void CheatSheet_ListsEngineAndAppShortcuts_AndFilters()
    {
        KeysConfig engine = new();
        engine.LoadDefault();
        CheatSheetViewModel sheet = new(AppShortcutMap.BuildCheatSheet(engine.Keys));

        var rows = sheet.Groups.SelectMany(g => g.Rows).ToList();
        rows.Should().Contain(r => r.ActionName == "TogglePlayPause" && r.Chords.Any(c => c.Keys.SequenceEqual(new[] { "Space" })));
        rows.Should().Contain(r => r.ActionName == "OpenFromFileDialog" && r.Chords.Any(c => c.Keys.SequenceEqual(new[] { "Ctrl", "O" })));
        rows.Should().Contain(r => r.ActionName == "ToggleSidebar" && r.Chords.Any(c => c.Keys.SequenceEqual(new[] { "Ctrl", "B" })));
        rows.Should().Contain(r => r.ActionName == "OpenWindowCheatSheet" && r.Chords.Count == 2);
        sheet.Groups.First().Name.Should().Be("Playback");

        sheet.FilterText = "sidebar";
        sheet.Groups.SelectMany(g => g.Rows).Should().OnlyContain(r => r.Description.Contains("Sidebar", StringComparison.OrdinalIgnoreCase) || r.ActionName.Contains("Sidebar"));
        sheet.HitText.Should().EndWith($"of {sheet.TotalCount}");
    }

    [Theory]
    [InlineData(WpfKey.Play, "Play")]
    [InlineData(WpfKey.PageDown, "PgDn")]
    [InlineData(WpfKey.OemQuestion, "/")]
    [InlineData(WpfKey.D1, "1")]
    [InlineData(WpfKey.Left, "←")]
    [InlineData(WpfKey.F1, "F1")]
    public void KeyNames_AreReadable(WpfKey key, string expected) => KeyMapper.KeyName(key).Should().Be(expected);

    [Theory]
    [InlineData("Ctrl+Shift++", new[] { "Ctrl", "Shift", "+" })]
    [InlineData("Ctrl+O", new[] { "Ctrl", "O" })]
    [InlineData("+", new[] { "+" })]
    [InlineData("Shift+/", new[] { "Shift", "/" })]
    public void ChordSplitting(string chord, string[] expected)
        => ShortcutRowViewModel.SplitChord(chord).Should().Equal(expected);
}

[Collection(FlyleafGlobalsCollection.Name)]
public class CollectionSyncBridgeTests
{
    sealed class QueueDispatcher : IUIDispatcher
    {
        public Queue<Action> Pending { get; } = new();
        public bool CheckAccess() => false;
        public void Post(Action action) => Pending.Enqueue(action);
        public void Invoke(Action action) => action();

        public void RunAll()
        {
            while (Pending.TryDequeue(out Action? a))
                a();
        }
    }

    [Fact]
    public void Watch_CoalescesBackgroundChanges_IntoOneUiCallback()
    {
        QueueDispatcher ui = new();
        CollectionSyncBridge bridge = new(ui);
        ObservableCollection<int> items = [];
        object gate = new();
        bridge.Register(items, gate);
        int calls = 0;
        using IDisposable watch = bridge.Watch(items, () => calls++);

        Parallel.For(0, 100, i =>
        {
            lock (gate)
                items.Add(i);
        });

        ui.Pending.Should().HaveCount(1, "changes are coalesced into one pending UI callback");
        ui.RunAll();
        calls.Should().Be(1);
        bridge.Snapshot(items).Should().HaveCount(100);

        items.Add(1000);
        ui.RunAll();
        calls.Should().Be(2);

        watch.Dispose();
        items.Add(2000);
        ui.Pending.Should().BeEmpty();
    }

    [Fact]
    public void Install_ForwardsEngineRegistrationsAndRefreshes()
    {
        QueueDispatcher ui = new();
        CollectionSyncBridge bridge = new(ui);
        var previousSync = System.Windows.Data.BindingOperations.CollectionSynchronizationHandler;
        var previousRefresh = System.Windows.Data.CollectionViewSource.RefreshHandler;
        try
        {
            bridge.Install();
            ObservableCollection<string> subs = [];
            System.Windows.Data.BindingOperations.EnableCollectionSynchronization(subs, new object());
            bridge.IsRegistered(subs).Should().BeTrue();

            int calls = 0;
            using IDisposable watch = bridge.Watch(subs, () => calls++);
            System.Windows.Data.CollectionViewSource.GetDefaultView(subs).Refresh();   // SubManager.Refresh()
            ui.RunAll();
            calls.Should().Be(1);
        }
        finally
        {
            System.Windows.Data.BindingOperations.CollectionSynchronizationHandler = previousSync;
            System.Windows.Data.CollectionViewSource.RefreshHandler = previousRefresh;
        }
    }
}

public class SubtitleTextTests
{
    [Theory]
    [InlineData("Hello world, this is a test.", 2, "Hello")]
    [InlineData("Hello world, this is a test.", 8, "world")]
    [InlineData("don't stop", 1, "don't")]
    [InlineData("well-known fact", 7, "well-known")]
    [InlineData("Hello world", 5, "")]
    [InlineData("Привет, мир", 2, "Привет")]
    public void WordAt(string text, int index, string expected)
        => LLPlayer.Avalonia.Controls.SubtitleText.WordAt(text, index).Should().Be(expected);
}

/// <summary>
/// The app takes its audio output from FlyleafLib's AudioBackendFactory (OpenAL Soft, Null fallback) and honours
/// LLPLAYER_AUDIO_BACKEND. Process-wide state (environment, AudioEngine.Backend): runs in the non-parallel collection.
/// </summary>
[Collection(FlyleafGlobalsCollection.Name)]
public class AudioBackendSelectorTests
{
    static T WithBackendVariable<T>(string? value, Func<T> action)
    {
        string? previous = Environment.GetEnvironmentVariable(AudioBackendFactory.BackendVariable);
        Environment.SetEnvironmentVariable(AudioBackendFactory.BackendVariable, value);
        try
        {
            return action();
        }
        finally
        {
            Environment.SetEnvironmentVariable(AudioBackendFactory.BackendVariable, previous);
        }
    }

    [Fact]
    public void ForcedNull_SelectsTheSilentBackend_AndSaysWhy()
    {
        List<string> log = [];
        IAudioBackend backend = WithBackendVariable("null", () => AudioBackendSelector.Select(log.Add));

        backend.Should().BeSameAs(NullAudioBackend.Instance);
        log.Should().ContainSingle().Which.Should().Contain("LLPLAYER_AUDIO_BACKEND=null");
        AudioBackendSelector.IsWarning(log[0]).Should().BeFalse("the user asked for silence");
    }

    [Fact]
    public void ForcedOpenAl_SelectsTheOpenAlBackend_WhenTheLibraryIsInstalled()
    {
        Assert.SkipUnless(System.Runtime.InteropServices.NativeLibrary.TryLoad("libopenal.so.1", out _)
                          || System.Runtime.InteropServices.NativeLibrary.TryLoad("libopenal.so", out _),
            "OpenAL Soft (libopenal.so.1) is not installed.");

        List<string> log = [];
        IAudioBackend backend = WithBackendVariable("openal", () => AudioBackendSelector.Select(log.Add));

        backend.Should().BeOfType<OpenAlAudioBackend>();
        backend.Name.Should().Be("OpenAL");
    }

    [Fact]
    public void Apply_AssignsTheSelectedBackend_ToTheAudioEngine_AndReturnsTheMessages()
    {
        IAudioBackend previous = AudioEngine.Backend;
        try
        {
            AudioEngine.Backend = new FakeAudioBackend();
            IReadOnlyList<string> messages = WithBackendVariable("null", AudioBackendSelector.Apply);

            AudioEngine.Backend.Should().BeSameAs(NullAudioBackend.Instance);
            messages.Should().ContainSingle().Which.Should().StartWith("Audio backend: Null");
        }
        finally
        {
            AudioEngine.Backend = previous;
        }
    }

    [Theory]
    [InlineData("Audio backend: OpenAL", false)]
    [InlineData("Audio backend: Null (LLPLAYER_AUDIO_BACKEND=null)", false)]
    [InlineData("Audio backend: Null (no sound output) because OpenAL is unavailable: x", true)]
    [InlineData("Audio backend: unknown LLPLAYER_AUDIO_BACKEND value 'x' (expected null, openal or auto); using auto", true)]
    public void FallbacksAreWarnings(string message, bool warning)
        => AudioBackendSelector.IsWarning(message).Should().Be(warning);

    sealed class FakeAudioBackend : IAudioBackend
    {
        public string Name => "Fake";
        public IReadOnlyList<AudioEngine.AudioEndpoint> EnumerateDevices() => [];
        public AudioEngine.AudioEndpoint? GetDefaultDevice() => null;
        public IAudioSink CreateSink(string? deviceId, int sampleRate, int channels) => throw new NotSupportedException();
    }
}

public class EngineConfigTests
{

    [Fact]
    public void EngineConfig_UsesGivenFFmpegFolder_AndStateLog()
    {
        AppPaths paths = new("/c", "/s");
        EngineConfig ec = EngineBootstrap.CreateEngineConfig("/ff", paths);
        ec.FFmpegPath.Should().Be("/ff");
        ec.LogOutput.Should().Be("/s/flyleaf.log");
        ec.UIRefresh.Should().BeTrue();
        typeof(EngineConfig).GetProperty(nameof(EngineConfig.PluginsPath), BindingFlags.Public | BindingFlags.Instance)!.GetValue(ec).Should().BeNull();
    }
}
