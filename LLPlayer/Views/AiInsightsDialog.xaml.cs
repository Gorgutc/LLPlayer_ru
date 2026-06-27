using System.Windows;
using System.Windows.Controls;
using LLPlayer.ViewModels;

namespace LLPlayer.Views;

public partial class AiInsightsDialog : UserControl
{
    public AiInsightsDialog()
    {
        InitializeComponent();

        DataContext = ((App)Application.Current).Container.Resolve<AiInsightsDialogVM>();
    }
}
