using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using FlyleafLib;
using FlyleafLib.MediaPlayer;
using FlyleafLib.MediaPlayer.AI;
using FlyleafLib.MediaPlayer.Dubbing;
using LLPlayer.Avalonia.Services;
using LLPlayer.Avalonia.ViewModels;
using LLPlayer.Avalonia.Views;
using LLPlayer.Core.Batch;
using LLPlayer.Core.Services;
using LLPlayer.Core.Services.Models;

namespace LLPlayer.Avalonia;

public sealed partial class AppHost
{
    HttpClient? modelDownloads;
    BatchWorkflow? languageBatch;
    BatchSubtitlesViewModel? languageBatchModel;
    BatchTrayService? languageTray;
    DubbingSettings? dubbingSettings;
    DubbingSettingsStore? dubbingStore;
    bool languageShuttingDown;

    void ConfigureLanguageWorkflows(MainWindow window, WordLibrary words, RecognitionSettingsStore recognitionStore,
        AvaloniaUIDispatcher dispatcher)
    {
        if (player == null || controller == null || viewModel == null) return;
        TesseractOCRService.ManagedDataDirectory = paths.TesseractModelsDirectory;
        modelDownloads = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        dubbingStore = new DubbingSettingsStore(paths.DubbingFile);
        dubbingSettings = dubbingStore.Load();
        dubbingSettings.Apply(player.Config.Subtitles);

        viewModel.RecognitionDialog = () => CreateRecognitionDialog(window, recognitionStore);
        viewModel.AiInsightsDialog = () => new AiInsightsViewModel(controller.CaptureInsightsTranscript, () =>
        {
            var llm = AiInsightLlmResolver.Resolve(player.Config.Subtitles);
            return llm == null ? null : new AiInsightConnection(llm.ServiceType +
                (string.IsNullOrWhiteSpace(llm.Model) ? "" : " - " + llm.Model), AiInsightService.ForSettings(llm));
        }, words, controller.IsInsightsTranscriptCurrent);
        viewModel.DubbingDialog = () =>
        {
            DubbingSettings baseline = dubbingSettings.Clone();
            return new DubbingViewModel(baseline, edited =>
            {
                DubbingSettings detachedEdit = edited.Clone();
                return SaveDubbingSettingsAsync(current => DubbingSettings.MergeChanges(current, baseline, detachedEdit));
            }, dubbingStore.LoadWarning);
        };
        IReadOnlyList<TtsVoice> Voices() => VoiceBankResolver.ForConfig(dubbingSettings.Engine.DefaultVoiceId,
            dubbingSettings.Engine.CustomVoiceIds);
        viewModel.DubbingVoices = Voices;
        viewModel.VoicePickerDialog = row => new VoicePickerViewModel(Voices(),
            controller.GetCueVoiceId(row.Cue, row.Slot), voice => controller.SetCueVoiceAsync(row.Cue, row.Slot, voice));

        var batchStore = new BatchSettingsStore(paths.BatchFile);
        var batchSettings = batchStore.Load();
        var activity = new BatchActivityMonitor();
        void InvokeUi(Action action)
        {
            if (Dispatcher.UIThread.CheckAccess()) action();
            else Dispatcher.UIThread.InvokeAsync(action).GetAwaiter().GetResult();
        }
        Config ConfigSnapshot()
        {
            Config? snapshot = null;
            InvokeUi(() => snapshot = controller.CaptureLanguageWorkflowConfig());
            return snapshot!;
        }
        var factory = new BatchRunFactory(ConfigSnapshot, activity.IsUserActive,
            snapshot => DubbingWorkflow.CreateRenderer(snapshot.Subtitles.DubbingConfig),
            () =>
            {
                IDubbingVoiceAssignmentProvider? assignments = null;
                InvokeUi(() => assignments = controller.CaptureBatchVoiceAssignments());
                return assignments;
            },
            controller.LanguageResources.AcquireUse,
            getUtf8Bom: () => controller.GetPlaybackSettings().SubtitleExportUtf8Bom);
        languageBatch = new BatchWorkflow(batchSettings, factory, new BatchFolderWatcher(), InvokeUi,
            new FlyleafBatchMediaCatalog(ConfigSnapshot));
        languageBatchModel = new BatchSubtitlesViewModel(languageBatch, batchStore, window.PickFolderAsync,
            path => { RestoreMain(); viewModel.Open(path); return Task.CompletedTask; }, window.RevealMediaAsync,
            () => viewModel.ShowDubbingSettingsCommand.Execute(null), Voices,
            () => dubbingSettings.Engine.DefaultVoiceId,
            voice => SaveDubbingSettingsAsync(current =>
            {
                DubbingSettings edited = current.Clone();
                edited.Engine.DefaultVoiceId = voice;
                return edited;
            }));
        viewModel.BatchDialog = () => languageBatchModel;
        void RestoreMain()
        {
            if (languageShuttingDown) return;
            if (!window.IsVisible) window.Show();
            if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
            window.Activate();
        }
        languageTray = new BatchTrayService(Application.Current!, languageBatch, RestoreMain,
            () => window.ShowBatch(languageBatchModel), () =>
            {
                window.RequestApplicationExit();
                return Task.CompletedTask;
            });
        window.KeepAliveOnClose = () => !languageShuttingDown && languageBatch.KeepAlive;
        window.ShutdownStarting = () =>
        {
            languageShuttingDown = true;
            controller.Recognition.Dispose(); // Cancel active OCR before waiting for any queued model/voice work.
        };
        window.BackgroundRequested = backgroundWindow =>
        {
            if (ReferenceEquals(backgroundWindow, window)) controller.Pause();
            languageTray.SendToBackground(backgroundWindow);
        };
        languageBatch.Changed += () =>
        {
            languageBatchModel.ActivityStatus = activity.Status;
            if (!languageShuttingDown && !languageBatch.KeepAlive
                && (!window.IsVisible || window.WindowState == WindowState.Minimized)) RestoreMain();
        };
        controller.OpenCompleted += (media, isSubtitle, error) =>
        {
            if (error == null && !isSubtitle && !languageShuttingDown)
                DubbedAudioAutoLoader.TryAttach(player, controller.CurrentMediaPath, dispatcher.Post);
        };
        controller.CuesChanged += slot =>
        {
            if (!languageShuttingDown) _ = controller.RestoreDubbingVoicesAsync(slot);
        };
    }

