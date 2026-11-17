using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using FlyleafLib.MediaPlayer;
using FlyleafLib.Plugins;
using LLPlayer.ControlApi;
using LLPlayer.Core.Automation;

namespace FlyleafLibTests.Platform;

public sealed class ControlEngineProcessOwnershipTests
{
    [Fact]
    public async Task A_recovered_engine_boundary_blocks_new_work_until_its_processes_have_drained()
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(20));
        CancellationToken token = deadline.Token;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var directory = Directory.CreateTempSubdirectory("llplayer-live-engine-");
        try
        {
            var scope = new ControlScope("engine-proof");
            var store = new ControlJobStore(directory.FullName);
            var oldRequest = new ControlRequest(Guid.NewGuid(), "proof", scope, null, JsonSerializer.SerializeToElement(new { }));
            var oldState = new ControlJob(Guid.NewGuid(), scope, "proof", ControlJobStatus.Accepted, 1, DateTimeOffset.UtcNow);
            var old = store.Admit(oldState, oldRequest, RandomNumberGenerator.GetBytes(32), secret: false);
            var owner = new ProcessOwner(identity => store.Save(old, oldState with { Status = ControlJobStatus.Running },
                new ControlJobJournal { EngineProcesses = { [identity.Id] = identity } }));
            var info = ChildInfo();
            using var owned = EngineProcessOwnership.Enter(owner);
            var process = OnlineVideoProcess.StartEngine(info);
            try
            {
                using var stdout = new StreamReader(process.Output, leaveOpen: true);
                string? line = await stdout.ReadLineAsync(token);
                Assert.StartsWith("CHILD=", line);
                Assert.False(EngineProcessOwnership.IsDrained(Assert.IsType<EngineProcessIdentity>(owner.Identity)));

                await using var restarted = new ControlJobService(directory.FullName,
                    new Dictionary<string, ControlJobHandler>
                    {
                        ["proof"] = (_, _) =>
                        {
                            started.TrySetResult();
                            return Task.FromResult(new ControlJobOutcome(ControlJobStatus.Completed));
                        }
                    });
                Assert.True(restarted.HasPendingRecovery);
                Assert.Equal(ControlJobStatus.AwaitingCleanup, (await restarted.GetAsync(scope, oldState.JobId, token))?.Status);
                var accepted = await restarted.EnqueueAsync(oldRequest with { RequestId = Guid.NewGuid() }, token);
                Guid id = Assert.IsType<Guid>(accepted.JobId);
                while ((await restarted.GetAsync(scope, id, token))?.Status == ControlJobStatus.Accepted)
                    await Task.Delay(15, token);
                Assert.Equal(ControlJobStatus.WaitingForResource, (await restarted.GetAsync(scope, id, token))?.Status);
                Assert.False(started.Task.IsCompleted);

                await process.StopEngineAsync();
                await started.Task.WaitAsync(token);
                Assert.True(EngineProcessOwnership.IsDrained(Assert.IsType<EngineProcessIdentity>(owner.Identity)));
                Assert.False(restarted.HasPendingRecovery);
                Assert.Equal(ControlJobStatus.Interrupted, (await restarted.GetAsync(scope, oldState.JobId, token))?.Status);
            }
            finally { await process.DisposeEngineAsync(); }
        }
        finally { directory.Delete(recursive: true); }
    }

    private static ProcessStartInfo ChildInfo()
    {
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
            info.ArgumentList.Add("-NoProfile");
            info.ArgumentList.Add("-Command");
            info.ArgumentList.Add("""
                $spec = New-Object System.Diagnostics.ProcessStartInfo
                $spec.FileName = [Environment]::SystemDirectory + '\WindowsPowerShell\v1.0\powershell.exe'
                $spec.Arguments = '-NoProfile -Command Start-Sleep -Seconds 30'
                $spec.UseShellExecute = $false
                $spec.CreateNoWindow = $true
                $child = [System.Diagnostics.Process]::Start($spec)
                [Console]::WriteLine('CHILD=' + $child.Id)
                Start-Sleep -Seconds 30
                """);
        }
        else
        {
            info.FileName = "/bin/sh";
            info.ArgumentList.Add("-c");
            info.ArgumentList.Add("sleep 30 & printf 'CHILD=%s\n' \"$!\"; wait");
        }
        return info;
    }

    private sealed class ProcessOwner(Action<EngineProcessIdentity> save) : IEngineProcessOwner
    {
        internal EngineProcessIdentity? Identity { get; private set; }

        public void Record(EngineProcessIdentity process)
        {
            save(process);
            Identity = process;
        }

        public void Drained(Guid id) { }
    }

}
