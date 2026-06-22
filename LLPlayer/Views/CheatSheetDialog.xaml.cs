using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using LLPlayer.ViewModels;

namespace LLPlayer.Views;

public partial class CheatSheetDialog : UserControl
{
    public CheatSheetDialog()
    {
        InitializeComponent();

        DataContext = ((App)Application.Current).Container.Resolve<CheatSheetDialogVM>();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Window? window = Window.GetWindow(this);
        window!.CommandBindings.Add(new CommandBinding(ApplicationCommands.Find, OnFindExecuted));

        // Focus the search box so the user can type immediately (mirrors CommandPaletteDialog).
        SearchBox.Focus();
        Keyboard.Focus(SearchBox);
    }

    // Esc closes the dialog, but only when there is nothing to clear: the SearchBox binds Esc to
    // ClearText, so the first Esc clears the filter and a second Esc (empty box) closes the dialog.
    private void Root_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || DataContext is not CheatSheetDialogVM vm)
        {
            return;
        }

        // Defer to the SearchBox's Esc -> ClearText only while it actually owns focus; otherwise (focus on a
        // Play button / tab header) Esc closes immediately even if the filter still has text.
        if (!string.IsNullOrEmpty(vm.SearchText) && SearchBox.IsKeyboardFocused)
        {
            return;
        }

        vm.CmdClose.Execute();
        e.Handled = true;
    }

    private void OnFindExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        SearchBox.Focus();
        SearchBox.SelectAll();
        e.Handled = true;
    }
}

[ValueConversion(typeof(int), typeof(Visibility))]
public class CountToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (int.TryParse(value.ToString(), out var count))
        {
            return count == 0 ? Visibility.Collapsed : Visibility.Visible;
        }

        return Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
