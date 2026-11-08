using System.Diagnostics;
using System.Runtime.InteropServices;

namespace GameTune.Measure.Capture;

public sealed record TimerAccuracyResult(int Samples, double MeanMs, double P99Ms, double MaxMs, double? CurrentResolutionMs);

/// <summary>Микротест: насколько точно выполняется Sleep(1). Показывает влияние таймерных настроек.</summary>
public static class TimerAccuracyTest
{
    [DllImport("ntdll.dll")] private static extern int NtQueryTimerResolution(out uint min, out uint max, out uint current);

    public static TimerAccuracyResult Run(int samples = 1000)
    {
        var list = new double[samples];
        var sw = new Stopwatch();
        for (int i = 0; i < samples; i++)
        {
            sw.Restart();
            Thread.Sleep(1);
            list[i] = sw.Elapsed.TotalMilliseconds;
        }
        double? res = null;
        if (OperatingSystem.IsWindows())
        {
            try { if (NtQueryTimerResolution(out _, out _, out var cur) == 0) res = cur / 10000.0; } catch { /* нет ntdll */ }
        }
        Array.Sort(list);
        return new TimerAccuracyResult(samples, list.Average(), list[(int)(samples * 0.99) - 1], list[^1], res);
    }
}
