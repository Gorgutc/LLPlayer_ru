using Avalonia;
using Avalonia.Controls;

namespace LLPlayer.Avalonia.Views;

public partial class ThemeGalleryWindow : Window
{
    public ThemeGalleryWindow()
    {
        InitializeComponent();
    }

    /// <summary>The whole gallery (both themes, full height) for screenshots.</summary>
    public Visual GalleryContent => GalleryGrid;
}
