using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.Win32.SafeHandles;

namespace FlyleafLib.MediaPlayer.Dubbing;

#nullable enable

/// <summary>
/// Run-scoped owner of the local TTS Python sidecar: one long-lived process that loads the (multi-GB)
/// model ONCE and serves many per-line synth requests over localhost HTTP. Exactly one instance is
/// allowed process-wide (a static gate) so a batch run and a single-file render never double VRAM.
/// Built from an immutable <see cref="DubbingConfig"/> snapshot at run start; config changes require an
/// explicit restart. Orphan-safe: the child is placed in a kill-on-close Job Object (<see
/// cref="JobObjectGuard"/>) and is also told the parent PID so it can self-terminate.
/// </summary>
public sealed class DubSidecarHost : ITtsService
{
    private static readonly SemaphoreSlim _singleInstance = new(1, 1);

    private readonly DubbingSnapshot _cfg;
    private readonly bool _mock;

    private Process? _process;
    private SafeFileHandle? _job;
    private HttpClient? _http;
    private string? _workDir;
    private bool _singleHeld;

    private DubSidecarHost(DubbingSnapshot cfg, bool mock)
    {
        _cfg = cfg;
        _mock = mock;
    }

    /// <summary>Create a host from a config snapshot. Does not start the process.</summary>
    public static DubSidecarHost Create(DubbingConfig config, bool mock = false)
    {
        ArgumentNullException.ThrowIfNull(config);
        return new DubSidecarHost(DubbingSnapshot.From(config), mock);
    }

