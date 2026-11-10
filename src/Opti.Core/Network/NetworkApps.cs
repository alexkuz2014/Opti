namespace Opti.Core.Network;

/// <summary>Программы, которые в фоне качают или раздают данные и этим поднимают пинг в играх.</summary>
public static class NetworkApps
{
    /// <summary>Имя процесса (без .exe) → понятное название и что с ним сделать.</summary>
    public static readonly IReadOnlyDictionary<string, (string Title, string Hint)> Known =
        new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase)
        {
            ["qbittorrent"] = ("qBittorrent", "закройте или поставьте раздачи на паузу на время игры"),
            ["utorrent"] = ("µTorrent", "закройте или поставьте раздачи на паузу на время игры"),
            ["uTorrentie"] = ("µTorrent", "закройте или поставьте раздачи на паузу на время игры"),
            ["BitTorrent"] = ("BitTorrent", "закройте или поставьте раздачи на паузу на время игры"),
            ["transmission-qt"] = ("Transmission", "закройте или поставьте раздачи на паузу на время игры"),
            ["deluge"] = ("Deluge", "закройте или поставьте раздачи на паузу на время игры"),
            ["tixati"] = ("Tixati", "закройте или поставьте раздачи на паузу на время игры"),
            ["MediaGet"] = ("MediaGet", "закройте: раздаёт файлы в фоне"),
            ["OneDrive"] = ("OneDrive", "значок в трее → «Приостановить синхронизацию» на время игры"),
            ["Dropbox"] = ("Dropbox", "значок в трее → «Приостановить синхронизацию»"),
            ["GoogleDriveFS"] = ("Google Диск", "значок в трее → «Приостановить синхронизацию»"),
            ["YandexDisk2"] = ("Яндекс Диск", "значок в трее → приостановите синхронизацию"),
            ["MEGAsync"] = ("MEGA", "значок в трее → приостановите передачу"),
            ["IDMan"] = ("Internet Download Manager", "дождитесь конца загрузок или поставьте их на паузу"),
            ["fdm"] = ("Free Download Manager", "дождитесь конца загрузок или поставьте их на паузу"),
            ["JDownloader2"] = ("JDownloader", "дождитесь конца загрузок или поставьте их на паузу"),
        };

    public static IEnumerable<string> Match(IEnumerable<string> processNames) =>
        processNames.Where(Known.ContainsKey).Select(n => Known[n].Title).Distinct(StringComparer.OrdinalIgnoreCase);

    public static string? HintFor(string title) =>
        Known.Values.FirstOrDefault(v => string.Equals(v.Title, title, StringComparison.OrdinalIgnoreCase)).Hint;

    /// <summary>Признаки VPN / туннельных адаптеров в описании интерфейса.</summary>
    private static readonly string[] VpnMarkers =
    {
        "wireguard", "openvpn", "tap-windows", "tap adapter", "wintun", "nordlynx", "nordvpn", "expressvpn", "proton", "mullvad",
        "surfshark", "cloudflare warp", "fortinet", "anyconnect", "globalprotect", "zerotier", "tailscale", "hamachi", "radmin vpn", "amnezia", "outline", "windscribe",
    };

    public static bool LooksLikeVpn(string? description, string? name = null)
    {
        var text = ((description ?? "") + " " + (name ?? "")).ToLowerInvariant();
        return VpnMarkers.Any(text.Contains) || text.Contains(" vpn") || text.StartsWith("vpn", StringComparison.Ordinal);
    }
}
