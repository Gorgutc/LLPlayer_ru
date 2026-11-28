using System.Text.Json;
using LLPlayer.ControlApi;
using LLPlayer.Core.Automation;

namespace FlyleafLibTests.Core;

public sealed class ControlRecognitionOmissionsTests
{
    [Fact]
    public void Off_and_unavailable_report_distinct_job_modes()
    {
        var off = ControlRecognitionOmissions.Capture(null, requested: false).Summarize();
        var unavailable = ControlRecognitionOmissions.Capture(null, requested: true).Summarize();

        Assert.False(off.Enabled);
        Assert.Equal("disabled", off.Status);
        Assert.False(off.Applied);
        Assert.Null(off.Available);
        Assert.True(unavailable.Enabled);
        Assert.Equal("unavailable", unavailable.Status);
        Assert.False(unavailable.Applied);
        Assert.False(unavailable.Available);
    }

    [Fact]
    public void Retained_report_pages_keep_source_clock_and_review_reason()
    {
        Guid job = Guid.NewGuid();
        var report = new ControlRecognitionOmissions(true, "partial",
        [
            new("confirmed", 12_300, 13_400, "contextConfirmedGap", ChunkOffsetMs: 10_000),
            new("review first", 21_300, 22_100, "contextConfirmedGap", "partialDonorCue", ChunkOffsetMs: 20_000)
        ]);
        var persisted = ControlProtocol.Encode(report).Deserialize<ControlRecognitionOmissions>(ControlProtocol.Json)!;
        var first = persisted.Read(new(job, Limit: 1, FileIndex: 2));
        var second = persisted.Read(new(job, Offset: first.NextOffset!.Value, Limit: 1, FileIndex: 2));

        Assert.Equal(job, first.JobId);
        Assert.Equal(2, first.FileIndex);
        Assert.Equal(2, first.OmissionDetection.TotalCount);
        Assert.True(first.OmissionDetection.Applied);
        Assert.True(first.OmissionDetection.Available);
        Assert.Equal(1, first.NextOffset);
        Assert.Equal(12_300, Assert.Single(first.Candidates).StartMs);
        Assert.Null(second.NextOffset);
        var pending = Assert.Single(second.Candidates);
        Assert.Equal("review first", pending.Text);
        Assert.Equal(22_100, pending.EndMs);
        Assert.True(pending.IsReviewOnly);
        Assert.Equal("partialDonorCue", pending.ReviewOnlyReason);
    }

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(1, 1)]
    [InlineData(0, 0)]
    [InlineData(0, 257)]
    public void Only_valid_bounded_pages_can_be_read(int offset, int limit)
    {
        var report = ControlRecognitionOmissions.Capture(null, requested: true);
        var error = Assert.Throws<ControlException>(() => report.Read(new(Guid.NewGuid(), offset, limit)));
        Assert.Equal("invalid_arguments", error.Error.Code);
    }
}
