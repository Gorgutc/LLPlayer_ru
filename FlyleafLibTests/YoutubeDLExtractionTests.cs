#if !WINDOWS
using System.Diagnostics;
using System.Text;
using System.Collections.Concurrent;
using FlyleafLib.MediaFramework.MediaContext;
using FlyleafLib.MediaPlayer;
using FlyleafLib.Platform;
using FlyleafLib.Plugins;

namespace FlyleafLib.Tests;

[Collection("PortableEngine")]
public sealed class YoutubeDLExtractionTests : IDisposable
{
    const string Url = "https://video.example/watch?id=%22--exec%22&lang=ru";
    const string Metadata = """
        {"title":"lesson","webpage_url":"https://video.example/watch","language":"en",
         "chapters":[{"start_time":1,"end_time":3,"title":"intro"}],
         "formats":[{"url":"https://cdn.example/v","height":720,"width":1280,"vcodec":"avc1","acodec":"none","fps":null,
           "http_headers":{"User-Agent":"agent","Referer":"https://video.example/","X-Token":"test"}},
          {"url":"https://cdn.example/big","height":1080,"width":1920,"vcodec":"avc1","acodec":"none"},
          {"url":"https://cdn.example/a","acodec":"opus","vcodec":"none","language":"en","height":null,"abr":null}],
         "automatic_captions":{"en-orig":[{"ext":"vtt","url":"https://youtube.example/api/timedtext?en"},
           {"ext":"srt","url":"https://cdn.example/srt"}],
           "de":[{"ext":"vtt","url":"https://youtube.example/api/timedtext?de"}]}}
        """;
    readonly DecoderContext decoder;
    readonly YoutubeDL plugin;
    readonly string directory;
    readonly string helper;
    readonly string deno;
    Func<ProcessStartInfo, OnlineVideoProcess> launch;

    public YoutubeDLExtractionTests()
    {
        PortableVideoTestMedia.RequireEngine();
        var config = new Config();
        decoder = new DecoderContext(config, enableDecoding: false);
        directory = Directory.CreateTempSubdirectory("LLPlayer путь ").FullName;
        helper = ToolPath("extractor");
        deno = ToolPath("deno");
        launch = _ => throw new InvalidOperationException("Unexpected helper launch");
        plugin = new YoutubeDL
        {
            Name = OnlineVideoRequest.PluginName, Handler = decoder, Log = new LogHandler("test"),
            StartHelper = info => launch(info)
        };
        config.Plugins[plugin.Name] = new();
        foreach (var (key, value) in plugin.GetDefaultOptions())
            plugin.Options[key] = value;
        plugin.Options[OnlineVideoRequest.YtDlpPath] = helper;
        plugin.Options[OnlineVideoRequest.DenoPath] = deno;
        config.Subtitles.Languages = [Language.Get("en")];
        config.Audio.Languages = [Language.Get("en")];
        decoder.PluginsOpen.Clear();
        decoder.PluginsOpen.Add(plugin.Name, plugin);
        Assert.True(OnlineVideoRequest.TryCreate(Url, out var request));
        decoder.Playlist.Url = request;
    }

    string ToolPath(string name)
    {
        var path = Path.Combine(directory, OperatingSystem.IsWindows() ? name + ".exe" : name);
        File.WriteAllText(path, "fixture placeholder");
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        return path;
    }

    sealed class Clipboard : IHostServices
    {
        public string? Text { get; set; }
        public string? GetClipboardText() => Text;
        public void SetClipboardText(string text) => Text = text;
        public Task<string?> PickMediaFileAsync(string? initialDirectory) => Task.FromResult<string?>(null);
        public void PlayCompletionSound(string soundPath) { }
    }

    [Fact]
    public void CopyOnlinePagePreservesOriginalQueryAndFragment()
    {
        const string page = "https://example.com/watch?a=%2F&b=2#lesson#part.vtt";
        Assert.True(OnlineVideoRequest.TryCreate(page, out string request));
        var previous = Utils.HostServices;
        var clipboard = new Clipboard();
        var player = new Player(new Config());
        try
        {
            Utils.HostServices = clipboard;
            player.Playlist.Url = request;
            player.CopyToClipboard();
            Assert.Equal(page, clipboard.Text);
        }
        finally { Utils.HostServices = previous; player.Dispose(); }
    }

