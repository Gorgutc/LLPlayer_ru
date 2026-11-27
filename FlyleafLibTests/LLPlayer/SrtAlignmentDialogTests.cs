using FlyleafLib.MediaPlayer;
using LLPlayer.Core.Services;
using LLPlayer.ViewModels;
using Prism.Dialogs;

namespace FlyleafLibTests.LLPlayer;

public sealed class SrtAlignmentDialogTests
{
    [Fact]
    public async Task Prepare_uses_pinned_session_and_requires_explicit_consent_to_keep_unmatched_cues()
    {
        Session session = new();
        var model = Open(session);

        Assert.Equal("en", model.Language);
        Assert.False(model.CanSaveOriginal);
        await model.PrepareAsync();

        Assert.Equal(("en", SrtAlignmentDevice.Cpu), session.LastRequest!.Value);
        Assert.Equal(2, model.ReviewRows.Count);
        Assert.Equal("00:00:05.000 – 00:00:06.000", model.ReviewRows[0].ProposedInterval);
        Assert.Equal("Keep original", model.ReviewRows[1].ProposedInterval);
        Assert.False(model.CanSaveOriginal);
        Assert.False(model.CanSaveTranslation);

        model.KeepUnmatched = true;

        Assert.True(model.CanSaveOriginal);
        Assert.True(model.CanSaveTranslation);
        session.IsCurrent = false;
        model.RefreshSource();
        Assert.Empty(model.ReviewRows);
        Assert.False(model.KeepUnmatched);
        Assert.Contains("source changed", model.StatusText);
        Assert.False(model.CanPrepare);
        Assert.False(model.CanSaveOriginal);
        Assert.False(model.CanSaveTranslation);
        session.IsCurrent = true;
        model.RefreshSource();
        Assert.False(model.CanPrepare);
    }

    [Fact]
    public async Task Blocked_draft_explains_conflicting_times_without_requesting_consent()
    {
        var rows = Draft().Rows.ToArray();
        rows[0] = rows[0] with { ProposedStart = TimeSpan.FromSeconds(7.5), ProposedEnd = TimeSpan.FromSeconds(8.5) };
        SrtAlignmentDraft blocked = new(rows, canSaveOriginal: false, canSaveTranslated: false)
        {
            HasTimingConflicts = true
        };
        var model = Open(new Session { Prepare = _ => Task.FromResult(blocked) });

        await model.PrepareAsync();

        Assert.Contains("overlap or are out of order", model.StatusText);
        Assert.False(model.HasUnmatched);
        Assert.False(model.CanSaveOriginal);
        Assert.False(model.CanSaveTranslation);
    }

    [Fact]
    public async Task Prepare_accepts_supported_whisper_codes_and_rejects_unknown_codes()
    {
        Session session = new();
        var model = Open(session);
        model.Language = "zz";

        Assert.False(model.CanPrepare);
        await model.PrepareAsync();
        Assert.Null(session.LastRequest);

        model.Language = " EN ";
        Assert.True(model.CanPrepare);
        await model.PrepareAsync();
        Assert.Equal(("en", SrtAlignmentDevice.Cpu), session.LastRequest!.Value);
    }

    [Fact]
    public async Task Stop_cancels_an_active_prepare_and_clears_its_preview()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Session session = new()
        {
            Prepare = async token =>
            {
                started.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return Draft();
            }
        };
        var model = Open(session);

        Task preparing = model.PrepareAsync();
        await started.Task;
        Assert.True(model.IsWorking);
        await model.StopAsync();
        await preparing;

        Assert.Equal(1, session.StopCount);
        Assert.False(model.IsWorking);
        Assert.Empty(model.ReviewRows);
        Assert.True(model.CanCloseDialog());
    }

    [Fact]
    public async Task Stale_source_detected_by_prepare_shows_a_source_change_instead_of_a_user_cancel()
    {
        var result = new TaskCompletionSource<SrtAlignmentDraft>(TaskCreationOptions.RunContinuationsAsynchronously);
        Session session = new() { Prepare = _ => result.Task };
        var model = Open(session);

        Task preparing = model.PrepareAsync();
        session.IsCurrent = false;
        result.SetException(new OperationCanceledException());
        await preparing;

        Assert.Contains("source changed", model.StatusText);
        Assert.Empty(model.ReviewRows);
        Assert.False(model.CanPrepare);
    }

    [Fact]
    public async Task Stop_leaves_cancel_available_for_retry_after_an_unconfirmed_process_exit()
    {
        Session session = new()
        {
            Prepare = _ => Task.FromException<SrtAlignmentDraft>(new EngineProcessDrainException(
                "Faster Whisper is still running.", new IOException(), () => ValueTask.CompletedTask))
        };
        var model = Open(session);
        await model.PrepareAsync();
        Assert.True(model.CanStop);
        Assert.False(model.CanPrepare);
        session.StopError = new IOException("Faster Whisper is still running.");

        await model.StopAsync();

        Assert.False(model.CanPrepare);
        Assert.True(model.CanStop);
        Assert.Contains("still running", model.StatusText);

        session.StopError = null;
        await model.StopAsync();
        Assert.True(model.CanPrepare);
        Assert.False(model.CanStop);
        Assert.True(model.CanCloseDialog());
    }

    static SrtAlignmentDialogVM Open(Session session)
    {
        var model = new SrtAlignmentDialogVM(null);
        model.OnDialogOpened(new DialogParameters { { SrtAlignmentDialogVM.SessionKey, session } });
        return model;
    }

    static SrtAlignmentDraft Draft() => new(
        [
            new(0, "Original text", TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3),
                TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(6), 1, null),
            new(1, "Uncertain text", TimeSpan.FromSeconds(7), TimeSpan.FromSeconds(8), null, null, 0, "No match")
        ],
        canSaveOriginal: true, canSaveTranslated: true);

    sealed class Session : ISrtAlignmentSession
    {
        public string SourcePath => @"C:\media\original.srt";
        public string AudioLabel => "Stream 1";
        public string DefaultLanguage => "en";
        public bool CanUseCuda => false;
        public SrtAlignmentDevice DefaultDevice => SrtAlignmentDevice.Cpu;
        public bool IsCurrent { get; set; } = true;
        public (string, SrtAlignmentDevice)? LastRequest { get; private set; }
        public int StopCount { get; private set; }
        public Exception? StopError { get; set; }
        public Func<CancellationToken, Task<SrtAlignmentDraft>> Prepare { get; init; } = _ => Task.FromResult(Draft());

        public Task<SrtAlignmentDraft> PrepareAsync(string language, SrtAlignmentDevice device, CancellationToken token)
        {
            LastRequest = (language, device);
            return Prepare(token);
        }

        public Task SaveAsync(SrtAlignmentDraft draft, string path, bool translated, bool acceptUnmatched, CancellationToken token)
            => throw new NotSupportedException();

        public Task CancelAndDrainAsync()
        {
            StopCount++;
            return StopError == null ? Task.CompletedTask : Task.FromException(StopError);
        }
    }
}
