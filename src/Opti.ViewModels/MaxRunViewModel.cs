using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Opti.Workflow.Maximum;

namespace Opti.ViewModels;

/// <summary>Страница, которую видно, пока «Максимум оптимизации» делает твики, очистку и ставит драйверы: что уже сделано и что идёт сейчас.</summary>
public sealed partial class MaxRunViewModel : PageViewModel
{
    private readonly CancellationTokenSource _cts = new();
    public MaxRunViewModel(MainViewModel main) : base(main) { }

    public override string Title => "Максимум оптимизации";
    public CancellationToken Token => _cts.Token;
    public ObservableCollection<LogItem> Steps { get; } = new();
    [ObservableProperty] private string? _status = "Начинаю…";
    [ObservableProperty] private bool _isBusy = true;

    public void Add(MaxStep s) => Steps.Add(ToItem(s));
    public void Finish() { IsBusy = false; Status = "Готово"; }

    public static LogItem ToItem(MaxStep s) => new() { Title = s.Title, Status = s.Status, Color = s.Tone, Details = s.Details };

    [RelayCommand] private void Cancel() { _cts.Cancel(); Status = "Останавливаю после текущего шага…"; }
}
