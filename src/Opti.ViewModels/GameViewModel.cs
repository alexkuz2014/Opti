using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Opti.Core.Games;
using Opti.Workflow.Game;

namespace Opti.ViewModels;

public sealed partial class AppChoice : ObservableObject
{
    public AppChoice(AppGroup g, bool isChecked)
    {
        Group = g;
        _isChecked = isChecked;
    }
    public AppGroup Group { get; }
    public string Name => Group.Name;
    public string CountText => Group.Count > 1 ? $"×{Group.Count}" : "";
    public string MemoryText => Group.Bytes >= 1L << 30 ? $"{Group.Bytes / (double)(1L << 30):0.0} ГБ" : $"{Group.Bytes / (1024 * 1024)} МБ";
    [ObservableProperty] private bool _isChecked;
}

/// <summary>Диск с играми в списке «Где лежат ваши игры»: тип, место, игры и вывод простыми словами.</summary>
public sealed class GameDriveItem
{
    public GameDriveItem(GameDrive d)
    {
        Title = $"{d.Letter}  ·  {d.KindText}";
        Detail = $"свободно {d.FreeGb:0} из {d.SizeGb:0} ГБ  ·  " + string.Join(", ", d.Games.Select(g => g.Name).Take(5)) + (d.Games.Count > 5 ? $" и ещё {d.Games.Count - 5}" : "");
        (Verdict, Color) = d switch
        {
            { Kind: StorageKind.Hdd } => ("Медленно: игры подгружаются дольше. Перенесите любимые на SSD", Palette.Warn),
            { Kind: StorageKind.Usb } => ("Внешний диск: загрузка медленнее, чем с внутреннего SSD", Palette.Warn),
            { IsNearlyFull: true } => ("Диск почти полон: запись замедляется, освободите место", Palette.Bad),
            { Kind: StorageKind.Nvme or StorageKind.SataSsd } => ("Хорошо: игры на SSD", Palette.Good),
            _ => ("Тип диска определить не удалось", Palette.Neutral),
        };
    }
    public string Title { get; }
    public string Detail { get; }
    public string Verdict { get; }
    public string Color { get; }
}

/// <summary>Программа автозагрузки с переключателем: включено — запускается вместе с Windows.</summary>
public sealed partial class StartupToggle : ObservableObject
{
    private readonly GameViewModel _owner;
    private bool _suppress;
    public StartupToggle(GameViewModel owner, Opti.Core.Profile.StartupItem item)
    {
        _owner = owner;
        Item = item;
        _isOn = item.Enabled;
    }
    public Opti.Core.Profile.StartupItem Item { get; }
    public string Name => Item.Name;
    public string Detail => Item.Command is { Length: > 70 } c ? c[..70] + "…" : Item.Command ?? "";
    public bool CanToggle => Item.ApprovedKey is not null;
    [ObservableProperty] private bool _isOn;
    [ObservableProperty] private string? _error;

    partial void OnIsOnChanged(bool value)
    {
        if (_suppress) return;
        Error = _owner.ToggleStartup(this, value);
        if (Error is not null) { _suppress = true; IsOn = !value; _suppress = false; }
    }
}

/// <summary>Страница «Игра»: «Перед игрой» (закрыть выбранные фоновые программы) и «Приглушать фон во время игры».</summary>
public sealed partial class GameViewModel : PageViewModel
{
    private GamePrep? _prep;
    private BackgroundThrottler? _throttler;
    private bool _loading;

    public GameViewModel(MainViewModel main) : base(main) { }
    public override string Title => "Игра";

    public ObservableCollection<AppChoice> Apps { get; } = new();
    public ObservableCollection<GameDriveItem> Drives { get; } = new();
    public ObservableCollection<StartupToggle> StartupItems { get; } = new();
    [ObservableProperty] private string? _startupSummary;
    [ObservableProperty] private bool _hasDrives;
    [ObservableProperty] private bool _isAvailable;
    [ObservableProperty] private bool _throttleBackground;
    [ObservableProperty] private string? _throttleStatus;
    [ObservableProperty] private string? _prepResult;
    [ObservableProperty] private string? _appsSummary;

    /// <summary>Службы создаются при первом обращении: в тестах и на системах без доступа к процессам страница просто недоступна.</summary>
    private bool Ensure()
    {
        if (_prep is not null) return true;
        var procs = Main.Services.Processes;
        if (procs is null) return false;
        _prep = new GamePrep(procs);
        _throttler = new BackgroundThrottler(procs);
        return true;
    }

