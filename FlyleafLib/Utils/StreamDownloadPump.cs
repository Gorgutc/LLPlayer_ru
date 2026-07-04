using System.Diagnostics;
using System.IO;

namespace FlyleafLib;

#nullable enable

/// <summary>
/// UI-agnostic copy loop shared by the model/engine download dialogs (HC-41): reads <paramref name="source"/>
/// into <paramref name="destination"/> in 128&#160;KB chunks, honours <paramref name="token"/>, and reports the
/// running byte total to <paramref name="onProgress"/> no more often than <paramref name="progressThrottle"/>.
/// This is the single largest block that used to be copy-pasted (byte-for-byte) across all three download VMs;
/// extracting it here lets the WPF-bound view-models keep only their source acquisition and finalize steps, and
/// makes the copy behaviour unit-testable (the VMs live in LLPlayer, which has no test project).
/// </summary>
public static class StreamDownloadPump
{
    private const int BufferSize = 1024 * 128;

    /// <summary>
    /// Copies every byte of <paramref name="source"/> to <paramref name="destination"/>, returning the total
    /// number of bytes copied. Progress is reported through <paramref name="onProgress"/> at most once per
    /// <paramref name="progressThrottle"/> (pass <see cref="TimeSpan.Zero"/> or a negative value to report after
    /// every chunk). Cancellation throws <see cref="OperationCanceledException"/>, exactly as the previous inline
    /// loops did via <c>token.ThrowIfCancellationRequested()</c>.
    /// </summary>
    public static async Task<long> CopyWithProgressAsync(
        Stream source,
        Stream destination,
        Action<long>? onProgress,
        TimeSpan progressThrottle,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);

        byte[] buffer = new byte[BufferSize];
        long totalBytesRead = 0;

        // NOTE: intentionally no ConfigureAwait(false). The WPF download VMs call this without ConfigureAwait, so
        // the continuations (and thus onProgress → DownloadedSize) resume on the caller's SynchronizationContext
        // (the UI thread), exactly as the previous inline loops did. Adding ConfigureAwait(false) here would push
        // the progress callback onto a thread-pool thread.
        Stopwatch sw = Stopwatch.StartNew();

        int bytesRead;
        while ((bytesRead = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), token)) > 0)
        {
            await destination.WriteAsync(buffer.AsMemory(0, bytesRead), token);
            totalBytesRead += bytesRead;

            if (progressThrottle <= TimeSpan.Zero || sw.Elapsed > progressThrottle)
            {
                onProgress?.Invoke(totalBytesRead);
                sw.Restart();
            }

            token.ThrowIfCancellationRequested();
        }

        return totalBytesRead;
    }
}
