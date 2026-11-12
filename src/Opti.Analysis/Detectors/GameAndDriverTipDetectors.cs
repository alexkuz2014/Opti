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
        var steps = new List<string>
        {
            "Если видеокарта в игре загружена на 99–100 %, очередь кадров растёт и задержка ввода увеличивается. Ограничьте FPS немного ниже того, что видеокарта выдаёт, — загрузка около 90–95 % даёт заметно более быстрый отклик.",
        };
        if (gpu.Vendor == GpuVendor.Nvidia)
        {
            steps.Add("Игры с поддержкой NVIDIA Reflex: включите «Reflex» в настройках игры. Он заменяет драйверный «Режим низкой задержки»; режим «On + Boost» в тестах TechPowerUp отличий от «On» не дал.");
            steps.Add("Игры без Reflex: Панель управления NVIDIA → «Режим низкой задержки» → «Вкл». Выгода заметна, когда видеокарта загружена почти на 100 % — на среднем FPS это не сказывается.");
            steps.Add($"Панель NVIDIA → «Макс. частота кадров» → {hz}. Ровные кадры ощущаются лучше, видеокарта холоднее и тише.");
            steps.Add("Панель NVIDIA → «Режим управления электропитанием» → «Предпочтителен режим максимальной производительности» — полезно, если частота видеокарты в игре «прыгает»; на средний FPS почти не влияет.");
            steps.Add("Фильтры NVIDIA (Alt+F3 в игре) выключите, если не пользуетесь ими: они съедают часть кадров.");
            steps.Add("Драйвер ставьте через «Выборочную установку» → «Выполнить чистую установку», только видеодрайвер: без лишних модулей и телеметрии.");
        }
        else
        {
            steps.Add("Radeon Software → «Игры» → включите «Radeon Anti-Lag» для игр на DirectX 11 / 12 — уменьшает задержку ввода.");
            steps.Add("«Radeon Chill» для игр, где нужен максимум FPS, выключите: он сознательно снижает частоту кадров, когда вы не двигаетесь.");
            steps.Add($"Ограничьте частоту кадров значением {hz} (в игре или через «Radeon Chill» с одинаковым минимумом и максимумом) — кадры ровнее, нагрев ниже.");
        }
        steps.Add("В меню самого монитора выключите «улучшайзеры» — Dynamic Contrast, Local Dimming, шумоподавление, эмуляцию HDR: монитор буферизует кадр, и к выводу добавляется задержка. Режим Overdrive оставьте без артефактов.");
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

/// <summary>Resizable BAR (Smart Access Memory) выключен на видеокарте, которая его поддерживает.</summary>
public sealed class ResizableBarDetector : DetectorBase
{
    public override string Id => "GPU_REBAR_OFF";

    protected override IEnumerable<Finding> Run(SystemProfile p, DetectionContext ctx)
    {
        var gpu = p.DiscreteGpu;
        if (gpu is not { Vendor: GpuVendor.Nvidia } || p.Graphics.ResizableBarEnabled != false) yield break;
        // Официально Resizable BAR поддерживают видеокарты GeForce RTX 30-й серии и новее
        if (!System.Text.RegularExpressions.Regex.IsMatch(gpu.Name, @"RTX\s*[3-5]\d{3}", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) yield break;
        yield return new Finding
        {
            Id = Id,
            Title = "Resizable BAR выключен",
            Severity = Severity.Advice,
            Explanation = "Resizable BAR даёт процессору доступ ко всей видеопамяти сразу, а не окнами по 256 МБ. В части игр это добавляет несколько процентов FPS, " +
                          "в других ничего не меняет, а в отдельных даже чуть хуже — поэтому после включения проверьте игру замером. Включается в BIOS, из Windows это сделать нельзя.",
            Evidence = { $"{gpu.Name}: окно BAR1 {p.Graphics.Bar1TotalMb} МБ при видеопамяти {gpu.DedicatedVramMb} МБ" },
            ManualSteps =
            {
                "BIOS: включите «Above 4G Decoding», затем «Re-Size BAR Support»; режим загрузки UEFI, CSM выключить.",
                "Нужны актуальный BIOS материнской платы и драйвер видеокарты. Проверка — в GPU-Z: «Resizable BAR: Enabled».",
            },
            Links = { Sources.PcTuning },
            IsHardwareLimitation = false,
        };
    }
}

/// <summary>Фоновое ПО мыши, клавиатуры и подсветки: сохраните настройки в память устройства и закройте программу.</summary>
public sealed class PeripheralSoftwareDetector : DetectorBase
{
    public override string Id => "PERIPHERAL_SOFTWARE";

    protected override IEnumerable<Finding> Run(SystemProfile p, DetectionContext ctx)
    {
        var apps = p.Background.PeripheralApps;
        if (apps.Count == 0) yield break;
        yield return new Finding
        {
            Id = Id,
            Title = "Работает фоновое ПО периферии",
            Severity = Severity.Advice,
            Explanation = "Программы мышей, клавиатур и подсветки держат в фоне тяжёлые службы (часто на браузерном движке) и постоянно обращаются к драйверам и USB. " +
                          "Большинству устройств они нужны только для настройки: DPI, частоту опроса и подсветку можно один раз сохранить во встроенную память устройства и дальше обходиться без программы. " +
                          "Прямых измерений выигрыша в FPS нет — это меньше фоновой нагрузки и меньше обращений к USB, что важнее на слабых процессорах.",
            Evidence = apps.Select(a => $"Запущено: {a}").ToList(),
            ManualSteps =
            {
                "Настройте устройство в программе и сохраните профиль во встроенную память (Onboard Memory).",
                "Затем уберите программу из автозагрузки (раздел «Исправления» → автозагрузка) или закройте перед игрой на странице «Игра».",
                "Подсветку на время соревновательных игр лучше выключить: её анимацию считает тот же микроконтроллер, что обрабатывает клики и движение сенсора.",
            },
            Links = { Sources.PcOptimizationHub },
        };
    }
}
