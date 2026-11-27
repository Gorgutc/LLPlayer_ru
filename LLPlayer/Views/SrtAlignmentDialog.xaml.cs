using System.Windows;
using System.Windows.Controls;
using LLPlayer.ViewModels;

namespace LLPlayer.Views;

public partial class SrtAlignmentDialog : UserControl
{
    public SrtAlignmentDialog()
    {
        InitializeComponent();
        DataContext = ((App)Application.Current).Container.Resolve<SrtAlignmentDialogVM>();
    }
}
