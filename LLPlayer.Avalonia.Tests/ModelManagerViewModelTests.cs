using System.Net;
using System.Net.Http;
using LLPlayer.Avalonia.ViewModels;
using LLPlayer.Core.Services.Models;
using LLPlayer.Core.Services;

namespace LLPlayer.Avalonia.Tests;

public class ModelManagerViewModelTests
{
    [Fact]
    public async Task Engine_installation_adopts_the_existing_executable_so_recognition_draft_can_be_saved()
    {
        string directory = Path.Combine(Path.GetTempPath(), "llplayer-engine-adoption-" + Guid.NewGuid().ToString("N"));
        try
        {
            FakeModels service = new() { DirectoryPath = directory };
            var draft = new RecognitionSettings();
            ModelManagerViewModel vm = new(service, ModelKind.FasterWhisperEngine, _ => { },
                resourceDownloaded: entry => AppHost.AdoptDownloadedRecognitionResource(draft, entry));
            string executable = Path.Combine(vm.SelectedModel!.FullPath, "faster-whisper-xxl.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(executable)!);
            File.WriteAllBytes(executable, [1]);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserExecute);
            await vm.DownloadCommand.ExecuteAsync(null);
            Assert.Equal(executable, draft.FasterWhisper.ManualEnginePath);
            Assert.True(File.Exists(draft.FasterWhisper.ManualEnginePath));
            draft.Validate();
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData(ModelKind.Whisper)]
    [InlineData(ModelKind.Tesseract)]
    [InlineData(ModelKind.FasterWhisperEngine)]
    public async Task Only_successful_engine_installation_adopts_executable_and_other_operations_preserve_manual_choice(ModelKind kind)
    {
        FakeModels service = new();
        var draft = new RecognitionSettings();
        draft.FasterWhisper.ManualEnginePath = Path.Combine(Path.GetTempPath(), "owner-engine.exe");
        draft.FasterWhisper.UseManualEngine = false;
        string previous = draft.FasterWhisper.ManualEnginePath;
        int installed = 0;
        ModelManagerViewModel vm = new(service, kind, _ => { }, resourceDownloaded: entry =>
        {
            installed++;
            AppHost.AdoptDownloadedRecognitionResource(draft, entry);
        });
        string expected = Path.Combine(vm.SelectedModel!.FullPath, "faster-whisper-xxl.exe");
        await vm.DownloadCommand.ExecuteAsync(null);
        Assert.Equal(1, installed);
        Assert.Equal(kind == ModelKind.FasterWhisperEngine, draft.FasterWhisper.UseManualEngine);
        Assert.Equal(kind == ModelKind.FasterWhisperEngine ? expected : previous, draft.FasterWhisper.ManualEnginePath);
        string afterDownload = draft.FasterWhisper.ManualEnginePath;
        await vm.DeleteCommand.ExecuteAsync(null);
        Assert.Equal(1, installed);
        Assert.Equal(afterDownload, draft.FasterWhisper.ManualEnginePath);
    }

    [Fact]
    public async Task Download_refreshes_same_selection_and_parent_catalog_without_changing_selection()
    {
        FakeModels service = new();
        int changed = 0;
        ModelManagerViewModel vm = new(service, ModelKind.Whisper, _ => { }, () => changed++);
        vm.SelectedModel = vm.Models[1];
        await vm.DownloadCommand.ExecuteAsync(null);
        Assert.Equal("second", vm.SelectedModel?.Id);
        Assert.True(vm.SelectedModel?.Downloaded);
        Assert.Equal(1, changed);
        Assert.False(vm.Busy);
        Assert.False(vm.CanDownload);
        Assert.True(vm.CanDelete);
    }

    [Fact]
    public async Task Missing_quantization_is_actionable_and_timeout_is_not_reported_as_cancelled()
    {
        FakeModels service = new() { Failure = new HttpRequestException("404", null, HttpStatusCode.NotFound) };
        ModelManagerViewModel vm = new(service, ModelKind.Whisper, _ => { });
        await vm.DownloadCommand.ExecuteAsync(null);
        Assert.Contains("not available", vm.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("quantization", vm.Message, StringComparison.OrdinalIgnoreCase);
        service.Failure = new TimeoutException("Request timed out");
        await vm.DownloadCommand.ExecuteAsync(null);
        Assert.Contains("timed out", vm.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("canceled", vm.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Cancel_drains_owned_work_and_does_not_notify_parent_of_a_failed_download()
    {
        FakeModels service = new() { BlockDownload = true };
        int changed = 0;
        ModelManagerViewModel vm = new(service, ModelKind.Whisper, _ => { }, () => changed++);
        Task work = vm.DownloadCommand.ExecuteAsync(null);
        Assert.True(vm.Busy);
        vm.CancelCommand.Execute(null);
        await work;
        Assert.False(vm.Busy);
        Assert.Equal(0, changed);
        Assert.Contains("canceled", vm.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Open_folder_failures_are_visible_and_unavailable_engine_is_disabled()
    {
        FakeModels service = new() { Available = false };
        ModelManagerViewModel vm = new(service, ModelKind.FasterWhisperEngine, _ => throw new IOException("Shell failed"));
        Assert.False(vm.CanDownload);
        vm.OpenFolderCommand.Execute(null);
        Assert.Contains("Shell failed", vm.Message);
    }

    [Fact]
    public async Task Dispose_cancels_and_drains_work_then_retires_all_resource_actions()
    {
        FakeModels service = new() { BlockDownload = true };
        int opened = 0;
        ModelManagerViewModel vm = new(service, ModelKind.Whisper, _ => opened++);
        Task download = vm.DownloadCommand.ExecuteAsync(null);
        await vm.DisposeAsync();
        await download;
        Assert.False(vm.Busy);
        Assert.False(vm.CanDownload);
        Assert.False(vm.CanDelete);
        Assert.False(vm.RefreshCommand.CanExecute(null));
        vm.OpenFolderCommand.Execute(null);
        Assert.Equal(0, opened);
    }

    [Fact]
    public void Engine_source_is_explicit_and_progress_counters_keep_received_bytes_when_total_unknown()
    {
        Uri? opened = null;
        ModelManagerViewModel vm = new(new FakeModels(), ModelKind.FasterWhisperEngine, _ => { }, openSource: uri => opened = uri);
        Assert.Null(opened);
        Assert.Contains("r245.4", vm.ArtifactName);
        vm.OpenSourceCommand.Execute(null);
        Assert.Equal("github.com", opened?.Host);
        Assert.Contains("Purfview", opened?.AbsolutePath);
        vm.DownloadedBytes = 123;
        Assert.Contains("123", vm.ProgressText);
        Assert.Contains("unknown", vm.ProgressText);
        vm.TotalBytes = 456;
        Assert.Contains("456", vm.ProgressText);
        vm.SelectedKind = ModelKind.Whisper;
        Assert.False(vm.HasSource);
        Assert.False(vm.OpenSourceCommand.CanExecute(null));
    }

    sealed class FakeModels : IModelManagementService
    {
        bool downloaded;
        public string DirectoryPath { get; init; } = Path.GetTempPath();
        public bool Available { get; init; } = true;
        public bool BlockDownload { get; init; }
        public Exception? Failure { get; set; }
        public IReadOnlyList<ModelEntry> Discover(ModelKind kind) => new[] { "first", "second" }
            .Select(id => new ModelEntry(id, kind, id, id + ".bin", DirectoryPath, downloaded, downloaded ? 3 : 0, Available, Available ? null : "Windows only")).ToArray();
        public string GetDirectory(ModelKind kind) => DirectoryPath;
        public async Task DownloadAsync(string id, IProgress<ModelDownloadProgress>? progress, CancellationToken cancellationToken)
        {
            if (BlockDownload) await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            if (Failure != null) throw Failure;
            downloaded = true;
        }
        public Task DeleteAsync(string id, CancellationToken cancellationToken) { downloaded = false; return Task.CompletedTask; }
    }
}
