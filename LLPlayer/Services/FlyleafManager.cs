using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using FlyleafLib;
using FlyleafLib.Controls.WPF;
using FlyleafLib.MediaPlayer;
using LLPlayer.Core.Services;
using MaterialDesignThemes.Wpf;

namespace LLPlayer.Services;

public class FlyleafManager
{
    public Player Player { get; }
    public Config PlayerConfig => Player.Config;
    public FlyleafHost? FlyleafHost => Player.Host as FlyleafHost;
    public AppConfig Config { get; }
    public AppActions Action { get; }
    public LanguageResourceCoordinator LanguageResources { get; } = new();
    public SrtAlignmentWorkflow SrtAlignment { get; }
    public event Action? SrtAlignmentSourceChanged;

    private int _srtAlignmentSourceUpdatePending;
    private Task? _srtAlignmentShutdown;

    /// <summary>App-wide MaterialDesign snackbar queue (notifications + actionable config errors).</summary>
    public ISnackbarMessageQueue MessageQueue { get; }

    public AudioEngine AudioEngine => Engine.Audio;
    public EngineConfig ConfigEngine => Engine.Config;

    public FlyleafManager(Player player, IDialogService dialogService, ISnackbarMessageQueue messageQueue)
    {
        Player = player;
        MessageQueue = messageQueue;
        SrtAlignment = new(null, LanguageResources);

        // Load app configuration at this time
        Config = LoadAppConfig();
        Action = new AppActions(Player, Config, dialogService, SrtAlignment);

        Player.Opening += OnAlignmentSourceEvent;
        Player.OpenCompleted += OnAlignmentSourceEvent;
        Player.OpenAudioStreamCompleted += OnAlignmentSourceEvent;
        Player.OpenExternalAudioStreamCompleted += OnAlignmentSourceEvent;
        Player.OpenSubtitlesStreamCompleted += OnAlignmentSourceEvent;
        Player.OpenExternalSubtitlesStreamCompleted += OnAlignmentSourceEvent;
        Player.PropertyChanged += OnAlignmentPlayerChanged;
        Player.Audio.PropertyChanged += OnAlignmentAudioChanged;
        for (int slot = 0; slot < Player.subNum; slot++)
        {
            Player.Subtitles[slot].PropertyChanged += OnAlignmentSubtitleChanged;
            Player.SubtitlesManager[slot].PropertyChanged += OnAlignmentManagerChanged;
        }
    }

    private void OnAlignmentSourceEvent(object? sender, object? args) => QueueAlignmentSourceUpdate();

    private void OnAlignmentPlayerChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(Player.CanPlay) or nameof(Player.Status))
            QueueAlignmentSourceUpdate();
    }

    private void OnAlignmentAudioChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(Audio.IsOpened))
            QueueAlignmentSourceUpdate();
    }

    private void OnAlignmentSubtitleChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(Subtitle.IsOpened) or nameof(Subtitle.EnabledASR))
            QueueAlignmentSourceUpdate();
    }

    private void OnAlignmentManagerChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(SubManager.IsLoading))
            QueueAlignmentSourceUpdate();
    }

    private void QueueAlignmentSourceUpdate()
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.HasShutdownStarted ||
            Interlocked.Exchange(ref _srtAlignmentSourceUpdatePending, 1) != 0)
            return;

        // Engine events may run inside native locks. Recheck ownership only after yielding to the UI dispatcher.
        dispatcher.InvokeAsync(() =>
        {
            Interlocked.Exchange(ref _srtAlignmentSourceUpdatePending, 0);
            SrtAlignment.CancelStaleSources();
            SrtAlignmentSourceChanged?.Invoke();
        }, DispatcherPriority.Background);
    }

    public Task DrainSrtAlignmentForShutdownAsync()
    {
        if (_srtAlignmentShutdown == null || _srtAlignmentShutdown.IsFaulted || _srtAlignmentShutdown.IsCanceled)
            _srtAlignmentShutdown = DrainCoreAsync();
        return _srtAlignmentShutdown;

        async Task DrainCoreAsync()
        {
            SrtAlignment.BeginShutdown();
            try
            {
                await SrtAlignment.DrainAsync();
            }
            catch
            {
                SrtAlignment.ResumeAfterFailedShutdown();
                throw;
            }
        }
    }

    private AppConfig LoadAppConfig()
    {
        AppConfig? config = null;

        if (File.Exists(App.AppConfigPath))
        {
            try
            {
                config = AppConfig.Load(App.AppConfigPath);

                if (config.Version != App.Version)
                {
                    // One-shot default migrations keyed on the config's previous version, applied before the
                    // version is stamped + saved below (so they run at most once per upgrade).
                    // 0.3.2: Mica backdrop now defaults ON. Flip it on for configs created before 0.3.2; the
                    // value is then persisted at 0.3.2, so a user who later turns it off is respected. An
                    // empty/unparseable version (configs from before version stamping was added) is treated as
                    // older than 0.3.2 so the flip is applied consistently rather than relying on the default.
                    if (!Version.TryParse(config.Version, out Version? prev) || prev < new Version(0, 3, 2))
                    {
                        config.MicaBackdrop = true;
                    }

                    // HC-10: persist the migration/version-stamp in its OWN try/catch. A transient write failure
                    // (config file locked by AV/OneDrive, read-only directory) must not be treated as a fatal
                    // load error — the loaded config is valid and the migrations are idempotent (re-run next launch).
                    // Previously this Save shared the load try/catch below, so a locked file produced a false
                    // "Cannot load ..." + Environment.Exit(1) that bricked startup with an otherwise-valid config.
                    try
                    {
                        config.Version = App.Version;
                        config.Save(App.AppConfigPath);
                    }
                    catch
                    {
                        // ignored: non-fatal — run this session on the valid in-memory config; retry persist next launch
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Cannot load AppConfig from {Path.GetFileName(App.AppConfigPath)}, Please review the settings or delete the config file. Error details are recorded in {Path.GetFileName(App.CrashLogPath)}.");
                try
                {
                    File.WriteAllText(App.CrashLogPath, "AppConfig Loading Error: " + ex);
                }
                catch
                {
                    // ignored
                }

                // Fatal config-load failure: terminate immediately so execution does not continue
                // and initialize a throwaway default AppConfig during teardown.
                Environment.Exit(1);
            }
        }

        if (config == null)
        {
            config = new AppConfig();
        }
        config.Initialize(this);

        return config;
    }
}
