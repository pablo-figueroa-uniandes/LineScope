using System.Globalization;
using System.Windows;
using System.Windows.Media;
using LineScope.App.Model;
using LineScope.Core;

namespace LineScope.App.Views;

/// <summary>What a <see cref="ChartView"/> draws: one line series, its axes and an optional vertical rule.</summary>
public sealed record ChartData(
    double[] X, double[] Y,
    double XMin, double XMax, double YMin, double YMax,
    string XLabel, string YLabel,
    Brush Stroke,
    double? RuleX = null, string? RuleLabel = null);

/// <summary>A minimal line chart (the counterpart of Swift Charts' LinePlot) drawn directly with WPF.</summary>
public abstract class ChartView : FrameworkElement
{
    private const double Pad = 10;

    protected abstract ChartData? GetData();

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));
        var data = GetData();
        if (data is null || ActualWidth < 60 || ActualHeight < 50) return;

        double dip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var textBrush = SystemColors.ControlTextBrush;
        var secondary = SystemColors.GrayTextBrush;
        var gridPen = new Pen(new SolidColorBrush(Color.FromArgb(40, 128, 128, 128)), 1);
        var axisPen = new Pen(new SolidColorBrush(Color.FromArgb(110, 128, 128, 128)), 1);

        FormattedText Text(string s, Brush brush, double size = 10.5) =>
            new(s, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), size, brush, dip);

        double yRange = data.YMax - data.YMin, xRange = data.XMax - data.XMin;
        if (!(yRange > 0) || !(xRange > 0)) return;

        // Y tick labels decide the left margin.
        double approxPlotH = ActualHeight - 2 * Pad - 34;
        var yTicks = Ticks(data.YMin, data.YMax, Math.Max(2, (int)(approxPlotH / 28)));
        var yLabels = yTicks.Select(v => Text(TickLabel(v, yTicks), secondary)).ToList();
        double yLabelWidth = yLabels.Count > 0 ? yLabels.Max(t => t.Width) : 0;
        var yTitle = Text(data.YLabel, secondary, 11);

        double left = Pad + yTitle.Height + 4 + yLabelWidth + 5;
        double right = ActualWidth - Pad - 4;
        double top = Pad;
        var xTitle = Text(data.XLabel, secondary, 11);
        double bottom = ActualHeight - Pad - xTitle.Height - 4 - 14;
        if (right - left < 20 || bottom - top < 20) return;
        var plot = new Rect(left, top, right - left, bottom - top);

        double Px(double x) => plot.Left + (x - data.XMin) / xRange * plot.Width;
        double Py(double y) => plot.Bottom - (y - data.YMin) / yRange * plot.Height;

        // Grid and ticks.
        for (int i = 0; i < yTicks.Length; i++)
        {
            double y = Py(yTicks[i]);
            dc.DrawLine(gridPen, new Point(plot.Left, y), new Point(plot.Right, y));
            var t = yLabels[i];
            dc.DrawText(t, new Point(plot.Left - 5 - t.Width, y - t.Height / 2));
        }
        var xTicks = Ticks(data.XMin, data.XMax, Math.Max(2, (int)(plot.Width / 70)));
        foreach (double v in xTicks)
        {
            double x = Px(v);
            dc.DrawLine(gridPen, new Point(x, plot.Top), new Point(x, plot.Bottom));
            dc.DrawLine(axisPen, new Point(x, plot.Bottom), new Point(x, plot.Bottom + 3));
            var t = Text(TickLabel(v, xTicks), secondary);
            double tx = Math.Clamp(x - t.Width / 2, 0, ActualWidth - t.Width);
            dc.DrawText(t, new Point(tx, plot.Bottom + 3));
        }
        dc.DrawLine(axisPen, new Point(plot.Left, plot.Bottom), new Point(plot.Right, plot.Bottom));
        dc.DrawLine(axisPen, new Point(plot.Left, plot.Top), new Point(plot.Left, plot.Bottom));

        // Axis titles.
        dc.DrawText(xTitle, new Point(plot.Left + (plot.Width - xTitle.Width) / 2, plot.Bottom + 18));
        dc.PushTransform(new RotateTransform(-90, Pad, plot.Top + plot.Height / 2));
        dc.DrawText(yTitle, new Point(Pad - yTitle.Width / 2, plot.Top + plot.Height / 2));
        dc.Pop();

        dc.PushClip(new RectangleGeometry(new Rect(plot.Left, plot.Top - 1, plot.Width, plot.Height + 2)));

        // Rule (Nyquist).
        if (data.RuleX is double rx)
        {
            var rulePen = new Pen(secondary, 1) { DashStyle = new DashStyle([4, 3], 0) };
            double x = Px(rx);
            dc.DrawLine(rulePen, new Point(x, plot.Top), new Point(x, plot.Bottom));
            if (data.RuleLabel is { } label)
            {
                var t = Text(label, secondary, 10);
                dc.DrawText(t, new Point(x - t.Width - 3, plot.Top + 1));
            }
        }

        // Series.
        int n = Math.Min(data.X.Length, data.Y.Length);
        if (n >= 2)
        {
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(new Point(Px(data.X[0]), Py(data.Y[0])), false, false);
                var points = new List<Point>(n - 1);
                for (int i = 1; i < n; i++) points.Add(new Point(Px(data.X[i]), Py(data.Y[i])));
                ctx.PolyLineTo(points, true, true);
            }
            geometry.Freeze();
            dc.DrawGeometry(null, new Pen(data.Stroke, 1.2) { LineJoin = PenLineJoin.Round }, geometry);
        }
        dc.Pop();
    }

    /// <summary>"Nice" tick values (steps of 1, 2 or 5 × 10ⁿ) covering [min, max].</summary>
    internal static double[] Ticks(double min, double max, int maxCount)
    {
        double range = max - min;
        if (!(range > 0)) return [min];
        double rough = range / Math.Max(maxCount, 1);
        double mag = Math.Pow(10, Math.Floor(Math.Log10(rough)));
        double residual = rough / mag;
        double step = (residual <= 1 ? 1 : residual <= 2 ? 2 : residual <= 5 ? 5 : 10) * mag;
        var ticks = new List<double>();
        for (double v = Math.Ceiling(min / step) * step; v <= max + step * 1e-9; v += step)
            ticks.Add(Math.Abs(v) < step * 1e-9 ? 0 : v);
        return [.. ticks];
    }

    private static string TickLabel(double v, double[] ticks)
    {
        double step = ticks.Length > 1 ? ticks[1] - ticks[0] : Math.Abs(v);
        int decimals = step > 0 ? Math.Clamp(-(int)Math.Floor(Math.Log10(step) + 1e-9), 0, 6) : 0;
        return Fmt.Number(v, decimals);
    }
}

