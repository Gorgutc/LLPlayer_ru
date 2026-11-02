using LLPlayer.Core.Services;

namespace LLPlayer.Avalonia.Tests;

public class DubbingSaveAdmissionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Settings_and_batch_voice_merge_current_state_after_admission(bool batchVoice)
    {
        string directory = Path.Combine(Path.GetTempPath(), "llplayer-dubbing-admission-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new DubbingSettingsStore(Path.Combine(directory, "dubbing.json"));
            DubbingSettings current = new();
            DubbingSettings baseline = current.Clone();
            DubbingSettings edited = baseline.Clone();
            edited.PersistPerLineVoices = true;
            DubbingSettings previousCommit = current.Clone();
            previousCommit.Engine.DuckingPercent = 37;
            previousCommit.Engine.DefaultVoiceId = "admitted-voice";
            Backend backend = new() { BeforeAdmission = () => current = previousCommit };
            using RecognitionActions recognition = new(backend);
            await AppHost.SaveDubbingSettingsAsync(recognition, store, () => current, fresh =>
            {
                if (!batchVoice) return DubbingSettings.MergeChanges(fresh, baseline, edited);
                DubbingSettings candidate = fresh.Clone();
                candidate.Engine.DefaultVoiceId = "batch-voice";
                return candidate;
            }, candidate => current = candidate);

            Assert.Equal(37, current.Engine.DuckingPercent);
            Assert.Equal(batchVoice ? "batch-voice" : "admitted-voice", current.Engine.DefaultVoiceId);
            Assert.Equal(!batchVoice, current.PersistPerLineVoices);
            DubbingSettings saved = store.Load();
            Assert.Equal(current.Engine.DuckingPercent, saved.Engine.DuckingPercent);
            Assert.Equal(current.Engine.DefaultVoiceId, saved.Engine.DefaultVoiceId);
            Assert.Equal(current.PersistPerLineVoices, saved.PersistPerLineVoices);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    sealed class Backend : IRecognitionBackend
    {
        public Action? BeforeAdmission;
        public bool IsBusy { get { Interlocked.Exchange(ref BeforeAdmission, null)?.Invoke(); return false; } }
        public bool HasAudio => true;
        public bool IsLive => false;
        public bool IsPaused => false;
        public void Start(int slot) { }
        public void Pause() { }
        public void Resume() { }
        public void Cancel(int slot) { }
        public void Ocr(int slot) { }
        public void Apply(RecognitionSettings settings) { }
    }
}
