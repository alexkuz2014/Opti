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
        var hz = p.PrimaryDisplay?.CurrentHz is > 0 and var h ? $"{FpsCap.Recommend(h, p.PrimaryDisplay?.VrrSupported)} (монитор {h} Гц)" : "частота монитора";
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

/// <summary>Диск занят в покое: игре придётся делить его с фоновыми программами.</summary>
public sealed class DiskBusyDetector : DetectorBase
{
    public override string Id => "DISK_BUSY";
    public const double BusyThreshold = 70;

    private static readonly (string Process, string Hint)[] Hints =
    {
        ("MsMpEng", "Microsoft Defender сканирует файлы — дождитесь окончания"),
        ("TiWorker", "Windows ставит обновления — дождитесь окончания и перезагрузитесь"),
        ("TrustedInstaller", "Windows ставит обновления — дождитесь окончания и перезагрузитесь"),
        ("SearchIndexer", "Windows индексирует файлы — после большого копирования пройдёт само"),
        ("OneDrive", "OneDrive синхронизирует файлы — приостановите на время игры"),
        ("qbittorrent", "торрент-клиент качает или раздаёт — закройте на время игры"),
        ("utorrent", "торрент-клиент качает или раздаёт — закройте на время игры"),
        ("steam", "Steam обновляет игры — пусть закончит или поставьте на паузу"),
        ("EpicGamesLauncher", "Epic обновляет игры — пусть закончит или поставьте на паузу"),
        ("Battle.net", "Battle.net обновляет игры — пусть закончит или поставьте на паузу"),
    };

    protected override IEnumerable<Finding> Run(SystemProfile p, DetectionContext ctx)
    {
        if (p.DiskActivity is not { } a || (a.BusyPercent < BusyThreshold && a.QueueLength < 2)) yield break;
        bool hdd = p.SystemDisk?.MediaType == DiskMediaType.Hdd;
        var f = new Finding
        {
            Id = Id,
            Title = "Диск занят, хотя компьютер ничем не занят",
            Severity = a.BusyPercent >= 90 || (hdd && a.BusyPercent >= BusyThreshold) ? Severity.Important : Severity.Advice,
            Explanation = "Во время анализа диск работал почти на пределе. Пока он занят, игры дольше запускаются и подгружают уровни, а в самой игре возможны фризы: игре приходится делить диск с фоновыми программами." +
                          (hdd ? " На жёстком диске это ощущается сильнее всего." : ""),
            Evidence = { $"Занятость диска: {a.BusyPercent:0} %, очередь запросов: {a.QueueLength:0.#}, скорость: {a.ThroughputMBs:0.#} МБ/с" },
        };
        foreach (var w in a.Top)
        {
            f.Evidence.Add($"{w.Name}: {w.MbPerSec:0.#} МБ/с");
            var hint = Hints.FirstOrDefault(h => w.Name.Contains(h.Process, StringComparison.OrdinalIgnoreCase)).Hint;
            f.ManualSteps.Add(hint is not null ? $"{w.Name}: {hint}." : $"{w.Name}: закройте или поставьте на паузу, если программа не нужна сейчас.");
        }
        if (f.ManualSteps.Count == 0) f.ManualSteps.Add("Какая программа грузит диск, определить не удалось: откройте «Диспетчер задач» → «Производительность» → «Диск» и повторите анализ после закрытия лишнего.");
        yield return f;
    }
}

/// <summary>Игры на почти заполненном диске.</summary>
public sealed class GamesDiskFullDetector : DetectorBase
{
    public override string Id => "GAME_DISK_FULL";

