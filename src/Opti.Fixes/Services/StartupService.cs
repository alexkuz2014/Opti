using Opti.Core.Fixes;
using Opti.Core.Profile;

namespace Opti.Fixes.Services;

/// <summary>
/// Включение и выключение программ автозагрузки переключателем. Тем же способом, что «Диспетчер задач»: флаг в StartupApproved,
/// сама программа не удаляется. Выключение пишется в журнал; включение возвращает исходное значение из журнала.
/// </summary>
public sealed class StartupService
{
    public const string SessionId = "startup";
    private readonly IBackendRegistry _backends;
    private readonly IChangeJournal _journal;

    public StartupService(IBackendRegistry backends, IChangeJournal journal)
    {
        _backends = backends;
        _journal = journal;
    }

    public void Disable(StartupItem item)
    {
        if (item.ApprovedKey is null) throw new InvalidOperationException("Для этой программы нельзя изменить автозагрузку.");
        if (!_journal.GetSessions().Any(s => s.SessionId == SessionId))
            _journal.SaveSession(new SessionInfo { SessionId = SessionId, Goal = "Автозагрузка", Notes = "Автозагрузка" });
        var ctx = new ChangeContext(_backends, _journal, SessionId, FixIds.StartupDisable, new SystemProfile());
        // 03 00 00 00 + FILETIME — так помечает отключённые пункты Диспетчер задач
        var disabled = "03000000" + Convert.ToHexString(BitConverter.GetBytes(DateTime.UtcNow.ToFileTimeUtc()));
        ctx.Set(ChangeType.Registry, item.ApprovedKey, new StateValue(true, "Binary", disabled));
    }

    public void Enable(StartupItem item)
    {
        if (item.ApprovedKey is null) throw new InvalidOperationException("Для этой программы нельзя изменить автозагрузку.");
        var active = _journal.GetAll().Where(r => !r.RolledBack && r.Type == ChangeType.Registry && r.Target.Equals(item.ApprovedKey, StringComparison.OrdinalIgnoreCase)).ToList();
        if (active.Count > 0)
        {
            var report = new RollbackService(_backends, _journal).RollbackRecords(active);
            if (report.Errors.Count > 0) throw new InvalidOperationException(string.Join("; ", report.Errors));
            return;
        }
        // Выключено не Opti (например, в Диспетчере задач): включаем так же, как он — флагом «включено»
        var ctx = new ChangeContext(_backends, _journal, SessionId, FixIds.StartupDisable, new SystemProfile());
        ctx.Set(ChangeType.Registry, item.ApprovedKey, new StateValue(true, "Binary", "02" + new string('0', 22)));
    }
}
