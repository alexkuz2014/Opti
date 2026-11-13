using System.Diagnostics;
using System.Net.Sockets;
using System.Text.Json;
using System.Runtime.Versioning;
using Opti.Core.Network;
using Opti.Core.Profile;
using Opti.Native;

namespace Opti.Analysis.Collectors;

/// <summary>
/// Для каждого игрового сервиса: отвечает ли DNS (системный и публичный 1.1.1.1, через Resolve-DnsName) и открывается ли порт
/// (TCP, как Test-NetConnection -Port). Только чтение; кэш DNS перед замером сбрасывается, чтобы системный DNS не отвечал из памяти.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ServiceReachabilityCollector : ICollector
{
    public string Name => "Доступность сервисов";

    private static string Script => "Clear-DnsClientCache -ErrorAction SilentlyContinue;" +
        "$out = foreach ($h in @(" + string.Join(",", ServiceTargets.All.Select(t => "'" + t.Host + "'")) + ")) {" +
        " $r = [ordered]@{ Host=$h; SysOk=$false; SysMs=0; PubOk=$false; PubMs=0 };" +
        " try { $sw=[Diagnostics.Stopwatch]::StartNew(); $x = Resolve-DnsName $h -DnsOnly -Type A -ErrorAction Stop; $sw.Stop(); $r.SysOk=[bool]$x; $r.SysMs=[int]$sw.ElapsedMilliseconds } catch {}" +
        " try { $sw=[Diagnostics.Stopwatch]::StartNew(); $x = Resolve-DnsName $h -DnsOnly -Type A -Server 1.1.1.1 -ErrorAction Stop; $sw.Stop(); $r.PubOk=[bool]$x; $r.PubMs=[int]$sw.ElapsedMilliseconds } catch {}" +
        " [pscustomobject]$r }; @($out) | ConvertTo-Json -Compress";

    public void Collect(SystemProfile p)
    {
        List<ServiceCheck> checks;
        try
        {
            var r = ProcessRunner.Instance.RunPowerShell(Script, TimeSpan.FromSeconds(60));
            checks = r.Ok ? ParseDns(r.StdOut) : new();
        }
        catch (Exception ex) { p.CollectorErrors.Add("DNS: " + ex.Message); checks = new(); }
        // DNS не измерился (нет Resolve-DnsName) — всё равно проверим порты, но выводов про DNS делать не будем
        if (checks.Count == 0) checks = ServiceTargets.All.Select(t => new ServiceCheck { Host = t.Host, SystemDnsOk = true, PublicDnsOk = true }).ToList();
        foreach (var c in checks)
        {
            var t = ServiceTargets.All.FirstOrDefault(x => x.Host.Equals(c.Host, StringComparison.OrdinalIgnoreCase));
            c.Name = t.Name ?? c.Host; c.Port = t.Port == 0 ? 443 : t.Port; c.IsControl = ServiceTargets.All[0].Host == c.Host;
        }
        Task.WaitAll(checks.Select(c => Task.Run(() => ProbeTcp(c))).ToArray(), TimeSpan.FromSeconds(15));
        p.NetSettings.Services = checks;
    }

    private static void ProbeTcp(ServiceCheck c)
    {
        try
        {
            var sw = Stopwatch.StartNew();
            using var tcp = new TcpClient();
            var connect = tcp.ConnectAsync(c.Host, c.Port);
            c.TcpOk = connect.Wait(TimeSpan.FromSeconds(5)) && tcp.Connected;
            c.TcpMs = c.TcpOk ? (int)sw.ElapsedMilliseconds : 0;
        }
        catch { c.TcpOk = false; }
    }

    /// <summary>Разбор вывода скрипта (объект или массив объектов); вынесен отдельно ради проверки без Windows.</summary>
    public static List<ServiceCheck> ParseDns(string json)
    {
        var list = new List<ServiceCheck>();
        if (string.IsNullOrWhiteSpace(json)) return list;
        try
        {
            using var doc = JsonDocument.Parse(json.Trim());
            var items = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement.EnumerateArray().ToList() : new() { doc.RootElement };
            foreach (var e in items)
            {
                bool B(string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.True;
                int I(string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0;
                if (!e.TryGetProperty("Host", out var h) || h.GetString() is not { Length: > 0 } host) continue;
                list.Add(new ServiceCheck { Host = host, SystemDnsOk = B("SysOk"), SystemDnsMs = I("SysMs"), PublicDnsOk = B("PubOk"), PublicDnsMs = I("PubMs") });
            }
        }
        catch (JsonException) { /* вывода нет — DNS не оцениваем */ }
        return list;
    }
}
