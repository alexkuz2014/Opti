using System.Globalization;
using System.Windows;
using System.Windows.Media;
using GameTune.Workflow.Reports;

namespace GameTune.App.Controls;

/// <summary>Базовый класс графиков: оси, сетка и подписи.</summary>
public abstract class ChartBase : FrameworkElement
{
    public static readonly DependencyProperty PrimaryProperty = DependencyProperty.Register(nameof(Primary), typeof(IReadOnlyList<double>), typeof(ChartBase),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty SecondaryProperty = DependencyProperty.Register(nameof(Secondary), typeof(IReadOnlyList<double>), typeof(ChartBase),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    // Значения по умолчанию у DependencyProperty должны быть замороженными (иначе WPF бросает исключение при регистрации).
    public static readonly DependencyProperty PrimaryBrushProperty = DependencyProperty.Register(nameof(PrimaryBrush), typeof(Brush), typeof(ChartBase),
        new FrameworkPropertyMetadata(Frozen(0x7D, 0x86, 0x96), FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty SecondaryBrushProperty = DependencyProperty.Register(nameof(SecondaryBrush), typeof(Brush), typeof(ChartBase),
        new FrameworkPropertyMetadata(Frozen(0x5B, 0x9B, 0xFF), FrameworkPropertyMetadataOptions.AffectsRender));

    private static Brush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    public IReadOnlyList<double>? Primary { get => (IReadOnlyList<double>?)GetValue(PrimaryProperty); set => SetValue(PrimaryProperty, value); }
    public IReadOnlyList<double>? Secondary { get => (IReadOnlyList<double>?)GetValue(SecondaryProperty); set => SetValue(SecondaryProperty, value); }
    public Brush PrimaryBrush { get => (Brush)GetValue(PrimaryBrushProperty); set => SetValue(PrimaryBrushProperty, value); }
    public Brush SecondaryBrush { get => (Brush)GetValue(SecondaryBrushProperty); set => SetValue(SecondaryBrushProperty, value); }

    protected static readonly Brush GridBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x2B, 0x30, 0x38)));
    protected static readonly Brush LabelBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x9A, 0xA2, 0xAE)));
    protected static readonly Pen GridPen = FreezePen(new Pen(GridBrush, 1));
    protected const double Left = 46, Right = 10, Top = 10, Bottom = 24;

    private static Brush Freeze(Brush b) { b.Freeze(); return b; }
    private static Pen FreezePen(Pen p) { p.Freeze(); return p; }

    protected void Label(DrawingContext dc, string text, double x, double y, TextAlignment align)
    {
        var ft = new FormattedText(text, CultureInfo.GetCultureInfo("ru-RU"), FlowDirection.LeftToRight, new Typeface("Segoe UI"), 11, LabelBrush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        double dx = align switch { TextAlignment.Right => -ft.Width, TextAlignment.Center => -ft.Width / 2, _ => 0 };
        dc.DrawText(ft, new Point(x + dx, y - ft.Height / 2));
    }

    protected void Grid(DrawingContext dc, double w, double h, double yMax, string unit)
    {
        for (int i = 0; i <= 4; i++)
        {
            double v = yMax * i / 4;
            double y = Top + (h - Top - Bottom) * (1 - i / 4.0);
            dc.DrawLine(GridPen, new Point(Left, y), new Point(w - Right, y));
            Label(dc, v.ToString(v >= 10 ? "0" : "0.#", CultureInfo.GetCultureInfo("ru-RU")) + (i == 4 ? " " + unit : ""), Left - 6, y, TextAlignment.Right);
        }
    }

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 600 : availableSize.Width, double.IsInfinity(availableSize.Height) ? 220 : Math.Min(availableSize.Height, 260));
}

