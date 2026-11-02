using CommunityToolkit.Mvvm.Input;
using FlyleafLib.Plugins;
using LLPlayer.Avalonia.Services;

namespace LLPlayer.Avalonia.ViewModels;

public sealed partial class MainWindowViewModel
{
    [RelayCommand]
    void OpenNextLocalMedia() => OpenLocalSibling(next: true);

    [RelayCommand]
    void OpenPreviousLocalMedia() => OpenLocalSibling(next: false);

    void OpenLocalSibling(bool next)
    {
        if (disposed) return;
        string? source = player.CurrentMediaPath;
        try
        {
            string? target = LocalMediaNavigation.GetSibling(source ?? "", next);
            if (target != null) Open(target);
        }
        catch (Exception error)
        {
            ShowToast("Could not open local media", error.Message, ToastVariant.Destructive);
        }
    }

    [RelayCommand]
    async Task OpenMediaLocationAsync()
    {
        if (disposed) return;
        string? source = player.CurrentMediaPath;
        try
        {
            if (source != null && OnlineVideoRequest.TryGetUrl(source, out string original)) source = original;
            if (LocalMediaNavigation.TryGetLocalPath(source, out string path))
            {
                if (!File.Exists(path) && !Directory.Exists(path))
                    throw new InvalidOperationException("The current media's local location no longer exists.");
                bool revealed = await shell.RevealMediaAsync(path);
                if (!disposed && !revealed)
                    ShowToast("Could not open media location", "A file manager is not available for this location.", ToastVariant.Destructive);
                return;
            }
            if (!FlyleafLib.Utils.IsSafeProcessUrl(source)
                || !Uri.TryCreate(source, UriKind.Absolute, out Uri? page)
                || page.UserInfo.Length != 0 || string.IsNullOrEmpty(page.Host))
                throw new InvalidOperationException("The current media has no existing local location or public HTTP(S) page to open.");
            bool opened = await shell.OpenBrowserAsync(page);
            if (!disposed && !opened)
                ShowToast("Could not open media location", "A browser is not available for this page.", ToastVariant.Destructive);
        }
        catch (Exception error)
        {
            if (!disposed) ShowToast("Could not open media location", error.Message, ToastVariant.Destructive);
        }
    }

    [RelayCommand]
    void CopyMediaPath()
    {
        if (disposed) return;
        string? source = player.CurrentMediaPath;
        try
        {
            if (string.IsNullOrWhiteSpace(source))
                throw new InvalidOperationException("Open media before copying its path.");
            string path = OnlineVideoRequest.TryGetUrl(source, out string original) ? original : source;
            shell.SetClipboardText(path);
            ShowToast("Copied media path");
        }
        catch (Exception error)
        {
            ShowToast("Could not copy media path", error.Message, ToastVariant.Destructive);
        }
    }
}
