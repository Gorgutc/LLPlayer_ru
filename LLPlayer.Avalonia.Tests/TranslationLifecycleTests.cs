using FlyleafLib;
using FlyleafLib.MediaPlayer.Translation;
using FlyleafLib.MediaPlayer.Translation.Services;
using LLPlayer.Core.Services;
using LLPlayer.Avalonia.Services;
using LLPlayer.Avalonia.ViewModels;

namespace LLPlayer.Avalonia.Tests;

public class TranslationLifecycleTests
{
    [Fact]
    public async Task Supersede_keeps_canceled_token_source_alive_until_consumer_settles()
    {
        TaskCompletionSource<WordTranslationResult> late = new();
        CancellationToken captured = default;
        var translator = new FakeTranslator((word, _, token) =>
        {
            if (word != "old") return Task.FromResult(new WordTranslationResult(WordTranslationStatus.Translated, "new"));
            captured = token;
            return late.Task;
        });
        var popup = new WordPopupViewModel(translator, _ => { });
        Task old = popup.OpenAsync("old", "", 0, 0, 0);
        await popup.OpenAsync("new", "", 0, 0, 0);
        // An uncooperative provider can still register or obtain WaitHandle after cancellation.
        using var registration = captured.Register(() => { });
        Assert.True(captured.WaitHandle.WaitOne(0));
        late.SetResult(new(WordTranslationStatus.Translated, "stale"));
        await old;
        Assert.Equal("new", popup.Translation);
    }

