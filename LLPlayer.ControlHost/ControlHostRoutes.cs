using System.Globalization;
using System.Net;
using System.Text.Json;
using LLPlayer.ControlApi;
using LLPlayer.Core.Automation;
using Microsoft.Extensions.Primitives;

namespace LLPlayer.ControlHost;

internal static class ControlHostRoutes
{
    public static void Map(WebApplication app, ControlOperationCatalog catalog, ControlJobService jobs,
        ControlGuiBroker broker, Guid instanceId, string stateDirectory, string credential, Action requestRestart)
    {
        var authorization = "Bearer " + credential;
        app.Use(async (http, next) =>
        {
            http.Response.Headers.CacheControl = "no-store";
            if (http.Connection.RemoteIpAddress is not { } peer || !IPAddress.IsLoopback(peer) ||
                http.Request.Host.Host != "127.0.0.1" || http.Request.Host.Port != http.Connection.LocalPort ||
                http.Request.Headers.ContainsKey("Origin"))
            {
                http.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }
            if (!ControlHostDiscovery.MatchesCredential(authorization, http.Request.Headers.Authorization.ToString()))
            {
                http.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }
            try
            {
                await next(http);
            }
            catch (ControlException exception) when (!http.Response.HasStarted)
            {
                http.Response.StatusCode = exception.Error.Code switch
                {
                    "invalid_session" => StatusCodes.Status404NotFound,
                    "invalid_arguments" => StatusCodes.Status400BadRequest,
                    _ => StatusCodes.Status409Conflict
                };
                await http.Response.WriteAsJsonAsync(new ControlResult(Error: exception.Error), ControlProtocol.Json);
            }
        });

        var v1 = app.MapGroup("/v1");
        v1.MapGet("/identity", () => new ControlHostIdentity(ControlProtocol.Version, instanceId,
            Environment.ProcessId, stateDirectory));
        v1.MapGet("/capabilities", (HttpRequest http) => catalog.Describe(ReadScope(http)));
        v1.MapGet("/state", async (HttpRequest http, CancellationToken ct) =>
        {
            var scope = ReadScope(http);
            var page = await jobs.ListAsync(scope, ct: ct);
            var active = await jobs.ListActiveAsync(scope, ct);
            var visible = active.Concat(page.Items).DistinctBy(job => job.JobId).ToArray();
            return new ControlState(instanceId, scope.ProfileId, broker.List(), visible);
        });
        v1.MapPost("/operations", async (ControlRequest request, CancellationToken ct) =>
        {
            RequireProfile(request.Scope);
            return await catalog.InvokeAsync(request, ct);
        });
        v1.MapGet("/jobs", async (HttpRequest http, CancellationToken ct) =>
        {
            var scope = ReadScope(http);
            var cursor = ReadQuery(http, "cursor");
            var limit = ReadLimit(http);
            try
            {
                return await jobs.ListAsync(scope, cursor, limit, ct);
            }
            catch (ArgumentException)
            {
                throw new ControlException(new("invalid_arguments", "The job cursor is invalid."));
            }
        });
        v1.MapGet("/jobs/{jobId:guid}", async (Guid jobId, HttpRequest http, CancellationToken ct) =>
        {
            var job = await jobs.GetAsync(ReadScope(http), jobId, ct);
            return job is null ? Results.NotFound() : Results.Json(job, ControlProtocol.Json);
        });
        v1.MapPost("/jobs/{jobId:guid}/{verb}", async (
            Guid jobId, string verb, ControlJobAction action, CancellationToken ct) =>
        {
            RequireProfile(action.Scope);
            if (jobId != action.JobId)
                return ControlResult.Fail("invalid_arguments", "The route and body identify different jobs.");
            return verb switch
            {
                "cancel" => await jobs.CancelAsync(action, ct),
                "retry" => await jobs.RetryAsync(action, ct),
                "resume" => await jobs.ResumeAsync(action, ct),
                _ => ControlResult.Fail("unknown_operation", "Unknown job action.")
            };
        });
        v1.MapGet("/events", (HttpContext context, CancellationToken ct) => StreamEventsAsync(context, jobs, ct));

        v1.MapPost("/sessions", (ControlGuiRegistration registration) => broker.Register(registration));
        v1.MapPost("/sessions/{sessionId:guid}/poll", async (Guid sessionId, ControlGuiPoll poll, CancellationToken ct) =>
        {
            var command = await broker.PollAsync(sessionId, poll, ct);
            return command is null ? Results.NoContent() : Results.Json(command, ControlProtocol.Json);
        });
        v1.MapPost("/sessions/{sessionId:guid}/complete", (Guid sessionId, ControlGuiCompletion completion) =>
        {
            broker.Complete(sessionId, completion);
            return Results.NoContent();
        });
        v1.MapDelete("/sessions/{sessionId:guid}", async (Guid sessionId, HttpRequest request, CancellationToken ct) =>
        {
            var poll = await request.ReadFromJsonAsync<ControlGuiPoll>(ControlProtocol.Json, ct);
            if (poll is null)
                return Results.BadRequest();
            broker.Detach(sessionId, poll.OwnerToken);
            return Results.NoContent();
        });
        var shutdownRequested = 0;
        async Task<IResult> StopAsync(HttpResponse response, bool restart)
        {
            if (Interlocked.CompareExchange(ref shutdownRequested, 1, 0) != 0)
                return Results.Conflict(ControlResult.Fail("host_stopping", "A stop or restart is already in progress."));
            if (!await jobs.DrainForShutdownAsync(CancellationToken.None))
            {
                Interlocked.Exchange(ref shutdownRequested, 0);
                return Results.Conflict(ControlResult.Fail("cleanup_pending", "One or more jobs still require confirmed cleanup.", true));
            }
            response.OnCompleted(() =>
            {
                if (restart)
                    requestRestart();
                app.Lifetime.StopApplication();
                return Task.CompletedTask;
            });
            return Results.Accepted();
        }
        v1.MapPost("/stop", (HttpResponse response) => StopAsync(response, restart: false));
        v1.MapPost("/restart", (HttpResponse response) => StopAsync(response, restart: true));
    }

