using System.Net;
using System.Net.Http;
using LLPlayer.Core.Services.Models;

namespace FlyleafLibTests.Core;

public sealed class ModelEngineRemovalTests : IDisposable
{
    readonly string root = Path.Combine(Path.GetTempPath(), "llplayer-engine-removal-" + Guid.NewGuid().ToString("N"));
    readonly HttpClient client = new(new DownloadHandler());
    ModelManagementService Create() => new(client,
        new(Path.Combine(root, "models"), Path.Combine(root, "tessdata"), Path.Combine(root, "engines")),
        extractor: new Extractor(), isWindows: true);

    [Fact]
    public async Task Foreign_file_and_empty_directory_added_at_removal_boundary_are_restored_without_deletion()
    {
        var service = Create();
        var row = Assert.Single(service.Discover(ModelKind.FasterWhisperEngine));
        await service.DownloadAsync(row.Id, null, TestContext.Current.CancellationToken);
        service.EngineRemovalStaged = stage =>
        {
            Directory.CreateDirectory(Path.Combine(stage, "foreign-empty"));
            File.WriteAllText(Path.Combine(stage, "foreign.txt"), "preserve concurrent owner state");
        };
        var error = await Assert.ThrowsAsync<IOException>(() => service.DeleteAsync(row.Id, TestContext.Current.CancellationToken));
        Assert.Contains(row.FullPath, error.Message);
        Assert.True(Directory.Exists(Path.Combine(row.FullPath, "foreign-empty")));
        Assert.Equal("preserve concurrent owner state", File.ReadAllText(Path.Combine(row.FullPath, "foreign.txt")));
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(Path.Combine(row.FullPath, "faster-whisper-xxl.exe")));
        Assert.Empty(Directory.GetDirectories(row.Directory, ".llplayer-remove-*"));
    }

    [Fact]
    public async Task Cancellation_after_rename_preserves_full_tree_and_reports_actual_recovery_location()
    {
        var service = Create();
        var row = Assert.Single(service.Discover(ModelKind.FasterWhisperEngine));
        await service.DownloadAsync(row.Id, null, TestContext.Current.CancellationToken);
        using CancellationTokenSource cancel = new();
        service.EngineRemovalStaged = _ => cancel.Cancel();
        var error = await Assert.ThrowsAsync<IOException>(() => service.DeleteAsync(row.Id, cancel.Token));
        Assert.IsAssignableFrom<OperationCanceledException>(error.InnerException);
        Assert.Contains(row.FullPath, error.Message);
        Assert.True(File.Exists(Path.Combine(row.FullPath, "faster-whisper-xxl.exe")));
        Assert.Empty(Directory.GetDirectories(row.Directory, ".llplayer-remove-*"));
    }

    [Fact]
    public async Task Replacement_original_location_is_never_overwritten_when_restoring_changed_staging()
    {
        var service = Create();
        var row = Assert.Single(service.Discover(ModelKind.FasterWhisperEngine));
        await service.DownloadAsync(row.Id, null, TestContext.Current.CancellationToken);
        string? retained = null;
        service.EngineRemovalStaged = stage =>
        {
            retained = stage;
            File.WriteAllText(Path.Combine(stage, "foreign.txt"), "staged owner file");
            Directory.CreateDirectory(row.FullPath);
            File.WriteAllText(Path.Combine(row.FullPath, "replacement.txt"), "replacement owner file");
        };
        var error = await Assert.ThrowsAsync<IOException>(() => service.DeleteAsync(row.Id, TestContext.Current.CancellationToken));
        Assert.NotNull(retained);
        Assert.Contains(retained, error.Message);
        Assert.Equal("replacement owner file", File.ReadAllText(Path.Combine(row.FullPath, "replacement.txt")));
        Assert.Equal("staged owner file", File.ReadAllText(Path.Combine(retained, "foreign.txt")));
        Assert.True(File.Exists(Path.Combine(retained, "faster-whisper-xxl.exe")));
    }

    [Fact]
    public async Task Engine_file_edited_after_initial_snapshot_is_preserved()
    {
        var service = Create();
        var row = Assert.Single(service.Discover(ModelKind.FasterWhisperEngine));
        await service.DownloadAsync(row.Id, null, TestContext.Current.CancellationToken);
        service.EngineRemovalStaged = stage => File.WriteAllText(Path.Combine(stage, "faster-whisper-xxl.exe"), "owner edited file");
        await Assert.ThrowsAsync<IOException>(() => service.DeleteAsync(row.Id, TestContext.Current.CancellationToken));
        Assert.Equal("owner edited file", File.ReadAllText(Path.Combine(row.FullPath, "faster-whisper-xxl.exe")));
    }

    public void Dispose()
    {
        client.Dispose();
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
    sealed class DownloadHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) });
    }
    sealed class Extractor : IEngineArchiveExtractor
    {
        public Task ExtractAsync(string archivePath, string destinationDirectory, CancellationToken cancellationToken)
        {
            string engine = Path.Combine(destinationDirectory, ModelManagementService.EngineName);
            Directory.CreateDirectory(engine);
            File.WriteAllBytes(Path.Combine(engine, "faster-whisper-xxl.exe"), [1, 2, 3]);
            return Task.CompletedTask;
        }
    }
}
