namespace Opti.Core.Stutter;

/// <summary>Одна секунда наблюдения за системой во время проверки фризов.</summary>
public sealed record StutterSample(
    double CpuPercent, double DpcPercent, double InterruptPercent,
    double DiskBusyPercent, double DiskQueue, double PagesPerSec, double AvailableMb,
    string? TopCpuProcess, double TopCpuProcessPercent, string? TopDiskProcess, double TopDiskMBs);

public sealed class StutterCause
{
    /// <summary>dpc, disk, memory, cpu-background, cpu-game.</summary>
    public string Kind { get; set; } = "";
    /// <summary>Сколько секунд из проверки это длилось.</summary>
    public int Seconds { get; set; }
    public string Detail { get; set; } = "";
    public List<string> Culprits { get; set; } = new();
}

public sealed class StutterInfo
{
    public int Seconds { get; set; }
    public List<StutterCause> Causes { get; set; } = new();
    public StallSummary? Stalls { get; set; }
}

/// <summary>Шов над счётчиками производительности Windows: в тестах подменяется.</summary>
public interface IStutterSampler
{
    StutterSample Sample();
}

/// <summary>
/// По секундным замерам находит причины фризов. Причина засчитывается, только если условие держалось не меньше трёх секунд
/// (и не меньше 5 % проверки): одиночный всплеск — это шум, а не причина.
/// </summary>
public static class StutterAnalyzer
{
    public const int MinSeconds = 3;
    public const double DpcPercent = 10, DiskBusy = 90, DiskQueue = 3, PagesPerSec = 1500, CpuBusy = 95, LowMemoryPercent = 8;

    public static StutterInfo Analyze(IReadOnlyList<StutterSample> samples, Func<string, bool> isGame, double totalRamMb = 0, StallSummary? stalls = null)
    {
        var info = new StutterInfo { Seconds = samples.Count, Stalls = stalls };
        int need = Math.Max(MinSeconds, (int)Math.Ceiling(samples.Count * 0.05));

        // Микрозамирания: пять и больше по 4+ мс или хотя бы одно дольше кадра (16 мс). Одиночные 4-мс паузы — обычная работа планировщика.
        if (stalls is not null && (stalls.Over4Ms >= 5 || stalls.Over16Ms >= 1))
            info.Causes.Add(new StutterCause
            {
                Kind = "stall", Seconds = (int)Math.Round(stalls.Seconds),
                Detail = $"система «замирала» на 4+ мс {stalls.Over4Ms} раз, на 16+ мс {stalls.Over16Ms} раз, самое длинное замирание {stalls.MaxMs:0.#} мс" +
                         (stalls.Worst.Count > 0 ? "; самые длинные на " + string.Join(", ", stalls.Worst.Take(3).Select(w => $"{w.AtSec:0.#} с ({w.Ms:0.#} мс)")) : ""),
            });

        var dpc = samples.Where(s => s.DpcPercent >= DpcPercent || s.InterruptPercent >= DpcPercent).ToList();
        if (dpc.Count >= need)
            info.Causes.Add(new StutterCause
            {
                Kind = "dpc", Seconds = dpc.Count,
                Detail = $"драйверы занимали процессор до {dpc.Max(s => Math.Max(s.DpcPercent, s.InterruptPercent)):0} % времени (обработка прерываний)",
            });

        var disk = samples.Where(s => s.DiskBusyPercent >= DiskBusy || s.DiskQueue >= DiskQueue).ToList();
        if (disk.Count >= need)
            info.Causes.Add(new StutterCause
            {
                Kind = "disk", Seconds = disk.Count,
                Detail = $"диск был занят на {disk.Average(s => s.DiskBusyPercent):0} %, очередь запросов до {disk.Max(s => s.DiskQueue):0.#}",
                Culprits = Top(disk.Select(s => s.TopDiskProcess)),
            });

        var mem = samples.Where(s => s.PagesPerSec >= PagesPerSec || (totalRamMb > 0 && s.AvailableMb > 0 && 100 * s.AvailableMb / totalRamMb <= LowMemoryPercent)).ToList();
        if (mem.Count >= need)
            info.Causes.Add(new StutterCause
            {
                Kind = "memory", Seconds = mem.Count,
                Detail = $"не хватало оперативной памяти: Windows подкачивала данные с диска до {mem.Max(s => s.PagesPerSec):0} страниц в секунду",
                Culprits = Top(mem.Select(s => s.TopCpuProcess)),
            });

        var cpu = samples.Where(s => s.CpuPercent >= CpuBusy).ToList();
        if (cpu.Count >= need)
        {
            var tops = cpu.Select(s => s.TopCpuProcess).Where(n => !string.IsNullOrEmpty(n)).ToList();
            var topName = tops.GroupBy(n => n!, StringComparer.OrdinalIgnoreCase).OrderByDescending(g => g.Count()).FirstOrDefault()?.Key;
            bool game = topName is not null && isGame(topName);
            info.Causes.Add(new StutterCause
            {
                Kind = game ? "cpu-game" : "cpu-background", Seconds = cpu.Count,
                Detail = $"процессор был загружен на {cpu.Average(s => s.CpuPercent):0} %",
                Culprits = Top(cpu.Select(s => s.TopCpuProcess)),
            });
        }
        return info;
    }

    private static List<string> Top(IEnumerable<string?> names) =>
        names.Where(n => !string.IsNullOrEmpty(n)).GroupBy(n => n!, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count()).Take(3).Select(g => g.Key).ToList();
}