    protected override IEnumerable<Finding> Run(SystemProfile p, DetectionContext ctx)
    {
        var full = GameStorage.Analyze(p).Where(d => d.IsNearlyFull).ToList();
        if (full.Count == 0) yield break;
        yield return new Finding
        {
            Id = Id,
            Title = "Диск с играми почти заполнен",
            Severity = Severity.Advice,
            Explanation = "Когда на диске остаётся меньше 10 % свободного места, запись замедляется (особенно у SSD): обновления игр, кэш шейдеров и файлы подкачки пишутся дольше, а загрузка может «запинаться». " +
                          "Освободите место или перенесите часть игр на другой диск.",
            Evidence = full.Select(d => $"{d.Letter} ({d.KindText}): свободно {d.FreeGb:0} ГБ из {d.SizeGb:0} ГБ ({d.FreePercent:0} %)").ToList(),
            ManualSteps = { "Удалите игры, в которые не играете (Steam, Epic и другие лаунчеры удаляют и возвращают игры без потери сохранений в облаке).", "Раздел «Очистка» в Opti освободит временные файлы и кэш." },
        };
    }
}

/// <summary>Как ускорить загрузку игр: что именно мешает на этом ПК. Файлы игр Opti не трогает.</summary>
public sealed class GameLoadTipsDetector : DetectorBase
{
    public override string Id => "GAME_LOAD_TIPS";

    protected override IEnumerable<Finding> Run(SystemProfile p, DetectionContext ctx)
    {
        if (p.Games.Count == 0) yield break;
        var drives = GameStorage.Analyze(p);
        var steps = new List<string>();
        var links = new List<string>();

        if (drives.Any(d => d.Kind == StorageKind.Hdd))
        {
            steps.Add("Игры на жёстком диске: перенесите на SSD те, в которые играете чаще всего. По обзорам игры с HDD грузятся заметно дольше SSD — от десятков процентов до 2–3 раз в зависимости от игры; сильнее всего разница в играх с открытым миром, где карта подгружается прямо во время игры (GTA V).");
            links.Add(Sources.GameLoadBenchmark);
        }
        if (drives.Any(d => d.Kind == StorageKind.Usb))
            steps.Add("Игра на внешнем USB-диске: через USB загрузка медленнее, чем с внутреннего SSD. Если диск не NVMe во внешнем корпусе — лучше держать игру внутри.");
        if (drives.Any(d => d.Kind == StorageKind.SataSsd) && !drives.Any(d => d.Kind is StorageKind.Hdd or StorageKind.Usb))
        {
            steps.Add("SATA SSD уже хорош: замена на NVMe для обычных игр экономит 1–3 секунды на загрузке в 15–30 секунд и ощутима в основном в играх с DirectStorage. Менять диск ради этого не стоит.");
            links.Add(Sources.DirectStorageTest);
        }
        foreach (var d in drives.Where(d => d.IsNearlyFull))
            steps.Add($"На диске {d.Letter} осталось {d.FreePercent:0} % места: освободите — запись на почти полный SSD замедляется, обновления игр и кэш шейдеров пишутся дольше.");
        if (p.DiskActivity is { } a && (a.BusyPercent >= DiskBusyDetector.BusyThreshold || a.QueueLength >= 2))
            steps.Add("Диск был занят во время анализа — см. отдельную находку «Диск занят»: пока он занят, игра грузится медленнее.");
        if (p.Games.Any(g => g.Name.Contains("Fortnite", StringComparison.OrdinalIgnoreCase) || g.Executables.Any(e => e.Contains("Fortnite", StringComparison.OrdinalIgnoreCase))))
        {
            steps.Add("Fortnite: после обновления игры или драйвера первый запуск и первые матчи идут дольше — игра пересобирает шейдеры. Не прерывайте компиляцию. Если играете в DirectX 11, в Epic Games Launcher → Fortnite → «Параметры» можно снять «DirectX 12 Shaders» (по сообщениям игроков, это 3–6 ГБ). Если клиент бесконечно пересобирает шейдеры в DX12, Epic советует очистить их кэш.");
            links.Add(Sources.FortniteShaders);
        }
        var thirdPartyAv = p.Security.AntiVirus.Where(n => !n.Contains("Defender", StringComparison.OrdinalIgnoreCase) && !n.Contains("Windows", StringComparison.OrdinalIgnoreCase)).ToList();
        if (thirdPartyAv.Count > 0)
            steps.Add($"Антивирус ({string.Join(", ", thirdPartyAv)}) может сканировать файлы игры при каждом запуске и подгрузке. Исключение папки игры ускоряет загрузку, но снижает защиту — решайте сами и только для игр из официальных магазинов. Opti этого сам не делает.");
        if (steps.Count == 0) yield break;

        steps.Add("Не очищайте кэш шейдеров без нужды: после очистки первые запуски и подгрузки снова будут медленнее (в «Очистке» он по умолчанию не отмечен).");
        yield return new Finding
        {
            Id = Id,
            Title = "Как ускорить загрузку игр на этом ПК",
            Severity = Severity.Advice,
            Explanation = "Время загрузки и подгрузки игр почти целиком зависит от диска и от того, что ещё его использует. Файлы игр Opti не меняет — ниже то, что мешает именно на вашем компьютере. Точные секунды зависят от игры и железа.",
            Evidence = drives.Select(d => $"{d.Letter} — {d.KindText}, свободно {d.FreeGb:0} из {d.SizeGb:0} ГБ: {string.Join(", ", d.Games.Select(g => g.Name).Take(4))}{(d.Games.Count > 4 ? $" и ещё {d.Games.Count - 4}" : "")}").ToList(),
            ManualSteps = steps,
            Links = links.Distinct().ToList(),
        };
    }
}

