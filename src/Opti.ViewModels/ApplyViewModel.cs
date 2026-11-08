using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Opti.Fixes.Services;
using Opti.Workflow.Sessions;

namespace Opti.ViewModels;

public sealed partial class ApplyViewModel : PageViewModel
{
    private readonly bool _abMode;

    public ApplyViewModel(MainViewModel main, ApplyReport? report, bool abMode = false) : base(main)
    {
        _abMode = abMode;
        if (report is null) return;
        RestorePoint = report.RestorePoint is not null ? $"Точка восстановления создана: {report.RestorePoint}" : report.RestorePointWarning;
        RebootRequired = report.RebootRequired;
        foreach (var r in report.Results)
        {
            var title = Main.Services.Workflow.Catalog.FirstOrDefault(f => f.Id == r.FixId)?.Title ?? r.FixId;
            Log.Add(new LogItem
            {
                Title = title,
                Status = r.Outcome switch { FixOutcome.Applied => "применено", FixOutcome.Failed => "ошибка: " + r.Error, FixOutcome.NothingToChange => "уже в нужном состоянии", _ => "пропущено" },
                Color = r.Outcome switch { FixOutcome.Applied => Palette.Good, FixOutcome.Failed => Palette.Bad, _ => Palette.Neutral },
                Details = r.Changes.Select(c => $"{c.Type} {c.Target}: {c.Before} → {c.After}").ToList(),
            });
        }
        Headline = report.Results.Any(r => r.Outcome == FixOutcome.Failed)
            ? "Часть исправлений не применилась — они откачены, остальное действует."
            : "Исправления применены.";
        NextText = RebootRequired ? "Нужна перезагрузка. После входа в систему Opti откроется сам и продолжит."
                                  : _abMode ? "Теперь замер варианта B в тех же условиях." : "Теперь замер «после» в тех же условиях.";
    }

    public override string Title => _abMode ? "A/B: вариант B применён" : "Применение";
    public ObservableCollection<LogItem> Log { get; } = new();
    public ObservableCollection<LogItem> PostRebootItems { get; } = new();
    [ObservableProperty] private string? _headline;
    [ObservableProperty] private string? _restorePoint;
    [ObservableProperty] private bool _rebootRequired;
    [ObservableProperty] private string? _nextText;
    [ObservableProperty] private bool _suggestRollback;
    public PostRebootReport? PostReboot { get; set; }

    public void FillPostReboot(PostRebootReport check)
    {
        Headline = "Перезагрузка выполнена. Самопроверка:";
        RebootRequired = false;
        PostRebootItems.Add(new LogItem { Title = $"Подтверждено изменений: {check.Verified}", Status = check.Healthy ? "всё в порядке" : "есть замечания", Color = check.Healthy ? Palette.Good : Palette.Warn });
        foreach (var n in check.NotApplied) PostRebootItems.Add(new LogItem { Title = "Не применилось", Status = n, Color = Palette.Warn });
        foreach (var c in check.Crashes) PostRebootItems.Add(new LogItem { Title = $"Сбой системы {c.Time.LocalDateTime:dd.MM HH:mm}", Status = $"событие {c.EventId} ({c.Source})", Color = Palette.Bad });
        SuggestRollback = check.SuggestRollback;
        NextText = check.SuggestRollback ? "После изменений были сбои системы. Рекомендуется откатить." : "Теперь замер «после» в тех же условиях, что и «до».";
    }

    [RelayCommand] private void RebootNow() => Main.Services.RestartComputer();

    [RelayCommand]
    private void Continue()
    {
        var s = Main.Session!;
        if (_abMode) { Main.Navigate(MeasureViewModel.ForAbB(Main)); return; }
        if (s.Before.Count == 0)
        {
            Main.Services.Workflow.Close(s);
            Main.ShowMessage("Замера «до» не было — сравнивать не с чем. Отчёт можно открыть в «Истории».");
            Main.History.Refresh();
            Main.Navigate(Main.History);
            return;
        }
        Main.Navigate(MeasureViewModel.ForAfter(Main, auto: s.AutoMode));
    }

    [RelayCommand]
    private async Task RollbackAsync()
    {
        var s = Main.Session!;
        if (!await Main.Services.ConfirmAsync("Откат", "Вернуть все изменения этой сессии?")) return;
        var r = await Task.Run(() => Main.Services.Workflow.RollbackSession(s));
        Main.Services.Workflow.Close(s);
        Main.ShowMessage($"Откачено изменений: {r.Restored.Count}" + (r.Errors.Count > 0 ? $", ошибок: {r.Errors.Count}" : "") + ". Для части настроек нужна перезагрузка.");
        Main.History.Refresh();
        Main.Navigate(Main.History);
    }
}
