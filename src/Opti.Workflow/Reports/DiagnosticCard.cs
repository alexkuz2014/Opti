using System.Text;
using Opti.Core.Findings;
using Opti.Core.Profile;

namespace Opti.Workflow.Reports;

/// <summary>
/// Короткая обезличенная сводка о компьютере (10–15 строк) для техподдержки и чатов: имя пользователя, имя ПК, пути и адреса в неё не попадают.
/// </summary>
public static class DiagnosticCard
{
    public static string Build(SystemProfile p, IReadOnlyList<Finding> findings, string appVersion)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"[Opti {appVersion} — карточка ПК]");
        if (p.Cpu.Name is { } cpu) sb.AppendLine($"CPU: {Clean(cpu)}, {p.Cpu.Cores} ядер / {p.Cpu.Threads} потоков");
        foreach (var g in p.Gpus.Where(g => !g.IsSoftware))
        {
            var extra = new List<string>();
            if (g.DriverVersion is { } dv) extra.Add($"драйвер {dv}" + (g.DriverDate is { } dd ? $" от {dd:dd.MM.yyyy}" : ""));
            if (!g.IsIntegrated && p.Graphics.PcieWidthCurrent is { } w) extra.Add($"PCIe x{w}");
            if (!g.IsIntegrated && p.Graphics.ResizableBarEnabled is { } rb) extra.Add($"ReBAR: {(rb ? "вкл" : "выкл")}");
            sb.AppendLine($"GPU: {g.Name} ({string.Join(", ", extra)})".Replace(" ()", ""));
        }
        if (p.Memory.TotalMb > 0)
        {
            var mods = p.Memory.Modules;
            var speed = mods.Count > 0 ? $", {mods.Max(m => m.ConfiguredSpeed)} МТ/с" : "";
            sb.AppendLine($"RAM: {p.Memory.TotalMb / 1024.0:0.#} ГБ{(mods.Count > 0 ? $" ({mods.Count} шт.)" : "")}{speed}");
        }
        if (p.PrimaryDisplay is { } d) sb.AppendLine($"Экран: {d.Width}×{d.Height} @ {d.CurrentHz} Гц" + (d.MaxHzAtCurrentResolution > d.CurrentHz + 1 ? $" (макс. {d.MaxHzAtCurrentResolution})" : ""));
        if (p.SystemDisk is { } sd) sb.AppendLine($"Системный диск: {sd.MediaType}, свободно {sd.FreeGb:0} ГБ" + (sd.TrimEnabled == false ? ", TRIM выкл" : ""));
        sb.AppendLine($"Windows: {p.Os.ProductName} {p.Os.DisplayVersion}, сборка {p.Os.Build}.{p.Os.Ubr}" + (p.Os.ModifiedBuild is { } m2 ? $" ({m2})" : ""));
        if (p.NetQuality is { } q && !double.IsNaN(q.IdlePingMs))
            sb.AppendLine($"Сеть: пинг {q.IdlePingMs:0} мс, скачки ±{q.IdleJitterMs:0.#} мс, потери {q.PacketLossPercent:0.#} %" + (q.GatewayPingMs is { } gw ? $"; до роутера {gw:0.#} мс" : ""));
        var important = findings.Where(f => f.Severity >= Severity.Important).Take(6).ToList();
        sb.AppendLine(important.Count == 0 ? "Найдено проблем: нет критичных и важных" : $"Найдено: {findings.Count(f => f.Severity == Severity.Critical)} критичных, {findings.Count(f => f.Severity == Severity.Important)} важных");
        foreach (var f in important) sb.AppendLine($"  • {f.Title}");
        return sb.ToString().TrimEnd();
    }

    /// <summary>Убирает лишние пробелы и «(R)», «(TM)» из названий процессоров.</summary>
    private static string Clean(string s) => System.Text.RegularExpressions.Regex.Replace(s.Replace("(R)", "").Replace("(TM)", ""), @"\s+", " ").Trim();
}
