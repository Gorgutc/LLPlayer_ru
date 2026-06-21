using System.Windows;
using System.Windows.Controls;
using LLPlayer.Controls.Settings;
using LLPlayer.ViewModels;

namespace LLPlayer.Views;

public partial class SettingsDialog : UserControl
{
    // Lazily created once per dialog instance, then reused — avoids re-instantiating a settings page
    // (and the UI-thread freeze on heavy pages like ASR/Trans/Keys) on every tree selection, and keeps
    // each page's scroll position / transient state while the dialog is open.
    private readonly Dictionary<string, UserControl> _pageCache = new();
    private readonly Dictionary<string, Func<UserControl>> _pageFactories;

    public SettingsDialog()
    {
        InitializeComponent();

        DataContext = ((App)Application.Current).Container.Resolve<SettingsDialogVM>();

        _pageFactories = new Dictionary<string, Func<UserControl>>
        {
            [nameof(SettingsPlayer)] = () => new SettingsPlayer(),
            [nameof(SettingsAudio)] = () => new SettingsAudio(),
            [nameof(SettingsVideo)] = () => new SettingsVideo(),
            [nameof(SettingsSubtitles)] = () => new SettingsSubtitles(),
            [nameof(SettingsSubtitlesPS)] = () => new SettingsSubtitlesPS(),
            [nameof(SettingsSubtitlesASR)] = () => new SettingsSubtitlesASR(),
            [nameof(SettingsSubtitlesOCR)] = () => new SettingsSubtitlesOCR(),
            [nameof(SettingsSubtitlesTrans)] = () => new SettingsSubtitlesTrans(),
            [nameof(SettingsSubtitlesAction)] = () => new SettingsSubtitlesAction(),
            [nameof(SettingsKeys)] = () => new SettingsKeys(),
            [nameof(SettingsKeysOffset)] = () => new SettingsKeysOffset(),
            [nameof(SettingsMouse)] = () => new SettingsMouse(),
            [nameof(SettingsThemes)] = () => new SettingsThemes(),
            [nameof(SettingsPlugins)] = () => new SettingsPlugins(),
            [nameof(SettingsAbout)] = () => new SettingsAbout(),
        };
    }

    private void SettingsTreeView_OnSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (SettingsContent == null)
        {
            return;
        }

        if (SettingsTreeView.SelectedItem is not TreeViewItem selectedItem)
        {
            return;
        }

        if (selectedItem.Tag is not string tag || !_pageFactories.TryGetValue(tag, out Func<UserControl>? factory))
        {
            return;
        }

        if (!_pageCache.TryGetValue(tag, out UserControl? page))
        {
            page = factory();
            _pageCache[tag] = page;
        }

        SettingsContent.Content = page;
    }
}
