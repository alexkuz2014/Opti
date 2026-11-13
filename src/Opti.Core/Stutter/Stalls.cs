namespace Opti.Core.Stutter;

/// <summary>Итог наблюдения за «замираниями» системы: моменты, когда поток не получал процессор дольше обычного.</summary>
public sealed class StallSummary
{
    public double Seconds { get; set; }
    /// <summary>Замираний на 4 мс и дольше (заметны как микрофриз).</summary>
    public int Over4Ms { get; set; }
    /// <summary>Замираний на 16 мс и дольше (дольше кадра при 60 FPS).</summary>
    public int Over16Ms { get; set; }
    public double MaxMs { get; set; }
    /// <summary>Секунды от начала проверки, когда случились самые длинные замирания.</summary>
    public List<(double AtSec, double Ms)> Worst { get; set; } = new();
}

public interface IStallProbe
{
    /// <summary>Наблюдает указанное время; отмена прерывает наблюдение и возвращает то, что накоплено.</summary>
    StallSummary Run(TimeSpan duration, CancellationToken ct);
}

/// <summary>
/// Считает паузы между последовательными отметками времени. Отдельный от потока класс, чтобы проверяться без Windows:
/// пауза внутри короткого цикла ожидания, где отметки идут через доли микросекунды, означает, что поток сняли с процессора.
/// </summary>
public sealed class StallTracker
{
    private readonly long _freq;
    private readonly double _minMs;
    private long _last, _start;
    private readonly List<(double, double)> _all = new();
    public int Over4Ms { get; private set; }
    public int Over16Ms { get; private set; }
    public double MaxMs { get; private set; }

    public StallTracker(long ticksPerSecond, double minRecordMs = 2) { _freq = ticksPerSecond; _minMs = minRecordMs; }

    /// <summary>Начало наблюдения или возврат после намеренной паузы (сна): эту паузу замиранием не считаем.</summary>
    public void Skip(long now) { if (_start == 0) _start = now; _last = now; }

    public void Observe(long now)
    {
        if (_last != 0)
        {
            double ms = (now - _last) * 1000.0 / _freq;
            if (ms >= _minMs)
            {
                if (ms >= 4) Over4Ms++;
                if (ms >= 16) Over16Ms++;
                MaxMs = Math.Max(MaxMs, ms);
                _all.Add(((_last - _start) / (double)_freq, ms));
            }
        }
        _last = now;
    }

    public StallSummary Summary(double seconds) => new()
    {
        Seconds = seconds, Over4Ms = Over4Ms, Over16Ms = Over16Ms, MaxMs = Math.Round(MaxMs, 1),
        Worst = _all.OrderByDescending(x => x.Item2).Take(5).OrderBy(x => x.Item1).Select(x => (Math.Round(x.Item1, 1), Math.Round(x.Item2, 1))).ToList(),
    };
}
