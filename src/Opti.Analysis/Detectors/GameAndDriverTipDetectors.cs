using Opti.Core.Findings;
using Opti.Core.Fixes;
using Opti.Core.Games;
using Opti.Core.Os;
using Opti.Core.Profile;

namespace Opti.Analysis.Detectors;

/// <summary>Параметры загрузки, которые только урезают систему (msconfig → «Число процессоров», «Максимум памяти»).</summary>
public sealed class BootLimitsDetector : DetectorBase
{
    public override string Id => "BOOT_LIMITS";

    protected override IEnumerable<Finding> Run(SystemProfile p, DetectionContext ctx)
    {
        var limits = BootLimits.Find(p);
        if (limits.Count == 0) yield break;
        yield return new Finding
        {
            Id = Id,
            Title = "В параметрах загрузки стоит ограничение",
            Severity = limits.Any(l => l.Element is "numproc" or "onecpu") ? Severity.Critical : Severity.Important,
            Explanation = "Кто-то (или «оптимизатор») задал ограничение в msconfig → «Загрузка» → «Дополнительные параметры». " +
                          "Эти параметры придуманы для отладки и ничего не ускоряют: Windows и так использует все ядра и всю память. " +
                          "Зато меньшее значение, чем есть в компьютере, прямо отнимает производительность — игры получают меньше ядер.",
            Evidence = limits.Select(l => $"{l.Title}: {l.Evidence}").ToList(),
            FixIds = { FixIds.BcdBootLimitsReset },
        };
    }
}

/// <summary>Советы по GTA V и клиенту Majestic RP (altV). Файлы игры не меняются.</summary>
public sealed class Gta5ProfileDetector : DetectorBase
{
    public override string Id => GameProfiles.Gta5Id;

    protected override IEnumerable<Finding> Run(SystemProfile p, DetectionContext ctx)
    {
        var games = GameProfiles.MatchGta5(p);
        if (games.Count == 0) yield break;
        var tips = GameProfiles.Gta5Tips(p);
        var f = new Finding
        {
            Id = Id,
            Title = "GTA V / Majestic RP: что даёт FPS на вашем ПК",
            Severity = Severity.Advice,
            Explanation = "GTA V — старый движок, который сильно зависит от процессора и диска. Больше всего кадров дают настройки графики в самой игре; " +
                          "системные настройки (питание, видеокарта, фон) Opti исправляет сам. Файлы игры Opti не трогает — ниже то, что вы меняете в игре вручную, " +
                          "по убыванию эффекта. Точные проценты зависят от сцены и железа: проверяйте замером на своём ПК.",
            Evidence = games.Select(g => $"{g.Name}: {g.InstallDir ?? g.Executables.FirstOrDefault()}").ToList(),
            Links = { Sources.Gta5Guide },
        };
        foreach (var t in tips) f.ManualSteps.Add($"[{t.Label}] {t.Text}");
        yield return f;
    }
}

/// <summary>Настройки панели видеодрайвера, которые реально влияют на отклик и ровность кадров.</summary>
public sealed class GpuPanelTipsDetector : DetectorBase
{
    public override string Id => "GPU_PANEL_TIPS";

    protected override IEnumerable<Finding> Run(SystemProfile p, DetectionContext ctx)
    {
        var gpu = p.DiscreteGpu;
        if (gpu is null || p.Games.Count == 0 || gpu.Vendor is not (GpuVendor.Nvidia or GpuVendor.Amd)) yield break;
        var hz = p.PrimaryDisplay?.CurrentHz is > 0 and var h ? $"{h} Гц" : "частота монитора";
        var steps = new List<string>();
        if (gpu.Vendor == GpuVendor.Nvidia)
        {
            steps.Add("Игры с поддержкой NVIDIA Reflex: включите «Reflex» в настройках игры. Он заменяет драйверный «Режим низкой задержки»; режим «On + Boost» в тестах TechPowerUp отличий от «On» не дал.");
            steps.Add("Игры без Reflex: Панель управления NVIDIA → «Режим низкой задержки» → «Вкл». Выгода заметна, когда видеокарта загружена почти на 100 % — на среднем FPS это не сказывается.");
            steps.Add($"Панель NVIDIA → «Макс. частота кадров» → {hz}. Ровные кадры ощущаются лучше, видеокарта холоднее и тише.");
            steps.Add("Панель NVIDIA → «Режим управления электропитанием» → «Предпочтителен режим максимальной производительности» — полезно, если частота видеокарты в игре «прыгает»; на средний FPS почти не влияет.");
            steps.Add("Фильтры NVIDIA (Alt+F3 в игре) выключите, если не пользуетесь ими: они съедают часть кадров.");
        }
        else
        {
            steps.Add("Radeon Software → «Игры» → включите «Radeon Anti-Lag» для игр на DirectX 11 / 12 — уменьшает задержку ввода.");
            steps.Add("«Radeon Chill» для игр, где нужен максимум FPS, выключите: он сознательно снижает частоту кадров, когда вы не двигаетесь.");
            steps.Add($"Ограничьте частоту кадров значением {hz} (в игре или через «Radeon Chill» с одинаковым минимумом и максимумом) — кадры ровнее, нагрев ниже.");
        }
        yield return new Finding
        {
            Id = Id,
            Title = gpu.Vendor == GpuVendor.Nvidia ? "Панель NVIDIA: настройки для игр" : "Radeon Software: настройки для игр",
            Severity = Severity.Advice,
            Explanation = "Эти настройки драйвера Opti не меняет автоматически (для NVIDIA это требует отдельного интерфейса драйвера, и результат сильно зависит от игры), " +
                          "но они реально влияют на отклик и ровность кадров. Меняйте по одной и проверяйте замером.",
            Evidence = { $"Видеокарта: {gpu.Name}" },
            ManualSteps = steps,
            Links = gpu.Vendor == GpuVendor.Nvidia ? new() { Sources.ReflexTest } : new(),
        };
    }
}
