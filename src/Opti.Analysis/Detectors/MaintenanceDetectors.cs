using Opti.Core.Findings;
using Opti.Core.Hardware;
using Opti.Core.Os;
using Opti.Core.Profile;

namespace Opti.Analysis.Detectors;

/// <summary>Windows давно не обновлялась: нет исправлений ошибок, драйверов из Центра обновления и свежих компонентов для игр.</summary>
public sealed class OsUpdatesStaleDetector : DetectorBase
{
    public override string Id => "OS_UPDATES_STALE";
    public static readonly TimeSpan AdviceAge = TimeSpan.FromDays(75);
    public static readonly TimeSpan ImportantAge = TimeSpan.FromDays(180);

    protected override IEnumerable<Finding> Run(SystemProfile p, DetectionContext ctx)
    {
        if (p.Os.LastUpdateInstalled is not { } last) yield break;
        var age = ctx.Now - last;
        if (age < AdviceAge) yield break;
        // Выпуск, снятый с обслуживания, обновлений и не получает — об этом скажет OS_END_OF_SUPPORT
        if (WindowsLifecycle.IsOutOfServicing(p.Os.Build, ctx.Now) && !OsEndOfSupportDetector.HasExtendedServicing(p.Os)) yield break;
        int months = (int)(age.TotalDays / 30);
        yield return new Finding
        {
            Id = Id,
            Title = "Windows давно не обновлялась",
            Severity = age >= ImportantAge ? Severity.Important : Severity.Advice,
            Explanation = "Обновления Windows приносят не только защиту: в них исправляют фризы и падения в играх, ошибки планировщика процессора, " +
                          "обновляют DirectX, игровые компоненты и драйверы устройств. На системе без обновлений часть «лагов» давно исправлена — просто исправление не установлено." +
                          (p.Os.ModifiedBuild is { } mod ? $" В сборке {mod} обновления могут быть отключены намеренно." : ""),
            Evidence = { $"Последнее обновление установлено {last:dd.MM.yyyy} — {months} мес. назад", $"Сборка {p.Os.Build}.{p.Os.Ubr}" },
            ManualSteps =
            {
                "Параметры → Центр обновления Windows → «Проверить наличие обновлений». Установите всё и перезагрузитесь; повторяйте, пока не появится «Обновления не требуются».",
                "Затем откройте «Дополнительные параметры» → «Необязательные обновления» и установите драйверы.",
                "После обновления запустите анализ в Opti ещё раз.",
            },
            Links = { Sources.WindowsUpdate, Sources.WindowsOptionalUpdates },
        };
    }
}

public sealed class OsEndOfSupportDetector : DetectorBase
{
    public override string Id => "OS_END_OF_SUPPORT";

    /// <summary>Корпоративные, учебные и LTSC-редакции обслуживаются дольше — для них сроки Home/Pro не подходят.</summary>
    public static bool HasExtendedServicing(OsInfo os)
    {
        var e = (os.Edition ?? "") + " " + (os.ProductName ?? "");
        return e.Contains("Enterprise", StringComparison.OrdinalIgnoreCase) || e.Contains("Education", StringComparison.OrdinalIgnoreCase)
               || e.Contains("LTSC", StringComparison.OrdinalIgnoreCase) || e.Contains("IoT", StringComparison.OrdinalIgnoreCase)
               || e.Contains("Server", StringComparison.OrdinalIgnoreCase);
    }

    protected override IEnumerable<Finding> Run(SystemProfile p, DetectionContext ctx)
    {
        if (HasExtendedServicing(p.Os) || WindowsLifecycle.Find(p.Os.Build) is not { } rel || ctx.Now.Date <= rel.EndOfServicing) yield break;
        bool win10 = !p.Os.IsWindows11;
        var f = new Finding
        {
            Id = Id,
            Title = win10 ? "Windows 10 больше не получает обновления" : $"{rel.Name} больше не получает обновления",
            Severity = Severity.Advice,
            Explanation = win10
                ? "Поддержка Windows 10 закончилась 14 октября 2025 года. Система продолжит работать, но исправлений больше не будет, " +
                  "а новые драйверы видеокарт и игры постепенно перестают её поддерживать. Если компьютер подходит для Windows 11 — обновление бесплатное. " +
                  "Если не подходит (старый процессор, нет TPM 2.0) — оставайтесь на Windows 10: Opti настроит её так же."
                : $"Обслуживание этого выпуска закончилось {rel.EndOfServicing:dd.MM.yyyy}. Новый выпуск Windows 11 ставится через Центр обновления как обычное обновление, " +
                  "файлы и программы остаются на месте. В новых выпусках исправлены ошибки планировщика и игровых компонентов.",
            Evidence = { $"{p.Os.ProductName} {p.Os.DisplayVersion}, сборка {p.Os.Build}" },
            ManualSteps =
            {
                win10 ? "Параметры → Обновление и безопасность → Центр обновления Windows: если там есть предложение Windows 11 — компьютер подходит."
                      : "Параметры → Центр обновления Windows → установите предлагаемое обновление компонентов.",
            },
            Links = { win10 ? Sources.Windows10EndOfSupport : Sources.WindowsLifecycle, Sources.WindowsUpdate },
        };
        yield return f;
    }
}

