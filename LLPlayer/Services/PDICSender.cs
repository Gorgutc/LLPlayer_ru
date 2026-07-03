using System.Diagnostics;
using System.IO;
using System.Text;

namespace LLPlayer.Services;

public class PipeClient : IDisposable
{
    private Process _proc;

    public PipeClient(string pipePath)
    {
        _proc = new Process
        {
            StartInfo = new ProcessStartInfo()
            {
                RedirectStandardOutput = true,
                RedirectStandardInput = true,
                RedirectStandardError = true,
                FileName = pipePath,
                CreateNoWindow = true,
            }
        };
        _proc.Start();
    }

    public async Task SendMessage(string message)
    {
        Debug.WriteLine(message);
        //byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(message);

        // Enclose double quotes before and after since it is sent as a JSON string
        byte[] bytes = Encoding.UTF8.GetBytes('"' + message + '"');

        var length = BitConverter.GetBytes(bytes.Length);
        // ConfigureAwait(false): PDICSender.Dispose() blocks on this via .Wait() from App.OnExit (UI thread);
        // keep the write continuations off the captured dispatcher context so .Wait() cannot deadlock.
        await _proc.StandardInput.BaseStream.WriteAsync(length, 0, length.Length).ConfigureAwait(false);
        await _proc.StandardInput.BaseStream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
        await _proc.StandardInput.BaseStream.FlushAsync().ConfigureAwait(false);
    }

    public void Dispose()
    {
        _proc.Kill();
        _proc.WaitForExit();
    }
}

public class PDICSender : IDisposable
{
    private readonly PipeClient _pipeClient;
    public FlyleafManager FL { get; }

    // HC-23: the live singleton instance if it was ever resolved (else null). App.OnExit disposes THIS directly,
    // because Prism/DryIoc does not dispose the DI container at shutdown; tracking the instance lets exit close the
    // pipe exactly once WITHOUT a bare Container.Resolve that would spawn a pipe just to kill it when PDIC was unused.
    public static PDICSender? Current { get; private set; }

    public PDICSender(FlyleafManager fl)
    {
        FL = fl;

        string? exePath = FL.Config.Subs.PDICPipeExecutablePath;

        if (!File.Exists(exePath))
        {
            throw new FileNotFoundException($"PDIC executable is not set correctly: {exePath}");
        }

        _pipeClient = new PipeClient(exePath);
        Current = this;
    }

    public void Dispose()
    {
        // HC-23: synchronous, bounded, robust disposal (was async void — could crash the process on a faulted
        // send and might not complete before shutdown). Invoked once from App.OnExit. Best-effort tell PDIC to
        // close its popup window, then always kill the pipe process. PipeClient.SendMessage uses
        // ConfigureAwait(false) so the bounded .Wait() below cannot deadlock the UI thread at exit.
        Current = null;
        try
        {
            _pipeClient.SendMessage("p:Dictionary,Close,").Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
            // ignore: the pipe may already be gone; the process is killed below regardless
        }
        finally
        {
            try { _pipeClient.Dispose(); } catch { /* process may have already exited */ }
        }
    }

    // Send the same way as Firepop
    // webextension native extensions
    // ref: https://developer.mozilla.org/en-US/docs/Mozilla/Add-ons/WebExtensions/Native_messaging

    // See Firepop source
    public async Task<int> SendWithPipe(string sentence, int offset)
    {
        try
        {
            await _pipeClient.SendMessage($"p:Dictionary,SetUrl,{App.Name}");
            await _pipeClient.SendMessage($"p:Dictionary,PopupSearch3,{offset},{sentence}");

            // Incremental search
            //await _pipeClient.SendMessage($"p:Simulate,InputWord3,word");

            return 0;
        }
        catch (Exception e)
        {
            Debug.WriteLine(e.ToString());
            return -1;
        }
    }
}
