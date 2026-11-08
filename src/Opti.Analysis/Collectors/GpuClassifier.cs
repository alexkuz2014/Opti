using Opti.Core.Profile;

namespace Opti.Analysis.Collectors;

/// <summary>Классификация видеоадаптеров. Без привязки к Windows — покрыта тестами.</summary>
public static class GpuClassifier
{
    public static GpuVendor Vendor(uint vendorId, string name) => vendorId switch
    {
        0x10DE => GpuVendor.Nvidia,
        0x1002 or 0x1022 => GpuVendor.Amd,
        0x8086 => GpuVendor.Intel,
        0x1414 => GpuVendor.Microsoft,
        _ => name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ? GpuVendor.Nvidia
           : name.Contains("Radeon", StringComparison.OrdinalIgnoreCase) || name.Contains("AMD", StringComparison.OrdinalIgnoreCase) ? GpuVendor.Amd
           : name.Contains("Intel", StringComparison.OrdinalIgnoreCase) ? GpuVendor.Intel
           : GpuVendor.Other,
    };

    /// <summary>Производитель по PNPDeviceID вида PCI\VEN_10DE&amp;DEV_…</summary>
    public static GpuVendor VendorFromPnp(string pnp)
    {
        var m = System.Text.RegularExpressions.Regex.Match(pnp, @"VEN_([0-9A-Fa-f]{4})");
        return m.Success ? Vendor(Convert.ToUInt32(m.Groups[1].Value, 16), "") : GpuVendor.Unknown;
    }

    public static bool IsIntegrated(GpuVendor vendor, string name, long dedicatedVramMb)
    {
        var n = name.ToLowerInvariant();
        switch (vendor)
        {
            case GpuVendor.Nvidia:
                return false;
            case GpuVendor.Intel:
                // Arc A/B-серии — дискретные; «Intel Arc Graphics» (Meteor/Lunar Lake) — встроенная.
                return !(System.Text.RegularExpressions.Regex.IsMatch(n, @"arc\(tm\)\s*[ab]\d{3}|arc\s+[ab]\d{3}") || n.Contains("iris xe max"));
            case GpuVendor.Amd:
                if (n.Contains("radeon(tm) graphics") || n.Contains("radeon graphics")) return true;
                if (System.Text.RegularExpressions.Regex.IsMatch(n, @"\b\d{3}m\b")) return true;          // 680M, 780M, 890M
                if (n.Contains("vega") && n.Contains("graphics")) return true;                               // «RX Vega 10 Graphics» в Ryzen
                if (n.Contains("rx ") || n.Contains("radeon pro") || n.Contains("firepro")) return false;
                return dedicatedVramMb < 1024;
            case GpuVendor.Microsoft:
                return false;
            default:
                return dedicatedVramMb < 1024;
        }
    }
}
