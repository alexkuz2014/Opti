namespace Opti.Measure.Stats;

public enum Verdict { Better, NoChange, Worse, Insufficient }

public sealed record ConfidenceInterval(double Low, double High)
{
    public bool Overlaps(ConfidenceInterval o) => Low <= o.High && o.Low <= High;
}

public sealed class MetricComparison
{
    public required MetricDef Metric { get; init; }
    public required double Before { get; init; }
    public required double After { get; init; }
    public required ConfidenceInterval BeforeCi { get; init; }
    public required ConfidenceInterval AfterCi { get; init; }
    public required double[] BeforeRuns { get; init; }
    public required double[] AfterRuns { get; init; }
    /// <summary>Разброс между прогонами (максимум размахов «до» и «после»).</summary>
    public required double RunSpread { get; init; }
    public required Verdict Verdict { get; init; }

    public double DeltaPercent => Before == 0 ? 0 : (After - Before) / Before * 100.0;

    public string Format()
    {
        string v = Verdict switch
        {
            Verdict.Better => "лучше, достоверно",
            Verdict.Worse => "хуже, достоверно",
            Verdict.NoChange => "в пределах шума",
            _ => "мало данных",
        };
        return $"{Metric.Name}: {Before:0.#} → {After:0.#} {Metric.Unit} ({DeltaPercent:+0.#;-0.#;0} %, {v})";
    }
}

public sealed class ComparisonResult
{
    public List<MetricComparison> Metrics { get; } = new();
    public List<string> Warnings { get; } = new();
    public bool Reliable { get; set; } = true;

    /// <summary>Общий вердикт: хуже, если хоть одна ключевая метрика достоверно хуже; лучше — если есть улучшение и нет ухудшений.</summary>
    public Verdict Overall
    {
        get
        {
            var key = Metrics.Where(m => m.Metric.Id is MetricId.AvgFps or MetricId.Low1Fps or MetricId.FrameTimeP99).ToList();
            if (key.Count == 0 || key.All(m => m.Verdict == Verdict.Insufficient)) return Verdict.Insufficient;
            if (key.Any(m => m.Verdict == Verdict.Worse)) return Verdict.Worse;
            if (key.Any(m => m.Verdict == Verdict.Better)) return Verdict.Better;
            return Verdict.NoChange;
        }
    }
}

/// <summary>
/// Честное сравнение «до / после»:
///  • среднее по прогонам и разброс между ними;
///  • 95 % доверительный интервал блочным бутстрепом (блоки ≈ 1 с, чтобы учесть автокорреляцию кадров);
///  • «лучше/хуже» только если интервалы не пересекаются И разница больше разброса между прогонами.
/// </summary>
public sealed class Comparer
{
    public int BootstrapIterations { get; init; } = 1000;
    public double BlockMs { get; init; } = 1000;
    public int Seed { get; init; } = 12345;

    public ComparisonResult Compare(IReadOnlyList<MeasurementRun> before, IReadOnlyList<MeasurementRun> after)
    {
        var result = new ComparisonResult();
        result.Warnings.AddRange(CheckConditions(before, after));

        var b = SelectRuns(before, "до", result);
        var a = SelectRuns(after, "после", result);
        if (result.Warnings.Count > 0) result.Reliable = false;

        var bPerRun = b.Select(r => Metrics.ComputeAll(r.FrameTimesMs)).ToList();
        var aPerRun = a.Select(r => Metrics.ComputeAll(r.FrameTimesMs)).ToList();
        var bCis = BootstrapAll(b);
        var aCis = BootstrapAll(a);

        foreach (var def in Metrics.All)
        {
            var bRuns = bPerRun.Select(m => m[def.Id]).ToArray();
            var aRuns = aPerRun.Select(m => m[def.Id]).ToArray();
            if (bRuns.Length == 0 || aRuns.Length == 0 || bRuns.Concat(aRuns).Any(double.IsNaN))
            {
                result.Metrics.Add(new MetricComparison
                {
                    Metric = def, Before = double.NaN, After = double.NaN, BeforeCi = new(double.NaN, double.NaN),
                    AfterCi = new(double.NaN, double.NaN), BeforeRuns = bRuns, AfterRuns = aRuns, RunSpread = double.NaN,
                    Verdict = Verdict.Insufficient,
                });
                continue;
            }

            double bMean = bRuns.Average(), aMean = aRuns.Average();
            var bCi = bCis[def.Id];
            var aCi = aCis[def.Id];
            double spread = Math.Max(Range(bRuns), Range(aRuns));
            double diff = aMean - bMean;
            bool significant = !bCi.Overlaps(aCi) && Math.Abs(diff) > spread;
            bool improved = def.HigherIsBetter ? diff > 0 : diff < 0;
            var verdict = !significant ? Verdict.NoChange : improved ? Verdict.Better : Verdict.Worse;

            result.Metrics.Add(new MetricComparison
            {
                Metric = def, Before = bMean, After = aMean, BeforeCi = bCi, AfterCi = aCi,
                BeforeRuns = bRuns, AfterRuns = aRuns, RunSpread = spread, Verdict = verdict,
            });
        }
        return result;
    }

