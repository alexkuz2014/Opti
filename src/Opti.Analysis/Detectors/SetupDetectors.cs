using System.Text.RegularExpressions;
using Opti.Core.Findings;
using Opti.Core.Fixes;
using Opti.Core.Profile;

namespace Opti.Analysis.Detectors;

/// <summary>Видеокарта без драйвера производителя («Базовый видеоадаптер Майкрософт») — частая картина после переустановки Windows.</summary>
public sealed class GpuDriverMissingDetector : DetectorBase
{
    public override string Id => "GPU_DRIVER_MISSING";

    protected override IEnumerable<Finding> Run(SystemProfile p, DetectionContext ctx)
    {
        var basic = p.Gpus.Where(g => g.UsingBasicDriver).ToList();
        if (basic.Count == 0) yield break;
        var f = new Finding
        {
            Id = Id,
            Title = "Не установлен драйвер видеокарты",
            Severity = Severity.Critical,
            Explanation = "Видеокарта работает на стандартном драйвере Windows. В таком режиме нет 3D-ускорения в полную силу, высоких частот экрана и функций игр — " +
                          "производительность может быть в разы ниже. Установите драйвер с сайта производителя видеокарты (для ноутбука — можно с сайта производителя ноутбука).",
            Evidence = basic.Select(g => $"{g.Name} ({g.PnpDeviceId})").ToList(),
            ManualSteps = { "Скачайте драйвер по ссылке ниже и установите, затем перезагрузитесь и повторите анализ." },
        };
        foreach (var g in basic)
            f.Links.Add(g.Vendor switch { GpuVendor.Nvidia => Sources.NvidiaDrivers, GpuVendor.Amd => Sources.AmdDrivers, _ => Sources.IntelDsa });
        if (f.Links.Count == 0) f.Links.Add(Sources.WindowsOptionalUpdates);
        yield return f;
    }
}

public sealed class ProblemDevicesDetector : DetectorBase
{
    public override string Id => "DEVICES_WITHOUT_DRIVERS";

    protected override IEnumerable<Finding> Run(SystemProfile p, DetectionContext ctx)
    {
        // Отключённые пользователем устройства (22) — это его выбор, не проблема
        var bad = p.ProblemDevices.Where(d => d.ErrorCode != 22).ToList();
        if (bad.Count == 0) yield break;
        bool missing = bad.Any(d => d.ErrorCode == 28);
        yield return new Finding
        {
            Id = Id,
            Title = missing ? "Есть устройства без драйверов" : "Есть устройства с ошибками",
            Severity = Severity.Important,
            Explanation = "Windows сообщает об устройствах, которые работают неправильно или вовсе без драйвера. После переустановки Windows это обычно чипсет, " +
                          "сеть, звук или датчики. Без драйвера чипсета хуже работают управление питанием, USB и шины — это влияет и на игры.",
            Evidence = bad.Take(12).Select(d => $"{d.Name}{(d.DeviceClass is null ? "" : $" [{d.DeviceClass}]")}: {d.ErrorText}").ToList(),
            ManualSteps =
            {
                "Откройте «Центр обновления Windows» → «Дополнительные параметры» → «Необязательные обновления» → «Обновления драйверов» и установите предложенное.",
                "Драйвер чипсета скачайте с сайта производителя материнской платы или ноутбука (или AMD/Intel).",
            },
            Links = { Sources.WindowsOptionalUpdates },
        };
    }
}

/// <summary>На настольном ПК с видеокартой монитор подключён к разъёму материнской платы.</summary>
public sealed class MonitorOnIgpuDetector : DetectorBase
{
    public override string Id => "MONITOR_ON_IGPU";

    protected override IEnumerable<Finding> Run(SystemProfile p, DetectionContext ctx)
    {
        if (p.Device.IsLaptop || p.DiscreteGpu is not { } dgpu || p.IntegratedGpu is not { } igpu) yield break;
        var onIgpu = p.Displays.Where(d => string.Equals(d.AdapterName, igpu.Name, StringComparison.OrdinalIgnoreCase)).ToList();
        if (onIgpu.Count == 0) yield break;
        yield return new Finding
        {
            Id = Id,
            Title = "Монитор подключён к материнской плате, а не к видеокарте",
            Severity = Severity.Critical,
            Explanation = $"В компьютере есть {dgpu.Name}, но экран выводится через встроенную графику процессора. Кадры гоняются через лишнюю копию, " +
                          "растёт задержка, а частота экрана может быть ограничена. Это одна из самых частых ошибок при сборке и переустановке.",
            Evidence = onIgpu.Select(d => $"{d.FriendlyName ?? d.DeviceName} → {d.AdapterName}").ToList(),
            IsHardwareLimitation = true,
            ManualSteps =
            {
                "Выключите компьютер и переключите кабель монитора в разъём видеокарты — он ниже, на отдельной планке сзади корпуса, а не рядом с USB.",
                "Если кабель уже в видеокарте — в BIOS выберите основным выводом PCIe/PEG вместо IGD.",
            },
        };
    }
}

