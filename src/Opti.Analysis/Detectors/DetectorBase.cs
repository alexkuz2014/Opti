using Opti.Core.Findings;
using Opti.Core.Profile;

namespace Opti.Analysis.Detectors;

/// <summary>Детектор с одним правилом. Ошибки при неполных данных не пробрасываются наружу.</summary>
public abstract class DetectorBase : IDetector
{
    public abstract string Id { get; }

    public IEnumerable<Finding> Detect(DetectionContext ctx)
    {
        try { return Run(ctx.Profile, ctx).ToList(); }
        catch (Exception ex)
        {
            ctx.Profile.CollectorErrors.Add($"Детектор {Id}: {ex.Message}");
            return Array.Empty<Finding>();
        }
    }

    protected abstract IEnumerable<Finding> Run(SystemProfile p, DetectionContext ctx);
}

public static class DetectorCatalog
{
    public static IReadOnlyList<IDetector> All() => new IDetector[]
    {
        new DisplayLowHzDetector(),
        new HybridGameOnIgpuDetector(),
        new HybridNoMuxInfoDetector(),
        new DriverOldDetector(),
        new RamBelowRatedDetector(),
        new RamSingleChannelDetector(),
        new RamPressureDetector(),
        new ThermalThrottleDetector(),
        new PowerPlanDetector(),
        new ObsDisplayCaptureHybridDetector(),
        new ObsAdminDetector(),
        new ObsReplayBufferDetector(),
        new OverlayHeavyDetector(),
        new BcdRiskyTimersDetector(),
        new MpoKeyDetector(),
        new HvciDetector(),
        new DpcLatencyDetector(),
        new WifiGamingDetector(),
        new NetAdapterPowerSavingDetector(),
        new BufferbloatDetector(),
        new DiskFullDetector(),
        new DiskHealthDetector(),
        new StartupHeavyDetector(),
        new GameSettingsDetector(),
        new HagsAbCandidateDetector(),
        new TimerResolutionAbCandidateDetector(),
        new WeakHardwareDetector(),
        new BiosOldDetector(),
        new VirtualMachineDetector(),
        new SystemRestoreOffDetector(),
        new RamMixedModulesDetector(),
        new PrimaryDisplaySlowDetector(),
        new Display60HzCapTipDetector(),
        new HeavyResolutionDetector(),
        new GpuDriverMissingDetector(),
        new ProblemDevicesDetector(),
        new MonitorOnIgpuDetector(),
        new FreshInstallDetector(),
        new AmdX3dDriverDetector(),
        new IntelHybridWin10Detector(),
        new PendingRebootDetector(),
        new PageFileDisabledDetector(),
        new InputDetector(),
        new NetUnstableDetector(),
        new NetHighPingDetector(),
        new WifiQualityDetector(),
        new EthernetSlowLinkDetector(),
        new VpnProxyDetector(),
        new BackgroundTrafficDetector(),
        new TcpAutoTuningDetector(),
        new NetEnergySavingDetector(),
        new NagleCandidateDetector(),
        new OsUpdatesStaleDetector(),
        new OsEndOfSupportDetector(),
        new GpuLegacyDetector(),
        new OnBatteryDetector(),
        new BackgroundCpuHogDetector(),
        new GamesOnHddDetector(),
        new BootLimitsDetector(),
        new Gta5ProfileDetector(),
        new GpuPanelTipsDetector(),
        new ResizableBarDetector(),
        new PeripheralSoftwareDetector(),
        new DiskBusyDetector(),
        new PcieLinkWidthDetector(),
        new NvmeThermalDetector(),
        new MsiAuditDetector(),
        new NetRssDetector(),
        new NetServicesDetector(),
        new WifiCongestionDetector(),
        new BatteryHealthDetector(),
        new MultipleAntivirusDetector(),
        new GamesDiskFullDetector(),
        new GameLoadTipsDetector(),
        new DriverAuditDetector(),
        new RegistryAuditDetector(),
    };

    /// <summary>Запускает все детекторы и сортирует находки по важности.</summary>
    public static List<Finding> RunAll(SystemProfile profile, IEnumerable<IDetector>? detectors = null, DateTimeOffset? now = null)
    {
        var ctx = new DetectionContext { Profile = profile, Now = now ?? DateTimeOffset.Now };
        return (detectors ?? All())
            .SelectMany(d => d.Detect(ctx))
            .OrderByDescending(f => f.Severity)
            .ThenBy(f => f.Id, StringComparer.Ordinal)
            .ToList();
    }
}
