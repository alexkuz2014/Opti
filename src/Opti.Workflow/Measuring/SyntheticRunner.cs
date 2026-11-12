using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using Opti.Core.Json;
using Opti.Measure.Stats;

namespace Opti.Workflow.Measuring;

/// <summary>Синтетический тест Opti (D3D11, встроен в программу): одна и та же сцена и длительность — полная воспроизводимость.</summary>
[SupportedOSPlatform("windows")]
/// <summary>Замер, который умеет подбирать нагрузку под ПК и запоминать её на всю сессию.</summary>
public interface ILoadCalibrated
{
    /// <summary>Подбирать нагрузку перед первым прогоном, если она ещё не задана.</summary>
    bool AutoCalibrate { get; set; }
    double? CalibratedGpuLoad { get; set; }
}

public sealed class SyntheticRunner : IMeasureRunner, ILoadCalibrated
{
    private readonly string _exe;
    private readonly string _prefixArgs;
    /// <param name="exe">Программа, которая умеет запускать тест (Opti.exe или opti-cli.exe).</param>
    /// <param name="prefixArgs">Ключ запуска теста.</param>
    public SyntheticRunner(string exe, string prefixArgs = "--bench")
    {
        _exe = exe;
        _prefixArgs = prefixArgs;
    }
    public string Name => "Синтетический тест Opti";

    public int Instances { get; init; } = 8192;
    public double GpuLoad { get; init; } = 24;
    public double CpuLoadMs { get; init; } = 1.0;

    /// <summary>Тест встроен в сам Opti.exe / opti-cli.exe: запускаем текущую программу с ключом --bench.</summary>
    public static string? Locate() => Environment.ProcessPath is { } p && File.Exists(p) ? p : null;

    public bool AutoCalibrate { get; set; }
    public double? CalibratedGpuLoad { get; set; }
    /// <summary>К какой частоте кадров подбирать нагрузку: на 250+ FPS разница в 1–2 % тонет в шуме, около 80 каждый кадр «весит» больше.</summary>
    public const double TargetFps = 80;
    public const double MaxGpuLoad = 4000;

    /// <summary>Нагрузка шейдера растёт линейно, поэтому новую считаем пропорцией; слабым ПК (меньше 110 FPS) оставляем базовую.</summary>
    public static double ScaleLoad(double baseLoad, double measuredFps) =>
        measuredFps <= 110 || double.IsNaN(measuredFps) ? baseLoad : Math.Min(MaxGpuLoad, Math.Round(baseLoad * measuredFps / TargetFps));

    public async Task<MeasurementRun> RunAsync(MeasureRequest req, IProgress<MeasureProgress>? progress = null, CancellationToken ct = default)
    {
        if (AutoCalibrate && CalibratedGpuLoad is null)
        {
            // Короткая проба на базовой нагрузке, затем вторая — на подобранной: если видеокарта всё ещё слишком быстра, добавляем ещё
            double load = GpuLoad;
            for (int i = 0; i < 2; i++)
            {
                var probe = await RunBenchAsync(new MeasureRequest("подбор нагрузки", 4, 2, null, "синтетика"), load, null, ct);
                double fps = probe.FrameTimesMs.Count > 0 ? 1000.0 / probe.FrameTimesMs.Average() : double.NaN;
                var next = ScaleLoad(load, fps);
                if (next <= load) break;
                load = next;
            }
            CalibratedGpuLoad = load;
        }
        return await RunBenchAsync(req, CalibratedGpuLoad ?? GpuLoad, progress, ct);
    }

    private async Task<MeasurementRun> RunBenchAsync(MeasureRequest req, double gpuLoad, IProgress<MeasureProgress>? progress, CancellationToken ct)
    {
        var outFile = Path.Combine(Path.GetTempPath(), $"opti-bench-{Guid.NewGuid():N}.json");
        var args = _prefixArgs + " " + string.Format(CultureInfo.InvariantCulture,
            "--out \"{0}\" --duration {1} --warmup {2} --instances {3} --gpu-load {4} --cpu-load {5} --label \"{6}\"",
            outFile, req.DurationSec, req.WarmupSec, Instances, gpuLoad, CpuLoadMs, req.Label.Replace("\"", "'"));
        var probe = new ThrottleProbe();
        using var p = Process.Start(new ProcessStartInfo(_exe, args) { UseShellExecute = false })
                      ?? throw new InvalidOperationException("Не удалось запустить Opti.Bench");
        var sw = Stopwatch.StartNew();
        double total = req.DurationSec + req.WarmupSec;
        int tick = 0;
        while (!p.HasExited)
        {
            if (ct.IsCancellationRequested) { try { p.Kill(true); } catch { } ct.ThrowIfCancellationRequested(); }
            if (sw.Elapsed.TotalSeconds > total + 60) { try { p.Kill(true); } catch { } throw new TimeoutException("Синтетический тест завис."); }
            await Task.Delay(500, CancellationToken.None);
            if (++tick % 10 == 0) probe.SampleInBackground();
            progress?.Report(new MeasureProgress(Math.Min(sw.Elapsed.TotalSeconds, total), total, Array.Empty<double>(), "Opti.Bench", sw.Elapsed.TotalSeconds < req.WarmupSec));
        }
        if (p.ExitCode == 2) throw new OperationCanceledException("Синтетический тест прерван клавишей Esc.");
        if (p.ExitCode != 0 || !File.Exists(outFile))
        {
            var err = File.Exists(outFile + ".error.txt") ? File.ReadAllText(outFile + ".error.txt") : $"код {p.ExitCode}";
            throw new InvalidOperationException("Синтетический тест завершился с ошибкой: " + err.Split('\n')[0]);
        }
        var run = OptiJson.Deserialize<MeasurementRun>(File.ReadAllText(outFile));
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
    public static IMeasureRunner Create(Sessions.MeasureMode mode, OptiSettings settings) => mode switch
    {
        Sessions.MeasureMode.Synthetic => new SyntheticRunner(SyntheticRunner.Locate()
            ?? throw new FileNotFoundException("Не удалось определить путь к программе для запуска теста.")),
        Sessions.MeasureMode.PresentMon => new PresentMonRunner(PresentMonRunner.Locate(settings.PresentMonPath)
            ?? throw new FileNotFoundException("PresentMon не найден. Скачайте PresentMon (https://github.com/GameTechDev/PresentMon/releases), положите рядом с Opti или укажите путь в настройках.")),
        _ => new EtwGameRunner(),
    };
}
