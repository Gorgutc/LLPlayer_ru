using LLPlayer.Avalonia.Services;
using LLPlayer.Avalonia.ViewModels;

namespace LLPlayer.Avalonia.Tests;

public sealed class OnlinePlaylistAvailabilityTests
{
    static bool Availability(PlaylistViewModel model, string name)
    {
        var property = typeof(PlaylistViewModel).GetProperty(name);
        Assert.NotNull(property);
        return (bool)property.GetValue(model)!;
    }

    [Theory]
    [InlineData(0, false, true)]
    [InlineData(1, true, true)]
    [InlineData(2, true, false)]
    [InlineData(-1, false, false)]
    public void AdjacentAvailabilityAndCanExecuteMatchFirstMiddleLastAndNoCurrent(int current, bool previous, bool next)
    {
        AvailabilityFake player = new(); player.Replace(3, current);
        using PlaylistViewModel model = new(player);
        Assert.Equal(previous, Availability(model, "HasPrevious"));
        Assert.Equal(next, Availability(model, "HasNext"));
        Assert.Equal(previous, model.PreviousCommand.CanExecute(null));
        Assert.Equal(next, model.NextCommand.CanExecute(null));
        player.Replace(0, -1);
        Assert.False(Availability(model, "HasPrevious"));
        Assert.False(Availability(model, "HasNext"));
        Assert.False(model.PreviousCommand.CanExecute(null));
        Assert.False(model.NextCommand.CanExecute(null));
    }

    [Fact]
    public async Task BusyAndOpeningDisableCommands_ReplacementUsesOnlyCurrentDetachedTokens()
    {
        AvailabilityFake player = new(); player.Replace(3, 1);
        using PlaylistViewModel model = new(player);
        int nextChanges = 0, previousChanges = 0;
        model.NextCommand.CanExecuteChanged += (_, _) => nextChanges++;
        model.PreviousCommand.CanExecuteChanged += (_, _) => previousChanges++;
        player.IsOpening = true;
        Assert.False(model.NextCommand.CanExecute(null));
        Assert.False(model.PreviousCommand.CanExecute(null));
        await model.NextCommand.ExecuteAsync(null);
        Assert.Empty(player.Selected);
        player.IsOpening = false;
        Assert.True(model.NextCommand.CanExecute(null));
        TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        player.Pending = completion.Task;
        Task next = model.NextCommand.ExecuteAsync(null);
        Assert.True(model.Busy);
        Assert.False(model.NextCommand.CanExecute(null));
        Assert.False(model.PreviousCommand.CanExecute(null));
        Assert.False(model.OpenSelectedCommand.CanExecute(null));
        Assert.False(model.ReopenCommand.CanExecute(null));
        completion.SetResult(); await next;
        Assert.False(model.Busy);
        Assert.True(model.PreviousCommand.CanExecute(null));
        Assert.True(model.NextCommand.CanExecute(null));
        Assert.True(previousChanges >= 4);
        Assert.True(nextChanges >= 4);
        player.Replace(2, 0);
        PlaybackPlaylistItem expected = player.Rows[1];
        Assert.False(model.PreviousCommand.CanExecute(null));
        Assert.True(model.NextCommand.CanExecute(null));
        player.Pending = Task.CompletedTask;
        await model.NextCommand.ExecuteAsync(null);
        Assert.Same(expected.Token, player.Selected[^1].Token);
        Assert.DoesNotContain(player.Rows, row => ReferenceEquals(row.Token, player.Selected[0].Token));
    }

    sealed class AvailabilityFake : FakePlaybackController, IPlaybackController
    {
        public event Action? PlaylistChanged;
        public List<PlaybackPlaylistItem> Rows { get; private set; } = [];
        public List<PlaybackPlaylistItem> Selected { get; } = [];
        public Task Pending { get; set; } = Task.CompletedTask;
        public IReadOnlyList<PlaybackPlaylistItem> GetPlaylistItems() => Rows;
        public Task SelectPlaylistItemAsync(PlaybackPlaylistItem item) { Selected.Add(item); return Pending; }
        public void Replace(int count, int current)
        {
            Rows = Enumerable.Range(0, count).Select(index => new PlaybackPlaylistItem(index, $"Row {index}", index == current, new object())).ToList();
            PlaylistChanged?.Invoke();
        }
    }
}
