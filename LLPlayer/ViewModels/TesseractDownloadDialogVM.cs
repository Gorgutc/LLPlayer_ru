using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using FlyleafLib;
using LLPlayer.Extensions;
using LLPlayer.Services;

namespace LLPlayer.ViewModels;

public class TesseractDownloadDialogVM : ModelDownloadDialogVMBase
{
    public TesseractDownloadDialogVM(FlyleafManager fl) : base(fl)
    {
        Title = $"Tesseract Downloader - {App.Name}";
        StatusText = "Select a model to download.";

        List<TesseractModel> models = TesseractModelLoader.LoadAllModels();
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

    public ObservableCollection<TesseractModel> Models { get; set => Set(ref field, value); } = new();

    public TesseractModel SelectedModel
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
        TesseractModel downloadModel = SelectedModel;
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

                TotalSize = 0;
                IsIndeterminateDownload = true;

                using HttpClient httpClient = new();
                httpClient.Timeout = TimeSpan.FromSeconds(10);

                using var response = await httpClient.GetAsync(
                    $"https://github.com/tesseract-ocr/tessdata/raw/refs/heads/main/{downloadModel.LangCode}.traineddata",
                    HttpCompletionOption.ResponseHeadersRead, token);

                response.EnsureSuccessStatusCode();

                TotalSize = response.Content.Headers.ContentLength ?? 0;
                IsIndeterminateDownload = TotalSize <= 0;

                await using Stream modelStream = await response.Content.ReadAsStreamAsync(token);
                long modelSize = await DownloadToFileAsync(modelStream, tempModelPath, token);

                // After a successful download, rename the temp file to the final file.
                File.Move(tempModelPath, downloadModel.ModelFilePath);

                downloadModel.Size = modelSize;
                OnDownloadStatusChanged();

                StatusText = $"Model '{SelectedModel}' is downloaded successfully";
            },
            cleanup: () => TryDeleteFile(tempModelPath));
    }).ObservesCanExecute(() => CanDownload);

    public DelegateCommand CmdCancelDownloadModel => field ??= new(CancelActiveDownload);

    public DelegateCommand CmdDeleteModel => field ??= new DelegateCommand(() =>
    {
        try
        {
            StatusText = $"Model '{SelectedModel}' deleting...";

            TesseractModel deleteModel = SelectedModel;

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

    public DelegateCommand CmdOpenFolder => field ??= new(() => OpenFolderSafe(TesseractModel.ModelsDirectory));

    private void OnDownloadStatusChanged()
    {
        OnPropertyChanged(nameof(CanDownload));
        OnPropertyChanged(nameof(CanDelete));
    }

    public override bool CanCloseDialog() => !CmdDownloadModel.IsExecuting;
}
