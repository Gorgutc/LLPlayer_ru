#if !WINDOWS
using System.Collections.Concurrent;
using FlyleafLib;
using FlyleafLib.MediaPlayer;
using FlyleafLib.MediaPlayer.Diarization;
using FlyleafLib.MediaPlayer.Dubbing;
using LLPlayer.Core.Batch;
using LLPlayer.Core.Services;
using LLPlayer.Core.Services.Diarization;
using LLPlayer.Core.Services.Models;

namespace LLPlayer.Core.Automation;

/// <summary>Creates the display-free language services owned by the local automation process.</summary>
public sealed class ControlLanguageRuntime : IAsyncDisposable
{
    readonly HeadlessDispatcher dispatcher;
    readonly Lazy<bool> engineReady;
    readonly BatchActivityMonitor batchActivity = new();
    readonly Func<int, bool> userActive;

    public ControlLanguageRuntime(string configDirectory, string stateDirectory, string ffmpegDirectory,
        Func<Config>? captureConfig = null, Func<bool>? getUtf8Bom = null, Func<int, bool>? isUserActive = null,
        Action<EngineConfig>? applyStartup = null)
    {
        string configPath = Path.GetFullPath(configDirectory);
        string statePath = Path.GetFullPath(stateDirectory);
        string ffmpegPath = Path.GetFullPath(ffmpegDirectory);
        dispatcher = new();
        userActive = isUserActive ?? batchActivity.IsUserActive;
        Paths = new ModelStoragePaths(Path.Combine(statePath, "Models", "Whisper"),
            Path.Combine(statePath, "Models", "Tesseract"), Path.Combine(statePath, "Engines", "FasterWhisper"));

        engineReady = new Lazy<bool>(() =>
        {
            dispatcher.Invoke(() => StartEngine(statePath, ffmpegPath, applyStartup));
            return Engine.IsLoaded;
        });

        Func<Config> settings = captureConfig ?? (() => CaptureSavedSettings(configPath));
        Workflows = new ControlWorkflows(settings, EnsureEngine, Gpu, Resources, Paths.WhisperModels);
        Models = new ControlModelResources(settings, Paths, Resources, CreateArchiveExtractor());
        Diarization = new DiarizationWorkflow(Gpu);
        BatchSettings batchSettings = new BatchSettingsStore(Path.Combine(configPath, "LLPlayer.Batch.json")).Load();
        Config CaptureEngineConfig()
        {
            EnsureEngine();
            return settings();
        }

        var factory = MakeBatchFactory(CaptureEngineConfig, getUtf8Bom);
        Batch = new BatchWorkflow(batchSettings, factory, new BatchFolderWatcher(), dispatcher.Invoke,
            new FlyleafBatchMediaCatalog(CaptureEngineConfig), Gpu);
    }

    BatchRunFactory MakeBatchFactory(Func<Config> capture, Func<bool>? getUtf8Bom, BatchOutputOwnership? outputs = null,
        IDubbingVoiceAssignmentProvider? voices = null)
        => new(capture, userActive,
            snapshot => DubbingWorkflow.CreateRenderer(snapshot.Subtitles.DubbingConfig, confirmProcessExit: true),
            () => voices ?? DubbingWorkflow.CreateVoiceAssignments(capture().Subtitles.PersistPerLineVoices, null, null),
            Resources.AcquireUse, getUtf8Bom, Gpu, outputs);

    public IBatchRunFactory CreateOwnedBatchFactory(Config savedConfig, bool utf8Bom, BatchOutputOwnership outputs,
        IDubbingVoiceAssignmentProvider? voices = null)
    {
        EnsureEngine();
        var captured = Workflows.CaptureLocalConfig(savedConfig);
        return MakeBatchFactory(() => captured, () => utf8Bom, outputs, voices);
    }

    public ControlWorkflows Workflows { get; }
    public ControlModelResources Models { get; }
    public ModelStoragePaths Paths { get; }
    public GpuWorkCoordinator Gpu { get; } = new();
    public LanguageResourceCoordinator Resources { get; } = new();
    public BatchWorkflow Batch { get; }
    public string BatchActivityStatus => batchActivity.Status;
    public DiarizationWorkflow Diarization { get; }

    public void Dispatch(Action action) => dispatcher.Invoke(action);

    public void EnsureEngine()
    {
        if (!engineReady.Value)
            throw new InvalidOperationException("The display-free media engine did not start.");
    }

