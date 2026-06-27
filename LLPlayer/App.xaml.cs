using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using FlyleafLib;
using FlyleafLib.MediaPlayer;
using FlyleafLib.MediaPlayer.AI;
using LLPlayer.Extensions;
using LLPlayer.Services;
using LLPlayer.Views;
using MaterialDesignThemes.Wpf;

namespace LLPlayer;

public partial class App : PrismApplication
{
    public static string Name => "LLPlayer";
    public static string? CmdUrl { get; private set; } = null;
    public static string PlayerConfigPath { get; } = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "LLPlayer.PlayerConfig.json");
    public static string EngineConfigPath { get; } = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "LLPlayer.Engine.json");
    public static string AppConfigPath { get; } = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "LLPlayer.Config.json");
    // F-10: global cumulative word list (additive — absent file means an empty list).
    public static string WordListPath { get; } = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "LLPlayer.WordList.json");
    public static string CrashLogPath { get; } = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log");

    private readonly LogHandler Log;

    public App()
    {
        Log = new LogHandler("[App] [MainApp       ] ");
    }

    static App()
    {
        // Set thread culture to English and error messages to English
        Utils.SaveOriginalCulture();

        CultureInfo.DefaultThreadCurrentCulture = new CultureInfo("en-US");
        CultureInfo.DefaultThreadCurrentUICulture = new CultureInfo("en-US");
    }

    protected override void RegisterTypes(IContainerRegistry containerRegistry)
    {
        containerRegistry
            .Register<Player>(FlyleafLoader.CreateFlyleafPlayer)
            .RegisterSingleton<FlyleafManager>()
            .RegisterSingleton<AccentColorService>()
            // Background batch activity + system-tray presence: keep the app alive in the tray when the main
            // window is closed during a batch run (see AppTrayService / BatchActivityService).
            .RegisterSingleton<BatchActivityService>()
            .RegisterSingleton<AppTrayService>()
            .RegisterSingleton<IDialogService, ExtendedDialogService>();

        // Single app-wide snackbar queue (notifications + actionable config errors), hosted in FlyleafOverlay.
        containerRegistry.RegisterInstance<ISnackbarMessageQueue>(new SnackbarMessageQueue(TimeSpan.FromSeconds(6)));

        // F-10: the global word list, loaded once from disk beside the exe (absent file → empty list).
        containerRegistry.RegisterSingleton(typeof(WordListStore), () =>
        {
            WordListStore store = new(WordListPath);
            store.Load();
            return store;
        });

        containerRegistry.RegisterDialogWindow<MyDialogWindow>();

        containerRegistry.RegisterDialog<SettingsDialog>();
        containerRegistry.RegisterDialog<SelectLanguageDialog>();
        containerRegistry.RegisterDialog<SubtitlesDownloaderDialog>();
        containerRegistry.RegisterDialog<SubtitlesExportDialog>();
        containerRegistry.RegisterDialog<AiInsightsDialog>();
        containerRegistry.RegisterDialog<WordManagerDialog>();
        containerRegistry.RegisterDialog<BatchSubtitlesDialog>();
        containerRegistry.RegisterDialog<CheatSheetDialog>();
        containerRegistry.RegisterDialog<CommandPaletteDialog>();
        containerRegistry.RegisterDialog<WhisperModelDownloadDialog>();
        containerRegistry.RegisterDialog<WhisperEngineDownloadDialog>();
        containerRegistry.RegisterDialog<TesseractDownloadDialog>();
        containerRegistry.RegisterDialog<ErrorDialog>();
    }

    protected override Window CreateShell()
    {
        return Container.Resolve<MainWindow>();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        if (e.Args.Length == 1)
        {
            CmdUrl = e.Args[0];
        }

        // TODO: L: customizable?
        // Ensures that we have enough worker threads to avoid the UI from freezing or not updating on time
        ThreadPool.GetMinThreads(out int workers, out int ports);
        ThreadPool.SetMinThreads(workers + 6, ports + 6);

        // Start flyleaf engine
        FlyleafLoader.StartEngine();

        base.OnStartup(e);
    }

    protected override void OnInitialized()
    {
        base.OnInitialized();

        // Apply the persisted theme mode (Dark default / Light / FollowOS) + optional accent sync now that
        // the shell and MDIX resources exist. This is intentionally NOT done during FlyleafManager
        // construction (too early). Cosmetic — must never crash startup.
        try
        {
            var fl = Container.Resolve<FlyleafManager>();
            fl.Config.Theme.ApplyBaseTheme();
            // The colour setters are gated by AppConfigTheme.Loaded (so config load no longer mutates the
            // global palette), so the stored Primary/Secondary colours must be pushed explicitly at startup.
            if (fl.Config.AccentColorSync)
            {
                // Accent sync overrides primary with the OS accent and applies the stored secondary.
                fl.Config.Theme.ApplyAccentSync(AccentColorService.GetWindowsAccentColor());
            }
            else
            {
                fl.Config.Theme.ApplyUserColors();
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to apply startup theme: {ex}");
        }

        // Create the tray service now (after the shell exists) so it can subscribe to batch activity. It is
        // what keeps the app alive in the tray when the main window is closed mid-batch. The tray icon stays
        // hidden until it is actually needed. Cosmetic infra — must never crash startup.
        try
        {
            Container.Resolve<AppTrayService>();
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to initialize tray service: {ex}");
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Remove the tray icon on exit so it does not linger in the notification area.
        try
        {
            Container.Resolve<AppTrayService>().Dispose();
        }
        catch
        {
            // best-effort tray cleanup
        }

        base.OnExit(e);
    }

    private void App_OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // Ignore WPF Clipboard exception
        if (e.Exception is COMException { ErrorCode: -2147221040 })
        {
            e.Handled = true;
        }

        if (!e.Handled)
        {
            Log.Error($"Unknown error occurred in App: {e.Exception}");
            Logger.ForceFlush();

            // Persist to crash.log so the error is diagnosable even in release builds (where LogOutput is
            // unset and Log goes nowhere). Best-effort; must not throw.
            try
            {
                File.AppendAllText(CrashLogPath,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {e.Exception}{Environment.NewLine}{Environment.NewLine}");
            }
            catch
            {
                // ignore
            }

            // Showing the error must never itself crash the app (e.g. the Prism container is unavailable
            // during early startup/shutdown). ErrorDialogHelper already falls back to a message box; this is
            // belt-and-braces so the global handler can never turn one exception into a fatal crash.
            try
            {
                ErrorDialogHelper.ShowUnknownErrorPopup($"Unhandled Exception: {e.Exception.Message}", "Global", e.Exception);
            }
            catch (Exception dialogEx)
            {
                Log.Error($"Failed to show error dialog: {dialogEx}");
            }

            e.Handled = true;
        }
    }

    #region App Version
    public static string OSArchitecture => RuntimeInformation.OSArchitecture.ToString().ToLowerFirstChar();
    public static string ProcessArchitecture => RuntimeInformation.ProcessArchitecture.ToString().ToLowerFirstChar();

    private static string? _version;
    public static string Version
    {
        get
        {
            if (_version == null)
            {
                (_version, _commitHash) = GetVersion();
            }

            return _version;
        }
    }

    private static string? _commitHash;
    public static string CommitHash
    {
        get
        {
            if (_commitHash == null)
            {
                (_version, _commitHash) = GetVersion();
            }

            return _commitHash;
        }
    }

    private static (string version, string commitHash) GetVersion()
    {
        string exeLocation = Process.GetCurrentProcess().MainModule!.FileName;
        FileVersionInfo fvi = FileVersionInfo.GetVersionInfo(exeLocation);

        Guards.ThrowIfNull(fvi.ProductVersion);

        // ProductVersion (= AssemblyInformationalVersion) is "<version>" or "<version>+<commitHash>". The
        // "+<commitHash>" suffix is appended by the SDK only when the build injects a git SHA (Source Link /
        // a SourceRevisionId MSBuild property); local and agent publish builds omit it. Parse tolerantly so a
        // SHA-less build (empty commit hash) never throws — App.Version is first read at startup inside the
        // FlyleafLoader try/catch that calls Environment.Exit(1), so throwing here would block the app from
        // starting (and previously surfaced as a misleading "ProductVersion is invalid" / "delete the config").
        return InformationalVersion.Parse(fvi.ProductVersion);
    }
    #endregion
}
