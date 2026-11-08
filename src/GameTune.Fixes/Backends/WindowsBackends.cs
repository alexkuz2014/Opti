using System.Runtime.Versioning;
using GameTune.Core.Fixes;

namespace GameTune.Fixes.Backends;

public static class GameTunePaths
{
    /// <summary>
    /// C:\ProgramData\GameTune. Переменная окружения GAMETUNE_DATA_DIR переопределяет путь
    /// (портативный режим и тесты).
    /// </summary>
    public static string DataDir =>
        Environment.GetEnvironmentVariable("GAMETUNE_DATA_DIR") is { Length: > 0 } custom
            ? custom
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "GameTune");
    public static string JournalPath => Path.Combine(DataDir, "journal.db");
    public static string BackupDir => Path.Combine(DataDir, "backups");
    public static string ReportsDir => Path.Combine(DataDir, "reports");
    public static string SessionsDir => Path.Combine(DataDir, "sessions");
    public static string SettingsPath => Path.Combine(DataDir, "settings.json");
    public static string LogsDir => Path.Combine(DataDir, "logs");
}

[SupportedOSPlatform("windows")]
public static class WindowsBackends
{
    public static BackendRegistry Create(string? backupDir = null) => new(new IStateBackend[]
    {
        new WindowsRegistryBackend(),
        new WindowsBcdBackend(),
        new WindowsPowerBackend(),
        new WindowsDisplayBackend(),
        new WindowsNetAdapterBackend(),
        new FileBackend(backupDir ?? GameTunePaths.BackupDir),
    });
}
