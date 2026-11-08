using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameTune.Measure.Stats;
using GameTune.Workflow.Measuring;
using GameTune.Workflow.Sessions;

namespace GameTune.ViewModels;

public enum MeasurePhase { Before, After, AbA, AbB }

public sealed class ModeOption
{
    public required MeasureMode Mode { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }
    public bool Available { get; init; } = true;
    public string AvailabilityText => Available ? "" : "недоступно";
}

/// <summary>Замер «до», «после» и варианты A/B. Один и тот же экран, разное продолжение.</summary>
public sealed partial class MeasureViewModel : PageViewModel
{
    private CancellationTokenSource? _cts;

    private MeasureViewModel(MainViewModel main, MeasurePhase phase) : base(main)
    {
        Phase = phase;
        var svc = main.Services;
        Modes = new[]
        {
            new ModeOption { Mode = MeasureMode.Game, Title = "Реальная игра", Description = "Вы играете, программа записывает кадры через ETW. Стойте на месте или повторяйте один и тот же отрезок.", Available = svc.IsModeAvailable(MeasureMode.Game) },
            new ModeOption { Mode = MeasureMode.Synthetic, Title = "Синтетический тест GameTune", Description = "Своя D3D11-сцена с фиксированной нагрузкой: полностью воспроизводимо, игра не нужна.", Available = svc.IsModeAvailable(MeasureMode.Synthetic) },
            new ModeOption { Mode = MeasureMode.PresentMon, Title = "Игра через PresentMon", Description = "Дополнительно: режим вывода (Independent Flip) и задержка. Нужен PresentMon рядом с программой.", Available = svc.IsModeAvailable(MeasureMode.PresentMon) },
        };
        var s = main.Session;
        var sessionMode = s?.MeasureMode ?? MeasureMode.Game;
        _selectedMode = Modes.FirstOrDefault(m => m.Mode == sessionMode && m.Available) ?? Modes.First(m => m.Available || m.Mode == MeasureMode.Game);
        _runs = svc.Settings.MeasureRuns;
        _duration = svc.Settings.MeasureDurationSec;
        _warmup = svc.Settings.MeasureWarmupSec;
        _processName = s?.GameProcess;
        // «После» и вариант B — строго в тех же условиях, что «до» / A
        ModeLocked = phase is MeasurePhase.After or MeasurePhase.AbB;
        var previous = phase switch
        {
            MeasurePhase.After => s?.Before,
            MeasurePhase.AbB => s?.AbTests.LastOrDefault()?.RunsA,
            _ => null,
        };
        if (previous is { Count: > 0 }) _runs = Math.Max(1, previous.Count);
    }

    public static MeasureViewModel ForBefore(MainViewModel m) => new(m, MeasurePhase.Before);
    public static MeasureViewModel ForAfter(MainViewModel m) => new(m, MeasurePhase.After);
    public static MeasureViewModel ForAbA(MainViewModel m) => new(m, MeasurePhase.AbA);
    public static MeasureViewModel ForAbB(MainViewModel m) => new(m, MeasurePhase.AbB);

