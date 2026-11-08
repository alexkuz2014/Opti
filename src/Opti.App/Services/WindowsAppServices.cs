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
        Workflow = new TuneWorkflow(Settings, new SessionStore(), WindowsBackends.Create(), _journal,
            new WindowsRestorePointService(), scheduler, new WindowsCrashEventSource());
    }

    public TuneWorkflow Workflow { get; }
    public OptiSettings Settings { get; }
    public IChangeJournal Journal => _journal;
    public string AppVersion => AppInfo.Version;
    public string ReportsDir => OptiPaths.ReportsDir;
    public string DataDir => OptiPaths.DataDir;
    public string? StartupNotice { get; }
    public string ResumeExePath => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "Opti.exe");

    public Task<AnalysisResult> AnalyzeAsync(IProgress<string> progress, CancellationToken ct) =>
        Task.Run(() => SystemAnalyzer.Collect(SystemAnalyzer.DefaultCollectors(Settings.ObsConfigDir), progress.Report), ct);

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
