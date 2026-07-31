using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace AR.Iec61850.VirtualRelayLab.Controls;

public sealed class WaveformScope : FrameworkElement
{
    public static readonly DependencyProperty PhaseAProperty = DependencyProperty.Register(
        nameof(PhaseA), typeof(double), typeof(WaveformScope),
        new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty PhaseBProperty = DependencyProperty.Register(
        nameof(PhaseB), typeof(double), typeof(WaveformScope),
        new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty PhaseCProperty = DependencyProperty.Register(
        nameof(PhaseC), typeof(double), typeof(WaveformScope),
        new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ResidualProperty = DependencyProperty.Register(
        nameof(Residual), typeof(double), typeof(WaveformScope),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FrequencyProperty = DependencyProperty.Register(
        nameof(Frequency), typeof(double), typeof(WaveformScope),
        new FrameworkPropertyMetadata(50.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty PickupPositionProperty = DependencyProperty.Register(
        nameof(PickupPosition), typeof(double), typeof(WaveformScope),
        new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TripPositionProperty = DependencyProperty.Register(
        nameof(TripPosition), typeof(double), typeof(WaveformScope),
        new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender));

    public double PhaseA
    {
        get => (double)GetValue(PhaseAProperty);
        set => SetValue(PhaseAProperty, value);
    }

    public double PhaseB
    {
        get => (double)GetValue(PhaseBProperty);
        set => SetValue(PhaseBProperty, value);
    }

    public double PhaseC
    {
        get => (double)GetValue(PhaseCProperty);
        set => SetValue(PhaseCProperty, value);
    }

    public double Residual
    {
        get => (double)GetValue(ResidualProperty);
        set => SetValue(ResidualProperty, value);
    }

    public double Frequency
    {
        get => (double)GetValue(FrequencyProperty);
        set => SetValue(FrequencyProperty, value);
    }

    public double PickupPosition
    {
        get => (double)GetValue(PickupPositionProperty);
        set => SetValue(PickupPositionProperty, value);
    }

    public double TripPosition
    {
        get => (double)GetValue(TripPositionProperty);
        set => SetValue(TripPositionProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        var width = Math.Max(1, ActualWidth);
        var height = Math.Max(1, ActualHeight);
        var plot = new Rect(52, 16, Math.Max(1, width - 70), Math.Max(1, height - 46));

        drawingContext.DrawRoundedRectangle(
            new SolidColorBrush(Color.FromRgb(15, 24, 32)),
            new Pen(new SolidColorBrush(Color.FromRgb(52, 68, 80)), 1),
            new Rect(0.5, 0.5, width - 1, height - 1),
            5,
            5);

        DrawGrid(drawingContext, plot);
        DrawMarker(drawingContext, plot, PickupPosition, Color.FromRgb(205, 151, 52), "PICKUP");
        DrawMarker(drawingContext, plot, TripPosition, Color.FromRgb(215, 78, 72), "TRIP");

        var maxCurrent = Math.Max(1.2, Math.Max(Math.Max(PhaseA, PhaseB), Math.Max(PhaseC, Residual)) * 1.18);
        DrawTrace(drawingContext, plot, PhaseA, 0.0, maxCurrent, Color.FromRgb(86, 192, 229));
        DrawTrace(drawingContext, plot, PhaseB, -2.0 * Math.PI / 3.0, maxCurrent, Color.FromRgb(245, 188, 81));
        DrawTrace(drawingContext, plot, PhaseC, 2.0 * Math.PI / 3.0, maxCurrent, Color.FromRgb(194, 124, 222));
        DrawTrace(drawingContext, plot, Residual, 0.0, maxCurrent, Color.FromRgb(108, 202, 137), 1.15);

        DrawScale(drawingContext, plot, maxCurrent);
        DrawLegend(drawingContext, plot);
    }

    private static void DrawGrid(DrawingContext dc, Rect plot)
    {
        var minorPen = new Pen(new SolidColorBrush(Color.FromRgb(31, 45, 56)), 1);
        var majorPen = new Pen(new SolidColorBrush(Color.FromRgb(45, 62, 74)), 1);
        var zeroPen = new Pen(new SolidColorBrush(Color.FromRgb(81, 101, 115)), 1);

        for (var column = 0; column <= 16; column++)
        {
            var x = plot.Left + plot.Width * column / 16.0;
            dc.DrawLine(column % 4 == 0 ? majorPen : minorPen, new Point(x, plot.Top), new Point(x, plot.Bottom));
        }

        for (var row = 0; row <= 8; row++)
        {
            var y = plot.Top + plot.Height * row / 8.0;
            var pen = row == 4 ? zeroPen : row % 2 == 0 ? majorPen : minorPen;
            dc.DrawLine(pen, new Point(plot.Left, y), new Point(plot.Right, y));
        }
    }

    private static void DrawTrace(
        DrawingContext dc,
        Rect plot,
        double amplitude,
        double phase,
        double maxCurrent,
        Color color,
        double thickness = 1.55)
    {
        if (amplitude <= 0.0001)
            return;

        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            const int pointCount = 640;
            for (var index = 0; index < pointCount; index++)
            {
                var normalized = index / (double)(pointCount - 1);
                var x = plot.Left + normalized * plot.Width;
                var angle = normalized * Math.PI * 4.0 + phase;
                var normalizedAmplitude = Math.Clamp(amplitude / maxCurrent, 0, 1);
                var y = plot.Top + plot.Height / 2.0 - Math.Sin(angle) * normalizedAmplitude * plot.Height * 0.42;
                var point = new Point(x, y);
                if (index == 0)
                    context.BeginFigure(point, false, false);
                else
                    context.LineTo(point, true, false);
            }
        }

        geometry.Freeze();
        dc.DrawGeometry(null, new Pen(new SolidColorBrush(color), thickness), geometry);
    }

    private static void DrawMarker(DrawingContext dc, Rect plot, double normalizedPosition, Color color, string label)
    {
        if (double.IsNaN(normalizedPosition) || normalizedPosition < 0 || normalizedPosition > 1)
            return;

        var x = plot.Left + plot.Width * normalizedPosition;
        var brush = new SolidColorBrush(color);
        var pen = new Pen(brush, 1) { DashStyle = DashStyles.Dash };
        dc.DrawLine(pen, new Point(x, plot.Top), new Point(x, plot.Bottom));
        dc.DrawText(CreateText(label, 9, brush), new Point(Math.Min(x + 4, plot.Right - 46), plot.Top + 3));
    }

    private static void DrawScale(DrawingContext dc, Rect plot, double maxCurrent)
    {
        var muted = new SolidColorBrush(Color.FromRgb(143, 160, 173));
        dc.DrawText(CreateText($"+{maxCurrent:0.0} A", 9.5, muted), new Point(4, plot.Top - 3));
        dc.DrawText(CreateText("0", 9.5, muted), new Point(28, plot.Top + plot.Height / 2.0 - 7));
        dc.DrawText(CreateText($"-{maxCurrent:0.0} A", 9.5, muted), new Point(4, plot.Bottom - 12));
        dc.DrawText(CreateText("0 ms", 9.5, muted), new Point(plot.Left, plot.Bottom + 7));
        dc.DrawText(CreateText("1 cycle", 9.5, muted), new Point(plot.Left + plot.Width / 2.0 - 21, plot.Bottom + 7));
        dc.DrawText(CreateText("2 cycles", 9.5, muted), new Point(plot.Right - 42, plot.Bottom + 7));
    }

    private static void DrawLegend(DrawingContext dc, Rect plot)
    {
        var entries = new[]
        {
            ("IA", Color.FromRgb(86, 192, 229)),
            ("IB", Color.FromRgb(245, 188, 81)),
            ("IC", Color.FromRgb(194, 124, 222)),
            ("3I0", Color.FromRgb(108, 202, 137))
        };

        var x = plot.Right - 174;
        foreach (var entry in entries)
        {
            var brush = new SolidColorBrush(entry.Item2);
            dc.DrawLine(new Pen(brush, 2), new Point(x, plot.Top + 10), new Point(x + 15, plot.Top + 10));
            dc.DrawText(CreateText(entry.Item1, 9.5, brush), new Point(x + 20, plot.Top + 3));
            x += 43;
        }
    }

    private static FormattedText CreateText(string text, double size, Brush brush)
    {
        return new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Cascadia Mono"),
            size,
            brush,
            1.0);
    }
}
