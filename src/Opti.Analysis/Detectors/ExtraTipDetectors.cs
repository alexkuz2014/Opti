using Opti.Core.Findings;
using Opti.Core.Profile;

namespace Opti.Analysis.Detectors;

/// <summary>Старая прошивка материнской платы: в новых версиях исправляют совместимость памяти, процессоров и стабильность.</summary>
public sealed class BiosOldDetector : DetectorBase
{
    public override string Id => "BIOS_OLD";
    public static readonly TimeSpan OldAge = TimeSpan.FromDays(365 * 4);

    protected override IEnumerable<Finding> Run(SystemProfile p, DetectionContext ctx)
    {
        if (p.Device.IsVirtualMachine || p.Device.BiosDate is not { } date || date.Year < 2005) yield break;
        var age = ctx.Now - date;
        if (age < OldAge) yield break;
        yield return new Finding
        {
            Id = Id,
            Title = "BIOS давно не обновлялся",
            Severity = Severity.Advice,
            Explanation = "Производители плат и ноутбуков исправляют в новых версиях BIOS совместимость памяти и процессоров, ошибки питания и стабильность. " +
                          "Прироста FPS это само по себе не даёт, но если есть вылеты, проблемы с XMP или новый процессор на старой плате — начните с обновления. Если всё работает стабильно, можно не трогать.",
            Evidence = { $"Версия BIOS: {p.Device.BiosVersion ?? "неизвестна"} от {date:dd.MM.yyyy} — {(int)(age.TotalDays / 365)} лет назад", $"Устройство: {p.Device.Manufacturer} {p.Device.Model}".Trim() },
            ManualSteps =
            {
                "Найдите страницу поддержки своей платы или ноутбука на сайте производителя и сравните версию BIOS.",
                "Обновляйте только по инструкции производителя и не выключайте питание в процессе; на ноутбуке — от сети и с заряженной батареей.",
                "Если включён BitLocker, перед обновлением сохраните ключ восстановления.",
            },
        };
    }
}

/// <summary>Виртуальная машина: замеры и часть советов не отражают настоящий компьютер.</summary>
public sealed class VirtualMachineDetector : DetectorBase
{
    public override string Id => "VIRTUAL_MACHINE";

    protected override IEnumerable<Finding> Run(SystemProfile p, DetectionContext ctx)
    {
        if (!p.Device.IsVirtualMachine) yield break;
        yield return new Finding
        {
            Id = Id,
            Title = "Opti запущен в виртуальной машине",
            Severity = Severity.Advice,
            Explanation = "В виртуальной машине видеокарта, таймеры и диски эмулируются. Результаты теста и часть находок относятся к виртуальной машине, а не к настоящему компьютеру. " +
                          "Чтобы оптимизировать ПК для игр, запустите Opti в основной системе.",
            Evidence = { $"Устройство: {p.Device.Manufacturer} {p.Device.Model}".Trim() },
            IsHardwareLimitation = true,
        };
    }
}

/// <summary>Защита системы выключена: точку восстановления перед изменениями создать не получится.</summary>
public sealed class SystemRestoreOffDetector : DetectorBase
{
    public override string Id => "SYSTEM_RESTORE_OFF";

    protected override IEnumerable<Finding> Run(SystemProfile p, DetectionContext ctx)
    {
        if (p.Security.SystemRestoreEnabled != false) yield break;
        yield return new Finding
        {
            Id = Id,
            Title = "Выключены точки восстановления Windows",
            Severity = Severity.Advice,
            Explanation = "Перед изменениями Opti создаёт точку восстановления — запасной способ вернуть систему, если что-то пойдёт не так (например, после драйвера). " +
                          "Сейчас защита системы выключена, и точка не создастся. Свой журнал изменений Opti ведёт в любом случае, откат исправлений работает.",
            Evidence = { "Защита системы для диска Windows: выключена" },
            ManualSteps =
            {
                "Нажмите Win+R, введите SystemPropertiesProtection и нажмите Enter.",
                "Выберите системный диск → «Настроить» → «Включить защиту системы», отведите 3–5 % диска.",
            },
        };
    }
}

