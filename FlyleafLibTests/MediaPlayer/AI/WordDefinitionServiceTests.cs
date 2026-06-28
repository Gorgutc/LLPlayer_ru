using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using FlyleafLib.MediaPlayer.AI;
using FlyleafLib.MediaPlayer.Translation.Services;

namespace FlyleafLib.MediaPlayer.AI;

#nullable enable

public class WordDefinitionServiceTests
{
    private static DictionaryEntry DictHit(string term) =>
        new(term, "/r/", [new DictionarySense("noun", "dict definition", "")]);

    // Records each backend's call count and lets a test drive the dict result + LLM reply/exception.
    private sealed class Fakes
    {
        public int DictCalls;
        public int LlmCalls;
        public DictionaryEntry DictResult = DictionaryEntry.Empty;
        public string LlmReply = "/r/ | llm definition";
        public Exception? LlmThrow;

        public WordDefinitionService.DictionaryLookup Dict => (word, ct) =>
        {
            DictCalls++;
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(DictResult);
        };

        public WordDefinitionService.LlmComplete Llm => (messages, ct) =>
        {
            LlmCalls++;
            ct.ThrowIfCancellationRequested();
            if (LlmThrow != null)
                throw LlmThrow;
            return Task.FromResult(LlmReply);
        };
    }

    [Fact]
    public async Task DictionaryApi_CallsOnlyDict()
    {
        Fakes f = new() { DictResult = DictHit("dog") };
        using WordDefinitionService svc = new(f.Dict, f.Llm);

        DictionaryEntry e = await svc.GetDefinitionAsync(
            WordDefinitionProvider.DictionaryApi, false, "dog", "English", "Russian", "ctx", CancellationToken.None);

        e.Senses[0].Definition.Should().Be("dict definition");
        f.DictCalls.Should().Be(1);
        f.LlmCalls.Should().Be(0);
    }

    [Fact]
    public async Task Llm_CallsOnlyLlm_AndParsesReply()
    {
        Fakes f = new();
        using WordDefinitionService svc = new(f.Dict, f.Llm);

        DictionaryEntry e = await svc.GetDefinitionAsync(
            WordDefinitionProvider.Llm, false, "perro", "Spanish", "English", "ctx", CancellationToken.None);

        e.Senses[0].Definition.Should().Be("llm definition");
        f.LlmCalls.Should().Be(1);
        f.DictCalls.Should().Be(0);
    }

    [Fact]
    public async Task Auto_DictEmpty_FallsBackToLlm_WhenAllowed()
    {
        Fakes f = new() { DictResult = DictionaryEntry.Empty };
        using WordDefinitionService svc = new(f.Dict, f.Llm);

        DictionaryEntry e = await svc.GetDefinitionAsync(
            WordDefinitionProvider.DictionaryApi, allowLlmFallback: true, "cat", "English", "Russian", "ctx", CancellationToken.None);

        e.Senses[0].Definition.Should().Be("llm definition");
        f.DictCalls.Should().Be(1);
        f.LlmCalls.Should().Be(1);
    }

    [Fact]
    public async Task Auto_DictHit_DoesNotCallLlm()
    {
        Fakes f = new() { DictResult = DictHit("dog") };
        using WordDefinitionService svc = new(f.Dict, f.Llm);

        DictionaryEntry e = await svc.GetDefinitionAsync(
            WordDefinitionProvider.DictionaryApi, allowLlmFallback: true, "dog", "English", "Russian", "ctx", CancellationToken.None);

        e.Senses[0].Definition.Should().Be("dict definition");
        f.LlmCalls.Should().Be(0);
    }

    [Fact]
    public async Task DictEmpty_NoFallback_ReturnsEmpty()
    {
        Fakes f = new() { DictResult = DictionaryEntry.Empty };
        using WordDefinitionService svc = new(f.Dict, f.Llm);

        DictionaryEntry e = await svc.GetDefinitionAsync(
            WordDefinitionProvider.DictionaryApi, allowLlmFallback: false, "zzz", "English", "Russian", "ctx", CancellationToken.None);

        e.IsEmpty.Should().BeTrue();
        f.LlmCalls.Should().Be(0);
    }

