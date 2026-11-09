namespace LLPlayer.Avalonia.Services;

public sealed record MouseHelpRow(string Group, string Gesture, string Description);

public static class MouseHelpCatalog
{
    public static IReadOnlyList<MouseHelpRow> Build(AppPrefs prefs)
    {
        List<MouseHelpRow> rows = [];
        if (prefs.MouseSingleClickToPlay) rows.Add(new("Video", "Left click", "Toggle play / pause, or close an open word popup."));
        if (prefs.MouseDoubleClickToFullScreen) rows.Add(new("Video", "Double click", "Toggle fullscreen. The first tap keeps its normal play / pause action."));
        rows.AddRange([
            new("Video", "Right click", "Open the player context menu."),
            new("Video", "Middle click", "Seek to the current subtitle when one is available."),
            new("Video", "X1 click", "Seek to the previous subtitle, or backward when none is available."),
            new("Video", "X2 click", "Seek to the next subtitle, or forward when none is available."),
        ]);
        if (prefs.MouseWheelToVolumeUpDown) rows.Add(new("Video", "Wheel", "Increase / decrease volume."));
        rows.AddRange([
            new("Video", "Ctrl + wheel", "Zoom in / out."),
            new("Video", "Ctrl + middle click", "Reset zoom and pan."),
            new("Video", "Ctrl + left drag", "Pan the video."),
            new("Video", "Shift + wheel", "Move subtitles up / down."),
            new("Video", "Ctrl + Shift + wheel", "Increase / decrease subtitle size."),
            new("Subtitle text", "Left click on a word", "Run the configured word action (lookup, clipboard or PDIC)."),
            new("Subtitle text", "Left drag across words", "Select a phrase for the configured word action."),
            new("Subtitle text", "Middle click on text", "Select the whole rendered cue for sentence lookup."),
            new("Subtitle text", "Right click on a word", "Open the configured word search / copy actions."),
            new("Menus", "Left click on an action", "Run the selected action."),
            new("Menus", "Right click on an action", "Run the action and keep the menu open."),
            new("Menus", "Ctrl + left click on an action", "Run the action and keep the menu open."),
            new("Window title", "Left click on media title", "Open the media file picker."),
            new("Window title", "Right click on media title", "Open current-media navigation, path and location actions."),
        ]);
        if (prefs.WordLastSearchOnSelected)
        {
            string modifier = prefs.WordLastSearchModifier switch
            {
                WordSearchModifier.Control => "Ctrl",
                WordSearchModifier.Alt => "Alt",
                WordSearchModifier.Shift => "Shift",
                _ => "",
            };
            rows.Add(new("Subtitle text", modifier.Length == 0 ? "Select a word" : modifier + " + select a word",
                "Repeat the last enabled word-search action."));
        }
        return rows;
    }
}