    [Fact]
    public async Task OrdinaryClipboardEnvelopeCannotLaunchExtractor()
    {
        var previous = Utils.HostServices;
        var player = new Player(new Config());
        int starts = 0;
        launch = _ => { Interlocked.Increment(ref starts); throw new InvalidOperationException("unexpected helper"); };
        TaskCompletionSource completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            Utils.HostServices = new Clipboard { Text = decoder.Playlist.Url };
            plugin.Handler = player.decoder;
            player.Config.Plugins[plugin.Name] = decoder.Config.Plugins[plugin.Name];
            player.decoder.PluginsOpen.Clear();
            player.decoder.PluginsOpen.Add(plugin.Name, plugin);
            player.OpenCompleted += (_, _) => completed.TrySetResult();
            player.OpenFromClipboardSafe();
            await completed.Task.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
            Assert.Equal(0, starts);
        }
        finally { Utils.HostServices = previous; player.Dispose(); }
    }

    // Only the process-creation boundary is substituted. The plugin owns both real pipes,
    // JSON decoding, waiting, termination and publication through the actual Flyleaf handler.
    static OnlineVideoProcess Fixture(string output, int exitCode = 0, string? stallMarker = null)
    {
        bool windows = OperatingSystem.IsWindows();
        var info = new ProcessStartInfo(windows ? Path.Combine(Environment.SystemDirectory,
            "WindowsPowerShell", "v1.0", "powershell.exe") : "/bin/sh")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        info.Environment["LLPLAYER_FIXTURE_OUTPUT"] = output;
        if (stallMarker != null) info.Environment["LLPLAYER_FIXTURE_READY"] = stallMarker;
        if (windows)
        {
            info.ArgumentList.Add("-NoProfile");
            info.ArgumentList.Add("-NonInteractive");
            info.ArgumentList.Add("-EncodedCommand");
            var code = "[Console]::Write($env:LLPLAYER_FIXTURE_OUTPUT);" +
                (stallMarker == null ? "" : "[Console]::Out.Flush();[IO.File]::WriteAllText($env:LLPLAYER_FIXTURE_READY,'ready');Start-Sleep -Seconds 30;") +
                "exit " + exitCode;
            info.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(code)));
        }
        else
        {
            info.ArgumentList.Add("-c");
            info.ArgumentList.Add("printf '%s' \"$LLPLAYER_FIXTURE_OUTPUT\"; " +
                (stallMarker == null ? "" : "printf ready > \"$LLPLAYER_FIXTURE_READY\"; sleep 30; ") + "exit " + exitCode);
        }
        return OnlineVideoProcess.Start(info);
    }

    [Fact]
    public void OpensOneVideoAndPreservesSelectionCaptionsChaptersAndHeaders()
    {
        ProcessStartInfo? received = null;
        launch = info => { received = info; return Fixture(Metadata); };
        var result = OpenAdmitted(decoder.Open, TestContext.Current.CancellationToken);
        Assert.True(result.Success, result.Error);
        Assert.NotNull(received);
        Assert.Equal(helper, received.FileName);
        Assert.False(received.UseShellExecute);
        Assert.Empty(received.Arguments);
        Assert.Equal(["--playlist-items", "1:1001", "--ignore-config", "--no-plugin-dirs", "--no-cache-dir", "--no-js-runtimes",
            "--js-runtimes", "deno:" + deno, "--no-remote-components", "--skip-download",
            "--dump-single-json", "--encoding", "utf-8", "--", Url], received.ArgumentList);
        var item = Assert.Single(decoder.Playlist.Items);
        Assert.True(decoder.Playlist.Completed);
        Assert.Equal("lesson", item.Title);
        Assert.Equal("https://video.example/watch", item.DirectUrl);
        var chapter = Assert.Single(item.Chapters);
        Assert.Equal("intro", chapter.Title);
        Assert.Equal(TimeSpan.TicksPerSecond, chapter.StartTime);
        decoder.Playlist.Selected = item;
        var video = plugin.SuggestExternalVideo();
        Assert.Equal("https://cdn.example/v", video.Url);
        Assert.Equal("agent", video.UserAgent);
        Assert.Equal("https://video.example/", video.Referrer);
        Assert.Equal("test", video.HTTPHeaders["X-Token"]);
        Assert.Equal("https://cdn.example/a", plugin.SuggestExternalAudio().Url);
        var subtitle = Assert.Single(item.ExternalSubtitlesStreamsAll);
        Assert.True(subtitle.Automatic);
        Assert.Equal("https://youtube.example/api/timedtext?en", subtitle.Url);
    }

    [Fact]
    public void PlaylistPublishesAllPlayableItemsInOrderAndKeepsUnavailableCountVisible()
    {
        ProcessStartInfo? received = null;
        string second = Metadata.Replace("\"lesson\"", "\"second lesson\"", StringComparison.Ordinal)
            .Replace("https://video.example/watch", "https://video.example/second", StringComparison.Ordinal);
        string playlist = "{\"_type\":\"playlist\",\"title\":\"Study collection\",\"playlist_count\":3,\"entries\":[" + Metadata + ",null," + second + "]}";
        launch = info => { received = info; return Fixture(playlist); };
        plugin.Options["ExtraArguments"] = "--cookies-from-browser firefox --playlist-items 1:3";
        var result = OpenAdmitted(decoder.Open, TestContext.Current.CancellationToken);
        Assert.True(result.Success, result.Error);
        Assert.NotNull(received);
        Assert.Equal(["--playlist-items", "1:1001", "--cookies-from-browser", "firefox", "--playlist-items", "1:3"], received.ArgumentList.Take(6));
        Assert.DoesNotContain("--no-playlist", received.ArgumentList);
        Assert.Equal("--", received.ArgumentList[^2]);
        Assert.Equal(Url, received.ArgumentList[^1]);
        Assert.Equal("Study collection", decoder.Playlist.Title);
        Assert.Equal(3, decoder.Playlist.ExpectingItems);
        Assert.True(decoder.Playlist.Completed);
        Assert.Equal(["lesson", "second lesson"], decoder.Playlist.Items.Select(item => item.Title));
        decoder.Playlist.Selected = decoder.Playlist.Items[1];
        Assert.Equal("https://cdn.example/v", plugin.SuggestExternalVideo().Url);
        Assert.Equal("https://video.example/second", decoder.Playlist.Selected.DirectUrl);
    }

    [Theory]
    [InlineData("", 0)]
    [InlineData("{malformed", 0)]
    [InlineData("{}", 0)]
    [InlineData("{\"_type\":\"playlist\"}", 0)]
    [InlineData(Metadata, 12)]
    public void UnusableMetadataNeverPublishes(string json, int exitCode)
    {
        launch = _ => Fixture(json, exitCode);
        Assert.False(OpenAdmitted(decoder.Open, TestContext.Current.CancellationToken).Success);
        Assert.Empty(decoder.Playlist.Items);
        Assert.False(decoder.Playlist.Completed);
    }

    [Fact]
    public void MissingHelpersDoNotStartAProcess()
    {
        plugin.Options[OnlineVideoRequest.DenoPath] = "deno";
        int starts = 0;
        launch = _ => { starts++; return Fixture(Metadata); };
        Assert.Contains("absolute paths", OpenAdmitted(decoder.Open, TestContext.Current.CancellationToken).Error);
        Assert.Equal(0, starts);
        Assert.Empty(decoder.Playlist.Items);
    }

    [Fact]
    public void CancelBeforeInitializeCannotBeLostByDecoderStop()
    {
        decoder.Interrupt = true;
        using var scope = OnlineVideoRequest.ObserveCancellation(new CancellationToken(canceled: true));
        // No network or stream open: without the post-Initialize guard, admission runs.
        var result = decoder.Open("https://direct.example/media", defaultPlaylistItem: false);
        Assert.Equal("Cancelled", result.Error);
        Assert.Null(decoder.Playlist.Url);
    }

    [Fact]
    public void CancellationAtNativeOpenCompletionPreventsAutoplay()
    {
        var clip = PortableVideoTestMedia.Hd;
        var config = new Config();
        config.Player.AutoPlay = true;
        config.Audio.Enabled = false;
        using var cancel = new CancellationTokenSource();
        using var scope = OnlineVideoRequest.ObserveCancellation(cancel.Token);
        var player = new Player(config);
        try
        {
            bool playable = false;
            player.decoder.OpenCompleted += (_, args) =>
            {
                playable = args.Success && player.CanPlay;
                cancel.Cancel();
            };
            var result = player.Open(clip);
            Assert.True(playable);
            Assert.Equal("Cancelled", result.Error);
            Assert.False(player.IsPlaying);
            Assert.False(player.CanPlay);
            Assert.Null(player.decoder.VideoStream);
        }
        finally { player.Dispose(); }
    }

    [Fact]
    public void CancellationAtPlaylistAdmissionCannotBeLostByItemSwitch()
    {
        var clip = PortableVideoTestMedia.Hd;
        var config = new Config();
        config.Player.AutoPlay = false;
        config.Audio.Enabled = false;
        using var cancel = new CancellationTokenSource();
        using var scope = OnlineVideoRequest.ObserveCancellation(cancel.Token);
        var player = new Player(config);
        try
        {
            player.Playlist.Items.CollectionChanged += (_, args) =>
            {
                if (args.NewItems != null) cancel.Cancel();
            };
            var result = player.Open(clip);
            Assert.True(cancel.IsCancellationRequested);
            Assert.Equal("Cancelled", result.Error);
            Assert.False(player.CanPlay);
        }
        finally { player.Dispose(); }
    }

    [Fact]
    public void LateCancelDoesNotClearNewerOpenOfIdenticalMedia()
    {
        var clip = PortableVideoTestMedia.Hd;
        var config = new Config();
        config.Player.AutoPlay = false;
        config.Audio.Enabled = false;
        using var cancel = new CancellationTokenSource();
        using var scope = OnlineVideoRequest.ObserveCancellation(cancel.Token);
        var player = new Player(config);
        try
        {
            bool replaced = false;
            player.decoder.OpenCompleted += (_, args) =>
            {
                if (replaced || !args.Success) return;
                replaced = true;
                cancel.Cancel();
                using var replacementScope = OnlineVideoRequest.ObserveCancellation(CancellationToken.None);
                Assert.True(player.Open(clip).Success);
            };
            var result = player.Open(clip);
            Assert.True(replaced);
            Assert.Equal("Cancelled", result.Error);
            Assert.True(player.CanPlay);
            Assert.Equal(Status.Paused, player.Status);
            Assert.NotNull(player.decoder.VideoStream);
        }
        finally { player.Dispose(); }
    }

    [Fact]
    public void OwnedProcessPreservesActualArgumentBoundaries()
    {
        var path = Path.Combine(directory, "echo arguments.ps1");
        File.WriteAllText(path, "[Console]::OutputEncoding=[Text.Encoding]::UTF8; foreach ($a in $args) { [Console]::WriteLine($a) }");
        bool windows = OperatingSystem.IsWindows();
        var info = new ProcessStartInfo(windows ? Path.Combine(Environment.SystemDirectory,
            "WindowsPowerShell", "v1.0", "powershell.exe") : "/bin/sh")
        { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string arg in windows
            ? new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", path }
            : new[] { "-c", "printf '%s\\n' \"$@\"", "fixture" }) info.ArgumentList.Add(arg);
        string[] expected = ["deno:C:\\путь с пробелом\\", Url, "quote\\\"inside", ""];
        foreach (var arg in expected) info.ArgumentList.Add(arg);
        using var child = OnlineVideoProcess.Start(info);
        Assert.True(child.WaitForExit(5000));
        Assert.Equal(0, child.ExitCode);
        using var reader = new StreamReader(child.Output);
        Assert.Equal(expected, expected.Select(_ => reader.ReadLine()).ToArray());
        Assert.Null(reader.ReadLine());
    }

    [Fact]
    public async Task DescendantCannotOutliveExtractorThatAlreadyExited()
    {
        string marker = Path.Combine(directory, "orphan-pid");
        launch = _ =>
        {
            var info = new ProcessStartInfo
            {
                FileName = OperatingSystem.IsWindows() ? Path.Combine(Environment.SystemDirectory,
                    "WindowsPowerShell", "v1.0", "powershell.exe") : "/bin/sh",
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
            };
            info.Environment["LLPLAYER_FIXTURE_READY"] = marker;
            if (OperatingSystem.IsWindows())
            {
                string code = """
                    $p=Start-Process -FilePath (Get-Process -Id $pid).Path -ArgumentList '-NoProfile','-NonInteractive','-Command','Start-Sleep -Seconds 30' -NoNewWindow -PassThru
                    [IO.File]::WriteAllText($env:LLPLAYER_FIXTURE_READY,$p.Id)
                    [Console]::Write('{}')
                    """;
                foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-EncodedCommand",
                    Convert.ToBase64String(Encoding.Unicode.GetBytes(code)) }) info.ArgumentList.Add(arg);
            }
            else
            {
                info.ArgumentList.Add("-c");
                info.ArgumentList.Add("sleep 30 & echo $! > \"$LLPLAYER_FIXTURE_READY\"; printf '{}'");
            }
            return OnlineVideoProcess.Start(info);
        };
        var result = await Task.Run(() => OpenAdmitted(plugin.Open, TestContext.Current.CancellationToken)).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.False(result.Success);
        Assert.True(File.Exists(marker));
        Process? child;
        try { child = Process.GetProcessById(int.Parse(File.ReadAllText(marker))); }
        catch (ArgumentException) { return; } // Already reaped.
        using (child)
        {
            try { Assert.True(child.WaitForExit(1500), "The extractor exited but its child is still running."); }
            finally { if (!child.HasExited) child.Kill(entireProcessTree: true); }
        }
    }

    [Fact]
    public async Task PublishesOnDispatcherWithoutAWorkerHoldingTheUiThread()
    {
        var previous = Utils.UIDispatcher;
        var dispatcher = new PumpDispatcher();
        bool publishedOnUi = false;
        decoder.Playlist.Items.CollectionChanged += (_, _) => publishedOnUi = dispatcher.CheckAccess();
        launch = _ => Fixture(Metadata);
        Utils.IsTesting = false;
        Utils.UIDispatcher = dispatcher;
        try
        {
            var opening = Task.Run(() => OpenAdmitted(plugin.Open, TestContext.Current.CancellationToken));
            var elapsed = Stopwatch.StartNew();
            while (!opening.IsCompleted && elapsed.Elapsed < TimeSpan.FromSeconds(10))
            {
                dispatcher.Pump();
                await Task.Delay(10, TestContext.Current.CancellationToken);
            }
            var result = await opening.WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
            Assert.True(result.Success, result.Error);
            Assert.True(publishedOnUi);
        }
        finally { Utils.UIDispatcher = previous; }
    }

    sealed class PumpDispatcher : IUIDispatcher
    {
        readonly ConcurrentQueue<Action> pending = new();
        public TaskCompletionSource NeedsUi { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int uiThread;
        public bool CheckAccess()
        {
            bool isUiThread = Environment.CurrentManagedThreadId == Volatile.Read(ref uiThread);
            if (!isUiThread) NeedsUi.TrySetResult();
            return isUiThread;
        }
        public void Post(Action action) => pending.Enqueue(action);
        public void Invoke(Action action) => throw new InvalidOperationException("Worker attempted synchronous UI dispatch.");
        public void Pump()
        {
            Volatile.Write(ref uiThread, Environment.CurrentManagedThreadId);
            try { while (pending.TryDequeue(out var action)) action(); }
            finally { Volatile.Write(ref uiThread, 0); }
        }
    }

    [Fact]
    public async Task StopWhileDispatchIsPendingRejectsLatePublication()
    {
        var previous = Utils.UIDispatcher;
        var dispatcher = new PumpDispatcher();
        launch = _ => Fixture(Metadata);
        Utils.IsTesting = false;
        Utils.UIDispatcher = dispatcher;
        try
        {
            var opening = Task.Run(() => OpenAdmitted(plugin.Open, TestContext.Current.CancellationToken));
            await dispatcher.NeedsUi.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            plugin.Dispose();
            var result = await opening.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
            Assert.Equal("Cancelled", result.Error);
            dispatcher.Pump();
            Assert.Empty(decoder.Playlist.Items);
        }
        finally { Utils.UIDispatcher = previous; }
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("interrupt")]
    [InlineData("counter")]
    [InlineData("replace")]
    [InlineData("close")]
    public async Task StalledOwnedProcessIsTerminatedWithoutLatePublication(string action)
    {
        using var cts = new CancellationTokenSource();
        var started = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var marker = Path.Combine(directory, "metadata-written");
        int count = 0;
        launch = _ =>
        {
            bool first = Interlocked.Increment(ref count) == 1;
            var child = Fixture(Metadata, stallMarker: first ? marker : null);
            if (first) started.SetResult(child.Id);
            return child;
        };
        var firstOpen = Task.Run(() => OpenAdmitted(plugin.Open, cts.Token));
        var pid = await started.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        using var child = Process.GetProcessById(pid);
        // Wait until the stalled child has actually emitted valid metadata. Cancellation
        // immediately after Start() would not exercise the late-publication case.
        var elapsed = Stopwatch.StartNew();
        while (!File.Exists(marker) && elapsed.Elapsed < TimeSpan.FromSeconds(10))
            await Task.Delay(10, TestContext.Current.CancellationToken);
        Assert.True(File.Exists(marker));
        Assert.Empty(decoder.Playlist.Items);
        switch (action)
        {
            case "cancel": cts.Cancel(); break;
            case "interrupt": ((PluginHandler)decoder).Interrupt = true; break;
            case "counter": decoder.OpenCounter++; break;
            case "replace": Assert.True(OpenAdmitted(plugin.Open, TestContext.Current.CancellationToken).Success); break;
            case "close": plugin.Dispose(); break;
        }
        var result = await firstOpen.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal("Cancelled", result.Error);
        Assert.True(child.HasExited);
        Assert.Equal(action == "replace" ? 1 : 0, decoder.Playlist.Items.Count);
    }

    OpenResults OpenAdmitted(Func<OpenResults> open, CancellationToken token)
    {
        using var scope = OnlineVideoRequest.ObserveCancellation(token, decoder.Playlist.Url);
        return open();
    }

    public void Dispose()
    {
        plugin.Dispose();
        decoder.Dispose();
        Directory.Delete(directory, recursive: true);
    }
}
#endif
