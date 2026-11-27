#nullable enable

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using CliWrap.Builders;
using CliWrap.EventStream;
using FlyleafLib.MediaPlayer;
using FlyleafLib.MediaPlayer.Dubbing;

namespace FlyleafLib.Plugins;

public sealed partial class OnlineVideoProcess
{
    private StreamWriter? engineControl;
    private string? engineExitPrefix;
    private IEngineProcessOwner? engineOwner;
    private EngineProcessIdentity? engineIdentity;
    private readonly SemaphoreSlim engineStopGate = new(1, 1);

    // The session leader remains alive until it signals its own group, so the host does not signal a
    // potentially reused PID after the leader exits. EOF on the control pipe also covers app death.
    private const string EngineSupervisor = """
        status_prefix=$1
        admission=$2
        shift 2
        trap 'kill -KILL "-$$"' TERM HUP INT
        if [ "$admission" = wait ]; then
            IFS= read -r request || exit 125
            [ "$request" = start ] || exit 125
        fi
        exec 3<&0
        "$@" </dev/null 3<&- &
        worker=$!
        (exec 1>&- 2>&-; IFS= read -r request <&3; kill -TERM "$$") &
        wait "$worker"
        status=$?
        printf '\n%s%d\n' "$status_prefix" "$status" >&2
        kill -KILL "-$$"
        """;

    internal static OnlineVideoProcess StartEngine(ProcessStartInfo info,
        ProcessPriorityClass priority = ProcessPriorityClass.Normal)
    {
        if (!IsExecutablePath(info.FileName) || info.UseShellExecute)
            throw new ArgumentException("An absolute engine executable is required.");
        if (OperatingSystem.IsWindows())
            return StartWindows(info, ownedEngine: true, priority);
        if (!OperatingSystem.IsLinux())
            throw new PlatformNotSupportedException();
        if (!IsExecutablePath(LinuxSessionLauncher) || !IsExecutablePath("/bin/sh"))
            throw new FileNotFoundException("GPU engine coordination requires /usr/bin/setsid and /bin/sh.");

        string prefix = "LLPLAYER_ENGINE_EXIT_" + Guid.NewGuid().ToString("N") + "=";
        IEngineProcessOwner? owner = EngineProcessOwnership.Current;
        ArgumentsBuilder args = new();
        args.Add("--").Add("/bin/sh").Add("-c").Add(EngineSupervisor).Add("llplayer-engine").Add(prefix)
            .Add(owner is null ? "immediate" : "wait");
        if (priority != ProcessPriorityClass.Normal)
        {
            string priorityValue = priority switch
            {
                ProcessPriorityClass.Idle => "19",
                ProcessPriorityClass.BelowNormal => "10",
                ProcessPriorityClass.AboveNormal => "-5",
                ProcessPriorityClass.High => "-10",
                ProcessPriorityClass.RealTime => "-20",
                _ => throw new ArgumentOutOfRangeException(nameof(priority))
            };
            args.Add("/usr/bin/nice").Add("-n").Add(priorityValue).Add("--");
        }
        args.Add(info.FileName);
        foreach (string arg in info.ArgumentList)
            args.Add(arg);
        string originalArguments = info.Arguments;
        info.ArgumentList.Clear();
        info.Arguments = originalArguments.Length == 0 ? args.Build() : args.Build() + " " + originalArguments;
        info.FileName = LinuxSessionLauncher;
        info.RedirectStandardInput = true;

        Process process = Process.Start(info) ?? throw new IOException("Could not launch the contained engine.");
        var engine = new OnlineVideoProcess(process, process.StandardOutput.BaseStream, process.StandardError.BaseStream)
        {
            engineControl = process.StandardInput,
            engineExitPrefix = prefix
        };
        if (owner is not null)
        {
            try
            {
                var identity = EngineProcessOwnership.IdentifyLinuxProcess(process.Id);
                owner.Record(identity);
                engine.engineOwner = owner;
                engine.engineIdentity = identity;
                // The supervisor waits before launching any workers. Loss of stdin during startup
                // is safe both before and after the identity transaction commits.
                engine.engineControl.WriteLine("start");
                engine.engineControl.Flush();
            }
            catch
            {
                engine.engineControl.Dispose();
                engine.Output.Dispose();
                engine.Error.Dispose();
                process.Dispose();
                throw;
            }
        }
        return engine;
    }

