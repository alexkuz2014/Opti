using Opti.Core.Tweaks;
using Opti.Fixes.Services;
using Opti.Workflow.Cleanup;

namespace Opti.Workflow.Maximum;

/// <summary>Один шаг «Максимума оптимизации» для списка «что сделано»: хранится в сессии и переживает перезагрузку.</summary>
public sealed class MaxStep
{
    public string Title { get; set; } = "";
    public string Status { get; set; } = "";
    /// <summary>good / warn / bad / neutral — как у остальных списков интерфейса.</summary>
    public string Tone { get; set; } = "neutral";
    public List<string> Details { get; set; } = new();
}

/// <summary>Итог установки драйверов через Центр обновления Windows.</summary>
public sealed class DriverUpdateReport
{
    public List<string> Installed { get; set; } = new();
    public List<string> Failed { get; set; } = new();
    /// <summary>Видеодрайверы Центр обновления предлагал, но мы их не ставим: свежий видеодрайвер надёжнее брать у производителя.</summary>
    public List<string> SkippedDisplay { get; set; } = new();
    public bool RebootRequired { get; set; }
    /// <summary>Не удалось связаться с Центром обновления (нет сети, служба отключена, нет прав).</summary>
    public string? Error { get; set; }
}

public interface IDriverUpdater
{
    Task<DriverUpdateReport> UpdateAsync(IProgress<string>? progress, CancellationToken ct);
}

/// <summary>
/// Шаги «Максимума оптимизации» после исправлений: твики, очистка, драйверы. Каждый шаг независим: ошибка одного не останавливает остальные.
/// Твики пишутся в журнал той же сессией, что и исправления, поэтому откат сессии возвращает всё разом. Очистка и установка драйверов
/// откату через журнал не подлежат — об этом честно сказано в итоге.
/// </summary>
public sealed class MaxOptimizer
{
    private readonly TweakService _tweaks;
    private readonly Func<IReadOnlyList<ICleanupTarget>> _cleanup;
    private readonly IDriverUpdater? _drivers;
    private readonly int _osBuild;
    private readonly Opti.Core.Profile.SystemProfile? _profile;

    public MaxOptimizer(TweakService tweaks, Func<IReadOnlyList<ICleanupTarget>> cleanup, IDriverUpdater? drivers, int osBuild, Opti.Core.Profile.SystemProfile? profile = null)
    {
        _profile = profile;
        _tweaks = tweaks;
        _cleanup = cleanup;
        _drivers = drivers;
        _osBuild = osBuild;
    }

    public sealed record Result(List<MaxStep> Steps, bool RebootRequired);

    public async Task<Result> RunAsync(string sessionId, IProgress<MaxStep>? stepDone, IProgress<string>? status, bool updateDrivers, CancellationToken ct)
    {
        var steps = new List<MaxStep>();
        bool reboot = false;
        async Task Step(string title, Func<Task<MaxStep>> run)
        {
            if (ct.IsCancellationRequested) return;
            status?.Report(title + "…");
            MaxStep result;
            try { result = await run(); }
            catch (OperationCanceledException) { return; }
            catch (Exception ex) { result = new MaxStep { Title = title, Status = "не удалось", Tone = "bad", Details = { ex.Message } }; }
            result.Title = title;
            steps.Add(result);
            stepDone?.Report(result);
        }

        await Step("Твики для игр и фоновой нагрузки", () => Task.FromResult(ApplyTweaks(sessionId)));
        await Step("Очистка временных файлов", () => CleanAsync(ct));
        if (updateDrivers) await Step("Драйверы через Центр обновления Windows", async () => { var (st, rb) = await UpdateDriversAsync(status, ct); reboot |= rb; return st; });
        return new Result(steps, reboot);
    }

    private MaxStep ApplyTweaks(string sessionId)
    {
        var done = new List<string>();
        var failed = new List<string>();
        foreach (var id in TweakCatalog.MaxIdsFor(_profile))
        {
            var t = TweakCatalog.Find(id);
            if (t is null || !_tweaks.IsAvailable(t, _osBuild) || _tweaks.IsOn(t)) continue;
            try { _tweaks.Enable(t, sessionId); done.Add(t.Title); }
            catch (Exception ex) { failed.Add($"{t.Title}: {ex.Message}"); }
        }
        var step = new MaxStep { Details = done.Select(d => "включено: " + d).Concat(failed.Select(f => "не удалось: " + f)).ToList() };
        (step.Status, step.Tone) = (done.Count, failed.Count) switch
        {
            (0, 0) => ("уже всё включено", "neutral"),
            (_, 0) => ($"включено: {done.Count}", "good"),
            (0, _) => ("не удалось", "bad"),
            _ => ($"включено {done.Count}, не удалось {failed.Count}", "warn"),
        };
        return step;
    }

    private async Task<MaxStep> CleanAsync(CancellationToken ct)
    {
        long freed = 0;
        int files = 0;
        var errors = new List<string>();
        // Только то, что отмечено по умолчанию и чистится самой программой (без «Проверьте сами» и параметров Windows)
        foreach (var target in _cleanup().Where(t => t.DefaultSelected && t.ManualUri is null))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var r = await target.CleanAsync(ct);
                freed += r.FreedBytes;
                files += r.Deleted;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { errors.Add($"{target.Title}: {ex.Message}"); }
        }
        return new MaxStep
        {
            Status = freed > 0 ? $"освобождено {FormatSize(freed)}" : "чистить было нечего",
            Tone = errors.Count > 0 ? "warn" : freed > 0 ? "good" : "neutral",
            Details = new List<string> { $"удалено файлов: {files}" }.Concat(errors).ToList(),
        };
    }

    private async Task<(MaxStep Step, bool Reboot)> UpdateDriversAsync(IProgress<string>? status, CancellationToken ct)
    {
        if (_drivers is null) return (new MaxStep { Status = "недоступно на этом компьютере", Tone = "neutral" }, false);
        var r = await _drivers.UpdateAsync(status, ct);
        var step = new MaxStep();
        step.Details.AddRange(r.Installed.Select(i => "установлено: " + i));
        step.Details.AddRange(r.Failed.Select(f => "не установилось: " + f));
        step.Details.AddRange(r.SkippedDisplay.Select(d => "видеодрайвер пропущен (установите с сайта производителя): " + d));
        if (r.Error is not null) { step.Status = "не удалось проверить"; step.Tone = "warn"; step.Details.Insert(0, r.Error); }
        else if (r.Installed.Count > 0)
        {
            step.Status = $"установлено: {r.Installed.Count}" + (r.Failed.Count > 0 ? $", ошибок: {r.Failed.Count}" : "");
            step.Tone = r.Failed.Count > 0 ? "warn" : "good";
        }
        else if (r.Failed.Count > 0) { step.Status = "не удалось установить"; step.Tone = "warn"; }
        else { step.Status = "новых драйверов нет"; step.Tone = "neutral"; }
        step.Details.Add("Драйверы откатываются в Диспетчере устройств: свойства устройства → «Драйвер» → «Откатить». Перед запуском создана точка восстановления.");
        return (step, r.RebootRequired);
    }

    public static string FormatSize(long bytes) => bytes >= 1L << 30 ? $"{bytes / (double)(1L << 30):0.#} ГБ" : $"{bytes / (1024 * 1024)} МБ";
}
