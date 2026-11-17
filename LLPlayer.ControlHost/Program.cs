using System.Diagnostics;
using System.Net;
using LLPlayer.ControlApi;
using LLPlayer.Core.Automation;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http.Json;

namespace LLPlayer.ControlHost;

internal static class Program
{
    private const int RestartRequested = 77;

    public static async Task<int> Main(string[] args)
    {
        try
        {
            var (configDir, stateDir) = ParsePaths(args);
            if (args.FirstOrDefault() == "--detach")
                return Detach(configDir, stateDir);
            var result = await ServeAsync(configDir, stateDir);
            return result == RestartRequested ? Detach(configDir, stateDir) : result;
        }
        catch (ControlException exception)
        {
            Console.Error.WriteLine($"[{exception.Error.Code}] {exception.Error.Message}");
            return 2;
        }
        catch (Exception)
        {
            Console.Error.WriteLine("LLPlayer.ControlHost could not start or stop cleanly.");
            return 1;
        }
    }

    private static (string ConfigDir, string StateDir) ParsePaths(string[] args)
    {
        var (configDir, stateDir) = ControlPaths.DefaultAppDirectories();
        if (args.Length == 0)
            return (configDir, stateDir);
        if (args[0] is not ("--detach" or "--serve") || args.Length % 2 != 1)
            throw new ArgumentException("Expected --serve or --detach and optional directory pairs.");
        for (var i = 1; i < args.Length; i += 2)
        {
            var path = Path.GetFullPath(args[i + 1]);
            switch (args[i])
            {
                case "--config-dir":
                    configDir = path;
                    break;
                case "--state-dir":
                    stateDir = path;
                    break;
                default:
                    throw new ArgumentException("Unknown directory option.");
            }
        }
        return (configDir, stateDir);
    }

    private static int Detach(string configDir, string stateDir)
    {
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Apphost path is unavailable.");
        var setsid = !OperatingSystem.IsWindows() && File.Exists("/usr/bin/setsid");
        var psi = new ProcessStartInfo(setsid ? "/usr/bin/setsid" : executable)
        {
            WorkingDirectory = AppContext.BaseDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        if (setsid)
        {
            psi.ArgumentList.Add("--fork");
            psi.ArgumentList.Add(executable);
        }
        if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            psi.ArgumentList.Add(typeof(Program).Assembly.Location);
        psi.ArgumentList.Add("--serve");
        psi.ArgumentList.Add("--config-dir");
        psi.ArgumentList.Add(configDir);
        psi.ArgumentList.Add("--state-dir");
        psi.ArgumentList.Add(stateDir);
        if (OperatingSystem.IsWindows())
        {
            WindowsDetachedHost.Start(psi);
            return 0;
        }
        using var process = Process.Start(psi);
        return process is null ? 1 : 0;
    }

    private static async Task<int> ServeAsync(string configDir, string stateDir)
    {
        using var ownership = ControlHostDiscovery.TryAcquire();
        if (ownership is null)
            return 0;

        Directory.CreateDirectory(configDir);
        configDir = ControlPaths.ResolveDirectory(configDir);
        stateDir = ControlPaths.ResolveDirectory(stateDir);
        var jobDirectory = Path.Combine(stateDir, "Automation");
        ownership.BindState(stateDir);
        ControlHostDiscovery.EnsurePrivateDirectory(jobDirectory);
        var instanceId = Guid.NewGuid();
        var token = ControlHostDiscovery.CreateCredential();
        var broker = new ControlGuiBroker();
        var handlers = new Dictionary<string, ControlJobHandler>(StringComparer.Ordinal);
        await using var jobs = new ControlJobService(jobDirectory, handlers, ControlHostOperations.RequiresCleanResources);
        var settings = new ControlSettingsService(configDir, fingerprintSettings: jobs.FingerprintSettings);
        var ffmpeg = Environment.GetEnvironmentVariable("LLPLAYER_FFMPEG_DIR")
            ?? Path.Combine(AppContext.BaseDirectory, "FFmpeg");
        await using var runtime = new ControlLanguageRuntime(configDir, stateDir, ffmpeg,
            settings.CaptureEngineConfig, () => settings.CapturePlayback().SubtitleExportUtf8Bom,
            applyStartup: settings.ApplyPlaybackStartupTo);
        var catalog = new ControlOperationCatalog();
        new ControlHostOperations(catalog, jobs, handlers, broker, settings, runtime).Register(configDir, stateDir);

        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            Args = [],
            ContentRootPath = AppContext.BaseDirectory
        });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            kestrel.AddServerHeader = false;
            kestrel.Limits.MaxRequestBodySize = 2 * 1024 * 1024;
            kestrel.Listen(IPAddress.Loopback, 0);
        });
        builder.Services.Configure<JsonOptions>(options =>
        {
            var source = ControlProtocol.Json;
            options.SerializerOptions.PropertyNamingPolicy = source.PropertyNamingPolicy;
            options.SerializerOptions.PropertyNameCaseInsensitive = source.PropertyNameCaseInsensitive;
            options.SerializerOptions.NumberHandling = source.NumberHandling;
            options.SerializerOptions.UnmappedMemberHandling = source.UnmappedMemberHandling;
            options.SerializerOptions.RespectRequiredConstructorParameters = source.RespectRequiredConstructorParameters;
            options.SerializerOptions.RespectNullableAnnotations = source.RespectNullableAnnotations;
            options.SerializerOptions.MaxDepth = source.MaxDepth;
            foreach (var converter in source.Converters)
                options.SerializerOptions.Converters.Add(converter);
        });
        await using var app = builder.Build();
        var restart = false;
        ControlHostRoutes.Map(app, catalog, jobs, broker, instanceId, stateDir, token, () => restart = true);
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features
            .Get<IServerAddressesFeature>()!.Addresses.Single();
        await ownership.PublishAsync(new(ControlProtocol.Version, instanceId, Environment.ProcessId,
            ControlProtocol.DefaultProfileId, configDir, address, token), app.Lifetime.ApplicationStopping);

        using var timer = new Timer(_ => broker.Expire(), null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
        using var closeSessions = app.Lifetime.ApplicationStopping.Register(broker.CloseAll);
        bool drained;
        try
        {
            await app.WaitForShutdownAsync();
        }
        finally
        {
            drained = await jobs.DrainForShutdownAsync(CancellationToken.None);
            if (drained)
                ownership.ReleaseState();
        }
        return !drained ? 1 : restart ? RestartRequested : 0;
    }
}
