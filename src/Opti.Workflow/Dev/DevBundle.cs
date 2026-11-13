using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using Opti.Analysis;
using Opti.Core.Findings;
using Opti.Core.Fixes;
using Opti.Core.Json;
using Opti.Core.Strategy;
using Opti.Fixes.Backends;
using Opti.Fixes.Catalog;
using Opti.Native;

namespace Opti.Workflow.Dev;

/// <summary>
/// Диагностический архив dev.zip для разработчика: полный профиль ПК, находки, план исправлений, журнал изменений,
/// сессии, настройки, журналы ошибок и вывод системных утилит (только чтение).
/// Имя пользователя и компьютера, а также MAC-адреса заменяются заглушками. Пароли, куки и содержимое документов не собираются.
/// </summary>
public static partial class DevBundle
{
    public sealed class Input
    {
        public required string AppVersion { get; init; }
        /// <summary>Почему сделан архив: «после анализа», «выход», «ошибка», «таймер».</summary>
        public required string Reason { get; init; }
        public AnalysisResult? Analysis { get; init; }
        public IChangeJournal? Journal { get; init; }
        /// <summary>Вывод системных утилит (см. <see cref="CollectProbes"/>); null — не собирался.</summary>
        public IReadOnlyDictionary<string, string>? Probes { get; init; }
        public string? Note { get; init; }
    }

    private static readonly UserGoal[] Goals = Enum.GetValues<UserGoal>();

