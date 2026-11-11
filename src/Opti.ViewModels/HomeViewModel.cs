using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Opti.Workflow.Sessions;

namespace Opti.ViewModels;

public sealed partial class HomeViewModel : PageViewModel
{
    private CancellationTokenSource? _cts;

    public HomeViewModel(MainViewModel main) : base(main)
    {
        _selectedGoal = GoalOption.All.First(g => g.Goal == main.Services.Settings.DefaultGoal);
    }

    public override string Title => "Главная";
    public IReadOnlyList<GoalOption> Goals => GoalOption.All;

    [ObservableProperty] private GoalOption? _selectedGoal;
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(AnalyzeCommand))] [NotifyPropertyChangedFor(nameof(IsIdle))] private bool _isAnalyzing;
    public bool IsIdle => !IsAnalyzing;
    [ObservableProperty] private string? _progressText;
    /// <summary>Номер текущего шага анализа и их общее число; если число неизвестно — полоса «бегущая».</summary>
    [ObservableProperty] private double _progressValue;
    public double ProgressMax => Math.Max(1, Main.Services.AnalysisStepCount);
    public bool ProgressUnknown => Main.Services.AnalysisStepCount <= 0;
    /// <summary>«Действует 5 изменений Opti» — или null, если Opti ничего не менял.</summary>
    [ObservableProperty] private string? _activeChangesText;
    [ObservableProperty] private string? _openSessionText;
    [ObservableProperty] private bool _hasOpenSession;

    /// <summary>Свободное место на системном диске — подсказка открыть «Очистку».</summary>
    public string? DiskText
    {
        get
        {
            try
            {
                var root = Path.GetPathRoot(Environment.SystemDirectory);
                if (string.IsNullOrEmpty(root)) return null;
                var d = new DriveInfo(root);
                if (!d.IsReady || d.TotalSize == 0) return null;
                double free = d.AvailableFreeSpace / 1e9, total = d.TotalSize / 1e9;
                return $"Диск {root.TrimEnd('\\', '/')} — свободно {free:0} ГБ из {total:0} ГБ";
            }
            catch { return null; }
        }
    }
    public bool DiskLow
    {
        get
        {
            try { var d = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory)!); return d.IsReady && d.AvailableFreeSpace < d.TotalSize * 0.15; }
            catch { return false; }
        }
    }

    [RelayCommand] private void OpenCleanup() => Main.Navigate(Main.Cleanup);
    [RelayCommand] private void OpenSoftware() => Main.Navigate(Main.Software);
    [RelayCommand] private void OpenTweaks() => Main.Navigate(Main.Tweaks);
    [RelayCommand] private void OpenHistory() => Main.Navigate(Main.History);
    public override void OnShown() => Refresh();

    /// <summary>Драйверы проверяются в составе анализа: если его ещё не было — запускаем.</summary>
    [RelayCommand]
    private async Task OpenDriversAsync()
    {
        if (Main.Session?.Profile is null)
        {
            await AnalyzeAsync();
            if (Main.Session?.Profile is null) return;
        }
        Main.Navigate(Main.Drivers);
    }

    public void Refresh()
    {
        var s = Main.Session;
        HasOpenSession = s is not null && s.Stage is not (SessionStage.Closed or SessionStage.Analyzed);
        OpenSessionText = s is null ? null : s.Stage switch
        {
            SessionStage.MeasuredBefore => "Замер «до» сделан — следующий шаг: стратегия.",
            SessionStage.Applied => "Исправления применены — сделайте замер «после».",
            SessionStage.AwaitingReboot => "Ожидается перезагрузка. После неё Opti продолжит сам.",
            SessionStage.VerifiedAfterReboot => "Перезагрузка выполнена и проверена — сделайте замер «после».",
            SessionStage.MeasuredAfter => "Замеры готовы — откройте сравнение.",
            SessionStage.Compared => "Сравнение готово.",
            _ => null,
        };
        try
        {
            int active = Main.Services.Journal.GetAll().Where(r => !r.RolledBack).Select(r => r.FixId).Distinct().Count();
            ActiveChangesText = active == 0 ? null
                : $"{AnalysisViewModel.Plural(active, "Действует", "Действуют", "Действует")} {active} {AnalysisViewModel.Plural(active, "изменение", "изменения", "изменений")} Opti";
        }
        catch { ActiveChangesText = null; }
    }

    private bool CanAnalyze() => !IsAnalyzing;

    [RelayCommand(CanExecute = nameof(CanAnalyze))]
    private async Task AnalyzeAsync()
    {
        IsAnalyzing = true;
        _cts = new CancellationTokenSource();
        try
        {
            int step = 0, total = Main.Services.AnalysisStepCount;
            ProgressValue = 0;
            var progress = new Progress<string>(s =>
            {
                step++;
                ProgressValue = Math.Min(step, ProgressMax);
                ProgressText = total > 0 ? $"Шаг {Math.Min(step, total)} из {total}: {s}…" : $"Собираю данные: {s}…";
            });
            var result = await Main.Services.AnalyzeAsync(progress, _cts.Token);
            var session = Main.Services.Workflow.StartSession(result, SelectedGoal?.Goal ?? Main.Services.Settings.DefaultGoal);
            Main.Session = session;
            Main.Analysis.Load(session, result.Timings.Select(t => t.Error is null ? null : $"{t.Name}: {t.Error}").OfType<string>().ToList());
            Main.Navigate(Main.Analysis);
        }
        catch (OperationCanceledException) { Main.ShowMessage("Анализ отменён."); }
        catch (Exception ex) { Main.ShowMessage("Анализ не удался: " + ex.Message, error: true); }
        finally
        {
            IsAnalyzing = false;
            ProgressText = null;
        }
    }

    [RelayCommand] private void CancelAnalysis() => _cts?.Cancel();

    [RelayCommand]
    private void ContinueSession()
    {
        var s = Main.Session;
        if (s is null) return;
        switch (s.Stage)
        {
            case SessionStage.MeasuredBefore:
                Main.Strategy.Load(s);
                Main.Navigate(Main.Strategy);
                break;
            case SessionStage.Applied or SessionStage.VerifiedAfterReboot:
                Main.Navigate(MeasureViewModel.ForAfter(Main));
                break;
            case SessionStage.MeasuredAfter or SessionStage.Compared:
                Main.ShowCompare(CompareViewModel.ForSession(Main, s));
                break;
            default:
                Main.Analysis.Load(s, new());
                Main.Navigate(Main.Analysis);
                break;
        }
    }
}
