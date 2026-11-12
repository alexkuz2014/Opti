using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Opti.Native;

/// <summary>
/// Режим эффективности процесса (EcoQoS, «Режим эффективности» в Диспетчере задач). В Windows 11 планировщик старается держать такие
/// процессы на энергоэффективных ядрах (на гибридных процессорах Intel) и не поднимает им частоту. Включается только для чужих
/// фоновых процессов, которые пользователь сам разрешил приглушать; игра и её защита не затрагиваются.
/// </summary>
[SupportedOSPlatform("windows")]
public static class ProcessPowerNative
{
    private const int ProcessPowerThrottling = 4;
    private const uint PROCESS_POWER_THROTTLING_CURRENT_VERSION = 1;
    private const uint PROCESS_POWER_THROTTLING_EXECUTION_SPEED = 0x1;
    private const uint PROCESS_SET_INFORMATION = 0x0200;

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_POWER_THROTTLING_STATE
    {
        public uint Version;
        public uint ControlMask;
        public uint StateMask;
    }

    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetProcessInformation(IntPtr process, int infoClass, ref PROCESS_POWER_THROTTLING_STATE info, int size);

    /// <summary>on = включить режим эффективности; off = вернуть управление Windows (флаг снимается, а не «запрещается»).</summary>
    public static bool SetEfficiency(int pid, bool on)
    {
        var h = OpenProcess(PROCESS_SET_INFORMATION, false, pid);
        if (h == IntPtr.Zero) return false;
        try
        {
            var state = new PROCESS_POWER_THROTTLING_STATE
            {
                Version = PROCESS_POWER_THROTTLING_CURRENT_VERSION,
                ControlMask = PROCESS_POWER_THROTTLING_EXECUTION_SPEED,
                StateMask = on ? PROCESS_POWER_THROTTLING_EXECUTION_SPEED : 0,
            };
            return SetProcessInformation(h, ProcessPowerThrottling, ref state, Marshal.SizeOf<PROCESS_POWER_THROTTLING_STATE>());
        }
        finally { CloseHandle(h); }
    }
}
