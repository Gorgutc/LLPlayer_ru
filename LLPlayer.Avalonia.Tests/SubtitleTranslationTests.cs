using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using FlyleafLib;
using FlyleafLib.MediaPlayer;
using FlyleafLib.MediaPlayer.Translation;
using FlyleafLib.MediaPlayer.Translation.Services;
using LLPlayer.Avalonia.Controls;
using LLPlayer.Avalonia.Services;
using LLPlayer.Avalonia.ViewModels;
using LLPlayer.Avalonia.Views;
using LLPlayer.Core.Services;
using SkiaSharp;

namespace LLPlayer.Avalonia.Tests;

[Collection(FlyleafGlobalsCollection.Name)]
public sealed class SubtitleTranslationTests
{
    static (Player Player, FlyleafPlaybackController Controller) Create()
    {
        string? ffmpeg = Environment.GetEnvironmentVariable("LLPLAYER_FFMPEG_DIR");
        Assert.SkipWhen(string.IsNullOrEmpty(ffmpeg), "Portable controller integration requires existing FFmpeg shared libraries.");
        AvaloniaUIDispatcher dispatcher = new();
        CollectionSyncBridge bridge = new(dispatcher);
        EngineBootstrap.InstallSeams(dispatcher, new AvaloniaHostServices(() => null), bridge, new KeyMapper());
        if (!Engine.IsLoaded)
            EngineBootstrap.Start(EngineBootstrap.CreateEngineConfig(ffmpeg!, new(Path.GetTempPath(), Path.GetTempPath())));
        var config = EngineBootstrap.CreatePlayerConfig(new AppPrefs().Normalize());
        config.Subtitles.LanguageAutoDetect = false; config.Subtitles.SearchLocal = false;
        var player = new Player(config);
        return (player, new FlyleafPlaybackController(player, dispatcher, bridge));
    }

    static SubtitleData AddCue(Player player, int slot, string text)
    {
        var manager = player.SubtitlesManager[slot];
        manager.LanguageSource = Language.English;
        SubtitleData cue = new() { Text = text, StartTime = TimeSpan.Zero, EndTime = TimeSpan.FromSeconds(3) };
        manager.Add(cue); manager.SetCurrentTime(TimeSpan.FromSeconds(1));
        return cue;
    }

