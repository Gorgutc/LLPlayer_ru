using System.Windows;
using System.Windows.Controls;
using LLPlayer.ViewModels;

namespace LLPlayer.Views;

public partial class WordManagerDialog : UserControl
{
    public WordManagerDialog()
    {
        InitializeComponent();

        DataContext = ((App)Application.Current).Container.Resolve<WordManagerDialogVM>();
    }
}
