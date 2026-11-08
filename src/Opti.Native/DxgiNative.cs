using System.Runtime.Versioning;
using Vortice.DXGI;

namespace Opti.Native;

public sealed record DxgiAdapter(string Name, uint VendorId, uint DeviceId, long DedicatedVramMb, long SharedMemoryMb,
    string Luid, bool IsSoftware, IReadOnlyList<string> OutputDeviceNames);

[SupportedOSPlatform("windows")]
public static class DxgiNative
{
    public static IReadOnlyList<DxgiAdapter> GetAdapters()
    {
        var list = new List<DxgiAdapter>();
        using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
        for (int i = 0; factory.EnumAdapters1(i, out IDXGIAdapter1? adapter).Success; i++)
        {
            using (adapter)
            {
                var d = adapter!.Description1;
                var outputs = new List<string>();
                for (int o = 0; adapter.EnumOutputs(o, out IDXGIOutput? output).Success; o++)
                    using (output) outputs.Add(output!.Description.DeviceName);

                list.Add(new DxgiAdapter(
                    d.Description,
                    (uint)d.VendorId,
                    (uint)d.DeviceId,
                    (long)d.DedicatedVideoMemory / (1024 * 1024),
                    (long)d.SharedSystemMemory / (1024 * 1024),
                    $"{d.Luid.HighPart:X8}{d.Luid.LowPart:X8}",
                    (d.Flags & AdapterFlags.Software) != 0,
                    outputs));
            }
        }
        return list;
    }
}
