using System.Text;
using LLPlayer.Avalonia.Services;
using LLPlayer.Avalonia.ViewModels;

namespace LLPlayer.Avalonia.Tests;

public class OwnedSubtitleLoadTests
{
    [Fact]
    public async Task Primary_load_is_distinct_from_save_uses_latest_BOM_and_transfers_language_and_owned_file()
    {
        string root = TemporaryRoot();
        DownloadClient client = new(); OwnedSubtitleFile? adopted = null; bool bom = false; int pickerCalls = 0;
        await using OpenSubtitlesDownloadViewModel model = new(client, useUtf8Bom: () => bom,
            capturePrimaryLoadTarget: () => async (owned, language, cancellation) =>
            {
                adopted = owned; Assert.Equal("ja", language);
                Assert.True(File.Exists(owned.FilePath));
                byte[] bytes = await File.ReadAllBytesAsync(owned.FilePath, cancellation);
                Assert.True(bytes.AsSpan().StartsWith(Encoding.UTF8.GetPreamble()));
                var stream = FlyleafPlaybackController.CreateDownloadedSubtitleStream(owned, language);
                Assert.Equal("ja", stream.Language.ISO6391); Assert.True(stream.ManualDownloaded);
                Assert.True(stream.Downloaded); Assert.False(stream.IsBitmap);
                return true;
            }, temporaryRoot: root)
        { ApiKey = "key", Username = "user", Password = "password", Query = "movie" };
        model.PickSavePath = _ => { pickerCalls++; return Task.FromResult<string?>(null); };
        try
        {
            Assert.False(Directory.Exists(root));
            await model.SearchAsync(); model.SelectedResult = Assert.Single(model.Results); bom = true;
            await model.LoadPrimaryAsync();
            Assert.Equal(0, pickerCalls); Assert.Equal(1, client.Downloads); Assert.NotNull(adopted);
            Assert.True(File.Exists(adopted.FilePath));
            Assert.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, adopted.FilePath);
            Assert.Equal("safe.srt", Path.GetFileName(adopted.FilePath));
            await model.DisposeAsync();
            Assert.True(File.Exists(adopted.FilePath)); // The native owner can outlive the dialog.
        }
        finally
        {
            if (adopted != null) Assert.True(adopted.TryDelete(out string? error), error);
            if (Directory.Exists(root)) Directory.Delete(root);
        }
    }

    [Fact]
    public async Task Stale_primary_load_target_rejects_the_result_and_removes_only_its_unique_child()
    {
        string root = TemporaryRoot(); OwnedSubtitleFile? rejected = null;
        await using OpenSubtitlesDownloadViewModel model = new(new DownloadClient(),
            capturePrimaryLoadTarget: () => (file, _, _) => { rejected = file; return Task.FromResult(false); }, temporaryRoot: root)
        { ApiKey = "key", Username = "user", Password = "password", Query = "movie" };
        try
        {
            await model.SearchAsync(); model.SelectedResult = Assert.Single(model.Results);
            await model.LoadPrimaryAsync();
            Assert.NotNull(rejected); Assert.False(File.Exists(rejected.FilePath));
            Assert.Empty(Directory.GetFileSystemEntries(root)); Assert.Contains("media changed", model.Message);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root); }
    }

    [Fact]
    public async Task Each_primary_load_captures_a_fresh_target_before_download_and_rejects_a_replaced_media_generation()
    {
        string root = TemporaryRoot(); int generation = 1, captures = 0;
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        DownloadClient client = new()
        {
            BeforeReturn = async cancellation =>
            {
                entered.TrySetResult(); await release.Task.WaitAsync(cancellation);
            },
        };
        List<OwnedSubtitleFile> accepted = [];
        await using OpenSubtitlesDownloadViewModel model = new(client,
            capturePrimaryLoadTarget: () =>
            {
                int captured = generation; captures++;
                return (file, _, _) =>
                {
                    bool current = captured == generation;
                    if (current) accepted.Add(file);
                    return Task.FromResult(current);
                };
            }, temporaryRoot: root)
        { ApiKey = "key", Username = "user", Password = "password", Query = "movie" };
        try
        {
            await model.SearchAsync(); model.SelectedResult = Assert.Single(model.Results);
            Task first = model.LoadPrimaryAsync(); await entered.Task.WaitAsync(TestContext.Current.CancellationToken);
            Assert.Equal(1, captures);
            generation = 2; release.SetResult(); await first;
            Assert.Empty(accepted); Assert.Empty(Directory.GetFileSystemEntries(root));
            await model.LoadPrimaryAsync();
            Assert.Equal(2, captures); Assert.Equal(2, client.Downloads);
            Assert.True(File.Exists(Assert.Single(accepted).FilePath));
        }
        finally
        {
            foreach (OwnedSubtitleFile owned in accepted) Assert.True(owned.TryDelete(out string? error), error);
            if (Directory.Exists(root)) Directory.Delete(root);
        }
    }

    [Fact]
    public async Task Owned_cleanup_preserves_an_unrelated_child_and_reports_why_the_directory_was_retained()
    {
        string root = TemporaryRoot();
        OwnedSubtitleFile owned = await OwnedSubtitleFile.CreateAsync("../../safe.srt", [0x31], false, TestContext.Current.CancellationToken, root);
        string child = Path.GetDirectoryName(owned.FilePath)!;
        string unrelated = Path.Combine(child, "unrelated.txt");
        try
        {
            await File.WriteAllTextAsync(unrelated, "preserve", TestContext.Current.CancellationToken);
            Assert.False(owned.TryDelete(out string? error)); Assert.Contains(owned.FilePath, error);
            Assert.True(File.Exists(unrelated)); Assert.False(File.Exists(owned.FilePath));
        }
        finally
        {
            if (File.Exists(unrelated)) File.Delete(unrelated);
            Assert.True(owned.TryDelete(out string? error), error);
            Directory.Delete(root);
        }
    }

    [Fact]
    public async Task Closing_during_primary_download_cancels_before_any_temp_child_is_created()
    {
        string root = TemporaryRoot(); DownloadClient client = new() { Block = true };
        OpenSubtitlesDownloadViewModel model = new(client,
            capturePrimaryLoadTarget: () => (_, _, _) => throw new Xunit.Sdk.XunitException("A cancelled download cannot load."), temporaryRoot: root)
        { ApiKey = "key", Username = "user", Password = "password", Query = "movie" };
        await model.SearchAsync(); model.SelectedResult = Assert.Single(model.Results);
        Task load = model.LoadPrimaryAsync(); await client.Started.Task;
        await model.DisposeAsync(); await load;
        Assert.True(client.Canceled); Assert.False(Directory.Exists(root));
    }

    [Fact]
    public async Task Busy_primary_target_admission_is_visible_spends_no_quota_and_does_not_block_closing()
    {
        string root = TemporaryRoot(); DownloadClient client = new();
        OpenSubtitlesDownloadViewModel model = new(client,
            capturePrimaryLoadTarget: () => throw new InvalidOperationException("Wait for subtitle work to finish before loading subtitles."), temporaryRoot: root)
        { ApiKey = "key", Username = "user", Password = "password", Query = "movie" };
        await model.SearchAsync(); model.SelectedResult = Assert.Single(model.Results);
        await model.LoadPrimaryAsync();
        Assert.Equal(0, client.Downloads); Assert.Contains("Wait for subtitle work", model.Message);
        Assert.False(Directory.Exists(root)); Assert.False(model.Busy);
        ValueTask closed = model.DisposeAsync();
        Assert.True(closed.IsCompletedSuccessfully);
        await closed;
    }

    static string TemporaryRoot() => Path.Combine(Path.GetTempPath(), "llplayer-owned-subtitle-" + Guid.NewGuid().ToString("N"));
    sealed class DownloadClient : IOpenSubtitlesClient
    {
        public int Downloads; public bool Block, Canceled;
        public Func<CancellationToken, Task>? BeforeReturn;
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<OpenSubtitlesSearchPage> SearchAsync(OpenSubtitlesCredentials credentials, string query, string language = "", int page = 1, CancellationToken cancellationToken = default)
            => Task.FromResult(new OpenSubtitlesSearchPage([new(42, "safe.srt", "ja", "Movie", 1, 0)], 1, 1));
        public async Task<OpenSubtitlesDownload> DownloadAsync(OpenSubtitlesCredentials credentials, OpenSubtitlesResult result, CancellationToken cancellationToken = default)
        {
            Downloads++; Started.TrySetResult();
            if (Block)
                try { await Task.Delay(Timeout.Infinite, cancellationToken); }
                catch (OperationCanceledException) { Canceled = true; throw; }
            if (BeforeReturn != null) await BeforeReturn(cancellationToken);
            return new("../../safe.srt", Encoding.UTF8.GetBytes("1\n00:00:00,000 --> 00:00:01,000\n日本語\n"));
        }
        public void Dispose() { }
    }
}
