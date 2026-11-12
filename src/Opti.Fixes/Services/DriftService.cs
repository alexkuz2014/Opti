using Opti.Core.Fixes;

namespace Opti.Fixes.Services;

/// <summary>Изменённая Opti настройка, значение которой потом поменялось (обновление Windows, установка драйвера, ручная правка).</summary>
public sealed record DriftItem(ChangeRecord Record, StateValue Current);

/// <summary>
/// Сверяет журнал с системой: Windows и установщики драйверов иногда молча сбрасывают частоту экрана, схему питания и другие настройки.
/// Проверка только читает. Восстановление идёт тем же путём, что и применение: через <see cref="ChangeContext"/>, с записью в журнал.
/// </summary>
public sealed class DriftService
{
    private readonly IBackendRegistry _backends;
    private readonly IChangeJournal _journal;

    public DriftService(IBackendRegistry backends, IChangeJournal journal)
    {
        _backends = backends;
        _journal = journal;
    }

    /// <summary>Для каждой настройки берётся последнее действующее изменение: его «после» и должно быть в системе сейчас.</summary>
    public IReadOnlyList<DriftItem> Check()
    {
        var result = new List<DriftItem>();
        var latest = _journal.GetAll().Where(r => !r.RolledBack)
            .GroupBy(r => (r.Type, Target: r.Target.ToLowerInvariant()))
            .Select(g => g.OrderBy(r => r.Id).Last());
        foreach (var r in latest)
        {
            try
            {
                var current = _backends.Get(r.Type).Read(r.Target);
                if (current != r.After) result.Add(new DriftItem(r, current));
            }
            catch { /* настройку сейчас не прочитать (нет прав, устройство отключено) — выводов не делаем */ }
        }
        return result;
    }

    /// <summary>Возвращает настройки к значениям, которые поставил Opti. Исходные значения в журнале при этом не затираются.</summary>
    public int Restore(IEnumerable<DriftItem> items)
    {
        int restored = 0;
        foreach (var grp in items.GroupBy(i => (i.Record.SessionId, i.Record.FixId)))
        {
            var ctx = new ChangeContext(_backends, _journal, grp.Key.SessionId, grp.Key.FixId, new Opti.Core.Profile.SystemProfile());
            foreach (var item in grp)
            {
                try { if (ctx.Set(item.Record.Type, item.Record.Target, item.Record.After) is not null) restored++; }
                catch (ChangeAbortedException) { /* не получилось — остальные всё равно пробуем */ }
            }
        }
        return restored;
    }
}
