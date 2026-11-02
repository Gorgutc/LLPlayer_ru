using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using LLPlayer.Avalonia.Services;

namespace LLPlayer.Avalonia.ViewModels;

public sealed partial class MainWindowViewModel
{
    bool transportPointerOver;
    bool transportFocusWithin;
    bool transportPopupOpen;
    DateTimeOffset? lastKeyboardActivity;

    [ObservableProperty] public partial int ActivityTimeoutMs { get; private set; } = AppPrefs.DefaultActivityTimeoutMs;
    [ObservableProperty] public partial bool SeekBarShowOnlyMouseOver { get; private set; }
    [ObservableProperty] public partial int SeekBarFadeInTimeMs { get; private set; } = AppPrefs.DefaultSeekBarFadeInTimeMs;
    [ObservableProperty] public partial int SeekBarFadeOutTimeMs { get; private set; } = AppPrefs.DefaultSeekBarFadeOutTimeMs;
    [ObservableProperty] public partial double SeekBarHeight { get; private set; } = AppPrefs.DefaultSeekBarHeight;
    [ObservableProperty] public partial double SeekBarThumbHeight { get; private set; } = AppPrefs.DefaultSeekBarThumbHeight;
    [ObservableProperty] public partial double SeekBarTrackHeight { get; private set; } = AppPrefs.DefaultSeekBarTrackHeight;
    [ObservableProperty] public partial bool AlwaysOnTop { get; private set; }
    [ObservableProperty] public partial bool MouseSingleClickToPlay { get; private set; } = true;
    [ObservableProperty] public partial bool MouseDoubleClickToFullScreen { get; private set; } = true;
    [ObservableProperty] public partial bool MouseWheelToVolumeUpDown { get; private set; } = true;

    static void MergePlayerUiSettings(AppPrefs candidate, SettingsViewModel draft)
    {
        candidate.ActivityTimeoutMs = draft.ActivityTimeoutMs;
        candidate.SeekBarShowOnlyMouseOver = draft.SeekBarShowOnlyMouseOver;
        candidate.SeekBarFadeInTimeMs = draft.SeekBarFadeInTimeMs;
        candidate.SeekBarFadeOutTimeMs = draft.SeekBarFadeOutTimeMs;
        candidate.SeekBarHeight = draft.SeekBarHeight;
        candidate.SeekBarThumbHeight = draft.SeekBarThumbHeight;
        candidate.SeekBarTrackHeight = draft.SeekBarTrackHeight;
        candidate.AlwaysOnTop = draft.AlwaysOnTop;
        candidate.MouseSingleClickToPlay = draft.MouseSingleClickToPlay;
        candidate.MouseDoubleClickToFullScreen = draft.MouseDoubleClickToFullScreen;
        candidate.MouseWheelToVolumeUpDown = draft.MouseWheelToVolumeUpDown;
    }

    // Called only at startup or after the detached candidate was persisted successfully.
    void ApplyPlayerUiPreferences(AppPrefs source)
    {
        prefs.ActivityTimeoutMs = ActivityTimeoutMs = source.ActivityTimeoutMs;
        prefs.SeekBarShowOnlyMouseOver = SeekBarShowOnlyMouseOver = source.SeekBarShowOnlyMouseOver;
        prefs.SeekBarFadeInTimeMs = SeekBarFadeInTimeMs = source.SeekBarFadeInTimeMs;
        prefs.SeekBarFadeOutTimeMs = SeekBarFadeOutTimeMs = source.SeekBarFadeOutTimeMs;
        prefs.SeekBarHeight = SeekBarHeight = source.SeekBarHeight;
        prefs.SeekBarThumbHeight = SeekBarThumbHeight = source.SeekBarThumbHeight;
        prefs.SeekBarTrackHeight = SeekBarTrackHeight = source.SeekBarTrackHeight;
        prefs.AlwaysOnTop = AlwaysOnTop = source.AlwaysOnTop;
        prefs.MouseSingleClickToPlay = MouseSingleClickToPlay = source.MouseSingleClickToPlay;
        prefs.MouseDoubleClickToFullScreen = MouseDoubleClickToFullScreen = source.MouseDoubleClickToFullScreen;
        prefs.MouseWheelToVolumeUpDown = MouseWheelToVolumeUpDown = source.MouseWheelToVolumeUpDown;
        UpdateIdle();
    }

    /// <summary>The stable transport reveal area owns hover even while the bar itself is hidden.</summary>
    public void SetTransportPointerOver(bool pointerOver)
    {
        transportPointerOver = pointerOver;
        UpdateIdle();
    }

    /// <summary>Keyboard focus keeps the transport usable until focus leaves the transport.</summary>
    public void SetTransportFocusWithin(bool focusWithin)
    {
        transportFocusWithin = focusWithin;
        UpdateIdle();
    }

    /// <summary>Detached transport flyouts remain visible after the pointer leaves their reveal area.</summary>
    public void SetTransportPopupOpen(bool popupOpen)
    {
        transportPopupOpen = popupOpen;
        UpdateIdle();
    }

    /// <summary>Reveal on keyboard input without treating unrelated pointer movement as transport hover.</summary>
    public void NotifyKeyboardActivity()
    {
        lastKeyboardActivity = lastActivity = time.GetUtcNow();
        UpdateIdle();
    }

    void OnPlayerUiWordPopupChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WordPopupViewModel.IsOpen)) UpdateIdle();
    }
}
