using System.Collections.ObjectModel;
using System.IO;
using System.Net;
using System.Net.Http;
using FlyleafLib;
using LLPlayer.Extensions;
using LLPlayer.Services;
using Whisper.net.Ggml;

namespace LLPlayer.ViewModels;

public class WhisperModelDownloadDialogVM : ModelDownloadDialogVMBase
{
    public WhisperModelDownloadDialogVM(FlyleafManager fl) : base(fl)
    {
        Title = $"Whisper Downloader - {App.Name}";
        StatusText = "Select a model to download.";

        List<WhisperCppModel> models = WhisperCppModelLoader.LoadAllModels();
        foreach (var model in models)
        {
            Models.Add(model);
        }

        SelectedModel = Models.First();

        CmdDownloadModel!.PropertyChanged += (sender, args) =>
        {
            if (args.PropertyName == nameof(CmdDownloadModel.IsExecuting))
            {
                OnPropertyChanged(nameof(CanDownload));
                OnPropertyChanged(nameof(CanDelete));
            }
        };
    }

    private const string TempExtension = ".tmp";

    public ObservableCollection<WhisperCppModel> Models { get; set => Set(ref field, value); } = new();

    public WhisperCppModel SelectedModel
    {
        get;
        set
        {
            if (Set(ref field, value))
            {
                OnPropertyChanged(nameof(CanDownload));
                OnPropertyChanged(nameof(CanDelete));
            }
        }
    }

    public bool CanDownload =>
        SelectedModel is { Downloaded: false } && !CmdDownloadModel.IsExecuting;

    public bool CanDelete =>
        SelectedModel is { Downloaded: true } && !CmdDownloadModel.IsExecuting;

    public AsyncDelegateCommand CmdDownloadModel => field ??= new AsyncDelegateCommand(async () =>
    {
        WhisperCppModel downloadModel = SelectedModel;
        string tempModelPath = downloadModel.ModelFilePath + TempExtension;

        await RunDownloadAsync(
            async token =>
            {
                if (downloadModel.Downloaded)
                {
                    StatusText = $"Model '{SelectedModel}' is already downloaded";
                    return;
                }

                // Delete any leftover temp file first (forces a clean re-download).
                if (!TryDeleteFile(tempModelPath))
                {
                    StatusText = "Failed to remove temp model";
                    return;
                }

                StatusText = $"Model '{downloadModel}' downloading..";

                await using Stream modelStream =
                    await WhisperGgmlDownloader.Default.GetGgmlModelAsync(downloadModel.Model, downloadModel.Quantization, token);
                long modelSize = await DownloadToFileAsync(modelStream, tempModelPath, token);

                // After a successful download, rename the temp file to the final file.
                File.Move(tempModelPath, downloadModel.ModelFilePath);

                downloadModel.Size = modelSize;
                OnDownloadStatusChanged();

                StatusText = $"Model '{SelectedModel}' is downloaded successfully";
            },
            cleanup: () => TryDeleteFile(tempModelPath),
            // Not every (model, quantization) pair is published on the mirror — surface a clear message instead of
            // a raw 404. The partial temp is removed by the cleanup above, so the model stays "not downloaded".
            mapError: ex => ex is HttpRequestException { StatusCode: HttpStatusCode.NotFound }
                ? $"'{downloadModel}' is not available on the server. Try a different model or quantization."
                : null);
    }).ObservesCanExecute(() => CanDownload);

    public DelegateCommand CmdCancelDownloadModel => field ??= new(CancelActiveDownload);

    public DelegateCommand CmdDeleteModel => field ??= new DelegateCommand(() =>
    {
        try
        {
            StatusText = $"Model '{SelectedModel}' deleting...";

            WhisperCppModel deleteModel = SelectedModel;

            // Delete model file if exists
            if (File.Exists(deleteModel.ModelFilePath))
            {
                File.Delete(deleteModel.ModelFilePath);
            }

            deleteModel.Size = 0;
            OnDownloadStatusChanged();

            StatusText = $"Model '{deleteModel}' is deleted successfully";
        }
        catch (Exception ex)
        {
            StatusText = $"Failed to delete model: {ex.Message}";
        }
    }).ObservesCanExecute(() => CanDelete);

    public DelegateCommand CmdOpenFolder => field ??= new(() => OpenFolderSafe(WhisperConfig.ModelsDirectory));

    private void OnDownloadStatusChanged()
    {
        OnPropertyChanged(nameof(CanDownload));
        OnPropertyChanged(nameof(CanDelete));
    }

    public override bool CanCloseDialog() => !CmdDownloadModel.IsExecuting;
}
