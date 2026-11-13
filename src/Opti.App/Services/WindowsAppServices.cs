using System.Diagnostics;
using System.Windows;
using Opti.Analysis;
using Opti.Core.Fixes;
using Opti.Fixes.Backends;
using Opti.Fixes.Journal;
using Opti.Fixes.Services;
using Opti.ViewModels;
using Opti.Workflow;
using Opti.Workflow.Cleanup;
using Opti.Workflow.Measuring;
using Opti.Workflow.Sessions;

namespace Opti.App.Services;

/// <summary>Реализация сервисов для Windows: реальные коллекторы, реестр/BCD/powercfg, журнал SQLite, планировщик.</summary>
public sealed class WindowsAppServices : IAppServices, IDisposable
{
    private readonly SqliteChangeJournal _journal;

    public WindowsAppServices()
    {
        StartupNotice = OptiPaths.MigrateLegacyData();
        var scheduler = new WindowsResumeScheduler();
        scheduler.CancelLegacy();
        Settings = OptiSettings.Load();
        _journal = new SqliteChangeJournal(OptiPaths.JournalPath);
        var backends = WindowsBackends.Create();
        Workflow = new TuneWorkflow(Settings, new SessionStore(), backends, _journal,
            new WindowsRestorePointService(), scheduler, new WindowsCrashEventSource());
        Tweaks = new TweakService(backends, _journal);
        Startup = new Opti.Fixes.Services.StartupService(backends, _journal);
    }

    public TuneWorkflow Workflow { get; }
    public TweakService Tweaks { get; }
    public int OsBuild => Environment.OSVersion.Version.Build;
    public Opti.Core.Stutter.IStutterSampler? StutterSampler { get; } = new Opti.Analysis.Collectors.WmiStutterSampler();
    public Opti.Core.Stutter.IStallProbe? StallProbe { get; } = new Opti.Measure.Capture.SpinStallProbe();
    public Task<Opti.Core.Profile.DpcLatencyInfo?> CaptureDpcAsync(TimeSpan duration, CancellationToken ct) => Task.Run<Opti.Core.Profile.DpcLatencyInfo?>(() =>
    {
        try { return Opti.Measure.Capture.EtwDpcCapture.Run(duration, ct); }
        catch { return null; }   // нет прав администратора
    });
    public IReadOnlyList<Opti.Core.Profile.StartupItem> ReadStartupItems() => Opti.Analysis.Collectors.BackgroundCollector.ReadStartupItems();
    public Opti.Fixes.Services.StartupService? Startup { get; private set; }
    public Opti.Workflow.Maximum.IDriverUpdater? DriverUpdater { get; } = new Opti.Workflow.Maximum.WindowsUpdateDriverUpdater();

    [System.Runtime.InteropServices.DllImport("kernel32.dll")] private static extern uint SetThreadExecutionState(uint flags);
    /// <summary>ES_CONTINUOUS | ES_SYSTEM_REQUIRED | ES_DISPLAY_REQUIRED: без сна и без отключения экрана (на тёмном экране замер теста ненадёжен).</summary>
    public void KeepAwake(bool on) => SetThreadExecutionState(on ? 0x80000000u | 0x00000001u | 0x00000002u : 0x80000000u);
    public void CopyText(string text) { try { System.Windows.Clipboard.SetText(text); } catch { /* буфер обмена занят */ } }
    public Opti.Workflow.Game.IProcessControl? Processes { get; } = new Opti.Workflow.Game.SystemProcessControl();
    public int AnalysisStepCount => SystemAnalyzer.DefaultCollectors(null, Settings.NetworkCheckInAnalysis ? Settings.NetworkTarget : null).Count;

    public void RestartExplorer()
    {
        try
        {
            foreach (var p in Process.GetProcessesByName("explorer")) { try { p.Kill(); p.WaitForExit(5000); } catch { } }
            // Обычно Windows перезапускает оболочку сама; если нет — запускаем
            Thread.Sleep(1500);
            if (Process.GetProcessesByName("explorer").Length == 0) Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true });
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Opti", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
    public OptiSettings Settings { get; }
    public IChangeJournal Journal => _journal;
    public string AppVersion => AppInfo.Version;
    public string ReportsDir => OptiPaths.ReportsDir;
    public string DataDir => OptiPaths.DataDir;
    public string? StartupNotice { get; }
    public string ResumeExePath => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "Opti.exe");

    public Task<AnalysisResult> AnalyzeAsync(IProgress<string> progress, CancellationToken ct) =>
        Task.Run(() =>
        {
            var result = SystemAnalyzer.Collect(SystemAnalyzer.DefaultCollectors(Settings.ObsConfigDir, Settings.NetworkCheckInAnalysis ? Settings.NetworkTarget : null), progress.Report);
#if DEV
            Opti.App.Services.DevExport.OnAnalysis(result);
#endif
            return result;
        }, ct);

    public bool IsModeAvailable(MeasureMode mode) => mode switch
    {
        MeasureMode.Synthetic => SyntheticRunner.Locate() is not null,
        MeasureMode.Game => true,
        _ => false,
    };

    public IMeasureRunner CreateRunner(MeasureMode mode) => MeasureRunnerFactory.Create(mode, Settings);

    public void RestartComputer() => RebootHelper.RestartNow(5);

    public void OpenPath(string pathOrUrl)
    {
        try { Process.Start(new ProcessStartInfo(pathOrUrl) { UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Opti", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    public void RevealFile(string path)
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Opti", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    public Task<bool> ConfirmAsync(string title, string message) =>
        Task.FromResult(MessageBox.Show(Application.Current.MainWindow, message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes);

    public Task<UpdateInfo?> CheckForUpdatesAsync() => UpdateChecker.CheckAsync(AppVersion);

    public IReadOnlyList<ICleanupTarget> CleanupTargets() => CleanupCatalog.ForWindows();

    public void Dispose() => _journal.Dispose();
}
