using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameTune.Core.Strategy;
using GameTune.Workflow.Sessions;

namespace GameTune.ViewModels;

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
        Nav = new ObservableCollection<NavItem>
        {
            new() { Title = "Главная", Icon = "⌂", Page = () => Home },
            new() { Title = "Анализ", Icon = "🔍", Page = () => Analysis, IsEnabled = false },
            new() { Title = "Стратегия", Icon = "☑", Page = () => Strategy, IsEnabled = false },
            new() { Title = "Сравнение", Icon = "📊", Page = () => Compare ?? (PageViewModel)Home, IsEnabled = false },
            new() { Title = "История", Icon = "🕘", Page = () => History },
            new() { Title = "Настройки", Icon = "⚙", Page = () => SettingsPage },
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
    public CompareViewModel? Compare { get; private set; }
    public ObservableCollection<NavItem> Nav { get; }
    public string WindowTitle => $"GameTune {Services.AppVersion}";

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
                if (wf.ResumeAbIfRebooted(open))
                {
                    ShowMessage("Перезагрузка выполнена. Продолжаем A/B-проверку: замер варианта B.");
                    Navigate(MeasureViewModel.ForAbB(this));
                }
            }
            else if (open.Stage == SessionStage.AwaitingReboot)
            {
                var check = wf.ResumeIfRebooted(open);
                if (check is not null)
                {
                    var apply = new ApplyViewModel(this, report: null) { PostReboot = check };
                    apply.FillPostReboot(check);
                    Navigate(apply);
                }
            }
            else if (open.Stage >= SessionStage.Applied)
            {
                Home.Refresh();
            }
            Session = open;
        }

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
}
