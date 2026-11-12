using System.Text.RegularExpressions;
using Opti.Core.Profile;

namespace Opti.Core.Hardware;

/// <summary>Статус поддержки видеокарты производителем.</summary>
public sealed record GpuLegacyInfo(string Family, DateTime LastFeatureDriver, string Note);

/// <summary>
/// Видеокарты, для которых производитель прекратил выпуск обычных драйверов.
/// Для них «драйвер старше полутора лет» — норма, а не проблема: новее просто не существует.
/// </summary>
public static partial class GpuSupport
{
    public static GpuLegacyInfo? Legacy(GpuInfo g) => Legacy(g.Vendor, g.Name);

    /// <summary>
    /// Устарел ли драйвер. Для снятых с поддержки видеокарт сравниваем с датой последнего драйвера,
    /// а не с сегодняшним днём: новее его уже не будет.
    /// </summary>
    public static bool IsDriverOutdated(GpuVendor vendor, string name, DateTime driverDate, DateTimeOffset now, TimeSpan maxAge)
    {
        if (Legacy(vendor, name) is { } legacy) return driverDate < legacy.LastFeatureDriver.AddDays(-90);
        return now - driverDate > maxAge;
    }

    public static GpuLegacyInfo? Legacy(GpuVendor vendor, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        switch (vendor)
        {
            case GpuVendor.Nvidia:
                // Maxwell и Pascal: последний игровой драйвер — ветка 580 (октябрь 2025), далее только обновления безопасности
                if (NvPascalMaxwell().IsMatch(name))
                    return new("NVIDIA GeForce GTX 900 / 10", new DateTime(2025, 10, 1),
                        "Последний игровой драйвер вышел в октябре 2025 года (ветка 580). Дальше NVIDIA выпускает для этих видеокарт только обновления безопасности — до октября 2028 года.");
                // Kepler: ветка 470, поддержка закончилась в 2021 году
                if (NvKepler().IsMatch(name))
                    return new("NVIDIA GeForce GTX 600 / 700", new DateTime(2021, 10, 1),
                        "Игровые драйверы для этих видеокарт закончились в 2021 году (ветка 470). Новые игры на них не оптимизируются.");
                break;
            case GpuVendor.Amd:
                if (AmdPre400().IsMatch(name))
                    return new("AMD Radeon HD 7000 – R9 300", new DateTime(2022, 6, 1),
                        "AMD перевела эти видеокарты в устаревшие в 2021 году; последний драйвер — Adrenalin 22.6.1.");
                if (AmdPolarisVega().IsMatch(name))
                    return new("AMD Radeon RX 400 / 500 / Vega", new DateTime(2023, 11, 1),
                        "С ноября 2023 года AMD выпускает для этих видеокарт отдельный драйвер только с критическими исправлениями.");
                break;
            case GpuVendor.Intel:
                if (IntelOldIgpu().IsMatch(name))
                    return new("Intel HD / UHD Graphics (6–10 поколение)", new DateTime(2022, 7, 1),
                        "Intel перевела графику процессоров 6–10 поколения в устаревшие: выходят только редкие исправления безопасности.");
                break;
        }
        return null;
    }

    // GTX 745/750, GTX 950…980 Ti, GTX 1050…1080 Ti, GT 1010/1030, TITAN X / Xp, MX110–MX350
    [GeneratedRegex(@"\b(GTX\s*(7[45]0|9[5-8]0|10[5-8]0)|GT\s*10[13]0|TITAN\s*X|MX\s*[1-3][135]0)", RegexOptions.IgnoreCase)]
    private static partial Regex NvPascalMaxwell();

    [GeneratedRegex(@"\b(GTX\s*(6[0-9]0|7[6-8]0)|GTX\s*TITAN(?!\s*X))", RegexOptions.IgnoreCase)]
    private static partial Regex NvKepler();

    [GeneratedRegex(@"\b(HD\s*[78][0-9]{3}|R[579]\s*[23][0-9]{2}|R9\s*(Fury|Nano))", RegexOptions.IgnoreCase)]
    private static partial Regex AmdPre400();

    [GeneratedRegex(@"\b(RX\s*[45][5-9]0|RX\s*Vega|Vega\s*(56|64)|Radeon\s*VII)\b", RegexOptions.IgnoreCase)]
    private static partial Regex AmdPolarisVega();

    // HD Graphics 510–530, HD/UHD Graphics 610–655, Iris 540–655, Iris Plus (10 поколение)
    [GeneratedRegex(@"\b(U?HD\s*Graphics\s*(5[0-9]{2}|6[0-9]{2})|Iris\s*(Pro\s*)?(Plus\s*)?Graphics\s*(5[0-9]{2}|6[0-9]{2})?)\b", RegexOptions.IgnoreCase)]
    private static partial Regex IntelOldIgpu();
}
