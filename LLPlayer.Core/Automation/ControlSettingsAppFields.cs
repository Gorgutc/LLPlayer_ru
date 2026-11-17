#if !WINDOWS
using LLPlayer.Avalonia.Services;

namespace LLPlayer.Core.Automation;

public sealed partial class ControlSettingsService
{
    readonly SettingsFamily<AppPrefs> app;
    readonly PrefsStore prefsStore;

    public AppPrefs CaptureAppPrefs() { lock (gate) return app.Load().Settings; }

    static SettingsFields<AppPrefs> AppFields()
    {
        var fields = new SettingsFields<AppPrefs>();
        fields.Add("volume", s => s.Volume, (s, value) => s.Volume = value);
        fields.Add("mute", s => s.Mute, (s, value) => s.Mute = value);
        fields.Add("activityTimeoutMs", s => s.ActivityTimeoutMs, (s, value) => s.ActivityTimeoutMs = value);
        fields.Add("seekBarShowOnlyMouseOver", s => s.SeekBarShowOnlyMouseOver, (s, value) => s.SeekBarShowOnlyMouseOver = value);
        fields.Add("seekBarFadeInTimeMs", s => s.SeekBarFadeInTimeMs, (s, value) => s.SeekBarFadeInTimeMs = value);
        fields.Add("seekBarFadeOutTimeMs", s => s.SeekBarFadeOutTimeMs, (s, value) => s.SeekBarFadeOutTimeMs = value);
        fields.Add("seekBarHeight", s => s.SeekBarHeight, (s, value) => s.SeekBarHeight = value);
        fields.Add("seekBarThumbHeight", s => s.SeekBarThumbHeight, (s, value) => s.SeekBarThumbHeight = value);
        fields.Add("seekBarTrackHeight", s => s.SeekBarTrackHeight, (s, value) => s.SeekBarTrackHeight = value);
        fields.Add("alwaysOnTop", s => s.AlwaysOnTop, (s, value) => s.AlwaysOnTop = value);
        fields.Add("mouseSingleClickToPlay", s => s.MouseSingleClickToPlay, (s, value) => s.MouseSingleClickToPlay = value);
        fields.Add("mouseDoubleClickToFullScreen", s => s.MouseDoubleClickToFullScreen, (s, value) => s.MouseDoubleClickToFullScreen = value);
        fields.Add("mouseWheelToVolumeUpDown", s => s.MouseWheelToVolumeUpDown, (s, value) => s.MouseWheelToVolumeUpDown = value);
        fields.Add("subtitleFontSize", s => s.SubtitleFontSize, (s, value) => s.SubtitleFontSize = value);
        fields.Add("secondaryFontScale", s => s.SecondaryFontScale, (s, value) => s.SecondaryFontScale = value);
        fields.Add("subtitleFontFamily", s => s.SubtitleFontFamily, (s, value) => s.SubtitleFontFamily = value);
        fields.Add("subtitleFontWeight", s => s.SubtitleFontWeight, (s, value) => s.SubtitleFontWeight = value);
        fields.Add("subtitleFontStyle", s => s.SubtitleFontStyle, (s, value) => s.SubtitleFontStyle = value);
        fields.Add("secondarySubtitleFontFamily", s => s.SecondarySubtitleFontFamily, (s, value) => s.SecondarySubtitleFontFamily = value);
        fields.Add("secondarySubtitleFontWeight", s => s.SecondarySubtitleFontWeight, (s, value) => s.SecondarySubtitleFontWeight = value);
        fields.Add("secondarySubtitleFontStyle", s => s.SecondarySubtitleFontStyle, (s, value) => s.SecondarySubtitleFontStyle = value);
        fields.Add("secondarySubtitleInheritsFont", s => s.SecondarySubtitleInheritsFont, (s, value) => s.SecondarySubtitleInheritsFont = value);
        fields.Add("subtitleBottomOffset", s => s.SubtitleBottomOffset, (s, value) => s.SubtitleBottomOffset = value);
        fields.Add("subtitleOutlineThickness", s => s.SubtitleOutlineThickness, (s, value) => s.SubtitleOutlineThickness = value);
        fields.Add("secondarySubtitleOutlineThickness", s => s.SecondarySubtitleOutlineThickness, (s, value) => s.SecondarySubtitleOutlineThickness = value);
        fields.Add("subtitleTextColor", s => s.SubtitleTextColor, (s, value) => s.SubtitleTextColor = value);
        fields.Add("secondarySubtitleTextColor", s => s.SecondarySubtitleTextColor, (s, value) => s.SecondarySubtitleTextColor = value);
        fields.Add("subtitleOutlineColor", s => s.SubtitleOutlineColor, (s, value) => s.SubtitleOutlineColor = value);
        fields.Add("subtitleBackgroundColor", s => s.SubtitleBackgroundColor, (s, value) => s.SubtitleBackgroundColor = value);
        fields.Add("subtitleBackgroundOpacityPercent", s => s.SubtitleBackgroundOpacityPercent, (s, value) => s.SubtitleBackgroundOpacityPercent = value);
        fields.Add("secondarySubtitleBackgroundOpacityPercent", s => s.SecondarySubtitleBackgroundOpacityPercent, (s, value) => s.SecondarySubtitleBackgroundOpacityPercent = value);
        fields.Add("subtitleWidthPercent", s => s.SubtitleWidthPercent, (s, value) => s.SubtitleWidthPercent = value);
        fields.Add("subtitleMaxWidth", s => s.SubtitleMaxWidth, (s, value) => s.SubtitleMaxWidth = value);
        fields.Add("subtitleIgnoreLineBreak", s => s.SubtitleIgnoreLineBreak, (s, value) => s.SubtitleIgnoreLineBreak = value);
        fields.Add("subtitleLineAlignment", s => s.SubtitleLineAlignment, (s, value) => s.SubtitleLineAlignment = value);
        fields.Add("subtitlePrimarySecondaryDistance", s => s.SubtitlePrimarySecondaryDistance, (s, value) => s.SubtitlePrimarySecondaryDistance = value);
        fields.Add("subtitleSeparatorMaxWidth", s => s.SubtitleSeparatorMaxWidth, (s, value) => s.SubtitleSeparatorMaxWidth = value);
        fields.Add("subtitleSeparatorOpacityPercent", s => s.SubtitleSeparatorOpacityPercent, (s, value) => s.SubtitleSeparatorOpacityPercent = value);
        fields.Add("subtitlePositionAlignment", s => s.SubtitlePositionAlignment, (s, value) => s.SubtitlePositionAlignment = value);
        fields.Add("subtitlePositionAlignmentWhenDual", s => s.SubtitlePositionAlignmentWhenDual, (s, value) => s.SubtitlePositionAlignmentWhenDual = value);
        fields.Add("subtitlesVisible", s => s.SubtitlesVisible, (s, value) => s.SubtitlesVisible = value);
        fields.Add("showWaveform", s => s.ShowWaveform, (s, value) => s.ShowWaveform = value);
        fields.Add("appShortcutOverrides", s => s.AppShortcutOverrides, (s, value) => s.AppShortcutOverrides = value);
        fields.Add("appKeyBindings", s => s.AppKeyBindings, (s, value) => s.AppKeyBindings = value, allowsNull: true);
        fields.Add("engineKeysCustomized", s => s.EngineKeysCustomized, (s, value) => s.EngineKeysCustomized = value);
        fields.Add("engineKeyBindings", s => s.EngineKeyBindings, (s, value) => s.EngineKeyBindings = value);
        fields.Add("sidebarSearchMatchCase", s => s.SidebarSearchMatchCase, (s, value) => s.SidebarSearchMatchCase = value);
        fields.Add("sidebarSearchWholeWord", s => s.SidebarSearchWholeWord, (s, value) => s.SidebarSearchWholeWord = value);
        fields.Add("sidebarSearchRegex", s => s.SidebarSearchRegex, (s, value) => s.SidebarSearchRegex = value);
        fields.Add("sidebarShowSecondary", s => s.SidebarShowSecondary, (s, value) => s.SidebarShowSecondary = value);
        fields.Add("sidebarVisible", s => s.SidebarVisible, (s, value) => s.SidebarVisible = value);
        fields.Add("sidebarWidth", s => s.SidebarWidth, (s, value) => s.SidebarWidth = value);
        fields.Add("sidebarLeft", s => s.SidebarLeft, (s, value) => s.SidebarLeft = value);
        fields.Add("sidebarTextMask", s => s.SidebarTextMask, (s, value) => s.SidebarTextMask = value);
        fields.Add("sidebarShowOriginalText", s => s.SidebarShowOriginalText, (s, value) => s.SidebarShowOriginalText = value);
        fields.Add("sidebarSubPadding", s => s.SidebarSubPadding, (s, value) => s.SidebarSubPadding = value);
        fields.Add("sidebarFontFamily", s => s.SidebarFontFamily, (s, value) => s.SidebarFontFamily = value);
        fields.Add("sidebarFontSize", s => s.SidebarFontSize, (s, value) => s.SidebarFontSize = value);
        fields.Add("sidebarFontWeight", s => s.SidebarFontWeight, (s, value) => s.SidebarFontWeight = value);
        fields.Add("theme", s => s.Theme, (s, value) => s.Theme = value);
        fields.Add("recentFiles", s => s.RecentFiles, (s, value) => s.RecentFiles = value);
        fields.Add("lastFolder", s => s.LastFolder, (s, value) => s.LastFolder = value, allowsNull: true);
        fields.Add("wordTranslationService", s => s.WordTranslationService, (s, value) => s.WordTranslationService = value);
        fields.Add("translateTargetLanguage", s => s.TranslateTargetLanguage, (s, value) => s.TranslateTargetLanguage = value, allowsNull: true);
        fields.Add("wordClickAction", s => s.WordClickAction, (s, value) => s.WordClickAction = value);
        fields.Add("ankiDeck", s => s.AnkiDeck, (s, value) => s.AnkiDeck = value);
        fields.Add("wordMenuActions", s => s.WordMenuActions, (s, value) => s.WordMenuActions = value, allowsNull: true);
        fields.Add("wordCopyOnSelected", s => s.WordCopyOnSelected, (s, value) => s.WordCopyOnSelected = value);
        fields.Add("wordLastSearchOnSelected", s => s.WordLastSearchOnSelected, (s, value) => s.WordLastSearchOnSelected = value);
        fields.Add("wordLastSearchModifier", s => s.WordLastSearchModifier, (s, value) => s.WordLastSearchModifier = value);
        fields.Add("subsAutoTextCopy", s => s.SubsAutoTextCopy, (s, value) => s.SubsAutoTextCopy = value);
        fields.Add("subsAutoTextCopyTarget", s => s.SubsAutoTextCopyTarget, (s, value) => s.SubsAutoTextCopyTarget = value);
        return fields;
    }

