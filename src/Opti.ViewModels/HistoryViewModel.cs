using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Opti.Fixes.Services;
using Opti.Workflow.Reports;
using Opti.Workflow.Sessions;
using Opti.Measure.Stats;
using Opti.Core.Strategy;

namespace Opti.ViewModels;

public sealed class SessionItem
{
    public required TuneSession Session { get; init; }
    public string Title => $"{Session.CreatedAt.LocalDateTime:dd.MM.yyyy HH:mm} · {Session.Goal.Ru()}";
    public required string OutcomeText { get; init; }
    public required string Color { get; init; }
    public required string Details { get; init; }
}

public sealed partial class HistoryViewModel : PageViewModel
{
    public HistoryViewModel(MainViewModel main) : base(main) { }
    public override string Title => "История";
    public ObservableCollection<SessionItem> Sessions { get; } = new();

    public override void OnShown() => Refresh();

    [RelayCommand]
    public void Refresh()
    {
        Sessions.Clear();
        var journal = Main.Services.Journal.GetAll();
        foreach (var s in Main.Services.Workflow.Store.All())
        {
            var recs = journal.Where(r => r.SessionId.StartsWith(s.Id, StringComparison.Ordinal)).ToList();
            var (text, color) = s.Outcome switch
            {
                SessionOutcome.Improved => ("стало лучше", Palette.Good),
                SessionOutcome.Worse => ("стало хуже", Palette.Bad),
                SessionOutcome.NoEffect => ("без эффекта", Palette.Neutral),
                SessionOutcome.RolledBack => ("откачено", Palette.Neutral),
                _ => (s.Stage == SessionStage.Closed ? "без сравнения" : "в процессе", Palette.Accent),
            };
            Sessions.Add(new SessionItem
            {
                Session = s, OutcomeText = text, Color = color,
                Details = $"Изменений: {recs.Count(r => !r.RolledBack)} действует, {recs.Count(r => r.RolledBack)} откачено" +
                          (s.AbTests.Count > 0 ? $" · A/B: {string.Join(", ", s.AbTests.Select(a => a.FixId + (a.KeptB ? " ✓" : " ✗")))}" : ""),
            });
        }
    }

    [RelayCommand]
    private void OpenReport(SessionItem item)
    {
        var s = item.Session;
        Directory.CreateDirectory(Main.Services.ReportsDir);
        var path = Path.Combine(Main.Services.ReportsDir, $"report-{s.Id}.html");
        File.WriteAllText(path, HtmlReport.Build(new ReportData
        {
            Session = s,
            Comparison = s.Before.Count > 0 && s.After.Count > 0 ? new Comparer().Compare(s.Before, s.After) : null,
            Changes = Main.Services.Journal.GetAll().Where(r => r.SessionId.StartsWith(s.Id, StringComparison.Ordinal)).ToList(),
            Catalog = Main.Services.Workflow.Catalog,
            AppVersion = Main.Services.AppVersion,
        }));
        Main.Services.OpenPath(path);
    }

    [RelayCommand]
    private async Task RollbackSessionAsync(SessionItem item)
    {
        if (!await Main.Services.ConfirmAsync("Откат сессии", $"Вернуть изменения сессии {item.Title}?")) return;
        var r = await Task.Run(() => Main.Services.Workflow.RollbackSession(item.Session));
        Main.ShowMessage($"Откачено изменений: {r.Restored.Count}." + (r.Errors.Count > 0 ? $" Ошибок: {r.Errors.Count}." : ""), r.Errors.Count > 0);
        Refresh();
    }

    [RelayCommand]
    private async Task RollbackAllAsync()
    {
        if (!await Main.Services.ConfirmAsync("Полный откат", "Вернуть ВСЕ изменения, когда-либо сделанные Opti?")) return;
        var wf = Main.Services.Workflow;
        var r = await Task.Run(() => new RollbackService(wf.BackendsForRollback, Main.Services.Journal).RollbackAll());
        foreach (var s in wf.Store.All().Where(s => s.Outcome != SessionOutcome.RolledBack)) { s.Outcome = SessionOutcome.RolledBack; s.Stage = SessionStage.Closed; wf.Store.Save(s); }
        Main.ShowMessage($"Полный откат: восстановлено {r.Restored.Count}." + (r.Errors.Count > 0 ? $" Ошибок: {r.Errors.Count}." : "") + " Перезагрузите компьютер.");
        Refresh();
    }
}
