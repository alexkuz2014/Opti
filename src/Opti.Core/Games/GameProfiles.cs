using Opti.Core.Profile;

namespace Opti.Core.Games;

public enum TipImpact { Big, Medium, Small }

public sealed record GameTip(TipImpact Impact, string Text)
{
    public string Label => Impact switch { TipImpact.Big => "больше всего", TipImpact.Medium => "заметно", _ => "немного" };
}

/// <summary>
/// Советы по конкретным играм. Файлы игры Opti не трогает: только подсказки по настройкам, которые пользователь меняет сам,
/// и системная часть (видеокарта, диск, питание), которая и так входит в исправления Opti.
/// </summary>
public static class GameProfiles
{
    public const string Gta5Id = "GAME_GTA5_PROFILE";

    public static bool IsGta5(GameInfo g) =>
        g.Name.Contains("Grand Theft Auto V", StringComparison.OrdinalIgnoreCase)
        || g.Name.Contains("GTA V", StringComparison.OrdinalIgnoreCase)
        || g.Name.Contains("Majestic", StringComparison.OrdinalIgnoreCase)
        || g.Name.Contains("altV", StringComparison.OrdinalIgnoreCase)
        || g.Executables.Any(e => e.Split('\\', '/')[^1].Equals("GTA5.exe", StringComparison.OrdinalIgnoreCase));

    public static IReadOnlyList<GameInfo> MatchGta5(SystemProfile p) => p.Games.Where(IsGta5).ToList();

    /// <summary>
    /// Что даёт FPS в GTA V (и на серверах altV, например Majestic RP), по убыванию эффекта. Пункты про процессор, видеопамять,
    /// диск и монитор подбираются под этот ПК. Порядок параметров графики — по обзору GamersNexus и сводкам игроков;
    /// точные проценты зависят от сцены и железа, поэтому здесь их нет.
    /// </summary>
    public static IReadOnlyList<GameTip> Gta5Tips(SystemProfile p)
    {
        var tips = new List<GameTip>
        {
            new(TipImpact.Big, "Сглаживание MSAA — выключить, оставить FXAA. Это самый дорогой параметр игры при слабой отдаче: картинка почти не меняется, кадров заметно больше."),
            new(TipImpact.Big, "Качество травы — «Стандарт» или «Высокое», не «Ультра». За городом «Ультра» режет FPS сильнее всего."),
            new(TipImpact.Big, "«Масштаб разрешения» (Resolution Scale) — не выше 1×: больше 1× рендерит игру в повышенном разрешении."),
            new(TipImpact.Medium, "Дальность прорисовки (Extended Distance Scaling) — 0 %. Заметно нагружает процессор, а в плотной городской сцене видимой выгоды почти нет."),
            new(TipImpact.Medium, "Тени: «Очень высокое» и «Ультра» непропорционально дороже «Высокого». «Длинные тени» (Long Shadows) — выключить."),
            new(TipImpact.Medium, "Тесселяция, MSAA отражений, глубина резкости, размытие при движении — выключить: отнимают кадры, прицеливанию не помогают."),
            new(TipImpact.Small, "«Подгрузка детальных текстур при полёте» (High Detail Streaming While Flying) — выключить, если не летаете."),
            new(TipImpact.Small, "Анизотропная фильтрация x16 можно оставить: она почти ничего не стоит и делает дорогу вдали чётче."),
        };

        var gpu = p.DiscreteGpu ?? p.IntegratedGpu;
        if (gpu is { DedicatedVramMb: > 0 and <= 2048 })
            tips.Insert(0, new(TipImpact.Big, $"Видеопамяти мало ({gpu.DedicatedVramMb / 1024.0:0.#} ГБ): текстуры — не выше «Стандарт», иначе игра упирается в память и появляются подгрузки и фризы."));
        else if (gpu is { DedicatedVramMb: >= 6000 })
            tips.Add(new(TipImpact.Small, $"Видеопамяти достаточно ({gpu.DedicatedVramMb / 1024.0:0.#} ГБ): текстуры «Высокое» или «Очень высокое» можно оставить — на FPS они влияют мало."));

        if (p.Cpu.Cores is > 0 and <= 4)
            tips.Add(new(TipImpact.Medium, $"Процессор {p.Cpu.Cores}-ядерный, а GTA V сильно зависит от процессора: параметры дальности прорисовки и теней дают здесь больше, чем снижение разрешения."));

        if (p.PrimaryDisplay is { CurrentHz: > 0 } d)
            tips.Add(new(TipImpact.Medium, $"Лимит FPS около {FpsCap.Recommend(d.CurrentHz, d.VrrSupported)} (монитор {d.CurrentHz} Гц{(d.VrrSupported == true ? ", VRR: чуть ниже частоты" : "")}) — в игре или в драйвере. Движок GTA V капризен на очень высоком FPS (по отчётам игроков, возможны статтеры), а ровные кадры ощущаются лучше «максимума»."));

        var hddLetters = p.Disks.Where(x => x.MediaType == DiskMediaType.Hdd).SelectMany(x => x.DriveLetters).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (MatchGta5(p).Any(g => g.InstallDir is { Length: >= 2 } dir && hddLetters.Contains(dir[..2])))
            tips.Insert(0, new(TipImpact.Big, "Игра установлена на жёсткий диск. GTA V постоянно подгружает карту с диска: на HDD это долгие загрузки, непрогрузы и фризы. Перенос на SSD даёт больше любых настроек."));

        if (p.Background.RunningOverlays.Count > 0)
            tips.Add(new(TipImpact.Small, "Оверлеи запущены: " + string.Join(", ", p.Background.RunningOverlays) + ". Выключите те, что не нужны во время игры."));

        if (p.Hybrid.IsHybrid && p.DiscreteGpu is not null)
            tips.Add(new(TipImpact.Big, "Ноутбук с двумя видеокартами: GTA V и клиент Majestic должны работать на дискретной. Opti привяжет их сам — см. исправление «Привязать игры к дискретной видеокарте»."));

        return tips.OrderBy(t => t.Impact).ToList();
    }
}
