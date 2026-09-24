using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using FlyleafLib;
using FlyleafLib.MediaPlayer;
using LLPlayer.Avalonia.Controls;
using LLPlayer.Avalonia.Services;
using LLPlayer.Avalonia.ViewModels;
using LLPlayer.Avalonia.Views;

namespace LLPlayer.Avalonia;

/// <summary>
/// Start-up and shutdown of the Linux app: preferences, theme, FFmpeg lookup, FlyleafLib seams + engine, player,
/// main window, command-line media, and the developer screenshot automation.
/// </summary>
public sealed class AppHost(AppOptions options, IClassicDesktopStyleApplicationLifetime desktop)
{
    readonly AppPaths paths = AppPaths.FromEnvironment();
    PrefsStore? prefsStore;
    AppPrefs prefs = new();
    Player? player;
    FlyleafPlaybackController? controller;
    FlyleafWordTranslator? translator;
    MainWindowViewModel? viewModel;

    public MainWindow? MainWindow { get; private set; }

    public void Start()
    {
        desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
        Dispatcher.UIThread.UnhandledException += OnUnhandledUIException;
        prefsStore = new PrefsStore(paths.PrefsFile);
        prefs = prefsStore.Load();
        ThemeService.Apply(options.Theme ?? prefs.Theme);

        if (options.ThemeGallery)
        {
            ThemeGalleryWindow gallery = new();
            desktop.MainWindow = gallery;
            gallery.Show();
            ScheduleScreenshot(gallery, null);
            return;
        }

        FFmpegLocation ffmpeg = FFmpegLocator.Locate(options.FFmpegDir);
        if (!ffmpeg.IsValid)
        {
            ShowFatal(ErrorWindow.ForMissingFFmpeg(ffmpeg));
            return;
        }

        KeyMapper keys = new();
        AvaloniaUIDispatcher dispatcher = new();
        CollectionSyncBridge bridge = new(dispatcher);
        MainWindow? window = null;
        AvaloniaHostServices hostServices = new(() => window);
        EngineBootstrap.InstallSeams(dispatcher, hostServices, bridge, keys);

        try
        {
            EngineBootstrap.Start(EngineBootstrap.CreateEngineConfig(ffmpeg.Directory, paths));
        }
        catch (Exception ex)
        {
            CrashLog.Write("Engine.Start", ex);
            ShowFatal(new ErrorWindow("The media engine could not start", ex.Message,
                $"FFmpeg folder: {ffmpeg.Directory}\nEngine log: {paths.EngineLogFile}\nCrash log: {paths.CrashLogFile}\n\n{ex}"));
            return;
        }

        player = new Player(EngineBootstrap.CreatePlayerConfig(prefs));
        controller = new FlyleafPlaybackController(player, dispatcher, bridge);
        translator = new FlyleafWordTranslator(player, () => prefs.WordTranslationService);

        window = new MainWindow(keys, hostServices);
        viewModel = new MainWindowViewModel(controller, prefs, SavePrefs, translator, window);
        window.Attach(viewModel, player.Renderer, () => AppShortcutMap.BuildCheatSheet(player.Config.Player.KeyBindings.Keys));
        player.Host = window;
        MainWindow = window;

        hostServices.FolderPicked += folder => prefs.LastFolder = folder;
        hostServices.CompletionRequested += () => viewModel.ShowToast("Done", "The background task finished.", ToastVariant.Success);
        if (prefsStore.LoadWarning is { } warning)
            viewModel.ShowToast("Preferences reset", warning, ToastVariant.Destructive);

        if (options.Theme != null)
            viewModel.ShowSessionTheme(options.Theme);
        if (options.ShowSidebar && !viewModel.SidebarVisible)
            viewModel.ToggleSidebar();

        window.Closed += (_, _) => Shutdown();
        desktop.MainWindow = window;
        window.Show();

        if (options.MediaPath != null)
        {
            if (options.SeekSeconds is { } seek)
                controller.OpenCompleted += SeekOnce(seek);
            viewModel.Open(options.MediaPath, options.SubtitlesPath is { } sub ? [sub] : null);
        }

        ScheduleScreenshot(window, viewModel);
    }

