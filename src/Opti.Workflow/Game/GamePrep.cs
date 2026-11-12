namespace Opti.Workflow.Game;

/// <summary>Программа или группа процессов с одним именем (chrome ×58) в списке «Перед игрой».</summary>
public sealed record AppGroup(string Name, int Count, long Bytes, IReadOnlyList<int> Pids);

public sealed record CloseResult(int Closed, int Failed, long FreedBytes);

/// <summary>
/// «Перед игрой»: показывает, что занимает память в фоне, и по выбору пользователя закрывает.
/// Системные, защитные и драйверные процессы не показываются и не закрываются никогда.
/// «Очистка оперативной памяти» утилитами — миф: Windows и так освобождает память по требованию, поэтому здесь только закрытие программ.
/// </summary>
public sealed class GamePrep
{
    private readonly IProcessControl _procs;
    public GamePrep(IProcessControl procs) => _procs = procs;

    /// <summary>Не показываем и не трогаем: система, оболочка, защита, драйверы, сам Opti, средства разработки и сами игры.</summary>
    public static readonly HashSet<string> Protected = new(StringComparer.OrdinalIgnoreCase)
    {
        "System", "Idle", "Registry", "Memory Compression", "smss", "csrss", "wininit", "winlogon", "services", "lsass", "lsm", "svchost", "dwm", "fontdrvhost",
        "explorer", "sihost", "taskhostw", "ctfmon", "RuntimeBroker", "ShellExperienceHost", "StartMenuExperienceHost", "SearchHost", "SearchApp", "TextInputHost",
        "audiodg", "spoolsv", "dllhost", "conhost", "WmiPrvSE", "MsMpEng", "NisSrv", "SecurityHealthService", "SecurityHealthSystray", "MpDefenderCoreService",
        "Opti", "Opti.Dev", "opti-cli", "dotnet", "cmd", "powershell", "pwsh", "WindowsTerminal", "Taskmgr",
        "nvcontainer", "NVDisplay.Container", "NVIDIA Web Helper", "RadeonSoftware", "AMDRSServ", "cncmd", "igfxEM", "igfxCUIService", "RtkAudUService64", "RtkNGUI64",
        "GTA5", "GTAV", "altv", "Majestic", "RockstarService",
        "obs64", "obs32", "msedgewebview2",
    };

    public IReadOnlyList<AppGroup> Scan(int top = 20, long minBytes = 40L * 1024 * 1024)
    {
        int self = Environment.ProcessId;
        return _procs.List()
            .Where(p => p.Pid != self && !Protected.Contains(p.Name))
            .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => new AppGroup(g.Key, g.Count(), g.Sum(x => x.WorkingSetBytes), g.Select(x => x.Pid).ToList()))
            .Where(g => g.Bytes >= minBytes)
            .OrderByDescending(g => g.Bytes)
            .Take(top)
            .ToList();
    }

    /// <summary>Закрывает выбранные программы. Несохранённое в них пропадёт — об этом предупреждает интерфейс.</summary>
    public CloseResult Close(IEnumerable<string> names, TimeSpan? grace = null)
    {
        var wanted = names.Where(n => !Protected.Contains(n)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        int self = Environment.ProcessId;
        int closed = 0, failed = 0;
        long freed = 0;
        foreach (var p in _procs.List().Where(p => p.Pid != self && wanted.Contains(p.Name)))
        {
            if (_procs.TryClose(p.Pid, grace ?? TimeSpan.FromSeconds(3))) { closed++; freed += p.WorkingSetBytes; }
            else failed++;
        }
        return new CloseResult(closed, failed, freed);
    }
}
