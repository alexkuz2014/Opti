using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Opti.Core.Tweaks;

namespace Opti.ViewModels;

public sealed partial class TweakItem : ObservableObject
{
    private readonly TweaksViewModel _owner;
    private bool _suppress;

    public TweakItem(TweaksViewModel owner, Tweak t, bool isOn, bool available)
    {
        _owner = owner;
        Tweak = t;
        _isOn = isOn;
        IsAvailable = available;
    }

    public Tweak Tweak { get; }
    public string Title => Tweak.Title;
    public string Description => Tweak.Description;
    public bool IsAvailable { get; }
    public bool Recommended => Tweak.Recommended;
    public bool ShowEvidence => Tweak.Evidence != Opti.Core.Fixes.EvidenceLevel.Solid;
    public string EvidenceText => EvidenceTexts.For(Tweak.Evidence);
    public string EvidenceColor => EvidenceTexts.Color(Tweak.Evidence);
    public string ApplyText => IsAvailable ? "Вступает в силу: " + TweakCatalog.ApplyModeText(Tweak.ApplyMode) : "Нужна более новая Windows 11";
    public string? Link => Tweak.Link;

    [ObservableProperty] private bool _isOn;
    [ObservableProperty] private string? _error;

    partial void OnIsOnChanged(bool value)
    {
        if (_suppress) return;
        Error = _owner.Toggle(this, value);
        if (Error is not null)
        {
            _suppress = true;
            IsOn = !value; // не получилось — возвращаем переключатель
            _suppress = false;
        }
    }

    internal void SetSilently(bool value)
    {
        _suppress = true;
        IsOn = value;
        _suppress = false;
    }
}

public sealed partial class TweakGroup : ObservableObject
{
    public required string Title { get; init; }
    public required string Icon { get; init; }
    public required IReadOnlyList<TweakItem> Items { get; init; }
    /// <summary>«включено 2 из 12» — по разделу.</summary>
    [ObservableProperty] private string? _counter;
    internal void Recount() => Counter = $"включено {Items.Count(i => i.IsOn)} из {Items.Count(i => i.IsAvailable)}";
}

/// <summary>
/// Твики удобства: заметные изменения поведения Windows, которые человек включает сам.
/// Не влияют на замеры FPS и не входят в «Исправить автоматически».
/// </summary>
public sealed partial class TweaksViewModel : PageViewModel
{
    public TweaksViewModel(MainViewModel main) : base(main) { }
    public override string Title => "Твики";

    public ObservableCollection<TweakGroup> Groups { get; } = new();
    [ObservableProperty] private bool _needsExplorerRestart;
    [ObservableProperty] private string? _pendingText;
    [ObservableProperty] private string? _summary;

    /// <summary>Поиск по названию и описанию твика.</summary>
    [ObservableProperty] private string? _search;
    partial void OnSearchChanged(string? value) => Load();
    public bool NothingFound => Groups.Count == 0;
    /// <summary>Файл набора твиков лежит рядом с программой: его можно перенести на другой ПК вместе с Opti.exe.</summary>
    public string ConfigPath { get; set; } = Path.Combine(AppContext.BaseDirectory, "opti-tweaks.json");

    public override void OnShown() => Load();

    private bool Matches(Tweak t)
    {
        var q = Search?.Trim();
        return string.IsNullOrEmpty(q) || t.Title.Contains(q, StringComparison.OrdinalIgnoreCase) || t.Description.Contains(q, StringComparison.OrdinalIgnoreCase);
    }

    public void Load()
    {
        Groups.Clear();
        var svc = Main.Services.Tweaks;
        foreach (var g in TweakCatalog.All.Where(Matches).GroupBy(t => t.Category))
            Groups.Add(new TweakGroup
            {
                Title = TweakCatalog.CategoryTitle(g.Key), Icon = TweakCatalog.CategoryIcon(g.Key),
                Items = g.Select(t => new TweakItem(this, t, svc.IsOn(t), svc.IsAvailable(t, Main.Services.OsBuild))).ToList(),
            });
        UpdateSummary();
        OnPropertyChanged(nameof(NothingFound));
    }

    private IEnumerable<TweakItem> AllItems => Groups.SelectMany(g => g.Items);

    private void UpdateSummary()
    {
        int on = AllItems.Count(i => i.IsOn), total = AllItems.Count(i => i.IsAvailable);
        Summary = $"Включено {on} из {total}";
        foreach (var g in Groups) g.Recount();
    }

