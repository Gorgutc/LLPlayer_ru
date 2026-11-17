#if !WINDOWS
using FlyleafLib;
using LLPlayer.Core.Automation;
using LLPlayer.Core.Services;
using LLPlayer.Core.Services.Models;

namespace FlyleafLibTests.Automation;

public sealed class ControlModelDirectoryTests
{
    [Fact]
    public async Task Explicit_model_directory_uses_the_shared_resource_lease()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        string root = Path.Combine(Path.GetTempPath(), $"llplayer-model-directory-{Guid.NewGuid():N}");
        string ordinary = Directory.CreateDirectory(Path.Combine(root, "ordinary")).FullName;
        string chosen = Directory.CreateDirectory(Path.Combine(root, "chosen")).FullName;
        string? ordinaryFile = null, chosenFile = null;
        try
        {
            Config config = new(true);
            LanguageResourceCoordinator leases = new();
            using ControlModelResources models = new(() => config, new(ordinary, root, root), leases);
            string? selected = config.Subtitles.WhisperCppConfig.Model?.ModelFileName;
            ModelEntry entry = models.List(ModelKind.Whisper, chosen).First(model => model.FileName != selected);
            Assert.Equal(chosen, models.GetDirectory(ModelKind.Whisper, chosen));
            ordinaryFile = Path.Combine(ordinary, entry.FileName);
            chosenFile = entry.FullPath;
            await File.WriteAllTextAsync(ordinaryFile, "ordinary fixture", token);
            await File.WriteAllTextAsync(chosenFile, "chosen fixture", token);

            Task removal;
            using (leases.AcquireUse())
            {
                removal = models.RemoveAsync(entry.Id, token, chosen);
                Assert.False(removal.IsCompleted);
                Assert.True(File.Exists(chosenFile));
            }
            await removal.WaitAsync(TimeSpan.FromSeconds(5), token);
            Assert.False(File.Exists(chosenFile));
            Assert.Equal("ordinary fixture", await File.ReadAllTextAsync(ordinaryFile, token));
        }
        finally
        {
            if (ordinaryFile != null) File.Delete(ordinaryFile);
            if (chosenFile != null) File.Delete(chosenFile);
            Directory.Delete(chosen);
            Directory.Delete(ordinary);
            Directory.Delete(root);
        }
    }
}
#endif