    public MeasurePhase Phase { get; }
    public override string Title => Phase switch
    {
        MeasurePhase.Before => "Замер «ДО»",
        MeasurePhase.After => "Замер «ПОСЛЕ»",
        MeasurePhase.AbA => "A/B: замер варианта A (как сейчас)",
        _ => "A/B: замер варианта B (с изменением)",
    };
    public string Hint => Phase switch
    {
        MeasurePhase.Before => "Запишите несколько прогонов в типичной для вас сцене. Те же условия понадобятся для замера «после».",
        MeasurePhase.After => "Повторите тот же сценарий, что и в замере «до»: та же игра, настройки, сцена, питание от сети.",
        _ => "Для честного A/B лучше синтетический тест: он не зависит от того, как вы играете.",
    };
    public IReadOnlyList<ModeOption> Modes { get; }
    public bool ModeLocked { get; }
    public bool ModeEditable => !ModeLocked;
    public string FinishText => Phase switch
    {
        MeasurePhase.Before => "Готово → стратегия",
        MeasurePhase.After => "Готово → сравнение",
        MeasurePhase.AbA => "Готово → применить вариант B",
        _ => "Готово → решение A/B",
    };
    public bool CanSkip => Phase == MeasurePhase.Before;

    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(StartCommand))] private ModeOption _selectedMode;
    [ObservableProperty] private int _runs;
    [ObservableProperty] private int _duration;
    [ObservableProperty] private int _warmup;
    [ObservableProperty] private string? _processName;
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(StartCommand), nameof(FinishCommand))] private bool _isRunning;
    [ObservableProperty] private double _progress;
    [ObservableProperty] private string? _status;
    [ObservableProperty] private IReadOnlyList<double> _liveFrameTimes = Array.Empty<double>();
    [ObservableProperty] private string? _liveFps;
    public ObservableCollection<RunSummary> Completed { get; } = new();

    private bool CanStart() => !IsRunning && SelectedMode.Available;
    private bool CanFinish() => !IsRunning && Completed.Count > 0;

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        IsRunning = true;
        _cts = new CancellationTokenSource();
        try
        {
            var runner = Main.Services.CreateRunner(SelectedMode.Mode);
            int toDo = Math.Max(1, Runs - Completed.Count);
            for (int i = 0; i < toDo; i++)
            {
                if (SelectedMode.Mode != MeasureMode.Synthetic)
                    for (int c = 5; c > 0; c--)
                    {
                        Status = $"Переключитесь в игру — прогон {Completed.Count + 1}/{Runs} начнётся через {c} с";
                        await Task.Delay(1000, _cts.Token);
                    }
                Status = $"Прогон {Completed.Count + 1}/{Runs}";
                var progress = new Progress<MeasureProgress>(p =>
                {
                    Progress = p.TotalSec > 0 ? 100 * p.ElapsedSec / p.TotalSec : 0;
                    if (p.RecentFrameTimesMs.Count > 0)
                    {
                        LiveFrameTimes = p.RecentFrameTimesMs;
                        LiveFps = $"{1000.0 / p.RecentFrameTimesMs.Average():0} FPS";
                    }
                    Status = $"Прогон {Completed.Count + 1}/{Runs}: {(p.Warmup ? "прогрев" : "запись")} {p.ElapsedSec:0}/{p.TotalSec:0} с" + (p.Process is null ? "" : $" · {p.Process}");
                });
                var label = Phase switch { MeasurePhase.Before => "до", MeasurePhase.After => "после", MeasurePhase.AbA => "A", _ => "B" };
                var run = await runner.RunAsync(new MeasureRequest($"{label} #{Completed.Count + 1}", Duration, SelectedMode.Mode == MeasureMode.Synthetic ? Math.Min(Warmup, 5) : Warmup,
                    string.IsNullOrWhiteSpace(ProcessName) ? null : ProcessName, SelectedMode.Mode == MeasureMode.Synthetic ? "синтетика" : "игра"), progress, _cts.Token);
                Completed.Add(new RunSummary(run));
                ProcessName ??= SelectedMode.Mode == MeasureMode.Synthetic ? null : run.Conditions.ProcessName;
                FinishCommand.NotifyCanExecuteChanged();
            }
            Status = "Замер завершён.";
        }
        catch (OperationCanceledException) { Status = "Замер остановлен."; }
        catch (Exception ex) { Status = null; Main.ShowMessage(ex.Message, error: true); }
        finally
        {
            IsRunning = false;
            Progress = 0;
        }
    }

    [RelayCommand] private void Cancel() => _cts?.Cancel();

    [RelayCommand]
    private void RemoveRun(RunSummary run)
    {
        if (!IsRunning) Completed.Remove(run);
        FinishCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanFinish))]
    private async Task FinishAsync()
    {
        var wf = Main.Services.Workflow;
        var s = Main.Session!;
        var runs = Completed.Select(r => r.Run).ToList();
        try
        {
            switch (Phase)
            {
                case MeasurePhase.Before:
                    s.MeasureMode = SelectedMode.Mode;
                    s.GameProcess = SelectedMode.Mode == MeasureMode.Synthetic ? null : runs[0].Conditions.ProcessName;
                    wf.RecordBefore(s, runs);
                    Main.Strategy.Load(s);
                    Main.Navigate(Main.Strategy);
                    break;
                case MeasurePhase.After:
                    wf.RecordAfter(s, runs);
                    Main.ShowCompare(CompareViewModel.ForSession(Main, s, wf.Compare(s)));
                    break;
                case MeasurePhase.AbA:
                    s.MeasureMode = SelectedMode.Mode;
                    wf.RecordAbA(s, runs);
                    var report = await Task.Run(() => wf.ApplyAbB(s, Main.Services.ResumeExePath));
                    Main.Navigate(new ApplyViewModel(Main, report, abMode: true));
                    break;
                case MeasurePhase.AbB:
                    var (ab, result, _) = wf.DecideAb(s, runs);
                    Main.ShowCompare(CompareViewModel.ForAb(Main, s, ab, result));
                    break;
            }
            Main.Session = s;
        }
        catch (Exception ex) { Main.ShowMessage(ex.Message, error: true); }
    }

    [RelayCommand]
    private void Skip()
    {
        Main.Strategy.Load(Main.Session!);
        Main.Navigate(Main.Strategy);
    }
}
