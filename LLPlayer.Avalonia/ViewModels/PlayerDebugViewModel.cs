using CommunityToolkit.Mvvm.ComponentModel;
using LLPlayer.Avalonia.Services;

namespace LLPlayer.Avalonia.ViewModels;

public sealed partial class PlayerDebugViewModel : ObservableObject
{
    public PlayerDebugViewModel(PlayerDebugSnapshot snapshot) => Refresh(snapshot);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CopyText))]
    public partial PlayerDebugSnapshot Snapshot { get; private set; } = new("", "", "", "", "", false, "", "", "", "", "");

    public string Direct3DStatus => "Direct3D renderer statistics are not exposed by the shared portable renderer.";
    public string CopyText => $"Playback: {Snapshot.Status}\nTitle: {Snapshot.MediaTitle}\nSource: {Snapshot.MediaSource}\nPosition: {Snapshot.Position} / {Snapshot.Duration}\nRecording: {Snapshot.IsRecording}\nVideo: {Snapshot.Video}\nAudio: {Snapshot.Audio}\nSubtitles: {Snapshot.Subtitles}\nRenderer: {Snapshot.Renderer}\nDecode: {Snapshot.Hardware}\n{Direct3DStatus}";

    public void Refresh(PlayerDebugSnapshot snapshot)
        => Snapshot = snapshot with
        {
            MediaTitle = PlayerDebugSnapshot.SafeMediaTitle(snapshot.MediaTitle),
            MediaSource = PlayerDebugSnapshot.SafeMediaSource(snapshot.MediaSource),
        };
}
