using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using AR.Iec61850.SvPublisher.ViewModels;

namespace AR.Iec61850.SvPublisher.Controls;

public sealed class WaveformPlot : FrameworkElement
{
    public static readonly DependencyProperty ChannelsProperty =
        DependencyProperty.Register(
            nameof(Channels),
            typeof(IEnumerable),
            typeof(WaveformPlot),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnChannelsChanged));

    public IEnumerable? Channels
    {
        get => (IEnumerable?)GetValue(ChannelsProperty);
        set => SetValue(ChannelsProperty, value);
    }

    private static void OnChannelsChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        var plot = (WaveformPlot)dependencyObject;
        plot.Detach(e.OldValue as IEnumerable);
        plot.Attach(e.NewValue as IEnumerable);
        plot.InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 1 || height <= 1)
            return;

        drawingContext.DrawRoundedRectangle(
            new SolidColorBrush(Color.FromRgb(248, 250, 252)),
            null,
            new Rect(0, 0, width, height),
            8,
            8);

        var plotRect = new Rect(42, 22, Math.Max(40, width - 62), Math.Max(40, height - 54));
        var halfHeight = plotRect.Height / 2.0;
        var voltageLane = new Rect(plotRect.X, plotRect.Y, plotRect.Width, halfHeight - 10);
        var currentLane = new Rect(plotRect.X, plotRect.Y + halfHeight + 10, plotRect.Width, halfHeight - 10);

        DrawLane(drawingContext, voltageLane, "Voltage", "V", new[] { "Va", "Vb", "Vc" });
        DrawLane(drawingContext, currentLane, "Current", "A", new[] { "Ia", "Ib", "Ic" });
    }

    private void DrawLane(DrawingContext drawingContext, Rect lane, string title, string unit, IReadOnlyList<string> keys)
    {
        var gridPen = new Pen(new SolidColorBrush(Color.FromRgb(226, 232, 240)), 1);
        var axisPen = new Pen(new SolidColorBrush(Color.FromRgb(148, 163, 184)), 1);
        var channels = GetChannels().Where(c => c.IsEnabled && keys.Contains(c.Key)).ToArray();
        var maxMagnitude = Math.Max(0.001, channels.Select(c => c.Magnitude).DefaultIfEmpty(0).Max());
        var midY = lane.Y + lane.Height / 2.0;
        var amp = Math.Max(4, lane.Height * 0.38);

        drawingContext.DrawRectangle(null, gridPen, lane);
        drawingContext.DrawLine(axisPen, new Point(lane.X, midY), new Point(lane.Right, midY));

        for (var i = 1; i < 4; i++)
        {
            var x = lane.X + lane.Width * i / 4.0;
            drawingContext.DrawLine(gridPen, new Point(x, lane.Y), new Point(x, lane.Bottom));
        }

        DrawText(drawingContext, title, new Point(10, lane.Y + 4), 12, Color.FromRgb(51, 65, 85));
        DrawText(drawingContext, unit, new Point(14, lane.Bottom - 20), 11, Color.FromRgb(100, 116, 139));

        foreach (var channel in channels)
        {
            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                for (var i = 0; i <= 240; i++)
                {
                    var t = i / 240.0;
                    var angle = (4.0 * Math.PI * t) + (channel.AngleDegrees * Math.PI / 180.0);
                    var normalized = channel.Magnitude / maxMagnitude;
                    var x = lane.X + lane.Width * t;
                    var y = midY - Math.Sin(angle) * amp * normalized;
                    var point = new Point(x, y);

                    if (i == 0)
                        context.BeginFigure(point, false, false);
                    else
                        context.LineTo(point, true, false);
                }
            }

            geometry.Freeze();
            drawingContext.DrawGeometry(null, new Pen(new SolidColorBrush(ResolveColor(channel.Key)), 2), geometry);
        }

        var legendX = lane.Right - 128;
        for (var i = 0; i < channels.Length; i++)
        {
            var channel = channels[i];
            var y = lane.Y + 8 + (i * 18);
            var color = ResolveColor(channel.Key);
            drawingContext.DrawLine(new Pen(new SolidColorBrush(color), 2), new Point(legendX, y + 7), new Point(legendX + 18, y + 7));
            DrawText(drawingContext, $"{channel.Name} {channel.Magnitude:0.###}", new Point(legendX + 24, y), 11, color);
        }
    }

    private IEnumerable<SignalChannelViewModel> GetChannels()
    {
        if (Channels is null)
            yield break;

        foreach (var item in Channels)
        {
            if (item is SignalChannelViewModel channel)
                yield return channel;
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

    private void DrawText(DrawingContext drawingContext, string text, Point origin, double size, Color color)
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

        drawingContext.DrawText(formatted, origin);
    }

    private static Color ResolveColor(string key)
        => key switch
        {
            "Ia" => Color.FromRgb(37, 99, 235),
            "Ib" => Color.FromRgb(14, 165, 233),
            "Ic" => Color.FromRgb(79, 70, 229),
            "Va" => Color.FromRgb(217, 119, 6),
            "Vb" => Color.FromRgb(22, 163, 74),
            "Vc" => Color.FromRgb(220, 38, 38),
            _ => Color.FromRgb(71, 85, 105)
        };
}
