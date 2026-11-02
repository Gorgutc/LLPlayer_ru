using System.Net;
using System.Net.Http;
using LLPlayer.Core.Services.Models;

namespace LLPlayer.Avalonia.Tests;

public sealed class ModelManagementServiceTests : IDisposable
{
    readonly string root = Path.Combine(Path.GetTempPath(), "llplayer-model-management-" + Guid.NewGuid().ToString("N"));
    readonly List<HttpClient> clients = [];
    ModelStoragePaths Paths => new(Path.Combine(root, "whisper"), Path.Combine(root, "tessdata"), Path.Combine(root, "engines"));

    ModelManagementService Create(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? send = null,
        Func<ModelEntry, bool>? inUse = null, Func<ModelEntry, Func<Task>, CancellationToken, Task>? mutate = null,
        IEngineArchiveExtractor? extractor = null, bool windows = false)
    {
        HttpClient client = new(new Handler(send ?? ((_, _) => Task.FromResult(Response([1, 2, 3])))));
        clients.Add(client);
        return new(client, Paths, inUse, mutate, extractor, windows);
    }
    static HttpResponseMessage Response(byte[] bytes) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
    static ModelEntry First(IModelManagementService service) => service.Discover(ModelKind.Tesseract).First();

    [Fact]
    public void Discover_is_read_only_and_contains_quantized_whisper_and_tesseract_catalogs()
    {
        var service = Create((_, _) => throw new InvalidOperationException("Discovery requested network"));
        var whisper = service.Discover(ModelKind.Whisper);
        Assert.Contains(whisper, row => row.FileName.Contains("q5_0", StringComparison.Ordinal));
        Assert.Contains(whisper, row => row.FileName == "ggml-base.bin");
        Assert.NotEmpty(service.Discover(ModelKind.Tesseract));
        Assert.All(whisper, row => Assert.False(row.Downloaded));
        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public async Task Download_publishes_bytes_reports_progress_and_refreshes_status()
    {
        var service = Create();
        var row = First(service);
        List<ModelDownloadProgress> progress = [];
        await service.DownloadAsync(row.Id, new InlineProgress(progress.Add), TestContext.Current.CancellationToken);
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(row.FullPath));
        Assert.True(service.Discover(row.Kind).Single(x => x.Id == row.Id).Downloaded);
        Assert.Contains(progress, item => item.BytesReceived == 3);
        Assert.Single(Directory.GetFiles(row.Directory));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Download_never_overwrites_an_existing_destination_even_when_empty(bool empty)
    {
        int requests = 0;
        var service = Create((_, _) => { requests++; return Task.FromResult(Response([1])); });
        var row = First(service);
        Directory.CreateDirectory(row.Directory);
        byte[] original = empty ? [] : [7, 8];
        File.WriteAllBytes(row.FullPath, original);
        await Assert.ThrowsAsync<IOException>(() => service.DownloadAsync(row.Id, null, TestContext.Current.CancellationToken));
        Assert.Equal(original, File.ReadAllBytes(row.FullPath));
        Assert.Equal(0, requests);
    }

    [Fact]
    public async Task Missing_remote_model_preserves_404_and_removes_owned_partial()
    {
        var service = Create((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)));
        var row = First(service);
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => service.DownloadAsync(row.Id, null, TestContext.Current.CancellationToken));
        Assert.Equal(HttpStatusCode.NotFound, error.StatusCode);
        Assert.False(File.Exists(row.FullPath));
        Assert.Empty(Directory.GetFiles(row.Directory));
    }

    [Fact]
    public async Task Provider_timeout_is_failure_and_explicit_cancel_remains_cancellation()
    {
        var timed = Create((_, _) => throw new TaskCanceledException("HTTP timeout"));
        await Assert.ThrowsAsync<TimeoutException>(() => timed.DownloadAsync(First(timed).Id, null, TestContext.Current.CancellationToken));
        using CancellationTokenSource cts = new();
        var cancelled = Create((_, _) => { cts.Cancel(); throw new OperationCanceledException(cts.Token); });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled.DownloadAsync(First(cancelled).Id, null, cts.Token));
        Assert.Empty(Directory.GetFiles(Paths.TesseractModels));
    }

    [Fact]
    public async Task Empty_and_truncated_content_are_rejected_without_final_files()
    {
        foreach (bool empty in new[] { true, false })
        {
            var service = Create((_, _) =>
            {
                var response = Response(empty ? [] : [1]);
                if (!empty) response.Content.Headers.ContentLength = 100;
                return Task.FromResult(response);
            });
            var row = First(service);
            await Assert.ThrowsAsync<IOException>(() => service.DownloadAsync(row.Id, null, TestContext.Current.CancellationToken));
            Assert.False(File.Exists(row.FullPath));
            Assert.Empty(Directory.GetFiles(row.Directory));
        }
    }

    [Fact]
    public async Task Final_promotion_and_delete_use_idle_admission_and_recheck_in_use_inside_it()
    {
        bool held = false, busy = false;
        int admissions = 0;
        var service = Create(inUse: _ => { Assert.True(held); return busy; }, mutate: async (_, action, _) =>
        {
            admissions++;
            held = true;
            try { await action(); } finally { held = false; }
        });
        var row = First(service);
        await service.DownloadAsync(row.Id, null, TestContext.Current.CancellationToken);
        busy = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteAsync(row.Id, TestContext.Current.CancellationToken));
        Assert.True(File.Exists(row.FullPath));
        busy = false;
        await service.DeleteAsync(row.Id, TestContext.Current.CancellationToken);
        Assert.False(File.Exists(row.FullPath));
        Assert.Equal(3, admissions);
    }

    [Fact]
    public async Task Destination_created_during_network_wait_is_preserved()
    {
        ModelEntry? row = null;
        var service = Create((_, _) => { File.WriteAllBytes(row!.FullPath, [9]); return Task.FromResult(Response([1])); });
        row = First(service);
        await Assert.ThrowsAsync<IOException>(() => service.DownloadAsync(row.Id, null, TestContext.Current.CancellationToken));
        Assert.Equal(new byte[] { 9 }, File.ReadAllBytes(row.FullPath));
        Assert.Single(Directory.GetFiles(row.Directory));
    }

    [Fact]
    public async Task Linux_engine_is_explicitly_unavailable_and_never_requests_network()
    {
        var service = Create((_, _) => throw new InvalidOperationException("Unexpected network"));
        var row = Assert.Single(service.Discover(ModelKind.FasterWhisperEngine));
        Assert.False(row.Available);
        Assert.Contains("Windows", row.UnavailableReason);
        await Assert.ThrowsAsync<PlatformNotSupportedException>(() => service.DownloadAsync(row.Id, null, TestContext.Current.CancellationToken));
        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public async Task Engine_install_and_remove_are_staged_and_foreign_changes_block_delete()
    {
        var service = Create(extractor: new EngineExtractor(), windows: true);
        var row = Assert.Single(service.Discover(ModelKind.FasterWhisperEngine));
        await service.DownloadAsync(row.Id, null, TestContext.Current.CancellationToken);
        Assert.True(File.Exists(Path.Combine(row.FullPath, "faster-whisper-xxl.exe")));
        string foreign = Path.Combine(row.FullPath, "user.txt");
        File.WriteAllText(foreign, "preserve");
        await Assert.ThrowsAsync<IOException>(() => service.DeleteAsync(row.Id, TestContext.Current.CancellationToken));
        Assert.Equal("preserve", File.ReadAllText(foreign));
        File.Delete(foreign);
        await service.DeleteAsync(row.Id, TestContext.Current.CancellationToken);
        Assert.False(Directory.Exists(row.FullPath));
        Assert.Empty(Directory.GetFileSystemEntries(row.Directory));
    }

    [Fact]
    public async Task External_engine_install_is_never_recursively_removed()
    {
        var service = Create(extractor: new EngineExtractor(), windows: true);
        var row = Assert.Single(service.Discover(ModelKind.FasterWhisperEngine));
        Directory.CreateDirectory(row.FullPath);
        File.WriteAllText(Path.Combine(row.FullPath, "faster-whisper-xxl.exe"), "user");
        await Assert.ThrowsAsync<IOException>(() => service.DeleteAsync(row.Id, TestContext.Current.CancellationToken));
        Assert.Equal("user", File.ReadAllText(Path.Combine(row.FullPath, "faster-whisper-xxl.exe")));
    }

    [Fact]
    public async Task A_second_download_cannot_share_the_first_operations_owned_files()
    {
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<HttpResponseMessage> release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = Create((_, _) => { started.SetResult(); return release.Task; });
        var row = First(service);
        Task first = service.DownloadAsync(row.Id, null, TestContext.Current.CancellationToken);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.DownloadAsync(row.Id, null, TestContext.Current.CancellationToken));
        }
        finally
        {
            release.TrySetResult(Response([1, 2, 3]));
            await first;
        }
        Assert.Single(Directory.GetFiles(row.Directory));
    }

    [Fact]
    public async Task Busy_at_final_admission_prevents_publication_and_cleans_partial()
    {
        var service = Create(inUse: _ => true);
        var row = First(service);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DownloadAsync(row.Id, null, TestContext.Current.CancellationToken));
        Assert.False(File.Exists(row.FullPath));
        Assert.Empty(Directory.GetFiles(row.Directory));
    }

    [Fact]
    public async Task User_edited_managed_engine_file_is_preserved()
    {
        var service = Create(extractor: new EngineExtractor(), windows: true);
        var row = Assert.Single(service.Discover(ModelKind.FasterWhisperEngine));
        await service.DownloadAsync(row.Id, null, TestContext.Current.CancellationToken);
        string executable = Path.Combine(row.FullPath, "faster-whisper-xxl.exe");
        File.WriteAllBytes(executable, [99, 88]);
        await Assert.ThrowsAsync<IOException>(() => service.DeleteAsync(row.Id, TestContext.Current.CancellationToken));
        Assert.Equal(new byte[] { 99, 88 }, File.ReadAllBytes(executable));
    }

    [Fact]
    public async Task Foreign_empty_engine_directory_is_preserved()
    {
        var service = Create(extractor: new EngineExtractor(), windows: true);
        var row = Assert.Single(service.Discover(ModelKind.FasterWhisperEngine));
        await service.DownloadAsync(row.Id, null, TestContext.Current.CancellationToken);
        string foreign = Path.Combine(row.FullPath, "owner-empty-folder");
        Directory.CreateDirectory(foreign);
        await Assert.ThrowsAsync<IOException>(() => service.DeleteAsync(row.Id, TestContext.Current.CancellationToken));
        Assert.True(Directory.Exists(foreign));
    }

    public void Dispose()
    {
        foreach (var client in clients) client.Dispose();
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
    sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }
    sealed class InlineProgress(Action<ModelDownloadProgress> report) : IProgress<ModelDownloadProgress>
    {
        public void Report(ModelDownloadProgress value) => report(value);
    }
    sealed class EngineExtractor : IEngineArchiveExtractor
    {
        public Task ExtractAsync(string archivePath, string destinationDirectory, CancellationToken cancellationToken)
        {
            string engine = Path.Combine(destinationDirectory, "Faster-Whisper-XXL");
            Directory.CreateDirectory(engine);
            File.WriteAllBytes(Path.Combine(engine, "faster-whisper-xxl.exe"), [1, 2]);
            File.WriteAllText(Path.Combine(destinationDirectory, "license.txt"), "license");
            return Task.CompletedTask;
        }
    }
}
