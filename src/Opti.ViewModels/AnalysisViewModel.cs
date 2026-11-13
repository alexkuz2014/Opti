using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Opti.Core.Findings;
using Opti.Core.Strategy;
using Opti.Workflow.Reports;
using Opti.Workflow.Sessions;

namespace Opti.ViewModels;

public sealed partial class AnalysisViewModel : PageViewModel
{
    public AnalysisViewModel(MainViewModel main) : base(main) { }
    public override string Title => "Отчёт анализа";

    public ObservableCollection<FindingItem> Findings { get; } = new();
    private List<FindingItem> _all = new();

    /// <summary>Фильтр списка: null — всё; иначе critical / important / advice / fixable.</summary>
    [ObservableProperty] private string? _filter;
    [ObservableProperty] private int _criticalCount;
    [ObservableProperty] private int _importantCount;
    [ObservableProperty] private int _adviceCount;
    [ObservableProperty] private int _fixableCount;
    public bool FilterCritical { get => Filter == "critical"; set => Filter = value ? "critical" : null; }
    public bool FilterImportant { get => Filter == "important"; set => Filter = value ? "important" : null; }
    public bool FilterAdvice { get => Filter == "advice"; set => Filter = value ? "advice" : null; }
    public bool FilterFixable { get => Filter == "fixable"; set => Filter = value ? "fixable" : null; }
    public bool IsFiltered => Filter is not null;

    partial void OnFilterChanged(string? value)
    {
        OnPropertyChanged(nameof(FilterCritical)); OnPropertyChanged(nameof(FilterImportant));
        OnPropertyChanged(nameof(FilterAdvice)); OnPropertyChanged(nameof(FilterFixable)); OnPropertyChanged(nameof(IsFiltered));
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        Findings.Clear();
        foreach (var f in _all.Where(f => Filter switch
                 {
                     "critical" => f.Finding.Severity == Severity.Critical,
                     "important" => f.Finding.Severity == Severity.Important,
                     "advice" => f.Finding.Severity == Severity.Advice,
                     "fixable" => f.HasFix,
                     _ => true,
                 }))
            Findings.Add(f);
    }

    [RelayCommand] private void ClearFilter() => Filter = null;
    public ObservableCollection<string> ProfileLines { get; } = new();
    public ObservableCollection<string> Errors { get; } = new();
    [ObservableProperty] private string? _deviceText;
    [ObservableProperty] private string? _summary;
    [ObservableProperty] private int _recommendedCount;
    [ObservableProperty] private string? _autoFixText;
    public bool CanAutoFix => RecommendedCount > 0;
    partial void OnRecommendedCountChanged(int value) => OnPropertyChanged(nameof(CanAutoFix));

