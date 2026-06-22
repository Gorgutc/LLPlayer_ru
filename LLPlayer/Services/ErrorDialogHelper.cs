using System.Windows;
using FlyleafLib.MediaPlayer;
using LLPlayer.Views;

namespace LLPlayer.Services;

public static class ErrorDialogHelper
{
    public static void ShowKnownErrorPopup(string message, string errorType)
    {
        IDialogService? dialogService = TryResolveDialogService();
        if (dialogService == null)
        {
            ShowFallback(message, errorType);
            return;
        }

        DialogParameters p = new()
        {
            { "type", "known" },
            { "message", message },
            { "errorType", errorType }
        };

        dialogService.ShowDialog(nameof(ErrorDialog), p);
    }

    public static void ShowKnownErrorPopup(string message, KnownErrorType errorType)
    {
        ShowKnownErrorPopup(message, errorType.ToString());
    }

    public static void ShowUnknownErrorPopup(string message, string errorType, Exception? ex = null)
    {
        IDialogService? dialogService = TryResolveDialogService();
        if (dialogService == null)
        {
            ShowFallback(message, errorType);
            return;
        }

        DialogParameters p = new()
        {
            { "type", "unknown" },
            { "message", message },
            { "errorType", errorType },
        };

        if (ex != null)
        {
            p.Add("exception", ex);
        }

        dialogService.ShowDialog(nameof(ErrorDialog), p);
    }

    public static void ShowUnknownErrorPopup(string message, UnknownErrorType errorType, Exception? ex = null)
    {
        ShowUnknownErrorPopup(message, errorType.ToString(), ex);
    }

    // Resolving the dialog service can fail (NRE) when the Prism container is not available — e.g. an
    // exception raised during early startup or shutdown. This is the app's error path, so it must NEVER
    // throw: returns null when the themed dialog cannot be shown via Prism.
    private static IDialogService? TryResolveDialogService()
    {
        try
        {
            if (Application.Current is App { Container: { } container })
            {
                return container.Resolve<IDialogService>();
            }
        }
        catch
        {
            // fall through to the basic fallback
        }

        return null;
    }

    // Last resort when the themed dialog cannot be shown (container unavailable): a plain message box is
    // better than a swallowed error or a crash. Never throws.
    private static void ShowFallback(string message, string errorType)
    {
        try
        {
            MessageBox.Show(message, $"LLPlayer - {errorType}", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch
        {
            // No UI thread / shutting down: give up silently rather than crash the process.
        }
    }
}
