using Opti.Core.Fixes;

namespace Opti.Fixes.Services;

public sealed class RollbackReport
{
    public List<ChangeRecord> Restored { get; } = new();
    /// <summary>Настройку позже изменило другое активное исправление — оставлена как есть.</summary>
    public List<ChangeRecord> SkippedSupersededBy { get; } = new();
    /// <summary>Пользователь (или Windows) сам поменял значение после Opti — откат его не затирает.</summary>
    public List<ChangeRecord> SkippedChangedExternally { get; } = new();
    public List<string> Errors { get; } = new();
}

/// <summary>Откат по исправлению, по сессии или полный. Восстанавливает исходные значения в обратном порядке.</summary>
public sealed class RollbackService
{
    private readonly IBackendRegistry _backends;
    private readonly IChangeJournal _journal;

    public RollbackService(IBackendRegistry backends, IChangeJournal journal)
    {
        _backends = backends;
        _journal = journal;
    }

    public RollbackReport RollbackFix(string fixId, string? sessionId = null) =>
        RollbackRecords(Active().Where(r => string.Equals(r.FixId, fixId, StringComparison.OrdinalIgnoreCase)
                                            && (sessionId is null || r.SessionId == sessionId)));

    public RollbackReport RollbackSession(string sessionId) =>
        RollbackRecords(Active().Where(r => r.SessionId == sessionId));

    public RollbackReport RollbackAll() => RollbackRecords(Active());

    private IEnumerable<ChangeRecord> Active() => _journal.GetAll().Where(r => !r.RolledBack);

    public RollbackReport RollbackRecords(IEnumerable<ChangeRecord> records)
    {
        var report = new RollbackReport();
        var toRollback = records.Where(r => !r.RolledBack).OrderByDescending(r => r.Id).ToList();
        var ids = toRollback.Select(r => r.Id).ToHashSet();
        var allActive = _journal.GetAll().Where(r => !r.RolledBack).ToList();

        foreach (var rec in toRollback)
        {
            // Если ту же настройку позже изменило исправление, которое не откатывается, — не трогаем значение.
            bool superseded = allActive.Any(o => o.Id > rec.Id && !ids.Contains(o.Id) && o.Type == rec.Type
                                                 && string.Equals(o.Target, rec.Target, StringComparison.OrdinalIgnoreCase));
            try
            {
                if (!superseded && ChangedExternally(rec))
                {
                    report.SkippedChangedExternally.Add(rec);
                    _journal.MarkRolledBack(rec.Id, DateTimeOffset.Now);
                    continue;
                }
                if (superseded)
                    report.SkippedSupersededBy.Add(rec);
                else
                    _backends.Get(rec.Type).Write(rec.Target, rec.Before);
                _journal.MarkRolledBack(rec.Id, DateTimeOffset.Now);
                if (!superseded) report.Restored.Add(rec);
            }
            catch (Exception ex)
            {
                report.Errors.Add($"{rec.FixId} {rec.Type} {rec.Target}: {ex.Message}");
            }
        }
        return report;
    }

    /// <summary>Текущее значение не равно ни тому, что поставил Opti, ни исходному: это чужой выбор, перезаписывать молча нельзя.</summary>
    private bool ChangedExternally(ChangeRecord rec)
    {
        if (rec.Type == ChangeType.File) return false;
        try
        {
            var current = _backends.Get(rec.Type).Read(rec.Target);
            return current != rec.After && current != rec.Before;
        }
        catch { return false; }   // не прочитать — поведение как раньше
    }
}
