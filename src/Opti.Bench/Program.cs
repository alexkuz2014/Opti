using System.Diagnostics;
using Opti.Core.Json;
using Opti.Measure.Stats;

namespace Opti.Bench;

/// <summary>
/// Синтетический тест Opti: фиксированная D3D11-сцена и фиксированная длительность — полная воспроизводимость.
/// opti.bench --out run.json [--duration 60] [--warmup 5] [--instances 8192] [--gpu-load 24] [--cpu-load 1.0] [--window 1280x720] [--gpu high|default]
/// Код выхода: 0 — успех, 2 — прервано (Esc), 1 — ошибка.
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        string? Get(string key)
        {
            int i = Array.IndexOf(args, "--" + key);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }
        var outPath = Get("out");
        double duration = double.Parse(Get("duration") ?? "60", System.Globalization.CultureInfo.InvariantCulture);
        double warmup = double.Parse(Get("warmup") ?? "5", System.Globalization.CultureInfo.InvariantCulture);
        int instances = int.Parse(Get("instances") ?? "8192");
        float load = float.Parse(Get("gpu-load") ?? "24", System.Globalization.CultureInfo.InvariantCulture);
        double cpuLoadMs = double.Parse(Get("cpu-load") ?? "1.0", System.Globalization.CultureInfo.InvariantCulture);
        string label = Get("label") ?? "синтетика";
        int w = 0, h = 0;
        if (Get("window") is { } win && win.Split('x') is [var ws, var hs]) { w = int.Parse(ws); h = int.Parse(hs); }

        try
        {
            using var window = new Win32Window("Opti — синтетический тест", w, h);
            using var renderer = new Renderer(window.Handle, window.Width, window.Height, Get("gpu") != "default");
            uint gridX = 32, gridY = (uint)Math.Max(1, Math.Min(32, instances / 32));

            var frames = new List<double>(200_000);
            var sw = Stopwatch.StartNew();
            long last = sw.ElapsedTicks;
            double total = duration + warmup;
            double cpuSink = 0;
            while (!window.Closed && sw.Elapsed.TotalSeconds < total)
            {
                window.PumpMessages();
                // «Логика игры»: фиксированная работа CPU на кадр (детерминированная)
                cpuSink += SimulateCpuWork(cpuLoadMs);
                renderer.Render((float)sw.Elapsed.TotalSeconds, instances, load, gridX, gridY);
                renderer.Present();
                long now = sw.ElapsedTicks;
                frames.Add((now - last) * 1000.0 / Stopwatch.Frequency);
                last = now;
            }
            if (window.Escaped) return 2;
            GC.KeepAlive(cpuSink);

            var run = new MeasurementRun
            {
                Label = label,
                Source = "Opti.Bench",
                FrameTimesMs = MeasurementRun.TrimWarmup(frames, warmup),
                Conditions = new RunConditions
                {
                    ProcessName = "Opti.Bench",
                    Scenario = $"синтетика: {instances} объектов, GPU {load}, CPU {cpuLoadMs} мс, {renderer.AdapterName}",
                    Width = window.Width, Height = window.Height,
                    OnAcPower = OperatingSystem.IsWindows() ? Opti.Native.PowerNative.GetStatus().OnAc : null,
                },
            };
            if (!renderer.Tearing) run.Warnings.Add("Система не поддерживает вывод с разрывами — FPS ограничен частотой экрана.");
            if (outPath is not null) File.WriteAllText(outPath, OptiJson.Serialize(run));
            return 0;
        }
        catch (Exception ex)
        {
            if (outPath is not null) File.WriteAllText(outPath + ".error.txt", ex.ToString());
            return 1;
        }
    }

    /// <summary>Детерминированная нагрузка на CPU ~ cpuMs на кадр (калибруется по количеству итераций, а не по времени).</summary>
    private static double SimulateCpuWork(double cpuMs)
    {
        int iterations = (int)(cpuMs * 120_000);
        double x = 0.5;
        for (int i = 0; i < iterations; i++) x = x * 1.0000001 + Math.Sin(x) * 1e-7;
        return x;
    }
}
