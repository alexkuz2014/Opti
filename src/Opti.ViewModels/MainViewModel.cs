using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Opti.Core.Strategy;
using Opti.Workflow.Sessions;

namespace Opti.ViewModels;

public abstract partial class PageViewModel : ObservableObject
{
    protected PageViewModel(MainViewModel main) => Main = main;
    public MainViewModel Main { get; }
    public abstract string Title { get; }
    /// <summary>Вызывается при показе страницы.</summary>
    public virtual void OnShown() { }
}

public sealed partial class NavItem : ObservableObject
{
    public required string Title { get; init; }
    public required string Icon { get; init; }
    public required Func<PageViewModel> Page { get; init; }
    [ObservableProperty] private bool _isEnabled = true;
    [ObservableProperty] private bool _isActive;
}

/// <summary>Корневая модель окна: навигация, текущая сессия, общие сообщения.</summary>
public sealed partial class MainViewModel : ObservableObject
{
    public MainViewModel(IAppServices services)
    {
        Services = services;
        Home = new HomeViewModel(this);
        Analysis = new AnalysisViewModel(this);
        Strategy = new StrategyViewModel(this);
        History = new HistoryViewModel(this);
        SettingsPage = new SettingsViewModel(this);
        Cleanup = new CleanupViewModel(this);
        Nav = new ObservableCollection<NavItem>
        {
            // Значки — глифы шрифта Segoe Fluent Icons / Segoe MDL2 Assets
            new() { Title = "Главная", Icon = "\uE80F", Page = () => Home },
            new() { Title = "Анализ", Icon = "\uE9D9", Page = () => Analysis, IsEnabled = false },
            new() { Title = "Исправления", Icon = "\uE90F", Page = () => Strategy, IsEnabled = false },
            new() { Title = "Результат", Icon = "\uE9D2", Page = () => Compare ?? (PageViewModel)Home, IsEnabled = false },
            new() { Title = "Очистка", Icon = "\uEA99", Page = () => Cleanup },
            new() { Title = "История", Icon = "\uE81C", Page = () => History },
            new() { Title = "Настройки", Icon = "\uE713", Page = () => SettingsPage },
        };
        _currentPage = Home;
        Nav[0].IsActive = true;
    }

    public IAppServices Services { get; }
    public HomeViewModel Home { get; }
    public AnalysisViewModel Analysis { get; }
    public StrategyViewModel Strategy { get; }
    public HistoryViewModel History { get; }
    public SettingsViewModel SettingsPage { get; }
    public CleanupViewModel Cleanup { get; }
    public CompareViewModel? Compare { get; private set; }
    public ObservableCollection<NavItem> Nav { get; }
    public string WindowTitle => $"Opti {Services.AppVersion}";

    [ObservableProperty] private PageViewModel _currentPage;
    [ObservableProperty] private TuneSession? _session;
    [ObservableProperty] private string? _toast;
    [ObservableProperty] private string? _toastColor;
    [ObservableProperty] private string? _updateMessage;
    [ObservableProperty] private string? _updateUrl;

    partial void OnSessionChanged(TuneSession? value)
    {
        Nav[1].IsEnabled = value?.Profile is not null;
        Nav[2].IsEnabled = value?.Profile is not null;
        Nav[3].IsEnabled = Compare is not null;
        Home.Refresh();
    }

    public void Navigate(PageViewModel page)
    {
        CurrentPage = page;
        foreach (var n in Nav)
        {
            PageViewModel? target = null;
            try { target = n.Page(); } catch { /* страница ещё не создана */ }
            n.IsActive = ReferenceEquals(target, page);
        }
        page.OnShown();
    }

    [RelayCommand]
    private void NavigateTo(NavItem item)
    {
        if (item.IsEnabled) Navigate(item.Page());
    }

    public void ShowMessage(string text, bool error = false)
    {
        Toast = text;
        ToastColor = error ? Palette.Bad : Palette.Accent;
    }

    [RelayCommand] private void CloseToast() => Toast = null;

    [RelayCommand]
    private void OpenUpdate()
    {
        if (UpdateUrl is not null) Services.OpenPath(UpdateUrl);
    }

    public void ShowCompare(CompareViewModel vm)
    {
        Compare = vm;
        Nav[3].IsEnabled = true;
        Navigate(vm);
    }