/// <summary>Видеокарта работает не на полной ширине шины PCIe (x8 или x4 вместо x16).</summary>
public sealed class PcieLinkWidthDetector : DetectorBase
{
    public override string Id => "GPU_PCIE_WIDTH";

    protected override IEnumerable<Finding> Run(SystemProfile p, DetectionContext ctx)
    {
        var g = p.Graphics;
        if (p.Device.IsLaptop || p.DiscreteGpu is not { Vendor: GpuVendor.Nvidia } gpu) yield break;
        if (g.PcieWidthCurrent is not { } cur || g.PcieWidthMax is not { } max || max < 16 || cur >= max || cur > 8) yield break;
        yield return new Finding
        {
            Id = Id,
            Title = $"Видеокарта работает на шине PCIe x{cur} вместо x{max}",
            Severity = Severity.Important,
            Explanation = "Видеокарта получила меньше линий PCIe, чем умеет. Для большинства игр x8 хватает, но x4 и меньше уже режет подгрузку текстур и карты (GTA V и ей подобные) и просаживает минимальные кадры. " +
                          "Частые причины: видеокарта стоит в нижнем слоте, часть линий отдана M.2-накопителю, перекошен райзер или пыль в слоте. Ширина шины в простое у части видеокарт снижается штатно, поэтому проверьте под нагрузкой.",
            Evidence = { $"{gpu.Name}: x{cur} из x{max}" },
            ManualSteps =
            {
                "Откройте GPU-Z → «Bus Interface» → «?» и запустите «Render Test»: если под нагрузкой написано x16 — всё в порядке.",
                "Если ширина осталась x8 или x4: переставьте видеокарту в верхний слот PCIe x16, проверьте в инструкции к плате, не делят ли линии слот M.2 и слот видеокарты, продуйте слот.",
            },
        };
    }
}

/// <summary>NVMe-накопитель горячий: при перегреве он сбрасывает скорость, и подгрузка игры «запинается».</summary>
public sealed class NvmeThermalDetector : DetectorBase
{
    public override string Id => "DISK_NVME_HOT";

