namespace Opti.Core.Os;

/// <summary>Выпуск Windows и дата окончания его обслуживания (редакции Home / Pro).</summary>
public sealed record WindowsRelease(int Build, string Name, DateTime EndOfServicing);

/// <summary>
/// Сроки обслуживания выпусков Windows 10/11 для редакций Home и Pro по данным Microsoft Lifecycle.
/// Выпуск без обновлений не получает исправлений безопасности и ошибок; новые драйверы и игры на нём не проверяются.
/// </summary>
public static class WindowsLifecycle
{
    /// <summary>Все выпуски Windows 10 закончили обслуживание 14 октября 2025 года (дальше — только платная/условная программа ESU).</summary>
    public static readonly DateTime Windows10End = new(2025, 10, 14);

    public static readonly IReadOnlyList<WindowsRelease> Windows11 = new WindowsRelease[]
    {
        new(22000, "Windows 11 21H2", new DateTime(2023, 10, 10)),
        new(22621, "Windows 11 22H2", new DateTime(2024, 10, 8)),
        new(22631, "Windows 11 23H2", new DateTime(2025, 11, 11)),
        new(26100, "Windows 11 24H2", new DateTime(2026, 10, 13)),
        new(26200, "Windows 11 25H2", new DateTime(2027, 10, 12)),
    };

    /// <summary>Выпуск по номеру сборки; null — сборка новее известных (сроки неизвестны, считаем поддерживаемой).</summary>
    public static WindowsRelease? Find(int build)
    {
        if (build <= 0) return null;
        if (build < 22000) return new WindowsRelease(build, "Windows 10", Windows10End);
        // Сборки между известными выпусками (предварительные) относим к предыдущему выпуску
        var known = Windows11.LastOrDefault(r => build >= r.Build);
        return known is not null && build > Windows11[^1].Build ? null : known;
    }

    public static bool IsOutOfServicing(int build, DateTimeOffset now) =>
        Find(build) is { } r && now.Date > r.EndOfServicing;
}
