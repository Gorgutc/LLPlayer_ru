using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LLPlayer.Avalonia.Services;

namespace LLPlayer.Avalonia.ViewModels;

public sealed partial class PlaylistViewModel : ObservableObject, IDisposable
{
    readonly IPlaybackController player;
    bool disposed;
    public ObservableCollection<PlaybackPlaylistItem> Items { get; } = [];
    [ObservableProperty] PlaybackPlaylistItem? selectedItem;
    [ObservableProperty] string title = "Playlist";
    [ObservableProperty] string summary = "No playlist items.";
    [ObservableProperty] string message = "";
    [ObservableProperty] bool busy;
    int CurrentIndex => Items.ToList().FindIndex(item => item.IsCurrent);
    public bool HasPrevious => !disposed && CurrentIndex > 0;
    public bool HasNext => !disposed && CurrentIndex >= 0 && CurrentIndex + 1 < Items.Count;
    bool CanOpen => !disposed && !Busy && !player.IsOpening && !player.CanCancelOpen;
    bool CanPrevious() => CanOpen && HasPrevious;
    bool CanNext() => CanOpen && HasNext;
    bool CanReopen() => CanOpen && Items.Any(item => item.IsCurrent);
    bool CanOpenSelected() => CanOpen && SelectedItem != null && Items.Contains(SelectedItem);

    partial void OnBusyChanged(bool value) => NotifyAvailability();
    partial void OnSelectedItemChanged(PlaybackPlaylistItem? value) => OpenSelectedCommand.NotifyCanExecuteChanged();
    void NotifyAvailability()
    {
        OnPropertyChanged(nameof(HasPrevious));
        OnPropertyChanged(nameof(HasNext));
        PreviousCommand.NotifyCanExecuteChanged();
        NextCommand.NotifyCanExecuteChanged();
        ReopenCommand.NotifyCanExecuteChanged();
        OpenSelectedCommand.NotifyCanExecuteChanged();
    }

    public PlaylistViewModel(IPlaybackController player)
    {
        this.player = player;
        player.PlaylistChanged += Refresh;
        player.PropertyChanged += OnPlayerChanged;
        Refresh();
    }
    void OnPlayerChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IPlaybackController.IsOpening) or nameof(IPlaybackController.CanCancelOpen)) Refresh();
    }
    public void Refresh()
    {
        if (disposed) return;
        PlaybackPlaylistItem? previous = SelectedItem;
        Items.Clear();
        foreach (var item in player.GetPlaylistItems()) Items.Add(item);
        SelectedItem = Items.FirstOrDefault(item => previous != null && Equals(item.Token, previous.Token))
            ?? Items.FirstOrDefault(item => item.IsCurrent);
        Title = player.PlaylistTitle;
        int expected = Math.Max(player.PlaylistExpectedCount, Items.Count);
        Summary = Items.Count == 0 ? player.IsOpening ? "Opening playlist…" : "No playlist items."
            : expected > Items.Count ? $"{Items.Count} playable / {expected} expected. Unavailable or unselected entries are omitted."
            : $"{Items.Count} item{(Items.Count == 1 ? "" : "s")}";
        NotifyAvailability();
    }

    async Task OpenItemAsync(PlaybackPlaylistItem? item)
    {
        if (!CanOpen || item == null || !Items.Contains(item)) return;
        Busy = true;
        Message = "";
        try { await player.SelectPlaylistItemAsync(item); }
        catch (Exception) { Message = "The playlist item could not be opened. Select an available item and try again."; }
        finally { Busy = false; Refresh(); }
    }
    [RelayCommand(CanExecute = nameof(CanOpenSelected))] Task OpenSelectedAsync() => OpenItemAsync(SelectedItem);
    [RelayCommand(CanExecute = nameof(CanReopen))] Task ReopenAsync() => OpenItemAsync(Items.FirstOrDefault(item => item.IsCurrent));
    [RelayCommand(CanExecute = nameof(CanPrevious))] Task PreviousAsync()
    {
        int current = Items.ToList().FindIndex(item => item.IsCurrent);
        return OpenItemAsync(current > 0 ? Items[current - 1] : null);
    }
    [RelayCommand(CanExecute = nameof(CanNext))] Task NextAsync()
    {
        int current = Items.ToList().FindIndex(item => item.IsCurrent);
        return OpenItemAsync(current >= 0 && current + 1 < Items.Count ? Items[current + 1] : null);
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        player.PlaylistChanged -= Refresh;
        player.PropertyChanged -= OnPlayerChanged;
        NotifyAvailability();
    }
}
