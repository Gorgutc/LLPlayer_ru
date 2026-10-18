using FlyleafLib.MediaPlayer.AI;
using LLPlayer.Core.Services;
using FlyleafLib;
using FlyleafLib.MediaPlayer;
using FlyleafLib.MediaPlayer.Translation;
using FlyleafLib.MediaPlayer.Translation.Services;

namespace LLPlayer.Avalonia.Services;

public enum WordTranslationStatus
{
    Translated,
    /// <summary>No usable translator: service off, unknown / same source language, or a configuration error.</summary>
    NotConfigured,
    Failed,
}

public sealed record WordTranslationResult(WordTranslationStatus Status, string? Text, string? Detail = null);

/// <summary>Translates a clicked subtitle word (word popup).</summary>
public interface IWordTranslator
{
    Task<WordTranslationResult> TranslateAsync(string word, int slot, CancellationToken token);
    WordClickSnapshot Capture(string word, string context, int slot, bool translated) => new(word, context, slot, "", "", translated);
    Task<DictionaryEntry> DefineAsync(WordClickSnapshot click, CancellationToken token) => Task.FromResult(DictionaryEntry.Empty);
}

/// <summary>Lazy word translator with generation invalidation and terminal asynchronous ownership.</summary>
public sealed class FlyleafWordTranslator : IWordTranslator, IAsyncDisposable
{
    readonly object sync = new();
    readonly SemaphoreSlim gate = new(1, 1);
    readonly SemaphoreSlim definitionGate = new(1, 1);
    readonly Dictionary<string, DictionaryEntry> definitionCache = [];
    WordDefinitionService? definitionService;
    long definitionServiceVersion = -1;
    readonly Func<TranslationSettings, WordDefinitionService> definitionFactory;
    readonly Dictionary<string, string> cache = new(StringComparer.Ordinal);
    readonly Dictionary<CancellationTokenSource, Task> operations = [];
    readonly HashSet<Task> settingsOperations = [];
    readonly Func<int, Language?> sourceLanguage;
    readonly Func<TranslationSettings, TranslateServiceType, ITranslateService> factory;
    TranslationSettings settings;
    ITranslateService? service;
    string? serviceKey;
    long generation;
    bool stopping;
    Task? shutdown;

    public FlyleafWordTranslator(Player player, TranslationSettings settings)
        : this(slot => player.SubtitlesManager[slot].Language, settings, (snapshot, type) =>
        {
            var config = new Config(true).Subtitles;
            snapshot.Apply(config);
            return new TranslateServiceFactory(config).GetService(type, wordMode: true);
        }) { }

    // Pure source/factory seam: tests never require a player, network, or credentials.
    public FlyleafWordTranslator(Func<int, Language?> sourceLanguage, TranslationSettings settings,
        Func<TranslationSettings, TranslateServiceType, ITranslateService> factory,
        Func<TranslationSettings, WordDefinitionService>? definitionFactory = null)
    {
        this.sourceLanguage = sourceLanguage;
        this.settings = settings.Clone();
        this.factory = factory;
        this.definitionFactory = definitionFactory ?? (snapshot =>
        {
            var config = new Config(true).Subtitles; snapshot.Apply(config);
            return WordDefinitionService.ForConfig(config);
        });
    }
    public TranslationSettings Settings { get { lock (sync) return settings.Clone(); } }

