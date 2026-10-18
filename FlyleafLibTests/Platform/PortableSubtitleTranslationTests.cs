using System.Reflection;
using FlyleafLib.MediaPlayer;
using FlyleafLib.MediaPlayer.Translation;
using FlyleafLib.MediaPlayer.Translation.Services;

namespace FlyleafLib.Platform;

[Collection("PortableEngine")]
public sealed class PortableSubtitleTranslationTests
{
    static (Config Config, SubManager Manager, SubtitleData Cue) Fixture(Func<ITranslateService> provider)
    {
        if (Engine.Config == null) typeof(Engine).GetProperty(nameof(Engine.Config))!.SetValue(null, new EngineConfig { LogLevel = LogLevel.Quiet });
        Config config = new(true);
        SubManager manager = new(config, 0);
        manager.LanguageSource = Language.English;
        manager.Translator.ServiceFactoryOverride = provider;
        SubtitleData cue = new() { Text = "source", StartTime = TimeSpan.Zero, EndTime = TimeSpan.FromSeconds(1), EnabledTranslated = true };
        manager.Add(cue);
        config.Subtitles[0].EnabledTranslated = true;
        manager.SetCurrentTime(TimeSpan.FromMilliseconds(500));
        return (config, manager, cue);
    }

    [Fact]
    public async Task Disabled_track_rejects_late_provider_reply_before_cue_cache_commit()
    {
        Provider provider = new();
        var (config, manager, cue) = Fixture(() => provider);
        await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        config.Subtitles[0].EnabledTranslated = false;
        Task drain = manager.Translator.SuspendAsync();
        Assert.True(provider.Token.IsCancellationRequested);
        Assert.False(drain.IsCompleted); Assert.False(provider.Disposed);
        // CTS stays alive until the uncooperative consumer settles.
        using var registration = provider.Token.Register(() => { });
        Assert.True(provider.Token.WaitHandle.WaitOne(0));
        provider.Reply.SetResult("stale translation");
        await drain.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Null(cue.TranslatedText); Assert.True(provider.Disposed);
        await manager.Translator.DisposeAsync();
    }

    [Fact]
    public async Task Same_provider_records_chat_target_replacement_recreates_service_and_invalidates_cache()
    {
        var old = new Provider(); var fresh = new Provider();
        int created = 0;
        var (config, manager, cue) = Fixture(() => ++created == 1 ? old : fresh);
        await old.Started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        old.Reply.SetResult("old translation");
        await WaitUntil(() => cue.TranslatedText == "old translation");
        // Awaitable host boundary clears cache even if provider enum itself stays unchanged.
        await manager.Translator.SuspendAsync();
        Assert.Null(cue.TranslatedText); Assert.True(old.Disposed);
        config.Subtitles.TranslateServiceSettings = new() { [TranslateServiceType.GoogleV1] = new GoogleV1TranslateSettings { Endpoint = "https://example.invalid/new" } };
        var chat = new TranslateChatConfig(); config.Subtitles.TranslateChatConfig = chat;
        config.Subtitles.TranslateTargetLanguage = TargetLanguage.Russian;
        manager.Translator.Resume();
        await fresh.Started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        fresh.Reply.SetResult("fresh translation");
        await WaitUntil(() => cue.TranslatedText == "fresh translation");
        Assert.Equal(2, created); Assert.Equal(TargetLanguage.Russian, fresh.Target);
        chat.PromptOneByOne = "new prompt";
        Task drain = manager.Translator.SuspendAsync(); await drain;
        Assert.Null(cue.TranslatedText); Assert.True(fresh.Disposed);
        await manager.Translator.DisposeAsync();
    }

    [Fact]
    public async Task Shutdown_drains_active_reply_and_never_resumes_or_caches_it()
    {
        Provider provider = new();
        var (_, manager, cue) = Fixture(() => provider);
        await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Task shutdown = manager.Translator.DisposeAsync().AsTask();
        Assert.False(shutdown.IsCompleted); Assert.True(provider.Token.IsCancellationRequested);
        provider.Reply.SetResult("late shutdown reply");
        await shutdown.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Null(cue.TranslatedText); Assert.True(provider.Disposed);
        manager.Translator.Resume(); Assert.Equal(1, provider.Calls);
        await manager.Translator.DisposeAsync();
    }

    [Fact]
    public async Task Suspension_cancels_delayed_admission_before_any_provider_exists()
    {
        Provider provider = new();
        var (_, manager, cue) = Fixture(() => provider);
        await manager.Translator.SuspendAsync();
        Assert.Equal(0, provider.Calls); Assert.Null(cue.TranslatedText);
        await manager.Translator.DisposeAsync();
    }

    [Fact]
    public async Task Superseded_delayed_starts_are_owned_and_all_drained_before_dispose_returns()
    {
        Provider provider = new();
        var (_, manager, _) = Fixture(() => provider);
        for (int i = 1; i <= 12; i++)
        {
            manager.Add(new SubtitleData { Text = $"source {i}", StartTime = TimeSpan.FromSeconds(i), EndTime = TimeSpan.FromSeconds(i + 1) });
            manager.SetCurrentTime(TimeSpan.FromSeconds(i + 0.5));
        }
        var starts = ((List<Task>)typeof(SubTranslator).GetField("_startTasks", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(manager.Translator)!).ToArray();
        Assert.NotEmpty(starts);
        await manager.Translator.DisposeAsync();
        Assert.All(starts, task => Assert.True(task.IsCompleted));
        Assert.Equal(0, provider.Calls);
        manager.SetCurrentTime(TimeSpan.FromMilliseconds(500));
        manager.Translator.Resume();
        Assert.Equal(0, provider.Calls);
    }

    [Fact]
    public async Task Track_replacement_cancels_old_cue_then_resumes_current_new_cue()
    {
        var old = new Provider(); var fresh = new Provider(); int creates = 0;
        var (_, manager, cue) = Fixture(() => ++creates == 1 ? old : fresh);
        await old.Started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Task drain = manager.Translator.SuspendAsync();
        Assert.False(drain.IsCompleted);
        old.Reply.SetResult("old track reply"); await drain;
        manager.Clear(); manager.LanguageSource = Language.English;
        SubtitleData replacement = new() { Text = "new track", StartTime = TimeSpan.Zero, EndTime = TimeSpan.FromSeconds(1), EnabledTranslated = true };
        manager.Add(replacement); manager.SetCurrentTime(TimeSpan.FromMilliseconds(500));
        manager.Translator.Resume();
        await fresh.Started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        fresh.Reply.SetResult("new track translation");
        await WaitUntil(() => replacement.TranslatedText == "new track translation");
        Assert.Null(cue.TranslatedText); Assert.Equal(2, creates);
        await manager.Translator.DisposeAsync();
    }

    static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException();
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    sealed class Provider : ITranslateService
    {
        public TranslateServiceType ServiceType => TranslateServiceType.GoogleV1;
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<string> Reply { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken Token;
        public TargetLanguage Target;
        public bool Disposed;
        public int Calls;
        public void Initialize(Language src, TargetLanguage target) { Target = target; }
        public Task<string> TranslateAsync(string text, CancellationToken token) { ++Calls; Token = token; Started.TrySetResult(); return Reply.Task; }
        public void Dispose() => Disposed = true;
    }
}
