using System.Runtime.Versioning;
using GameTune.Core.Fixes;

namespace GameTune.Fixes.Backends;

public static class GameTunePaths
{
    public static string DataDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "GameTune");
    public static string JournalPath => Path.Combine(DataDir, "journal.db");
    public static string BackupDir => Path.Combine(DataDir, "backups");
    public static string ReportsDir => Path.Combine(DataDir, "reports");
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
