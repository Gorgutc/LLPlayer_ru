using System;
using System.Collections.Generic;
using AwesomeAssertions;
using FlyleafLib.MediaPlayer.Translation.Services;

namespace FlyleafLib;

public class LocalLlmTimeoutTests
{
    [Fact]
    public void LocalLlmSettings_DefaultTimeout_Is180Seconds()
    {
        // B-04: the overall HttpClient timeout is the whole-request budget; reasoning models need headroom.
        new OllamaTranslateSettings().TimeoutMs.Should().Be(180000);
        new LMStudioTranslateSettings().TimeoutMs.Should().Be(180000);
        new KoboldCppTranslateSettings().TimeoutMs.Should().Be(180000);
    }

    [Theory]
    [InlineData(TranslateServiceType.Ollama)]
    [InlineData(TranslateServiceType.LMStudio)]
    [InlineData(TranslateServiceType.KoboldCpp)]
    public void Migrate_BumpsPriorDefaultTimeout60sTo180s(TranslateServiceType type)
    {
        // An existing config saved at the old default (60000) is bumped on load so the owner stops hitting the
        // 60s cancel on reasoning models without editing Settings by hand.
        Dictionary<TranslateServiceType, ITranslateSettings> services = new();
        OpenAIBaseTranslateSettings settings = NewSettings(type);
        settings.TimeoutMs = 60000; // the prior persisted default
        services[type] = settings;

        Config.MigrateLocalLlmTimeoutDefault(services);

        ((OpenAIBaseTranslateSettings)services[type]).TimeoutMs.Should().Be(180000);
    }

    [Fact]
    public void Migrate_PreservesAnExplicitNonDefaultTimeout()
    {
        // A user who deliberately tuned the timeout must NOT be overwritten by the migration.
        Dictionary<TranslateServiceType, ITranslateSettings> services = new()
        {
            [TranslateServiceType.LMStudio] = new LMStudioTranslateSettings { TimeoutMs = 300000 },
        };

        Config.MigrateLocalLlmTimeoutDefault(services);

        ((OpenAIBaseTranslateSettings)services[TranslateServiceType.LMStudio]).TimeoutMs.Should().Be(300000);
    }

    [Fact]
    public void Migrate_NoConfiguredService_DoesNothing()
    {
        Dictionary<TranslateServiceType, ITranslateSettings> services = new();

        Config.MigrateLocalLlmTimeoutDefault(services); // must not throw on an empty/unconfigured dictionary

        services.Should().BeEmpty();
    }

    [Fact]
    public void OpenAiLikeSettings_DefaultTimeout_Is180Seconds()
    {
        // T-12: OpenAILike / LiteLLM front slow local reasoning models; the inherited 15s default cancelled them.
        new OpenAILikeTranslateSettings().TimeoutMs.Should().Be(180000);
        new LiteLLMTranslateSettings().TimeoutMs.Should().Be(180000);
    }

    [Theory]
    [InlineData(TranslateServiceType.OpenAILike)]
    [InlineData(TranslateServiceType.LiteLLM)]
    public void MigrateOpenAiLike_BumpsPriorDefaultTimeout15sTo180s(TranslateServiceType type)
    {
        // An existing config saved at the old 15s default is bumped on load so the owner stops hitting the cancel on
        // reasoning models fronted by these endpoints without editing Settings by hand.
        Dictionary<TranslateServiceType, ITranslateSettings> services = new();
        OpenAIBaseTranslateSettings settings = NewSettings(type);
        settings.TimeoutMs = 15000; // the prior persisted default
        services[type] = settings;

        Config.MigrateOpenAiLikeTimeoutDefault(services);

        ((OpenAIBaseTranslateSettings)services[type]).TimeoutMs.Should().Be(180000);
    }

    [Fact]
    public void MigrateOpenAiLike_PreservesAnExplicitNonDefaultTimeout()
    {
        Dictionary<TranslateServiceType, ITranslateSettings> services = new()
        {
            [TranslateServiceType.LiteLLM] = new LiteLLMTranslateSettings { TimeoutMs = 30000 },
        };

        Config.MigrateOpenAiLikeTimeoutDefault(services);

        ((OpenAIBaseTranslateSettings)services[TranslateServiceType.LiteLLM]).TimeoutMs.Should().Be(30000);
    }

    [Fact]
    public void MigrateOpenAiLike_DoesNotTouchCloudOpenAiAtSameDefault()
    {
        // Cloud OpenAI also inherits 15000 but is out of T-12 scope (fast API) — the migration must leave it alone.
        Dictionary<TranslateServiceType, ITranslateSettings> services = new()
        {
            [TranslateServiceType.OpenAI] = new OpenAITranslateSettings { TimeoutMs = 15000 },
        };

        Config.MigrateOpenAiLikeTimeoutDefault(services);

        ((OpenAIBaseTranslateSettings)services[TranslateServiceType.OpenAI]).TimeoutMs.Should().Be(15000);
    }

    private static OpenAIBaseTranslateSettings NewSettings(TranslateServiceType type) => type switch
    {
        TranslateServiceType.Ollama => new OllamaTranslateSettings(),
        TranslateServiceType.LMStudio => new LMStudioTranslateSettings(),
        TranslateServiceType.KoboldCpp => new KoboldCppTranslateSettings(),
        TranslateServiceType.OpenAILike => new OpenAILikeTranslateSettings(),
        TranslateServiceType.LiteLLM => new LiteLLMTranslateSettings(),
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };
}
