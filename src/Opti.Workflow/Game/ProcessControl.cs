using System.Diagnostics;

namespace Opti.Workflow.Game;

public enum ProcPriority { Idle, BelowNormal, Normal, AboveNormal, High, Other }

/// <summary>Запущенный процесс в минимальном виде — достаточно для списка «Перед игрой» и для приглушения фона.</summary>
public sealed record ProcInfo(int Pid, string Name, long WorkingSetBytes, ProcPriority Priority, bool HasWindow);

/// <summary>Шов над процессами Windows: в тестах подменяется, в программе работает через System.Diagnostics.</summary>
public interface IProcessControl
{
    IReadOnlyList<ProcInfo> List();
    bool TrySetPriority(int pid, ProcPriority priority);
    /// <summary>Просит закрыться штатно (как крестик в окне); если не вышло за время — завершает процесс.</summary>
    bool TryClose(int pid, TimeSpan grace);
}

public sealed class SystemProcessControl : IProcessControl
{
    public IReadOnlyList<ProcInfo> List()
    {
        var result = new List<ProcInfo>();
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                var prio = p.PriorityClass switch
                {
                    ProcessPriorityClass.Idle => ProcPriority.Idle,
                    ProcessPriorityClass.BelowNormal => ProcPriority.BelowNormal,
                    ProcessPriorityClass.Normal => ProcPriority.Normal,
                    ProcessPriorityClass.AboveNormal => ProcPriority.AboveNormal,
                    ProcessPriorityClass.High => ProcPriority.High,
                    _ => ProcPriority.Other,
                };
                result.Add(new ProcInfo(p.Id, p.ProcessName, p.WorkingSet64, prio, p.MainWindowHandle != IntPtr.Zero));
            }
            catch { /* системный процесс или уже завершился */ }
            finally { p.Dispose(); }
        }
        return result;
    }

    public bool TrySetPriority(int pid, ProcPriority priority)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            p.PriorityClass = priority switch
            {
                ProcPriority.Idle => ProcessPriorityClass.Idle,
                ProcPriority.BelowNormal => ProcessPriorityClass.BelowNormal,
                ProcPriority.AboveNormal => ProcessPriorityClass.AboveNormal,
                ProcPriority.High => ProcessPriorityClass.High,
                _ => ProcessPriorityClass.Normal,
            };
            return true;
        }
        catch { return false; }
    }

    public bool TryClose(int pid, TimeSpan grace)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            if (p.HasExited) return true;
            if (p.MainWindowHandle != IntPtr.Zero) p.CloseMainWindow();
            if (p.WaitForExit(grace)) return true;
            p.Kill(entireProcessTree: false);
            return p.WaitForExit(2000);
        }
        catch (ArgumentException) { return true; } // процесса уже нет
        catch { return false; }
    }
}
