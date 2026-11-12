using System.Runtime.Versioning;
using System.Text.Json;
using Opti.Native;

namespace Opti.Workflow.Maximum;

/// <summary>
/// Драйверы из Центра обновления Windows (официальный API Windows Update Agent): ищем необновлённые, скачиваем и ставим.
/// Это подписанные драйверы Microsoft Update Catalog, а не файлы с непроверенных сайтов. Видеодрайверы пропускаются:
/// через Центр обновления они приходят старее, чем у производителя. Условия лицензий автоматически не принимаются.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsUpdateDriverUpdater : IDriverUpdater
{
    private const string Script = """
        $session = New-Object -ComObject Microsoft.Update.Session
        $session.ClientApplicationID = 'Opti'
        $searcher = $session.CreateUpdateSearcher()
        $found = $searcher.Search("IsInstalled=0 and Type='Driver' and IsHidden=0")
        $take = New-Object -ComObject Microsoft.Update.UpdateColl
        $skipped = @()
        foreach ($u in $found.Updates) {
            if ($u.EulaAccepted -eq $false) { continue }
            if ($u.DriverClass -eq 'Display') { $skipped += $u.Title; continue }
            [void]$take.Add($u)
        }
        $installed = @(); $failed = @(); $reboot = $false
        if ($take.Count -gt 0) {
            $dl = $session.CreateUpdateDownloader(); $dl.Updates = $take; [void]$dl.Download()
            $ready = New-Object -ComObject Microsoft.Update.UpdateColl
            foreach ($u in $take) { if ($u.IsDownloaded) { [void]$ready.Add($u) } else { $failed += $u.Title } }
            if ($ready.Count -gt 0) {
                $inst = $session.CreateUpdateInstaller(); $inst.Updates = $ready
                $res = $inst.Install(); $reboot = [bool]$res.RebootRequired
                for ($i = 0; $i -lt $ready.Count; $i++) {
                    if ($res.GetUpdateResult($i).ResultCode -eq 2) { $installed += $ready.Item($i).Title } else { $failed += $ready.Item($i).Title }
                }
            }
        }
        [pscustomobject]@{ Installed = @($installed); Failed = @($failed); Skipped = @($skipped); Reboot = $reboot } | ConvertTo-Json -Compress -Depth 4
        """;

    public Task<DriverUpdateReport> UpdateAsync(IProgress<string>? progress, CancellationToken ct) => Task.Run(() =>
    {
        progress?.Report("Ищу драйверы в Центре обновления Windows (до 15 минут)…");
        try
        {
            var r = ProcessRunner.Instance.RunPowerShell(Script, TimeSpan.FromMinutes(15));
            return Parse(r.Ok, r.StdOut, r.StdErr);
        }
        catch (TimeoutException) { return new DriverUpdateReport { Error = "Центр обновления не ответил за 15 минут." }; }
        catch (Exception ex) { return new DriverUpdateReport { Error = ex.Message }; }
    }, ct);

    /// <summary>Разбор вывода скрипта; вынесен отдельно, чтобы проверяться без Windows.</summary>
    public static DriverUpdateReport Parse(bool ok, string stdout, string stderr)
    {
        if (!ok || string.IsNullOrWhiteSpace(stdout))
            return new DriverUpdateReport { Error = string.IsNullOrWhiteSpace(stderr) ? "Центр обновления не вернул ответ (нужны права администратора и интернет)." : stderr.Trim() };
        try
        {
            using var doc = JsonDocument.Parse(stdout.Trim().Split('\n').Last().Trim());
            var root = doc.RootElement;
            List<string> List(string name) => !root.TryGetProperty(name, out var e) ? new()
                : e.ValueKind == JsonValueKind.Array ? e.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x.Length > 0).ToList()
                : e.ValueKind == JsonValueKind.String ? new() { e.GetString()! } : new();
            return new DriverUpdateReport
            {
                Installed = List("Installed"), Failed = List("Failed"), SkippedDisplay = List("Skipped"),
                RebootRequired = root.TryGetProperty("Reboot", out var rb) && rb.ValueKind == JsonValueKind.True,
            };
        }
        catch (JsonException) { return new DriverUpdateReport { Error = "Не удалось разобрать ответ Центра обновления." }; }
    }
}