/// <summary>Видеокарта снята производителем с обычной поддержки: новых игровых драйверов не будет.</summary>
public sealed class GpuLegacyDetector : DetectorBase
{
    public override string Id => "GPU_LEGACY";

    protected override IEnumerable<Finding> Run(SystemProfile p, DetectionContext ctx)
    {
        // Важна та видеокарта, на которой идут игры: при наличии дискретной встроенную не обсуждаем
        var gaming = p.DiscreteGpu ?? p.IntegratedGpu;
        if (gaming is null || GpuSupport.Legacy(gaming) is not { } legacy) yield break;
        bool driverBehind = gaming.DriverDate is { } d && d < legacy.LastFeatureDriver.AddDays(-90);
        var f = new Finding
        {
            Id = Id,
            Title = "Видеокарта больше не получает игровые драйверы",
            Severity = Severity.Advice,
            Explanation = legacy.Note + " Видеокарта при этом исправна и продолжит работать. " +
                          (driverBehind ? "У вас установлен драйвер старее последнего — стоит поставить финальную версию один раз. "
                                        : "Установленный драйвер — из последних, обновлять его больше не нужно. ") +
                          "В новых играх основной резерв — настройки графики: разрешение, масштабирование (FSR / XeSS работают на любых видеокартах), тени и сглаживание.",
            Evidence = { $"{gaming.Name}" + (gaming.DriverDate is { } dd ? $": драйвер от {dd:dd.MM.yyyy}" : "") },
            IsHardwareLimitation = true,
        };
        if (driverBehind)
        {
            f.ManualSteps.Add("Скачайте последний драйвер для этой видеокарты с официального сайта и установите его.");
            f.Links.Add(gaming.Vendor switch
            {
                GpuVendor.Nvidia => Sources.NvidiaDrivers,
                GpuVendor.Amd => Sources.AmdDrivers,
                _ => Sources.IntelDsa,
            });
        }
        f.ManualSteps.Add("В играх включайте FSR или XeSS в режиме «Качество» / «Баланс» — это даёт больше FPS, чем любые настройки Windows.");
        yield return f;
    }
}

/// <summary>Ноутбук работает от батареи: процессор и видеокарта ограничены прошивкой, настройками Windows это не снять.</summary>
public sealed class OnBatteryDetector : DetectorBase
{
    public override string Id => "POWER_ON_BATTERY";

    protected override IEnumerable<Finding> Run(SystemProfile p, DetectionContext ctx)
    {
        if (!p.Device.IsLaptop || p.Power.OnAcPower != false) yield break;
        yield return new Finding
        {
            Id = Id,
            Title = "Ноутбук работает от батареи",
            Severity = Severity.Important,
            Explanation = "От батареи ноутбук ограничивает мощность процессора и видеокарты — FPS в играх падает в полтора-два раза, а дискретная видеокарта может не включаться вовсе. " +
                          "Это защита батареи, зашитая производителем. Подключите зарядное устройство и запустите анализ ещё раз: часть проверок и исправлений работает только от сети.",
            Evidence = { p.Power.BatteryPercent is { } pct ? $"Питание от батареи, заряд {pct} %" : "Питание от батареи" },
            ManualSteps = { "Подключите штатное зарядное устройство (не слабый USB-C адаптер) и повторите анализ." },
        };
    }
}

/// <summary>Программа, которая во время анализа занимала заметную часть процессора.</summary>
public sealed class BackgroundCpuHogDetector : DetectorBase
{
    public override string Id => "BG_CPU_HOG";
    public const double ThresholdPercent = 12;

    /// <summary>Сам Opti и то, что он запускает для сбора данных, а также ядро системы.</summary>
    private static readonly HashSet<string> Ignored = new(StringComparer.OrdinalIgnoreCase)
    {
        "Opti", "opti-cli", "dotnet", "powershell", "pwsh", "conhost", "WmiPrvSE", "cmd", "nvidia-smi", "dxdiag", "bcdedit", "powercfg",
        "System", "Idle", "Registry", "Memory Compression", "dwm", "csrss", "svchost", "explorer", "Taskmgr",
    };