    protected override IEnumerable<Finding> Run(SystemProfile p, DetectionContext ctx)
    {
        var hot = p.Disks.Where(d => d.MediaType == DiskMediaType.Nvme && (d.TemperatureC >= 65 || d.TemperatureMaxC >= 80)).ToList();
        if (hot.Count == 0) yield break;
        yield return new Finding
        {
            Id = Id,
            Title = "NVMe-накопитель сильно греется",
            Severity = Severity.Advice,
            Explanation = "Быстрые NVMe без радиатора греются и при перегреве снижают скорость — иногда до уровня медленного диска. В играх это выглядит как фризы при подгрузке карты, а после выхода из игры температура быстро падает, и причина остаётся незамеченной. " +
                          "Порог срабатывания защиты зависит от модели (обычно 70–85 °C).",
            Evidence = hot.Select(d => $"{d.Model}: сейчас {(d.TemperatureC is { } t ? t + " °C" : "?")}" + (d.TemperatureMaxC is { } m ? $", максимум за всё время {m} °C" : "")).ToList(),
            ManualSteps = { "Поставьте радиатор на M.2 (у многих плат он в комплекте), улучшите обдув корпуса. Накопитель под горячей видеокартой греется сильнее.", "Если слотов несколько, игры лучше держать на накопителе, который остаётся холоднее." },
            IsHardwareLimitation = true,
        };
    }
}

/// <summary>Режим MSI у видеокарты явно выключен.</summary>
public sealed class MsiAuditDetector : DetectorBase
{
    public override string Id => "GPU_MSI_OFF";

    protected override IEnumerable<Finding> Run(SystemProfile p, DetectionContext ctx)
    {
        if (p.DiscreteGpu is not { MsiSupported: 0 } gpu) yield break;
        yield return new Finding
        {
            Id = Id,
            Title = "У видеокарты выключен режим прерываний MSI",
            Severity = Severity.Advice,
            Explanation = "Современные видеокарты работают в режиме MSI, а при выключенном используют общие линии прерываний, что увеличивает задержки обработки (ISR/DPC). Иногда режим сбрасывается после неудачного обновления драйвера. " +
                          "Opti это только показывает: менять ветку реестра устройства вручную рискованно, правильный путь — чистая установка драйвера.",
            Evidence = { $"{gpu.Name}: MSISupported = 0" },
            ManualSteps = { "Установите драйвер видеокарты заново: установщик NVIDIA → «Выборочная установка» → «Выполнить чистую установку».", "Если не помогло, проверьте вывод LatencyMon — он покажет, какой драйвер даёт большие задержки." },
        };
    }
}

/// <summary>RSS отключён: входящие пакеты обрабатывает одно ядро.</summary>
public sealed class NetRssDetector : DetectorBase
{
    public override string Id => "NET_RSS_OFF";

    protected override IEnumerable<Finding> Run(SystemProfile p, DetectionContext ctx)
    {
        var a = p.Network.Where(n => n.IsUp && n.RssEnabled == false && n.Kind is NetAdapterKind.Ethernet or NetAdapterKind.WiFi).ToList();
        if (a.Count == 0 || p.Cpu.Threads < 4) yield break;
        yield return new Finding
        {
            Id = Id,
            Title = "Масштабирование приёма пакетов (RSS) выключено",
            Severity = Severity.Advice,
            Explanation = "RSS распределяет обработку входящего сетевого трафика по ядрам процессора. Когда он выключен, всё идёт через одно ядро: на онлайн-играх с большим числом игроков это может давать сетевые статтеры. По умолчанию RSS включён — его отключают «оптимизаторы» или сторонние драйверы.",
            Evidence = a.Select(n => $"{n.Description ?? n.Name}: RSS выключен").ToList(),
            FixIds = { FixIds.NetRssOn },
        };
    }
}

/// <summary>
/// Дерево решений из диагностики Test-NetConnection / Resolve-DnsName: имя не разрешается (смотрим DNS) — или имя разрешается, а порт закрыт
/// (файрвол, антивирус, VPN, блокировка провайдером или сервис недоступен) — или DNS просто медленный.
/// </summary>
public sealed class NetServicesDetector : DetectorBase
{
    public override string Id => "NET_SERVICES";
    public const int SlowDnsMs = 100;

