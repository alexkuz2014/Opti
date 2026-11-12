using Opti.Core.Profile;

namespace Opti.Core.Fixes;

/// <summary>Насколько надёжно известно, что настройка помогает. Показывается рядом с каждым исправлением и твиком.</summary>
public enum EvidenceLevel
{
    /// <summary>Исправляет заведомо неверное состояние или документированное поведение: эффект предсказуем.</summary>
    Solid = 0,
    /// <summary>Эффект есть, но зависит от ПК и игры — проверяйте замером.</summary>
    Varies = 1,
    /// <summary>Часто советуют, но надёжных измерений нет или они противоречат друг другу.</summary>
    Hypothesis = 2,
}

public enum RiskLevel { Safe = 0, Moderate = 1, Risky = 2 }

public enum FixCategory
{
    Display = 0,
    GpuAssignment = 1,
    Obs = 2,
    Background = 3,
    Power = 4,
    GameSettings = 5,
    Network = 6,
    Timers = 7,
    Experimental = 8,
    /// <summary>Возврат стандартных значений реестра после «оптимизаторов».</summary>
    Registry = 9,
}

public interface IFix
{
    string Id { get; }
    string Title { get; }
    /// <summary>Простым языком: что и зачем.</summary>
    string Explanation { get; }
    RiskLevel Risk { get; }
    bool RequiresReboot { get; }
    FixCategory Category { get; }

    /// <summary>Эффект спорный — оставлять только по результату A/B-замера.</summary>
    EvidenceLevel Evidence => EvidenceLevel.Solid;
    bool RequiresAbTest => false;
    /// <summary>Затрагивает BCD (нужна проверка BitLocker).</summary>
    bool TouchesBcd => false;
    /// <summary>Пользователь должен сам выбрать пункты (например, автозагрузку) — по умолчанию не отмечено.</summary>
    bool NeedsUserChoice => false;
    /// <summary>Порядок внутри одной категории: меньше — раньше.</summary>
    int Priority => 0;

    bool IsApplicable(SystemProfile p);
    bool IsAlreadyApplied(SystemProfile p);
    IReadOnlyList<ChangeRecord> Apply(ChangeContext ctx);
}
