using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace GameTune.Native;

[SupportedOSPlatform("windows")]
public static class PowerNative
{
    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag;
        public int BatteryLifeTime, BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll")] private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);
    [DllImport("powrprof.dll")] private static extern uint PowerGetEffectiveOverlayScheme(out Guid overlay);
    [DllImport("powrprof.dll")] private static extern uint PowerSetActiveOverlayScheme(Guid overlay);
    [DllImport("powrprof.dll")] private static extern uint CallNtPowerInformation(int level, IntPtr input, uint inputLen, byte[] output, uint outputLen);

    private const int SystemPowerCapabilities = 4;

    /// <summary>SYSTEM_POWER_CAPABILITIES: [2] LidPresent, [20] AoAc (Modern Standby).</summary>
    public static (bool? LidPresent, bool? ModernStandby) GetCapabilities()
    {
        var buf = new byte[128];
        if (CallNtPowerInformation(SystemPowerCapabilities, IntPtr.Zero, 0, buf, (uint)buf.Length) != 0) return (null, null);
        return (buf[2] != 0, buf[20] != 0);
    }

    public static readonly Guid OverlayBetterBattery = new("961cc777-2547-4f9d-8174-7d86181b8a7a");
    public static readonly Guid OverlayBetterPerformance = new("3af9b8d9-7c97-431d-ad78-34a8bfea439f");
    public static readonly Guid OverlayBestPerformance = new("ded574b5-45a0-4f42-8737-46345c09c238");

    /// <summary>(от сети?, есть батарея?, заряд %)</summary>
    public static (bool? OnAc, bool HasBattery, int? Percent) GetStatus()
    {
        if (!GetSystemPowerStatus(out var s)) return (null, false, null);
        bool? ac = s.ACLineStatus switch { 0 => false, 1 => true, _ => null };
        bool hasBattery = (s.BatteryFlag & 128) == 0 && s.BatteryFlag != 255;
        int? pct = s.BatteryLifePercent <= 100 ? s.BatteryLifePercent : null;
        return (ac, hasBattery, pct);
    }

    /// <summary>GUID текущего режима питания (ползунка). Guid.Empty — «Сбалансированный».</summary>
    public static Guid? GetOverlayGuid()
    {
        try { return PowerGetEffectiveOverlayScheme(out var g) == 0 ? g : null; }
        catch (EntryPointNotFoundException) { return null; }
    }

    /// <summary>Устанавливает режим питания для текущего источника питания (от сети / от батареи).</summary>
    public static void SetOverlay(Guid overlay)
    {
        uint rc = PowerSetActiveOverlayScheme(overlay);
        if (rc != 0) throw new InvalidOperationException($"PowerSetActiveOverlayScheme вернул {rc}");
    }

    /// <summary>Режим питания Windows (ползунок): BetterBattery / Balanced / BetterPerformance / BestPerformance.</summary>
    public static string? GetOverlayMode()
    {
        try
        {
            if (PowerGetEffectiveOverlayScheme(out var g) != 0) return null;
            if (g == OverlayBetterBattery) return "BetterBattery";
            if (g == OverlayBetterPerformance) return "BetterPerformance";
            if (g == OverlayBestPerformance) return "BestPerformance";
            if (g == Guid.Empty) return "Balanced";
            return g.ToString();
        }
        catch (EntryPointNotFoundException) { return null; }
    }
}
