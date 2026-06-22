using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LLPlayer.Controls.Settings;
using LLPlayer.ViewModels;
using MaterialDesignThemes.Wpf;

namespace LLPlayer.Views;

public partial class SettingsDialog : UserControl
{
    // MDIX 5.3.1 DialogHost has no built-in Escape handling, and the colour-picker sub-dialog only closes via
    // its own Apply/Cancel buttons. Without this, the UserControl-level Esc InputBinding would close the WHOLE
    // Settings dialog while the picker is still open. Swallow Esc (tunneling, so it wins over the bubbling
    // InputBinding regardless of focus) whenever an inner dialog is open; the picker is dismissed via its buttons.
    private void SettingsDialog_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && DialogHost.IsDialogOpen("SettingsDialog_RootDialog"))
        {
            e.Handled = true;
        }
    }

    // Tracks the single open instance (this is a ShowSingleton dialog) so an external caller can deep-link
    // to a settings section, plus a remembered target applied once the dialog has loaded.
    private static SettingsDialog? _current;
    private static string? _pendingTab;

    /// <summary>True while the settings dialog is open.</summary>
    public static bool IsOpen => _current != null;

    /// <summary>
    /// Deep-link to a settings section by page key (the TreeViewItem Tag). Navigates immediately if the
    /// dialog is open, otherwise the target is applied when the dialog next loads.
    /// </summary>
    public static void RequestNavigate(string pageKey)
    {
        if (_current != null)
        {
            _current.NavigateToTab(pageKey);
        }
        else
        {
            _pendingTab = pageKey;
        }
    }

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

        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _current = this;

        // Clear the open-instance reference on the actual window close. Window.Closed is a reliable
        // one-shot signal, unlike UserControl.Unloaded which can fire spuriously while still open.
        Window host = Window.GetWindow(this);
        if (host != null)
        {
            host.Closed -= OnHostClosed;
            host.Closed += OnHostClosed;
        }

        if (_pendingTab != null)
        {
            NavigateToTab(_pendingTab);
            _pendingTab = null;
        }
    }

    private void OnHostClosed(object? sender, EventArgs e)
    {
        if (_current == this)
        {
            _current = null;
        }

        // Drop any unapplied deep-link target so it cannot hijack the next (manual) open.
        _pendingTab = null;
    }

    /// <summary>Bring the open settings window to the front (restoring it if minimized).</summary>
    public static void ActivateExisting()
    {
        if (_current == null)
        {
            return;
        }

        Window host = Window.GetWindow(_current);
        if (host == null)
        {
            return;
        }

        if (host.WindowState == WindowState.Minimized)
        {
            host.WindowState = WindowState.Normal;
        }

        host.Activate();
    }

    private void NavigateToTab(string pageKey)
    {
        if (SettingsTreeView == null)
        {
            return;
        }

        TreeViewItem? item = FindTreeViewItem(SettingsTreeView.Items, pageKey);
        if (item != null)
        {
            bool wasSelected = item.IsSelected;
            item.IsSelected = true;
            item.BringIntoView();

            // Selecting a not-yet-selected node raises SelectedItemChanged -> LoadPage already runs.
            // Only load explicitly when it was ALREADY selected (no event fires then), avoiding a double load
            // while still guaranteeing a deep-link shows its target section.
            if (wasSelected)
            {
                LoadPage(pageKey);
            }
        }
    }

    private static TreeViewItem? FindTreeViewItem(ItemCollection items, string tag)
    {
        foreach (object obj in items)
        {
            if (obj is not TreeViewItem tvi)
            {
                continue;
            }

            if (tvi.Tag as string == tag)
            {
                return tvi;
            }

            TreeViewItem? child = FindTreeViewItem(tvi.Items, tag);
            if (child != null)
            {
                // Expand the parent so the nested item can be selected/realized.
                tvi.IsExpanded = true;
                return child;
            }
        }

        return null;
    }

    private void SettingsTreeView_OnSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (SettingsTreeView.SelectedItem is TreeViewItem { Tag: string tag })
        {
            LoadPage(tag);
        }
    }

    private void LoadPage(string tag)
    {
        if (SettingsContent == null)
        {
            return;
        }

        if (!_pageFactories.TryGetValue(tag, out Func<UserControl>? factory))
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
