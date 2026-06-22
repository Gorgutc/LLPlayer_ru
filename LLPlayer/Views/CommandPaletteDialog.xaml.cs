using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LLPlayer.ViewModels;

namespace LLPlayer.Views;

public partial class CommandPaletteDialog : UserControl
{
    public CommandPaletteDialog()
    {
        InitializeComponent();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Focus the search box so the user can type immediately.
        SearchBox.Focus();
        Keyboard.Focus(SearchBox);
    }

    // The search box keeps focus; arrows move the list selection and Enter runs it (typical palette UX).
    private void Root_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not CommandPaletteDialogVM vm)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Escape:
                vm.CmdClose.Execute();
                e.Handled = true;
                break;
            case Key.Enter:
                if (CommandList.SelectedItem is KeyBindingCS cmd)
                {
                    vm.CmdActivate.Execute(cmd);
                }
                e.Handled = true;
                break;
            case Key.Down:
                MoveSelection(1);
                e.Handled = true;
                break;
            case Key.Up:
                MoveSelection(-1);
                e.Handled = true;
                break;
        }
    }

    private void MoveSelection(int delta)
    {
        int count = CommandList.Items.Count;
        if (count == 0)
        {
            return;
        }

        int idx = CommandList.SelectedIndex < 0 ? 0 : CommandList.SelectedIndex + delta;
        idx = Math.Clamp(idx, 0, count - 1);
        CommandList.SelectedIndex = idx;
        CommandList.ScrollIntoView(CommandList.SelectedItem);
    }

    private void CommandList_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is CommandPaletteDialogVM vm && CommandList.SelectedItem is KeyBindingCS cmd)
        {
            vm.CmdActivate.Execute(cmd);
        }
    }
}
