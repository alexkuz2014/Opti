using System.Diagnostics;
using System.Text;

namespace Opti.Native;

public sealed record ProcessResult(int ExitCode, string StdOut, string StdErr)
{
    public bool Ok => ExitCode == 0;
}

/// <summary>Запуск системных утилит (bcdedit, powercfg, PowerShell) с выводом в UTF-8.</summary>
public interface IProcessRunner
{
    ProcessResult Run(string fileName, string arguments, TimeSpan? timeout = null);
}

public sealed class ProcessRunner : IProcessRunner
{
    public static readonly ProcessRunner Instance = new();

    public ProcessResult Run(string fileName, string arguments, TimeSpan? timeout = null)
    {
        var psi = new ProcessStartInfo(fileName, arguments)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        using var p = Process.Start(psi) ?? throw new InvalidOperationException($"Не удалось запустить {fileName}");
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit((int)(timeout ?? TimeSpan.FromSeconds(60)).TotalMilliseconds))
        {
            try { p.Kill(entireProcessTree: true); } catch { /* уже завершился */ }
            throw new TimeoutException($"{fileName} {arguments}: превышено время ожидания");
        }
        return new ProcessResult(p.ExitCode, stdout.Result, stderr.Result);
    }

    /// <summary>Консольная утилита Windows с выводом в UTF-8 (через chcp 65001).</summary>
    public ProcessResult RunConsoleUtf8(string command, TimeSpan? timeout = null) =>
        Run("cmd.exe", $"/d /c chcp 65001 >nul & {command}", timeout);

    /// <summary>Скрипт PowerShell 5.1 с выводом в UTF-8.</summary>
    public ProcessResult RunPowerShell(string script, TimeSpan? timeout = null)
    {
        var full = "[Console]::OutputEncoding=[Text.Encoding]::UTF8;$ErrorActionPreference='Stop';$ProgressPreference='SilentlyContinue';" + script;
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(full));
        var r = Run("powershell.exe", $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encoded}", timeout);
        return r with { StdErr = CleanPowerShellError(r.StdErr) };
    }

    /// <summary>Экранирование строки для одинарных кавычек PowerShell.</summary>
    /// <summary>
    /// PowerShell при перенаправленном выводе пишет ошибки в виде CLIXML («#&lt; CLIXML &lt;Objs…&gt;»). Достаём из него первую осмысленную строку,
    /// чтобы пользователь видел текст ошибки, а не разметку.
    /// </summary>
    public static string CleanPowerShellError(string stderr)
    {
        if (string.IsNullOrWhiteSpace(stderr) || !stderr.Contains("CLIXML", StringComparison.Ordinal)) return stderr.Trim();
        var parts = System.Text.RegularExpressions.Regex.Matches(stderr, "<S S=\"Error\">(.*?)</S>", System.Text.RegularExpressions.RegexOptions.Singleline)
            .Select(m => System.Net.WebUtility.HtmlDecode(m.Groups[1].Value).Replace("_x000D_", "").Replace("_x000A_", "\n"));
        var text = string.Concat(parts);
        var first = text.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? "";
        // Сообщение PowerShell переносится по ширине консоли — склеиваем до служебных строк («строка:1 знак:…», «+ …»)
        var lines = text.Split('\n').Select(l => l.TrimEnd()).TakeWhile(l => !l.TrimStart().StartsWith("+") && !System.Text.RegularExpressions.Regex.IsMatch(l, @"^\s*(At line|строка):")).ToList();
        var joined = string.Concat(lines).Trim();
        return joined.Length > 0 ? joined : first;
    }

    public static string PsQuote(string s) => "'" + s.Replace("'", "''") + "'";
}
