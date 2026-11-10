using System.Diagnostics;
using Opti.Analysis.Collectors;
using Opti.Analysis.Detectors;
using Opti.Core.Findings;
using Opti.Core.Profile;

namespace Opti.Analysis;

public sealed class AnalysisResult
{
    public required SystemProfile Profile { get; init; }
    public required List<Finding> Findings { get; init; }
    public List<CollectorTiming> Timings { get; init; } = new();
}

/// <summary>Анализ системы: сбор профиля всеми коллекторами + запуск детекторов.</summary>
public static class SystemAnalyzer
{
    public static IReadOnlyList<ICollector> DefaultCollectors(string? obsConfigDir = null, string? networkTarget = "1.1.1.1")
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Сбор профиля работает только в Windows. Для тестов используйте сохранённые профили (--profile).");
        var list = new List<ICollector>
        {
            new DeviceCollector(), new OsCollector(), new CpuCollector(), new GpuDisplayCollector(), new MemoryCollector(),
            new DiskCollector(), new PowerCollector(), new ThermalCollector(), new TimersAndSecurityCollector(),
            new GraphicsSettingsCollector(), new BackgroundCollector(), new ObsCollector(obsConfigDir), new NetworkCollector(), new GamesCollector(), new ExtrasCollector(), new DriversCollector(),
        };
        // Быстрый пинг до роутера и интернета; networkTarget = null — проверка сети выключена в настройках
        if (networkTarget is not null) list.Add(new NetworkQualityCollector(networkTarget));
        return list;
    }

    public static AnalysisResult Collect(IEnumerable<ICollector>? collectors = null, Action<string>? progress = null)
    {
        var profile = new SystemProfile { CollectedAt = DateTimeOffset.Now, Label = Environment.MachineName };
        var timings = new List<CollectorTiming>();
        foreach (var c in collectors ?? DefaultCollectors())
        {
            progress?.Invoke(c.Name);
            var sw = Stopwatch.StartNew();
            string? error = null;
            try { c.Collect(profile); }
            catch (Exception ex)
            {
                error = ex.Message;
                profile.CollectorErrors.Add($"{c.Name}: {ex.GetType().Name}: {ex.Message}");
            }
            timings.Add(new CollectorTiming(c.Name, sw.Elapsed, error));
        }
        return new AnalysisResult { Profile = profile, Findings = DetectorCatalog.RunAll(profile), Timings = timings };
    }

    public static AnalysisResult FromProfile(SystemProfile profile) =>
        new() { Profile = profile, Findings = DetectorCatalog.RunAll(profile) };
}
