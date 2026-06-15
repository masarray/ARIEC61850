using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using AR.Iec61850.SvPublisher.ViewModels;

namespace AR.Iec61850.SvPublisher.Controls;

public sealed class StateTimelinePlot : FrameworkElement
{
    public static readonly DependencyProperty SequenceStatesProperty =
        DependencyProperty.Register(
            nameof(SequenceStates),
            typeof(IEnumerable),
            typeof(StateTimelinePlot),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnSequenceStatesChanged));

    public IEnumerable? SequenceStates
    {
        get => (IEnumerable?)GetValue(SequenceStatesProperty);
        set => SetValue(SequenceStatesProperty, value);
    }

    private static void OnSequenceStatesChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        var plot = (StateTimelinePlot)dependencyObject;
        plot.Detach(e.OldValue as IEnumerable);
        plot.Attach(e.NewValue as IEnumerable);
        plot.InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var width = ActualWidth;
        var height = ActualHeight;
        if (width < 100 || height < 80)
            return;

        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(250, 250, 250)), null, new Rect(0, 0, width, height), 6, 6);
        var states = GetStates().ToArray();
        if (states.Length == 0)
        {
            DrawText(dc, "No states", new Point(16, 16), 12, Color.FromRgb(100, 116, 139));
            return;
        }

        var total = states.Sum(s => Math.Max(0.001, s.DurationSeconds));
        var plot = new Rect(66, 36, Math.Max(10, width - 86), Math.Max(10, height - 72));
        var gridPen = new Pen(new SolidColorBrush(Color.FromRgb(226, 232, 240)), 1);
        var axisPen = new Pen(new SolidColorBrush(Color.FromRgb(30, 41, 59)), 1);
        dc.DrawLine(axisPen, new Point(plot.Left, plot.Bottom), new Point(plot.Right, plot.Bottom));
        dc.DrawLine(axisPen, new Point(plot.Left, plot.Top), new Point(plot.Left, plot.Bottom));
        for (var i = 1; i <= 4; i++)
        {
            var y = plot.Top + plot.Height * i / 4.0;
            dc.DrawLine(gridPen, new Point(plot.Left, y), new Point(plot.Right, y));
        }

        var cursor = 0.0;
        var phasePens = new[]
        {
            new Pen(new SolidColorBrush(Color.FromRgb(220, 38, 38)), 1.1),
            new Pen(new SolidColorBrush(Color.FromRgb(217, 119, 6)), 1.1),
            new Pen(new SolidColorBrush(Color.FromRgb(37, 99, 235)), 1.1)
        };

        foreach (var state in states)
        {
            var startX = plot.Left + cursor / total * plot.Width;
            cursor += Math.Max(0.001, state.DurationSeconds);
            var endX = plot.Left + cursor / total * plot.Width;
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(26, 59, 130, 246)), null, new Rect(startX, plot.Top, Math.Max(1, endX - startX), plot.Height));
            dc.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(100, 116, 139)), 1), new Point(startX, plot.Top - 18), new Point(startX, plot.Bottom));
            DrawText(dc, state.Name, new Point(startX + 4, plot.Top - 30), 11, Color.FromRgb(30, 41, 59));

            for (var phase = 0; phase < 3; phase++)
                DrawSine(dc, startX, endX, plot, phasePens[phase], state.VoltageScale, phase * 120 + state.AngleShiftDegrees);
        }

        DrawText(dc, "V / I preview", new Point(10, plot.Top + 4), 11, Color.FromRgb(30, 41, 59));
        DrawText(dc, total.ToString("0.000", CultureInfo.InvariantCulture) + " s", new Point(plot.Right - 48, plot.Bottom + 10), 11, Color.FromRgb(30, 41, 59));
    }

    private static void DrawSine(DrawingContext dc, double startX, double endX, Rect plot, Pen pen, double scale, double phaseDegrees)
    {
        if (endX - startX < 2)
            return;

        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            var amplitude = Math.Clamp(scale, 0.05, 4.0) / 4.0 * plot.Height * 0.42;
            var centerY = plot.Top + plot.Height / 2.0;
            var phase = phaseDegrees * Math.PI / 180.0;
            for (var i = 0; i <= 120; i++)
            {
                var t = i / 120.0;
                var x = startX + (endX - startX) * t;
                var y = centerY - Math.Sin((t * Math.PI * 10) + phase) * amplitude;
                if (i == 0)
                    context.BeginFigure(new Point(x, y), false, false);
                else
                    context.LineTo(new Point(x, y), true, false);
            }
        }

        geometry.Freeze();
        dc.DrawGeometry(null, pen, geometry);
    }

    private IEnumerable<SequenceStateViewModel> GetStates()
    {
        if (SequenceStates is null)
            yield break;

        foreach (var item in SequenceStates)
        {
            if (item is SequenceStateViewModel state)
                yield return state;
        }
    }

    private void Attach(IEnumerable? enumerable)
    {
        if (enumerable is INotifyCollectionChanged collection)
            collection.CollectionChanged += OnCollectionChanged;
        foreach (var item in enumerable ?? Array.Empty<object>())
            if (item is INotifyPropertyChanged propertyChanged)
                propertyChanged.PropertyChanged += OnItemChanged;
    }

    private void Detach(IEnumerable? enumerable)
    {
        if (enumerable is INotifyCollectionChanged collection)
            collection.CollectionChanged -= OnCollectionChanged;
        foreach (var item in enumerable ?? Array.Empty<object>())
            if (item is INotifyPropertyChanged propertyChanged)
                propertyChanged.PropertyChanged -= OnItemChanged;
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        InvalidateVisual();
    }

    private void OnItemChanged(object? sender, PropertyChangedEventArgs e)
        => InvalidateVisual();

    private void DrawText(DrawingContext dc, string text, Point origin, double size, Color color)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            size,
            new SolidColorBrush(color),
            dpi.PixelsPerDip);
        dc.DrawText(formatted, origin);
    }
}
