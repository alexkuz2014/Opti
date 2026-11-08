using GameTune.Core.Json;
using GameTune.Core.Strategy;
using GameTune.Fixes.Backends;

namespace GameTune.Workflow;

/// <summary>Настройки программы (C:\ProgramData\GameTune\settings.json).</summary>
public sealed class GameTuneSettings
{
    public UserGoal DefaultGoal { get; set; } = UserGoal.Smoothness;
    /// <summary>Исправления, которые никогда не предлагать.</summary>
    public List<string> ExcludedFixIds { get; set; } = new();
    /// <summary>Свой путь к папке конфигурации OBS (если OBS портативный).</summary>
    public string? ObsConfigDir { get; set; }
    public int MeasureRuns { get; set; } = 3;
    public int MeasureDurationSec { get; set; } = 60;
    public int MeasureWarmupSec { get; set; } = 10;
    /// <summary>Путь к PresentMon.exe. Если пусто — ищется рядом с программой.</summary>
    public string? PresentMonPath { get; set; }
    public string NetworkTarget { get; set; } = "1.1.1.1";
    /// <summary>Откатывать изменения, если замер не показал улучшения (принцип 2 ТЗ). Иначе — только пометка.</summary>
    public bool RollbackWhenNoEffect { get; set; }
    /// <summary>Проверять наличие новой версии на GitHub (только уведомление, ничего не скачивается).</summary>
    public bool CheckForUpdates { get; set; } = true;

    public static GameTuneSettings Load(string? path = null)
    {
        path ??= GameTunePaths.SettingsPath;
        try { return File.Exists(path) ? GameTuneJson.Deserialize<GameTuneSettings>(File.ReadAllText(path)) : new(); }
        catch { return new(); }
    }

    public void Save(string? path = null)
    {
        path ??= GameTunePaths.SettingsPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, GameTuneJson.Serialize(this));
    }
}