    internal static DubSidecarHost Create(DubbingSnapshot snapshot, bool mock = false)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new DubSidecarHost(snapshot, mock);
    }

    /// <summary>The localhost port the sidecar bound to (reported on its stdout as <c>DUB_PORT=</c>).</summary>
    public int Port { get; private set; }

    /// <summary>
    /// Start the sidecar, read its port, and wait until it reports ready. Returns false if it never
    /// becomes ready within the timeout. Throws if the engine/script is missing or the process dies.
    /// </summary>
    public async Task<bool> TryInitializeAsync(CancellationToken token)
    {
        if (!await _singleInstance.WaitAsync(TimeSpan.FromSeconds(30), token).ConfigureAwait(false))
            throw new InvalidOperationException("Another dubbing sidecar is already running.");
        _singleHeld = true;

        try
        {
            return await StartProcessAsync(token).ConfigureAwait(false);
        }
        catch
        {
            // Any failure after acquiring the lock: release it + reap a partially-started process/dir.
            await DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private async Task<bool> StartProcessAsync(CancellationToken token)
    {
        if (!File.Exists(_cfg.PythonPath))
            throw new FileNotFoundException("Dub engine Python was not found. Provision the dub engine first.", _cfg.PythonPath);
        if (!File.Exists(_cfg.ServerScript))
            throw new FileNotFoundException("Dub sidecar script was not found.", _cfg.ServerScript);

        // Per-run temp dir for the sidecar's synth clips; deleted in DisposeAsync so nothing leaks to %TEMP%.
        _workDir = Path.Combine(Path.GetTempPath(), "llp_dub_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workDir);

        ProcessStartInfo psi = new()
        {
            FileName = _cfg.PythonPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(_cfg.ServerScript) ?? AppDomain.CurrentDomain.BaseDirectory
        };
        psi.ArgumentList.Add(_cfg.ServerScript);
        psi.ArgumentList.Add("--port");
        psi.ArgumentList.Add("0");
        psi.ArgumentList.Add("--model");
        psi.ArgumentList.Add(_cfg.Model);
        psi.ArgumentList.Add("--models-dir");
        psi.ArgumentList.Add(_cfg.ModelsDir);
        psi.ArgumentList.Add("--parent-pid");
        psi.ArgumentList.Add(Environment.ProcessId.ToString());
        psi.ArgumentList.Add("--work-dir");
        psi.ArgumentList.Add(_workDir);
        if (_mock)
            psi.ArgumentList.Add("--mock");

        TaskCompletionSource<int> portReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
        StringBuilder stderr = new();

        _process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        _process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null)
                return;

            const string marker = "DUB_PORT=";
            int idx = e.Data.IndexOf(marker, StringComparison.Ordinal);
            if (idx >= 0 && int.TryParse(e.Data.AsSpan(idx + marker.Length).Trim(), out int p))
                portReady.TrySetResult(p);
        };
        _process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null)
                return;
            lock (stderr)
                stderr.AppendLine(e.Data);
        };

        if (!_process.Start())
            throw new InvalidOperationException("Failed to start the dubbing sidecar process.");

        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        if (OperatingSystem.IsWindows())
        {
            try { _job = JobObjectGuard.AssignKillOnClose(_process.Handle); }
            catch { _job = null; }
        }

        using (CancellationTokenSource portCts = CancellationTokenSource.CreateLinkedTokenSource(token))
        {
            portCts.CancelAfter(TimeSpan.FromSeconds(120));
            Task exited = _process.WaitForExitAsync(portCts.Token);
            Task completed = await Task.WhenAny(portReady.Task, exited).ConfigureAwait(false);
            if (completed == exited && !portReady.Task.IsCompleted)
            {
                // WaitForExitAsync completing does NOT prove the child died: a caller-cancel or the 120s
                // timeout cancels portCts, which completes `exited` while the process is still alive. Classify +
                // map so a cancel surfaces as OperationCanceled (job Canceled, not Failed), a timeout gets its
                // own message, and only a genuine early exit reports "exited before reporting a port".
                SidecarStartFault fault = ClassifyPortWaitFailure(token.IsCancellationRequested, _process.HasExited);
                if (fault == SidecarStartFault.Canceled)
                    token.ThrowIfCancellationRequested(); // the real, token-bound cancellation

                string detail;
                lock (stderr)
                    detail = stderr.ToString();
                throw BuildPortWaitException(fault, detail);
            }

            Port = await portReady.Task.ConfigureAwait(false);
        }

        _http = new HttpClient
        {
            BaseAddress = new Uri($"http://127.0.0.1:{Port}"),
            Timeout = TimeSpan.FromMinutes(10)
        };

        return await ProbeReadyAsync(token).ConfigureAwait(false);
    }

    /// <summary>Why the port wait ended without the sidecar reporting a port.</summary>
    internal enum SidecarStartFault
    {
        /// <summary>The caller's token was cancelled — surface as OperationCanceled (job Canceled).</summary>
        Canceled,
        /// <summary>The 120s port-report budget elapsed while the process was still alive.</summary>
        Timeout,
        /// <summary>The process genuinely exited before reporting a port.</summary>
        ExitedEarly
    }

    // Pure decision for the port-wait error branch (unit-tested). Precondition: portReady did NOT complete and
    // the exit-wait task completed — which happens on a real exit OR because a caller-cancel / the 120s timeout
    // cancelled the linked token. Caller-cancel wins (the user asked to stop); otherwise a still-alive process
    // means the 120s budget elapsed (timeout); only a dead process is a genuine early exit.
    internal static SidecarStartFault ClassifyPortWaitFailure(bool callerCanceled, bool processHasExited)
    {
        if (callerCanceled)
            return SidecarStartFault.Canceled;
        if (!processHasExited)
            return SidecarStartFault.Timeout;
        return SidecarStartFault.ExitedEarly;
    }

    // Map a classified fault to the exception the caller sees (unit-tested). This is the behavioural payload of
    // HC-30: a cancel must surface as an OperationCanceledException (job Canceled) and a timeout as a
    // TimeoutException, never as the InvalidOperationException("exited before…") the fix set out to eliminate.
    // Canceled maps to a plain OperationCanceledException as a fallback — the hot path throws the token-bound one
    // via ThrowIfCancellationRequested first, and this only runs if the token raced back to not-cancelled.
    internal static Exception BuildPortWaitException(SidecarStartFault fault, string stderrDetail) => fault switch
    {
        SidecarStartFault.Canceled => new OperationCanceledException(),
        SidecarStartFault.Timeout => new TimeoutException("Dubbing sidecar did not report a port within 120 seconds."),
        _ => new InvalidOperationException($"Dubbing sidecar exited before reporting a port.\n{stderrDetail}")
    };

    private async Task<bool> ProbeReadyAsync(CancellationToken token)
    {
        Stopwatch sw = Stopwatch.StartNew();
        TimeSpan budget = TimeSpan.FromSeconds(180);

        while (sw.Elapsed < budget)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                HealthDto? health = await _http!.GetFromJsonAsync<HealthDto>("/health", token).ConfigureAwait(false);
                if (health is { Ready: true })
                    return true;
            }
            catch (HttpRequestException) { /* not up yet */ }
            catch (TaskCanceledException) when (!token.IsCancellationRequested) { /* per-try timeout */ }

            await Task.Delay(500, token).ConfigureAwait(false);
        }

        return false;
    }

    /// <summary>Synthesize a single line. The sidecar returns a temp WAV path.</summary>
    public async Task<TtsResult> SynthesizeAsync(TtsRequest request, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (_http is null)
            throw new InvalidOperationException("Sidecar is not started.");

        SynthRequestDto dto = new(request.Text, request.VoiceId, request.TargetDurationMs);
        using HttpResponseMessage resp = await _http.PostAsJsonAsync("/synthesize", dto, token).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();

        SynthResponseDto? body = await resp.Content.ReadFromJsonAsync<SynthResponseDto>(token).ConfigureAwait(false);
        if (body is null || string.IsNullOrEmpty(body.WavPath))
            throw new InvalidOperationException("Sidecar returned an empty synthesis result.");

        return new TtsResult(body.WavPath, body.DurationMs);
    }

    /// <summary>List the preset voices the sidecar exposes (for the voice-bank / override UI).</summary>
    public async Task<IReadOnlyList<TtsVoice>> GetVoicesAsync(CancellationToken token)
    {
        if (_http is null)
            throw new InvalidOperationException("Sidecar is not started.");

        List<VoiceDto>? voices = await _http.GetFromJsonAsync<List<VoiceDto>>("/voices", token).ConfigureAwait(false);
        if (voices is null)
            return [];

        List<TtsVoice> result = new(voices.Count);
        foreach (VoiceDto v in voices)
            result.Add(new TtsVoice(v.Id, v.Name, v.Gender ?? "unknown", v.Language ?? "ru"));

        return result;
    }

    /// <summary>
    /// Assemble the final dub track: the sidecar decodes the original audio, time-stretches + places
    /// each clip on a silence bed, ducks the original under the dub, and encodes to the output path.
    /// </summary>
    public async Task AssembleAsync(AssembleRequest request, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (_http is null)
            throw new InvalidOperationException("Sidecar is not started.");

        AssembleRequestDto dto = new(
            request.MediaPath,
            request.OutputPath,
            request.OutputFormat,
            request.DuckingPercent,
            request.TotalMs,
            request.Clips.Select(c => new ClipDto(c.WavPath, c.StartMs, c.Atempo)).ToList());

        using HttpResponseMessage resp = await _http.PostAsJsonAsync("/assemble", dto, token).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
    }

    public async ValueTask DisposeAsync()
    {
        // Best-effort graceful shutdown first.
        if (_http is not null)
        {
            try
            {
                using CancellationTokenSource cts = new(TimeSpan.FromSeconds(3));
                using HttpRequestMessage req = new(HttpMethod.Post, "/shutdown");
                await _http.SendAsync(req, cts.Token).ConfigureAwait(false);
            }
            catch { /* ignore — we kill below */ }

            _http.Dispose();
            _http = null;
        }

        if (_process is not null)
        {
            try
            {
                if (!_process.HasExited)
                {
                    if (!_process.WaitForExit(2000))
                        _process.Kill(entireProcessTree: true);
                }
            }
            catch { /* already gone */ }

            _process.Dispose();
            _process = null;
        }

        // Closing the job handle reaps anything still assigned (belt-and-suspenders with Kill above).
        _job?.Dispose();
        _job = null;

        // Remove our per-run temp clips (we own this dir; the process is reaped above first).
        if (_workDir != null)
        {
            try { Directory.Delete(_workDir, recursive: true); }
            catch { /* temp dir — OS reclaims it if a handle lingers */ }
            _workDir = null;
        }

        if (_singleHeld)
        {
            _singleHeld = false;
            _singleInstance.Release();
        }
    }

    internal sealed record DubbingSnapshot(string PythonPath, string ServerScript, string Model, string ModelsDir)
    {
        public static DubbingSnapshot From(DubbingConfig c)
        {
            string python = c.UseManualEngine && !string.IsNullOrWhiteSpace(c.ManualVenvPython)
                ? c.ManualVenvPython!
                : Path.Combine(DubbingConfig.DefaultEngineDir, "Scripts", "python.exe");

            return new DubbingSnapshot(python, DubbingConfig.DefaultServerScript, c.Model, DubbingConfig.ModelsDirectory);
        }
    }

    private sealed record SynthRequestDto(
        [property: JsonPropertyName("text")] string Text,
        [property: JsonPropertyName("voice_id")] string VoiceId,
        [property: JsonPropertyName("target_duration_ms")] int TargetDurationMs);

    private sealed record SynthResponseDto(
        [property: JsonPropertyName("wav_path")] string WavPath,
        [property: JsonPropertyName("duration_ms")] int DurationMs);

    private sealed record HealthDto([property: JsonPropertyName("ready")] bool Ready);

    private sealed record VoiceDto(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("gender")] string? Gender,
        [property: JsonPropertyName("language")] string? Language);

    private sealed record ClipDto(
        [property: JsonPropertyName("wav_path")] string WavPath,
        [property: JsonPropertyName("start_ms")] double StartMs,
        [property: JsonPropertyName("atempo")] double Atempo);

    private sealed record AssembleRequestDto(
        [property: JsonPropertyName("media_path")] string MediaPath,
        [property: JsonPropertyName("output_path")] string OutputPath,
        [property: JsonPropertyName("output_format")] string OutputFormat,
        [property: JsonPropertyName("ducking_percent")] int DuckingPercent,
        [property: JsonPropertyName("total_ms")] double TotalMs,
        [property: JsonPropertyName("clips")] List<ClipDto> Clips);
}
