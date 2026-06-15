using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using AR.Iec61850.SvPublisher.ViewModels;

namespace AR.Iec61850.SvPublisher.Controls;

public sealed class RampTimelinePlot : FrameworkElement
{
    public static readonly DependencyProperty RampStatesProperty =
        DependencyProperty.Register(
            nameof(RampStates),
            typeof(IEnumerable),
            typeof(RampTimelinePlot),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnRampStatesChanged));

    public IEnumerable? RampStates
    {
        get => (IEnumerable?)GetValue(RampStatesProperty);
        set => SetValue(RampStatesProperty, value);
    }

    private static void OnRampStatesChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        var plot = (RampTimelinePlot)dependencyObject;
        plot.Detach(e.OldValue as IEnumerable);
        plot.Attach(e.NewValue as IEnumerable);
        plot.InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var width = ActualWidth;
        var height = ActualHeight;
        if (width < 80 || height < 80)
            return;

        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(250, 250, 250)), null, new Rect(0, 0, width, height), 6, 6);

        var plot = new Rect(58, 28, Math.Max(10, width - 78), Math.Max(10, height - 58));
        var axisPen = new Pen(new SolidColorBrush(Color.FromRgb(30, 41, 59)), 1);
        var gridPen = new Pen(new SolidColorBrush(Color.FromRgb(226, 232, 240)), 1);
        var rampPen = new Pen(new SolidColorBrush(Color.FromRgb(220, 38, 38)), 1.8);

        for (var i = 0; i <= 5; i++)
        {
            var x = plot.Left + (plot.Width * i / 5.0);
            dc.DrawLine(gridPen, new Point(x, plot.Top), new Point(x, plot.Bottom));
            var y = plot.Top + (plot.Height * i / 5.0);
            dc.DrawLine(gridPen, new Point(plot.Left, y), new Point(plot.Right, y));
        }

        dc.DrawLine(axisPen, new Point(plot.Left, plot.Bottom), new Point(plot.Right, plot.Bottom));
        dc.DrawLine(axisPen, new Point(plot.Left, plot.Top), new Point(plot.Left, plot.Bottom));

        var states = GetRampStates().ToArray();
        if (states.Length == 0)
        {
            DrawText(dc, "No ramp states", new Point(plot.Left + 12, plot.Top + 16), 12, Color.FromRgb(100, 116, 139));
            return;
        }

        var min = states.SelectMany(s => new[] { s.From, s.To }).Min();
        var max = states.SelectMany(s => new[] { s.From, s.To }).Max();
        if (Math.Abs(max - min) < 0.000001)
        {
            max += 1;
            min -= 1;
        }

        var total = states.Sum(s => Math.Max(0.001, s.TimeSeconds));
        var cursor = 0.0;
        Point? last = null;
        foreach (var state in states)
        {
            var startX = plot.Left + (cursor / total * plot.Width);
            var endCursor = cursor + Math.Max(0.001, state.TimeSeconds);
            var endX = plot.Left + (endCursor / total * plot.Width);
            var y1 = MapY(state.From, min, max, plot);
            var y2 = MapY(state.To, min, max, plot);
            var p1 = new Point(startX, y1);
            var p2 = new Point(endX, y2);

            if (last is not null)
                dc.DrawLine(rampPen, last.Value, p1);
            dc.DrawLine(rampPen, p1, p2);
            dc.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(148, 163, 184)), 0.8), new Point(startX, plot.Top), new Point(startX, plot.Bottom));
            DrawText(dc, state.Name, new Point(startX + 4, plot.Top - 22), 11, Color.FromRgb(30, 41, 59));
            last = p2;
            cursor = endCursor;
        }

        DrawText(dc, max.ToString("0.000", CultureInfo.InvariantCulture), new Point(10, plot.Top - 6), 11, Color.FromRgb(30, 41, 59));
        DrawText(dc, min.ToString("0.000", CultureInfo.InvariantCulture), new Point(10, plot.Bottom - 14), 11, Color.FromRgb(30, 41, 59));
        DrawText(dc, total.ToString("0.000", CultureInfo.InvariantCulture) + " s", new Point(plot.Right - 48, plot.Bottom + 8), 11, Color.FromRgb(30, 41, 59));
    }

    private static double MapY(double value, double min, double max, Rect plot)
        => plot.Bottom - ((value - min) / (max - min) * plot.Height);

    private IEnumerable<RampStateViewModel> GetRampStates()
    {
        if (RampStates is null)
            yield break;

        foreach (var item in RampStates)
        {
            if (item is RampStateViewModel state)
                yield return state;
        }
    }

    private void Attach(IEnumerable? enumerable)
    {
        if (enumerable is INotifyCollectionChanged collection)
            collection.CollectionChanged += OnCollectionChanged;

        foreach (var item in enumerable ?? Array.Empty<object>())
        {
            if (item is INotifyPropertyChanged propertyChanged)
                propertyChanged.PropertyChanged += OnItemChanged;
        }
    }

    private void Detach(IEnumerable? enumerable)
    {
        if (enumerable is INotifyCollectionChanged collection)
            collection.CollectionChanged -= OnCollectionChanged;

        foreach (var item in enumerable ?? Array.Empty<object>())
        {
            if (item is INotifyPropertyChanged propertyChanged)
                propertyChanged.PropertyChanged -= OnItemChanged;
        }
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (var item in e.OldItems)
            {
                if (item is INotifyPropertyChanged propertyChanged)
                    propertyChanged.PropertyChanged -= OnItemChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (var item in e.NewItems)
            {
                if (item is INotifyPropertyChanged propertyChanged)
                    propertyChanged.PropertyChanged += OnItemChanged;
            }
        }

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
