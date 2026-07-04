using System.Diagnostics;
using System.IO;
using System.Windows;
using FlyleafLib;
using LLPlayer.Extensions;
using LLPlayer.Services;

namespace LLPlayer.ViewModels;

/// <summary>
/// Shared scaffolding for the three model/engine download dialogs — Whisper model
/// (<see cref="WhisperModelDownloadDialogVM"/>), Tesseract (<see cref="TesseractDownloadDialogVM"/>) and the
/// Whisper engine (<see cref="WhisperEngineDownloadDialogVM"/>). These were ~90% copy-paste that had already
/// drifted: only Whisper disposed its <see cref="CancellationTokenSource"/> and only Whisper guarded its
/// open-folder command against a shell failure. Centralising the CTS lifecycle, the download error policy, the
/// stream pump, the guarded open-folder and the temp-file cleanup here fixes both latent bugs in one place and
/// keeps each concrete VM down to its model source, finalize step and UI labels (HC-41).
/// </summary>
public abstract class ModelDownloadDialogVMBase : Bindable, IDialogAware
{
    // 50 ms UI progress throttle — unchanged from the previous inline download loops.
    private static readonly TimeSpan ProgressThrottle = TimeSpan.FromMilliseconds(50);

    public FlyleafManager FL { get; }

    protected ModelDownloadDialogVMBase(FlyleafManager fl)
    {
        FL = fl;
    }

    public string StatusText { get; set => Set(ref field, value); } = "";

    public long DownloadedSize { get; set => Set(ref field, value); }

    public long TotalSize { get; set => Set(ref field, value); }

    public bool IsIndeterminateDownload { get; set => Set(ref field, value); } = true;

    // The active download's cancellation source. Only one download runs at a time (CanDownload is false while
    // the command executes), so a single field is sufficient.
    protected CancellationTokenSource? _cts;

    /// <summary>
    /// Runs one download attempt with the single, correct CTS lifecycle and error policy shared by every dialog.
    /// <paramref name="core"/> does the dialog-specific work (acquire source, pump, finalize) and must honour the
    /// token; <paramref name="cleanup"/> runs in the finally (e.g. delete the temp file); <paramref name="mapError"/>
    /// can turn a specific exception into a friendlier status (e.g. HTTP 404 → "not available"), returning null to
    /// fall back to the generic message.
    /// </summary>
    protected async Task RunDownloadAsync(
        Func<CancellationToken, Task> core,
        Action cleanup,
        Func<Exception, string?>? mapError = null)
    {
        _cts = new CancellationTokenSource();
        CancellationToken token = _cts.Token;

        try
        {
            await core(token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // The user pressed Cancel. A provider/HTTP timeout also surfaces as OperationCanceledException but
            // leaves the token un-cancelled, so it is NOT caught here — it falls through to the generic branch and
            // is reported as a failure instead of a misleading "Download canceled" (the bug the Whisper-engine and,
            // via a brittle message string-match, the Tesseract dialog previously handled inconsistently).
            StatusText = "Download canceled";
        }
        catch (Exception ex)
        {
            StatusText = mapError?.Invoke(ex) ?? $"Failed to download: {ex.Message}";
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            cleanup();
        }
    }

    protected void CancelActiveDownload() => _cts?.Cancel();

    /// <summary>
    /// Copies <paramref name="source"/> to <paramref name="destinationPath"/> (created/truncated), reporting
    /// progress into <see cref="DownloadedSize"/>. Returns the number of bytes written.
    /// </summary>
    protected async Task<long> DownloadToFileAsync(Stream source, string destinationPath, CancellationToken token)
    {
        DownloadedSize = 0;

        await using FileStream fileWriter = File.Open(destinationPath, FileMode.Create);
        return await StreamDownloadPump.CopyWithProgressAsync(
            source, fileWriter, bytes => DownloadedSize = bytes, ProgressThrottle, token);
    }

    /// <summary>
    /// Opens <paramref name="directory"/> in the shell, guarded so a shell/association failure (or the folder
    /// disappearing after the existence check) shows an error dialog instead of throwing an unhandled
    /// <see cref="System.ComponentModel.Win32Exception"/> on the UI thread.
    /// </summary>
    protected static void OpenFolderSafe(string directory)
    {
        if (!Directory.Exists(directory))
            return;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = directory,
                UseShellExecute = true,
                CreateNoWindow = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to open folder: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>Best-effort delete of a temp file; returns false only if the file exists and could not be deleted.</summary>
    protected static bool TryDeleteFile(string path)
    {
        if (!File.Exists(path))
            return true;

        try
        {
            File.Delete(path);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    #region IDialogAware
    public string Title { get; set => Set(ref field, value); } = App.Name;
    public double WindowWidth { get; set => Set(ref field, value); } = 400;
    public double WindowHeight { get; set => Set(ref field, value); } = 200;

    public abstract bool CanCloseDialog();
    public virtual void OnDialogClosed() { }
    public virtual void OnDialogOpened(IDialogParameters parameters) { }
    public DialogCloseListener RequestClose { get; }
    #endregion IDialogAware
}
