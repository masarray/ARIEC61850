
using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace AR.Iec61850.SvPublisher.Converters;

public sealed class SignalBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var key = value as string ?? string.Empty;
        return key switch
        {
            "Va" or "Vab" or "V1" or "Ia" or "I1" => new SolidColorBrush(Color.FromRgb(220, 38, 38)),
            "Vb" or "Vbc" or "V2" or "Ib" or "I2" => new SolidColorBrush(Color.FromRgb(217, 119, 6)),
            "Vc" or "Vca" or "V3" or "Ic" or "I3" => new SolidColorBrush(Color.FromRgb(37, 99, 235)),
            _ => new SolidColorBrush(Color.FromRgb(71, 85, 105))
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => global::System.Windows.Data.Binding.DoNothing;
}
