using Flyleaf.FFmpeg;
using LLPlayer.Avalonia.Services;
using LLPlayer.Core.Automation;

namespace FlyleafLib.Platform;

[Collection("PortableEngine")]
public sealed class ControlWorkflowPlaybackSettingsTests
{
    [Theory]
    [InlineData(LoadProfile.All, null, true, true)]
    [InlineData(LoadProfile.Filters, null, true, true)]
    [InlineData(LoadProfile.Main, null, true, false)]
    [InlineData(LoadProfile.All, null, false, false)]
    [InlineData(LoadProfile.All, LoadProfile.Main, true, false)]
    [InlineData(LoadProfile.Main, LoadProfile.Filters, true, true)]
    public void Workflow_capture_applies_saved_playback_before_engine_start(
        LoadProfile savedProfile, LoadProfile? startedProfile, bool requestedFilters, bool expectedFilters)
    {
        string directory = Path.Combine(Path.GetTempPath(), "llplayer-workflow-playback-" + Guid.NewGuid().ToString("N"));
        var configProperty = typeof(Engine).GetProperty(nameof(Engine.Config))!;
        var originalEngine = Engine.Config;
        var activeEngine = startedProfile is { } profile ? new EngineConfig { FFmpegLoadProfile = profile } : null;

        try
        {
            configProperty.SetValue(null, activeEngine);
            Directory.CreateDirectory(directory);
            var store = new PlaybackSettingsStore(Path.Combine(directory, "LLPlayer.Playback.json"));
            store.Save(new PlaybackSettings
            {
                AudioFiltersEnabled = requestedFilters,
                FFmpegLoadProfile = savedProfile,
                LanguageFallbackSecondarySame = false,
                LanguageFallbackSecondary = "ru",
            });

            var workflow = new ControlSettingsService(directory).CaptureWorkflowSettings();

            Assert.Same(activeEngine, Engine.Config);
            Assert.Equal(expectedFilters, workflow.Config.Audio.FiltersEnabled);
            Assert.Equal("ru", workflow.Config.Subtitles.LanguageFallbackSecondary.ISO6391);
            Assert.Equal(requestedFilters, store.Load().AudioFiltersEnabled);
        }
        finally
        {
            configProperty.SetValue(null, originalEngine);
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}
