using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FlyleafLib.MediaPlayer;
using LLPlayer.Avalonia.Services;
using LLPlayer.Avalonia.ViewModels;
using LLPlayer.Avalonia.Views;
using LLPlayer.Core.Services;

namespace LLPlayer.Avalonia.Tests;

public sealed class SrtAlignmentSidebarTests
{
    [AvaloniaFact]
    public void Sidebar_exposes_alignment_and_disables_it_without_a_loaded_srt()
    {
        var player = new FakePlaybackController { IsOpened = true };
        var window = new MainWindow(new KeyMapper(), null);
        using var vm = new MainWindowViewModel(player, new AppPrefs { SidebarVisible = true }.Normalize(),
            _ => { }, new FakeTranslator(), window);
        window.Attach(vm, renderer: null);

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var button = Assert.IsType<Button>(window.FindControl<Button>("AlignSrtButton"));
            Assert.True(button.IsVisible);
            Assert.NotNull(button.Command);
            Assert.False(button.Command.CanExecute(null));
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(0, "Primary")]
    [InlineData(1, "Secondary")]
    public void Sidebar_uses_the_current_slot_when_opening_alignment(int slot, string label)
    {
        var player = new AlignmentPlayer { IsOpened = true, AvailableSlot = slot };
        var shell = new AlignmentShell();
        var window = new MainWindow(new KeyMapper(), null);
        using var vm = new MainWindowViewModel(player, new AppPrefs { SidebarVisible = true }.Normalize(),
            _ => { }, new FakeTranslator(), shell);
        window.Attach(vm, renderer: null);

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var button = Assert.IsType<Button>(window.FindControl<Button>("AlignSrtButton"));
            vm.Sidebar.SelectedSlot = 1 - slot;
            Assert.False(button.IsEffectivelyEnabled);
            vm.Sidebar.SelectedSlot = slot;
            Assert.True(button.IsEffectivelyEnabled);

            button.Command!.Execute(null);

            Assert.Equal(slot, player.CapturedSlot);
            Assert.Equal(label, shell.Model!.TrackLabel);
            Assert.Equal(player.Session.AudioLabel, shell.Model.AudioLabel);
            Assert.False(shell.Model.UseCpu);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Partial_draft_requires_confirmation_and_supports_separate_translation_export()
    {
        var session = new AlignmentSession { Result = MakeDraft(partial: true) };
        var model = new SrtAlignmentViewModel(session, 1)
        {
            UseCpu = true,
            PickOutputPath = (name, _) => Task.FromResult<string?>(name)
        };
        await model.PrepareCommand.ExecuteAsync(null);

        Assert.Equal(("en", SrtAlignmentDevice.Cpu), session.Invocation);
        Assert.Equal("1 of 2 cues matched", model.CoverageText);
        Assert.True(model.NeedsAcknowledgment);
        Assert.False(model.SaveOriginalCommand.CanExecute(null));
        Assert.False(model.SaveTranslationCommand.CanExecute(null));

        model.KeepUnmatched = true;
        Assert.True(model.SaveOriginalCommand.CanExecute(null));
        Assert.True(model.SaveTranslationCommand.CanExecute(null));
        await model.SaveTranslationCommand.ExecuteAsync(null);

        var saved = Assert.Single(session.Saves);
        Assert.Equal(("source.aligned.translated.srt", true, true), saved);
    }

    [AvaloniaFact]
    public async Task Blocked_draft_explains_conflicting_times_and_displays_cue_coverage()
    {
        var rows = MakeDraft(partial: true).Rows.ToArray();
        rows[0] = rows[0] with
        {
            ProposedStart = TimeSpan.FromSeconds(4.5),
            ProposedEnd = TimeSpan.FromSeconds(5.5),
            Coverage = 0.8
        };
        var session = new AlignmentSession
        {
            Result = new(rows, canSaveOriginal: false, canSaveTranslated: false) { HasTimingConflicts = true }
        };
        var model = new SrtAlignmentViewModel(session, 0);
        var window = new SrtAlignmentWindow(model);

        try
        {
            window.Show();
            await model.PrepareCommand.ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();

            Assert.Contains("overlap or are out of order", model.Message);
            Assert.False(window.FindControl<CheckBox>("AlignmentKeepUnmatched")!.IsVisible);
            Assert.False(model.SaveOriginalCommand.CanExecute(null));
            Assert.False(model.SaveTranslationCommand.CanExecute(null));
            string coverage = $"Matched {0.8:P0}";
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), control => control.Text == coverage);
        }
        finally
        {
            await window.DrainForShutdownAsync();
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Source_change_during_picker_rejects_the_old_save()
    {
        var session = new AlignmentSession();
        var picked = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var model = new SrtAlignmentViewModel(session, 0) { PickOutputPath = (_, _) => picked.Task };
        await model.PrepareCommand.ExecuteAsync(null);

        Task saving = model.SaveOriginalCommand.ExecuteAsync(null);
        Assert.True(model.Busy);
        session.IsCurrent = false;
        model.RefreshSource();
        picked.SetResult("aligned.srt");
        await saving;

        Assert.Empty(session.Saves);
        Assert.False(model.HasPreview);
        Assert.False(model.PrepareCommand.CanExecute(null));
        Assert.Contains("source changed", model.Message);
    }

    [AvaloniaFact]
    public async Task Reload_instruction_is_preserved_when_loaded_srt_changes_on_disk()
    {
        var session = new AlignmentSession();
        session.SaveAction = () =>
        {
            session.IsCurrent = false;
            return Task.FromException(new OperationCanceledException("The subtitle file changed on disk. Reload it before alignment."));
        };
        var model = new SrtAlignmentViewModel(session, 0)
        {
            PickOutputPath = (name, _) => Task.FromResult<string?>(name)
        };
        await model.PrepareCommand.ExecuteAsync(null);
        await model.SaveOriginalCommand.ExecuteAsync(null);

        Assert.False(model.HasPreview);
        Assert.False(model.SaveOriginalCommand.CanExecute(null));
        Assert.Contains("Reload it before alignment.", model.Message);
    }

    [AvaloniaFact]
    public async Task Closing_owner_drains_the_alignment_picker_before_closing_both_windows()
    {
        var window = new MainWindow(new KeyMapper(), null);
        var session = new AlignmentSession();
        var model = new SrtAlignmentViewModel(session, 0);
        var picked = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task? saving = null;
        bool shutDown = false;
        window.ShutdownRequested = () => { shutDown = true; return Task.CompletedTask; };

        try
        {
            window.Show();
            window.ShowSrtAlignment(model);
            var dialog = Assert.Single(window.OwnedWindows.OfType<SrtAlignmentWindow>());
            Assert.Equal(WindowClosingBehavior.OwnerWindowOnly, window.ClosingBehavior);
            model.PickOutputPath = (_, _) => picked.Task;
            await model.PrepareCommand.ExecuteAsync(null);
            saving = model.SaveOriginalCommand.ExecuteAsync(null);
            Assert.True(model.Busy);

            window.Close();
            Assert.True(window.IsVisible);
            Assert.False(dialog.IsEffectivelyEnabled);
            Assert.False(shutDown);
            picked.SetResult("aligned.srt");
            await saving;
            await window.RequestApplicationExitAsync();

            Assert.True(shutDown);
            Assert.False(window.IsVisible);
            Assert.False(dialog.IsVisible);
            Assert.Empty(session.Saves);
        }
        finally
        {
            picked.TrySetResult(null);
            if (saving != null) await saving;
            if (window.IsVisible) await window.RequestApplicationExitAsync();
        }
    }

    [AvaloniaFact]
    public async Task Cancel_waits_for_process_drain_and_discards_incomplete_preview()
    {
        var session = new AlignmentSession
        {
            PrepareAction = async token =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return MakeDraft();
            }
        };
        var model = new SrtAlignmentViewModel(session, 0);
        Task preparation = model.PrepareCommand.ExecuteAsync(null);

        Assert.True(model.Busy);
        Assert.True(model.CancelCommand.CanExecute(null));
        await model.CancelCommand.ExecuteAsync(null);
        await preparation;

        Assert.Equal(1, session.DrainCalls);
        Assert.False(model.HasPreview);
        Assert.False(model.Busy);
        Assert.Contains("cancelled", model.Message);
    }

