using Opti.Analysis.Detectors;
using Opti.Core.Findings;
using Opti.Core.Profile;
using Opti.Core.Stutter;

namespace Opti.Workflow.Game;

public sealed record StutterResult(StutterInfo Info, DpcLatencyInfo? Dpc, IReadOnlyList<Finding> Findings);

/// <summary>
/// Проверка фризов: минуту наблюдает за системой (процессор, прерывания драйверов, диск, память, главные потребители) и, если есть права
/// администратора, параллельно пишет DPC/ISR драйверов через ETW. Пользователь в это время играет или работает как обычно.
/// Ничего не меняет: только находки с понятными шагами.
/// </summary>
public static class StutterCheck
{
    public static async Task<StutterResult> RunAsync(IStutterSampler sampler, Func<TimeSpan, CancellationToken, Task<DpcLatencyInfo?>>? captureDpc,
        int seconds, Func<string, bool> isGame, double totalRamMb, IProgress<int>? elapsed, CancellationToken ct, TimeSpan? tick = null, IStallProbe? stallProbe = null)
    {
        var step = tick ?? TimeSpan.FromSeconds(1);
        Task<DpcLatencyInfo?> dpcTask;
        try { dpcTask = captureDpc?.Invoke(TimeSpan.FromTicks(step.Ticks * seconds), ct) ?? Task.FromResult<DpcLatencyInfo?>(null); }
        catch { dpcTask = Task.FromResult<DpcLatencyInfo?>(null); }   // нет прав или ETW недоступен
        // Микрозамирания ловим отдельным потоком: секундные счётчики их не видят
        var stallTask = stallProbe is null ? Task.FromResult<StallSummary?>(null)
            : Task.Factory.StartNew(() => (StallSummary?)stallProbe.Run(TimeSpan.FromTicks(step.Ticks * seconds), ct), TaskCreationOptions.LongRunning);
        var samples = new List<StutterSample>();
        for (int i = 0; i < seconds && !ct.IsCancellationRequested; i++)
        {
            var started = DateTime.UtcNow;
            try { samples.Add(await Task.Run(sampler.Sample, CancellationToken.None)); } catch { /* один замер не вышел — пропускаем секунду */ }
            elapsed?.Report(i + 1);
            var rest = step - (DateTime.UtcNow - started);
            if (rest > TimeSpan.Zero) { try { await Task.Delay(rest, ct); } catch (OperationCanceledException) { break; } }
        }
        DpcLatencyInfo? dpc = null;
        try { dpc = await dpcTask; } catch { /* нет прав или ETW недоступен — остальная проверка остаётся в силе */ }
        StallSummary? stalls = null;
        try { stalls = await stallTask; } catch { /* наблюдение за замираниями не удалось — остальное остаётся */ }
        var info = StutterAnalyzer.Analyze(samples, isGame, totalRamMb, stalls);
        var profile = new SystemProfile { Dpc = dpc, Stutter = info };
        var findings = DetectorCatalog.RunAll(profile, new IDetector[] { new DpcLatencyDetector(), new StutterCauseDetector() });
        return new StutterResult(info, dpc, findings);
    }
}