    public void Load(TuneSession s, List<string> collectorErrors)
    {
        _all = s.Findings.Select(f => new FindingItem(f, Main.Services.Workflow.Catalog)).ToList();
        CriticalCount = _all.Count(f => f.Finding.Severity == Severity.Critical);
        ImportantCount = _all.Count(f => f.Finding.Severity == Severity.Important);
        AdviceCount = _all.Count(f => f.Finding.Severity == Severity.Advice);
        FixableCount = _all.Count(f => f.HasFix);
        if (Filter is null) ApplyFilter(); else Filter = null;
        var p = s.Profile!;
        DeviceText = $"{p.Device.Manufacturer} {p.Device.Model} — {DeviceClassification.From(p).Describe()}";
        ProfileLines.Clear();
        ProfileLines.Add($"{p.Os.ProductName} {p.Os.DisplayVersion} (сборка {p.Os.Build}.{p.Os.Ubr})" + (p.Os.ModifiedBuild is { } m ? $", {m}" : ""));
        if (p.Cpu.Name is not null) ProfileLines.Add($"Процессор: {p.Cpu.Name}, {p.Cpu.Cores} ядер / {p.Cpu.Threads} потоков");
        foreach (var g in p.Gpus) ProfileLines.Add($"{(g.IsIntegrated ? "Встроенная графика" : "Видеокарта")}: {g.Name} (драйвер {g.DriverDate:dd.MM.yyyy})");
        foreach (var d in p.Displays) ProfileLines.Add($"Экран: {d.FriendlyName ?? d.DeviceName} {d.Width}×{d.Height} @ {d.CurrentHz} Гц (макс. {d.MaxHzAtCurrentResolution})");
        if (p.Memory.TotalMb > 0) ProfileLines.Add($"Память: {p.Memory.TotalMb / 1024.0:0.#} ГБ, свободно {p.Memory.AvailablePercent:0} %");
        if (p.SystemDisk is { } sd) ProfileLines.Add($"Системный диск: {sd.Model} ({sd.MediaType}), свободно {sd.FreeGb:0} ГБ");
        ProfileLines.Add($"Питание: {(p.Power.OnAcPower == true ? "от сети" : p.Power.OnAcPower == false ? "от батареи" : "?")}; игр найдено: {p.Games.Count}");
        if (p.NetQuality is { } nq && !double.IsNaN(nq.IdlePingMs))
            ProfileLines.Add($"Сеть: пинг {nq.IdlePingMs:0} мс, скачки ±{nq.IdleJitterMs:0.#} мс, потери {nq.PacketLossPercent:0.#} %" +
                             (nq.GatewayPingMs is { } gw ? $"; до роутера {gw:0.#} мс" : ""));
        if (p.Os.LastUpdateInstalled is { } upd) ProfileLines.Add($"Последнее обновление Windows: {upd:dd.MM.yyyy}");
        Errors.Clear();
        foreach (var e in collectorErrors.Concat(p.CollectorErrors).Distinct()) Errors.Add(e);
        RecommendedCount = Main.RecommendedFixes(s).Count;
        AutoFixText = RecommendedCount == 0 ? "Автоматически исправлять нечего"
            : $"{RecommendedCount} {Plural(RecommendedCount, "исправление", "исправления", "исправлений")} · тест до и после · всё откатывается";
        int crit = s.Findings.Count(f => f.Severity == Severity.Critical), imp = s.Findings.Count(f => f.Severity == Severity.Important);
        int hw = s.Findings.Count(f => f.IsHardwareLimitation);
        Summary = s.Findings.Count == 0 ? "Явных проблем не найдено." :
            $"Найдено {s.Findings.Count}: критичных {crit}, важных {imp}, советов {s.Findings.Count - crit - imp}." +
            (hw > 0 ? $" {hw} — ограничения железа, программно не исправляются." : "");
    }

    public static string Plural(int n, string one, string few, string many)
    {
        int m10 = n % 10, m100 = n % 100;
        return m10 == 1 && m100 != 11 ? one : m10 is >= 2 and <= 4 && (m100 < 12 || m100 > 14) ? few : many;
    }

    [RelayCommand] private Task AutoFixAsync() => Main.StartAutoFlowAsync();

    /// <summary>Обезличенная сводка о ПК в буфер обмена — для техподдержки и чатов.</summary>
    [RelayCommand]
    private void CopyCard()
    {
        if (Main.Session?.Profile is not { } p) return;
        Main.Services.CopyText(HtmlReport.DiagnosticCardText(p, Main.Session.Findings, Main.Services.AppVersion));
        Main.ShowMessage("Сводка скопирована. В ней нет имени пользователя и компьютера — можно отправлять в техподдержку.");
    }

    /// <summary>Повторный анализ с той же целью — после ручных шагов (драйверы, обновления, кабель).</summary>
    [RelayCommand]
    private async Task ReanalyzeAsync()
    {
        Main.Navigate(Main.Home);
        if (Main.Home.AnalyzeCommand.CanExecute(null)) await Main.Home.AnalyzeCommand.ExecuteAsync(null);
    }

    [RelayCommand] private void MeasureBefore() => Main.Navigate(MeasureViewModel.ForBefore(Main));

    [RelayCommand]
    private void SkipToStrategy()
    {
        Main.Strategy.Load(Main.Session!);
        Main.Navigate(Main.Strategy);
    }

    [RelayCommand]
    private void ExportReport()
    {
        if (Main.Session is null) return;
        var path = Path.Combine(Main.Services.ReportsDir, $"analysis-{Main.Session.Id}.html");
        Directory.CreateDirectory(Main.Services.ReportsDir);
        File.WriteAllText(path, HtmlReport.Build(new ReportData { Session = Main.Session, Catalog = Main.Services.Workflow.Catalog, AppVersion = Main.Services.AppVersion }));
        Main.Services.OpenPath(path);
    }

    [RelayCommand] private void OpenLink(string url) => Main.Services.OpenPath(url);
}
