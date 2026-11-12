#if DEV
using System.Windows.Threading;
using Opti.Analysis;
using Opti.Fixes.Backends;
using Opti.Workflow;
using Opti.Workflow.Dev;

namespace Opti.App.Services;

/// <summary>
/// Dev-сборка: рядом с exe поддерживает актуальный dev.zip. Обновляется после анализа, раз в минуту, при ошибке и при выходе.
/// Вывод системных утилит собирается один раз после анализа в фоне (около минуты).
/// </summary>
internal static class DevExport
{
    private static readonly object Gate = new();
    private static WindowsAppServices? _services;
    private static AnalysisResult? _analysis;
    private static IReadOnlyDictionary<string, string>? _probes;
    private static DispatcherTimer? _timer;
    private static Action<string>? _status;

    public static string ZipPath { get; } = ResolvePath();

    private static string ResolvePath()
    {
        var dir = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
        try
        {
            var probe = Path.Combine(dir, ".dev-write-test");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return Path.Combine(dir, "dev.zip");
        }
        catch { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "dev.zip"); }
    }

    public static void Start(WindowsAppServices services, Action<string> status)
    {
        _services = services;
        _status = status;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
        _timer.Tick += (_, _) => Write("таймер");
        _timer.Start();
        Write("запуск");
    }

    public static void OnAnalysis(AnalysisResult result)
    {
        _analysis = result;
        Write("после анализа");
        // Утилиты — после анализа, чтобы пинги не искажали проверку сети; в фоне, окно не блокируется
        Task.Run(() =>
        {
            try
            {
                var probes = DevBundle.CollectProbes(result.Profile.NetQuality?.Gateway);
                lock (Gate) _probes = probes;
                Write("системные утилиты собраны");
            }
            catch { /* архив без утилит всё равно полезен */ }
        });
    }

    public static void Write(string reason)
    {
        // Журнал изменений (SQLite) читаем только из потока интерфейса, где им пользуется остальная программа
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            try { dispatcher.Invoke(() => Write(reason)); } catch { /* программа закрывается */ }
            return;
        }
        lock (Gate)
        {
            if (_services is null) return;
            try
            {
                DevBundle.Write(ZipPath, new DevBundle.Input
                {
                    AppVersion = _services.AppVersion,
                    Reason = reason,
                    Analysis = _analysis,
                    Journal = _services.Journal,
                    Probes = _probes,
                });
                _status?.Invoke($"dev.zip обновлён {DateTime.Now:HH:mm:ss} ({reason}) — {ZipPath}");
            }
            catch (Exception ex) { _status?.Invoke("dev.zip не записан: " + ex.Message); }
        }
    }
}
#endif