    [AvaloniaFact]
    public async Task Process_cleanup_failure_can_be_retried_from_the_same_dialog()
    {
        var session = new AlignmentSession
        {
            PrepareAction = _ => Task.FromException<SrtAlignmentDraft>(
                new EngineProcessDrainException("Not drained.", new IOException(), () => ValueTask.CompletedTask))
        };
        var model = new SrtAlignmentViewModel(session, 0);
        await model.PrepareCommand.ExecuteAsync(null);

        Assert.False(model.Busy);
        Assert.False(model.PrepareCommand.CanExecute(null));
        Assert.True(model.CancelCommand.CanExecute(null));
        await model.CancelCommand.ExecuteAsync(null);

        Assert.Equal(1, session.DrainCalls);
        Assert.True(model.PrepareCommand.CanExecute(null));
    }

    static SrtAlignmentDraft MakeDraft(bool partial = false)
    {
        var matched = new SrtAlignmentRow(1, "Some known speech.", TimeSpan.Zero, TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), 1, null);
        var unmatched = new SrtAlignmentRow(2, "Could not place this.", TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(5),
            null, null, 0, "No match");
        return new(partial ? [matched, unmatched] : [matched], canSaveOriginal: true, canSaveTranslated: true);
    }

    sealed class AlignmentPlayer : FakePlaybackController, ISrtAlignmentController
    {
        public AlignmentSession Session { get; } = new();
        public int AvailableSlot { get; init; }
        public int? CapturedSlot { get; private set; }
        public bool CanStartSrtAlignment(int slot) => slot == AvailableSlot;
        public ISrtAlignmentSession CaptureSrtAlignment(int slot)
        {
            CapturedSlot = slot;
            return Session;
        }
    }

    sealed class AlignmentSession : ISrtAlignmentSession
    {
        public string SourcePath => "source.srt";
        public string AudioLabel => "Audio track 1 (English)";
        public string DefaultLanguage => "en";
        public bool CanUseCuda => true;
        public SrtAlignmentDevice DefaultDevice => SrtAlignmentDevice.Cuda;
        public bool IsCurrent { get; set; } = true;
        public SrtAlignmentDraft Result { get; init; } = MakeDraft();
        public Func<CancellationToken, Task<SrtAlignmentDraft>>? PrepareAction { get; init; }
        public Func<Task>? SaveAction { get; set; }
        public (string Language, SrtAlignmentDevice Device)? Invocation { get; private set; }
        public int DrainCalls { get; private set; }
        public List<(string Path, bool Translated, bool AcceptUnmatched)> Saves { get; } = [];

        public Task<SrtAlignmentDraft> PrepareAsync(string language, SrtAlignmentDevice device, CancellationToken token)
        {
            Invocation = (language, device);
            return PrepareAction?.Invoke(token) ?? Task.FromResult(Result);
        }

        public Task SaveAsync(SrtAlignmentDraft draft, string path, bool translated, bool acceptUnmatched, CancellationToken token)
        {
            Saves.Add((path, translated, acceptUnmatched));
            return SaveAction?.Invoke() ?? Task.CompletedTask;
        }

        public Task CancelAndDrainAsync()
        {
            DrainCalls++;
            return Task.CompletedTask;
        }
    }

    sealed class AlignmentShell : IAppShell
    {
        public SrtAlignmentViewModel? Model { get; private set; }
        public bool IsFullScreen { get; set; }
        public void ShowSrtAlignment(SrtAlignmentViewModel model) => Model = model;
        public Task<string?> PickMediaFileAsync(string? initialFolder) => Task.FromResult<string?>(null);
        public Task<string?> PickSubtitlesFileAsync(string? initialFolder) => Task.FromResult<string?>(null);
        public void ShowCheatSheet() { }
        public void SetClipboardText(string text) { }
        public void ApplyTheme(string theme) { }
        public void Exit() { }
    }
}
