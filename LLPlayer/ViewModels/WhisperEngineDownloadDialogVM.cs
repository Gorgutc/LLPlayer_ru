using System.IO;
using System.Net.Http;
using FlyleafLib;
using LLPlayer.Extensions;
using LLPlayer.Services;
using SevenZip;
using File = System.IO.File;

namespace LLPlayer.ViewModels;

public class WhisperEngineDownloadDialogVM : ModelDownloadDialogVMBase
{
    // currently not reusable at all
    public static string EngineURL => "https://github.com/Purfview/whisper-standalone-win/releases/tag/Faster-Whisper-XXL";
    public static string EngineFile => "Faster-Whisper-XXL_r245.4_windows.7z";
    private static string EngineDownloadURL =
        "https://github.com/umlx5h/LLPlayer/releases/download/v0.0.1/Faster-Whisper-XXL_r245.4_windows.7z";
    private static string EngineName = "Faster-Whisper-XXL";
    private static string EnginePath = Path.Combine(WhisperConfig.EnginesDirectory, EngineName);

    public WhisperEngineDownloadDialogVM(FlyleafManager fl) : base(fl)
    {
        Title = $"Whisper Engine Downloader - {App.Name}";
        WindowHeight = 210;

        CmdDownloadEngine!.PropertyChanged += (sender, args) =>
        {
            if (args.PropertyName == nameof(CmdDownloadEngine.IsExecuting))
            {
                OnPropertyChanged(nameof(CanDownload));
                OnPropertyChanged(nameof(CanDelete));
            }
        };
    }

    public bool Downloaded => Directory.Exists(EnginePath);

    public bool CanDownload => !Downloaded && !CmdDownloadEngine.IsExecuting;

    public bool CanDelete => Downloaded && !CmdDownloadEngine.IsExecuting;

    public AsyncDelegateCommand CmdDownloadEngine => field ??= new AsyncDelegateCommand(async () =>
    {
        string tempDownloadFile = Path.Combine(Path.GetTempPath(), EngineFile);

        await RunDownloadAsync(
            async token =>
            {
                StatusText = $"Engine '{EngineName}' downloading..";

                TotalSize = 0;
                IsIndeterminateDownload = true;

                using HttpClient httpClient = new();
                httpClient.Timeout = TimeSpan.FromSeconds(10);

                using var response = await httpClient.GetAsync(
                    EngineDownloadURL, HttpCompletionOption.ResponseHeadersRead, token);

                response.EnsureSuccessStatusCode();

                TotalSize = response.Content.Headers.ContentLength ?? 0;
                IsIndeterminateDownload = TotalSize <= 0;

                await using Stream engineStream = await response.Content.ReadAsStreamAsync(token);
                await DownloadToFileAsync(engineStream, tempDownloadFile, token);

                StatusText = $"Engine '{EngineName}' unzipping..";
                await UnzipEngine(tempDownloadFile);

                StatusText = $"Engine '{EngineName}' is downloaded successfully";
                OnDownloadStatusChanged();
            },
            cleanup: () => TryDeleteFile(tempDownloadFile));
    }).ObservesCanExecute(() => CanDownload);

    public DelegateCommand CmdCancelDownloadEngine => field ??= new(CancelActiveDownload);

    public AsyncDelegateCommand CmdDeleteEngine => field ??= new AsyncDelegateCommand(async () =>
    {
        try
        {
            StatusText = $"Engine '{EngineName}' deleting...";

            // Delete engine if exists
            if (Directory.Exists(EnginePath))
            {
                await Task.Run(() =>
                {
                    Directory.Delete(EnginePath, true);
                });
            }

            OnDownloadStatusChanged();

            StatusText = $"Engine '{EngineName}' is deleted successfully";
        }
        catch (Exception ex)
        {
            StatusText = $"Failed to delete engine: {ex.Message}";
        }
    }).ObservesCanExecute(() => CanDelete);

    public DelegateCommand CmdOpenFolder => field ??= new(() => OpenFolderSafe(WhisperConfig.EnginesDirectory));

    private void OnDownloadStatusChanged()
    {
        OnPropertyChanged(nameof(CanDownload));
        OnPropertyChanged(nameof(CanDelete));
    }

    private async Task UnzipEngine(string zipPath)
    {
        WhisperConfig.EnsureEnginesDirectory();

        // HC-11: resolve 7z.dll from the app install directory, not the process CWD. Launching via a file
        // association/shortcut with a different "Start in" makes a CWD-relative path throw SevenZipLibraryException.
        SevenZipBase.SetLibraryPath(Path.Combine(AppContext.BaseDirectory, "lib", "7z.dll"));

        using (SevenZipExtractor extractor = new(zipPath))
        {
            await extractor.ExtractArchiveAsync(WhisperConfig.EnginesDirectory);
        }

        string licencePath = Path.Combine(WhisperConfig.EnginesDirectory, "license.txt");

        if (File.Exists(licencePath) && Directory.Exists(WhisperConfig.EnginesDirectory))
        {
            // move license.txt to engine directory
            File.Move(licencePath, Path.Combine(EnginePath, "license.txt"));
        }
    }

    public override bool CanCloseDialog() => !CmdDownloadEngine.IsExecuting;
}
