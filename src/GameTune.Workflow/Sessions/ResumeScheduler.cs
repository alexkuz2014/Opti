using System.Runtime.Versioning;
using GameTune.Native;

namespace GameTune.Workflow.Sessions;

public interface IResumeScheduler
{
    /// <summary>Запустить программу с указанными аргументами при следующем входе пользователя (с повышенными правами).</summary>
    void ScheduleOnLogon(string exePath, string arguments);
    void Cancel();
    bool IsScheduled();
}

/// <summary>Задача планировщика «GameTune\Resume» — запускается при входе текущего пользователя, с наивысшими правами, без UAC.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsResumeScheduler : IResumeScheduler
{
    public const string TaskName = @"GameTune\Resume";
    private readonly ProcessRunner _runner = ProcessRunner.Instance;

    public void ScheduleOnLogon(string exePath, string arguments)
    {
        if (exePath.Contains('"')) throw new ArgumentException("Недопустимый путь к программе");
        var user = Environment.UserDomainName + "\\" + Environment.UserName;
        var tr = $"\\\"{exePath}\\\" {arguments}";
        var r = _runner.Run("schtasks.exe", $"/Create /F /TN \"{TaskName}\" /SC ONLOGON /RU \"{user}\" /IT /RL HIGHEST /TR \"{tr}\"");
        if (!r.Ok) throw new IOException("Не удалось создать задачу продолжения: " + (r.StdErr + r.StdOut).Trim());
    }

    public void Cancel() => _runner.Run("schtasks.exe", $"/Delete /F /TN \"{TaskName}\"");

    public bool IsScheduled() => _runner.Run("schtasks.exe", $"/Query /TN \"{TaskName}\"").Ok;
}

public static class RebootHelper
{
    /// <summary>Время последней загрузки системы.</summary>
    public static DateTimeOffset LastBootTime => DateTimeOffset.Now - TimeSpan.FromMilliseconds(Environment.TickCount64);

    /// <summary>Была ли перезагрузка после указанного момента.</summary>
    public static bool RebootedSince(DateTimeOffset moment) => LastBootTime > moment;

    [SupportedOSPlatform("windows")]
    public static void RestartNow(int delaySec = 10) =>
        ProcessRunner.Instance.Run("shutdown.exe", $"/r /t {delaySec} /c \"GameTune: перезагрузка для применения настроек. Программа продолжит работу после входа.\"");
}
