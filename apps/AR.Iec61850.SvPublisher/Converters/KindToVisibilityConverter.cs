
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace AR.Iec61850.SvPublisher.Converters;

public sealed class KindToVisibilityConverter : DependencyObject, IValueConverter
{
    public static readonly DependencyProperty MatchKindProperty =
        DependencyProperty.Register(nameof(MatchKind), typeof(string), typeof(KindToVisibilityConverter), new PropertyMetadata(string.Empty));

    public string MatchKind
    {
        get => (string)GetValue(MatchKindProperty);
        set => SetValue(MatchKindProperty, value);
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => string.Equals(value as string, MatchKind, StringComparison.OrdinalIgnoreCase) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => global::System.Windows.Data.Binding.DoNothing;
}
