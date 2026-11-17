using System.Text.Json;
using FlyleafLib.MediaPlayer.Translation.Services;
using LLPlayer.ControlApi;
using LLPlayer.Core.Automation;
using LLPlayer.Core.Services;

namespace FlyleafLibTests.Automation;

public sealed class ControlSettingsTests : IDisposable
{
    readonly string directory = Path.Combine(Path.GetTempPath(), "llplayer-control-settings-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Recognition_validates_an_edit_before_writing_and_preserves_untouched_fields()
    {
        var service = new ControlSettingsService(directory);
        var settings = new RecognitionSettings();
        settings.WhisperCpp.Threads = 4;
        var store = new RecognitionSettingsStore(Path.Combine(directory, "LLPlayer.Recognition.json"));
        store.Save(settings);

        var current = service.Read("recognition");
        var edit = Patch("recognition", "whisperCpp.prompt", "custom prompt");
        var candidate = service.Validate(edit, current.Revision);
        Assert.Equal("custom prompt", candidate.Requested["whisperCpp.prompt"].GetString());
        Assert.Empty(store.Load()!.WhisperCpp.Prompt);

        service.Apply(edit, current.Revision);
        var persisted = store.Load()!;
        Assert.Equal("custom prompt", persisted.WhisperCpp.Prompt);
        Assert.Equal(4, persisted.WhisperCpp.Threads);

        var invalid = Assert.Throws<ControlException>(() => service.Apply(Patch("recognition", "asrChunkSeconds", 0)));
        Assert.Equal("invalid_settings", invalid.Error.Code);
        Assert.Equal("custom prompt", store.Load()!.WhisperCpp.Prompt);
    }

    [Fact]
    public void Fresh_disk_edit_invalidates_revision_and_unspecified_fields_survive_a_retry()
    {
        var service = new ControlSettingsService(directory);
        var current = service.Read("batch");
        var patch = Patch("batch", "watchFolder", true);
        service.Apply(Patch("batch", "recursive", false), current.Revision);

        var conflict = Assert.Throws<ControlException>(() => service.Apply(patch, current.Revision));
        Assert.Equal("revision_conflict", conflict.Error.Code);
        var next = service.Read("batch");
        service.Apply(patch, next.Revision);
        Assert.False(service.CaptureBatch().Recursive);
        Assert.True(service.CaptureBatch().WatchFolder);
    }

    [Fact]
    public void Credential_edits_never_expose_values_and_ordinary_edits_cannot_touch_them()
    {
        const string credentialName = "providers.openAI.apiKey";
        const string secret = "literal-secret-only-for-this-test";
        var service = new ControlSettingsService(directory);
        var revision = service.Read("translation").Revision;
        Assert.Contains(credentialName, service.Schema("translation").Credentials);
        Assert.False(service.Schema("translation").Fields.ContainsKey(credentialName));

        var bad = Assert.Throws<ControlException>(() => service.Apply(Patch("translation", credentialName, secret), revision));
        Assert.DoesNotContain(secret, bad.Message);
        var invalidAdmission = Assert.Throws<ControlException>(() => service.ValidatePatchAdmission(
            Patch("translation", "providers.openAI.endpoint", "https://user:" + secret + "@example.test")));
        Assert.DoesNotContain(secret, invalidAdmission.Message);
        Assert.False(Directory.Exists(directory));

        var saved = service.SetCredential(new("translation", credentialName, secret), revision);
        Assert.True(saved.Credentials[credentialName].Present);
        Assert.DoesNotContain(secret, JsonSerializer.Serialize(saved));
        Assert.Equal(secret, Assert.IsType<OpenAITranslateSettings>(service.CaptureTranslation().Providers[TranslateServiceType.OpenAI]).ApiKey);

        var updated = service.Apply(Patch("translation", "providers.openAI.model", "test-model"), saved.Revision);
        Assert.Equal(secret, Assert.IsType<OpenAITranslateSettings>(service.CaptureTranslation().Providers[TranslateServiceType.OpenAI]).ApiKey);
        var cleared = service.ClearCredential(new("translation", credentialName), updated.Revision);
        Assert.False(cleared.Credentials[credentialName].Present);
        Assert.DoesNotContain(secret, JsonSerializer.Serialize(cleared));
    }

    [Fact]
    public void Failed_persistence_does_not_apply_settings_to_live_batch_owner()
    {
        Directory.CreateDirectory(directory);
        Directory.CreateDirectory(Path.Combine(directory, "LLPlayer.Batch.json"));
        int applied = 0;
        var service = new ControlSettingsService(directory, applyBatch: _ => applied++);
        var error = Assert.Throws<ControlException>(() => service.Apply(Patch("batch", "watchFolder", true)));
        Assert.Equal("settings_persistence_failed", error.Error.Code);
        Assert.Equal(0, applied);
    }

    [Fact]
    public void Recovery_settings_identity_survives_a_same_key_restart_and_detects_credentials_changed()
    {
        byte[] key = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        byte[] Fingerprint(byte[] value) => System.Security.Cryptography.HMACSHA256.HashData(key, value);
        var service = new ControlSettingsService(directory, fingerprintSettings: Fingerprint);
        service.SetCredential(new("translation", "providers.openAI.apiKey", "test-secret-before"));
        string before = service.GetIdentity("translation", "batch");

        var restarted = new ControlSettingsService(directory, fingerprintSettings: Fingerprint);
        Assert.Equal(before, restarted.GetIdentity("batch", "translation"));
        restarted.SetCredential(new("translation", "providers.openAI.apiKey", "test-secret-after"));
        Assert.NotEqual(before, restarted.GetIdentity("translation", "batch"));
    }

    [Fact]
    public void Secure_translation_draft_is_atomic_and_masks_legacy_endpoint_credentials()
    {
        const string provider = "providers.openAI.";
        const string urlCredential = "local-url-secret";
        const string apiKey = "local-provider-secret";
        var service = new ControlSettingsService(directory);
        var baseline = service.CaptureTranslation();
        var changed = baseline.Clone();
        changed.Providers[TranslateServiceType.OpenAI] = new OpenAITranslateSettings
        {
            Endpoint = "https://user:" + urlCredential + "@example.test/?mode=compatible", ApiKey = apiKey,
            Model = "test", TimeoutMs = -1
        };
        var invalid = ControlSettingsService.CreateDraft(baseline, changed);
        var request = new ControlCredentialPatch("translation", invalid.Patch.Fields, invalid.CredentialChanges!);
        Assert.Throws<ControlException>(() => service.ValidateCredentialPatchAdmission(request));
        Assert.False(Directory.Exists(directory));

        Assert.IsType<OpenAITranslateSettings>(changed.Providers[TranslateServiceType.OpenAI]).TimeoutMs = 1000;
        var draft = ControlSettingsService.CreateDraft(baseline, changed);
        var saved = service.ApplyCredentialPatch(new("translation", draft.Patch.Fields, draft.CredentialChanges!));
        string response = JsonSerializer.Serialize(saved);
        Assert.DoesNotContain(urlCredential, response);
        Assert.DoesNotContain(apiKey, response);
        Assert.True(saved.Credentials[provider + "apiKey"].Present);
        var persisted = Assert.IsType<OpenAITranslateSettings>(service.CaptureTranslation().Providers[TranslateServiceType.OpenAI]);
        Assert.Contains(urlCredential, persisted.Endpoint);
        Assert.Equal(apiKey, persisted.ApiKey);
        Assert.Equal("test", persisted.Model);
    }

    [Fact]
    public void Dubbing_and_PDIC_use_existing_validators_and_stores()
    {
        var service = new ControlSettingsService(directory);
        service.Apply(Patch("dubbing", "engine.duckingPercent", 23));
        Assert.Equal(23, new DubbingSettingsStore(Path.Combine(directory, "LLPlayer.Dubbing.json")).Load().Engine.DuckingPercent);

        const string bridge = @"C:\external\pdic-pipe.exe";
        service.Apply(Patch("pdic", "executablePath", bridge));
        Assert.Equal(bridge, service.CapturePdic().ExecutablePath);
        var err = Assert.Throws<ControlException>(() => service.Apply(Patch("pdic", "executablePath", "relative.exe")));
        Assert.Equal("invalid_settings", err.Error.Code);
        Assert.Equal(bridge, service.CapturePdic().ExecutablePath);
    }

#if !WINDOWS
    [Fact]
    public void Workflow_uses_the_same_detached_settings_as_its_identity()
    {
        var service = new ControlSettingsService(directory);
        var before = service.CaptureWorkflowSettings();
        service.Apply(Patch("recognition", "whisperCpp.prompt", "next run"));
        var after = service.CaptureWorkflowSettings();

        Assert.NotEqual(before.RecognitionIdentity, after.RecognitionIdentity);
        Assert.Equal(before.TranslationIdentity, after.TranslationIdentity);
        Assert.Equal(before.DubbingIdentity, after.DubbingIdentity);
        Assert.Empty(before.Config.Subtitles.WhisperCppConfig.Prompt);
        Assert.Equal("next run", after.Config.Subtitles.WhisperCppConfig.Prompt);
    }

    [Fact]
    public void App_preferences_and_playback_share_the_real_persisted_owners()
    {
        var service = new ControlSettingsService(directory);
        Assert.Equal(new[] { "app", "batch", "dubbing", "online_video", "pdic", "playback", "recognition", "translation" }, service.Families);
        service.Apply(Patch("app", "alwaysOnTop", true));
        service.Apply(Patch("app", "theme", "Light"));
        var prefs = new LLPlayer.Avalonia.Services.PrefsStore(Path.Combine(directory, "LLPlayer.Avalonia.json")).Load();
        Assert.Equal("Light", prefs.Theme);
        Assert.True(prefs.AlwaysOnTop);
        Assert.True(service.Schema("playback").Fields["logLevel"].SavedForNextStart);

        var before = service.Read("playback");
        string initial = before.Effective["logLevel"].GetString()!;
        string replacement = initial == "Info" ? "Warn" : "Info";
        var saved = service.Apply(Patch("playback", "logLevel", replacement), before.Revision);
        Assert.Equal(initial, saved.Effective["logLevel"].GetString());
        Assert.Equal(replacement, saved.Requested["logLevel"].GetString());
        Assert.Contains("logLevel", saved.Pending);
        Assert.Equal(replacement, new LLPlayer.Avalonia.Services.PlaybackSettingsStore(Path.Combine(directory, "LLPlayer.Playback.json")).Load().LogLevel.ToString());
    }

    [Fact]
    public void Desktop_save_freshly_merges_only_locally_edited_app_fields()
    {
        var service = new ControlSettingsService(directory);
        var store = new LLPlayer.Avalonia.Services.PrefsStore(Path.Combine(directory, "LLPlayer.Avalonia.json"));
        var original = store.Load();
        var edited = original.Clone();
        edited.Theme = "Light";
        service.Apply(Patch("app", "alwaysOnTop", true));

        store.SaveEdits(original, edited);

        Assert.True(service.CaptureAppPrefs().AlwaysOnTop);
        Assert.Equal("Light", service.CaptureAppPrefs().Theme);
    }

    [Fact]
    public void Online_helper_auth_options_use_the_existing_validator_and_never_echo()
    {
        const string extraArgs = "--password local-test-only-token --playlist-items 1:2";
        var service = new ControlSettingsService(directory);
        var settings = service.Read("online_video");
        var saved = service.SetCredential(new("online_video", "extraArguments", extraArgs), settings.Revision);
        Assert.False(saved.Requested.ContainsKey("extraArguments"));
        Assert.True(saved.Credentials["extraArguments"].Present);
        Assert.DoesNotContain("local-test-only-token", JsonSerializer.Serialize(saved));
        Assert.Equal(extraArgs, service.CaptureOnlineVideo().ExtraArguments);
        var error = Assert.Throws<ControlException>(() => service.SetCredential(new("online_video", "extraArguments", "--exec disallowed-value")));
        Assert.Equal("invalid_settings", error.Error.Code);
        Assert.DoesNotContain("disallowed-value", error.Message);
        Assert.Equal(extraArgs, service.CaptureOnlineVideo().ExtraArguments);
    }
#endif

    static ControlSettingsPatch Patch<T>(string family, string name, T value)
        => new(family, new() { [name] = JsonSerializer.SerializeToElement(value) });

    public void Dispose()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}