    public override void OnShown()
    {
        IsAvailable = Ensure();
        _loading = true;
        ThrottleBackground = Main.Services.Settings.ThrottleBackground;
        _loading = false;
        LoadDrives();
        LoadStartup();
        Refresh();
    }

    private void LoadStartup()
    {
        StartupItems.Clear();
        foreach (var i in Main.Services.ReadStartupItems().OrderByDescending(i => i.Enabled).ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase))
            StartupItems.Add(new StartupToggle(this, i));
        UpdateStartupSummary();
    }

    private void UpdateStartupSummary() =>
        StartupSummary = StartupItems.Count == 0 ? null : $"{StartupItems.Count(i => i.IsOn)} из {StartupItems.Count} запускаются вместе с Windows";

    internal string? ToggleStartup(StartupToggle t, bool on)
    {
        var svc = Main.Services.Startup;
        if (svc is null) return "Недоступно на этом компьютере.";
        try
        {
            if (on) svc.Enable(t.Item); else svc.Disable(t.Item);
            t.Item.Enabled = on;
        }
        catch (Exception ex) { return "Не удалось: " + ex.Message; }
        UpdateStartupSummary();
        return null;
    }

    private void LoadDrives()
    {
        Drives.Clear();
        if (Main.Session?.Profile is { } p)
            foreach (var d in GameStorage.Analyze(p)) Drives.Add(new GameDriveItem(d));
        HasDrives = Drives.Count > 0;
    }

    [RelayCommand]
    public void Refresh()
    {
        Apps.Clear();
        if (!Ensure()) return;
        var remembered = Main.Services.Settings.PrepareCloseList.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var g in _prep!.Scan()) Apps.Add(new AppChoice(g, remembered.Contains(g.Name)));
        long total = Apps.Sum(a => a.Group.Bytes);
        AppsSummary = Apps.Count == 0 ? "Крупных фоновых программ нет." : $"{Apps.Count} программ занимают {total / (double)(1L << 30):0.0} ГБ памяти";
    }

    partial void OnThrottleBackgroundChanged(bool value)
    {
        if (_loading) return;
        Main.Services.Settings.ThrottleBackground = value;
        Save();
        if (value) Tick();
        else
        {
            _throttler?.Restore();
            ThrottleStatus = null;
        }
    }

    /// <summary>Вызывается интерфейсом раз в несколько секунд.</summary>
    public void Tick()
    {
        if (!Main.Services.Settings.ThrottleBackground || !Ensure()) return;
        _throttler!.SetGames(Main.Session?.Profile?.Games.SelectMany(g => g.Executables) ?? Enumerable.Empty<string>());
        var st = _throttler.Tick();
        ThrottleStatus = st.GameRunning
            ? $"Идёт {st.GameName}: приглушено программ — {st.Throttled}"
            : "Жду запуска игры. Когда она закроется, приоритеты вернутся сами.";
    }

    /// <summary>Закрытие Opti: вернуть приоритеты.</summary>
    public void Shutdown() => _throttler?.Restore();

    [RelayCommand]
    private async Task PrepareAsync()
    {
        if (!Ensure()) return;
        var chosen = Apps.Where(a => a.IsChecked).ToList();
        if (chosen.Count == 0) { Main.ShowMessage("Отметьте программы, которые нужно закрыть."); return; }
        var names = string.Join(", ", chosen.Select(a => a.Name));
        if (!await Main.Services.ConfirmAsync("Закрыть программы?", $"Будут закрыты: {names}.\n\nНесохранённое в них пропадёт.")) return;
        var result = _prep!.Close(chosen.Select(a => a.Name));
        Main.Services.Settings.PrepareCloseList = chosen.Select(a => a.Name).ToList();
        Save();
        PrepResult = result.Failed == 0
            ? $"Закрыто процессов: {result.Closed}, освобождено около {result.FreedBytes / (1024 * 1024)} МБ."
            : $"Закрыто {result.Closed}, не удалось {result.Failed} (возможно, нужны права администратора).";
        Refresh();
    }

    private void Save()
    {
        try { Main.Services.Settings.Save(Path.Combine(Main.Services.DataDir, "settings.json")); } catch { /* настройки не сохранились — не критично */ }
    }
}
