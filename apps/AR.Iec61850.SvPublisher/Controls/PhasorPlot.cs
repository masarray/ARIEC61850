using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using AR.Iec61850.SvPublisher.ViewModels;

namespace AR.Iec61850.SvPublisher.Controls;

public sealed class PhasorPlot : FrameworkElement
{
    public static readonly DependencyProperty ChannelsProperty =
        DependencyProperty.Register(
            nameof(Channels),
            typeof(IEnumerable),
            typeof(PhasorPlot),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnChannelsChanged));

    public IEnumerable? Channels
    {
        get => (IEnumerable?)GetValue(ChannelsProperty);
        set => SetValue(ChannelsProperty, value);
    }

    private static void OnChannelsChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        var plot = (PhasorPlot)dependencyObject;
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

        var background = new SolidColorBrush(Color.FromRgb(248, 250, 252));
        drawingContext.DrawRoundedRectangle(background, null, new Rect(0, 0, width, height), 8, 8);

        var center = new Point(width / 2.0, height / 2.0);
        var radius = Math.Max(24, Math.Min(width, height) * 0.38);
        var gridPen = new Pen(new SolidColorBrush(Color.FromRgb(215, 221, 230)), 1);
        var axisPen = new Pen(new SolidColorBrush(Color.FromRgb(148, 163, 184)), 1);

        drawingContext.DrawEllipse(null, gridPen, center, radius, radius);
        drawingContext.DrawEllipse(null, gridPen, center, radius * 0.5, radius * 0.5);
        drawingContext.DrawLine(axisPen, new Point(center.X - radius - 10, center.Y), new Point(center.X + radius + 10, center.Y));
        drawingContext.DrawLine(axisPen, new Point(center.X, center.Y - radius - 10), new Point(center.X, center.Y + radius + 10));

        DrawLabel(drawingContext, "0 deg", new Point(center.X + radius + 12, center.Y - 10), 11, Color.FromRgb(71, 85, 105));
        DrawLabel(drawingContext, "90", new Point(center.X + 8, center.Y - radius - 20), 11, Color.FromRgb(71, 85, 105));

        var channels = GetChannels().Where(c => c.IsEnabled && c.Magnitude > 0).ToArray();
        var currentMax = Math.Max(0.001, channels.Where(c => c.Kind == "I").Select(c => c.Magnitude).DefaultIfEmpty(0).Max());
        var voltageMax = Math.Max(0.001, channels.Where(c => c.Kind == "V").Select(c => c.Magnitude).DefaultIfEmpty(0).Max());

        foreach (var channel in channels)
        {
            var scale = channel.Kind == "V" ? voltageMax : currentMax;
            var length = radius * Math.Clamp(channel.Magnitude / scale, 0.0, 1.0);
            var angle = -channel.AngleDegrees * Math.PI / 180.0;
            var end = new Point(center.X + Math.Cos(angle) * length, center.Y + Math.Sin(angle) * length);
            var color = ResolveColor(channel.Key);
            var pen = new Pen(new SolidColorBrush(color), channel.Kind == "V" ? 2.4 : 2.1)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round
            };

            if (channel.Kind == "I")
                pen.DashStyle = DashStyles.Solid;

            drawingContext.DrawLine(pen, center, end);
            DrawArrowHead(drawingContext, center, end, color);
            DrawLabel(drawingContext, channel.Name, new Point(end.X + 6, end.Y - 8), 12, color);
        }

        DrawLabel(drawingContext, "Current and voltage are normalized separately", new Point(14, height - 26), 11, Color.FromRgb(71, 85, 105));
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

    private static void DrawArrowHead(DrawingContext drawingContext, Point start, Point end, Color color)
    {
        var vector = start - end;
        if (vector.Length < 1)
            return;

        vector.Normalize();
        var normal = new Vector(-vector.Y, vector.X);
        var p1 = end + (vector * 10) + (normal * 4);
        var p2 = end + (vector * 10) - (normal * 4);
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(end, true, true);
            context.LineTo(p1, true, false);
            context.LineTo(p2, true, false);
        }

        geometry.Freeze();
        drawingContext.DrawGeometry(new SolidColorBrush(color), null, geometry);
    }

    private void DrawLabel(DrawingContext drawingContext, string text, Point origin, double size, Color color)
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
            "In" => Color.FromRgb(100, 116, 139),
            "Va" => Color.FromRgb(217, 119, 6),
            "Vb" => Color.FromRgb(22, 163, 74),
            "Vc" => Color.FromRgb(220, 38, 38),
            "Vn" => Color.FromRgb(120, 113, 108),
            _ => Color.FromRgb(51, 65, 85)
        };
}
