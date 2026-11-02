#if !WINDOWS
using System.Reflection;
using System.Runtime.Loader;
using System.Runtime.Versioning;
using FlyleafLib.Plugins;

namespace FlyleafLib.Tests;

public class YoutubeDLPackageTests
{
    [Fact]
    public void PortablePluginLoadsAgainstSharedEngine()
    {
        ValidatePlugin(Environment.GetEnvironmentVariable("LLPLAYER_PACKAGE_DIR"));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("invalid")]
    [InlineData("incompatible")]
    public void PackagedPluginRejectsMissingOrIncompatiblePackagedEngine(string scenario)
        => WithPackage(scenario, directory => Assert.ThrowsAny<Exception>(() => ValidatePlugin(directory)));

    [Fact]
    public void PackagedPortableEngineLoadsAsItsOwnAssembly()
        => WithPackage("portable", ValidatePlugin);

    [Fact]
    public void PackagedPluginRejectsWpfEngine()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "FlyleafLib", "FlyleafLib.csproj"))) root = root.Parent;
        var bin = root == null ? null : Path.Combine(root.FullName, "FlyleafLib", "bin");
        var windowsEngine = bin == null || !Directory.Exists(bin) ? null :
            Directory.EnumerateFiles(bin, "FlyleafLib.dll", SearchOption.AllDirectories)
                .FirstOrDefault(path => path.Contains("net10.0-windows", StringComparison.Ordinal));
        Assert.SkipWhen(windowsEngine == null, "Requires the separately built WPF engine; the Linux --fast gate can skip its build.");
        WithPackage("windows", directory => Assert.ThrowsAny<Exception>(() => ValidatePlugin(directory)), windowsEngine);
    }

    private static void WithPackage(string scenario, Action<string> check, string? engineSource = null)
    {
        var directory = Path.Combine(Path.GetTempPath(), "LLPlayer-package-load-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(directory, "Plugins", "YoutubeDL"));
        try
        {
            File.Copy(typeof(YoutubeDL).Assembly.Location, Path.Combine(directory, "Plugins", "YoutubeDL", "YoutubeDL.dll"));
            // A managed component root is enough for AssemblyDependencyResolver; no app/native startup occurs.
            File.Copy(typeof(YoutubeDL).Assembly.Location, Path.Combine(directory, "LLPlayer.Avalonia.dll"));
            var engine = Path.Combine(directory, "FlyleafLib.dll");
            if (scenario == "invalid") File.WriteAllText(engine, "not a managed engine");
            if (scenario is "portable" or "incompatible" or "windows")
                File.Copy(engineSource ?? typeof(PluginBase).Assembly.Location, engine);
            if (scenario == "incompatible")
            {
                // Keep the real engine's assembly/TFM identity but remove the plugin's required base type.
                var bytes = File.ReadAllBytes(engine);
                var oldName = System.Text.Encoding.UTF8.GetBytes("PluginBase");
                var newName = System.Text.Encoding.UTF8.GetBytes("PluginBaze");
                var replacements = 0;
                for (var i = 0; i <= bytes.Length - oldName.Length; i++)
                    if (bytes.AsSpan(i, oldName.Length).SequenceEqual(oldName))
                    {
                        newName.CopyTo(bytes, i);
                        replacements++;
                    }
                Assert.True(replacements > 0, "The incompatible engine fixture must alter its required type metadata.");
                File.WriteAllBytes(engine, bytes);
            }
            check(directory);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void ValidatePlugin(string? directory)
    {
        var path = directory == null ? typeof(YoutubeDL).Assembly.Location :
            Path.GetFullPath(Path.Combine(directory, "Plugins", "YoutubeDL", "YoutubeDL.dll"));
        var context = directory == null
            ? new AssemblyLoadContext("YoutubeDL local-output test", isCollectible: true)
            : new PackageLoadContext(Path.GetFullPath(directory));
        if (directory == null)
            context.Resolving += (_, name) => name.Name == "FlyleafLib" ? typeof(PluginBase).Assembly : null;
        try
        {
            var engine = directory == null ? typeof(PluginBase).Assembly :
                LoadManaged(context, Path.GetFullPath(Path.Combine(directory, "FlyleafLib.dll")));
            Assert.Equal("FlyleafLib", engine.GetName().Name);
            AssertPortable(engine);
            if (directory != null)
            {
                Assert.NotSame(typeof(PluginBase).Assembly, engine);
                Assert.Same(context, AssemblyLoadContext.GetLoadContext(engine));
            }
            var assembly = LoadManaged(context, path);
            Assert.Equal("YoutubeDL", assembly.GetName().Name);
            AssertPortable(assembly);
            var type = assembly.GetType("FlyleafLib.Plugins.YoutubeDL", throwOnError: true)!;
            var packagedBase = engine.GetType("FlyleafLib.Plugins.PluginBase", throwOnError: true)!;
            var packagedOpen = engine.GetType("FlyleafLib.Plugins.IOpen", throwOnError: true)!;
            Assert.Same(engine, type.BaseType!.Assembly);
            Assert.True(packagedBase.IsAssignableFrom(type));
            Assert.True(packagedOpen.IsAssignableFrom(type));
            using var plugin = Assert.IsAssignableFrom<IDisposable>(Activator.CreateInstance(type));
            var options = Assert.IsAssignableFrom<IDictionary<string, string>>(
                type.GetMethod("GetDefaultOptions")!.Invoke(plugin, null));
            Assert.Equal("", options[OnlineVideoRequest.YtDlpPath]);
            Assert.Equal("", options[OnlineVideoRequest.DenoPath]);
            Assert.Equal("720", options["MaxVideoHeight"]);
            Assert.Equal("", options["ExtraArguments"]);
        }
        finally { context.Unload(); }
    }

    private static Assembly LoadManaged(AssemblyLoadContext context, string path)
    {
        // Consume these exact packaged bytes without mapping a fixture DLL for the lifetime of the
        // testhost. Dependency resolution still uses the real package's app .deps.json below.
        using var input = File.OpenRead(path);
        return context.LoadFromStream(input);
    }

    private static void AssertPortable(Assembly assembly)
    {
        Assert.Equal(".NETCoreApp,Version=v10.0", assembly.GetCustomAttribute<TargetFrameworkAttribute>()?.FrameworkName);
        Assert.Null(assembly.GetCustomAttribute<TargetPlatformAttribute>());
        Assert.DoesNotContain(assembly.GetReferencedAssemblies(), reference =>
            reference.Name is "PresentationCore" or "PresentationFramework" or "WindowsBase" or "System.Windows.Forms");
    }

    private sealed class PackageLoadContext(string directory) : AssemblyLoadContext("YoutubeDL packaged-engine test", isCollectible: true)
    {
        private readonly AssemblyDependencyResolver resolver = new(Path.Combine(directory, "LLPlayer.Avalonia.dll"));

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            // Never satisfy the packaged plugin with a testhost FlyleafLib, even if its package copy
            // is absent or incompatible. Other app dependencies use the actual app's dependency graph.
            var resolved = assemblyName.Name == "FlyleafLib"
                ? Path.Combine(directory, "FlyleafLib.dll")
                : resolver.ResolveAssemblyToPath(assemblyName);
            return resolved == null ? null : LoadManaged(this, resolved);
        }
    }
}
#endif
