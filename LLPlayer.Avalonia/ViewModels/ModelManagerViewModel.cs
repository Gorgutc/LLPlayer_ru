using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LLPlayer.Core.Services.Models;
using System.Net;

namespace LLPlayer.Avalonia.ViewModels;

public partial class ModelManagerViewModel : ObservableObject, IAsyncDisposable
{
    readonly IModelManagementService service;
    readonly Action<string> openDirectory;
    readonly Action? modelsChanged;
    readonly Action<Uri>? openSource;
    readonly Action<ModelEntry>? resourceDownloaded;
    CancellationTokenSource? operation;
    bool disposed;
    [ObservableProperty] IReadOnlyList<ModelEntry> models = [];
    [ObservableProperty] ModelEntry? selectedModel;
    [ObservableProperty] ModelKind selectedKind;
    [ObservableProperty] bool busy;
    [ObservableProperty] string message = "Choose a model, then download explicitly. No models are downloaded automatically.";
    [ObservableProperty] long downloadedBytes;
    [ObservableProperty] long totalBytes;
    [ObservableProperty] bool indeterminate = true;
    public IReadOnlyList<ModelKind> Kinds { get; } = Enum.GetValues<ModelKind>();
    public string DirectoryPath => service.GetDirectory(SelectedKind);
    public bool HasSource => SelectedKind == ModelKind.FasterWhisperEngine;
    public string ArtifactName => HasSource ? "Faster-Whisper-XXL_r245.4_windows.7z" : "";
    public Uri? SourceUri => HasSource ? new Uri("https://github.com/Purfview/whisper-standalone-win/releases/tag/Faster-Whisper-XXL") : null;
    public string ProgressText => TotalBytes > 0 ? $"{DownloadedBytes:N0} / {TotalBytes:N0} bytes" : $"{DownloadedBytes:N0} bytes received (total unknown)";
    public bool CanDownload => !disposed && !Busy && SelectedModel is { Available: true, Downloaded: false, Present: false };
    public bool CanDelete => !disposed && !Busy && SelectedModel is { Available: true } row && (row.Present || row.Downloaded);

    public ModelManagerViewModel(IModelManagementService service, ModelKind kind, Action<string> openDirectory, Action? modelsChanged = null,
        Action<Uri>? openSource = null, Action<ModelEntry>? resourceDownloaded = null)
    {
        this.service = service;
        this.openDirectory = openDirectory;
        this.modelsChanged = modelsChanged;
        this.openSource = openSource;
        this.resourceDownloaded = resourceDownloaded;
        selectedKind = kind;
        Refresh();
    }

    partial void OnSelectedKindChanged(ModelKind value)
    {
        OnPropertyChanged(nameof(DirectoryPath));
        OnPropertyChanged(nameof(HasSource));
        OnPropertyChanged(nameof(ArtifactName));
        OnPropertyChanged(nameof(SourceUri));
        OpenSourceCommand.NotifyCanExecuteChanged();
        Refresh();
    }
    partial void OnSelectedModelChanged(ModelEntry? value) => NotifyActions();
    partial void OnDownloadedBytesChanged(long value) => OnPropertyChanged(nameof(ProgressText));
    partial void OnTotalBytesChanged(long value) => OnPropertyChanged(nameof(ProgressText));
    partial void OnBusyChanged(bool value)
    {
        NotifyActions();
        RefreshCommand.NotifyCanExecuteChanged();
        OpenFolderCommand.NotifyCanExecuteChanged();
        OpenSourceCommand.NotifyCanExecuteChanged();
    }
    void NotifyActions()
    {
        OnPropertyChanged(nameof(CanDownload));
        OnPropertyChanged(nameof(CanDelete));
        DownloadCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
    }
    bool CanRefresh() => !disposed && !Busy;
    [RelayCommand(CanExecute = nameof(CanRefresh))]
    public void Refresh()
    {
        if (Busy || disposed) return;
        string? selected = SelectedModel?.Id;
        try
        {
            Models = service.Discover(SelectedKind);
            SelectedModel = Models.FirstOrDefault(row => row.Id == selected) ?? Models.FirstOrDefault();
            if (SelectedModel is { Available: false } row) Message = row.UnavailableReason ?? "Unavailable on this platform.";
            NotifyActions();
        }
        catch (Exception e) { Models = []; SelectedModel = null; Message = e.Message; }
    }

    [RelayCommand(CanExecute = nameof(CanDownload))]
    async Task DownloadAsync()
    {
        if (!CanDownload || SelectedModel == null) return;
        ModelEntry requested = SelectedModel;
        string id = requested.Id;
        operation = new();
        Busy = true;
        DownloadedBytes = TotalBytes = 0;
        Indeterminate = true;
        try
        {
            CancellationTokenSource owner = operation;
            Progress<ModelDownloadProgress> progress = new(value =>
            {
                if (!ReferenceEquals(operation, owner)) return;
                DownloadedBytes = value.BytesReceived;
                TotalBytes = value.TotalBytes ?? 0;
                Indeterminate = value.TotalBytes is null or <= 0;
                Message = value.Stage;
            });
            await service.DownloadAsync(id, progress, operation.Token);
            Message = "Downloaded successfully.";
            resourceDownloaded?.Invoke(requested);
            modelsChanged?.Invoke();
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested) { Message = "Download canceled."; }
        catch (HttpRequestException e) when (e.StatusCode == HttpStatusCode.NotFound)
        { Message = "This model is not available on the server. Try another model or quantization."; }
        catch (Exception e) { Message = $"Download failed: {e.Message}"; }
        finally
        {
            operation.Dispose();
            operation = null;
            Busy = false;
            Refresh();
        }
    }

    [RelayCommand(CanExecute = nameof(CanDelete))]
    async Task DeleteAsync()
    {
        if (!CanDelete || SelectedModel == null) return;
        string id = SelectedModel.Id;
        operation = new();
        Busy = true;
        try
        {
            await service.DeleteAsync(id, operation.Token);
            Message = "Deleted successfully.";
            modelsChanged?.Invoke();
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested) { Message = "Delete canceled."; }
        catch (Exception e) { Message = $"Delete failed: {e.Message}"; }
        finally { operation.Dispose(); operation = null; Busy = false; Refresh(); }
    }

    [RelayCommand]
    void Cancel() => operation?.Cancel();

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    void OpenFolder()
    {
        if (!CanRefresh()) return;
        try { openDirectory(DirectoryPath); }
        catch (Exception e) { Message = $"Could not open folder: {e.Message}"; }
    }

    bool CanOpenSource() => !disposed && !Busy && HasSource;
    [RelayCommand(CanExecute = nameof(CanOpenSource))]
    void OpenSource()
    {
        if (!CanOpenSource() || SourceUri == null) return;
        try
        {
            if (openSource == null) throw new InvalidOperationException("A browser launcher is not available in this host.");
            openSource(SourceUri);
        }
        catch (Exception e) { Message = $"Could not open source: {e.Message}"; }
    }

    /// <summary>Window/app shutdown must await this before releasing its service.</summary>
    public async Task CancelAndDrainAsync()
    {
        operation?.Cancel();
        if (DownloadCommand.ExecutionTask is { } download) await download;
        if (DeleteCommand.ExecutionTask is { } delete) await delete;
    }

    public async ValueTask DisposeAsync()
    {
        disposed = true;
        NotifyActions();
        RefreshCommand.NotifyCanExecuteChanged();
        OpenFolderCommand.NotifyCanExecuteChanged();
        OpenSourceCommand.NotifyCanExecuteChanged();
        await CancelAndDrainAsync();
    }
}
