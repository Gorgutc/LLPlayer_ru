using Avalonia.Controls;

namespace LLPlayer.Avalonia.Views;

public partial class ThemeGalleryPanel : UserControl
{
    public ThemeGalleryPanel()
    {
        InitializeComponent();
    }

    public string Header
    {
        get => HeaderText.Text ?? "";
        set => HeaderText.Text = value;
    }
}
