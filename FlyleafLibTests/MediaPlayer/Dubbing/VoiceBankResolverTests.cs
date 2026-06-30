using AwesomeAssertions;

namespace FlyleafLib.MediaPlayer.Dubbing;

public class VoiceBankResolverTests
{
    // ---- BuiltIn: the canonical bank (hand-mirror of dub_sidecar/server.py VOICES) ----
    // This pins the C# bank's ids/order so an accidental C# edit fails; it does not parse server.py,
    // so the two lists must still be kept in lockstep by hand (see the cross-ref comment in both files).

    [Fact]
    public void BuiltIn_MirrorsServerPyPresets_InOrder()
    {
        VoiceBankResolver.BuiltIn.Should().HaveCount(2);

        VoiceBankResolver.BuiltIn[0].Should().Be(new TtsVoice("ru-preset-1", "Russian Narrator (M)", "male", "ru"));
        VoiceBankResolver.BuiltIn[1].Should().Be(new TtsVoice("ru-preset-2", "Russian Narrator (F)", "female", "ru"));
    }

    [Fact]
    public void BuiltIn_FirstEntry_IsTheDefaultVoiceId()
    {
        // Guards frozen-safe default selection: the picker pre-selects DubbingConfig.DefaultVoiceId,
        // which must be present (and is the first/top entry) so the default install shows it.
        string defaultVoiceId = new DubbingConfig().DefaultVoiceId;

        defaultVoiceId.Should().Be("ru-preset-1");
        VoiceBankResolver.BuiltIn[0].Id.Should().Be(defaultVoiceId);
        VoiceBankResolver.Resolve(defaultVoiceId).Should().NotBeNull();
    }

    // ---- Resolve: exact-id lookup ----

