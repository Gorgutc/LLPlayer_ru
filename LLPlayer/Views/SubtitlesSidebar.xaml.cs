using System.Windows;
using System.Windows.Controls;
using LLPlayer.Controls;
using LLPlayer.ViewModels;

namespace LLPlayer.Views;

public partial class SubtitlesSidebar : UserControl
{
    private SubtitlesSidebarVM VM => (SubtitlesSidebarVM)DataContext;
    public SubtitlesSidebar()
    {
        InitializeComponent();

        DataContext = ((App)Application.Current).Container.Resolve<SubtitlesSidebarVM>();

        Loaded += (sender, args) =>
        {
            VM.RequestScrollToTop += OnRequestScrollToTop;
        };

        Unloaded += (sender, args) =>
        {
            WordPopupControl.ReleaseOwnedResources();
            VM.RequestScrollToTop -= OnRequestScrollToTop;
            VM.Dispose();
        };
    }

    /// <summary>
    /// Scroll to the current subtitle
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void SubtitleListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded && SubtitleListBox.SelectedItem != null)
        {
            SubtitleListBox.ScrollIntoView(SubtitleListBox.SelectedItem);
        }
    }

    /// <summary>
    /// Scroll to the top subtitle
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="args"></param>
    private void OnRequestScrollToTop(object? sender, EventArgs args)
    {
        if (SubtitleListBox.Items.Count <= 0)
            return;

        var first = SubtitleListBox.Items[0];
        if (first != null)
        {
            SubtitleListBox.ScrollIntoView(first);
        }
    }

    private async void SelectableTextBox_OnWordClicked(object? sender, WordClickedEventArgs e)
    {
        try
        {
            await WordPopupControl.OnWordClicked(e);
        }
        catch (Exception ex)
        {
            // async void: keep a provider/process failure on this frequent interaction path out of the global
            // dispatcher exception handler, matching the overlay WordPopup host.
            WordPopupControl.FL.MessageQueue.Enqueue($"Word lookup failed: {ex.Message}");
        }
    }

    /// <summary>
    /// F-16 phase 2a: open the per-line dub-voice picker. The voice choices live in a <see cref="ContextMenu"/>
    /// opened on left-click (a dropdown of the GPU-free voice bank); selecting one sets the cue's
    /// <c>AssignedVoiceId</c> via <c>SubtitlesSidebarVM.CmdSubSetVoice</c>.
    /// </summary>
    private void SubVoiceButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { ContextMenu: { } menu } button)
        {
            menu.PlacementTarget = button;
            menu.IsOpen = true;
        }
    }
}