public sealed class FreshInstallDetector : DetectorBase
{
    public override string Id => "FRESH_WINDOWS";
    public static readonly TimeSpan Window = TimeSpan.FromDays(21);

    protected override IEnumerable<Finding> Run(SystemProfile p, DetectionContext ctx)
    {
        if (p.Os.InstallDate is not { } installed || ctx.Now - installed > Window) yield break;
        yield return new Finding
        {
            Id = Id,
            Title = "Windows установлена недавно — чек-лист после установки",
            Severity = Severity.Advice,
            Explanation = $"Система установлена {installed:dd.MM.yyyy}. После переустановки чаще всего «теряются» драйвер видеокарты и чипсета, XMP в BIOS, " +
                          "частота экрана и схема питания. Opti проверяет всё это автоматически; ниже — то, что делаете вы.",
            Evidence = { $"Дата установки: {installed:dd.MM.yyyy}" },
            ManualSteps =
            {
                "Установите все обновления Windows и перезагрузитесь.",
                "Поставьте драйвер видеокарты с сайта NVIDIA / AMD / Intel (не только из Центра обновления).",
                "Поставьте драйвер чипсета с сайта производителя материнской платы или AMD/Intel.",
                "Если BIOS сбрасывался — снова включите XMP/EXPO.",
                "Повторите анализ Opti после установки драйверов.",
            },
            Links = { Sources.WindowsUpdate, Sources.NvidiaDrivers, Sources.AmdDrivers, Sources.IntelDsa },
        };
    }
}

public sealed partial class AmdX3dDriverDetector : DetectorBase
{
    public override string Id => "AMD_X3D_DRIVER";

    protected override IEnumerable<Finding> Run(SystemProfile p, DetectionContext ctx)
    {
        if (p.Cpu.Vendor != CpuVendor.Amd || p.Cpu.Name is not { } name) yield break;
        bool dualCcdX3d = DualCcdX3d().IsMatch(name);
        if (dualCcdX3d && p.Chipset.AmdVCacheOptimizer == false)
        {
            yield return new Finding
            {
                Id = Id,
                Title = "Для процессора X3D не установлен драйвер чипсета AMD",
                Severity = Severity.Important,
                Explanation = "У двухкристальных Ryzen X3D игры должны выполняться на кристалле с дополнительным кешем. Это обеспечивает драйвер чипсета AMD " +
                              "(AMD 3D V-Cache Performance Optimizer) вместе с Xbox Game Bar. Без него игра может попасть на «не тот» кристалл и потерять заметную часть FPS.",
                Evidence = { $"{name}: AMD 3D V-Cache Performance Optimizer не найден" },
                ManualSteps =
                {
                    "Скачайте свежий драйвер чипсета AMD для вашей платформы и установите, затем перезагрузитесь.",
                    "Не удаляйте Xbox Game Bar и не отключайте игровой режим — они нужны для определения игр.",
                },
                Links = { Sources.AmdDrivers, Sources.AmdX3dGuide },
            };
        }
        else if (name.Contains("Ryzen", StringComparison.OrdinalIgnoreCase) && p.Chipset.AmdPpmDriver == false)
        {
            yield return new Finding
            {
                Id = "AMD_CHIPSET_DRIVER",
                Title = "Драйвер чипсета AMD, похоже, не установлен",
                Severity = Severity.Advice,
                Explanation = "Драйвер чипсета AMD настраивает управление питанием и планирование под процессоры Ryzen. После переустановки Windows его часто забывают.",
                Evidence = { "AMD PPM Provisioning File Driver не найден" },
                Links = { Sources.AmdDrivers },
            };
        }
    }

    [GeneratedRegex(@"\b(7950X3D|7900X3D|9950X3D|9900X3D)\b", RegexOptions.IgnoreCase)] private static partial Regex DualCcdX3d();
}

public sealed partial class IntelHybridWin10Detector : DetectorBase
{
    public override string Id => "INTEL_HYBRID_WIN10";