    static void ValidateApp(AppPrefs prefs)
    {
        if (!AppPrefs.TryNormalizeAnkiDeck(prefs.AnkiDeck, out _)
            || !AppPrefs.TryNormalizeWordClickAction(prefs.WordClickAction, out _)
            || !AppShortcutMap.TryNormalizeOverrides(prefs.AppShortcutOverrides, out var overrides))
            throw new ArgumentException("An app preference is invalid.");

        if (prefs.AppKeyBindings is { } appKeys && AppKeyBindings.NormalizePrefs(appKeys)?.Count != appKeys.Count)
            throw new ArgumentException("An application key binding is invalid.");
        var shortcuts = new AppShortcutMap(new Dictionary<string, Action>(), overrides, prefs.AppKeyBindings);
        if (!shortcuts.TryValidateEffective(out _)) throw new ArgumentException("Application shortcuts conflict.");
        if (prefs.EngineKeysCustomized
            && (EngineKeyBindings.NormalizePrefs(prefs.EngineKeyBindings).Count != prefs.EngineKeyBindings.Count
                || !EngineKeyBindings.TryValidate(EngineKeyBindings.FromPrefs(prefs.EngineKeyBindings), shortcuts.Shortcuts, out _)))
            throw new ArgumentException("Engine shortcuts are invalid or conflicting.");
        prefs.Normalize();
    }
}
#endif
