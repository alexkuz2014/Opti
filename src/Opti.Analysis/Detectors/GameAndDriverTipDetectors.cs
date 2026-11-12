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