/// <summary>Фреймтайм во времени: до двух рядов («до» и «после»). Прореживание сохраняет пики.</summary>
public sealed class FrameTimeChart : ChartBase
{
    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));
        var series = new[] { (Primary, PrimaryBrush), (Secondary, SecondaryBrush) }.Where(s => s.Item1 is { Count: > 1 }).ToList();
        if (series.Count == 0 || w < Left + Right + 20 || h < Top + Bottom + 20) return;

        var all = series.SelectMany(s => s.Item1!).ToArray();
        Array.Sort(all);
        double yMax = SvgCharts.NiceCeil(Math.Max(Pct(all, 99.9) * 1.25, Pct(all, 50) * 2));
        double xMax = series.Max(s => s.Item1!.Sum() / 1000.0);
        double pw = w - Left - Right, ph = h - Top - Bottom;
        Grid(dc, w, h, yMax, "мс");
        for (int i = 0; i <= 4; i++)
            Label(dc, (xMax * i / 4).ToString("0.#", CultureInfo.GetCultureInfo("ru-RU")) + " с", Left + pw * i / 4, h - Bottom / 2 + 2, TextAlignment.Center);

        foreach (var (data, brush) in series)
        {
            var pts = SvgCharts.Decimate(data!, (int)pw);
            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                bool first = true;
                foreach (var (sec, ms) in pts)
                {
                    var p = new Point(Left + sec / xMax * pw, Top + ph - Math.Min(ms, yMax) / yMax * ph);
                    if (first) { ctx.BeginFigure(p, false, false); first = false; }
                    else ctx.LineTo(p, true, false);
                }
            }
            geo.Freeze();
            var pen = new Pen(brush, 1.2);
            pen.Freeze();
            dc.DrawGeometry(null, pen, geo);
        }
    }

    private static double Pct(double[] sorted, double p) => GameTune.Measure.Stats.Metrics.PercentileSorted(sorted, p);
}

/// <summary>Гистограмма распределения фреймтайма (доля кадров, %).</summary>
public sealed class HistogramChart : ChartBase
{
    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));
        var series = new[] { (Primary, PrimaryBrush), (Secondary, SecondaryBrush) }.Where(s => s.Item1 is { Count: > 1 }).ToList();
        if (series.Count == 0 || w < Left + Right + 20 || h < Top + Bottom + 20) return;
        var all = series.SelectMany(s => s.Item1!).ToArray();
        Array.Sort(all);
        double lo = Math.Floor(GameTune.Measure.Stats.Metrics.PercentileSorted(all, 0.5));
        double hi = Math.Ceiling(GameTune.Measure.Stats.Metrics.PercentileSorted(all, 99));
        if (hi - lo < 4) hi = lo + 4;
        const int bins = 40;
        double bw = (hi - lo) / bins;
        var hist = series.Select(s =>
        {
            var arr = new double[bins];
            foreach (var v in s.Item1!) { int b = (int)((v - lo) / bw); if (b >= 0 && b < bins) arr[b]++; }
            return arr.Select(c => c * 100.0 / s.Item1!.Count).ToArray();
        }).ToList();
        double yMax = SvgCharts.NiceCeil(hist.SelectMany(x => x).DefaultIfEmpty(1).Max());
        double pw = w - Left - Right, ph = h - Top - Bottom;
        Grid(dc, w, h, yMax, "%");
        double slot = pw / bins, barW = Math.Max(1, (slot - 2) / series.Count);
        for (int si = 0; si < series.Count; si++)
            for (int b = 0; b < bins; b++)
            {
                double v = hist[si][b];
                if (v <= 0) continue;
                double bh = v / yMax * ph;
                dc.DrawRoundedRectangle(series[si].Item2, null, new Rect(Left + b * slot + 1 + si * barW, Top + ph - bh, barW, bh), 1.5, 1.5);
            }
        for (int i = 0; i <= 5; i++)
            Label(dc, (lo + (hi - lo) * i / 5).ToString("0.#", CultureInfo.GetCultureInfo("ru-RU")) + " мс", Left + pw * i / 5, h - Bottom / 2 + 2, TextAlignment.Center);
    }
}