    [Theory]
    [InlineData("ru-preset-1", "Russian Narrator (M)")]
    [InlineData("ru-preset-2", "Russian Narrator (F)")]
    [InlineData("RU-PRESET-1", "Russian Narrator (M)")] // case-insensitive on Id
    public void Resolve_KnownId_ReturnsVoice(string id, string expectedDisplayName)
    {
        VoiceBankResolver.Resolve(id)!.DisplayName.Should().Be(expectedDisplayName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("does-not-exist")]
    public void Resolve_UnknownOrBlank_ReturnsNull(string? id)
    {
        VoiceBankResolver.Resolve(id).Should().BeNull();
    }

    // ---- ForConfig: ItemsSource that never blanks the selection ----

    [Theory]
    [InlineData("ru-preset-1")]
    [InlineData("ru-preset-2")]
    [InlineData("RU-PRESET-2")] // known via case-insensitive membership -> no duplicate
    public void ForConfig_KnownId_ReturnsBuiltInUnchanged(string id)
    {
        VoiceBankResolver.ForConfig(id).Should().Equal(VoiceBankResolver.BuiltIn);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ForConfig_NullOrBlank_ReturnsBuiltIn(string? id)
    {
        VoiceBankResolver.ForConfig(id).Should().Equal(VoiceBankResolver.BuiltIn);
    }

    [Fact]
    public void ForConfig_CustomId_AppendsSelectablePlaceholder()
    {
        IReadOnlyList<TtsVoice> result = VoiceBankResolver.ForConfig("my-custom-voice");

        result.Should().HaveCount(3);
        result.Take(2).Should().Equal(VoiceBankResolver.BuiltIn);
        result[2].Should().Be(new TtsVoice("my-custom-voice", "my-custom-voice", "unknown", ""));
    }

    // ---- ForConfig(selected, customVoiceIds): built-in + user-declared custom ids (F-16 phase 2) ----

    [Fact]
    public void ForConfig_NullCustomIds_MatchesSingleArg()
    {
        VoiceBankResolver.ForConfig("ru-preset-1", null).Should().Equal(VoiceBankResolver.ForConfig("ru-preset-1"));
        VoiceBankResolver.ForConfig("hand-edited", null).Should().Equal(VoiceBankResolver.ForConfig("hand-edited"));
    }

    [Fact]
    public void ForConfig_EmptyCustomIds_KnownSelection_ReturnsBuiltInInstance()
    {
        VoiceBankResolver.ForConfig("ru-preset-1", []).Should().BeSameAs(VoiceBankResolver.BuiltIn);
    }

    [Fact]
    public void ForConfig_CustomIds_AppendedAfterBuiltIn_InDeclaredOrder()
    {
        IReadOnlyList<TtsVoice> result = VoiceBankResolver.ForConfig("ru-preset-1", ["custom-a", "custom-b"]);

        result.Should().HaveCount(4);
        result.Take(2).Should().Equal(VoiceBankResolver.BuiltIn);
        result[2].Should().Be(new TtsVoice("custom-a", "custom-a", "unknown", ""));
        result[3].Should().Be(new TtsVoice("custom-b", "custom-b", "unknown", ""));
    }

    [Fact]
    public void ForConfig_CustomIds_DedupAgainstBuiltIn_CaseInsensitive()
    {
        // "RU-PRESET-1" is a built-in id (case-insensitive) -> dropped; only "custom-a" is appended.
        IReadOnlyList<TtsVoice> result = VoiceBankResolver.ForConfig("ru-preset-1", ["RU-PRESET-1", "custom-a"]);

        result.Should().HaveCount(3);
        result.Take(2).Should().Equal(VoiceBankResolver.BuiltIn);
        result[2].Id.Should().Be("custom-a");
    }

    [Fact]
    public void ForConfig_CustomIds_DedupAmongThemselves_CaseInsensitive_FirstWins()
    {
        IReadOnlyList<TtsVoice> result = VoiceBankResolver.ForConfig("ru-preset-1", ["custom-a", "CUSTOM-A", "custom-b"]);

        result.Select(v => v.Id).Should().Equal("ru-preset-1", "ru-preset-2", "custom-a", "custom-b");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ForConfig_CustomIds_BlankEntries_AreSkipped(string? blank)
    {
        VoiceBankResolver.ForConfig("ru-preset-1", [blank!]).Should().BeSameAs(VoiceBankResolver.BuiltIn);
    }

    [Fact]
    public void ForConfig_CustomIds_AreTrimmed()
    {
        IReadOnlyList<TtsVoice> result = VoiceBankResolver.ForConfig("ru-preset-1", ["  spaced-id  "]);

        result.Should().HaveCount(3);
        result[2].Id.Should().Be("spaced-id");
    }

    [Fact]
    public void ForConfig_SelectedUnknownNotInCustom_AppendedAsPlaceholderLast()
    {
        IReadOnlyList<TtsVoice> result = VoiceBankResolver.ForConfig("hand-edited", ["custom-a"]);

        result.Select(v => v.Id).Should().Equal("ru-preset-1", "ru-preset-2", "custom-a", "hand-edited");
    }

    [Fact]
    public void ForConfig_SelectedIsACustomId_NotDuplicated()
    {
        IReadOnlyList<TtsVoice> result = VoiceBankResolver.ForConfig("custom-a", ["custom-a"]);

        result.Select(v => v.Id).Should().Equal("ru-preset-1", "ru-preset-2", "custom-a");
    }

    [Fact]
    public void ContainsVoiceId_MatchesExistingPickerEntries_CaseInsensitive()
    {
        IReadOnlyList<TtsVoice> voices = VoiceBankResolver.ForConfig("custom-a", []);

        VoiceBankResolver.ContainsVoiceId(voices, "CUSTOM-A").Should().BeTrue();
    }

    [Fact]
    public void ContainsVoiceId_BlankOrMissing_ReturnsFalse()
    {
        VoiceBankResolver.ContainsVoiceId(VoiceBankResolver.BuiltIn, null).Should().BeFalse();
        VoiceBankResolver.ContainsVoiceId(VoiceBankResolver.BuiltIn, "   ").Should().BeFalse();
        VoiceBankResolver.ContainsVoiceId(VoiceBankResolver.BuiltIn, "missing").Should().BeFalse();
    }

    // ---- ResolveAsync: phase-2 merge (fail-soft, built-in wins, dedup) ----

    [Fact]
    public async Task ResolveAsync_NullService_ReturnsBuiltIn()
    {
        (await VoiceBankResolver.ResolveAsync(null, TestContext.Current.CancellationToken))
            .Should().Equal(VoiceBankResolver.BuiltIn);
    }

    [Fact]
    public async Task ResolveAsync_EngineVoices_AppendedAfterBuiltIn()
    {
        var engine = new FakeTtsService(_ => Task.FromResult<IReadOnlyList<TtsVoice>>(
            [new TtsVoice("en-extra", "English Voice", "male", "en")]));

        IReadOnlyList<TtsVoice> result = await VoiceBankResolver.ResolveAsync(engine, TestContext.Current.CancellationToken);

        result.Should().HaveCount(3);
        result.Take(2).Should().Equal(VoiceBankResolver.BuiltIn);
        result[2].Id.Should().Be("en-extra");
    }

    [Fact]
    public async Task ResolveAsync_DuplicateId_BuiltInMetadataWins()
    {
        // Engine reports ru-preset-1 with a different (generic/mock) name -> built-in name must survive.
        var engine = new FakeTtsService(_ => Task.FromResult<IReadOnlyList<TtsVoice>>(
            [new TtsVoice("ru-preset-1", "Mock Voice 1", "unknown", "ru")]));

        IReadOnlyList<TtsVoice> result = await VoiceBankResolver.ResolveAsync(engine, TestContext.Current.CancellationToken);

        result.Should().Equal(VoiceBankResolver.BuiltIn);
        result[0].DisplayName.Should().Be("Russian Narrator (M)");
    }

    [Fact]
    public async Task ResolveAsync_DuplicateId_IsCaseInsensitive()
    {
        var engine = new FakeTtsService(_ => Task.FromResult<IReadOnlyList<TtsVoice>>(
            [new TtsVoice("RU-PRESET-2", "Dup", "female", "ru")]));

        (await VoiceBankResolver.ResolveAsync(engine, TestContext.Current.CancellationToken))
            .Should().Equal(VoiceBankResolver.BuiltIn);
    }

    [Fact]
    public async Task ResolveAsync_EngineVoiceWithEmptyId_IsSkipped()
    {
        var engine = new FakeTtsService(_ => Task.FromResult<IReadOnlyList<TtsVoice>>(
            [new TtsVoice("", "Nameless", "unknown", "")]));

        (await VoiceBankResolver.ResolveAsync(engine, TestContext.Current.CancellationToken))
            .Should().Equal(VoiceBankResolver.BuiltIn);
    }

    [Fact]
    public async Task ResolveAsync_EngineEmpty_ReturnsBuiltIn()
    {
        var engine = new FakeTtsService(_ => Task.FromResult<IReadOnlyList<TtsVoice>>([]));

        (await VoiceBankResolver.ResolveAsync(engine, TestContext.Current.CancellationToken))
            .Should().Equal(VoiceBankResolver.BuiltIn);
    }

    [Fact]
    public async Task ResolveAsync_EngineReturnsNull_ReturnsBuiltIn()
    {
        var engine = new FakeTtsService(_ => Task.FromResult<IReadOnlyList<TtsVoice>>(null!));

        (await VoiceBankResolver.ResolveAsync(engine, TestContext.Current.CancellationToken))
            .Should().Equal(VoiceBankResolver.BuiltIn);
    }

    [Fact]
    public async Task ResolveAsync_EngineThrows_FailsSoftToBuiltIn()
    {
        var engine = new FakeTtsService(_ => throw new InvalidOperationException("sidecar down"));

        (await VoiceBankResolver.ResolveAsync(engine, TestContext.Current.CancellationToken))
            .Should().Equal(VoiceBankResolver.BuiltIn);
    }

    [Fact]
    public async Task ResolveAsync_CancelledToken_Propagates()
    {
        var engine = new FakeTtsService(t =>
        {
            t.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<TtsVoice>>([]);
        });

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Func<Task> act = () => VoiceBankResolver.ResolveAsync(engine, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private sealed class FakeTtsService(Func<CancellationToken, Task<IReadOnlyList<TtsVoice>>> getVoices) : ITtsService
    {
        public Task<bool> TryInitializeAsync(CancellationToken token) => throw new NotSupportedException();
        public Task<TtsResult> SynthesizeAsync(TtsRequest request, CancellationToken token) => throw new NotSupportedException();
        public Task<IReadOnlyList<TtsVoice>> GetVoicesAsync(CancellationToken token) => getVoices(token);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
