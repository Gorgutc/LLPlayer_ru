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

    private static OpenAIBaseTranslateSettings NewSettings(TranslateServiceType type) => type switch
    {
        TranslateServiceType.Ollama => new OllamaTranslateSettings(),
        TranslateServiceType.LMStudio => new LMStudioTranslateSettings(),
        TranslateServiceType.KoboldCpp => new KoboldCppTranslateSettings(),
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };
}