    private static readonly (string Process, string Hint)[] Hints =
    {
        ("MsMpEng", "идёт проверка Microsoft Defender — дождитесь окончания; игровые папки можно добавить в исключения"),
        ("TiWorker", "Windows устанавливает обновления — дождитесь окончания и перезагрузитесь"),
        ("TrustedInstaller", "Windows устанавливает обновления — дождитесь окончания и перезагрузитесь"),
        ("MoUsoCoreWorker", "Windows проверяет обновления — это ненадолго"),
        ("SearchIndexer", "Windows индексирует файлы — после переустановки или большого копирования это нормально, пройдёт само"),
        ("CompatTelRunner", "сбор телеметрии совместимости Windows — завершится сам"),
        ("chrome", "закройте лишние вкладки браузера перед игрой"),
        ("msedge", "закройте лишние вкладки браузера перед игрой"),
        ("firefox", "закройте лишние вкладки браузера перед игрой"),
        ("browser", "закройте лишние вкладки браузера перед игрой"),
    };

    protected override IEnumerable<Finding> Run(SystemProfile p, DetectionContext ctx)
    {
        var hogs = p.Background.HeavyProcesses
            .Where(h => !Ignored.Contains(h.Name))
            // Браузеры и лаунчеры работают десятком процессов — сравниваем с порогом сумму по программе
            .GroupBy(h => h.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => (Name: g.Key, Cpu: g.Sum(x => x.CpuPercent ?? 0)))
            .Where(x => x.Cpu >= ThresholdPercent)
            .OrderByDescending(x => x.Cpu).Take(5).ToList();
        if (hogs.Count == 0) yield break;
        var f = new Finding
        {
            Id = Id,
            Title = "Фоновая программа нагружает процессор",
            Severity = hogs[0].Cpu >= 25 ? Severity.Important : Severity.Advice,
            Explanation = "Во время анализа, когда компьютер должен простаивать, эти программы занимали заметную часть процессора. " +
                          "В игре они отнимают те же ядра: падает минимальный FPS и появляются рывки. Если программа вам незнакома — проверьте её антивирусом.",
            Evidence = hogs.Select(h => $"{h.Name}: {h.Cpu:0} % процессора").ToList(),
        };
        foreach (var h in hogs)
        {
            var hint = Hints.FirstOrDefault(x => h.Name.Contains(x.Process, StringComparison.OrdinalIgnoreCase)).Hint;
            f.ManualSteps.Add(hint is not null ? $"{h.Name}: {hint}." : $"{h.Name}: закройте перед игрой, если программа не нужна.");
        }
        yield return f;
    }
}

/// <summary>Игры лежат на жёстком диске, хотя в компьютере есть SSD.</summary>
public sealed class GamesOnHddDetector : DetectorBase
{
    public override string Id => "GAME_ON_HDD";

    protected override IEnumerable<Finding> Run(SystemProfile p, DetectionContext ctx)
    {
        var hddLetters = p.Disks.Where(d => d.MediaType == DiskMediaType.Hdd).SelectMany(d => d.DriveLetters)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (hddLetters.Count == 0) yield break;
        var onHdd = p.Games.Where(g => DriveOf(g) is { } l && hddLetters.Contains(l)).ToList();
        if (onHdd.Count == 0) yield break;
        var ssd = p.Disks.Where(d => d.MediaType is DiskMediaType.Ssd or DiskMediaType.Nvme).ToList();
        var f = new Finding
        {
            Id = Id,
            Title = "Игры установлены на жёсткий диск",
            Severity = Severity.Advice,
            Explanation = "Современные игры подгружают текстуры и уровни прямо во время игры. С жёсткого диска это в десятки раз медленнее, чем с SSD: " +
                          "долгие загрузки, подгрузочные фризы, «мыльные» текстуры. Средний FPS при этом может быть нормальным." +
                          (ssd.Count > 0 ? " В компьютере есть SSD — перенесите на него игры, в которые играете сейчас." : " Поможет только SSD: даже недорогой SATA решает проблему."),
            Evidence = onHdd.Take(6).Select(g => $"{g.Name}: {g.InstallDir}").ToList(),
            IsHardwareLimitation = ssd.Count == 0,
        };
        if (onHdd.Count > 6) f.Evidence.Add($"…и ещё {onHdd.Count - 6}");
        if (ssd.Count > 0)
        {
            f.ManualSteps.Add("Steam: Настройки → Хранилище → выберите игру → «Переместить». Epic и Battle.net — через «Переместить» / смену папки в настройках игры.");
            f.ManualSteps.Add("Переносите в первую очередь игры с открытым миром и онлайн-шутеры.");
        }
        yield return f;
    }

    private static string? DriveOf(GameInfo g)
    {
        var path = g.InstallDir ?? g.Executables.FirstOrDefault();
        return path is { Length: >= 2 } && path[1] == ':' && char.IsLetter(path[0]) ? char.ToUpperInvariant(path[0]) + ":" : null;
    }
}
