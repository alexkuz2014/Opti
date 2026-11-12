using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Opti.Workflow.Sessions;

namespace Opti.ViewModels;

public sealed partial class HomeViewModel : PageViewModel
{
    private CancellationTokenSource? _cts;

    public HomeViewModel(MainViewModel main) : base(main)
    {
        // Если в настройках выбрана цель не из трёх пресетов («Стрим», «Тихо»), ничего не подсвечиваем — будет использована она
        _selectedGoal = GoalOption.Presets.FirstOrDefault(g => g.Goal == main.Services.Settings.DefaultGoal);
    }

    public override string Title => "Главная";
    public IReadOnlyList<GoalOption> Goals => GoalOption.Presets;

    [ObservableProperty] private GoalOption? _selectedGoal;
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(AnalyzeCommand))] [NotifyCanExecuteChangedFor(nameof(OptimizeCommand))] [NotifyCanExecuteChangedFor(nameof(MaximumCommand))] [NotifyPropertyChangedFor(nameof(IsIdle))] private bool _isAnalyzing;
    public bool IsIdle => !IsAnalyzing;
    [ObservableProperty] private string? _progressText;
    /// <summary>Номер текущего шага анализа и их общее число; если число неизвестно — полоса «бегущая».</summary>
    [ObservableProperty] private double _progressValue;
    public double ProgressMax => Math.Max(1, Main.Services.AnalysisStepCount);
    public bool ProgressUnknown => Main.Services.AnalysisStepCount <= 0;
    /// <summary>«Действует 5 изменений Opti» — или null, если Opti ничего не менял.</summary>
    [ObservableProperty] private string? _activeChangesText;
    /// <summary>«2 из 8 настроек Opti изменились после применения» — или null.</summary>
    [ObservableProperty] private string? _driftText;
    private IReadOnlyList<Opti.Fixes.Services.DriftItem> _drift = Array.Empty<Opti.Fixes.Services.DriftItem>();
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
        CheckDrift();
    }

    /// <summary>Сверяет журнал с системой: настройки, которые Opti менял, могла сбросить Windows или установщик драйвера.</summary>
    public void CheckDrift()
    {
        try
        {
            var svc = new Opti.Fixes.Services.DriftService(Main.Services.Workflow.BackendsForRollback, Main.Services.Journal);
            _drift = svc.Check();
            int total = Main.Services.Journal.GetAll().Where(r => !r.RolledBack && r.Type != Opti.Core.Fixes.ChangeType.File).Select(r => (r.Type, r.Target)).Distinct().Count();
            DriftText = _drift.Count == 0 ? null
                : $"{_drift.Count} из {total} настроек, которые менял Opti, изменились: {string.Join(", ", _drift.Select(d => Short(d.Record)).Distinct().Take(3))}. Обычно это обновление Windows или установка драйвера.";
        }
        catch { DriftText = null; }
    }

    private string Short(Opti.Core.Fixes.ChangeRecord r)
    {
        var t = Opti.Core.Fixes.FixIds.Title(r.FixId);
        return t != r.FixId ? t : Main.Services.Workflow.Catalog.FirstOrDefault(f => f.Id == r.FixId)?.Title ?? t;
    }

    [RelayCommand]
    private void RestoreDrift()
    {
        var svc = new Opti.Fixes.Services.DriftService(Main.Services.Workflow.BackendsForRollback, Main.Services.Journal);
        int n = svc.Restore(_drift);
        Main.ShowMessage(n > 0 ? $"Восстановлено настроек: {n}." : "Ничего восстановить не удалось.", error: n == 0);
        CheckDrift();
    }

    private bool CanAnalyze() => !IsAnalyzing;

    [RelayCommand(CanExecute = nameof(CanAnalyze))]
    private Task AnalyzeAsync() => RunAnalysisAsync(SelectedGoal?.Goal ?? Main.Services.Settings.DefaultGoal);

    private async Task RunAnalysisAsync(Opti.Core.Strategy.UserGoal goal)
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
            var session = Main.Services.Workflow.StartSession(result, goal);
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

    /// <summary>
    /// «Оптимизировать» — всё одной кнопкой: анализ → быстрый тест → рекомендованные исправления (с точкой восстановления) →
    /// перезагрузка, если нужна → повторный тест → результат. Применяется только то, что отмечено в плане по умолчанию:
    /// рискованное, спорное и требующее выбора остаётся на усмотрение пользователя в «Исправлениях».
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanAnalyze))]
    private async Task OptimizeAsync()
    {
        var before = Main.Session;
        await RunAnalysisAsync(SelectedGoal?.Goal ?? Main.Services.Settings.DefaultGoal);
        var s = Main.Session;
        if (s?.Profile is null || ReferenceEquals(s, before)) return;      // анализ отменён или не удался
        var fixes = Main.RecommendedFixes(s);
        if (fixes.Count == 0)
        {
            Main.ShowMessage("Автоматически исправлять нечего. Ниже — что можно сделать вручную.");
            return;
        }
        s.AutoFixIds = fixes.Select(f => f.Id).ToList();
        s.SkipMeasure = false;
        s.MeasureRuns = 2;
        s.MeasureDurationSec = 20;
        try { await Main.RunAutoFlowAsync(s); }
        catch (Exception ex) { Main.ShowMessage(ex.Message, error: true); }
    }

    /// <summary>
    /// «Максимум оптимизации»: пользователь нажимает один раз и уходит. Анализ → тест «до» → все безопасные исправления → твики, очистка,
    /// драйверы из Центра обновления → перезагрузка сама (с отсчётом, который можно отменить) → тест «после» → результат.
    /// Цель — минимальная задержка: она включает и сетевые исправления для пинга. Спорное (A/B) и рискованное по-прежнему только вручную.
    /// Всё, кроме очистки и драйверов, откатывается одной кнопкой.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanAnalyze))]
    private async Task MaximumAsync()
    {
        var before = Main.Session;
        Main.Services.KeepAwake(true);
        try
        {
            await RunAnalysisAsync(Opti.Core.Strategy.UserGoal.LowLatency);
            var s = Main.Session;
            if (s?.Profile is null || ReferenceEquals(s, before)) { Main.Services.KeepAwake(false); return; }   // анализ отменён или не удался
            s.AutoFixIds = Main.RecommendedFixes(s).Select(f => f.Id).ToList();
            s.Maximum = true;
            s.SkipMeasure = false;
            s.MeasureRuns = 2;
            s.MeasureDurationSec = 20;
            await Main.RunAutoFlowAsync(s);
        }
        catch (Exception ex)
        {
            Main.Services.KeepAwake(false);
            Main.ShowMessage(ex.Message, error: true);
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
