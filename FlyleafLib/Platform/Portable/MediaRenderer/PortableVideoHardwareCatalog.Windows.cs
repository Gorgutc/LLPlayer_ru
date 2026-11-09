#if PORTABLE_WINDOWS
#nullable enable
using System.Globalization;
using Vortice.DXGI;

namespace FlyleafLib.MediaFramework.MediaRenderer;

public static partial class PortableVideoHardwareCatalog
{
    static IReadOnlyList<PortableVideoAdapterInfo> EnumerateWindowsAdapters()
    {
        if (DXGI.CreateDXGIFactory1(out IDXGIFactory1? factory).Failure || factory is null)
        {
            factory?.Dispose();
            throw new InvalidOperationException("The DXGI adapter catalog could not be initialized.");
        }
        using (factory)
        {
            List<PortableVideoAdapterInfo> result = [];
            for (uint index = 0; ; index++)
            {
                var enumeration = factory.EnumAdapters1(index, out var adapter);
                if (enumeration == ResultCode.NotFound)
                {
                    adapter?.Dispose();
                    break;
                }
                if (enumeration.Failure || adapter is null)
                {
                    adapter?.Dispose();
                    throw new InvalidOperationException($"DXGI adapter enumeration failed ({enumeration}).");
                }
                using (adapter)
                {
                    var description = adapter.Description1;
                    long luid = description.Luid;
                    bool software = (description.Flags & AdapterFlags.Software) != 0;
                    result.Add(new($"dxgi:{unchecked((ulong)luid):X16}", description.Description,
                        PortableVideoDecodeMode.D3D11VA, index.ToString(CultureInfo.InvariantCulture), !software,
                        software ? "The DXGI adapter is a software adapter." : null));
                }
            }
            return result.AsReadOnly();
        }
    }
}
#endif
