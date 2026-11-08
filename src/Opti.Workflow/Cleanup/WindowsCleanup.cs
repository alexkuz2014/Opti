using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Opti.Fixes.Backends;
using Opti.Native;

namespace Opti.Workflow.Cleanup;

/// <summary>Корзина всех дисков (Shell API). Удаление из корзины необратимо — об этом предупреждаем.</summary>
[SupportedOSPlatform("windows")]
public sealed class RecycleBinTarget : ICleanupTarget
{
    // В x64 shellapi.h использует выравнивание 8 (размер 24 байта) — Pack не задаём, Opti только для x64.
    [StructLayout(LayoutKind.Sequential)]
    private struct SHQUERYRBINFO { public int cbSize; public long i64Size; public long i64NumItems; }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHQueryRecycleBinW(string? root, ref SHQUERYRBINFO info);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHEmptyRecycleBinW(IntPtr hwnd, string? root, uint flags);
    private const uint SHERB_NOCONFIRMATION = 1, SHERB_NOPROGRESSUI = 2, SHERB_NOSOUND = 4;

    public string Id => "recycle";
    public string Title => "Корзина";
    public string Description => "Файлы, которые вы уже удалили. После очистки их не восстановить.";
    public bool DefaultSelected => true;
    public string? Caution => "Проверьте, что в корзине нет нужного — восстановить будет нельзя.";
    public string? ManualUri => null;

    public Task<CleanupEstimate> EstimateAsync(CancellationToken ct = default) => Task.Run(() =>
    {
        var info = new SHQUERYRBINFO { cbSize = Marshal.SizeOf<SHQUERYRBINFO>() };
        return SHQueryRecycleBinW(null, ref info) == 0 ? new CleanupEstimate(info.i64Size, (int)Math.Min(int.MaxValue, info.i64NumItems)) : new CleanupEstimate(0, 0);
    }, ct);

    public async Task<CleanupResult> CleanAsync(CancellationToken ct = default)
    {
        var before = await EstimateAsync(ct);
        var r = new CleanupResult();
        int hr = await Task.Run(() => SHEmptyRecycleBinW(IntPtr.Zero, null, SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND), ct);
        // S_OK или «корзина уже пуста» (0x8000FFFF на некоторых системах)
        if (hr == 0 || before.Files == 0) { r.FreedBytes = before.Bytes; r.Deleted = before.Files; }
        else r.Errors.Add($"Корзина: код 0x{hr:X8}");
        return r;
    }
}

/// <summary>Кеш оптимизации доставки (P2P-раздача обновлений Windows) — через штатную команду PowerShell.</summary>
[SupportedOSPlatform("windows")]
public sealed class DeliveryOptimizationTarget : ICleanupTarget
{
    public const string CacheDir = @"C:\Windows\ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization\Cache";
    public string Id => "delivery-optimization";
    public string Title => "Кеш оптимизации доставки";
    public string Description => "Копии обновлений Windows, которые раздаются другим компьютерам. Windows очищает его и сама, но не всегда вовремя.";
    public bool DefaultSelected => true;
    public string? Caution => null;
    public string? ManualUri => null;

    public Task<CleanupEstimate> EstimateAsync(CancellationToken ct = default) =>
        new FolderCleanupTarget(Id, Title, Description, () => new[] { CacheDir }, TimeSpan.Zero).EstimateAsync(ct);

    public async Task<CleanupResult> CleanAsync(CancellationToken ct = default)
    {
        var before = await EstimateAsync(ct);
        var r = new CleanupResult();
        var pr = await Task.Run(() => ProcessRunner.Instance.RunPowerShell("Delete-DeliveryOptimizationCache -Force", TimeSpan.FromMinutes(2)), ct);
        var after = await EstimateAsync(ct);
        r.FreedBytes = Math.Max(0, before.Bytes - after.Bytes);
        r.Deleted = Math.Max(0, before.Files - after.Files);
        if (!pr.Ok) r.Errors.Add("Оптимизация доставки: " + pr.StdErr.Trim());
        return r;
    }
}

public static class CleanupCatalog
{
    private static string Local => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    private static string Win => Environment.GetFolderPath(Environment.SpecialFolder.Windows);
    private static string ProgramData => Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

    /// <summary>Подпапки профилей браузеров на основе Chromium: …\User Data\Default|Profile N\{Cache,Code Cache,GPUCache}.</summary>
    private static IEnumerable<string> ChromiumCaches(string userData)
    {
        if (!Directory.Exists(userData)) yield break;
        foreach (var profile in Directory.EnumerateDirectories(userData).Where(d => Path.GetFileName(d) is "Default" || Path.GetFileName(d).StartsWith("Profile ", StringComparison.Ordinal)))
            foreach (var sub in new[] { "Cache", "Code Cache", "GPUCache" })
                yield return Path.Combine(profile, sub);
    }

