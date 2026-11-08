using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Opti.Workflow.Cleanup;

namespace Opti.ViewModels;

public sealed partial class CleanupItem : ObservableObject
{
    public CleanupItem(ICleanupTarget t)
    {
        Target = t;
        _isChecked = t.DefaultSelected;
    }
    public ICleanupTarget Target { get; }
    public string Title => Target.Title;
    public string Description => Target.Description;
    public string? Caution => Target.Caution;
    public bool IsManual => Target.ManualUri is not null;
    public bool IsAutomatic => !IsManual;

    [ObservableProperty] private bool _isChecked;
    [ObservableProperty] private long _bytes;
    [ObservableProperty] private bool _measured;
    [ObservableProperty] private string _sizeText = "…";
}

/// <summary>Бережная очистка: показываем размер каждой категории, удаляем только отмеченное.</summary>
public sealed partial class CleanupViewModel : PageViewModel
{
    private bool _estimated;
    public CleanupViewModel(MainViewModel main) : base(main) { }
    public override string Title => "Очистка";

    public ObservableCollection<CleanupItem> Items { get; } = new();
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(CleanCommand))] private bool _isBusy;
    [ObservableProperty] private string? _totalText;
    [ObservableProperty] private string? _resultText;

    public override async void OnShown()
    {
        if (_estimated) return;
        _estimated = true;
        await EstimateAsync();
    }

    [RelayCommand]
    public async Task EstimateAsync()
    {
        if (Items.Count == 0)
            foreach (var t in Main.Services.CleanupTargets())
            {
                var item = new CleanupItem(t);
                item.PropertyChanged += (_, e) => { if (e.PropertyName is nameof(CleanupItem.IsChecked) or nameof(CleanupItem.Bytes)) UpdateTotal(); };
                Items.Add(item);
            }
        IsBusy = true;
        try
        {
            await Task.WhenAll(Items.Select(async i =>
            {
                i.SizeText = "…";
                try { var e = await i.Target.EstimateAsync(); i.Bytes = e.Bytes; i.SizeText = e.Bytes == 0 ? "пусто" : SizeFormat.Bytes(e.Bytes); }
                catch { i.Bytes = 0; i.SizeText = "нет доступа"; }
                i.Measured = true;
            }));
        }
        finally { IsBusy = false; }
        UpdateTotal();
    }

    private void UpdateTotal()
    {
        long sel = Items.Where(i => i.IsChecked && i.IsAutomatic).Sum(i => i.Bytes);
        TotalText = sel > 0 ? $"Будет освобождено около {SizeFormat.Bytes(sel)}" : "Выберите, что очистить";
        CleanCommand.NotifyCanExecuteChanged();
    }

    private bool CanClean() => !IsBusy && Items.Any(i => i.IsChecked && i.IsAutomatic && i.Bytes > 0);

    [RelayCommand(CanExecute = nameof(CanClean))]
    private async Task CleanAsync()
    {
        var chosen = Items.Where(i => i.IsChecked && i.IsAutomatic && i.Bytes > 0).ToList();
        var cautions = chosen.Where(c => c.Caution is not null).Select(c => $"• {c.Title}: {c.Caution}").ToList();
        var msg = "Очистить:\n" + string.Join("\n", chosen.Select(c => $"• {c.Title} — {c.SizeText}")) +
                  (cautions.Count > 0 ? "\n\nОбратите внимание:\n" + string.Join("\n", cautions) : "") +
                  "\n\nУдаление необратимо. Документы, игры и настройки не затрагиваются.";
        if (!await Main.Services.ConfirmAsync("Очистка", msg)) return;

        IsBusy = true;
        long freed = 0; int skipped = 0; var errors = new List<string>();
        try
        {
            foreach (var c in chosen)
            {
                c.SizeText = "очищаю…";
                try
                {
                    var r = await c.Target.CleanAsync();
                    freed += r.FreedBytes; skipped += r.Skipped; errors.AddRange(r.Errors);
                }
                catch (Exception ex) { errors.Add($"{c.Title}: {ex.Message}"); }
            }
        }
        finally { IsBusy = false; }
        ResultText = $"Освобождено {SizeFormat.Bytes(freed)}." + (skipped > 0 ? $" Пропущено занятых файлов: {skipped}." : "") +
                     (errors.Count > 0 ? " " + string.Join(" ", errors) : "");
        Main.ShowMessage(ResultText, errors.Count > 0);
        await EstimateAsync();
    }

    [RelayCommand]
    private void OpenManual(CleanupItem item)
    {
        if (item.Target.ManualUri is { } uri) Main.Services.OpenPath(uri);
    }
}
