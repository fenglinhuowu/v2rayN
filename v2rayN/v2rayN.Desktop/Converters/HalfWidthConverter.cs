using Avalonia.Data.Converters;

namespace v2rayN.Desktop.Converters;

public class HalfWidthConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not double width || width <= 0)
        {
            return double.NaN;
        }

        var spacing = parameter is double gap ? gap : 16d;
        return Math.Max(0, (width - spacing) / 2);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return null;
    }
}