    /// <summary>Запуск окна: проверка обновлений и продолжение сессии после перезагрузки.</summary>
    public async Task InitializeAsync()
    {
        var wf = Services.Workflow;
        var open = wf.Store.LatestOpen();
        if (open is not null)
        {
            Session = open;
            if (open.AbTests.Any(a => a.Phase == AbPhase.AwaitRebootB))
            {
                if (await Task.Run(() => wf.ResumeAbIfRebooted(open)))
                {
                    ShowMessage("Перезагрузка выполнена. Продолжаем A/B-проверку: замер варианта B.");
                    Navigate(MeasureViewModel.ForAbB(this));
                }
            }
            else if (open.Stage == SessionStage.AwaitingReboot)
            {
                ShowMessage("Проверяю, что изменения применились после перезагрузки…");
                var check = await Task.Run(() => wf.ResumeIfRebooted(open));
                Toast = null;
                if (check is not null)
                {
                    Session = open;
                    if (open.AutoMode && check.Healthy && open.Before.Count > 0)
                    {
                        ShowMessage($"Перезагрузка выполнена, все изменения на месте ({check.Verified}). Продолжаю: замер после изменений.");
                        Navigate(MeasureViewModel.ForAfter(this, auto: true));
                    }
                    else
                    {
                        var apply = new ApplyViewModel(this, report: null) { PostReboot = check };
                        apply.FillPostReboot(check);
                        Navigate(apply);
                    }
                }
            }
            else if (open.Stage >= SessionStage.Applied)
            {
                Home.Refresh();
            }
            Session = open;
        }

        if (Services.StartupNotice is { } notice) ShowMessage(notice);

        if (Services.Settings.CheckForUpdates)
        {
            var u = await Services.CheckForUpdatesAsync();
            if (u is { IsNewer: true })
            {
                UpdateMessage = $"Доступна новая версия {u.LatestVersion}";
                UpdateUrl = u.Url;
            }
        }
    }

    public UserGoal SelectedGoal => Home.SelectedGoal?.Goal ?? Services.Settings.DefaultGoal;

    // ---------------- Автоматический цикл ----------------

    /// <summary>Рекомендованные исправления без спорных (A/B) — то, что применяет автоматический режим.</summary>
    public IReadOnlyList<Opti.Core.Fixes.IFix> RecommendedFixes(TuneSession s) =>
        Services.Workflow.BuildPlan(s).Fixes.Where(f => f.Selected && !f.ApplySeparately).Select(f => f.Fix).ToList();

    /// <summary>«Исправить автоматически»: тест → исправления → (перезагрузка) → тест → результат.</summary>
    public async Task StartAutoFlowAsync()
    {
        var s = Session ?? throw new InvalidOperationException("Сначала анализ.");
        var fixes = RecommendedFixes(s);
        if (fixes.Count == 0)
        {
            ShowMessage("Автоматически исправлять нечего — посмотрите советы в отчёте.");
            return;
        }
        bool reboot = fixes.Any(f => f.RequiresReboot);
        var msg = $"Opti сделает всё сам:\n\n1. Синтетический тест (~2 мин, на весь экран).\n2. Применит {fixes.Count} рекомендованных исправлений:\n   • " +
                  string.Join("\n   • ", fixes.Select(f => f.Title)) +
                  (reboot ? "\n3. Попросит перезагрузку и продолжит сам после входа." : "") +
                  $"\n{(reboot ? 4 : 3)}. Повторит тест и покажет результат.\n\nПеред изменениями создаётся точка восстановления, всё можно откатить. Начать?";
        if (!await Services.ConfirmAsync("Исправить автоматически", msg)) return;
        s.AutoMode = true;
        Services.Workflow.Store.Save(s);
        Navigate(s.Before.Count > 0 ? await ApplyRecommendedNavigateAsync(s) : MeasureViewModel.ForBefore(this, auto: true));
    }

    /// <summary>Шаг автоцикла после замера «до».</summary>
    public async Task ApplyRecommendedAsync() => Navigate(await ApplyRecommendedNavigateAsync(Session!));

    private async Task<PageViewModel> ApplyRecommendedNavigateAsync(TuneSession s)
    {
        var fixes = RecommendedFixes(s);
        var report = await Task.Run(() => Services.Workflow.Apply(s, fixes, Services.ResumeExePath));
        Session = s;
        if (!report.RebootRequired) return MeasureViewModel.ForAfter(this, auto: true);
        return new ApplyViewModel(this, report);
    }
}
