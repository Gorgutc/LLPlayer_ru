using LLPlayer.Avalonia.Services;
using WpfKey = System.Windows.Input.Key;

namespace LLPlayer.Avalonia.ViewModels;

public sealed partial class MainWindowViewModel
{
    readonly AppShortcutDispatcher appKeyDispatcher;
    readonly HashSet<WpfKey> engineKeysAwaitingRelease = [];
    readonly HashSet<WpfKey> inputKeysAwaitingRelease = [];
    readonly HashSet<WpfKey> orphanedInputReleases = [];
    bool keyboardContextLost;

    /// <summary>Cancellation retains consume-only ownership of app presses until their physical releases arrive.</summary>
    public void CancelAppShortcuts() => appKeyDispatcher.CancelPending();

    public void LoseKeyboardContext()
    {
        CancelAppShortcuts();
        keyboardContextLost = true;
    }

    public bool RestoreKeyboardContext()
    {
        if (!keyboardContextLost || disposed || wordActionsStopping) return false;
        keyboardContextLost = false;
        appKeyDispatcher.BeginInputEpoch();
        orphanedInputReleases.UnionWith(inputKeysAwaitingRelease);
        inputKeysAwaitingRelease.Clear();
        // Genuine native presses retain their ownership and are released only by actual physical ups.
        return true;
    }

    public void SuppressInputKeyDown(WpfKey key)
    {
        if (key != WpfKey.None) inputKeysAwaitingRelease.Add(key);
    }

    /// <summary>Owned-child releases may finish parent presses; child-origin input never creates a playback press.</summary>
    public bool HandleTrackedKeyUp(WpfKey key)
    {
        bool inputOwned = inputKeysAwaitingRelease.Remove(key) | orphanedInputReleases.Remove(key);
        if (appKeyDispatcher.HandleKeyUp(key)) return true;
        if (engineKeysAwaitingRelease.Remove(key))
        {
            _ = player.KeyUp(key);
            return true;
        }
        return inputOwned;
    }
}