    static Config CaptureSavedSettings(string configDirectory)
    {
        Config config = new(true);
        new RecognitionSettingsStore(Path.Combine(configDirectory, "LLPlayer.Recognition.json")).Load()?.Apply(config.Subtitles);
        new TranslationSettingsStore(Path.Combine(configDirectory, "LLPlayer.Translation.json")).Load("Off", null).Apply(config.Subtitles);
        new DubbingSettingsStore(Path.Combine(configDirectory, "LLPlayer.Dubbing.json")).Load().Apply(config.Subtitles);
        return config;
    }

    void StartEngine(string stateDirectory, string ffmpegDirectory, Action<EngineConfig>? applyStartup)
    {
        Directory.CreateDirectory(stateDirectory);
        Utils.UIDispatcher = dispatcher;
        AudioEngine.Backend = NullAudioBackend.Instance;
        TesseractOCRService.ManagedDataDirectory = Paths.TesseractModels;
        SubtitlesOCR.ServiceFactory = CreateOcrService;
        var config = new EngineConfig
        {
            FFmpegPath = ffmpegDirectory,
            FFmpegLoadProfile = Flyleaf.FFmpeg.LoadProfile.Filters,
            PluginsPath = Path.Combine(AppContext.BaseDirectory, "Plugins"),
            UIRefresh = false,
            LogLevel = LogLevel.Warn,
            FFmpegLogLevel = Flyleaf.FFmpeg.LogLevel.Warn,
        };
        applyStartup?.Invoke(config);
        config.UIRefresh = false;
        config.LogOutput = Path.Combine(stateDirectory, "control-engine.log");
        Engine.Start(config);
    }

    internal static IOCRService? CreateOcrService(Config.SubtitlesConfig config, SubOCREngineType engine)
    {
        if (engine == SubOCREngineType.Tesseract) return new TesseractOCRService(config);
#if LLPLAYER_CONTROL_WINDOWS
        if (engine == SubOCREngineType.MicrosoftOCR && OperatingSystem.IsWindowsVersionAtLeast(10, 0, 18362))
            return new MicrosoftOcrService(config);
#endif
        return null;
    }

    static IEngineArchiveExtractor? CreateArchiveExtractor()
    {
#if LLPLAYER_CONTROL_WINDOWS
        if (OperatingSystem.IsWindows()) return new WindowsEngineArchiveExtractor();
#endif
        return null;
    }

    public async ValueTask DisposeAsync()
    {
        await Batch.DisposeAsync().ConfigureAwait(false);
        Models.Dispose();
        if (ReferenceEquals(Utils.UIDispatcher, dispatcher)) Utils.UIDispatcher = null;
        dispatcher.Dispose();
    }

    sealed class HeadlessDispatcher : IUIDispatcher, IDisposable
    {
        readonly BlockingCollection<Action> queue = new();
        readonly Thread owner;

        public HeadlessDispatcher()
        {
            owner = new Thread(Run) { IsBackground = true, Name = "LLPlayer control dispatch" };
            owner.Start();
        }

        public bool CheckAccess() => Thread.CurrentThread == owner;

        public void Post(Action action)
        {
            var context = ExecutionContext.Capture();
            queue.Add(context == null ? action : () => ExecutionContext.Run(context, _ => action(), null));
        }

        public void Invoke(Action action)
        {
            if (CheckAccess()) { action(); return; }

            TaskCompletionSource done = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Post(() =>
            {
                try { action(); done.SetResult(); }
                catch (Exception error) { done.SetException(error); }
            });
            done.Task.GetAwaiter().GetResult();
        }

        void Run()
        {
            SynchronizationContext.SetSynchronizationContext(new DispatchSynchronizationContext(this));
            foreach (Action action in queue.GetConsumingEnumerable())
            {
                try { action(); }
                catch (Exception) { System.Diagnostics.Trace.TraceError("A Flyleaf control dispatcher callback failed."); }
            }
        }

        public void Dispose()
        {
            queue.CompleteAdding();
            owner.Join();
            queue.Dispose();
        }

        sealed class DispatchSynchronizationContext(HeadlessDispatcher dispatcher) : SynchronizationContext
        {
            public override void Post(SendOrPostCallback callback, object? state) => dispatcher.Post(() => callback(state));
            public override void Send(SendOrPostCallback callback, object? state) => dispatcher.Invoke(() => callback(state));
        }
    }
}
#endif