/// <summary>Разные модули памяти: система работает на параметрах самого медленного, профиль XMP может быть нестабилен.</summary>
public sealed class RamMixedModulesDetector : DetectorBase
{
    public override string Id => "RAM_MIXED_MODULES";

    protected override IEnumerable<Finding> Run(SystemProfile p, DetectionContext ctx)
    {
        var mods = p.Memory.Modules.Where(m => m.CapacityMb > 0).ToList();
        if (mods.Count < 2) yield break;
        bool sizes = mods.Select(m => m.CapacityMb).Distinct().Count() > 1;
        var parts = mods.Select(m => (m.PartNumber ?? "").Trim()).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        bool models = parts.Count > 1;
        bool speeds = mods.Where(m => m.RatedSpeed > 0).Select(m => m.RatedSpeed).Distinct().Count() > 1;
        if (!sizes && !models && !speeds) yield break;
        yield return new Finding
        {
            Id = Id,
            Title = "Установлены разные модули памяти",
            Severity = Severity.Advice,
            Explanation = "Память из разных комплектов работает на частоте и таймингах самого медленного модуля, а профиль XMP/EXPO с такими наборами не всегда стабилен. " +
                          "Если игры вылетают или появляются синие экраны после включения XMP — причина может быть здесь. Если всё стабильно, менять ничего не нужно.",
            Evidence = mods.Select(m => $"{m.Slot ?? "слот ?"}: {m.CapacityMb / 1024.0:0.#} ГБ, {(m.RatedSpeed > 0 ? m.RatedSpeed + " МГц" : "частота ?")}, {(string.IsNullOrWhiteSpace(m.PartNumber) ? "модель ?" : m.PartNumber!.Trim())}").ToList(),
            ManualSteps =
            {
                "При нестабильности с XMP/EXPO: выключите профиль в BIOS и проверьте, пропали ли вылеты.",
                "При покупке памяти берите готовый комплект из двух одинаковых модулей.",
            },
            Links = { Sources.IntelXmp },
            IsHardwareLimitation = true,
        };
    }
}

/// <summary>Основным назначен монитор с меньшей частотой: игры по умолчанию открываются на нём.</summary>
public sealed class PrimaryDisplaySlowDetector : DetectorBase
{
    public override string Id => "DISP_PRIMARY_SLOW";

    protected override IEnumerable<Finding> Run(SystemProfile p, DetectionContext ctx)
    {
        if (p.Displays.Count < 2 || p.Displays.FirstOrDefault(d => d.IsPrimary) is not { } primary) yield break;
        var fastest = p.Displays.OrderByDescending(d => d.MaxHzAtCurrentResolution).First();
        if (ReferenceEquals(fastest, primary) || fastest.MaxHzAtCurrentResolution < 100 || primary.MaxHzAtCurrentResolution <= 0) yield break;
        if (fastest.MaxHzAtCurrentResolution - primary.MaxHzAtCurrentResolution < 30) yield break;
        yield return new Finding
        {
            Id = Id,
            Title = "Основным выбран медленный монитор",
            Severity = Severity.Important,
            Explanation = "Игры по умолчанию запускаются на основном мониторе. Сейчас основной — тот, у которого частота обновления ниже, поэтому игра может идти на нём, а быстрый монитор простаивает.",
            Evidence =
            {
                $"Основной: {primary.FriendlyName ?? primary.DeviceName} — до {primary.MaxHzAtCurrentResolution} Гц",
                $"Быстрый: {fastest.FriendlyName ?? fastest.DeviceName} — до {fastest.MaxHzAtCurrentResolution} Гц",
            },
            ManualSteps =
            {
                "Параметры → Система → Дисплей: выберите быстрый монитор и отметьте «Сделать основным дисплеем».",
                "Либо в самой игре выберите нужный монитор в настройках экрана.",
            },
            Links = { Sources.DisplaySettings },
        };
    }
}

