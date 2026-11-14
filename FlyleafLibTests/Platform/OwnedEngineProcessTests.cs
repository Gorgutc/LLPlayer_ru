using System.Diagnostics;
using CliWrap.EventStream;
using FlyleafLib.Plugins;

namespace FlyleafLibTests.Platform;

public sealed class OwnedEngineProcessTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Confirmed_stop_waits_for_descendants(bool cancel)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
        var info = new ProcessStartInfo
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        if (OperatingSystem.IsWindows())
        {
            info.FileName = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
            var script = """
                $spec = New-Object System.Diagnostics.ProcessStartInfo
                $spec.FileName = [Environment]::SystemDirectory + '\WindowsPowerShell\v1.0\powershell.exe'
                $spec.Arguments = '-NoProfile -Command Start-Sleep -Seconds 30'
                $spec.UseShellExecute = $false
                $spec.CreateNoWindow = $true
                $child = [System.Diagnostics.Process]::Start($spec)
                [Console]::WriteLine('CHILD=' + $child.Id)
                """;
            if (cancel) script += "\nStart-Sleep -Seconds 30";
            info.ArgumentList.Add("-NoProfile");
            info.ArgumentList.Add("-Command");
            info.ArgumentList.Add(script);
        }
        else
        {
            info.FileName = "/bin/sh";
            info.ArgumentList.Add("-c");
            string script = "sleep 30 & printf 'CHILD=%s\\n' \"$!\"";
            if (cancel) script += "; sleep 30";
            info.ArgumentList.Add(script);
        }

        var engine = OnlineVideoProcess.StartEngine(info);
        int? descendant = null;
        int? exitCode = null;
        async Task ReadAsync()
        {
            await foreach (CommandEvent item in engine.ReadEngineEventsAsync(cancellation.Token))
            {
                if (item is StandardOutputCommandEvent stdout && stdout.Text.StartsWith("CHILD=", StringComparison.Ordinal))
                {
                    descendant = int.Parse(stdout.Text.AsSpan(6));
                    if (cancel) cancellation.Cancel();
                }
                if (item is ExitedCommandEvent exited)
                    exitCode = exited.ExitCode;
            }
        }

        try
        {
            if (cancel)
                await Assert.ThrowsAnyAsync<OperationCanceledException>(ReadAsync);
            else
            {
                await ReadAsync();
                Assert.Equal(0, exitCode);
            }
            Assert.False(deadline.IsCancellationRequested);
            Assert.NotNull(descendant);
            Assert.False(IsLive(descendant.Value));
        }
        finally { await engine.DisposeEngineAsync(); }
    }

    private static bool IsLive(int pid)
    {
        if (OperatingSystem.IsLinux())
        {
            string statPath = $"/proc/{pid}/stat";
            try
            {
                string stat = File.ReadAllText(statPath);
                return stat[stat.LastIndexOf(')') + 2] is not ('Z' or 'X');
            }
            catch (IOException) when (!File.Exists(statPath)) { return false; }
        }
        try
        {
            using Process process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException) { return false; }
    }
}
