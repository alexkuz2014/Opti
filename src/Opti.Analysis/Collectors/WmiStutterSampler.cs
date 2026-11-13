using System.Runtime.Versioning;
using Opti.Core.Stutter;

namespace Opti.Analysis.Collectors;

/// <summary>Секундный замер процессора (DPC, прерывания), диска, памяти и главных потребителей — из счётчиков производительности Windows.</summary>
[SupportedOSPlatform("windows")]
public sealed class WmiStutterSampler : IStutterSampler
{
    private static readonly HashSet<string> Ignored = new(StringComparer.OrdinalIgnoreCase)
        { "_Total", "Idle", "Opti", "Opti.Dev", "opti-cli", "WmiPrvSE", "powershell", "pwsh", "dotnet", "System" };

    public StutterSample Sample()
    {
        var cpu = Wmi.Query("SELECT PercentProcessorTime, PercentDPCTime, PercentInterruptTime FROM Win32_PerfFormattedData_PerfOS_Processor WHERE Name='_Total'").FirstOrDefault();
        var mem = Wmi.Query("SELECT PagesPersec, AvailableMBytes FROM Win32_PerfFormattedData_PerfOS_Memory").FirstOrDefault();
        var disk = Wmi.Query("SELECT PercentDiskTime, AvgDiskQueueLength FROM Win32_PerfFormattedData_PerfDisk_PhysicalDisk WHERE Name='_Total'").FirstOrDefault();
        int cores = Math.Max(1, Environment.ProcessorCount);
        string? topCpu = null, topDisk = null;
        double topCpuPct = 0, topDiskMb = 0;
        foreach (var r in Wmi.Query("SELECT Name, PercentProcessorTime, IODataBytesPersec FROM Win32_PerfFormattedData_PerfProc_Process"))
        {
            var name = DiskActivityCollector.Normalize(r.Str("Name") ?? "");
            if (name.Length == 0 || Ignored.Contains(name)) continue;
            double pct = (r.Long("PercentProcessorTime") ?? 0) / (double)cores;       // счётчик даёт проценты одного ядра
            double mb = (r.Long("IODataBytesPersec") ?? 0) / 1e6;
            if (pct > topCpuPct) (topCpu, topCpuPct) = (name, pct);
            if (mb > topDiskMb) (topDisk, topDiskMb) = (name, mb);
        }
        return new StutterSample(
            cpu?.Long("PercentProcessorTime") ?? 0, cpu?.Long("PercentDPCTime") ?? 0, cpu?.Long("PercentInterruptTime") ?? 0,
            Math.Min(100, disk?.Long("PercentDiskTime") ?? 0), disk?.Long("AvgDiskQueueLength") ?? 0,
            mem?.Long("PagesPersec") ?? 0, mem?.Long("AvailableMBytes") ?? 0,
            topCpu, Math.Round(topCpuPct, 1), topDisk, Math.Round(topDiskMb, 1));
    }
}
