using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using LLPlayer.Avalonia.Controls;
using LLPlayer.Avalonia.Services;
using LLPlayer.Avalonia.ViewModels;
using LLPlayer.Avalonia.Views;

namespace LLPlayer.Avalonia.Tests;

public sealed class DiarizationUiTests
{
    [AvaloniaFact]
    public void Recognition_menu_exposes_analysis_for_each_subtitle_slot_and_a_cancel_command()
    {
        var player = new FakePlaybackController();
        var window = new MainWindow(new KeyMapper(), null);
        using var vm = new MainWindowViewModel(player, new AppPrefs(), _ => { }, new FakeTranslator(), window);
        window.Attach(vm, renderer: null);

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var primary = Assert.IsType<MenuItem>(window.FindControl<MenuItem>("AnalyzePrimarySpeakersMenu"));
            var secondary = Assert.IsType<MenuItem>(window.FindControl<MenuItem>("AnalyzeSecondarySpeakersMenu"));
            var cancel = Assert.IsType<MenuItem>(window.FindControl<MenuItem>("CancelDiarizationMenu"));
            Assert.Equal("0", primary.CommandParameter);
            Assert.Equal("1", secondary.CommandParameter);
            Assert.Same(Assert.IsAssignableFrom<IAsyncRelayCommand>(primary.Command), secondary.Command);
            Assert.IsAssignableFrom<IAsyncRelayCommand>(cancel.Command);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Pending_analysis_disables_both_slots_and_can_retry_incomplete_cleanup()
    {
        var player = new DiarizationPlayer { IsOpened = true };
        var window = new MainWindow(new KeyMapper(), null);
        using var vm = new MainWindowViewModel(player, new AppPrefs(), _ => { }, new FakeTranslator(), window);
        window.Attach(vm, renderer: null);

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            MenuItem primary = window.FindControl<MenuItem>("AnalyzePrimarySpeakersMenu")!;
            MenuItem secondary = window.FindControl<MenuItem>("AnalyzeSecondarySpeakersMenu")!;
            MenuItem cancel = window.FindControl<MenuItem>("CancelDiarizationMenu")!;
            TextBlock status = window.FindControl<TextBlock>("DiarizationStatusText")!;
            var start = Assert.IsAssignableFrom<IAsyncRelayCommand>(secondary.Command);
            var stop = Assert.IsAssignableFrom<IAsyncRelayCommand>(cancel.Command);
            Assert.True(start.CanExecute(secondary.CommandParameter));
            Assert.False(stop.CanExecute(null));
            Assert.False(status.IsVisible);

            Task analysis = start.ExecuteAsync(secondary.CommandParameter);
            Assert.Equal(1, player.SelectedSlot);
            Assert.False(start.CanExecute(primary.CommandParameter));
            Assert.False(start.CanExecute(secondary.CommandParameter));
            Assert.True(stop.CanExecute(null));
            Assert.Equal("Analyzing speaker labels…", status.Text);

            await stop.ExecuteAsync(null);
            await analysis;
            Assert.Contains("retry", status.Text);
            Assert.False(start.CanExecute(primary.CommandParameter));
            Assert.True(stop.CanExecute(null));

            await stop.ExecuteAsync(null);
            Assert.Equal(2, player.CancelCalls);
            Assert.False(status.IsVisible);
            Assert.True(start.CanExecute(primary.CommandParameter));
            Assert.False(stop.CanExecute(null));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Live_speaker_badge_updates_existing_row_without_changing_subtitle_text()
    {
        var player = new FakePlaybackController { IsOpened = true };
        var cue = new CueInfo(0, TimeSpan.Zero, TimeSpan.FromSeconds(2), "translated words", true, "en", "fr", "source words");
        player.Cues[0].Add(cue);
        var window = new MainWindow(new KeyMapper(), null) { Width = 1100, Height = 700 };
        using var vm = new MainWindowViewModel(player, new AppPrefs { SidebarVisible = true }.Normalize(), _ => { }, new FakeTranslator(), window);
        window.Attach(vm, renderer: null);
        player.RaiseCuesChanged(0);

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            window.CaptureRenderedFrame()?.Dispose();
            CueItemViewModel row = Assert.Single(vm.Sidebar.Cues);
            SubtitleText subtitle = Assert.Single(window.GetVisualDescendants().OfType<SubtitleText>(), control => ReferenceEquals(row, control.DataContext));
            TextBlock label = Assert.Single(window.GetVisualDescendants().OfType<TextBlock>(), control => control.Name == "CueSpeakerLabel");
            Border badge = Assert.IsType<Border>(label.Parent);
            Assert.False(badge.IsVisible);

            player.Cues[0][0] = cue with { SpeakerId = "Speaker 1" };
            player.RaiseCuesChanged(0);
            Dispatcher.UIThread.RunJobs();

            Assert.Same(row, Assert.Single(vm.Sidebar.Cues));
            Assert.True(badge.IsVisible);
            Assert.Equal("Speaker 1", label.Text);
            Assert.Equal("translated words", subtitle.Text);
            Assert.True(row.IsDisplayTranslated);
            Assert.Equal("source words", row.OriginalText);

            player.Cues[0][0] = cue;
            player.RaiseCuesChanged(0);
            Dispatcher.UIThread.RunJobs();
            Assert.False(badge.IsVisible);
        }
        finally { window.Close(); }
    }

    sealed class DiarizationPlayer : FakePlaybackController, IDiarizationController
    {
        readonly TaskCompletionSource<int> analyze = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool IsDiarizing { get; private set => Set(ref field, value); }
        public bool CanCancelDiarization { get; private set => Set(ref field, value); }
        public int? SelectedSlot { get; private set; }
        public int CancelCalls { get; private set; }

        public Task<int> AnalyzeSpeakersAsync(int slot)
        {
            SelectedSlot = slot;
            IsDiarizing = true;
            CanCancelDiarization = true;
            return analyze.Task;
        }

        public Task CancelDiarizationAsync()
        {
            if (++CancelCalls == 1)
            {
                IsDiarizing = false;
                analyze.SetException(new IOException("Worker cleanup incomplete."));
                return Task.FromException(new IOException("Worker cleanup incomplete."));
            }
            CanCancelDiarization = false;
            return Task.CompletedTask;
        }
    }
}