    [SupportedOSPlatform("windows")]
    public static IReadOnlyList<ICleanupTarget> ForWindows() => new ICleanupTarget[]
    {
        new RecycleBinTarget(),
        new FolderCleanupTarget("user-temp", "Временные файлы", "Папка TEMP вашего профиля: остатки установщиков и программ. Удаляются только файлы старше двух дней.",
            () => new[] { Path.GetTempPath() }, TimeSpan.FromDays(2)),
        new FolderCleanupTarget("windows-temp", "Временные файлы Windows", "Системная папка Windows\\Temp. Только файлы старше двух дней.",
            () => new[] { Path.Combine(Win, "Temp") }, TimeSpan.FromDays(2)),
        new FolderCleanupTarget("wu-download", "Загруженные обновления Windows", "Установочные файлы уже установленных обновлений (старше 10 дней). При необходимости Windows скачает их снова.",
            () => new[] { Path.Combine(Win, "SoftwareDistribution", "Download") }, TimeSpan.FromDays(10)),
        new DeliveryOptimizationTarget(),
        new FolderCleanupTarget("wer", "Отчёты об ошибках", "Отчёты Windows о сбоях программ, которые уже отправлены или не нужны.",
            () => new[] { Path.Combine(ProgramData, @"Microsoft\Windows\WER\ReportArchive"), Path.Combine(ProgramData, @"Microsoft\Windows\WER\ReportQueue"), Path.Combine(Local, @"Microsoft\Windows\WER") },
            TimeSpan.FromDays(1)),
        new FolderCleanupTarget("opti-old", "Старые отчёты Opti", "HTML-отчёты и журналы Opti старше 30 дней (журнал отката не затрагивается).",
            () => new[] { OptiPaths.ReportsDir, OptiPaths.LogsDir }, TimeSpan.FromDays(30)),
        new FolderCleanupTarget("thumbnails", "Кеш миниатюр", "Картинки-превью в Проводнике. Будут созданы заново при открытии папок.",
            () => new[] { Path.Combine(Local, @"Microsoft\Windows\Explorer") }, TimeSpan.FromDays(1), defaultSelected: false,
            fileFilter: f => Path.GetFileName(f).StartsWith("thumbcache_", StringComparison.OrdinalIgnoreCase), recursive: false),
        new FolderCleanupTarget("shader-cache", "Кеш шейдеров (DirectX, NVIDIA, AMD)", "Скомпилированные шейдеры игр. Помогает, если после обновления драйвера игры стали фризить.",
            () => new[] { Path.Combine(Local, "D3DSCache"), Path.Combine(Local, @"NVIDIA\DXCache"), Path.Combine(Local, @"NVIDIA\GLCache"), Path.Combine(Local, @"AMD\DxCache"), Path.Combine(Local, @"AMD\DxcCache"), Path.Combine(Local, @"AMD\VkCache") },
            TimeSpan.FromDays(1), defaultSelected: false, caution: "Первый запуск каждой игры после очистки будет с подтормаживаниями, пока кеш не наполнится заново."),
        new FolderCleanupTarget("browser-cache", "Кеш браузеров", "Только кеш Chrome, Edge, Brave и Firefox: пароли, история, вкладки и расширения не затрагиваются.",
            () => ChromiumCaches(Path.Combine(Local, @"Google\Chrome\User Data"))
                .Concat(ChromiumCaches(Path.Combine(Local, @"Microsoft\Edge\User Data")))
                .Concat(ChromiumCaches(Path.Combine(Local, @"BraveSoftware\Brave-Browser\User Data")))
                .Concat(Directory.Exists(Path.Combine(Local, @"Mozilla\Firefox\Profiles"))
                    ? Directory.EnumerateDirectories(Path.Combine(Local, @"Mozilla\Firefox\Profiles")).Select(p => Path.Combine(p, "cache2")) : Array.Empty<string>()),
            TimeSpan.FromHours(1), defaultSelected: false, caution: "Закройте браузеры перед очисткой — открытые файлы будут пропущены. Сайты первый раз загрузятся чуть медленнее."),
        new FolderCleanupTarget("dumps", "Дампы памяти", "Файлы, которые Windows создаёт при синих экранах.",
            () => new[] { Path.Combine(Win, "Minidump"), Path.Combine(Win, "LiveKernelReports") }, TimeSpan.FromDays(7), defaultSelected: false,
            caution: "Нужны для диагностики синих экранов. Удаляйте, только если сбоев больше нет."),
        new ManualCleanupTarget("windows-old", "Предыдущая установка Windows (Windows.old)", "Остаётся после обновления или переустановки с сохранением файлов. Удаляет сама Windows: «Параметры» → «Память» → «Временные файлы».",
            () => new[] { Path.Combine(Path.GetPathRoot(Win)!, "Windows.old") }, "ms-settings:storagesense"),
    };
}
