using Opti.Core.Profile;

namespace Opti.Core.Games;

public enum StorageKind { Unknown, Hdd, SataSsd, Nvme, Usb }

/// <summary>Диск (том) с играми: тип накопителя, свободное место и какие игры на нём лежат.</summary>
public sealed record GameDrive(string Letter, StorageKind Kind, string? Model, double SizeGb, double FreeGb, IReadOnlyList<GameInfo> Games)
{
    public double FreePercent => SizeGb > 0 ? 100.0 * FreeGb / SizeGb : 100;
    /// <summary>Свободно меньше 10 % (и меньше 50 ГБ на больших дисках): запись замедляется, обновления игр и кэш шейдеров идут дольше.</summary>
    public bool IsNearlyFull => SizeGb > 0 && FreePercent < 10 && FreeGb < 50;
    public string KindText => Kind switch
    {
        StorageKind.Hdd => "HDD", StorageKind.SataSsd => "SSD (SATA)", StorageKind.Nvme => "NVMe SSD", StorageKind.Usb => "внешний USB-диск", _ => "тип неизвестен",
    };
}

/// <summary>Где лежат игры: помогает понять, что мешает им быстро грузиться.</summary>
public static class GameStorage
{
    public static string? LetterOf(GameInfo g)
    {
        var path = g.InstallDir ?? g.Executables.FirstOrDefault();
        return path is { Length: >= 2 } && path[1] == ':' && char.IsLetter(path[0]) ? char.ToUpperInvariant(path[0]) + ":" : null;
    }

    public static IReadOnlyList<GameDrive> Analyze(SystemProfile p)
    {
        var result = new List<GameDrive>();
        foreach (var grp in p.Games.Select(g => (Letter: LetterOf(g), Game: g)).Where(x => x.Letter is not null).GroupBy(x => x.Letter!, StringComparer.OrdinalIgnoreCase))
        {
            var disk = p.Disks.FirstOrDefault(d => d.DriveLetters.Contains(grp.Key, StringComparer.OrdinalIgnoreCase))
                       ?? p.Disks.FirstOrDefault(d => d.IsSystem && string.Equals(d.DriveLetter, grp.Key, StringComparison.OrdinalIgnoreCase));
            var vol = disk?.Volumes.FirstOrDefault(v => string.Equals(v.Letter, grp.Key, StringComparison.OrdinalIgnoreCase));
            double size = vol?.SizeGb ?? (disk is { IsSystem: true } ? disk.SizeGb : 0);
            double free = vol?.FreeGb ?? (disk is { IsSystem: true } ? disk.FreeGb : 0);
            var kind = disk?.MediaType switch
            {
                DiskMediaType.Hdd => StorageKind.Hdd, DiskMediaType.Ssd => StorageKind.SataSsd, DiskMediaType.Nvme => StorageKind.Nvme, DiskMediaType.Usb => StorageKind.Usb, _ => StorageKind.Unknown,
            };
            result.Add(new GameDrive(grp.Key, kind, disk?.Model, size, free, grp.Select(x => x.Game).ToList()));
        }
        return result.OrderBy(d => d.Letter, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
