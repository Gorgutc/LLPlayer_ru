using CommunityToolkit.Mvvm.ComponentModel;
using LLPlayer.Avalonia.Services;

namespace LLPlayer.Avalonia.ViewModels;

public sealed partial class SettingsViewModel
{
    [ObservableProperty] public partial int ActivityTimeoutMs { get; set; } = AppPrefs.DefaultActivityTimeoutMs;
    [ObservableProperty] public partial string ActivityTimeoutMsText { get; set; } = "";
    [ObservableProperty] public partial bool SeekBarShowOnlyMouseOver { get; set; }
    [ObservableProperty] public partial int SeekBarFadeInTimeMs { get; set; } = AppPrefs.DefaultSeekBarFadeInTimeMs;
    [ObservableProperty] public partial string SeekBarFadeInTimeMsText { get; set; } = "";
    [ObservableProperty] public partial int SeekBarFadeOutTimeMs { get; set; } = AppPrefs.DefaultSeekBarFadeOutTimeMs;
    [ObservableProperty] public partial string SeekBarFadeOutTimeMsText { get; set; } = "";
    [ObservableProperty] public partial double SeekBarHeight { get; set; } = AppPrefs.DefaultSeekBarHeight;
    [ObservableProperty] public partial string SeekBarHeightText { get; set; } = "";
    [ObservableProperty] public partial double SeekBarThumbHeight { get; set; } = AppPrefs.DefaultSeekBarThumbHeight;
    [ObservableProperty] public partial string SeekBarThumbHeightText { get; set; } = "";
    [ObservableProperty] public partial double SeekBarTrackHeight { get; set; } = AppPrefs.DefaultSeekBarTrackHeight;
    [ObservableProperty] public partial string SeekBarTrackHeightText { get; set; } = "";
    [ObservableProperty] public partial bool AlwaysOnTop { get; set; }
    [ObservableProperty] public partial bool MouseSingleClickToPlay { get; set; } = true;
    [ObservableProperty] public partial bool MouseDoubleClickToFullScreen { get; set; } = true;
    [ObservableProperty] public partial bool MouseWheelToVolumeUpDown { get; set; } = true;

    void InitializePlayerUi(AppPrefs current)
    {
        ActivityTimeoutMs = current.ActivityTimeoutMs;
        SeekBarShowOnlyMouseOver = current.SeekBarShowOnlyMouseOver;
        SeekBarFadeInTimeMs = current.SeekBarFadeInTimeMs;
        SeekBarFadeOutTimeMs = current.SeekBarFadeOutTimeMs;
        SeekBarHeight = current.SeekBarHeight;
        SeekBarThumbHeight = current.SeekBarThumbHeight;
        SeekBarTrackHeight = current.SeekBarTrackHeight;
        AlwaysOnTop = current.AlwaysOnTop;
        MouseSingleClickToPlay = current.MouseSingleClickToPlay;
        MouseDoubleClickToFullScreen = current.MouseDoubleClickToFullScreen;
        MouseWheelToVolumeUpDown = current.MouseWheelToVolumeUpDown;
        // Seed raw text even when the loaded value equals the generated property's initial value.
        ActivityTimeoutMsText = ActivityTimeoutMs.ToString(NumberFormat);
        SeekBarFadeInTimeMsText = SeekBarFadeInTimeMs.ToString(NumberFormat);
        SeekBarFadeOutTimeMsText = SeekBarFadeOutTimeMs.ToString(NumberFormat);
        SeekBarHeightText = SeekBarHeight.ToString(NumberFormat);
        SeekBarThumbHeightText = SeekBarThumbHeight.ToString(NumberFormat);
        SeekBarTrackHeightText = SeekBarTrackHeight.ToString(NumberFormat);
    }

    partial void OnActivityTimeoutMsChanged(int value) => ActivityTimeoutMsText = value.ToString(NumberFormat);
    partial void OnSeekBarFadeInTimeMsChanged(int value) => SeekBarFadeInTimeMsText = value.ToString(NumberFormat);
    partial void OnSeekBarFadeOutTimeMsChanged(int value) => SeekBarFadeOutTimeMsText = value.ToString(NumberFormat);
    partial void OnSeekBarHeightChanged(double value) => SeekBarHeightText = value.ToString(NumberFormat);
    partial void OnSeekBarThumbHeightChanged(double value) => SeekBarThumbHeightText = value.ToString(NumberFormat);
    partial void OnSeekBarTrackHeightChanged(double value) => SeekBarTrackHeightText = value.ToString(NumberFormat);

    bool ValidatePlayerUi()
    {
        if (!TryReadWhole(ActivityTimeoutMsText, AppPrefs.MinActivityTimeoutMs, AppPrefs.MaxActivityTimeoutMs, out int idle)
            || !TryReadWhole(SeekBarFadeInTimeMsText, AppPrefs.MinSeekBarFadeTimeMs, AppPrefs.MaxSeekBarFadeTimeMs, out int fadeIn)
            || !TryReadWhole(SeekBarFadeOutTimeMsText, AppPrefs.MinSeekBarFadeTimeMs, AppPrefs.MaxSeekBarFadeTimeMs, out int fadeOut))
        {
            Error = "Use whole milliseconds: idle timeout 0–600000 and seek bar fades 0–10000. Zero applies no idle wait or makes the corresponding fade immediate.";
            return false;
        }
        if (!TryRead(SeekBarHeightText, AppPrefs.MinSeekBarHeight, AppPrefs.MaxSeekBarHeight, out double height)
            || !TryRead(SeekBarThumbHeightText, AppPrefs.MinSeekBarThumbHeight, AppPrefs.MaxSeekBarThumbHeight, out double thumb)
            || !TryRead(SeekBarTrackHeightText, AppPrefs.MinSeekBarTrackHeight, AppPrefs.MaxSeekBarTrackHeight, out double track)
            || track > thumb || thumb > height)
        {
            Error = "Use seek bar height 8–80 DIP, thumb height 4–80 DIP and track height 1–40 DIP, with track ≤ thumb ≤ bar.";
            return false;
        }
        ActivityTimeoutMs = idle;
        SeekBarFadeInTimeMs = fadeIn;
        SeekBarFadeOutTimeMs = fadeOut;
        SeekBarHeight = height;
        SeekBarThumbHeight = thumb;
        SeekBarTrackHeight = track;
        return true;
    }
}
