using GameTune.Core.Findings;
using GameTune.Core.Fixes;
using GameTune.Core.Profile;

namespace GameTune.Core.Strategy;

public sealed class PlannedFix
{
    public required IFix Fix { get; init; }
    /// <summary>Отмечено по умолчанию (рекомендовано).</summary>
    public bool Selected { get; set; }
    public List<string> TriggeredBy { get; init; } = new();
    public List<string> Notes { get; init; } = new();
    public int Order { get; set; }
    /// <summary>Применять отдельно и проверять A/B-замером.</summary>
    public bool ApplySeparately => Fix.RequiresAbTest || Fix.Risk == RiskLevel.Risky;
}

public sealed record ExcludedFix(string FixId, string Reason, List<string> TriggeredBy);

public sealed class StrategyPlan
{
    public required UserGoal Goal { get; init; }
    public required DeviceClassification Device { get; init; }
    public List<PlannedFix> Fixes { get; init; } = new();
    public List<ExcludedFix> Excluded { get; init; } = new();
    public List<Finding> AdviceOnly { get; init; } = new();
    public bool RequiresReboot => Fixes.Any(f => f.Selected && f.Fix.RequiresReboot);
}

/// <summary>
/// Превращает находки в план. Исправление попадает в план, только если сработал
/// его детектор И оно совместимо с профилем устройства и целью.
/// </summary>
public sealed class StrategyEngine
{
    private readonly IReadOnlyDictionary<string, IFix> _catalog;

    public StrategyEngine(IEnumerable<IFix> catalog) =>
        _catalog = catalog.ToDictionary(f => f.Id, StringComparer.OrdinalIgnoreCase);

    /// <summary>Исправления, несовместимые с целью, и причина.</summary>
    private static readonly Dictionary<UserGoal, Dictionary<string, string>> GoalConflicts = new()
    {
        [UserGoal.QuietCool] = new()
        {
            [FixIds.PowerSchemePerf] = "цель «Тихо и холодно» несовместима со схемой максимальной производительности",
            [FixIds.PowerAcTuning] = "цель «Тихо и холодно»: не снимаем энергосберегающие ограничения",
            [FixIds.PowerThrottlingOff] = "цель «Тихо и холодно»: Power Throttling экономит энергию и снижает нагрев",
        },
        [UserGoal.Streaming] = new(),
        [UserGoal.LowLatency] = new(),
        [UserGoal.Smoothness] = new(),
        [UserGoal.MaxFps] = new(),
    };

    public StrategyPlan Build(SystemProfile profile, IReadOnlyList<Finding> findings, UserGoal goal)
    {
        var device = DeviceClassification.From(profile);
        var plan = new StrategyPlan { Goal = goal, Device = device };

        // fixId -> находки, которые его предложили
        var triggered = new Dictionary<string, List<Finding>>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in findings)
        {
            if (f.FixIds.Count == 0) { plan.AdviceOnly.Add(f); continue; }
            foreach (var id in f.FixIds)
            {
                if (!triggered.TryGetValue(id, out var list)) triggered[id] = list = new();
                list.Add(f);
            }
        }

        foreach (var (fixId, sources) in triggered)
        {
            var sourceIds = sources.Select(s => s.Id).Distinct().ToList();
            if (!_catalog.TryGetValue(fixId, out var fix))
            {
                plan.Excluded.Add(new ExcludedFix(fixId, "исправление не реализовано в этой версии", sourceIds));
                continue;
            }
            if (GoalConflicts.TryGetValue(goal, out var conflicts) && conflicts.TryGetValue(fixId, out var why))
            {
                plan.Excluded.Add(new ExcludedFix(fixId, why, sourceIds));
                continue;
            }
            bool applicable;
            try { applicable = fix.IsApplicable(profile); } catch { applicable = false; }
            if (!applicable)
            {
                plan.Excluded.Add(new ExcludedFix(fixId, "не подходит для этого ПК", sourceIds));
                continue;
            }
            bool already;
            try { already = fix.IsAlreadyApplied(profile); } catch { already = false; }
            if (already)
            {
                plan.Excluded.Add(new ExcludedFix(fixId,
                    device.IsModifiedOs ? "уже применено (вероятно, сборкой Windows)" : "уже применено", sourceIds));
                continue;
            }

            var planned = new PlannedFix { Fix = fix, TriggeredBy = sourceIds };
            planned.Selected = fix.Risk != RiskLevel.Risky;
            if (fix.NeedsUserChoice)
            {
                planned.Selected = false;
                planned.Notes.Add("Выберите пункты вручную — программа изменит только отмеченные.");
            }
            if (fix.RequiresAbTest)
                planned.Notes.Add("Спорная настройка: будет проверена A/B-замером, останется лучший вариант.");
            if (fix.Risk == RiskLevel.Risky)
                planned.Notes.Add("Рискованно: включайте только осознанно.");
            if (fix.TouchesBcd && profile.Security.BitLockerOnSystemDrive == true)
            {
                planned.Selected = false;
                planned.Notes.Add("На системном диске включён BitLocker: изменение BCD может запросить ключ восстановления. Подготовьте ключ или приостановите BitLocker.");
            }
            if (device.IsModifiedOs && fix.Category is FixCategory.Timers or FixCategory.Experimental
                || device.IsModifiedOs && fix.Id == FixIds.MpoReset)
            {
                planned.Selected = false;
                planned.Notes.Add($"Значение, вероятно, задано сборкой {profile.Os.ModifiedBuild} намеренно — не отмечено по умолчанию.");
            }
            ApplyGoalPreferences(planned, goal, profile);
            plan.Fixes.Add(planned);
        }

        int order = 0;
        foreach (var p in plan.Fixes
                     .OrderBy(p => p.ApplySeparately ? 1 : 0)
                     .ThenBy(p => (int)p.Fix.Risk)
                     .ThenBy(p => CategoryOrder(p.Fix.Category))
                     .ThenBy(p => p.Fix.Priority)
                     .ThenBy(p => p.Fix.Id, StringComparer.Ordinal))
            p.Order = ++order;
        plan.Fixes.Sort((a, b) => a.Order.CompareTo(b.Order));
        return plan;
    }

    private static void ApplyGoalPreferences(PlannedFix p, UserGoal goal, SystemProfile profile)
    {
        switch (p.Fix.Id)
        {
            case FixIds.GameDvrOff when goal == UserGoal.Streaming:
                p.Notes.Add("Фоновая запись Xbox Game Bar дублирует OBS; если вы пользуетесь именно Game Bar для записи — снимите галочку.");
                break;
            case FixIds.TimerResGlobal when goal is not (UserGoal.LowLatency or UserGoal.Smoothness):
                p.Selected = false;
                p.Notes.Add("Имеет смысл в основном для задержки и плавности.");
                break;
            case FixIds.PowerThrottlingOff:
                if (goal is not (UserGoal.MaxFps or UserGoal.LowLatency)) p.Selected = false;
                if (profile.Device.IsLaptop) p.Notes.Add("На ноутбуке повышает нагрев и расход батареи.");
                break;
        }
    }

    /// <summary>Сначала бесплатные по эффекту (экран, GPU, OBS), затем системные.</summary>
    private static int CategoryOrder(FixCategory c) => c switch
    {
        FixCategory.Display => 0,
        FixCategory.GpuAssignment => 1,
        FixCategory.Obs => 2,
        FixCategory.GameSettings => 3,
        FixCategory.Background => 4,
        FixCategory.Power => 5,
        FixCategory.Network => 6,
        FixCategory.Timers => 7,
        _ => 8,
    };
}