    private static async Task StreamEventsAsync(HttpContext context, ControlJobService jobs, CancellationToken ct)
    {
        var scope = ReadScope(context.Request);
        var value = ReadQuery(context.Request, "after") ?? context.Request.Headers["Last-Event-ID"].ToString();
        if (!long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var sequence) && value.Length > 0)
            throw new ControlException(new("invalid_arguments", "Event cursor must be an unsigned sequence number."));
        context.Response.ContentType = "text/event-stream";
        await context.Response.WriteAsync(": connected\n\n", ct);
        await context.Response.Body.FlushAsync(ct);
        while (!ct.IsCancellationRequested)
        {
            var page = await jobs.ReadChangesAsync(scope, sequence, 50, ct);
            foreach (var change in page)
            {
                sequence = change.Sequence;
                var json = JsonSerializer.Serialize(change.Job, ControlProtocol.Json);
                await context.Response.WriteAsync($"id: {sequence}\nevent: job\ndata: {json}\n\n", ct);
            }
            if (page.Length > 0)
                await context.Response.Body.FlushAsync(ct);
            else
                await Task.Delay(300, ct);
        }
    }

    private static ControlScope ReadScope(HttpRequest http)
    {
        var profile = ReadQuery(http, "profileId") ?? ControlProtocol.DefaultProfileId;
        var value = ReadQuery(http, "sessionId");
        var id = default(Guid);
        if (value is not null && !Guid.TryParse(value, out id))
            throw new ControlException(new("invalid_arguments", "Invalid session ID."));
        var scope = new ControlScope(profile, value is null ? null : id);
        RequireProfile(scope);
        return scope;
    }

    private static void RequireProfile(ControlScope scope)
    {
        if (scope.ProfileId != ControlProtocol.DefaultProfileId)
            throw new ControlException(new("profile_conflict", "The requested profile is not active."));
    }

    private static int ReadLimit(HttpRequest http)
    {
        var value = ReadQuery(http, "limit");
        if (value is null)
            return 50;
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var limit) || limit is < 1 or > 100)
            throw new ControlException(new("invalid_arguments", "Page size must be between 1 and 100."));
        return limit;
    }

    private static string? ReadQuery(HttpRequest http, string key)
    {
        var values = http.Query[key];
        if (values.Count > 1)
            throw new ControlException(new("invalid_arguments", "Supply a single value for each query parameter."));
        return StringValues.IsNullOrEmpty(values) ? null : values[0];
    }
}
