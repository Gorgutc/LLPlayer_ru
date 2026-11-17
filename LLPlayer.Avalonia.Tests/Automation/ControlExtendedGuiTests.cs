using System.Text;
using System.Text.Json;
using Avalonia.Headless.XUnit;
using LLPlayer.Avalonia.Services;
using LLPlayer.Avalonia.Services.Automation;
using LLPlayer.Avalonia.ViewModels;
using LLPlayer.Avalonia.Views;
using LLPlayer.ControlApi;
using LLPlayer.Core.Services;
using Xunit;

namespace LLPlayer.Avalonia.Tests.Automation;

[Collection(FlyleafGlobalsCollection.Name)]
public sealed class ControlExtendedGuiTests
{
    [Fact]
    public async Task SubtitleDownload_DoesNotReplaceExistingDestinationWithoutExplicitOverwrite()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        DirectoryInfo folder = Directory.CreateTempSubdirectory("llplayer-control-subtitles-");
        string destination = Path.Combine(folder.FullName, "existing.srt");
        try
        {
            await File.WriteAllTextAsync(destination, "existing", token);
            await Assert.ThrowsAsync<IOException>(() => OpenSubtitlesDownloadViewModel.SaveAsync(
                destination, Encoding.UTF8.GetBytes("downloaded"), bom: true, token, overwrite: false));

            Assert.Equal("existing", await File.ReadAllTextAsync(destination, token));
            Assert.Single(folder.GetFiles());
        }
        finally
        {
            File.Delete(destination);
            folder.Delete();
        }
    }

    [Fact]
    public void SubtitleProviderCredentialsOnlyAppearInImmediateSearchArguments()
    {
        var credentials = new ControlOpenSubtitleCredentials("example-api-key", "example-user", "example-password");
        var search = new ControlOpenSubtitleSearch(credentials, "Example Movie");
        Assert.DoesNotContain(credentials.ApiKey, search.ToString());
        Assert.DoesNotContain(credentials.Username, search.ToString());
        Assert.DoesNotContain(credentials.Password, search.ToString());

        var operations = ControlExtendedGuiOperations.All.Where(entry => entry.Operation.Family == "subtitle_download");
        var searchEntry = Assert.Single(operations, entry => entry.Arguments == typeof(ControlOpenSubtitleSearch));
        Assert.False(searchEntry.Operation.Mutates);
        Assert.Equal(ControlOperationCompletion.Immediate, searchEntry.Operation.Completion);
        Assert.All(operations.Where(entry => entry.Operation.Mutates), entry =>
            Assert.DoesNotContain(entry.Arguments.GetProperties(), property => property.PropertyType == typeof(ControlOpenSubtitleCredentials)));
    }

    [AvaloniaFact]
    public async Task SubtitleSearchUsesStableLiveCuesAndRejectsStaleContinuation()
    {
        string? media = Environment.GetEnvironmentVariable("LLPLAYER_TEST_MEDIA");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(media), "Requires the canonical local video fixture.");
        using TempDir state = new();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        CancellationToken token = deadline.Token;
        using var player = PlaybackControlOpenTests.CreatePlayer(state, out AvaloniaUIDispatcher dispatcher, out CollectionSyncBridge bridge);
        await using var controller = new FlyleafPlaybackController(player, dispatcher, bridge);
        var window = new MainWindow(new KeyMapper(), new AvaloniaHostServices(() => null));
        await using var translator = new FlyleafWordTranslator(player, new TranslationSettings());
        using var model = new MainWindowViewModel(controller, new AppPrefs(), _ => { }, translator, window);
        var executor = new ControlGuiExecutor(controller, model, window);
        string subtitle = Path.Combine(state.Path, "search.srt");
        await File.WriteAllTextAsync(subtitle, """
            1
            00:00:00,100 --> 00:00:00,450
            One fox.

            2
            00:00:00,500 --> 00:00:00,850
            Two foxes.

            3
            00:00:00,900 --> 00:00:01,200
            Another fox.
            """, token);
        await controller.OpenControlMediaAsync(media!, false, token);
        await controller.OpenControlSubtitleAsync(subtitle, 0, token);
        while (controller.GetCues(0).Count < 3)
            await Task.Delay(25, token);

        var request = new ControlSubtitleSearchRequest(0, "fox", WholeWord: true, Limit: 1);
        ControlResult first = await SearchAsync(request);
        Assert.Null(first.Error);
        var page = first.Value!.Value.Deserialize<ControlGuiSubtitlePage>(ControlProtocol.Json)!;
        Assert.Equal(2, page.TotalCount);
        Assert.Equal(1, page.NextCursor);
        Assert.Equal("One fox.", Assert.Single(page.Cues).Original);

        ControlResult next = await SearchAsync(request with { Cursor = 1, Revision = page.Revision });
        Assert.Null(next.Error);
        var continuation = next.Value!.Value.Deserialize<ControlGuiSubtitlePage>(ControlProtocol.Json)!;
        Assert.Equal("Another fox.", Assert.Single(continuation.Cues).Original);
        Assert.Null(continuation.NextCursor);
        Assert.Equal("stale_revision", (await SearchAsync(request with { Cursor = 1, Revision = Guid.NewGuid() })).Error?.Code);
        Assert.Equal("invalid_search_pattern", (await SearchAsync(request with { Query = "(", Regex = true })).Error?.Code);

        Task<ControlResult> SearchAsync(ControlSubtitleSearchRequest args)
            => executor.ExecuteAsync(new(Guid.NewGuid(), "subtitles.search", new(ControlProtocol.DefaultProfileId),
                null, ControlProtocol.Encode(args)), token);
    }

    [AvaloniaFact]
    public async Task ViewportAndColorChangesUseTheCurrentNativeVideo()
    {
        string? media = Environment.GetEnvironmentVariable("LLPLAYER_TEST_MEDIA");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(media), "Requires the canonical local video fixture.");
        using TempDir state = new();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        CancellationToken token = deadline.Token;
        using var player = PlaybackControlOpenTests.CreatePlayer(state, out AvaloniaUIDispatcher dispatcher, out CollectionSyncBridge bridge);
        await using var controller = new FlyleafPlaybackController(player, dispatcher, bridge);
        var window = new MainWindow(new KeyMapper(), new AvaloniaHostServices(() => null));
        await using var translator = new FlyleafWordTranslator(player, new TranslationSettings());
        using var model = new MainWindowViewModel(controller, new AppPrefs(), _ => { }, translator, window);
        var executor = new ControlGuiExecutor(controller, model, window);
        await controller.OpenControlMediaAsync(media!, false, token);

        ControlResult initial = await ExecuteAsync("viewport.get", new ControlGuiOperations.Empty(), null);
        Assert.Null(initial.Error);
        long revision = Assert.IsType<long>(initial.Revision);
        var requested = new ControlViewportUpdate(ZoomPercent: 125, PanX: -0.125, Rotation: 90,
            HorizontalFlip: true, Crop: new(1, 2, 3, 4), AspectRatio: "Fill");

        ControlResult update = await ExecuteAsync("viewport.set", requested, revision);
        var viewport = Read<ControlViewportSnapshot>(update);
        Assert.Equal(new ControlViewportSnapshot(125, -0.125, 0, 90, true, false, new(1, 2, 3, 4), "Fill"), viewport);
        Assert.Equal(125, player.Config.Video.Zoom);
        Assert.Equal("invalid_arguments", (await ExecuteAsync("viewport.set", requested with { ZoomPercent = 150, Rotation = 45 }, revision)).Error?.Code);
        Assert.Equal("stale_revision", (await ExecuteAsync("viewport.set", requested with { ZoomPercent = 150 }, revision - 1)).Error?.Code);
        Assert.Equal(viewport, Read<ControlViewportSnapshot>(await ExecuteAsync("viewport.get", new ControlGuiOperations.Empty(), revision)));

        ControlResult color = await ExecuteAsync("viewport.filters.set", new ControlColorFilterUpdate(Brightness: 15, Saturation: -20), revision);
        var observed = Read<ControlColorFilterSnapshot>(color);
        Assert.Equal(15, observed.Brightness);
        Assert.Equal(-20, observed.Saturation);
        Assert.Equal("invalid_arguments", (await ExecuteAsync("viewport.filters.set", new ControlColorFilterUpdate(Brightness: 25, Hue: 181), revision)).Error?.Code);
        Assert.Equal(observed, Read<ControlColorFilterSnapshot>(await ExecuteAsync("viewport.filters.get", new ControlGuiOperations.Empty(), revision)));

        Task<ControlResult> ExecuteAsync(string operation, object arguments, long? expected)
            => executor.ExecuteAsync(new(Guid.NewGuid(), operation, new(ControlProtocol.DefaultProfileId),
                expected, ControlProtocol.Encode(arguments)), token);

        static T Read<T>(ControlResult result)
        {
            Assert.Null(result.Error);
            return result.Value!.Value.Deserialize<T>(ControlProtocol.Json)!;
        }
    }
}
