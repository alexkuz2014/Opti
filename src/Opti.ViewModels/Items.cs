using CommunityToolkit.Mvvm.ComponentModel;
using Opti.Core.Findings;
using Opti.Core.Fixes;
using Opti.Core.Strategy;
using Opti.Measure.Stats;

namespace Opti.ViewModels;

public sealed class GoalOption
{
    public required UserGoal Goal { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }
    /// <summary>Глиф шрифта Segoe Fluent Icons.</summary>
    public required string Icon { get; init; }

    public static IReadOnlyList<GoalOption> All { get; } = new[]
    {
        new GoalOption { Goal = UserGoal.LowLatency, Title = "Отклик", Description = "Соревновательные игры", Icon = "\uE945" },
        new GoalOption { Goal = UserGoal.Smoothness, Title = "Плавность", Description = "Ровные кадры без рывков", Icon = "\uE9E9" },
        new GoalOption { Goal = UserGoal.MaxFps, Title = "Максимум FPS", Description = "Больше кадров", Icon = "\uEC4A" },
        new GoalOption { Goal = UserGoal.Streaming, Title = "Стрим", Description = "OBS и запись", Icon = "\uE714" },
        new GoalOption { Goal = UserGoal.QuietCool, Title = "Тихо", Description = "Меньше шума и нагрева", Icon = "\uE9CA" },
    };
}

public sealed class LinkItem
{
    public required string Text { get; init; }
    public required string Url { get; init; }

    public static LinkItem From(string url) => new()
    {
        Url = url,
        Text = url.StartsWith("ms-settings:", StringComparison.OrdinalIgnoreCase) ? "Открыть параметры Windows"
             : Uri.TryCreate(url, UriKind.Absolute, out var u) ? "Источник: " + u.Host.Replace("www.", "") : url,
    };
}

public sealed class FindingItem
{
    public FindingItem(Finding f)
    {
        Finding = f;
        SeverityText = f.Severity switch { Severity.Critical => "КРИТИЧНО", Severity.Important => "ВАЖНО", _ => "СОВЕТ" };
        Color = f.Severity switch { Severity.Critical => Palette.Critical, Severity.Important => Palette.Important, _ => Palette.Advice };
    }
    public Finding Finding { get; }
    public string Title => Finding.Title;
    public string Explanation => Finding.Explanation;
    public string SeverityText { get; }
    public string Color { get; }
    public bool IsHardware => Finding.IsHardwareLimitation;
    public IReadOnlyList<string> Evidence => Finding.Evidence;
    public IReadOnlyList<string> ManualSteps => Finding.ManualSteps;
    public IReadOnlyList<string> Links => Finding.Links;
    public IReadOnlyList<LinkItem> LinkItems => Finding.Links.Select(LinkItem.From).ToList();
    public bool HasDetails => Evidence.Count + ManualSteps.Count + Links.Count > 0;
    public bool HasFix => Finding.FixIds.Count > 0;
    public string FixHint => HasFix ? "Можно исправить автоматически" : IsHardware ? "Аппаратное ограничение — программно не исправить" : "Совет: действие за вами";
}

public sealed partial class FixItem : ObservableObject
{
    public FixItem(PlannedFix p)
    {
        Planned = p;
        _isSelected = p.Selected && !p.ApplySeparately;
    }
    public PlannedFix Planned { get; }
    public IFix Fix => Planned.Fix;
    public string Id => Fix.Id;
    public string Title => Fix.Title;
    public string Explanation => Fix.Explanation;
    public string RiskText => Fix.Risk switch { RiskLevel.Safe => "безопасно", RiskLevel.Moderate => "умеренно", _ => "рискованно" };
    public string RiskColor => Fix.Risk switch { RiskLevel.Safe => Palette.Good, RiskLevel.Moderate => Palette.Warn, _ => Palette.Bad };
    public bool RequiresReboot => Fix.RequiresReboot;
    public bool IsAb => Planned.ApplySeparately;
    public string Reason => "Причина: " + string.Join(", ", Planned.TriggeredBy);
    public IReadOnlyList<string> Notes => Planned.Notes;
    public bool NeedsChoice => Fix.NeedsUserChoice;

    [ObservableProperty] private bool _isSelected;
}

public sealed partial class ChoiceItem : ObservableObject
{
    public required string Name { get; init; }
    public string? Detail { get; init; }
    [ObservableProperty] private bool _isChecked;
}

public sealed class LogItem
{
    public required string Title { get; init; }
    public required string Status { get; init; }
    public required string Color { get; init; }
    public IReadOnlyList<string> Details { get; init; } = Array.Empty<string>();
}

public sealed class RunSummary
{
    public RunSummary(MeasurementRun r)
    {
        Run = r;
        var m = Metrics.ComputeAll(r.FrameTimesMs);
        AvgFps = m[MetricId.AvgFps];
        Low1 = m[MetricId.Low1Fps];
        P99 = m[MetricId.FrameTimeP99];
    }
    public MeasurementRun Run { get; }
    public string Label => Run.Label;
    public double AvgFps { get; }
    public double Low1 { get; }
    public double P99 { get; }
    public string Text => $"{Run.Conditions.ProcessName}: {Run.FrameTimesMs.Count} кадров · {AvgFps:0.#} FPS · 1 % low {Low1:0.#} · p99 {P99:0.##} мс" +
                          (Run.IndependentFlipShare is { } f ? $" · Independent Flip {f * 100:0} %" : "");
    public bool Reliable => Run.IsReliable;
    public string Color => Run.IsReliable ? Palette.Good : Palette.Warn;
    public string Warning => Run.ThermalThrottled ? "троттлинг — прогон ненадёжен" : Run.FrameTimesMs.Count < 300 ? "мало кадров" : string.Join("; ", Run.Warnings);
}

public sealed class MetricCard
{
    public MetricCard(MetricComparison m)
    {
        Name = m.Metric.Name;
        Before = Fmt(m.Before);
        After = Fmt(m.After);
        Unit = m.Metric.Unit;
        Delta = double.IsNaN(m.DeltaPercent) ? "" : $"{(m.DeltaPercent >= 0 ? "+" : "")}{m.DeltaPercent:0.#} %";
        VerdictText = m.Verdict switch { Verdict.Better => "лучше, достоверно", Verdict.Worse => "хуже, достоверно", Verdict.NoChange => "в пределах шума", _ => "мало данных" };
        Color = m.Verdict switch { Verdict.Better => Palette.Good, Verdict.Worse => Palette.Bad, _ => Palette.Neutral };
        Ci = $"95 % ДИ: {m.BeforeCi.Low:0.##}–{m.BeforeCi.High:0.##} → {m.AfterCi.Low:0.##}–{m.AfterCi.High:0.##}";
    }
    private static string Fmt(double v) => double.IsNaN(v) ? "—" : v.ToString(v >= 100 ? "0" : "0.#");
    public string Name { get; }
    public string Before { get; }
    public string After { get; }
    public string Unit { get; }
    public string Delta { get; }
    public string VerdictText { get; }
    public string Color { get; }
    public string Ci { get; }
}
