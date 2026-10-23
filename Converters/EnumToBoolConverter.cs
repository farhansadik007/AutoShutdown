using System;
using System.Globalization;
using System.Windows.Data;

namespace AutoShutdown.Converters;

/// <summary>
/// Binds a group of RadioButtons to one enum property.
/// Unchecking returns Binding.DoNothing so the newly checked sibling wins.
/// </summary>
public sealed class EnumToBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is not null
           && parameter is not null
           && string.Equals(value.ToString(), parameter.ToString(), StringComparison.OrdinalIgnoreCase);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not true || parameter is null)
        {
            return Binding.DoNothing;
        }

        try
        {
            return Enum.Parse(targetType, parameter.ToString()!, ignoreCase: true);
        }
        catch
        {
            return Binding.DoNothing;
        }
    }
}