    Task SaveDubbingSettingsAsync(Func<DubbingSettings, DubbingSettings> edit)
    {
        if (controller == null || player == null || dubbingStore == null) return Task.CompletedTask;
        return SaveDubbingSettingsAsync(controller.Recognition, dubbingStore, () => dubbingSettings!, edit, detached =>
        {
            controller.ApplyDubbingSettings(detached);
            dubbingSettings = detached;
        });
    }

    internal static Task SaveDubbingSettingsAsync(RecognitionActions recognition, DubbingSettingsStore store,
        Func<DubbingSettings> current, Func<DubbingSettings, DubbingSettings> edit, Action<DubbingSettings> publish)
    {
        return recognition.MutateIfIdleAsync(_ =>
        {
            DubbingSettings detached = edit(current()).Clone();
            store.Save(detached);
            publish(detached);
        });
    }

    RecognitionViewModel CreateRecognitionDialog(MainWindow window, RecognitionSettingsStore store)
    {
        RecognitionViewModel? model = null;
        model = new RecognitionViewModel(controller!.RecognitionSettings, controller.Recognition, store,
            controller.RecognitionWarning, settings => controller.SaveRecognitionSettingsAsync(settings, store),
            fallbackLanguagesEditable: false, copyText: window.SetClipboardText,
            microsoftLanguages: AvailableMicrosoftLanguages, tesseractDirectory: paths.TesseractModelsDirectory,
            manageResource: async kind =>
            {
                string whisperDirectory = string.IsNullOrWhiteSpace(model!.ModelDirectory)
                    ? paths.WhisperModelsDirectory : model.ModelDirectory;
                IEngineArchiveExtractor? extractor = null;
#if LLPLAYER_WINDOWS_OCR
                extractor = new WindowsEngineArchiveExtractor();
#endif
                var manager = new ModelManagementService(modelDownloads!,
                    new ModelStoragePaths(whisperDirectory, paths.TesseractModelsDirectory, paths.WhisperEngineDirectory),
                    controller.IsModelInUse, controller.MutateModelFilesAsync, extractor);
                var managerModel = new ModelManagerViewModel(manager, kind, directory => _ = RevealDirectoryAsync(directory), () =>
                {
                    if (Directory.Exists(whisperDirectory) && Directory.EnumerateFiles(whisperDirectory, "ggml-*.bin").Any())
                        model.ModelDirectory = whisperDirectory;
                    model.RefreshModelsCommand.Execute(null);
                }, openSource: uri => _ = window.OpenBrowserAsync(uri),
                    resourceDownloaded: entry => AdoptDownloadedRecognitionResource(model.Draft, entry));
                await window.ShowModelManagerAsync(managerModel);
            }, openFolder: directory => _ = RevealDirectoryAsync(directory),
            tesseractDirectories: TesseractOCRService.GetDataDirectories);
        return model;
    }

    internal static void AdoptDownloadedRecognitionResource(RecognitionSettings draft, ModelEntry entry)
    {
        if (entry.Kind != ModelKind.FasterWhisperEngine) return;
        draft.FasterWhisper.ManualEnginePath = Path.Combine(entry.FullPath, "faster-whisper-xxl.exe");
        draft.FasterWhisper.UseManualEngine = true;
    }

    static IReadOnlyList<string> AvailableMicrosoftLanguages()
    {
#if LLPLAYER_WINDOWS_OCR
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 18362)) return [];
        return MicrosoftOcrService.AvailableLanguageTags;
#else
        return [];
#endif
    }

    async Task RevealDirectoryAsync(string directory)
    {
        if (!Directory.Exists(directory)) return;
        try
        {
            if (MainWindow != null && !await MainWindow.RevealMediaAsync(directory))
                viewModel?.ShowToast("Open model folder", "The folder could not be opened.", ToastVariant.Destructive);
        }
        catch (Exception exception) { CrashLog.Write("Open models directory", exception); }
    }

    async Task DrainLanguageWorkflowsAsync()
    {
        languageShuttingDown = true;
        controller?.Recognition.Invalidate();
        if (languageBatchModel != null) await languageBatchModel.DisposeAsync();
        if (languageBatch != null) await languageBatch.DisposeAsync();
        if (languageTray != null) await languageTray.DisposeAsync();
        if (viewModel != null) await viewModel.DrainAiInsightsAsync();
        if (controller != null)
        {
            await controller.DrainVoiceRestoreAsync();
            await controller.DrainVoiceAssignmentsAsync();
        }
        modelDownloads?.Dispose();
    }
}