    /// <summary>Записывает архив атомарно: сначала во временный файл рядом, затем замена.</summary>
    public static void Write(string zipPath, Input input)
    {
        var errors = new List<string>();
        var tmp = zipPath + ".tmp";
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(zipPath))!);
        using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            void Text(string name, Func<string> produce)
            {
                try { Add(zip, name, produce()); }
                catch (Exception ex) { errors.Add($"{name}: {ex.GetType().Name}: {ex.Message}"); }
            }

            Text("meta.txt", () => Meta(input));
            if (input.Analysis is { } a)
            {
                Text("analysis/profile.json", () => OptiJson.Serialize(a.Profile));
                Text("analysis/findings.json", () => OptiJson.Serialize(a.Findings));
                Text("analysis/collector-timings.json", () => OptiJson.Serialize(a.Timings.Select(t => new { t.Name, Seconds = Math.Round(t.Elapsed.TotalSeconds, 2), t.Error })));
                Text("analysis/plans.json", () => OptiJson.Serialize(Plans(a)));
                Text("analysis/findings.txt", () => FindingsText(a.Findings));
            }
            else Add(zip, "analysis/README.txt", "Анализ в этом запуске ещё не выполнялся. Нажмите «Проанализировать» и закройте программу — архив обновится.");

            if (input.Journal is { } j)
            {
                Text("state/journal.json", () => OptiJson.Serialize(j.GetAll()));
                Text("state/journal-sessions.json", () => OptiJson.Serialize(j.GetSessions()));
            }
            CopyFiles(zip, OptiPaths.SessionsDir, "state/sessions", "*.json", 20, 2_000_000, errors);
            CopyFiles(zip, OptiPaths.DataDir, "state", "settings.json", 1, 100_000, errors);
            CopyFiles(zip, OptiPaths.LogsDir, "logs", "*.log", 7, 1_000_000, errors);
            CopyFiles(zip, OptiPaths.ReportsDir, "reports", "*.html", 3, 3_000_000, errors);

            if (input.Probes is { Count: > 0 } probes)
                foreach (var (name, text) in probes) Add(zip, "system/" + name + ".txt", text);
            else Add(zip, "system/README.txt", "Вывод системных утилит ещё собирается (около минуты после анализа). Подождите и закройте программу ещё раз.");

            if (errors.Count > 0) Add(zip, "bundle-errors.txt", string.Join("\n", errors));
        }
        File.Move(tmp, zipPath, overwrite: true);
    }

    // ---------- Сбор вывода утилит ----------

    private sealed record Probe(string Name, string Kind, string Command, int TimeoutSec = 25);

    private static IEnumerable<Probe> ProbeList(string? gateway)
    {
        yield return new("powercfg-list", "cmd", "powercfg /list & powercfg /getactivescheme");
        yield return new("powercfg-processor", "cmd", "powercfg /query scheme_current sub_processor");
        yield return new("bcdedit", "cmd", "bcdedit /enum {current}");
        yield return new("netsh-wlan", "cmd", "netsh wlan show interfaces");
        yield return new("ipconfig", "cmd", "ipconfig /all");
        yield return new("netsh-tcp-global", "cmd", "netsh int tcp show global");
        yield return new("fsutil-trim", "cmd", "fsutil behavior query DisableDeleteNotify");
        yield return new("registry-tuning", "cmd",
            string.Join(" & ", new[]
            {
                @"HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers",
                @"HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management",
                @"HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\kernel",
                @"HKLM\SYSTEM\CurrentControlSet\Control\PriorityControl",
                @"HKLM\SYSTEM\CurrentControlSet\Control\Power\PowerThrottling",
                @"HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters",
                @"HKCU\Software\Microsoft\DirectX\UserGpuPreferences",
                @"HKCU\System\GameConfigStore",
                @"HKCU\Software\Microsoft\Windows\CurrentVersion\GameDVR",
                @"HKCU\Software\Microsoft\GameBar",
            }.Select(k => $"echo ==== {k} & reg query \"{k}\"")));
        yield return new("netadapter", "ps", "Get-NetAdapter | Select-Object Name,InterfaceDescription,Status,LinkSpeed,MediaType,PhysicalMediaType,DriverVersionString,DriverDate,InterfaceGuid | ConvertTo-Json -Depth 3");
        yield return new("netadapter-advanced", "ps", "Get-NetAdapterAdvancedProperty | Select-Object Name,DisplayName,RegistryKeyword,DisplayValue,RegistryValue | ConvertTo-Json -Depth 4");
        yield return new("netadapter-power", "ps", "Get-NetAdapterPowerManagement | Format-List * | Out-String -Width 200");
        yield return new("hotfix", "ps", "Get-HotFix | Sort-Object InstalledOn -Descending | Select-Object -First 15 HotFixID,Description,InstalledOn | Format-Table -AutoSize | Out-String -Width 200");
        yield return new("video", "ps", "Get-CimInstance Win32_VideoController | Select-Object Name,DriverVersion,DriverDate,AdapterRAM,VideoProcessor,Status,CurrentRefreshRate | Format-List | Out-String -Width 200");
        yield return new("processes", "ps", "Get-Process | Sort-Object WorkingSet -Descending | Select-Object -First 40 Name,Id,@{n='CpuSec';e={[int]$_.CPU}},@{n='MemMB';e={[int]($_.WorkingSet/1MB)}} | Format-Table -AutoSize | Out-String -Width 200");
        yield return new("services", "ps", "Get-Service wuauserv,BITS,Audiosrv,SysMain,WSearch,Winmgmt,DoSvc | Select-Object Name,Status,StartType | Format-Table -AutoSize | Out-String -Width 200");
        yield return new("defender", "ps", "Get-MpComputerStatus | Select-Object AMServiceEnabled,RealTimeProtectionEnabled,AntivirusSignatureAge,QuickScanAge | Format-List | Out-String -Width 200");
        yield return new("startup-commands", "ps", "Get-CimInstance Win32_StartupCommand | Select-Object Name,Command,Location | Format-Table -AutoSize -Wrap | Out-String -Width 250");
        yield return new("physical-disk", "ps", "Get-PhysicalDisk | Select-Object FriendlyName,MediaType,BusType,HealthStatus,@{n='SizeGB';e={[int]($_.Size/1GB)}} | Format-Table -AutoSize | Out-String -Width 200");
        yield return new("nvidia-smi", "cmd", "nvidia-smi & nvidia-smi -q -d PERFORMANCE,TEMPERATURE,CLOCK,POWER");
        yield return new("systeminfo", "cmd", "systeminfo", 60);
        yield return new("test-netconnection", "ps", "foreach ($h in 'discord.com','store.steampowered.com','www.epicgames.com') { Test-NetConnection $h -Port 443 -WarningAction SilentlyContinue | Format-List ComputerName,RemoteAddress,RemotePort,TcpTestSucceeded,PingSucceeded | Out-String -Width 200 }");
        yield return new("resolve-dns", "ps", "foreach ($s in $null,'1.1.1.1','8.8.8.8') { \"== DNS: $(if ($s) { $s } else { 'системный' })\"; foreach ($h in 'discord.com','store.steampowered.com') { $sw=[Diagnostics.Stopwatch]::StartNew(); try { $r = if ($s) { Resolve-DnsName $h -DnsOnly -Server $s -ErrorAction Stop } else { Resolve-DnsName $h -DnsOnly -ErrorAction Stop }; \"$h: $($sw.ElapsedMilliseconds) мс, $((($r | Where-Object IPAddress).IPAddress) -join ', ')\" } catch { \"$h: ошибка $($_.Exception.Message)\" } } }", 60);
        yield return new("ping-internet", "cmd", "ping -n 12 1.1.1.1", 30);
        if (gateway is not null) yield return new("ping-router", "cmd", $"ping -n 12 {gateway}", 30);
        yield return new("tracert", "cmd", "tracert -d -h 10 -w 800 1.1.1.1", 60);
    }

    /// <summary>
    /// Запускает безопасные утилиты только для чтения параллельно (около минуты). Вызывать после анализа, не во время замеров:
    /// пинги и трассировка нагружают сеть.
    /// </summary>
    public static IReadOnlyDictionary<string, string> CollectProbes(string? gateway = null)
    {
        var result = new System.Collections.Concurrent.ConcurrentDictionary<string, string>();
        if (!OperatingSystem.IsWindows()) return result;
        Parallel.ForEach(ProbeList(gateway), new ParallelOptions { MaxDegreeOfParallelism = 6 }, probe =>
        {
            try
            {
                var timeout = TimeSpan.FromSeconds(probe.TimeoutSec);
                var r = probe.Kind == "ps"
                    ? ProcessRunner.Instance.RunPowerShell(probe.Command, timeout)
                    : ProcessRunner.Instance.RunConsoleUtf8(probe.Command, timeout);
                result[probe.Name] = $"> {probe.Command}\nкод выхода: {r.ExitCode}\n\n{r.StdOut}" + (string.IsNullOrWhiteSpace(r.StdErr) ? "" : $"\n--- stderr ---\n{r.StdErr}");
            }
            catch (Exception ex) { result[probe.Name] = $"> {probe.Command}\nне выполнено: {ex.GetType().Name}: {ex.Message}"; }
        });
        return result;
    }

    // ---------- Разделы ----------

    private static string Meta(Input i)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Opti {i.AppVersion} (DEV)");
        sb.AppendLine($"Архив создан: {DateTimeOffset.Now:O}");
        sb.AppendLine($"Причина: {i.Reason}");
        if (i.Note is not null) sb.AppendLine($"Заметка: {i.Note}");
        sb.AppendLine($"ОС: {Environment.OSVersion} ({(Environment.Is64BitOperatingSystem ? "x64" : "x86")}), процессов-ядер: {Environment.ProcessorCount}");
        sb.AppendLine($"Язык системы: {System.Globalization.CultureInfo.CurrentCulture.Name}, интерфейса: {System.Globalization.CultureInfo.CurrentUICulture.Name}");
        sb.AppendLine($"Работает: {TimeSpan.FromMilliseconds(Environment.TickCount64):d\\.hh\\:mm\\:ss}");
        sb.AppendLine($"Администратор: {IsAdmin()}");
        sb.AppendLine($".NET: {Environment.Version}");
        sb.AppendLine($"Файл программы: {Environment.ProcessPath}");
        sb.AppendLine();
        sb.AppendLine("Что внутри:");
        sb.AppendLine("  analysis/  профиль ПК, находки (JSON и текст), время и ошибки коллекторов, планы исправлений для каждой цели");
        sb.AppendLine("  state/     журнал изменений, сессии, настройки");
        sb.AppendLine("  logs/      журналы ошибок Opti");
        sb.AppendLine("  reports/   последние HTML-отчёты");
        sb.AppendLine("  system/    вывод утилит Windows (только чтение): питание, сеть, драйверы, службы, реестр, пинг и трассировка");
        sb.AppendLine("Имя пользователя и компьютера заменены на <user> и <pc>, MAC-адреса скрыты.");
        return sb.ToString();
    }

    private static bool IsAdmin()
    {
        try
        {
            if (!OperatingSystem.IsWindows()) return false;
            using var id = System.Security.Principal.WindowsIdentity.GetCurrent();
            return new System.Security.Principal.WindowsPrincipal(id).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }

    private static object Plans(AnalysisResult a)
    {
        var engine = new StrategyEngine(FixCatalog.All());
        return Goals.ToDictionary(g => g.ToString(), g =>
        {
            var plan = engine.Build(a.Profile, a.Findings, g);
            return new
            {
                Device = plan.Device.Describe(),
                Fixes = plan.Fixes.Select(f => new { f.Order, f.Fix.Id, f.Fix.Title, f.Selected, Risk = f.Fix.Risk.ToString(), f.Fix.RequiresReboot, f.Fix.RequiresAbTest, f.TriggeredBy, f.Notes }),
                plan.Excluded,
                AdviceOnly = plan.AdviceOnly.Select(x => x.Id),
            };
        });
    }

    private static string FindingsText(IEnumerable<Finding> findings)
    {
        var sb = new StringBuilder();
        foreach (var f in findings)
        {
            sb.AppendLine($"[{f.Severity}] {f.Id}: {f.Title}{(f.IsHardwareLimitation ? "  (железо)" : "")}");
            foreach (var e in f.Evidence) sb.AppendLine($"    • {e}");
            if (f.FixIds.Count > 0) sb.AppendLine($"    исправления: {string.Join(", ", f.FixIds)}");
            sb.AppendLine();
        }
        return sb.Length == 0 ? "Находок нет.\n" : sb.ToString();
    }

    // ---------- Файлы и скрытие личных данных ----------

    private static void CopyFiles(ZipArchive zip, string dir, string target, string pattern, int take, int maxBytes, List<string> errors)
    {
        try
        {
            if (!Directory.Exists(dir)) return;
            foreach (var f in new DirectoryInfo(dir).EnumerateFiles(pattern).OrderByDescending(x => x.LastWriteTimeUtc).Take(take))
            {
                try
                {
                    using var s = new FileStream(f.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    var text = ReadTail(s, maxBytes);
                    Add(zip, $"{target}/{f.Name}", text);
                }
                catch (Exception ex) { errors.Add($"{f.Name}: {ex.Message}"); }
            }
        }
        catch (Exception ex) { errors.Add($"{dir}: {ex.Message}"); }
    }

    private static string ReadTail(Stream s, int maxBytes)
    {
        if (s.Length > maxBytes) s.Seek(-maxBytes, SeekOrigin.End);
        using var r = new StreamReader(s, Encoding.UTF8);
        return (s.Length > maxBytes ? "…(начало файла пропущено)…\n" : "") + r.ReadToEnd();
    }

    private static void Add(ZipArchive zip, string name, string text)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var w = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        w.Write(Redact(text));
    }

    /// <summary>Заменяет имя пользователя, имя компьютера и MAC-адреса.</summary>
    public static string Redact(string text, string? user = null, string? machine = null)
    {
        user ??= Environment.UserName;
        machine ??= Environment.MachineName;
        text = Mac().Replace(text, "XX-XX-XX-XX-XX-XX");
        // Сначала более длинное имя: имя компьютера часто содержит имя пользователя («IVAN-PC»)
        foreach (var (name, stub) in new[] { (user, "<user>"), (machine, "<pc>") }.OrderByDescending(x => x.Item1.Length))
            if (name.Length >= 3) text = Regex.Replace(text, Regex.Escape(name), stub, RegexOptions.IgnoreCase);
        return text;
    }

    [GeneratedRegex(@"\b(?:[0-9A-Fa-f]{2}[-:]){5}[0-9A-Fa-f]{2}\b")]
    private static partial Regex Mac();
}