    /// <summary>Возвращает текст ошибки или null.</summary>
    internal string? Toggle(TweakItem item, bool on)
    {
        try
        {
            if (on) Main.Services.Tweaks.Enable(item.Tweak);
            else Main.Services.Tweaks.Disable(item.Tweak);
        }
        catch (Exception ex) { return "Не удалось: " + ex.Message; }
        Note(item.Tweak.ApplyMode);
        UpdateSummary();
        return null;
    }

    private void Note(TweakApplyMode mode)
    {
        switch (mode)
        {
            case TweakApplyMode.RestartExplorer:
                NeedsExplorerRestart = true;
                PendingText ??= "Часть изменений вступит в силу после перезапуска Проводника (окна папок закроются, программы — нет).";
                break;
            case TweakApplyMode.SignOut:
                PendingText = "Часть изменений вступит в силу после выхода из системы и повторного входа.";
                break;
            case TweakApplyMode.Reboot:
                PendingText = "Часть изменений вступит в силу после перезагрузки.";
                break;
        }
    }

    [RelayCommand]
    private void EnableRecommended()
    {
        var errors = new List<string>();
        foreach (var i in AllItems.Where(i => i.Recommended && i.IsAvailable && !i.IsOn))
        {
            i.IsOn = true;
            if (i.Error is not null) errors.Add($"{i.Title}: {i.Error}");
        }
        Main.ShowMessage(errors.Count == 0 ? "Рекомендуемые твики включены." : string.Join(" ", errors), errors.Count > 0);
    }

    /// <summary>Сохраняет список включённых твиков в файл рядом с программой.</summary>
    [RelayCommand]
    private void ExportConfig()
    {
        try
        {
            var svc = Main.Services.Tweaks;
            var ids = TweakCatalog.All.Where(svc.IsOn).Select(t => t.Id).ToList();
            File.WriteAllText(ConfigPath, System.Text.Json.JsonSerializer.Serialize(ids, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            Main.ShowMessage($"Набор сохранён ({ids.Count}): {ConfigPath}. Скопируйте файл вместе с Opti.exe на другой ПК и нажмите «Загрузить набор».");
        }
        catch (Exception ex) { Main.ShowMessage("Не удалось сохранить набор: " + ex.Message, true); }
    }

    /// <summary>Включает твики из сохранённого файла. Неизвестные и недоступные на этой Windows пропускаются; ничего не выключается.</summary>
    [RelayCommand]
    private void ImportConfig()
    {
        List<string>? ids;
        try
        {
            if (!File.Exists(ConfigPath)) { Main.ShowMessage($"Файл набора не найден: {ConfigPath}. Сначала сохраните набор или положите файл рядом с программой.", true); return; }
            ids = System.Text.Json.JsonSerializer.Deserialize<List<string>>(File.ReadAllText(ConfigPath));
        }
        catch (Exception ex) { Main.ShowMessage("Файл набора не читается: " + ex.Message, true); return; }
        Search = null;
        var wanted = new HashSet<string>(ids ?? new(), StringComparer.OrdinalIgnoreCase);
        int enabled = 0, skipped = 0;
        var errors = new List<string>();
        foreach (var i in AllItems.Where(i => wanted.Contains(i.Tweak.Id)))
        {
            if (!i.IsAvailable) { skipped++; continue; }
            if (i.IsOn) continue;
            i.IsOn = true;
            if (i.Error is null) enabled++; else errors.Add(i.Title);
        }
        Main.ShowMessage($"Включено из набора: {enabled}." + (skipped > 0 ? $" Недоступно на этой Windows: {skipped}." : "")
                         + (errors.Count > 0 ? " Не удалось: " + string.Join(", ", errors) + "." : ""), errors.Count > 0);
    }

    /// <summary>Возвращает все включённые твики к значениям, которые были до Opti.</summary>
    [RelayCommand]
    private async Task DisableAllAsync()
    {
        Search = null;
        var on = AllItems.Where(i => i.IsOn).ToList();
        if (on.Count == 0) { Main.ShowMessage("Включённых твиков нет."); return; }
        if (!await Main.Services.ConfirmAsync("Выключить все твики?", $"Будет выключено твиков: {on.Count}. Вернутся значения, которые были до Opti.")) return;
        int failed = 0;
        foreach (var i in on) { i.IsOn = false; if (i.Error is not null) failed++; }
        Main.ShowMessage(failed == 0 ? $"Выключено твиков: {on.Count}." : $"Выключено {on.Count - failed}, не удалось {failed}.", failed > 0);
    }

    [RelayCommand]
    private void RestartExplorer()
    {
        Main.Services.RestartExplorer();
        NeedsExplorerRestart = false;
        PendingText = null;
    }

    [RelayCommand] private void OpenLink(string url) => Main.Services.OpenPath(url);
}