    public WordClickSnapshot Capture(string word, string context, int slot, bool translated)
    {
        lock (sync) return new(word, context, slot, sourceLanguage(slot)?.ISO6391 ?? "", settings.TargetLanguage.ToISO6391(), translated);
    }
    public Task<DictionaryEntry> DefineAsync(WordClickSnapshot click, CancellationToken token)
    {
        lock (sync)
        {
            if (stopping) return Task.FromCanceled<DictionaryEntry>(new CancellationToken(true));
            var mine = CancellationTokenSource.CreateLinkedTokenSource(token);
            var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            operations.Add(mine, done.Task);
            return DefineOwnedAsync(click, mine, done, generation, settings.Clone());
        }
    }
    async Task<DictionaryEntry> DefineOwnedAsync(WordClickSnapshot click, CancellationTokenSource mine,
        TaskCompletionSource done, long version, TranslationSettings snapshot)
    {
        bool entered = false;
        try
        {
            mine.Token.ThrowIfCancellationRequested();
            if (click.IsTranslated || string.IsNullOrEmpty(click.SourceLanguage) || snapshot.DictionaryMode == WordDefinitionServiceType.Off)
                return DictionaryEntry.Empty;
            await definitionGate.WaitAsync(mine.Token).ConfigureAwait(false);
            entered = true;
            string key = $"{version}|{click.SourceLanguage}|{click.TargetLanguage}|{click.Term.ToLowerInvariant()}|{click.Context}";
            lock (sync)
            {
                CheckCurrent();
                if (definitionCache.TryGetValue(key, out var cached)) return cached;
                if (definitionService == null || definitionServiceVersion != version)
                {
                    definitionService?.Dispose();
                    definitionService = definitionFactory(snapshot);
                    definitionServiceVersion = version;
                }
            }
            var chosen = WordDefinitionSelector.Select(snapshot.DictionaryMode, click.SourceLanguage, definitionService.LlmAvailable);
            var entry = await definitionService.GetDefinitionAsync(chosen,
                WordDefinitionSelector.AllowLlmFallback(snapshot.DictionaryMode, chosen, definitionService.LlmAvailable),
                click.Term, click.SourceLanguage, click.TargetLanguage, click.Context, mine.Token).ConfigureAwait(false);
            lock (sync) { CheckCurrent(); definitionCache[key] = entry; }
            return entry;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { lock (sync) CheckCurrent(); return DictionaryEntry.Empty; }
        finally
        {
            if (entered) definitionGate.Release();
            lock (sync) { operations.Remove(mine); mine.Dispose(); done.SetResult(); }
        }
        void CheckCurrent()
        {
            if (stopping || generation != version || sourceLanguage(click.Slot)?.ISO6391 != click.SourceLanguage) throw new OperationCanceledException();
            mine.Token.ThrowIfCancellationRequested();
        }
    }
    public Task<WordTranslationResult> TranslateAsync(string word, int slot, CancellationToken token)
    {
        lock (sync)
        {
            if (stopping) return Task.FromCanceled<WordTranslationResult>(new CancellationToken(true));
            var mine = CancellationTokenSource.CreateLinkedTokenSource(token);
            var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            operations.Add(mine, done.Task);
            return RunAsync(word, slot, mine, done, generation, settings.Clone());
        }
    }
    async Task<WordTranslationResult> RunAsync(string word, int slot, CancellationTokenSource mine,
        TaskCompletionSource done, long version, TranslationSettings snapshot)
    {
        bool entered = false;
        Language? src = sourceLanguage(slot);
        string? srcCode = src?.ISO6391;
        var token = mine.Token;
        try
        {
            token.ThrowIfCancellationRequested();
            if (snapshot.WordProvider == "Off") return new(WordTranslationStatus.NotConfigured, null, "Word translation is off.");
            if (src == null || src == Language.Unknown) return new(WordTranslationStatus.NotConfigured, null, "The subtitle language is unknown.");
            if (srcCode == snapshot.TargetLanguage.ToISO6391()) return new(WordTranslationStatus.NotConfigured, null, "The subtitles are already in the target language.");
            var type = Enum.Parse<TranslateServiceType>(snapshot.WordProvider);
            string key = $"{version}|{srcCode}|{snapshot.TargetLanguage}|{word.ToLowerInvariant()}";
            await gate.WaitAsync(token).ConfigureAwait(false);
            entered = true;
            lock (sync)
            {
                CheckCurrent();
                if (cache.TryGetValue(key, out var cached)) return new(WordTranslationStatus.Translated, cached);
            }
            string wanted = $"{version}|{srcCode}|{snapshot.TargetLanguage}";
            if (service == null || serviceKey != wanted)
            {
                service?.Dispose();
                service = null;
                var created = factory(snapshot, type);
                try { created.Initialize(src, snapshot.TargetLanguage); }
                catch { created.Dispose(); throw; }
                service = created;
                serviceKey = wanted;
            }
            string result = await service.TranslateAsync(word, token).ConfigureAwait(false);
            lock (sync)
            {
                CheckCurrent();
                cache[key] = result;
                return new(WordTranslationStatus.Translated, result);
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (TranslationConfigException)
        {
            lock (sync) CheckCurrent();
            return new(WordTranslationStatus.NotConfigured, null, "Check the provider, language and required credentials in Translation settings.");
        }
        catch (Exception)
        {
            lock (sync) CheckCurrent();
            return new(WordTranslationStatus.Failed, null, "The provider could not translate this word. Check its settings and availability.");
        }
        finally
        {
            if (entered) gate.Release();
            lock (sync) { operations.Remove(mine); mine.Dispose(); done.SetResult(); }
        }
        void CheckCurrent()
        {
            if (stopping || version != generation || sourceLanguage(slot)?.ISO6391 != srcCode) throw new OperationCanceledException();
            token.ThrowIfCancellationRequested();
        }
    }
    public void Invalidate()
    {
        lock (sync)
        {
            generation++;
            cache.Clear();
            definitionCache.Clear();
            foreach (var operation in operations.Keys.ToArray()) operation.Cancel();
        }
    }
    /// <summary>Persistence succeeds before live invalidation. No provider is initialized on save.</summary>
    public Task SaveAndApplyAsync(TranslationSettings draft, TranslationSettingsStore store, Action invalidateDisplay, Action<TranslationSettings> apply)
    {
        lock (sync)
        {
            if (stopping) return Task.FromCanceled(new CancellationToken(true));
            var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            settingsOperations.Add(done.Task);
            return ApplyAsync(draft, store, invalidateDisplay, apply, done);
        }
    }
    async Task ApplyAsync(TranslationSettings draft, TranslationSettingsStore store, Action invalidateDisplay,
        Action<TranslationSettings> apply, TaskCompletionSource done)
    {
        try
        {
        var copy = draft.Clone();
        copy.Validate();
        lock (sync)
        {
            if (stopping) throw new OperationCanceledException();
            store.Save(copy);
            // Publish snapshot and generation under the admission lock. Lookups accepted while the old
            // provider drains must capture the persisted settings rather than old settings with a new epoch.
            settings = copy;
            Invalidate();
        }
        invalidateDisplay();
        await definitionGate.WaitAsync().ConfigureAwait(true);
        try { definitionService?.Dispose(); definitionService = null; }
        finally { definitionGate.Release(); }
        await gate.WaitAsync().ConfigureAwait(true);
        try
        {
            lock (sync)
            {
                if (stopping) throw new OperationCanceledException();
                service?.Dispose(); service = null; serviceKey = null;
                apply(copy);
            }
        }
        finally { gate.Release(); }
        }
        finally { lock (sync) { settingsOperations.Remove(done.Task); done.SetResult(); } }
    }
    public ValueTask DisposeAsync()
    {
        lock (sync)
        {
            if (shutdown != null) return new(shutdown);
            stopping = true;
            Invalidate();
            shutdown = DrainAsync([.. operations.Values, .. settingsOperations]);
            return new(shutdown);
        }
    }
    async Task DrainAsync(Task[] consumers)
    {
        await Task.WhenAll(consumers).ConfigureAwait(false);
        await gate.WaitAsync().ConfigureAwait(false);
        try { service?.Dispose(); service = null; }
        finally { gate.Release(); }
        gate.Dispose();
        definitionService?.Dispose(); definitionService = null;
        definitionGate.Dispose();
    }
}
