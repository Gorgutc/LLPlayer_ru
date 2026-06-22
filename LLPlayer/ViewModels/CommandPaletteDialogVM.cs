using System.Globalization;
using System.Windows.Data;
using FlyleafLib.MediaPlayer;
using LLPlayer.Converters;
using LLPlayer.Extensions;
using LLPlayer.Services;

namespace LLPlayer.ViewModels;

/// <summary>
/// Ctrl+K command palette (E4). Reuses the CheatSheet's <see cref="KeyBindingCS"/> model: a flat, filterable
/// list of every enabled key-bound action that runs the action's delegate (ActionInternal) and closes.
/// </summary>
public class CommandPaletteDialogVM : Bindable, IDialogAware
{
    public FlyleafManager FL { get; }
    private readonly ListCollectionView _view;

    public CommandPaletteDialogVM(FlyleafManager fl)
    {
        FL = fl;

        KeyToStringConverter keyConverter = new();

        Commands = FL.PlayerConfig.Player.KeyBindings.Keys
            .Where(k => k.IsEnabled)
            .Select(k =>
            {
                KeyBindingCS cmd = new()
                {
                    Action = k.Action,
                    ActionName = k.Action.ToString(),
                    Alt = k.Alt,
                    Ctrl = k.Ctrl,
                    Shift = k.Shift,
                    Description = k.Action.GetDescription(),
                    Group = k.Action.ToGroup(),
                    Key = k.Key,
                    KeyName = (string)keyConverter.Convert(k.Key, typeof(string), null, CultureInfo.CurrentCulture),
                    ActionInternal = k.ActionInternal,
                };

                if (cmd.Action == KeyBindingAction.Custom)
                {
                    if (!Enum.TryParse(k.ActionName, out CustomKeyBindingAction customAction))
                    {
                        return null;
                    }

                    cmd.ActionName = customAction.ToString();
                    cmd.CustomAction = customAction;
                    cmd.Description = customAction.GetDescription();
                    cmd.Group = customAction.ToGroup();
                }

                return cmd;
            })
            // Exclude the palette's own action so activating it doesn't close-then-reopen the palette.
            .Where(c => c != null && c.CustomAction != CustomKeyBindingAction.OpenCommandPalette)
            .OrderBy(c => c!.Description)
            .ToList()!;

        _view = (ListCollectionView)CollectionViewSource.GetDefaultView(Commands);
        _view.Filter = o =>
        {
            if (o is not KeyBindingCS c)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(SearchText))
            {
                return true;
            }

            string q = SearchText.Trim();
            return c.Description.Contains(q, StringComparison.OrdinalIgnoreCase)
                   || c.ActionName.Contains(q, StringComparison.OrdinalIgnoreCase)
                   || c.Shortcut.Contains(q, StringComparison.OrdinalIgnoreCase);
        };

        // Auto-select the first row so Enter has a target.
        SelectedCommand = _view.Cast<KeyBindingCS>().FirstOrDefault();
    }

    public List<KeyBindingCS> Commands { get; }

    public string SearchText
    {
        get;
        set
        {
            if (Set(ref field, value))
            {
                _view.Refresh();
                // Re-select the first match after each filter change (otherwise Enter would be a no-op).
                SelectedCommand = _view.Cast<KeyBindingCS>().FirstOrDefault();
            }
        }
    } = string.Empty;

    public KeyBindingCS? SelectedCommand { get; set => Set(ref field, value); }

    public DelegateCommand<KeyBindingCS> CmdActivate => field ??= new((cmd) =>
    {
        if (cmd == null)
        {
            return;
        }

        // Close first so actions that open their own (singleton) dialog don't fight the palette for focus.
        RequestClose.Invoke(ButtonResult.OK);
        FL.Player.Activity.ForceFullActive();
        // Defensive: a hand-edited config could bind an action with no wired delegate.
        cmd.ActionInternal?.Invoke();
    });

    public DelegateCommand CmdClose => field ??= new(() =>
    {
        RequestClose.Invoke(ButtonResult.Cancel);
    });

    #region IDialogAware
    public string Title { get; set => Set(ref field, value); } = $"Command Palette - {App.Name}";
    public double WindowWidth { get; set => Set(ref field, value); } = 620;
    public double WindowHeight { get; set => Set(ref field, value); } = 520;

    public bool CanCloseDialog() => true;
    public void OnDialogClosed() { }
    public void OnDialogOpened(IDialogParameters parameters) { }
    public DialogCloseListener RequestClose { get; }
    #endregion IDialogAware
}
