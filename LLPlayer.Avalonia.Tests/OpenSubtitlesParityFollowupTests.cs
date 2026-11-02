using LLPlayer.Avalonia.Services;
using LLPlayer.Avalonia.ViewModels;

namespace LLPlayer.Avalonia.Tests;

public class OpenSubtitlesParityFollowupTests
{
    [Fact]
    public async Task Search_results_use_language_then_download_count_then_file_name_order()
    {
        await using OpenSubtitlesDownloadViewModel model = new(new ResultsClient())
        { ApiKey = "key", Username = "user", Password = "password", Query = "movie" };
        await model.SearchAsync();
        Assert.Equal(new[] { "a.srt", "b.srt", "less.srt", "fr.srt" }, model.Results.Select(row => row.FileName));
    }

    [Fact]
    public void Downloader_exposes_distinct_primary_Load_and_modern_result_metadata()
    {
        Assert.NotNull(typeof(OpenSubtitlesDownloadViewModel).GetProperty("LoadPrimaryCommand"));
        Assert.NotNull(typeof(OpenSubtitlesResult).GetProperty("Format"));
        Assert.NotNull(typeof(OpenSubtitlesResult).GetProperty("Duration"));
        Assert.NotNull(typeof(OpenSubtitlesResult).GetProperty("Year"));
    }

    [Fact]
    public async Task Media_title_changes_refresh_the_query_without_starting_a_search()
    {
        ResultsClient client = new();
        OpenSubtitlesDownloadViewModel model = new(client);
        FakePlaybackController player = new() { Title = "Original movie", IsOpened = true };
        var bind = typeof(OpenSubtitlesDownloadViewModel).GetMethod("BindMedia");
        Assert.NotNull(bind);
        bind.Invoke(model, [player]);
        Assert.Equal("Original movie", model.Query);
        player.Title = "Replacement movie";
        Assert.Equal("Replacement movie", model.Query);
        Assert.Equal(0, client.SearchCalls);
        await model.DisposeAsync();
    }

    [Fact]
    public async Task Configured_languages_are_preferred_and_closed_dialog_unsubscribes_media_title_updates()
    {
        FakePlaybackController player = new() { Title = "Original", IsOpened = true };
        OpenSubtitlesDownloadViewModel model = new(new ResultsClient(), media: player, preferredLanguages: () => ["fr"])
        { ApiKey = "key", Username = "user", Password = "password" };
        await model.SearchAsync();
        Assert.Equal("fr", model.Results[0].Language);
        await model.DisposeAsync(); player.Title = "Replacement";
        Assert.Equal("Original", model.Query);
    }

    sealed class ResultsClient : IOpenSubtitlesClient
    {
        public int SearchCalls;
        public Task<OpenSubtitlesSearchPage> SearchAsync(OpenSubtitlesCredentials credentials, string query, string language = "", int page = 1, CancellationToken cancellationToken = default)
        {
            SearchCalls++;
            return Task.FromResult(new OpenSubtitlesSearchPage([
                new(1, "fr.srt", "fr", "", 100, 0), new(2, "less.srt", "en", "", 1, 0),
                new(3, "b.srt", "en", "", 10, 0), new(4, "a.srt", "en", "", 10, 0)], 1, 1));
        }
        public Task<OpenSubtitlesDownload> DownloadAsync(OpenSubtitlesCredentials credentials, OpenSubtitlesResult result, CancellationToken cancellationToken = default)
            => Task.FromResult(new OpenSubtitlesDownload(result.FileName, [0x31]));
        public void Dispose() { }
    }
}