    [Fact]
    public async Task Late_canceled_provider_result_is_not_cached_and_disposal_drains_then_rejects_admission()
    {
        var late = new TaskCompletionSource<string>();
        var provider = new Provider { Response = _ => late.Task };
        var translator = Translator(provider);
        using var cts = new CancellationTokenSource();
        Task first = translator.TranslateAsync("word", 0, cts.Token);
        cts.Cancel();
        Assert.False(provider.Disposed);
        Assert.True(provider.Token.WaitHandle.WaitOne(0));
        late.SetResult("stale");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        provider.Response = _ => Task.FromResult("fresh");
        Assert.Equal("fresh", (await translator.TranslateAsync("word", 0, TestContext.Current.CancellationToken)).Text);
        Assert.Equal(2, provider.Calls);
        Assert.Equal("fresh", (await translator.TranslateAsync("word", 0, TestContext.Current.CancellationToken)).Text);
        Assert.Equal(2, provider.Calls);

        late = new(); provider.Response = _ => late.Task;
        Task active = translator.TranslateAsync("other", 0, TestContext.Current.CancellationToken);
        Task drain = translator.DisposeAsync().AsTask();
        Assert.False(drain.IsCompleted); Assert.False(provider.Disposed);
        Assert.True(provider.Token.WaitHandle.WaitOne(0));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => translator.TranslateAsync("rejected", 0, TestContext.Current.CancellationToken));
        late.SetResult("late");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => active);
        await drain;
        Assert.True(provider.Disposed);
        await translator.DisposeAsync();
    }

    [Fact]
    public async Task Same_provider_save_cancels_display_and_retires_service_only_after_consumers_finish()
    {
        using TempDir dir = new();
        var late = new TaskCompletionSource<string>();
        var old = new Provider { Response = _ => late.Task };
        var fresh = new Provider { Response = _ => Task.FromResult("new settings") };
        int creates = 0;
        var translator = new FlyleafWordTranslator(_ => Language.English,
            new() { TargetLanguage = TargetLanguage.Russian }, (_, _) => ++creates == 1 ? old : fresh);
        Task active = translator.TranslateAsync("word", 0, TestContext.Current.CancellationToken);
        var draft = translator.Settings;
        draft.Providers[TranslateServiceType.GoogleV1] = new GoogleV1TranslateSettings { Endpoint = "https://example.invalid/changed" };
        draft.Chat.PromptOneByOne = "edited shared chat";
        bool invalidated = false; TranslationSettings? live = null;
        Task save = translator.SaveAndApplyAsync(draft, new TranslationSettingsStore(Path.Combine(dir.Path, "translation.json")),
            () => invalidated = true, copy => live = copy);
        Assert.True(invalidated); Assert.False(save.IsCompleted); Assert.False(old.Disposed); Assert.Null(live);
        late.SetResult("old settings");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => active);
        await save;
        Assert.True(old.Disposed); Assert.NotNull(live);
        Assert.Equal("edited shared chat", live.Chat.PromptOneByOne);
        Assert.Equal("edited shared chat", translator.Settings.Chat.PromptOneByOne);
        Assert.Equal("new settings", (await translator.TranslateAsync("word", 0, TestContext.Current.CancellationToken)).Text);
        Assert.Equal(2, creates); Assert.Equal(1, fresh.Calls);
        await translator.DisposeAsync();
    }

    [Fact]
    public async Task Click_admitted_during_save_drain_uses_persisted_snapshot_not_retired_settings()
    {
        using TempDir dir = new();
        var late = new TaskCompletionSource<string>();
        var old = new Provider { Response = _ => late.Task };
        var fresh = new Provider { Response = _ => Task.FromResult("fresh") };
        string? initializedEndpoint = null;
        int creates = 0;
        var translator = new FlyleafWordTranslator(_ => Language.English,
            new() { TargetLanguage = TargetLanguage.Russian }, (snapshot, _) =>
            {
                if (++creates == 1) return old;
                initializedEndpoint = ((GoogleV1TranslateSettings)snapshot.Providers[TranslateServiceType.GoogleV1]).Endpoint;
                return fresh;
            });
        Task active = translator.TranslateAsync("held", 0, TestContext.Current.CancellationToken);
        var draft = translator.Settings;
        draft.Providers[TranslateServiceType.GoogleV1] = new GoogleV1TranslateSettings { Endpoint = "https://example.invalid/new" };
        Task save = translator.SaveAndApplyAsync(draft, new TranslationSettingsStore(Path.Combine(dir.Path, "translation.json")), () => { }, _ => { });
        Assert.False(save.IsCompleted);
        Task<WordTranslationResult> pendingClick = translator.TranslateAsync("clicked", 0, TestContext.Current.CancellationToken);
        Assert.False(pendingClick.IsCompleted);
        late.SetResult("old");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => active);
        await save;
        Assert.Equal("fresh", (await pendingClick).Text);
        Assert.Equal("https://example.invalid/new", initializedEndpoint);
        Assert.Equal("fresh", (await translator.TranslateAsync("clicked", 0, TestContext.Current.CancellationToken)).Text);
        Assert.Equal(1, fresh.Calls);
        await translator.DisposeAsync();
    }

    [Fact]
    public async Task Failed_save_leaves_live_config_display_and_cache_untouched()
    {
        using TempDir dir = new();
        var provider = new Provider();
        var translator = Translator(provider);
        await translator.TranslateAsync("word", 0, TestContext.Current.CancellationToken);
        bool invalidated = false, applied = false;
        Exception? failure = await Record.ExceptionAsync(() => translator.SaveAndApplyAsync(new() { WordProvider = "Off" },
            new TranslationSettingsStore(dir.Path), () => invalidated = true, _ => applied = true));
        Assert.True(failure is IOException or UnauthorizedAccessException,
            $"Expected IOException or UnauthorizedAccessException, got {failure?.GetType().FullName ?? "no exception"}.");
        Assert.False(invalidated); Assert.False(applied); Assert.False(provider.Disposed);
        Assert.Equal("GoogleV1", translator.Settings.WordProvider);
        await translator.TranslateAsync("word", 0, TestContext.Current.CancellationToken);
        Assert.Equal(1, provider.Calls);
        await translator.DisposeAsync();
    }

    [Fact]
    public async Task Source_language_change_rejects_late_result_before_cache_or_popup_commit()
    {
        var late = new TaskCompletionSource<string>();
        var provider = new Provider { Response = _ => late.Task };
        Language source = Language.English;
        var translator = new FlyleafWordTranslator(_ => source, new() { TargetLanguage = TargetLanguage.Russian }, (_, _) => provider);
        var popup = new WordPopupViewModel(translator, _ => { });
        Task active = popup.OpenAsync("word", "", 0, 0, 0);
        source = Language.Get("fr");
        late.SetResult("stale");
        await active;
        Assert.Null(popup.Translation); Assert.False(popup.IsOpen); Assert.False(popup.IsBusy);
        source = Language.English;
        provider.Response = _ => Task.FromResult("fresh");
        await popup.OpenAsync("word", "", 0, 0, 0);
        Assert.Equal("fresh", popup.Translation); Assert.Equal(2, provider.Calls);
        await popup.DisposeAsync(); await translator.DisposeAsync();
    }

    [Fact]
    public async Task Popup_close_reopen_and_terminal_disposal_own_CTS_until_all_consumers_settle()
    {
        var late = new TaskCompletionSource<WordTranslationResult>();
        CancellationToken captured = default;
        var popup = new WordPopupViewModel(new FakeTranslator((word, _, token) =>
        {
            if (word == "old") { captured = token; return late.Task; }
            return Task.FromResult(new WordTranslationResult(WordTranslationStatus.Translated, "new"));
        }), _ => { });
        Task old = popup.OpenAsync("old", "", 0, 0, 0);
        popup.Close(); Assert.False(popup.IsOpen); Assert.True(captured.WaitHandle.WaitOne(0));
        await popup.OpenAsync("new", "", 0, 0, 0); Assert.Equal("new", popup.Translation);
        Task drain = popup.DisposeAsync().AsTask(); Assert.False(drain.IsCompleted);
        await popup.OpenAsync("rejected", "", 0, 0, 0); Assert.False(popup.IsOpen);
        late.SetResult(new(WordTranslationStatus.Translated, "stale")); await old; await drain;
        Assert.Null(popup.Translation);
        await popup.DisposeAsync();
    }

    static FlyleafWordTranslator Translator(Provider provider) => new(_ => Language.English,
        new() { TargetLanguage = TargetLanguage.Russian }, (_, _) => provider);
    sealed class Provider : ITranslateService
    {
        public TranslateServiceType ServiceType => TranslateServiceType.GoogleV1;
        public int Calls;
        public bool Disposed;
        public CancellationToken Token;
        public Func<string, Task<string>> Response = word => Task.FromResult("[" + word + "]");
        public void Initialize(Language source, TargetLanguage target) { }
        public Task<string> TranslateAsync(string word, CancellationToken token) { Calls++; Token = token; return Response(word); }
        public void Dispose() { Disposed = true; }
    }
}
