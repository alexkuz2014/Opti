using Opti.Core.Drivers;
using Opti.Core.Findings;
using Opti.Core.Fixes;
using Opti.Core.Profile;
using Opti.Core.Registry;

namespace Opti.Analysis.Detectors;

/// <summary>
/// Драйверы сети, звука, Bluetooth, чипсета и накопителей: стандартный драйвер Microsoft вместо драйвера производителя
/// или очень старая версия. Видеокарту проверяют DRIVER_OLD и GPU_DRIVER_MISSING.
/// </summary>
public sealed class DriverAuditDetector : DetectorBase
{
    public override string Id => "DRIVERS_UPDATE";

    protected override IEnumerable<Finding> Run(SystemProfile p, DetectionContext ctx)
    {
        var flagged = p.Drivers
            .Where(d => d.Category != DriverCategory.Display)
            .Select(d => (Driver: d, Status: DriverAdvisor.Evaluate(d, ctx.Now)))
            .Where(x => x.Status is DriverStatus.Old or DriverStatus.Generic)
            .ToList();
        if (flagged.Count == 0) yield break;

        var f = new Finding
        {
            Id = Id,
            Title = flagged.Count == 1 ? "Драйвер устройства стоит обновить" : $"Драйверы {flagged.Count} устройств стоит обновить",
            Severity = flagged.Any(x => x.Driver.Category == DriverCategory.Network && x.Status == DriverStatus.Old) ? Severity.Important : Severity.Advice,
            Explanation = "Стандартный драйвер Windows или драйвер старше трёх лет обычно работает, но без исправлений производителя: " +
                          "обрывы Wi-Fi, рост пинга, треск звука, задержки Bluetooth-наушников и проблемы со сном. Opti не скачивает драйверы сам — " +
                          "ниже ссылки на официальные страницы. Полный список — в разделе «Драйверы».",
            ManualSteps =
            {
                "Сначала загляните в «Необязательные обновления» Центра обновления Windows — там часто есть драйверы производителя.",
                "Для звука, сети и чипсета ноутбука надёжнее драйвер с сайта производителя ноутбука или материнской платы.",
            },
        };
        foreach (var (d, st) in flagged.Take(12))
            f.Evidence.Add($"{DriverAdvisor.CategoryName(d.Category)}: {d.DeviceName} — " +
                           (st == DriverStatus.Generic ? "стандартный драйвер Microsoft" : $"драйвер от {d.Date:dd.MM.yyyy}, версия {d.Version}"));
        foreach (var url in flagged.Select(x => DriverAdvisor.LinkFor(x.Driver, p.Device).Url).Prepend(Sources.WindowsOptionalUpdates).Distinct())
            f.Links.Add(url);
        yield return f;
    }
}

/// <summary>Нестандартные значения реестра, оставленные «оптимизаторами»: возвращаем стандарт Windows.</summary>
public sealed class RegistryAuditDetector : DetectorBase
{
    public override string Id => "REGISTRY_AUDIT";

    protected override IEnumerable<Finding> Run(SystemProfile p, DetectionContext ctx)
    {
        var perf = RegistryAuditCatalog.Violations(p.RegistryAudit, RegistryRuleGroup.Performance).ToList();
        if (perf.Count > 0)
            yield return Build("REGISTRY_TWEAKS", FixIds.RegistryPerfDefaults, perf, p,
                "Реестр: вредные «твики» производительности",
                "В реестре найдены значения, которые обычно оставляют «оптимизаторы» и твик-паки. Они не ускоряют игры, а некоторые — вредят: " +
                "забирают память, прячут зависания драйвера или замедляют запуск программ. Opti вернёт стандартные значения Windows (с откатом).");
        var sec = RegistryAuditCatalog.Violations(p.RegistryAudit, RegistryRuleGroup.UpdatesAndSecurity).ToList();
        if (sec.Count > 0)
            yield return Build("REGISTRY_SECURITY", FixIds.RegistrySecurityDefaults, sec, p,
                "Отключены обновления или защита Windows",
                "Через реестр отключены обновления, службы или встроенная защита. Для FPS это почти ничего не даёт, а компьютер перестаёт получать " +
                "исправления безопасности и драйверы. Opti вернёт стандарт (с откатом).");
    }

    private static Finding Build(string id, string fixId, List<RegistryRule> rules, SystemProfile p, string title, string explanation)
    {
        var f = new Finding
        {
            Id = id, Title = title, Explanation = explanation,
            Severity = rules.Max(r => r.Severity),
            FixIds = { fixId },
        };
        foreach (var r in rules)
            f.Evidence.Add($"{r.Title}: {r.Key}\\{r.ValueName} = {p.RegistryAudit.GetValueOrDefault(r.Id)} (стандарт — {r.DefaultText})");
        foreach (var link in rules.Select(r => r.Link).OfType<string>().Distinct()) f.Links.Add(link);
        return f;
    }
}
