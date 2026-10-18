using System.Windows.Input;

namespace FlyleafLib.MediaPlayer;

// F-13 portable (Linux) counterparts of the WPF/WinForms-bound members of Player.Extra.cs / Player.Keys.cs
// (excluded there with #if WINDOWS). Clipboard / file picker go through Utils.HostServices.
unsafe partial class Player
{
    /// <summary>
    /// Host-provided keyboard state used by <see cref="KeyDown(Player, Key)"/> to read the Alt/Ctrl/Shift modifiers
    /// (WPF's <c>Keyboard.IsKeyDown</c> on Windows). Hosts map their key events to <see cref="Key"/> and call
    /// <see cref="KeyDown(Player, Key)"/> / <see cref="KeyUp(Player, Key)"/>.
    /// </summary>
    public static Func<Key, bool> KeyStateProvider { get; set; }

    public void CopyToClipboard()
    {
        var url = Playlist.Url;
        if (url == null || Utils.HostServices == null)
            return;

        Utils.HostServices.SetClipboardText(url);
        OSDMessage = $"Copy {url}";
    }
    public void CopyItemToClipboard()
    {
        if (Playlist.Selected == null || Playlist.Selected.DirectUrl == null || Utils.HostServices == null)
            return;

        string url = Playlist.Selected.DirectUrl;

        Utils.HostServices.SetClipboardText(url);
        OSDMessage = $"Copy {url}";
    }
    public void OpenFromClipboard()
    {
        string text = Utils.HostServices?.GetClipboardText();
        if (!string.IsNullOrWhiteSpace(text))
        {
            OpenAsync(text);
        }
    }

    public void OpenFromClipboardSafe()
    {
        if (decoder.Playlist.Selected != null)
        {
            return;
        }

        OpenFromClipboard();
    }

    /// <summary>Shows the host's file picker (<see cref="IHostServices.PickMediaFileAsync"/>) and opens the selection.</summary>
    public void OpenFromFileDialog()
    {
        var host = Utils.HostServices;
        if (host == null || IsOpenFileDialogOpen)
            return;

        int prevTimeout = Activity.Timeout;
        Activity.IsEnabled = false;
        Activity.Timeout = 0;
        IsOpenFileDialogOpen = true;

        string folder = null;

        // If there is currently an open file, set that folder as the base folder
        if (decoder.Playlist.Url != null && File.Exists(decoder.Playlist.Url))
            folder = Path.GetDirectoryName(decoder.Playlist.Url);

        Task<string> pick;
        try
        {
            pick = host.PickMediaFileAsync(folder);
        }
        catch (Exception e)
        {
            pick = Task.FromException<string>(e);
        }

        pick.ContinueWith(t =>
        {
            UI(() =>
            {
                Activity.Timeout = prevTimeout;
                Activity.IsEnabled = true;
                IsOpenFileDialogOpen = false;
            });

            if (t.IsFaulted)
                Log.Error($"Open file dialog failed ({t.Exception?.GetBaseException().Message})");
            else if (t.IsCompletedSuccessfully && !string.IsNullOrEmpty(t.Result))
                OpenAsync(t.Result);
        }, TaskScheduler.Default);
    }

    /// <summary>
    /// <para>Saves the current video frame (encoding based on file extention .bmp, .png, .jpg)</para>
    /// <para>If filename not specified will use Config.Player.FolderSnapshots and with default filename title_frameNumber.ext (ext from Config.Player.SnapshotFormat)</para>
    /// <para>If width/height not specified will use the original size. If one of them will be set, the other one will be set based on original ratio</para>
    /// </summary>
    public void TakeSnapshotToFile(string filename = null, uint width = 0, uint height = 0)
    {
        if (!CanPlay)
            return;

        if (filename == null)
        {
            try
            {
                if (!Directory.Exists(Config.Player.FolderSnapshots))
                    Directory.CreateDirectory(Config.Player.FolderSnapshots);

                // TBR: if frame is specified we don't know the frame's number
                filename = GetValidFileName(string.IsNullOrEmpty(Playlist.Selected.Title) ? "Snapshot" : Playlist.Selected.Title) + $"_{VideoDecoder.GetFrameNumber(CurTime).ToString()}.{Config.Player.SnapshotFormat}";
                filename = FindNextAvailableFile(Path.Combine(Config.Player.FolderSnapshots, filename));
            } catch { return; }
        }

        if (Renderer == null || !Renderer.TakeSnapshotToFile(filename, width, height))
            return;

        UI(() =>
        {
            OSDMessage = $"Save snapshot to {Path.GetFileName(filename)}";
        });
    }
}

/// <summary>Portable stand-in for WPF's <c>Keyboard</c> used by Player.KeyDown (modifier keys).</summary>
internal static class Keyboard
{
    internal static bool IsKeyDown(Key key) => Player.KeyStateProvider?.Invoke(key) ?? false;
}
