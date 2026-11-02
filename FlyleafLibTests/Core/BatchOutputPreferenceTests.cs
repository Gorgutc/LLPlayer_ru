using LLPlayer.Core.Batch;

namespace FlyleafLibTests.Core;

public sealed class BatchOutputPreferenceTests
{
    [Fact]
    public void Global_bom_preference_is_captured_once_per_run_and_later_change_applies_to_next_run()
    {
        bool globalBom = false; int reads = 0;
        var factory = new BatchRunFactory(() => throw new InvalidOperationException("No native configuration needed for output policy."),
            getUtf8Bom: () => { reads++; return globalBom; });
        var first = factory.CaptureOptions(new BatchSettings(), "flac");
        Assert.False(first.Utf8Bom); Assert.Equal(1, reads);
        globalBom = true;
        Assert.False(first.Utf8Bom); Assert.Equal(1, reads);
        var second = factory.CaptureOptions(new BatchSettings(), "flac");
        Assert.True(second.Utf8Bom); Assert.Equal(2, reads);
    }

    [Fact]
    public void Old_unshipped_batch_bom_field_is_ignored_and_never_serialized_as_an_independent_owner()
    {
        string path = Path.Combine(Path.GetTempPath(), "llplayer-batch-output-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(path, "{\"Utf8Bom\":true,\"Recursive\":false}");
            var store = new BatchSettingsStore(path);
            var settings = store.Load(); Assert.False(settings.Recursive); Assert.Null(store.LoadWarning);
            var factory = new BatchRunFactory(() => throw new InvalidOperationException("No native configuration needed for output policy."), getUtf8Bom: () => false);
            Assert.False(factory.CaptureOptions(settings, "flac").Utf8Bom);
            store.Save(settings);
            Assert.DoesNotContain("Utf8Bom", File.ReadAllText(path));
        }
        finally { File.Delete(path); }
    }
}
