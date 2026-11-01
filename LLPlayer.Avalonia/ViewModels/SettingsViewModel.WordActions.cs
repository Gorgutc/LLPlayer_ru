using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LLPlayer.Avalonia.Services;

namespace LLPlayer.Avalonia.ViewModels;

public sealed partial class SettingsViewModel
{
    public ObservableCollection<WordMenuActionPref> WordMenuActions { get; } = [];
    public static IReadOnlyList<WordMenuActionKind> WordMenuActionKinds { get; } =
        [WordMenuActionKind.Search, WordMenuActionKind.Clipboard, WordMenuActionKind.ClipboardAll];
    public static IReadOnlyList<WordSearchModifier> WordSearchModifiers { get; } = Enum.GetValues<WordSearchModifier>();
    public static IReadOnlyList<SubtitleAutoCopyTarget> SubtitleAutoCopyTargets { get; } = Enum.GetValues<SubtitleAutoCopyTarget>();
    [ObservableProperty] public partial bool WordCopyOnSelected { get; set; }
    [ObservableProperty] public partial bool WordLastSearchOnSelected { get; set; }
    [ObservableProperty] public partial WordSearchModifier WordLastSearchModifier { get; set; } = WordSearchModifier.Control;
    [ObservableProperty] public partial bool SubsAutoTextCopy { get; set; }
    [ObservableProperty] public partial SubtitleAutoCopyTarget SubsAutoTextCopyTarget { get; set; } = SubtitleAutoCopyTarget.Primary;
    public IReadOnlyList<WordMenuActionPref> ValidatedWordMenuActions { get; private set; } = [];
    internal bool WordMenuResetRequested { get; private set; }

    void InitializeWordActions(AppPrefs current)
    {
        foreach (WordMenuActionPref row in WordActions.NormalizeRows(current.WordMenuActions)) WordMenuActions.Add(row);
        WordCopyOnSelected = current.WordCopyOnSelected;
        WordLastSearchOnSelected = current.WordLastSearchOnSelected;
        WordLastSearchModifier = current.WordLastSearchModifier;
        SubsAutoTextCopy = current.SubsAutoTextCopy;
        SubsAutoTextCopyTarget = current.SubsAutoTextCopyTarget;
    }

    bool ValidateWordActions()
    {
        if (!Enum.IsDefined(WordLastSearchModifier) || !Enum.IsDefined(SubsAutoTextCopyTarget))
        {
            Error = "Choose a supported word-search modifier and subtitle-copy target.";
            return false;
        }
        if (!WordActions.TryValidateRows(WordMenuActions, out List<WordMenuActionPref> rows, out string error))
        {
            Error = error;
            return false;
        }
        ValidatedWordMenuActions = rows;
        return true;
    }

    [RelayCommand]
    void AddWordAction(WordMenuActionKind kind)
    {
        if (kind is not (WordMenuActionKind.Search or WordMenuActionKind.Clipboard or WordMenuActionKind.ClipboardAll)) return;
        WordMenuActions.Add(new WordMenuActionPref
        {
            Kind = kind,
            Title = kind == WordMenuActionKind.Search ? "Search" : kind == WordMenuActionKind.Clipboard ? "Copy" : "Copy All",
            Url = kind == WordMenuActionKind.Search ? "https://www.google.com/search?q=%w" : "",
        });
    }
    [RelayCommand] void CloneWordAction(WordMenuActionPref? row)
    {
        int index = row == null ? -1 : WordMenuActions.IndexOf(row);
        if (index >= 0) WordMenuActions.Insert(index + 1, row!.Clone());
    }
    [RelayCommand] void DeleteWordAction(WordMenuActionPref? row) { if (row != null) WordMenuActions.Remove(row); }
    [RelayCommand] void MoveWordActionUp(WordMenuActionPref? row)
    {
        int index = row == null ? -1 : WordMenuActions.IndexOf(row);
        if (index > 0) WordMenuActions.Move(index, index - 1);
    }
    [RelayCommand] void MoveWordActionDown(WordMenuActionPref? row)
    {
        int index = row == null ? -1 : WordMenuActions.IndexOf(row);
        if (index >= 0 && index < WordMenuActions.Count - 1) WordMenuActions.Move(index, index + 1);
    }
    [RelayCommand] void ResetWordActions()
    {
        WordMenuResetRequested = true;
        WordMenuActions.Clear();
        foreach (WordMenuActionPref row in WordActions.Defaults()) WordMenuActions.Add(row);
    }
}
