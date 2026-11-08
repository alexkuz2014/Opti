using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using GameTune.Core.Json;
using GameTune.Measure.Stats;

namespace GameTune.Workflow.Measuring;

/// <summary>Синтетический тест GameTune.Bench (D3D11): одна и та же сцена и длительность — полная воспроизводимость.</summary>
[SupportedOSPlatform("windows")]
public sealed class SyntheticRunner : IMeasureRunner
{
    private readonly string _exe;
    public SyntheticRunner(string exe) => _exe = exe;
    public string Name => "Синтетический тест GameTune";

    public int Instances { get; init; } = 8192;
    public double GpuLoad { get; init; } = 24;
    public double CpuLoadMs { get; init; } = 1.0;

    public static string? Locate()
    {
        foreach (var dir in new[] { AppContext.BaseDirectory, Path.Combine(AppContext.BaseDirectory, "bench") })
        {
            var p = Path.Combine(dir, "GameTune.Bench.exe");
            if (File.Exists(p)) return p;
        }
        return null;
    }

    public async Task<MeasurementRun> RunAsync(MeasureRequest req, IProgress<MeasureProgress>? progress = null, CancellationToken ct = default)
    {
        var outFile = Path.Combine(Path.GetTempPath(), $"gametune-bench-{Guid.NewGuid():N}.json");
        var args = string.Format(CultureInfo.InvariantCulture,
            "--out \"{0}\" --duration {1} --warmup {2} --instances {3} --gpu-load {4} --cpu-load {5} --label \"{6}\"",
            outFile, req.DurationSec, req.WarmupSec, Instances, GpuLoad, CpuLoadMs, req.Label.Replace("\"", "'"));
        var probe = new ThrottleProbe();
        using var p = Process.Start(new ProcessStartInfo(_exe, args) { UseShellExecute = false })
                      ?? throw new InvalidOperationException("Не удалось запустить GameTune.Bench");
        var sw = Stopwatch.StartNew();
        double total = req.DurationSec + req.WarmupSec;
        int tick = 0;
        while (!p.HasExited)
        {
            if (ct.IsCancellationRequested) { try { p.Kill(true); } catch { } ct.ThrowIfCancellationRequested(); }
            if (sw.Elapsed.TotalSeconds > total + 60) { try { p.Kill(true); } catch { } throw new TimeoutException("Синтетический тест завис."); }
            await Task.Delay(500, CancellationToken.None);
            if (++tick % 10 == 0) probe.Sample();
            progress?.Report(new MeasureProgress(Math.Min(sw.Elapsed.TotalSeconds, total), total, Array.Empty<double>(), "GameTune.Bench", sw.Elapsed.TotalSeconds < req.WarmupSec));
        }
        if (p.ExitCode == 2) throw new OperationCanceledException("Синтетический тест прерван клавишей Esc.");
        if (p.ExitCode != 0 || !File.Exists(outFile))
        {
            var err = File.Exists(outFile + ".error.txt") ? File.ReadAllText(outFile + ".error.txt") : $"код {p.ExitCode}";
            throw new InvalidOperationException("Синтетический тест завершился с ошибкой: " + err.Split('\n')[0]);
        }
        var run = GameTuneJson.Deserialize<MeasurementRun>(File.ReadAllText(outFile));
        try { File.Delete(outFile); } catch { }
        run.ThermalThrottled |= probe.Throttled;
        if (probe.Reason is not null) run.Warnings.Add(probe.Reason);
        return run;
    }
}

public static class MeasureRunnerFactory
{
    /// <summary>Подбирает исполнителя замера по режиму сессии.</summary>
    [SupportedOSPlatform("windows")]
    public static IMeasureRunner Create(Sessions.MeasureMode mode, GameTuneSettings settings) => mode switch
    {
        Sessions.MeasureMode.Synthetic => new SyntheticRunner(SyntheticRunner.Locate()
            ?? throw new FileNotFoundException("GameTune.Bench.exe не найден рядом с программой.")),
        Sessions.MeasureMode.PresentMon => new PresentMonRunner(PresentMonRunner.Locate(settings.PresentMonPath)
            ?? throw new FileNotFoundException("PresentMon не найден. Скачайте PresentMon (https://github.com/GameTechDev/PresentMon/releases), положите рядом с GameTune или укажите путь в настройках.")),
        _ => new EtwGameRunner(),
    };
}
