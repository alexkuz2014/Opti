using System.Diagnostics;
using System.Runtime.InteropServices;
using Opti.Core.Stutter;

namespace Opti.Measure.Capture;

/// <summary>
/// Ловит микрофризы, которые секундные счётчики не видят: поток с повышенным приоритетом короткими отрезками крутится в цикле и
/// отмечает время; если между двумя отметками прошли миллисекунды, поток сняли с процессора (прерывания, DPC, SMI, троттлинг, нагрузка).
/// Нагрузка на одно ядро около трети, права администратора не нужны.
/// </summary>
public sealed class SpinStallProbe : IStallProbe
{
    [DllImport("winmm.dll")] private static extern uint timeBeginPeriod(uint ms);
    [DllImport("winmm.dll")] private static extern uint timeEndPeriod(uint ms);

    public StallSummary Run(TimeSpan duration, CancellationToken ct)
    {
        var tracker = new StallTracker(Stopwatch.Frequency);
        bool fine = OperatingSystem.IsWindows();
        if (fine) { try { timeBeginPeriod(1); } catch { fine = false; } }   // сон по 1 мс вместо 15 мс
        var sw = Stopwatch.StartNew();
        var worker = new Thread(() =>
        {
            long spin = Stopwatch.Frequency / 2000;                          // отрезок ожидания 0,5 мс
            while (sw.Elapsed < duration && !ct.IsCancellationRequested)
            {
                long begin = Stopwatch.GetTimestamp();
                tracker.Skip(begin);
                while (Stopwatch.GetTimestamp() - begin < spin) tracker.Observe(Stopwatch.GetTimestamp());
                Thread.Sleep(1);
            }
        }) { IsBackground = true, Priority = ThreadPriority.AboveNormal, Name = "Opti stall probe" };
        worker.Start();
        worker.Join();
        if (fine) { try { timeEndPeriod(1); } catch { } }
        return tracker.Summary(sw.Elapsed.TotalSeconds);
    }
}
