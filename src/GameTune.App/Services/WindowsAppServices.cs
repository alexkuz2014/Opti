using System.Diagnostics;
using System.Windows;
using GameTune.Analysis;
using GameTune.Core.Fixes;
using GameTune.Fixes.Backends;
using GameTune.Fixes.Journal;
using GameTune.Fixes.Services;
using GameTune.ViewModels;
using GameTune.Workflow;
using GameTune.Workflow.Measuring;
using GameTune.Workflow.Sessions;

namespace GameTune.App.Services;

/// <summary>Реализация сервисов для Windows: реальные коллекторы, реестр/BCD/powercfg, журнал SQLite, планировщик.</summary>
public sealed class WindowsAppServices : IAppServices, IDisposable
{
    private readonly SqliteChangeJournal _journal;

    public WindowsAppServices()
    {
        Settings = GameTuneSettings.Load();
        _journal = new SqliteChangeJournal(GameTunePaths.JournalPath);
        Workflow = new TuneWorkflow(Settings, new SessionStore(), WindowsBackends.Create(), _journal,
            new WindowsRestorePointService(), new WindowsResumeScheduler(), new WindowsCrashEventSource());
    }

    public TuneWorkflow Workflow { get; }
    public GameTuneSettings Settings { get; }
    public IChangeJournal Journal => _journal;
    public string AppVersion => AppInfo.Version;
    public string ReportsDir => GameTunePaths.ReportsDir;
    public string DataDir => GameTunePaths.DataDir;
    public string ResumeExePath => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "GameTune.exe");

    public Task<AnalysisResult> AnalyzeAsync(IProgress<string> progress, CancellationToken ct) =>
        Task.Run(() => SystemAnalyzer.Collect(SystemAnalyzer.DefaultCollectors(Settings.ObsConfigDir), progress.Report), ct);

    public bool IsModeAvailable(MeasureMode mode) => mode switch
    {
        MeasureMode.Synthetic => SyntheticRunner.Locate() is not null,
        MeasureMode.PresentMon => PresentMonRunner.Locate(Settings.PresentMonPath) is not null,
        _ => true,
    };

    public IMeasureRunner CreateRunner(MeasureMode mode) => MeasureRunnerFactory.Create(mode, Settings);

    public void RestartComputer() => RebootHelper.RestartNow(5);

    public void OpenPath(string pathOrUrl)
    {
        try { Process.Start(new ProcessStartInfo(pathOrUrl) { UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "GameTune", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    public Task<bool> ConfirmAsync(string title, string message) =>
        Task.FromResult(MessageBox.Show(Application.Current.MainWindow, message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes);

    public Task<UpdateInfo?> CheckForUpdatesAsync() => UpdateChecker.CheckAsync(AppVersion);

    public void Dispose() => _journal.Dispose();
}
