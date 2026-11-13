using System.Text.RegularExpressions;
using Opti.Core.Profile;

namespace Opti.Core.Network;

/// <summary>Разбор «netsh wlan show networks mode=bssid» и подсчёт соседей, мешающих вашему каналу.</summary>
public static partial class WifiScan
{
    public static List<WifiNeighbor> Parse(string netsh)
    {
        var list = new List<WifiNeighbor>();
        WifiNeighbor? cur = null;
        foreach (var raw in netsh.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (BssidRx().IsMatch(line)) { cur = new WifiNeighbor(); list.Add(cur); continue; }
            if (cur is null) continue;
            if (SignalRx().Match(line) is { Success: true } s && int.TryParse(s.Groups[1].Value, out var sig)) cur.Signal = sig;
            else if (ChannelRx().Match(line) is { Success: true } c && int.TryParse(c.Groups[1].Value, out var ch)) cur.Channel = ch;
        }
        return list.Where(n => n.Channel > 0).ToList();
    }

    /// <summary>
    /// Сколько других сетей заметно мешают каналу. Своя точка доступа есть в списке (самая сильная на своём канале) — её не считаем.
    /// В диапазоне 2,4 ГГц каналы пересекаются (мешают соседи в пределах ±4 номеров), в 5 ГГц — только тот же канал.
    /// </summary>
    public static int Interfering(int ownChannel, IReadOnlyList<WifiNeighbor> scan, int minSignal = 40)
    {
        if (ownChannel <= 0) return 0;
        bool band24 = ownChannel <= 14;
        var rivals = scan.Where(n => n.Signal >= minSignal && (band24 ? Math.Abs(n.Channel - ownChannel) <= 4 : n.Channel == ownChannel)).ToList();
        var own = rivals.Where(n => n.Channel == ownChannel).OrderByDescending(n => n.Signal).FirstOrDefault();
        if (own is not null) rivals.Remove(own);
        return rivals.Count;
    }

    [GeneratedRegex(@"^\s*BSSID\s+\d+\s*:", RegexOptions.IgnoreCase)] private static partial Regex BssidRx();
    [GeneratedRegex(@"^\s*(?:Signal|Сигнал)\s*:\s*(\d+)\s*%", RegexOptions.IgnoreCase)] private static partial Regex SignalRx();
    [GeneratedRegex(@"^\s*(?:Channel|Канал)\s*:\s*(\d+)\s*$", RegexOptions.IgnoreCase)] private static partial Regex ChannelRx();
}
