using Opti.Core.Profile;

namespace Opti.Core.Os;

/// <summary>Ограничение, оставленное в параметрах загрузки (BCD) — чаще всего через «msconfig → Загрузка → Дополнительные параметры».</summary>
public sealed record BootLimit(string Element, string Title, string Evidence);

/// <summary>
/// Параметры загрузки, которые только урезают систему. Главное: «Число процессоров» в msconfig равное числу потоков
/// ничего не ускоряет — Windows и так использует все ядра. А вот меньшее значение (или «Максимум памяти», «один процессор»)
/// режет производительность, и такие значения часто остаются после чужих советов и «оптимизаторов».
/// </summary>
public static class BootLimits
{
    public static IReadOnlyList<BootLimit> Find(SystemProfile p)
    {
        var list = new List<BootLimit>();
        var t = p.Timers;
        int logical = p.Cpu.Threads > 0 ? p.Cpu.Threads : p.Cpu.Cores;
        if (t.BcdNumProc is { } n && (logical <= 0 ? n > 0 : n < logical))
            list.Add(new("numproc", "Ограничено число процессоров", $"Windows разрешено использовать только {n} из {(logical > 0 ? logical.ToString() : "всех")} логических процессоров"));
        if (t.BcdOneCpu == true)
            list.Add(new("onecpu", "Включён режим одного процессора", "Windows запускается только на одном процессоре"));
        if (!string.IsNullOrWhiteSpace(t.BcdMaxMem))
            list.Add(new("maxmem", "Ограничен максимум памяти", $"Предел памяти: {t.BcdMaxMem} МБ"));
        if (!string.IsNullOrWhiteSpace(t.BcdTruncateMemory))
            list.Add(new("truncatememory", "Память обрезана выше адреса", $"truncatememory = {t.BcdTruncateMemory}"));
        return list;
    }
}
