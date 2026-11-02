using System.Buffers.Binary;
using System.Diagnostics;
using System.Text.Json;

namespace LLPlayer.Avalonia.Services;

/// <summary>An owned bridge process. Implementations must honor cancellation of pipe writes.</summary>
public interface IPdicProcess : IAsyncDisposable
{
    Task WriteAsync(ReadOnlyMemory<byte> frame, CancellationToken cancellationToken);
}
public interface IPdicProcessFactory
{
    Task<IPdicProcess> StartAsync(string executablePath, CancellationToken cancellationToken);
}

/// <summary>Application-owned, lazy PDIC bridge. All sends and shutdown serialize through one gate.</summary>
public sealed class PdicClient(Func<string?> executablePath, IPdicProcessFactory? factory = null, bool? isWindows = null) : IAsyncDisposable
{
    readonly IPdicProcessFactory processes = factory ?? new WindowsPdicProcessFactory();
    readonly SemaphoreSlim gate = new(1);
    readonly CancellationTokenSource lifetime = new();
    readonly object admission = new();
    IPdicProcess? process;
    string? activePath;
    volatile bool stopping;
    Task? shutdown;
    public bool IsAvailable { get; } = isWindows ?? OperatingSystem.IsWindows();

    public async Task SendAsync(string sentence, int zeroBasedOffset, CancellationToken cancellationToken = default)
    {
        if (!IsAvailable) throw new PlatformNotSupportedException("PDIC is available only on Windows. Choose Translation or a clipboard action on Linux.");
        if (string.IsNullOrWhiteSpace(sentence) || zeroBasedOffset < 0 || zeroBasedOffset >= sentence.Length
            || char.IsLowSurrogate(sentence[zeroBasedOffset]))
            throw new ArgumentException("PDIC needs the exact selected word position. Select the word again.");
        if (sentence.Length > 128 * 1024) throw new ArgumentException("The selected subtitle is too long for PDIC.");
        byte[] origin = Frame("p:Dictionary,SetUrl,LLPlayer");
        byte[] lookup = Frame($"p:Dictionary,PopupSearch3,{zeroBasedOffset + 1},{sentence}");
        CancellationTokenSource operation;
        lock (admission)
        {
            ObjectDisposedException.ThrowIf(stopping, this);
            operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token);
        }
        using (operation)
        {
            operation.CancelAfter(TimeSpan.FromSeconds(5));
            await gate.WaitAsync(operation.Token).ConfigureAwait(false);
            try
            {
                string path = executablePath() ?? "";
                new PdicSettings { ExecutablePath = path }.Validate();
                if (path.Length == 0) throw new InvalidOperationException("Set the pdic-pipe.exe path in PDIC settings before using this action.");
                if (process != null && !string.Equals(activePath, path, StringComparison.OrdinalIgnoreCase))
                    await ReleaseProcessAsync().ConfigureAwait(false);
                process ??= await processes.StartAsync(path, operation.Token).ConfigureAwait(false);
                activePath = path;
                operation.Token.ThrowIfCancellationRequested();
                await process.WriteAsync(origin, operation.Token).ConfigureAwait(false);
                await process.WriteAsync(lookup, operation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException error) when (!cancellationToken.IsCancellationRequested && !lifetime.IsCancellationRequested)
            {
                await ReleaseProcessAsync().ConfigureAwait(false);
                throw new IOException("The PDIC bridge did not accept this lookup in time. Check PDIC settings and select the word again.", error);
            }
            catch
            {
                // Shutdown owns the final Close message after the canceled writer releases the gate.
                if (!stopping) await ReleaseProcessAsync().ConfigureAwait(false);
                throw;
            }
            finally { gate.Release(); }
        }
    }

    internal static byte[] Frame(string message)
    {
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(message);
        if (json.Length > 1024 * 1024) throw new ArgumentException("The selected subtitle is too long for PDIC.");
        byte[] frame = new byte[json.Length + sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(frame, json.Length);
        json.CopyTo(frame, sizeof(int));
        return frame;
    }

    async Task ReleaseProcessAsync()
    {
        IPdicProcess? owned = process;
        process = null; activePath = null;
        if (owned != null) await owned.DisposeAsync().ConfigureAwait(false);
    }

    public ValueTask DisposeAsync()
    {
        lock (admission)
        {
            if (shutdown != null) return new(shutdown);
            stopping = true;
            lifetime.Cancel();
            return new(shutdown = StopAsync());
        }
    }
    async Task StopAsync()
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (process != null)
            {
                using CancellationTokenSource close = new(TimeSpan.FromSeconds(2));
                try { await process.WriteAsync(Frame("p:Dictionary,Close,"), close.Token).ConfigureAwait(false); }
                catch (Exception) { /* The existing bridge is always released, even after a failed close message. */ }
                await ReleaseProcessAsync().ConfigureAwait(false);
            }
        }
        finally { gate.Release(); }
    }
}

sealed class WindowsPdicProcessFactory : IPdicProcessFactory
{
    public Task<IPdicProcess> StartAsync(string executablePath, CancellationToken cancellationToken) => Task.Run<IPdicProcess>(() =>
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("PDIC is available only on Windows.");
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(executablePath)) throw new FileNotFoundException("The configured pdic-pipe.exe was not found. Correct its path in PDIC settings.");
        Process process = new()
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = executablePath, WorkingDirectory = Path.GetDirectoryName(executablePath)!,
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            }
        };
        try
        {
            if (!process.Start()) throw new IOException("The PDIC bridge could not start.");
            cancellationToken.ThrowIfCancellationRequested();
            return new PdicProcess(process);
        }
        catch
        {
            try { if (!process.HasExited) process.Kill(); } catch (Exception) { }
            process.Dispose(); throw;
        }
    }, cancellationToken);

    sealed class PdicProcess : IPdicProcess
    {
        readonly Process process;
        readonly CancellationTokenSource drains = new();
        readonly Task stdout, stderr;
        bool disposed;
        public PdicProcess(Process process)
        {
            this.process = process;
            stdout = DrainAsync(process.StandardOutput.BaseStream);
            stderr = DrainAsync(process.StandardError.BaseStream);
        }
        async Task DrainAsync(Stream stream)
        {
            try { await stream.CopyToAsync(Stream.Null, drains.Token).ConfigureAwait(false); }
            catch (Exception error) when (error is IOException or OperationCanceledException or ObjectDisposedException) { }
        }
        public async Task WriteAsync(ReadOnlyMemory<byte> frame, CancellationToken cancellationToken)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (process.HasExited) throw new IOException("The PDIC bridge exited. Select the word again to reconnect.");
            await process.StandardInput.BaseStream.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
            await process.StandardInput.BaseStream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        public async ValueTask DisposeAsync()
        {
            if (disposed) return;
            disposed = true;
            try
            {
                if (!process.HasExited) process.Kill();
                using CancellationTokenSource wait = new(TimeSpan.FromSeconds(2));
                await process.WaitForExitAsync(wait.Token).ConfigureAwait(false);
            }
            catch (Exception error) when (error is InvalidOperationException or OperationCanceledException or System.ComponentModel.Win32Exception) { }
            finally
            {
                drains.Cancel();
                process.Dispose();
                await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
                drains.Dispose();
            }
        }
    }
}
