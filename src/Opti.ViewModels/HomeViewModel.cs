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
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(AnalyzeCommand))] private bool _isAnalyzing;
    [ObservableProperty] private string? _progressText;
    [ObservableProperty] private string? _openSessionText;
    [ObservableProperty] private bool _hasOpenSession;

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
    }

    private bool CanAnalyze() => !IsAnalyzing;

    [RelayCommand(CanExecute = nameof(CanAnalyze))]
    private async Task AnalyzeAsync()
    {
        IsAnalyzing = true;
        _cts = new CancellationTokenSource();
        try
        {
            var progress = new Progress<string>(s => ProgressText = "Собираю данные: " + s + "…");
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
