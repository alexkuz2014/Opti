using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Opti.Core.Network;

/// <summary>Серия ICMP-пингов: время ответа, потери. Только чтение сети, ничего не меняет.</summary>
public static class PingProbe
{
    public sealed record Series(List<double> Ok, int Lost)
    {
        public int Sent => Ok.Count + Lost;
        public double? Average => Ok.Count > 0 ? Ok.Average() : null;
        public double? Max => Ok.Count > 0 ? Ok.Max() : null;
        public double LossPercent => Sent > 0 ? 100.0 * Lost / Sent : 0;
        public double Jitter => PingProbe.Jitter(Ok);
    }

    public static async Task<Series> RunAsync(string target, int count, int intervalMs = 200, int timeoutMs = 1000, CancellationToken ct = default)
    {
        using var ping = new Ping();
        var ok = new List<double>();
        int lost = 0;
        for (int i = 0; i < count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var sw = Stopwatch.StartNew();
            try
            {
                var r = await ping.SendPingAsync(target, timeoutMs);
                if (r.Status == IPStatus.Success) ok.Add(r.RoundtripTime > 0 ? r.RoundtripTime : sw.Elapsed.TotalMilliseconds);
                else lost++;
            }
            catch (Exception ex) when (ex is PingException or SocketException or InvalidOperationException) { lost++; }
            if (i < count - 1) await Task.Delay(intervalMs, ct);
        }
        return new Series(ok, lost);
    }

    /// <summary>Средняя разница между соседними ответами — так джиттер ощущается в игре.</summary>
    public static double Jitter(IReadOnlyList<double> rtts)
    {
        if (rtts.Count < 2) return 0;
        double sum = 0;
        for (int i = 1; i < rtts.Count; i++) sum += Math.Abs(rtts[i] - rtts[i - 1]);
        return sum / (rtts.Count - 1);
    }

    /// <summary>IPv4-адрес шлюза (роутера) активного подключения.</summary>
    public static string? DefaultGateway()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel))
                .SelectMany(n => n.GetIPProperties().GatewayAddresses)
                .Select(g => g.Address)
                .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !a.Equals(IPAddress.Any))?.ToString();
        }
        catch { return null; }
    }
}
