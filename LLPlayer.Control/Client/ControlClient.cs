using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LLPlayer.ControlApi;

public sealed class ControlClient : IPlayerControlService, IControlJobService, IDisposable
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(20);
    private static readonly JsonSerializerOptions BootstrapJson = new(ControlProtocol.Json)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip
    };
    private readonly HttpClient http;

    public ControlClient(ControlDiscovery discovery)
    {
        if (!Uri.TryCreate(discovery.Address, UriKind.Absolute, out var address) ||
            address.Scheme != Uri.UriSchemeHttp || address.Host != "127.0.0.1" ||
            address.UserInfo.Length != 0 || address.PathAndQuery != "/" || address.Fragment.Length != 0 ||
            string.IsNullOrWhiteSpace(discovery.Credential))
            throw new ArgumentException("The control host must use a local IPv4 loopback address.");
        http = new(new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false })
        {
            BaseAddress = address,
            Timeout = TimeSpan.FromSeconds(35)
        };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", discovery.Credential);
    }

    public static Task<ControlClient> ConnectOrStartAsync(
        string configDir, string stateDir, CancellationToken cancellationToken = default)
        => ConnectAsync(configDir, stateDir, startIfStopped: true, cancellationToken);

    public static Task<ControlClient> ConnectExistingAsync(
        string configDir, string stateDir, CancellationToken cancellationToken = default)
        => ConnectAsync(configDir, stateDir, startIfStopped: false, cancellationToken);

    private static async Task<ControlClient> ConnectAsync(
        string configDir, string stateDir, bool startIfStopped, CancellationToken cancellationToken)
    {
        var timer = Stopwatch.StartNew();
        bool started = false;
        while (timer.Elapsed < StartupTimeout)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await ReadDiscoveryAsync(cancellationToken).ConfigureAwait(false) is { } discovery)
            {
                ControlClient? client = null;
                bool matched = false;
                try
                {
                    client = new(discovery);
                    using var ping = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    var remaining = StartupTimeout - timer.Elapsed;
                    if (remaining <= TimeSpan.Zero)
                        break;
                    ping.CancelAfter(remaining < TimeSpan.FromSeconds(2) ? remaining : TimeSpan.FromSeconds(2));
                    using var response = await client.SendAsync(HttpMethod.Get, "v1/identity", null, ping.Token)
                        .ConfigureAwait(false);
                    var live = await response.Content.ReadFromJsonAsync<ControlHostIdentity>(BootstrapJson, ping.Token)
                        .ConfigureAwait(false);
                    if (live is not null && live.InstanceId == discovery.InstanceId && live.ProcessId == discovery.ProcessId)
                    {
                        if (discovery.ApiVersion != ControlProtocol.Version || live.ApiVersion != ControlProtocol.Version)
                            throw new ControlException(new("incompatible_version", "Stop the older control host explicitly."));
                        if (!ControlPaths.SameDirectory(discovery.ConfigDirectory, configDir) ||
                            !ControlPaths.SameDirectory(live.StateDirectory, stateDir))
                            throw new ControlException(new("profile_conflict", "The control host owns another profile."));
                        matched = true;
                        return client;
                    }
                }
                catch (HttpRequestException) { }
                catch (JsonException) { }
                catch (ArgumentException) { }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
                finally
                {
                    if (!matched)
                        client?.Dispose();
                }
            }

            if (!started)
            {
                if (!startIfStopped)
                    break;
                CheckRetainedState(stateDir);
                StartHost(configDir, stateDir);
                started = true;
            }
            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
        }
        throw new ControlException(new("host_unavailable", "The local control host did not become ready.", true));
    }

    private static void CheckRetainedState(string stateDirectory)
    {
        try
        {
            var path = Path.Combine(ControlPaths.UserConnectionDirectory, "control.lock");
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (stream.Length == 0)
                return;
            if (stream.Length <= 32_768 && JsonSerializer.Deserialize<string>(stream, BootstrapJson) is { } retained &&
                ControlPaths.SameDirectory(retained, stateDirectory))
                return;
        }
        catch (IOException)
        {
            // A concurrent owner holds the file. Its discovery identity will decide the profile.
            return;
        }
        catch (JsonException) { }
        throw new ControlException(new("cleanup_pending", "Recover the previous control state before switching profiles."));
    }

    public Task<ControlCapabilities> DescribeAsync(ControlScope scope, CancellationToken cancellationToken = default)
        => SendAsync<ControlCapabilities>(HttpMethod.Get, "v1/capabilities" + QueryScope(scope), null, cancellationToken);

    public Task<ControlState> GetStateAsync(ControlScope scope, CancellationToken cancellationToken = default)
        => SendAsync<ControlState>(HttpMethod.Get, "v1/state" + QueryScope(scope), null, cancellationToken);

    public Task<ControlResult> InvokeAsync(ControlRequest request, CancellationToken cancellationToken = default)
        => SendAsync<ControlResult>(HttpMethod.Post, "v1/operations", request, cancellationToken);

    public async Task<ControlJob?> GetAsync(ControlScope scope, Guid jobId, CancellationToken ct = default)
    {
        try
        {
            return await SendAsync<ControlJob>(HttpMethod.Get, $"v1/jobs/{jobId:D}" + QueryScope(scope), null, ct)
                .ConfigureAwait(false);
        }
        catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public Task<ControlJobPage> ListAsync(ControlScope scope, string? cursor = null, int limit = 50,
        CancellationToken ct = default)
        => SendAsync<ControlJobPage>(HttpMethod.Get, "v1/jobs" + QueryScope(scope) +
            $"&limit={limit}&cursor={Uri.EscapeDataString(cursor ?? string.Empty)}", null, ct);

    public Task<ControlResult> CancelAsync(ControlJobAction action, CancellationToken ct = default)
        => JobActionAsync("cancel", action, ct);

    public Task<ControlResult> RetryAsync(ControlJobAction action, CancellationToken ct = default)
        => JobActionAsync("retry", action, ct);

    public Task<ControlResult> ResumeAsync(ControlJobAction action, CancellationToken ct = default)
        => JobActionAsync("resume", action, ct);

    public async Task StopHostAsync(CancellationToken ct = default)
    {
        using var response = await SendAsync(HttpMethod.Post, "v1/stop", null, ct).ConfigureAwait(false);
    }

    public async Task RequestHostRestartAsync(CancellationToken ct = default)
    {
        using var response = await SendAsync(HttpMethod.Post, "v1/restart", null, ct).ConfigureAwait(false);
    }

    public async IAsyncEnumerable<(long Sequence, ControlJob Job)> ObserveJobsAsync(
        ControlScope scope, long after = 0, [EnumeratorCancellation] CancellationToken ct = default)
    {
        using var response = await SendAsync(HttpMethod.Get, "v1/events" + QueryScope(scope) +
            "&after=" + after.ToString(CultureInfo.InvariantCulture), null, ct).ConfigureAwait(false);
        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var reader = new StreamReader(stream);
        long sequence = 0;
        ControlJob? job = null;
        while (await reader.ReadLineAsync(ct).ConfigureAwait(false) is { } line)
        {
            if (line.Length == 0)
            {
                if (job is not null && sequence > after)
                {
                    after = sequence;
                    yield return (sequence, job);
                }
                job = null;
                sequence = 0;
            }
            else if (line.StartsWith("id: ", StringComparison.Ordinal))
            {
                long.TryParse(line.AsSpan(4), NumberStyles.None, CultureInfo.InvariantCulture, out sequence);
            }
            else if (line.StartsWith("data: ", StringComparison.Ordinal))
            {
                job = JsonSerializer.Deserialize<ControlJob>(line.AsSpan(6), ControlProtocol.Json);
            }
        }
    }

    public Task<ControlGuiSession> RegisterGuiAsync(ControlGuiRegistration registration, CancellationToken ct = default)
        => SendAsync<ControlGuiSession>(HttpMethod.Post, "v1/sessions", registration, ct);

    public async Task<ControlGuiCommand?> PollGuiAsync(Guid sessionId, ControlGuiPoll poll,
        CancellationToken ct = default)
    {
        using var response = await SendAsync(HttpMethod.Post, $"v1/sessions/{sessionId:D}/poll", poll, ct)
            .ConfigureAwait(false);
        return response.StatusCode == HttpStatusCode.NoContent ? null
            : await ReadAsync<ControlGuiCommand>(response, ct).ConfigureAwait(false);
    }

    public async Task CompleteGuiAsync(Guid sessionId, ControlGuiCompletion completion, CancellationToken ct = default)
    {
        using var response = await SendAsync(HttpMethod.Post, $"v1/sessions/{sessionId:D}/complete", completion, ct)
            .ConfigureAwait(false);
    }

    public async Task DetachGuiAsync(Guid sessionId, string ownerToken, CancellationToken ct = default)
    {
        using var response = await SendAsync(HttpMethod.Delete, $"v1/sessions/{sessionId:D}",
            new ControlGuiPoll(ownerToken), ct).ConfigureAwait(false);
    }

    public void Dispose() => http.Dispose();

    private Task<ControlResult> JobActionAsync(string actionName, ControlJobAction action, CancellationToken ct)
        => SendAsync<ControlResult>(HttpMethod.Post, $"v1/jobs/{action.JobId:D}/{actionName}", action, ct);

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var response = await SendAsync(method, path, body, ct).ConfigureAwait(false);
        return await ReadAsync<T>(response, ct).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
            request.Content = JsonContent.Create(body, body.GetType(), options: ControlProtocol.Json);
        var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (response.IsSuccessStatusCode)
            return response;
        var status = response.StatusCode;
        using (response)
        {
            if (status is HttpStatusCode.BadRequest or HttpStatusCode.Conflict &&
                response.Content.Headers.ContentType?.MediaType == "application/json")
            {
                await using var errorBody = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                var buffer = new byte[8192];
                var length = await errorBody.ReadAtLeastAsync(buffer, buffer.Length, false, ct)
                    .ConfigureAwait(false);
                if (length < buffer.Length && ParseError(buffer.AsSpan(0, length)) is { } error)
                    throw new ControlException(error);
            }
        }
        throw new HttpRequestException("The control host rejected the HTTP request.", null, status);
    }

    private static ControlError? ParseError(ReadOnlySpan<byte> data)
    {
        try
        {
            var error = JsonSerializer.Deserialize<ControlResult>(data, BootstrapJson)?.Error;
            return error is { Code: "invalid_arguments" or "profile_conflict" or "session_limit" or "cleanup_pending" or "host_stopping" }
                ? error : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken ct)
        => await response.Content.ReadFromJsonAsync<T>(ControlProtocol.Json, ct).ConfigureAwait(false)
            ?? throw new JsonException("The control host returned an empty response.");

    private static string QueryScope(ControlScope scope)
        => $"?profileId={Uri.EscapeDataString(scope.ProfileId)}&sessionId={scope.SessionId:D}";

    private static async Task<ControlDiscovery?> ReadDiscoveryAsync(CancellationToken ct)
    {
        try
        {
            var path = Path.Combine(ControlPaths.UserConnectionDirectory, "host.json");
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 4096, useAsync: true);
            return stream.Length <= 16_384
                ? await JsonSerializer.DeserializeAsync<ControlDiscovery>(stream, BootstrapJson, ct).ConfigureAwait(false)
                : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void StartHost(string configDir, string stateDir)
    {
        var overridePath = Environment.GetEnvironmentVariable("LLPLAYER_CONTROL_HOST");
        var executable = OperatingSystem.IsWindows() ? "LLPlayer.ControlHost.exe" : "LLPlayer.ControlHost";
        var baseDirectory = new DirectoryInfo(AppContext.BaseDirectory);
        var packageDirectory = baseDirectory.Name.Equals("mcp", StringComparison.OrdinalIgnoreCase)
            ? baseDirectory.Parent!.FullName : baseDirectory.FullName;
        var host = string.IsNullOrWhiteSpace(overridePath) ? Path.Combine(packageDirectory, executable) : Path.GetFullPath(overridePath);
        if (!File.Exists(host))
            throw new ControlException(new("host_missing", "The packaged LLPlayer.ControlHost executable was not found."));
        var psi = new ProcessStartInfo(host)
        {
            WorkingDirectory = Path.GetDirectoryName(host)!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        psi.ArgumentList.Add("--detach");
        psi.ArgumentList.Add("--config-dir");
        psi.ArgumentList.Add(Path.GetFullPath(configDir));
        psi.ArgumentList.Add("--state-dir");
        psi.ArgumentList.Add(Path.GetFullPath(stateDir));
        using var process = Process.Start(psi);
    }
}
