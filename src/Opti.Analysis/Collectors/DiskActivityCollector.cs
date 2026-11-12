using System.Runtime.Versioning;
using Opti.Core.Profile;

namespace Opti.Analysis.Collectors;

/// <summary>Один замер нагрузки на диск: занятость, очередь, скорость и кто сколько читает и пишет.</summary>
public sealed record DiskSample(double BusyPercent, double QueueLength, double BytesPerSec, IReadOnlyDictionary<string, double> ProcessBytesPerSec);

/// <summary>
/// Три замера по секунде во время анализа. Если диск занят, когда компьютер ничем не занят, игра будет грузиться медленнее:
/// ей придётся делить диск с обновлениями, индексатором, антивирусом или облачными дисками.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DiskActivityCollector : ICollector
{
    public string Name => "Нагрузка на диск";

    private static readonly HashSet<string> Ignored = new(StringComparer.OrdinalIgnoreCase)
        { "_Total", "Idle", "Opti", "Opti.Dev", "opti-cli", "WmiPrvSE", "powershell", "pwsh", "dotnet", "conhost", "cmd", "nvidia-smi" };

    public void Collect(SystemProfile p)
    {
        var samples = new List<DiskSample>();
        for (int i = 0; i < 3; i++)
        {
            if (i > 0) Thread.Sleep(1000);
            var disk = Wmi.Query("SELECT PercentDiskTime, AvgDiskQueueLength, DiskBytesPersec FROM Win32_PerfFormattedData_PerfDisk_PhysicalDisk WHERE Name='_Total'").FirstOrDefault();
            var procs = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in Wmi.Query("SELECT Name, IODataBytesPersec FROM Win32_PerfFormattedData_PerfProc_Process WHERE IODataBytesPersec > 500000"))
            {
                var name = Normalize(r.Str("Name") ?? "");
                if (name.Length == 0 || Ignored.Contains(name)) continue;
                procs[name] = procs.GetValueOrDefault(name) + (r.Long("IODataBytesPersec") ?? 0);
            }
            samples.Add(new DiskSample(disk?.Long("PercentDiskTime") ?? 0, disk?.Long("AvgDiskQueueLength") ?? 0, disk?.Long("DiskBytesPersec") ?? 0, procs));
        }
        p.DiskActivity = Summarize(samples);
    }

    /// <summary>«chrome#12» → «chrome».</summary>
    public static string Normalize(string wmiName)
    {
        int i = wmiName.IndexOf('#');
        return (i > 0 ? wmiName[..i] : wmiName).Trim();
    }

    public static DiskActivityInfo? Summarize(IReadOnlyList<DiskSample> samples)
    {
        if (samples.Count == 0) return null;
        var top = samples.SelectMany(s => s.ProcessBytesPerSec).GroupBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => new DiskWriter { Name = g.Key, MbPerSec = Math.Round(g.Sum(kv => kv.Value) / samples.Count / 1e6, 1) })
            .Where(w => w.MbPerSec >= 1).OrderByDescending(w => w.MbPerSec).Take(5).ToList();
        return new DiskActivityInfo
        {
            BusyPercent = Math.Round(Math.Min(100, samples.Average(s => s.BusyPercent)), 0),
            QueueLength = Math.Round(samples.Average(s => s.QueueLength), 1),
            ThroughputMBs = Math.Round(samples.Average(s => s.BytesPerSec) / 1e6, 1),
            Top = top,
        };
    }
}
