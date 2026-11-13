using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Opti.Workflow.Game;

namespace Opti.ViewModels;

/// <summary>Страница «Фризы»: минута наблюдения, пока пользователь играет, и вывод, что именно мешало.</summary>
public sealed partial class StutterViewModel : PageViewModel
{
    private CancellationTokenSource? _cts;
    public StutterViewModel(MainViewModel main) : base(main) { }

    public override string Title => "Фризы";
    public int Seconds { get; set; } = 60;
    public ObservableCollection<FindingItem> Findings { get; } = new();
    [ObservableProperty] private bool _isAvailable;
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(StartCommand))] private bool _isRunning;
    [ObservableProperty] private double _progress;
    [ObservableProperty] private string? _status;
    [ObservableProperty] private string? _summary;
    [ObservableProperty] private bool _hasResult;

    public override void OnShown() => IsAvailable = Main.Services.StutterSampler is not null;

    private bool CanStart() => !IsRunning && IsAvailable;

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        var sampler = Main.Services.StutterSampler;
        if (sampler is null) return;
        IsRunning = true;
        HasResult = false;
        Findings.Clear();
        _cts = new CancellationTokenSource();
        try
        {
            var games = new HashSet<string>(BackgroundThrottler.KnownGames, StringComparer.OrdinalIgnoreCase);
            foreach (var e in Main.Session?.Profile?.Games.SelectMany(g => g.Executables) ?? Enumerable.Empty<string>()) games.Add(BackgroundThrottler.ExeName(e));
            var progress = new Progress<int>(s =>
            {
                Progress = 100.0 * s / Seconds;
                Status = $"Идёт проверка: {s} из {Seconds} с. Играйте или пользуйтесь компьютером как обычно, лучше в тот момент, когда подлагивает.";
            });
            var result = await StutterCheck.RunAsync(sampler, Main.Services.CaptureDpcAsync, Seconds, n => games.Contains(n),
                Main.Session?.Profile?.Memory.TotalMb ?? 0, progress, _cts.Token);
            foreach (var f in result.Findings) Findings.Add(new FindingItem(f));
            HasResult = true;
            Summary = Findings.Count == 0
                ? $"За {result.Info.Seconds} с перегрузок системы не нашли. Если фризы остались, дело в самой игре, сети (раздел «Анализ») или видеокарте. Повторите проверку в момент, когда лагает сильнее всего."
                : $"Найдено причин: {Findings.Count}. Начните с первой.";
            if (result.Dpc is null) Summary += " Тест драйверов (DPC/ISR) не выполнен: нужны права администратора.";
        }
        catch (Exception ex) { Main.ShowMessage("Проверка не удалась: " + ex.Message, error: true); }
        finally { IsRunning = false; Status = null; }
    }

    [RelayCommand] private void Cancel() => _cts?.Cancel();
    [RelayCommand] private void OpenLink(string url) => Main.Services.OpenPath(url);
}
