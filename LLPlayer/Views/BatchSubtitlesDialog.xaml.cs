using System.Windows;
using System.Windows.Controls;
using LLPlayer.ViewModels;

namespace LLPlayer.Views;

public partial class BatchSubtitlesDialog : UserControl
{
    public BatchSubtitlesDialog()
    {
        InitializeComponent();
        DataContext = ((App)Application.Current).Container.Resolve<BatchSubtitlesDialogVM>();
    }
}