    protected override IEnumerable<Finding> Run(SystemProfile p, DetectionContext ctx)
    {
        if (p.Cpu.Vendor != CpuVendor.Intel || p.Cpu.Name is not { } name || p.Os.Build == 0 || p.Os.IsWindows11) yield break;
        bool ultra = name.Contains("Core(TM) Ultra", StringComparison.OrdinalIgnoreCase) || name.Contains("Core Ultra", StringComparison.OrdinalIgnoreCase);
        bool hybridGen = Gen12Plus().IsMatch(name) && p.Cpu.Threads > p.Cpu.Cores && p.Cpu.Threads < p.Cpu.Cores * 2;
        if (!ultra && !hybridGen) yield break;
        yield return new Finding
        {
            Id = Id,
            Title = "Процессор Intel с P- и E-ядрами на Windows 10",
            Severity = Severity.Important,
            Explanation = "У этого процессора два типа ядер. Распределять игру на быстрые P-ядра помогает Intel Thread Director, но полноценно он работает только в Windows 11. " +
                          "Intel рекомендует Windows 11 для оптимальной производительности таких процессоров.",
            Evidence = { $"{name}: {p.Cpu.Cores} ядер / {p.Cpu.Threads} потоков, Windows сборки {p.Os.Build}" },
            IsHardwareLimitation = false,
            ManualSteps = { "Рассмотрите обновление до Windows 11." },
            Links = { Sources.IntelHybridWin10 },
        };
    }

    [GeneratedRegex(@"\b1[234]th Gen\b|\b1[234]\d{3}[A-Z]*\b", RegexOptions.IgnoreCase)] private static partial Regex Gen12Plus();
}

public sealed class PendingRebootDetector : DetectorBase
{
    public override string Id => "PENDING_REBOOT";

    protected override IEnumerable<Finding> Run(SystemProfile p, DetectionContext ctx)
    {
        if (p.Os.PendingReboot != true) yield break;
        yield return new Finding
        {
            Id = Id,
            Title = "Windows ждёт перезагрузку для завершения обновлений",
            Severity = Severity.Advice,
            Explanation = "Пока обновления не завершены, в фоне может работать установка, а после перезагрузки поведение системы изменится. " +
                          "Перезагрузитесь перед замерами — иначе сравнение «до / после» будет нечестным.",
            Evidence = { "В реестре есть флаг ожидающей перезагрузки" },
        };
    }
}

public sealed class PageFileDisabledDetector : DetectorBase
{
    public override string Id => "PAGEFILE_DISABLED";

    protected override IEnumerable<Finding> Run(SystemProfile p, DetectionContext ctx)
    {
        if (p.Memory.PageFileDisabled != true) yield break;
        yield return new Finding
        {
            Id = Id,
            Title = "Файл подкачки отключён",
            Severity = Severity.Important,
            Explanation = "Популярный «твик», который вредит: без файла подкачки система не может вытеснить редко используемые данные, и при заполнении памяти " +
                          "игры вылетают или зависают, а дампы ошибок не создаются. Microsoft рекомендует размер, управляемый системой.",
            Evidence = { "Не задан ни один файл подкачки" },
            FixIds = { FixIds.PageFileAuto },
            Links = { Sources.PageFile },
        };
    }
}

public sealed class InputDetector : DetectorBase
{
    public override string Id => "INPUT_SETTINGS";

    protected override IEnumerable<Finding> Run(SystemProfile p, DetectionContext ctx)
    {
        if (p.Input.MouseAcceleration == true)
            yield return new Finding
            {
                Id = "MOUSE_ACCEL_ON",
                Title = "Включено ускорение мыши («Повышенная точность указателя»)",
                Severity = Severity.Advice,
                Explanation = "С ускорением курсор проходит разное расстояние при одном и том же движении руки — в зависимости от скорости. " +
                              "Для прицеливания в играх это мешает выработать мышечную память. Многие игры читают мышь напрямую, но меню, лаунчеры и часть игр — нет.",
                Evidence = { @"HKCU\Control Panel\Mouse\MouseSpeed ≠ 0" },
                FixIds = { FixIds.MouseAccelOff },
                Links = { Sources.MouseSettings },
            };

        bool sticky = p.Input.StickyKeysFlags is { } sf && (sf & 4) != 0 && (sf & 1) == 0;
        bool filter = p.Input.FilterKeysFlags is { } ff && (ff & 4) != 0 && (ff & 1) == 0;
        if (sticky || filter)
            yield return new Finding
            {
                Id = "ACCESSIBILITY_HOTKEYS_ON",
                Title = "Включены горячие клавиши залипания",
                Severity = Severity.Advice,
                Explanation = "Пятикратное нажатие Shift (или удержание правого Shift) в игре открывает окно «Залипание клавиш» и сворачивает игру. " +
                              "Можно отключить только сочетание — сами специальные возможности останутся доступны в параметрах.",
                Evidence = new List<string> { sticky ? "Залипание клавиш: сочетание Shift×5 включено" : null!, filter ? "Фильтрация ввода: удержание правого Shift включено" : null! }.Where(x => x is not null).ToList(),
                FixIds = { FixIds.AccessibilityHotkeysOff },
            };
    }
}
