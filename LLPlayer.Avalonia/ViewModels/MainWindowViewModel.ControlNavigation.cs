namespace LLPlayer.Avalonia.ViewModels;

public sealed partial class MainWindowViewModel
{
    internal void ClearControlRecent()
    {
        string[] previous = prefs.RecentFiles.ToArray();
        prefs.RecentFiles.Clear();
        try
        {
            savePrefs(prefs);
        }
        catch
        {
            prefs.RecentFiles.AddRange(previous);
            throw;
        }
        RecentFiles.Clear();
    }
}
