using System.Net.Http;

namespace FlyleafLib.MediaPlayer.Translation.Services;

#nullable enable

/// <summary>
/// Shared HTTP connection pool for the simple HTTP translate services (DeepLX, GoogleV1, Microsoft).
/// Those services are created and disposed frequently (per translator Reset, per word lookup, per batch
/// translation), so a per-instance HttpClient with its own handler kept accumulating sockets / TIME_WAIT
/// entries. A single process-wide <see cref="SocketsHttpHandler"/> pools connections across instances,
/// while each service still gets its own HttpClient so per-service BaseAddress / Timeout / default headers
/// stay isolated. The handler is never disposed (disposeHandler: false), so disposing a service's
/// HttpClient does not tear down the shared pool.
/// </summary>
internal static class TranslateHttpClient
{
    private static readonly SocketsHttpHandler SharedHandler = new()
    {
        // Recycle connections periodically so DNS changes are eventually picked up.
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        PooledConnectionIdleTimeout = TimeSpan.FromSeconds(90),
        MaxConnectionsPerServer = 20
    };

    public static HttpClient Create(Uri baseAddress, TimeSpan timeout)
        => new(SharedHandler, disposeHandler: false)
        {
            BaseAddress = baseAddress,
            Timeout = timeout
        };
}
