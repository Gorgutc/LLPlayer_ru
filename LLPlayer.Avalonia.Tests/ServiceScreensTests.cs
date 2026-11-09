using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using AwesomeAssertions;
using LLPlayer.Avalonia.Services;
using LLPlayer.Avalonia.ViewModels;

namespace LLPlayer.Avalonia.Tests;

public class ServiceScreensTests
{
    [Fact]
    public void About_reports_the_actual_executable_identity_and_active_shared_dependencies()
    {
        Assembly assembly = typeof(App).Assembly;
        AboutInformation info = AboutInformation.FromAssembly(assembly);
        info.Version.Should().Be(assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version!.ToString());
        info.Runtime.Should().Contain(".NET");
        info.ProcessArchitecture.Should().NotBeNullOrWhiteSpace();
        info.CopyText.Should().Contain(info.Version).And.Contain(info.Runtime);
        info.Credits.Select(c => c.Name).Should().Contain(["Avalonia", "Flyleaf", "FFmpeg"]);
        info.Credits.Should().NotContain(c => c.Name == "Prism" || c.Name == "MaterialDesignThemes" || c.Name == "WpfColorFontDialog");
    }

    [Fact]
    public void Mouse_guidance_tracks_current_click_double_click_and_wheel_preferences()
    {
        AppPrefs prefs = new();
        var enabled = MouseHelpCatalog.Build(prefs);
        enabled.Should().Contain(r => r.Gesture == "Left click" && r.Description.Contains("play", StringComparison.OrdinalIgnoreCase));
        enabled.Should().Contain(r => r.Gesture == "Double click" && r.Description.Contains("fullscreen", StringComparison.OrdinalIgnoreCase));
        enabled.Should().Contain(r => r.Gesture == "Wheel" && r.Description.Contains("volume", StringComparison.OrdinalIgnoreCase));
        prefs.MouseSingleClickToPlay = prefs.MouseDoubleClickToFullScreen = prefs.MouseWheelToVolumeUpDown = false;
        var disabled = MouseHelpCatalog.Build(prefs);
        disabled.Should().NotContain(r => r.Gesture == "Left click" || r.Gesture == "Double click" || r.Gesture == "Wheel");
        disabled.Should().Contain(r => r.Gesture == "Ctrl + wheel");
        disabled.Should().Contain(r => r.Gesture == "Ctrl + Shift + wheel");
        disabled.Should().Contain(r => r.Group == "Subtitle text");
        disabled.Should().Contain(r => r.Group == "Menus");
        disabled.Should().Contain(r => r.Group == "Window title");
    }

    [Theory]
    [InlineData("https://user:secret@example.org/video.mp4?token=secret#fragment", "https://example.org/video.mp4")]
    [InlineData("/tmp/a movie.mp4", "/tmp/a movie.mp4")]
    public void Debug_media_source_removes_network_credentials_query_and_fragment(string source, string expected)
        => PlayerDebugSnapshot.SafeMediaSource(source).Should().Be(expected);

    [Theory]
    [InlineData("https://user:secret@bad host/video.mp4?token=secret")]
    [InlineData("rtsp://user:secret@[invalid-host/video?token=secret")]
    [InlineData("//user:secret@bad host/video?token=secret")]
    public void Debug_media_source_omits_malformed_network_inputs_instead_of_exposing_secrets(string source)
    {
        string safe = PlayerDebugSnapshot.SafeMediaSource(source);
        safe.Should().Contain("[source details omitted]");
        safe.Should().NotContain("secret");
        safe.Should().NotBe(source);
    }

    [Theory]
    [InlineData("C:\\media\\a movie.mp4")]
    [InlineData("/tmp/a movie.mp4")]
    [InlineData("a movie.mp4")]
    public void Debug_media_source_preserves_ordinary_local_paths(string source)
        => PlayerDebugSnapshot.SafeMediaSource(source).Should().Be(source);

    [Theory]
    [InlineData("https://syntheticUser:syntheticSecret@example.org/video.mp4?token=syntheticToken#syntheticFragment", "https://example.org/video.mp4")]
    [InlineData("rtsp://syntheticUser:syntheticSecret@bad host/video?token=syntheticToken#syntheticFragment", "[source details omitted]")]
    [InlineData("//syntheticUser:syntheticSecret@bad host/video?token=syntheticToken#syntheticFragment", "[source details omitted]")]
    public void Debug_url_shaped_title_is_safe_in_the_visible_snapshot_and_copied_diagnostics(string title, string expected)
    {
        PlayerDebugSnapshot raw = new("Playing", title, "", "1", "2", false, "video", "audio", "subtitles", "renderer", "hardware");
        PlayerDebugViewModel model = new(raw);
        AssertSafeTitle(model);
        model.Refresh(raw with { MediaTitle = "Episode 2: Context" });
        model.Refresh(raw);
        AssertSafeTitle(model);

        void AssertSafeTitle(PlayerDebugViewModel current)
        {
            current.Snapshot.MediaTitle.Should().Be(expected);
            foreach (string secret in new[] { "syntheticUser", "syntheticSecret", "syntheticToken", "syntheticFragment" })
            {
                JsonSerializer.Serialize(current.Snapshot).Should().NotContain(secret);
                current.CopyText.Should().NotContain(secret);
            }
        }
    }