/// <summary>Zone 2: the selected channel sampled along the line in this variant's own pixel grid.</summary>
public sealed class ProfileChart(Session session, ImageVariant variant) : ChartView
{
    public ImageVariant Variant { get; set; } = variant;

    protected override ChartData GetData()
    {
        var profile = session.Profile(Variant);
        var (min, max) = session.ColorModel.RangeOfChannel(session.ChannelIndex);
        return new ChartData(
            profile.X, profile.Values.Select(v => (double)v).ToArray(), 0, 1, min, max,
            $"Position along line ({profile.Samples.Colors.Length} samples)", session.ChannelName, ChannelBrush());
    }

    private Brush ChannelBrush() => (session.ColorModel, session.ChannelIndex) switch
    {
        (ColorModel.Rgba, 0) => Rgb(0xFF, 0x3B, 0x30),
        (ColorModel.Rgba, 1) => Rgb(0x28, 0xCD, 0x41),
        (ColorModel.Rgba, 2) => Rgb(0x00, 0x7A, 0xFF),
        (ColorModel.Cmyk, 0) => Rgb(0x00, 0xC0, 0xE8),
        (ColorModel.Cmyk, 1) => Rgb(0xFF, 0x2D, 0x55),
        (ColorModel.Cmyk, 2) => Rgb(0xFF, 0xCC, 0x00),
        (ColorModel.Hsl, 0) => Rgb(0xAF, 0x52, 0xDE),
        (ColorModel.Hsl, 1) => Rgb(0xFF, 0x95, 0x00),
        _ => SystemColors.ControlTextBrush,
    };

    private static SolidColorBrush Rgb(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}

/// <summary>Zone 3: amplitude spectrum of the zone-2 profile.</summary>
public sealed class SpectrumChart(Session session, ImageVariant variant) : ChartView
{
    public ImageVariant Variant { get; set; } = variant;

    protected override ChartData? GetData()
    {
        var profile = session.Profile(Variant);
        var spectrum = session.Spectrum(profile);
        if (spectrum.FftSize == 0) return null;
        var (freqs, mags) = session.SpectrumPoints(spectrum);
        double nyquist = freqs[^1];
        double top = mags.Length > 0 ? mags.Max() : 1;
        var (yMin, yMax) = session.LogMagnitude
            ? (Session.DbFloor, Math.Max(0, Math.Ceiling(top / 10) * 10))
            : (0.0, Math.Max(top * 1.05, 1e-6));
        return new ChartData(
            freqs, mags, 0, Math.Max(nyquist, 1e-9), yMin, yMax,
            $"Frequency ({session.FrequencyAxis.DisplayName()}), FFT size {spectrum.FftSize}",
            session.LogMagnitude ? "Amplitude (dB)" : "Amplitude",
            SystemColors.HighlightBrush, nyquist, "Nyquist");
    }
}
