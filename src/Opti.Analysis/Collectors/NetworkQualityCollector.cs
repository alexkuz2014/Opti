using Opti.Core.Network;
using Opti.Core.Profile;

namespace Opti.Analysis.Collectors;

/// <summary>
/// Быстрая проверка сети: пинг до роутера и до интернета одновременно (несколько секунд, без нагрузки на канал).
/// Сравнение двух серий показывает, где теряются пакеты — дома (Wi-Fi, кабель) или у провайдера.
/// </summary>
public sealed class NetworkQualityCollector : ICollector
{
    private readonly string _target;
    private readonly int _pings;

    public NetworkQualityCollector(string target = "1.1.1.1", int pings = 20)
    {
        _target = string.IsNullOrWhiteSpace(target) ? "1.1.1.1" : target;
        _pings = pings;
    }

    public string Name => "Качество сети";

    public void Collect(SystemProfile p)
    {
        if (p.NetQuality is not null) return;
        var q = Task.Run(() => MeasureAsync(_target, _pings)).GetAwaiter().GetResult();
        if (q is not null) p.NetQuality = q;
    }

    public static async Task<NetworkQuality?> MeasureAsync(string target, int pings, CancellationToken ct = default)
    {
        var gateway = PingProbe.DefaultGateway();
        var internet = PingProbe.RunAsync(target, pings, 150, 1000, ct);
        var local = gateway is null ? null : PingProbe.RunAsync(gateway, pings, 150, 1000, ct);
        var i = await internet;
        var g = local is null ? null : await local;
        return Build(target, i, gateway, g);
    }

    /// <summary>Нет ни одного ответа — интернета нет или ICMP закрыт: о качестве судить нельзя.</summary>
    public static NetworkQuality? Build(string target, PingProbe.Series internet, string? gateway, PingProbe.Series? local)
    {
        if (internet.Ok.Count == 0) return null;
        var q = new NetworkQuality
        {
            Target = target,
            IdlePingMs = internet.Average!.Value,
            IdleJitterMs = internet.Jitter,
            IdleMaxPingMs = internet.Max,
            PacketLossPercent = internet.LossPercent,
        };
        // Часть роутеров не отвечает на пинг — тогда данных о домашнем участке просто нет
        if (gateway is not null && local is { Ok.Count: > 0 })
        {
            q.Gateway = gateway;
            q.GatewayPingMs = local.Average;
            q.GatewayJitterMs = local.Jitter;
            q.GatewayLossPercent = local.LossPercent;
        }
        return q;
    }
}
