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

        var background = new SolidColorBrush(Color.FromRgb(250, 250, 250));
        drawingContext.DrawRoundedRectangle(background, null, new Rect(0, 0, width, height), 8, 8);

        var center = new Point(width / 2.0, height / 2.0);
        var radius = Math.Max(24, Math.Min(width, height) * 0.38);
        var gridPen = new Pen(new SolidColorBrush(Color.FromRgb(205, 212, 222)), 0.85);
        var minorGridPen = new Pen(new SolidColorBrush(Color.FromRgb(220, 226, 234)), 0.8) { DashStyle = DashStyles.Dash };
        var axisPen = new Pen(new SolidColorBrush(Color.FromRgb(17, 24, 39)), 1.0);

        DrawPolarGrid(drawingContext, center, radius, gridPen, minorGridPen, axisPen);
        DrawAngleLabels(drawingContext, center, radius);

        var channels = GetChannels().Where(c => c.IsEnabled && c.Magnitude > 0).ToArray();
        var currentMax = Math.Max(0.001, channels.Where(c => c.Kind == "I").Select(c => c.Magnitude).DefaultIfEmpty(0).Max());
        var voltageMax = Math.Max(0.001, channels.Where(c => c.Kind == "V").Select(c => c.Magnitude).DefaultIfEmpty(0).Max());

        foreach (var channel in channels)
        {
            var scale = channel.Kind == "V" ? voltageMax : currentMax;
            var length = radius * Math.Clamp(channel.Magnitude / scale, 0.0, 1.0);
            var angle = -channel.AngleDegrees * Math.PI / 180.0;
            var end = new Point(center.X + Math.Cos(angle) * length, center.Y + Math.Sin(angle) * length);
            var color = ResolveColor(channel.Key, channel.Kind);
            var pen = new Pen(new SolidColorBrush(color), channel.Kind == "V" ? 2.1 : 2.3)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round
            };

            drawingContext.DrawLine(pen, center, end);
            DrawArrowHead(drawingContext, center, end, color);
            DrawLabel(drawingContext, channel.Name, new Point(end.X + 6, end.Y - 9), 12, color);
        }

        DrawLabel(drawingContext, voltageMax.ToString("0.000", CultureInfo.InvariantCulture) + " V", new Point(12, height - 26), 11, Color.FromRgb(37, 99, 235));
        DrawLabel(drawingContext, currentMax.ToString("0.000", CultureInfo.InvariantCulture) + " A", new Point(width - 58, height - 26), 11, Color.FromRgb(220, 38, 38));
    }

    private static void DrawPolarGrid(DrawingContext drawingContext, Point center, double radius, Pen gridPen, Pen minorGridPen, Pen axisPen)
    {
        for (var ring = 1; ring <= 4; ring++)
        {
            var ringRadius = radius * ring / 4.0;
            drawingContext.DrawEllipse(null, gridPen, center, ringRadius, ringRadius);
        }

        for (var degree = 0; degree < 360; degree += 30)
        {
            var radians = -degree * Math.PI / 180.0;
            var end = new Point(center.X + Math.Cos(radians) * (radius + 10), center.Y + Math.Sin(radians) * (radius + 10));
            var pen = degree % 90 == 0 ? axisPen : minorGridPen;
            drawingContext.DrawLine(pen, center, end);
        }
    }

    private void DrawAngleLabels(DrawingContext drawingContext, Point center, double radius)
    {
        var textColor = Color.FromRgb(15, 23, 42);
        DrawCenteredLabel(drawingContext, "0°", new Point(center.X + radius + 18, center.Y - 6), 11, textColor);
        DrawCenteredLabel(drawingContext, "+90°", new Point(center.X, center.Y - radius - 18), 11, textColor);
        DrawCenteredLabel(drawingContext, "-90°", new Point(center.X, center.Y + radius + 8), 11, textColor);
        DrawCenteredLabel(drawingContext, "180°", new Point(center.X - radius - 22, center.Y - 6), 11, textColor);
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
        var formatted = CreateFormattedText(text, size, color);
        drawingContext.DrawText(formatted, origin);
    }

    private void DrawCenteredLabel(DrawingContext drawingContext, string text, Point center, double size, Color color)
    {
        var formatted = CreateFormattedText(text, size, color);
        drawingContext.DrawText(formatted, new Point(center.X - (formatted.Width / 2.0), center.Y - (formatted.Height / 2.0)));
    }

    private FormattedText CreateFormattedText(string text, double size, Color color)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        return new FormattedText(
            text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            size,
            new SolidColorBrush(color),
            dpi.PixelsPerDip);
    }

    private static Color ResolveColor(string key, string kind)
    {
        // Phase color convention used by many IEC / substation drawings:
        // R/A/L1 = red, S/B/L2 = yellow, T/C/L3 = blue, neutral/residual = gray.
        // Voltage and current intentionally share the same phase color so the operator
        // sees phase identity consistently across phasor and waveform views.
        return ResolvePhaseColor(key);
    }

    private static Color ResolvePhaseColor(string key)
        => key switch
        {
            "Va" or "Vab" or "V1" or "Ia" or "I1" => Color.FromRgb(220, 38, 38),
            "Vb" or "Vbc" or "V2" or "Ib" or "I2" => Color.FromRgb(217, 119, 6),
            "Vc" or "Vca" or "Ic" => Color.FromRgb(37, 99, 235),
            "V0" or "I0" or "Vn" or "In" => Color.FromRgb(71, 85, 105),
            _ => Color.FromRgb(51, 65, 85)
        };
}