    [AvaloniaFact]
    public async Task Two_slot_menu_toggles_refresh_paused_overlay_sidebar_provenance_and_return_to_original()
    {
        var (player, controller) = Create();
        var window = new MainWindow { Width = 960, Height = 600 };
        var vm = new MainWindowViewModel(controller, new AppPrefs().Normalize(), _ => { }, new FakeTranslator(), window);
        var first = new Provider("primary translation"); var second = new Provider("secondary translation");
        try
        {
            window.Attach(vm, null); window.Show();
            Assert.False(controller.PrimaryTranslationEnabled); Assert.False(controller.SecondaryTranslationEnabled);
            player.Config.Subtitles[0].EnabledTranslated = true; // Existing engine shortcut path must also respect Off.
            Assert.False(controller.PrimaryTranslationEnabled);
            await Assert.ThrowsAsync<InvalidOperationException>(() => controller.ToggleTranslationAsync(0));
            controller.ApplyTranslationSettings(new() { SentenceProvider = "GoogleV1", TargetLanguage = TargetLanguage.Russian });
            player.SubtitlesManager[0].Translator.ServiceFactoryOverride = () => first;
            player.SubtitlesManager[1].Translator.ServiceFactoryOverride = () => second;
            var primary = AddCue(player, 0, "primary original"); var secondary = AddCue(player, 1, "secondary original");
            Dispatcher.UIThread.RunJobs();
            await vm.TogglePrimaryTranslationCommand.ExecuteAsync(null);
            await Wait(() => vm.PrimaryText == "primary translation");
            Assert.True(controller.PrimaryTranslationEnabled); Assert.False(controller.SecondaryTranslationEnabled);
            Assert.True(controller.PrimaryTextTranslated);
            Assert.Equal("primary translation", controller.GetCues(0).Single().Text);
            Assert.True(controller.GetCues(0).Single().IsTranslated);
            Assert.Equal("en", controller.GetCues(0).Single().SourceLanguage); Assert.Equal("ru", controller.GetCues(0).Single().TargetLanguage);
            Assert.Equal("primary original", primary.Text);
            await vm.ToggleSecondaryTranslationCommand.ExecuteAsync(null);
            await Wait(() => vm.SecondaryText == "secondary translation");
            Assert.True(controller.SecondaryTextTranslated); Assert.True(controller.GetCues(1).Single().IsTranslated);
            string? output = Environment.GetEnvironmentVariable("LLPLAYER_TRANSLATION_ARTIFACTS");
            if (output != null)
            {
                Directory.CreateDirectory(output);
                foreach (string theme in new[] { "Dark", "Light" })
                {
                    ThemeService.Apply(theme); Dispatcher.UIThread.RunJobs();
                    var secondaryControl = window.FindControl<SubtitleText>("SecondarySubtitle")!;
                    await Wait(() => window.PrimarySubtitle.Text == "primary translation" && secondaryControl.Text == "secondary translation"
                        && window.PrimarySubtitle.Bounds.Width > 0 && secondaryControl.Bounds.Width > 0);
                    Assert.True(window.PrimarySubtitle.IsEffectivelyVisible); Assert.True(secondaryControl.IsEffectivelyVisible);
                    Assert.NotNull(window.PrimarySubtitle.Fill); Assert.NotNull(secondaryControl.Fill);
                    // Control state/layout can lead the asynchronous headless compositor by a frame.
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);
                    string capture = Path.Combine(output, $"subtitle-translated-FINAL-{theme.ToLowerInvariant()}.png");
                    window.CaptureRenderedFrame()!.Save(capture, PngBitmapEncoderOptions.Default);
                    AssertRenderedText(capture, window, window.PrimarySubtitle);
                    AssertRenderedText(capture, window, secondaryControl);
                }
            }
            await vm.TogglePrimaryTranslationCommand.ExecuteAsync(null);
            await Wait(() => vm.PrimaryText == "primary original");
            Assert.False(vm.PrimaryTextTranslated); Assert.False(controller.GetCues(0).Single().IsTranslated);
            Assert.Equal("primary original", controller.GetCues(0).Single().Text);
            Assert.True(controller.SecondaryTranslationEnabled);
            await vm.ToggleSecondaryTranslationCommand.ExecuteAsync(null);
            await Wait(() => vm.SecondaryText == "secondary original");
            Assert.False(vm.SecondaryTextTranslated); Assert.Equal("secondary original", secondary.Text);
        }
        finally { window.Close(); vm.Dispose(); await vm.WordPopup.DisposeAsync(); await controller.DisposeAsync(); player.Dispose(); }
    }

    [AvaloniaFact]
    public async Task Same_provider_save_recreates_endpoint_and_failure_preserves_sentence_cache()
    {
        var (player, controller) = Create();
        using TempDir dir = new();
        var settings = new TranslationSettings { SentenceProvider = "GoogleV1", TargetLanguage = TargetLanguage.Russian };
        controller.ApplyTranslationSettings(settings);
        var old = new Provider("old endpoint"); var fresh = new Provider("new endpoint"); var chatFresh = new Provider("new chat"); int creates = 0;
        player.SubtitlesManager[0].Translator.ServiceFactoryOverride = () => ++creates switch { 1 => old, 2 => fresh, _ => chatFresh };
        var cue = AddCue(player, 0, "original");
        var words = new FlyleafWordTranslator(player, settings);
        try
        {
            await controller.ToggleTranslationAsync(0);
            await Wait(() => controller.PrimaryText == "old endpoint");
            var changed = settings.Clone();
            changed.Providers[TranslateServiceType.GoogleV1] = new GoogleV1TranslateSettings { Endpoint = "https://example.invalid/new" };
            await Assert.ThrowsAnyAsync<IOException>(() => controller.SaveTranslationAsync(() => words.SaveAndApplyAsync(changed,
                new TranslationSettingsStore(dir.Path), () => { }, controller.ApplyTranslationSettings)));
            Assert.Equal("old endpoint", cue.TranslatedText); Assert.Equal(1, creates); Assert.False(old.Disposed);
            await controller.SaveTranslationAsync(() => words.SaveAndApplyAsync(changed,
                new TranslationSettingsStore(Path.Combine(dir.Path, "translation.json")), () => { }, controller.ApplyTranslationSettings));
            await Wait(() => controller.PrimaryText == "new endpoint");
            Assert.True(old.Disposed); Assert.Equal(2, creates);
            Assert.Equal("https://example.invalid/new", ((GoogleV1TranslateSettings)player.Config.Subtitles.TranslateServiceSettings[TranslateServiceType.GoogleV1]).Endpoint);
            Assert.True(controller.PrimaryTextTranslated);
            var chatOnly = changed.Clone();
            chatOnly.Chat.TranslateMethod = ChatTranslateMethod.KeepContext;
            chatOnly.Chat.PromptKeepContext = "edited shared chat";
            chatOnly.Chat.SubtitleContextCount = 0;
            await controller.SaveTranslationAsync(() => words.SaveAndApplyAsync(chatOnly,
                new TranslationSettingsStore(Path.Combine(dir.Path, "translation.json")), () => { }, controller.ApplyTranslationSettings));
            await Wait(() => controller.PrimaryText == "new chat");
            Assert.True(fresh.Disposed); Assert.Equal(3, creates);
            Assert.Equal("new chat", cue.TranslatedText);
            Assert.Equal(ChatTranslateMethod.KeepContext, player.Config.Subtitles.TranslateChatConfig.TranslateMethod);
            Assert.Equal("edited shared chat", player.Config.Subtitles.TranslateChatConfig.PromptKeepContext);
            Assert.Equal(0, player.Config.Subtitles.TranslateChatConfig.SubtitleContextCount);
            await controller.SaveTranslationAsync(() => words.SaveAndApplyAsync(new() { SentenceProvider = "Off" },
                new TranslationSettingsStore(Path.Combine(dir.Path, "translation.json")), () => { }, controller.ApplyTranslationSettings));
            await Wait(() => controller.PrimaryText == "original");
            Assert.False(controller.PrimaryTranslationEnabled); Assert.False(controller.PrimaryTextTranslated);
        }
        finally { await words.DisposeAsync(); await controller.DisposeAsync(); player.Dispose(); }
    }

    [AvaloniaFact]
    public async Task Media_mutation_and_shutdown_drain_uncooperative_replies_before_disposal_or_cache_commit()
    {
        var (player, controller) = Create();
        controller.ApplyTranslationSettings(new() { SentenceProvider = "GoogleV1" });
        var old = new Provider("") { HeldReply = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        player.SubtitlesManager[0].Translator.ServiceFactoryOverride = () => old;
        var oldCue = AddCue(player, 0, "old track");
        Provider? fresh = null;
        try
        {
            await controller.ToggleTranslationAsync(0);
            await old.Started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            controller.Open(Path.Combine(Path.GetTempPath(), "llplayer-missing-media-" + Guid.NewGuid().ToString("N") + ".mp4"));
            await Wait(() => old.Token.IsCancellationRequested);
            Assert.False(old.Disposed); Assert.Null(oldCue.TranslatedText);
            old.HeldReply.SetResult("stale track reply");
            await Wait(() => old.Disposed && player.SubtitlesManager[0].Subs.Count == 0);
            Assert.Null(oldCue.TranslatedText);
            fresh = new Provider("") { HeldReply = new(TaskCreationOptions.RunContinuationsAsynchronously) };
            player.SubtitlesManager[0].Translator.ServiceFactoryOverride = () => fresh;
            var newCue = AddCue(player, 0, "new track");
            if (!controller.PrimaryTranslationEnabled) await controller.ToggleTranslationAsync(0);
            await fresh.Started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Task shutdown = controller.DisposeAsync().AsTask();
            await Wait(() => fresh.Token.IsCancellationRequested);
            Assert.False(shutdown.IsCompleted); Assert.False(fresh.Disposed);
            fresh.HeldReply.SetResult("stale shutdown reply");
            await shutdown.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.Null(newCue.TranslatedText); Assert.True(fresh.Disposed);
        }
        finally
        {
            old.HeldReply.TrySetResult("cleanup"); fresh?.HeldReply?.TrySetResult("cleanup");
            await controller.DisposeAsync(); player.Dispose();
        }
    }

    [AvaloniaFact]
    public async Task Provider_configuration_error_turns_slot_off_and_surfaces_error()
    {
        var (player, controller) = Create();
        string? error = null; controller.ErrorOccurred += message => error = message;
        controller.ApplyTranslationSettings(new() { SentenceProvider = "GoogleV1" });
        player.SubtitlesManager[1].Translator.ServiceFactoryOverride = () => new Provider("") { Failure = new TranslationConfigException("deterministic provider configuration error") };
        var cue = AddCue(player, 1, "original");
        try
        {
            await controller.ToggleTranslationAsync(1);
            await Wait(() => error != null && !controller.SecondaryTranslationEnabled);
            Assert.Contains("deterministic provider configuration error", error);
            Assert.Null(cue.TranslatedText); Assert.False(controller.SecondaryTextTranslated);
        }
        finally { await controller.DisposeAsync(); player.Dispose(); }
    }

    static void AssertRenderedText(string path, Window window, SubtitleText control)
    {
        using var bitmap = SKBitmap.Decode(path);
        Point origin = control.TranslatePoint(default, window)!.Value;
        double scale = window.RenderScaling;
        int left = Math.Max(0, (int)(origin.X * scale)); int top = Math.Max(0, (int)(origin.Y * scale));
        int right = Math.Min(bitmap.Width, (int)((origin.X + control.Bounds.Width) * scale));
        int bottom = Math.Min(bitmap.Height, (int)((origin.Y + control.Bounds.Height) * scale));
        int glyphPixels = 0;
        for (int y = top; y < bottom; y++) for (int x = left; x < right; x++)
        {
            var pixel = bitmap.GetPixel(x, y);
            if (pixel.Red > 180 && pixel.Green > 180 && pixel.Blue > 180) glyphPixels++;
        }
        Assert.True(glyphPixels > 50, $"Actual composed subtitle glyphs missing inside {control.Name} bounds ({glyphPixels} pixels) in {Path.GetFileName(path)}.");
    }

    static async Task Wait(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            Dispatcher.UIThread.RunJobs();
            if (DateTime.UtcNow > deadline) throw new TimeoutException();
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
        Dispatcher.UIThread.RunJobs();
    }

    sealed class Provider(string text) : ITranslateService
    {
        public TranslateServiceType ServiceType => TranslateServiceType.GoogleV1;
        public Exception? Failure;
        public bool Disposed;
        public TaskCompletionSource<string>? HeldReply;
        public TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken Token;
        public void Initialize(Language src, TargetLanguage target) { }
        public Task<string> TranslateAsync(string source, CancellationToken token)
        {
            Token = token; Started.TrySetResult();
            return HeldReply?.Task ?? (Failure == null ? Task.FromResult(text) : Task.FromException<string>(Failure));
        }
        public void Dispose() => Disposed = true;
    }
}