    internal Task WaitForEngineRootAsync(CancellationToken token) => root.WaitForExitAsync(token);
    internal bool EngineRootExited => root.HasExited;
    internal bool TryReadEngineExitCode(string line, out int exitCode)
    {
        exitCode = 0;
        return engineExitPrefix is not null && line.StartsWith(engineExitPrefix, StringComparison.Ordinal) &&
            int.TryParse(line.AsSpan(engineExitPrefix.Length), out exitCode);
    }
    internal bool ReportsEngineExit => engineExitPrefix is not null;

    internal async IAsyncEnumerable<CommandEvent> ReadEngineEventsAsync(
        [EnumeratorCancellation] CancellationToken token)
    {
        using var read = CancellationTokenSource.CreateLinkedTokenSource(token);
        var channel = Channel.CreateBounded<CommandEvent>(64);
        int? exited = null;
        Task stdout = ReadAsync(Output, isError: false);
        Task stderr = ReadAsync(Error, isError: true);
        Task done = CompleteAsync();
        try
        {
            await foreach (CommandEvent item in channel.Reader.ReadAllAsync(read.Token).ConfigureAwait(false))
                yield return item;

            int code = ReportsEngineExit
                ? exited ?? throw new IOException("The engine supervisor did not report a worker exit code.")
                : root.ExitCode;
            yield return new ExitedCommandEvent(code);
        }
        finally
        {
            await read.CancelAsync().ConfigureAwait(false);
            // Closing the owned processes breaks a pending Windows anonymous pipe read.
            await StopEngineAsync().ConfigureAwait(false);
            await done.ConfigureAwait(false);
        }

        async Task ReadAsync(Stream stream, bool isError)
        {
            using var reader = new StreamReader(stream, Encoding.Default, leaveOpen: true);
            while (await reader.ReadLineAsync(read.Token).ConfigureAwait(false) is string line)
            {
                if (isError && TryReadEngineExitCode(line, out int code))
                    exited = code;
                else
                    await channel.Writer.WriteAsync(isError ? new StandardErrorCommandEvent(line) :
                        new StandardOutputCommandEvent(line), read.Token).ConfigureAwait(false);
            }
        }

        async Task CompleteAsync()
        {
            try
            {
                await root.WaitForExitAsync(read.Token).ConfigureAwait(false);
                // A surviving child can inherit stdout/stderr and prevent EOF after the root exits.
                await StopEngineAsync().ConfigureAwait(false);
                await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
                channel.Writer.TryComplete();
            }
            catch (Exception ex) { channel.Writer.TryComplete(ex); }
        }
    }

    internal async ValueTask StopEngineAsync()
    {
        await engineStopGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (stopped)
                return;

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    if (!JobObjectGuard.IsEmpty(windowsJob))
                        JobObjectGuard.Terminate(windowsJob);
                    while (!JobObjectGuard.IsEmpty(windowsJob))
                        await Task.Delay(25, timeout.Token).ConfigureAwait(false);
                }
                else
                {
                    if (engineControl is not null)
                    {
                        engineControl.Dispose();
                        engineControl = null;
                    }
                    await root.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                    while (EngineProcessOwnership.LinuxSessionHasLiveMembers(root.Id))
                        await Task.Delay(25, timeout.Token).ConfigureAwait(false);
                }

                await root.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                if (engineIdentity is { } identity)
                    engineOwner?.Drained(identity.Id);
                stopped = true;
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                throw new TimeoutException("The engine process boundary is still occupied.");
            }
        }
        finally { engineStopGate.Release(); }
    }

    internal async ValueTask DisposeEngineAsync()
    {
        await StopEngineAsync().ConfigureAwait(false);
        windowsJob?.Dispose();
        Output.Dispose();
        Error.Dispose();
        root.Dispose();
    }

}
