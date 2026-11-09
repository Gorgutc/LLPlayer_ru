#nullable enable
using System.Linq;

namespace FlyleafLib.MediaFramework.MediaRenderer;

/// <summary>Explicit native adapter discovery. Availability means accessible, not successful codec negotiation.</summary>
public static partial class PortableVideoHardwareCatalog
{
    /// <summary>Enumerates real native identities without allocating an FFmpeg hardware context.</summary>
    public static IReadOnlyList<PortableVideoAdapterInfo> EnumerateAdapters()
    {
        if (OperatingSystem.IsWindows())
        {
#if PORTABLE_WINDOWS
            return EnumerateWindowsAdapters();
#else
            throw new PlatformNotSupportedException("This build does not contain the Windows DXGI adapter catalog.");
#endif
        }
        if (OperatingSystem.IsLinux())
            return EnumerateLinuxAdapters();
        throw new PlatformNotSupportedException("Portable hardware decoding supports Windows D3D11VA and Linux VAAPI.");
    }

    static IReadOnlyList<PortableVideoAdapterInfo> EnumerateLinuxAdapters()
    {
        if (!Directory.Exists("/dev/dri"))
            return Array.Empty<PortableVideoAdapterInfo>();

        Dictionary<string, string> byPath = new(StringComparer.Ordinal);
        if (Directory.Exists("/dev/dri/by-path"))
            foreach (string path in Directory.EnumerateFiles("/dev/dri/by-path", "*-render").Order(StringComparer.Ordinal))
            {
                var target = new FileInfo(path).ResolveLinkTarget(true);
                if (target is not null)
                    byPath.TryAdd(target.FullName, path);
            }

        List<PortableVideoAdapterInfo> result = [];
        foreach (string node in Directory.EnumerateFiles("/dev/dri", "renderD*").Order(StringComparer.Ordinal))
        {
            string? identity = byPath.GetValueOrDefault(node);
            identity ??= new DirectoryInfo($"/sys/class/drm/{Path.GetFileName(node)}/device").ResolveLinkTarget(true)?.FullName;
            string? reason = identity is null ? "A stable native render-device identity could not be resolved." : null;
            if (reason is null)
            {
                try
                {
                    // Explicit refresh only: access is checked independently of libva/codec support.
                    using var handle = File.OpenHandle(node, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
                }
                catch (UnauthorizedAccessException error) { reason = $"Render-node access denied: {error.Message}"; }
                catch (IOException error) { reason = $"Render node is inaccessible: {error.Message}"; }
            }
            result.Add(new("vaapi:" + (identity ?? node), Path.GetFileName(node) + " (VAAPI)",
                PortableVideoDecodeMode.VAAPI, node, reason is null, reason));
        }
        return result.AsReadOnly();
    }
}
