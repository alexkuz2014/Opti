using System.Diagnostics;
using System.Net.NetworkInformation;
using Opti.Core.Profile;

using Opti.Core.Network;

namespace Opti.Measure.Capture;

/// <summary>Пинг и джиттер в простое и под нагрузкой (скачивание) — для выявления bufferbloat.</summary>
public static class NetworkTest
{
    public const string DefaultTarget = "1.1.1.1";
    public const string DefaultLoadUrl = "https://speed.cloudflare.com/__down?bytes=200000000";

    public static async Task<NetworkQuality> RunAsync(string target = DefaultTarget, string? loadUrl = DefaultLoadUrl,
        int pings = 30, CancellationToken ct = default)
    {
        // До роутера и до интернета одновременно: так видно, где теряются пакеты — дома или у провайдера
        var gateway = PingProbe.DefaultGateway();
        var localTask = gateway is null ? null : PingProbe.RunAsync(gateway, pings, 200, 1000, ct);
        var idle = await PingProbe.RunAsync(target, pings, 200, 1000, ct);
        var local = localTask is null ? null : await localTask;
        double? loaded = null;
        if (loadUrl is not null)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var load = Task.WhenAll(Enumerable.Range(0, 4).Select(_ => DownloadAsync(loadUrl, cts.Token)));
            await Task.Delay(2000, ct); // дать очереди наполниться
            var underLoad = await PingProbe.RunAsync(target, pings, 200, 1000, ct);
            cts.Cancel();
            try { await load; } catch { /* отмена загрузки */ }
            loaded = underLoad.Average;
        }
        var q = new NetworkQuality
        {
            Target = target,
            IdlePingMs = idle.Average ?? double.NaN,
            IdleJitterMs = idle.Jitter,
            IdleMaxPingMs = idle.Max,
            LoadedPingMs = loaded,
            PacketLossPercent = idle.LossPercent,
        };
        if (gateway is not null && local is { Ok.Count: > 0 })
        {
            q.Gateway = gateway;
            q.GatewayPingMs = local.Average;
            q.GatewayJitterMs = local.Jitter;
            q.GatewayLossPercent = local.LossPercent;
        }
        return q;
    }

    private static async Task DownloadAsync(string url, CancellationToken ct)
    {
        using var http = new HttpClient();
        using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        await using var s = await resp.Content.ReadAsStreamAsync(ct);
        var buf = new byte[1 << 16];
        while (await s.ReadAsync(buf, ct) > 0) { }
    }

    /// <summary>Джиттер — среднее абсолютное изменение между соседними пингами (RFC 3550 в упрощённом виде).</summary>
    public static double Jitter(IReadOnlyList<double> rtts) => PingProbe.Jitter(rtts);
}