/// <summary>Монитор 60 Гц: кадры сверх 60 не видны — ограничение FPS снимет лишний нагрев и шум.</summary>
public sealed class Display60HzCapTipDetector : DetectorBase
{
    public override string Id => "DISP_60HZ_CAP_TIP";

    protected override IEnumerable<Finding> Run(SystemProfile p, DetectionContext ctx)
    {
        if (p.DiscreteGpu is null || p.Displays.Count == 0) yield break;
        if (p.Displays.Any(d => d.MaxHzAtCurrentResolution > 75 || d.MaxHzAtCurrentResolution <= 0)) yield break;
        int hz = p.Displays.Max(d => d.MaxHzAtCurrentResolution);
        yield return new Finding
        {
            Id = Id,
            Title = $"Монитор показывает не больше {hz} кадров в секунду",
            Severity = Severity.Advice,
            Explanation = $"Всё, что видеокарта рисует сверх {hz} FPS, монитор не покажет. Ограничение частоты кадров в лёгких играх снижает нагрев и шум, а на ноутбуке — расход батареи. " +
                          "В соревновательных шутерах высокий FPS всё же уменьшает задержку управления — там ограничение не ставьте. Самое заметное улучшение плавности — монитор на 120–144 Гц.",
            Evidence = p.Displays.Select(d => $"{d.FriendlyName ?? d.DeviceName}: {d.Width}×{d.Height}, максимум {d.MaxHzAtCurrentResolution} Гц").ToList(),
            ManualSteps =
            {
                $"В настройках игры включите ограничение FPS на {hz} или вертикальную синхронизацию.",
                "Либо задайте предел в панели драйвера видеокарты (NVIDIA: «Макс. частота кадров», AMD: Radeon Chill / FRTC).",
            },
            Links = { Sources.RefreshRate },
            IsHardwareLimitation = true,
        };
    }
}

/// <summary>Высокое разрешение на встроенной графике: главный рычаг — масштаб рендера, а не системные настройки.</summary>
public sealed class HeavyResolutionDetector : DetectorBase
{
    public override string Id => "DISP_RES_HEAVY";

    protected override IEnumerable<Finding> Run(SystemProfile p, DetectionContext ctx)
    {
        if (p.Displays.Count == 0 || p.Gpus.Count == 0) yield break;
        var d = p.Displays.FirstOrDefault(x => x.IsPrimary) ?? p.Displays[0];
        long pixels = (long)d.Width * d.Height;
        var dgpu = p.DiscreteGpu;
        bool igpuOnly = dgpu is null && p.IntegratedGpu is not null;
        bool heavy = (igpuOnly && pixels > 1920L * 1200) || (dgpu is not null && dgpu.DedicatedVramMb is > 0 and <= 4096 && pixels >= 3840L * 2160);
        if (!heavy) yield break;
        yield return new Finding
        {
            Id = Id,
            Title = "Разрешение экрана тяжёлое для этой графики",
            Severity = Severity.Important,
            Explanation = $"Экран {d.Width}×{d.Height} — это {pixels / 2073600.0:0.#}× больше точек, чем Full HD, и видеокарте приходится рисовать их все. " +
                          "Никакая настройка Windows это не компенсирует: самый большой прирост даст масштаб рендера или пониженное разрешение в самой игре.",
            Evidence = { $"Экран: {d.Width}×{d.Height}", igpuOnly ? $"Графика: {p.IntegratedGpu!.Name} (встроенная)" : $"Видеокарта: {dgpu!.Name}, {dgpu.DedicatedVramMb / 1024.0:0.#} ГБ памяти" },
            ManualSteps =
            {
                "В игре включите масштабирование: FSR, XeSS или DLSS в режиме «Качество» / «Баланс», либо «Масштаб рендера» 67–75 %.",
                "Если масштабирования нет — выберите в игре 1920×1080; интерфейс Windows при этом трогать не нужно.",
            },
            IsHardwareLimitation = true,
        };
    }
}
