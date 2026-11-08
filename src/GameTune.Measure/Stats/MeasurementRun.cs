namespace GameTune.Measure.Stats;

/// <summary>Условия замера: при их различии «до» и «после» сравнение помечается как ненадёжное.</summary>
public sealed record RunConditions
{
    public string? ProcessName { get; init; }
    public string? Scenario { get; init; }
    public int? Width { get; init; }
    public int? Height { get; init; }
    public int? RefreshHz { get; init; }
    public string? DisplayDevice { get; init; }
    public bool? OnAcPower { get; init; }
    public string? PowerScheme { get; init; }
}

public sealed class MeasurementRun
{
    public required string Label { get; init; }            // «до #1», «после #2»
    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.Now;
    public RunConditions Conditions { get; init; } = new();
    /// <summary>Интервалы между кадрами, мс (MsBetweenPresents), без прогрева.</summary>
    public List<double> FrameTimesMs { get; init; } = new();
    /// <summary>Доля кадров в режиме Independent Flip (0..1), если известна.</summary>
    public double? IndependentFlipShare { get; set; }
    public bool ThermalThrottled { get; set; }
    public List<string> Warnings { get; init; } = new();

    public bool IsReliable => !ThermalThrottled && FrameTimesMs.Count >= 300;
    public double DurationSec => FrameTimesMs.Sum() / 1000.0;

    /// <summary>Отбрасывает первые warmupSec секунд (компиляция шейдеров, подгрузка).</summary>
    public static List<double> TrimWarmup(IReadOnlyList<double> frameTimesMs, double warmupSec)
    {
        double acc = 0;
        int i = 0;
        while (i < frameTimesMs.Count && acc < warmupSec * 1000) acc += frameTimesMs[i++];
        return frameTimesMs.Skip(i).ToList();
    }

    public static List<double> FromTimestamps(IReadOnlyList<double> presentTimestampsMs)
    {
        var list = new List<double>(Math.Max(0, presentTimestampsMs.Count - 1));
        for (int i = 1; i < presentTimestampsMs.Count; i++)
        {
            var dt = presentTimestampsMs[i] - presentTimestampsMs[i - 1];
            if (dt > 0) list.Add(dt);
        }
        return list;
    }
}