    /// <summary>
    /// UI-thread exceptions are logged to the crash log and reported as a toast; the player keeps running (the WPF app
    /// likewise shows an error popup instead of terminating).
    /// </summary>
    void OnUnhandledUIException(object? sender, DispatcherUnhandledExceptionEventArgs e)
    {
        CrashLog.Write("Dispatcher.UnhandledException", e.Exception);
        if (viewModel != null)
        {
            viewModel.ShowToast("Unexpected error", e.Exception.Message + $" (details in {paths.CrashLogFile})", ToastVariant.Destructive);
            e.Handled = true;
        }
    }

    Action<string?, bool, string?> SeekOnce(double seconds)
    {
        Action<string?, bool, string?>? handler = null;
        handler = (_, isSubs, error) =>
        {
            if (isSubs || controller == null)
                return;
            controller.OpenCompleted -= handler;
            if (error == null)
                controller.SeekTo(TimeSpan.FromSeconds(seconds));
        };
        return handler;
    }

    void SavePrefs(AppPrefs p) => prefsStore?.Save(p);

    void ShowFatal(ErrorWindow error)
    {
        desktop.MainWindow = error;
        error.Show();
        ScheduleScreenshot(error, null);
    }

    void Shutdown()
    {
        viewModel?.SavePrefs();
        viewModel?.Dispose();
        controller?.Dispose();
        translator?.Dispose();
        player?.Dispose();
        player = null;
    }

    /// <summary>Developer option --screenshot: render <paramref name="window"/> to a PNG after the delay and exit.</summary>
    void ScheduleScreenshot(Window window, MainWindowViewModel? vm)
    {
        if (options.ScreenshotPath is not { } path)
            return;

        DispatcherTimer.RunOnce(async () =>
        {
            try
            {
                if (vm != null && options.DevWordPopup)
                {
                    OpenFirstWordPopup(window, vm);
                    await Task.Delay(1500);
                }

                SaveScreenshot(window, path);
                Console.WriteLine($"screenshot: {path}");
            }
            catch (Exception ex)
            {
                CrashLog.Write("Screenshot", ex);
                Console.Error.WriteLine("screenshot failed: " + ex.Message);
            }
            finally
            {
                window.Close();
                desktop.Shutdown();
            }
        }, TimeSpan.FromSeconds(options.ScreenshotDelaySeconds));
    }

    static void OpenFirstWordPopup(Window window, MainWindowViewModel vm)
    {
        if (window is not MainWindow main || string.IsNullOrWhiteSpace(vm.PrimaryText))
            return;

        SubtitleText sub = main.FindControl<SubtitleText>("PrimarySubtitle")!;
        string text = vm.PrimaryText;
        int start = 0;
        while (start < text.Length && !char.IsLetter(text[start]))
            start++;
        string word = SubtitleText.WordAt(text, Math.Min(start, text.Length - 1));
        Point p = sub.TranslatePoint(new Point(sub.Bounds.Width / 2, 0), main.FindControl<Panel>("VideoHost")!) ?? default;
        vm.OnWordClicked(word, text, 0, p.X, p.Y);
    }

    public static void SaveScreenshot(Window window, string path)
    {
        // The gallery is taller than the screen: render its whole content instead of the visible viewport.
        Visual target = window is ThemeGalleryWindow gallery ? gallery.GalleryContent : window;
        double scaling = window.RenderScaling;
        PixelSize size = new(Math.Max(1, (int)(target.Bounds.Width * scaling)), Math.Max(1, (int)(target.Bounds.Height * scaling)));
        using RenderTargetBitmap rtb = new(size, new Vector(96 * scaling, 96 * scaling));
        rtb.Render(target);
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        rtb.Save(path, PngBitmapEncoderOptions.Default);
    }
}