    protected override IEnumerable<Finding> Run(SystemProfile p, DetectionContext ctx)
    {
        var all = p.NetSettings.Services;
        var control = all.FirstOrDefault(s => s.IsControl);
        // Не открылся даже контрольный сервис — интернета нет совсем, это не проблема отдельных сервисов
        if (all.Count == 0 || control is { TcpOk: false, SystemDnsOk: false } || control is { TcpOk: false, PublicDnsOk: false }) yield break;
        var games = all.Where(s => !s.IsControl).ToList();

        var dnsBroken = games.Where(s => !s.SystemDnsOk && s.PublicDnsOk).ToList();
        if (dnsBroken.Count > 0)
            yield return new Finding
            {
                Id = "NET_DNS_BROKEN",
                Title = "DNS не находит игровые сервисы",
                Severity = Severity.Important,
                Explanation = "Системный DNS (обычно от провайдера) не может найти адреса этих сервисов, а публичный DNS находит. Из-за этого клиенты игр не подключаются или подключаются с задержкой. " +
                              "Пинг в самой игре это не меняет, но «не заходит в игру» и «долго ищет сервер» объясняет.",
                Evidence = dnsBroken.Select(s => $"{s.Name} ({s.Host}): системный DNS не ответил, 1.1.1.1 ответил за {s.PublicDnsMs} мс").ToList(),
                ManualSteps =
                {
                    "Параметры → Сеть и Интернет → ваше подключение → «Назначение DNS-сервера» → вручную: 1.1.1.1 и 8.8.8.8 (или DNS роутера).",
                    "Затем в PowerShell: Clear-DnsClientCache — очистит старые ответы.",
                },
                Links = { Sources.NetworkStatus },
            };

        var blocked = games.Where(s => s.SystemDnsOk && !s.TcpOk).ToList();
        if (blocked.Count > 0)
            yield return new Finding
            {
                Id = "NET_SERVICE_BLOCKED",
                Title = "Порт сервиса не открывается, хотя имя находится",
                Severity = Severity.Important,
                Explanation = "Адрес сервиса получен, но подключиться к порту 443 не удалось, а остальной интернет работает. Когда DNS отвечает верно, причина обычно не в нём: " +
                              "файрвол или антивирус, включённый VPN или прокси, блокировка на стороне провайдера или недоступность самого сервиса.",
                Evidence = blocked.Select(s => $"{s.Name} ({s.Host}:{s.Port}): имя найдено, подключение не удалось").ToList(),
                ManualSteps =
                {
                    "Проверьте на другом устройстве в той же сети: если там тоже не открывается, это сервис или провайдер.",
                    "Отключите VPN и прокси, проверьте файрвол и антивирус (разрешите клиент игры).",
                    "Проверка вручную: Test-NetConnection " + blocked[0].Host + " -Port " + blocked[0].Port + " — TcpTestSucceeded покажет результат.",
                },
            };

        var slow = games.Where(s => s.SystemDnsOk && s.PublicDnsOk && s.SystemDnsMs >= SlowDnsMs && s.SystemDnsMs >= 2 * Math.Max(1, s.PublicDnsMs)).ToList();
        if (slow.Count >= 2 && dnsBroken.Count == 0)
            yield return new Finding
            {
                Id = "NET_DNS_SLOW",
                Title = "Медленный DNS-сервер",
                Severity = Severity.Advice,
                Explanation = "Системный DNS отвечает заметно медленнее публичного. Это замедляет начало подключения (вход в игру, открытие страниц и магазинов), но не пинг во время игры.",
                Evidence = slow.Select(s => $"{s.Name}: системный DNS {s.SystemDnsMs} мс, 1.1.1.1 — {s.PublicDnsMs} мс").ToList(),
                ManualSteps = { "Смените DNS на 1.1.1.1 и 8.8.8.8: Параметры → Сеть и Интернет → подключение → «Назначение DNS-сервера»." },
                Links = { Sources.NetworkStatus },
            };
    }
}