    [Theory]
    [InlineData("file:///C:/media/video.mkv?token=syntheticToken#syntheticFragment", "file:///C:/media/video.mkv")]
    [InlineData("file:///tmp/video.mkv?token=syntheticToken#syntheticFragment", "file:///tmp/video.mkv")]
    [InlineData("FiLe://server/share/video.mkv?token=syntheticToken#syntheticFragment", "file://server/share/video.mkv")]
    public void Debug_explicit_file_uri_options_are_removed_from_title_source_and_copied_diagnostics(string source, string expected)
    {
        PlayerDebugSnapshot raw = new("Playing", source, source, "1", "2", false, "video", "audio", "subtitles", "renderer", "hardware");
        PlayerDebugViewModel model = new(raw);
        AssertSafeFileUri(model);
        model.Refresh(raw with { MediaTitle = "Episode 2: Context", MediaSource = "/tmp/video.mkv" });
        model.Refresh(raw);
        AssertSafeFileUri(model);

        void AssertSafeFileUri(PlayerDebugViewModel current)
        {
            current.Snapshot.MediaTitle.Should().Be(expected);
            current.Snapshot.MediaSource.Should().Be(expected);
            AssertNoSyntheticSecrets(current);
            current.CopyText.Should().Contain("Title: " + expected).And.Contain("Source: " + expected);
        }
    }

    [Theory]
    [InlineData("file://syntheticUser:syntheticSecret@server/share/video.mkv?token=syntheticToken#syntheticFragment")]
    [InlineData("FILE:/tmp/video.mkv?token=syntheticToken#syntheticFragment")]
    public void Debug_explicit_file_uri_variants_never_expose_credentials_or_options_even_if_the_parser_rejects_them(string source)
    {
        PlayerDebugSnapshot raw = new("Playing", source, source, "", "", false, "", "", "", "", "");
        PlayerDebugViewModel model = new(raw);
        AssertNoSyntheticSecrets(model);
        model.Snapshot.MediaTitle.Should().NotBeNullOrWhiteSpace();
        model.Snapshot.MediaSource.Should().NotBeNullOrWhiteSpace();
        model.Refresh(raw with { MediaTitle = "Ordinary title", MediaSource = "video.mkv" });
        model.Refresh(raw);
        AssertNoSyntheticSecrets(model);
    }

    [Theory]
    [InlineData("/tmp/a movie?director=cut#scene.mkv")]
    [InlineData("C:\\media\\a movie?director=cut#scene.mkv")]
    [InlineData("a movie?director=cut#scene.mkv")]
    public void Debug_ordinary_paths_preserve_literal_question_and_hash_characters(string path)
    {
        PlayerDebugViewModel model = new(new("Playing", path, path, "", "", false, "", "", "", "", ""));
        model.Snapshot.MediaTitle.Should().Be(path);
        model.Snapshot.MediaSource.Should().Be(path);
        model.CopyText.Should().Contain("Title: " + path).And.Contain("Source: " + path);
    }

    static void AssertNoSyntheticSecrets(PlayerDebugViewModel model)
    {
        foreach (string secret in new[] { "syntheticUser", "syntheticSecret", "syntheticToken", "syntheticFragment" })
        {
            JsonSerializer.Serialize(model.Snapshot).Should().NotContain(secret);
            model.CopyText.Should().NotContain(secret);
        }
    }

    [Theory]
    [InlineData("Episode 2: Context")]
    [InlineData("Episode 2: Why? #Context")]
    [InlineData("A film — director's cut")]
    [InlineData("C:\\media\\a movie.mp4")]
    [InlineData("/tmp/a movie.mp4")]
    public void Debug_preserves_authored_titles_and_local_paths(string title)
    {
        PlayerDebugViewModel model = new(new("Playing", title, "", "", "", false, "", "", "", "", ""));
        model.Snapshot.MediaTitle.Should().Be(title);
        model.CopyText.Should().Contain("Title: " + title);
    }

    [Fact]
    public void Clone_and_dotnet_restart_preserve_the_original_media_as_one_argument()
    {
        const string source = "https://example.org/a movie?x=1&y=тест";
        ApplicationLaunchContext apphost = new("/opt/llplayer/LLPlayer.Avalonia", "/opt/llplayer", RetainedOptions: ["--ffmpeg-dir", "/some ffmpeg"]);
        ProcessStartInfo fresh = ApplicationProcessPlanner.Create(ApplicationLaunchKind.New, apphost, source);
        fresh.ArgumentList.Should().Equal("--ffmpeg-dir", "/some ffmpeg");
        fresh.UseShellExecute.Should().BeFalse();
        fresh.WorkingDirectory.Should().Be("/opt/llplayer");
        ProcessStartInfo clone = ApplicationProcessPlanner.Create(ApplicationLaunchKind.Clone, apphost, source);
        clone.ArgumentList.Should().Equal("--ffmpeg-dir", "/some ffmpeg", "--", source);
        ApplicationLaunchContext dotnet = new("/usr/bin/dotnet", "/opt/llplayer", "/opt/llplayer/LLPlayer.Avalonia.dll");
        ApplicationProcessPlanner.Create(ApplicationLaunchKind.Restart, dotnet, source).ArgumentList
            .Should().Equal("/opt/llplayer/LLPlayer.Avalonia.dll", "--", source);
    }

    [Fact]
    public async Task Failed_restart_spawn_never_requests_exit_and_success_drains_after_spawn()
    {
        List<string> events = [];
        ApplicationLaunchContext context = new("player", ".");
        ApplicationProcessService failed = new(_ => { events.Add("failed-spawn"); return false; }, () => { events.Add("exit"); return Task.CompletedTask; });
        (await failed.LaunchAsync(ApplicationLaunchKind.Restart, context, "movie")).Should().BeFalse();
        events.Should().Equal("failed-spawn");
        ApplicationProcessService success = new(_ => { events.Add("spawn"); return true; }, () => { events.Add("drain"); return Task.CompletedTask; });
        (await success.LaunchAsync(ApplicationLaunchKind.Restart, context, "movie")).Should().BeTrue();
        events.Should().Equal("failed-spawn", "spawn", "drain");
    }
}
