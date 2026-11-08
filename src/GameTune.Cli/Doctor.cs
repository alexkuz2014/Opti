using System.Security.Principal;
using GameTune.Fixes.Backends;
using GameTune.Native;
using GameTune.Workflow;
using GameTune.Workflow.Measuring;
using Microsoft.Diagnostics.Tracing.Session;

namespace GameTune.Cli;

/// <summary>Проверка окружения: всё ли доступно для анализа, исправлений и замеров. Ничего не меняет.</summary>
internal static class Doctor
{
    public static int Run(Args a)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Проверка окружения возможна только в Windows.");
        Output.Header($"GameTune {AppInfo.Version}: проверка окружения");
        int fails = 0;
        void Check(string name, Func<(bool Ok, string Detail)> f, bool critical = true)
        {
            (bool ok, string detail) r;
            try { r = f(); } catch (Exception ex) { r = (false, ex.Message); }
            Output.Color(r.ok ? ConsoleColor.Green : critical ? ConsoleColor.Red : ConsoleColor.Yellow, r.ok ? "  ✓ " : critical ? "  ✗ " : "  ! ");
            Console.WriteLine($"{name}: {r.detail}");
            if (!r.ok && critical) fails++;
        }

        Check("Права администратора", () =>
        {
            using var id = WindowsIdentity.GetCurrent();
            bool admin = new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
            return (admin, admin ? "есть" : "нет — запустите от имени администратора");
        });
        Check("Windows", () => (Environment.OSVersion.Version.Build >= 17763, $"сборка {Environment.OSVersion.Version.Build}"));
        Check("ETW (сессии трассировки)", () => (TraceEventSession.IsElevated() == true, TraceEventSession.IsElevated() == true ? "доступны" : "нужны права администратора"));
        Check("bcdedit", () => { var r = ProcessRunner.Instance.RunConsoleUtf8("bcdedit /enum {current}"); return (r.Ok, r.Ok ? "читается" : (r.StdOut + r.StdErr).Trim()); });
        Check("powercfg", () => { var r = ProcessRunner.Instance.RunConsoleUtf8("powercfg /getactivescheme"); return (r.Ok, r.StdOut.Trim()); });
        Check("PowerShell (сетевые адаптеры)", () => { var r = ProcessRunner.Instance.RunPowerShell("(Get-NetAdapter -Physical | Measure-Object).Count"); return (r.Ok, r.Ok ? $"адаптеров: {r.StdOut.Trim()}" : r.StdErr.Trim()); });
        Check("Папка данных", () =>
        {
            Directory.CreateDirectory(GameTunePaths.DataDir);
            var probe = Path.Combine(GameTunePaths.DataDir, ".write-test");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return (true, GameTunePaths.DataDir);
        });
        Check("Журнал изменений", () =>
        {
            using var j = new GameTune.Fixes.Journal.SqliteChangeJournal(GameTunePaths.JournalPath);
            return (true, $"{j.GetAll().Count} записей, активных {j.GetAll().Count(r => !r.RolledBack)}");
        });
        Check("Синтетический тест", () => SyntheticRunner.Locate() is { } b ? (true, b) : (false, "GameTune.Bench.exe не найден рядом с программой"), critical: false);
        Check("PresentMon", () => PresentMonRunner.Locate(GameTuneSettings.Load().PresentMonPath) is { } pm ? (true, pm) : (false, "не найден — режим «Игра через PresentMon» недоступен (необязательно)"), critical: false);
        Check("nvidia-smi", () => { var r = ProcessRunner.Instance.Run("nvidia-smi", "-L", TimeSpan.FromSeconds(5)); return (r.Ok, r.StdOut.Split('\n')[0].Trim()); }, critical: false);
        Check("Задача продолжения", () => new GameTune.Workflow.Sessions.WindowsResumeScheduler().IsScheduled()
            ? (true, "запланирована (ожидается перезагрузка)") : (true, "не запланирована"), critical: false);

        Console.WriteLine(fails == 0 ? "\nВсё необходимое доступно." : $"\nПроблем: {fails}. Исправьте их перед оптимизацией.");
        return fails == 0 ? 0 : 1;
    }
}
