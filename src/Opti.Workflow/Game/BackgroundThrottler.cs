namespace Opti.Workflow.Game;

public sealed record ThrottleStatus(bool GameRunning, string? GameName, int Throttled);

/// <summary>
/// «Приглушать фон во время игры»: пока идёт игра, браузеры и вспомогательные процессы лаунчеров получают пониженный приоритет,
/// когда игра закрылась — прежний. Права администратора и перезагрузка не нужны. Эффект есть, когда процессор занят целиком:
/// фон отдаёт ядра игре. Если процессор и так не загружен, разницы не будет.
/// Голосовой чат (Discord), OBS и защита сюда не входят: им понижение приоритета вредит.
/// </summary>
public sealed class BackgroundThrottler
{
    private readonly IProcessControl _procs;
    private readonly Dictionary<int, (string Name, ProcPriority Original)> _throttled = new();
    private HashSet<string> _games = new(StringComparer.OrdinalIgnoreCase);

    public BackgroundThrottler(IProcessControl procs) => _procs = procs;

    public static readonly HashSet<string> Targets = new(StringComparer.OrdinalIgnoreCase)
    {
        // браузеры
        "chrome", "msedge", "firefox", "opera", "opera_gx", "brave", "vivaldi", "browser",
        // вспомогательные процессы лаунчеров и магазинов
        "steamwebhelper", "EpicWebHelper", "EpicGamesLauncher", "Battle.net", "Agent", "GalaxyClient", "Origin", "EADesktop", "Ubisoft Game Launcher", "upc", "RiotClientUx", "RiotClientServices",
        // облачные диски и обновляторы
        "OneDrive", "Dropbox", "GoogleDriveFS", "YandexDisk2", "MEGAsync", "qbittorrent", "utorrent",
    };

    /// <summary>Известные игры, которые определяются без анализа; процессы найденных игр добавляются через <see cref="SetGames"/>.</summary>
    public static readonly string[] KnownGames =
        { "GTA5", "GTAV", "altv", "Majestic", "cs2", "csgo", "VALORANT-Win64-Shipping", "FortniteClient-Win64-Shipping", "r5apex", "dota2", "RainbowSix", "eldenring", "cyberpunk2077", "witcher3" };

    public void SetGames(IEnumerable<string> processNames) =>
        _games = KnownGames.Concat(processNames.Select(ExeName)).ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>Имя процесса из пути к exe: «D:\\Games\\Game.exe» → «Game» (разделители любые, чтобы работало и в тестах).</summary>
    public static string ExeName(string path)
    {
        var file = path.Split('\\', '/')[^1];
        return file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? file[..^4] : file;
    }

    public int ThrottledCount => _throttled.Count;

    /// <summary>Один шаг: вызывается раз в несколько секунд, пока включена функция.</summary>
    public ThrottleStatus Tick()
    {
        var all = _procs.List();
        var game = all.FirstOrDefault(p => _games.Contains(p.Name));
        if (game is null)
        {
            Restore();
            return new ThrottleStatus(false, null, 0);
        }
        var alive = all.Select(p => p.Pid).ToHashSet();
        foreach (var gone in _throttled.Keys.Where(k => !alive.Contains(k)).ToList()) _throttled.Remove(gone);
        foreach (var p in all.Where(p => Targets.Contains(p.Name) && p.Priority == ProcPriority.Normal && !_throttled.ContainsKey(p.Pid)))
            if (_procs.TrySetPriority(p.Pid, ProcPriority.BelowNormal))
                _throttled[p.Pid] = (p.Name, p.Priority);
        return new ThrottleStatus(true, game.Name, _throttled.Count);
    }

    /// <summary>Возвращает прежние приоритеты всем, кому мы их меняли (игра закрылась, функция выключена, Opti закрывается).</summary>
    public void Restore()
    {
        if (_throttled.Count == 0) return;
        foreach (var (pid, (_, original)) in _throttled) _procs.TrySetPriority(pid, original);
        _throttled.Clear();
    }
}
