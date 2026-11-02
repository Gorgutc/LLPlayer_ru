using System.Text;
using LLPlayer.Avalonia.Services;
using LLPlayer.Avalonia.ViewModels;

namespace LLPlayer.Avalonia.Tests;

public class OpenSubtitlesDownloadTests
{
    [Fact]
    public async Task Cancelled_picker_spends_no_download_and_close_clears_credentials_and_disposes_client()
    {
        FakeClient client = new();
        OpenSubtitlesDownloadViewModel model = new(client) { ApiKey = "key", Username = "user", Password = "password", Query = "title" };
        Assert.Equal(0, client.SearchCalls);
        await model.SearchAsync(); model.SelectedResult = Assert.Single(model.Results);
        model.PickSavePath = _ => Task.FromResult<string?>(null);
        await model.DownloadAsync();
        Assert.Equal(0, client.DownloadCalls);
        await model.DisposeAsync();
        Assert.True(client.Disposed); Assert.Equal("", model.Password); Assert.Equal("", model.ApiKey);
    }

    [Fact]
    public async Task Download_saves_to_the_picked_path_with_the_current_BOM_without_invoking_primary_load()
    {
        string folder = Path.Combine(Path.GetTempPath(), "llplayer-opensubtitles-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "saved.srt");
        FakeClient client = new(); bool bom = false; int loads = 0;
        await using OpenSubtitlesDownloadViewModel model = new(client, useUtf8Bom: () => bom,
            capturePrimaryLoadTarget: () => { loads++; return (_, _, _) => Task.FromResult(true); })
        { ApiKey = "key", Username = "user", Password = "password", Query = "title" };
        try
        {
            await model.SearchAsync(); model.SelectedResult = Assert.Single(model.Results);
            model.PickSavePath = _ => Task.FromResult<string?>(path);
            bom = true;
            await model.DownloadAsync();
            byte[] bytes = await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken);
            Assert.True(bytes.AsSpan().StartsWith(Encoding.UTF8.GetPreamble()));
            Assert.Equal(0, loads); Assert.Contains("Subtitle saved", model.Message);
            Assert.Single(Directory.GetFiles(folder));
        }
        finally { if (File.Exists(path)) File.Delete(path); Directory.Delete(folder); }
    }

    [Fact]
    public async Task Closing_cancels_and_drains_the_inflight_search_before_disposing_the_provider()
    {
        FakeClient client = new() { Block = true };
        OpenSubtitlesDownloadViewModel model = new(client) { ApiKey = "key", Username = "user", Password = "password", Query = "title" };
        Task search = model.SearchAsync(); await client.Started.Task;
        Assert.True(model.Busy); await model.DisposeAsync(); await search;
        Assert.True(client.CancelObserved); Assert.True(client.Disposed); Assert.Empty(model.Results);
    }

    sealed class FakeClient : IOpenSubtitlesClient
    {
        public int SearchCalls, DownloadCalls;
        public bool Block, Disposed, CancelObserved;
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<OpenSubtitlesSearchPage> SearchAsync(OpenSubtitlesCredentials credentials, string query, string language = "", int page = 1, CancellationToken cancellationToken = default)
        {
            SearchCalls++; Started.TrySetResult();
            if (Block)
            {
                try { await Task.Delay(Timeout.Infinite, cancellationToken); }
                catch (OperationCanceledException) { CancelObserved = true; throw; }
            }
            return new([new(42, "subtitle.srt", "en", "title", 0, 0)], 1, 1);
        }
        public Task<OpenSubtitlesDownload> DownloadAsync(OpenSubtitlesCredentials credentials, OpenSubtitlesResult result, CancellationToken cancellationToken = default)
        { DownloadCalls++; return Task.FromResult(new OpenSubtitlesDownload("subtitle.srt", Encoding.UTF8.GetBytes("subtitle"))); }
        public void Dispose() { Assert.False(Block && !CancelObserved); Disposed = true; }
    }
}