    private static List<MeasurementRun> SelectRuns(IReadOnlyList<MeasurementRun> runs, string label, ComparisonResult result)
    {
        var reliable = runs.Where(r => r.IsReliable).ToList();
        foreach (var r in runs.Where(r => !r.IsReliable))
            result.Warnings.Add($"Прогон «{r.Label}» ненадёжен: " + (r.ThermalThrottled ? "был троттлинг" : "слишком мало кадров") +
                                (reliable.Count > 0 ? " — исключён из расчёта." : "."));
        if (runs.Count < 2) result.Warnings.Add($"Для «{label}» меньше двух прогонов — разброс между прогонами не оценить.");
        return reliable.Count > 0 ? reliable : runs.ToList();
    }

    public static IEnumerable<string> CheckConditions(IReadOnlyList<MeasurementRun> before, IReadOnlyList<MeasurementRun> after)
    {
        var all = before.Concat(after).Select(r => r.Conditions).ToList();
        if (all.Count == 0) yield break;
        string? Diff<T>(string name, Func<RunConditions, T?> sel)
        {
            var vals = all.Select(sel).Where(v => v is not null).Distinct().ToList();
            return vals.Count > 1 ? $"Разные условия замера: {name} ({string.Join(" / ", vals)})" : null;
        }
        var checks = new[]
        {
            Diff("процесс", c => c.ProcessName),
            Diff("сценарий", c => c.Scenario),
            Diff("разрешение", c => c.Width is null ? null : $"{c.Width}×{c.Height}"),
            Diff("экран", c => c.DisplayDevice),
            Diff("питание", c => c.OnAcPower is null ? null : c.OnAcPower.Value ? "от сети" : "от батареи"),
        };
        foreach (var c in checks) if (c is not null) yield return c;
        if (all.Any(c => c.OnAcPower == false)) yield return "Замер шёл от батареи — результаты могут быть занижены и нестабильны.";
    }

    public ConfidenceInterval BootstrapCi(MetricId id, IReadOnlyList<MeasurementRun> runs) => BootstrapAll(runs)[id];

    /// <summary>
    /// Блочный бутстреп по объединённым прогонам: блоки ≈ 1 с выбираются с возвращением, пока не наберётся
    /// исходное число кадров. Детерминирован (фиксированный seed). Возвращает 95 % интервал для каждой метрики.
    /// </summary>
    public Dictionary<MetricId, ConfidenceInterval> BootstrapAll(IReadOnlyList<MeasurementRun> runs)
    {
        var blocks = runs.SelectMany(r => MakeBlocks(r.FrameTimesMs)).ToList();
        if (blocks.Count < 2)
        {
            var point = Metrics.ComputeAll(runs.SelectMany(r => r.FrameTimesMs).ToList());
            return point.ToDictionary(kv => kv.Key, kv => new ConfidenceInterval(kv.Value, kv.Value));
        }
        int totalFrames = blocks.Sum(b => b.Length);
        int maxBlock = blocks.Max(b => b.Length);
        var rng = new Random(Seed);
        var stats = Metrics.All.ToDictionary(m => m.Id, _ => new double[BootstrapIterations]);
        var buffer = new double[totalFrames + maxBlock];
        for (int i = 0; i < BootstrapIterations; i++)
        {
            int count = 0;
            while (count < totalFrames)
            {
                var blk = blocks[rng.Next(blocks.Count)];
                Array.Copy(blk, 0, buffer, count, blk.Length);
                count += blk.Length;
            }
            var sample = buffer.AsSpan(0, count).ToArray();
            Array.Sort(sample);
            foreach (var (id, v) in Metrics.ComputeAllSorted(sample)) stats[id][i] = v;
        }
        return stats.ToDictionary(kv => kv.Key, kv =>
        {
            Array.Sort(kv.Value);
            return new ConfidenceInterval(Metrics.PercentileSorted(kv.Value, 2.5), Metrics.PercentileSorted(kv.Value, 97.5));
        });
    }

    private IEnumerable<double[]> MakeBlocks(IReadOnlyList<double> frames)
    {
        var cur = new List<double>();
        double acc = 0;
        foreach (var f in frames)
        {
            cur.Add(f);
            acc += f;
            if (acc >= BlockMs) { yield return cur.ToArray(); cur.Clear(); acc = 0; }
        }
        if (cur.Count > 0) yield return cur.ToArray();
    }

    private static double Range(double[] v) => v.Length < 2 ? 0 : v.Max() - v.Min();
}