    [Fact]
    public async Task None_CallsNeitherBackend()
    {
        Fakes f = new() { DictResult = DictHit("dog") };
        using WordDefinitionService svc = new(f.Dict, f.Llm);

        DictionaryEntry e = await svc.GetDefinitionAsync(
            WordDefinitionProvider.None, true, "dog", "English", "Russian", "ctx", CancellationToken.None);

        e.IsEmpty.Should().BeTrue();
        f.DictCalls.Should().Be(0);
        f.LlmCalls.Should().Be(0);
    }

    [Fact]
    public async Task Llm_NotConfigured_ReturnsEmpty()
    {
        Fakes f = new();
        using WordDefinitionService svc = new(f.Dict, llm: null);

        DictionaryEntry e = await svc.GetDefinitionAsync(
            WordDefinitionProvider.Llm, false, "x", "Spanish", "English", "ctx", CancellationToken.None);

        e.IsEmpty.Should().BeTrue();
        svc.LlmAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task Llm_ThrowsTranslationException_SwallowedToEmpty()
    {
        Fakes f = new() { LlmThrow = new TranslationException("boom") { Kind = TranslationFailureKind.Truncated } };
        using WordDefinitionService svc = new(f.Dict, f.Llm);

        DictionaryEntry e = await svc.GetDefinitionAsync(
            WordDefinitionProvider.Llm, false, "x", "Spanish", "English", "ctx", CancellationToken.None);

        e.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public async Task Llm_ThrowsConfigException_SwallowedToEmpty()
    {
        Fakes f = new() { LlmThrow = new TranslationConfigException("no llm") };
        using WordDefinitionService svc = new(f.Dict, f.Llm);

        DictionaryEntry e = await svc.GetDefinitionAsync(
            WordDefinitionProvider.Llm, false, "x", "Spanish", "English", "ctx", CancellationToken.None);

        e.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public async Task Llm_ThrowsNonTranslationException_SwallowedToEmpty()
    {
        // Fail-soft: ANY non-cancellation exception from the LLM delegate (e.g. a malformed-endpoint
        // UriFormatException from GetHttpClient's new Uri(Endpoint)) must degrade to Empty, never escape the
        // hot interactive path. RED-without-fix: dropping the broad catch in LlmDefineAsync fails this.
        Fakes f = new() { LlmThrow = new UriFormatException("bad endpoint") };
        using WordDefinitionService svc = new(f.Dict, f.Llm);

        DictionaryEntry e = await svc.GetDefinitionAsync(
            WordDefinitionProvider.Llm, false, "x", "Spanish", "English", "ctx", CancellationToken.None);

        e.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public async Task DictionaryApi_Cancellation_Propagates()
    {
        // Cancellation must propagate on the dict path too (not only the Llm path), so a re-click cancels it.
        Fakes f = new() { DictResult = DictHit("dog") };
        using WordDefinitionService svc = new(f.Dict, f.Llm);
        using CancellationTokenSource cts = new();
        cts.Cancel();

        Func<Task> act = async () => await svc.GetDefinitionAsync(
            WordDefinitionProvider.DictionaryApi, allowLlmFallback: true, "dog", "English", "Russian", "ctx", cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task DictEmpty_FallbackAllowed_ButNoLlm_ReturnsEmpty()
    {
        // The `&& _llm != null` guard: a fallback requested with no LLM configured returns Empty, no NRE.
        Fakes f = new() { DictResult = DictionaryEntry.Empty };
        using WordDefinitionService svc = new(f.Dict, llm: null);

        DictionaryEntry e = await svc.GetDefinitionAsync(
            WordDefinitionProvider.DictionaryApi, allowLlmFallback: true, "zzz", "English", "Russian", "ctx", CancellationToken.None);

        e.IsEmpty.Should().BeTrue();
        f.DictCalls.Should().Be(1);
    }

    [Fact]
    public async Task Cancellation_Propagates()
    {
        Fakes f = new();
        using WordDefinitionService svc = new(f.Dict, f.Llm);
        using CancellationTokenSource cts = new();
        cts.Cancel();

        Func<Task> act = async () => await svc.GetDefinitionAsync(
            WordDefinitionProvider.Llm, false, "x", "Spanish", "English", "ctx", cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
