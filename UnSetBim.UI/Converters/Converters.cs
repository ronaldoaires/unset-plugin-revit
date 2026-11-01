using System.Collections;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace UnSetBim.UI.Converters;

/// <summary>true → Visible. ConverterParameter="Invert" flips it.</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var flag = value is true;
        if (parameter is string p && p.Equals("Invert", StringComparison.OrdinalIgnoreCase))
            flag = !flag;
        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Visibility.Visible;
}

/// <summary>Null, empty string or empty collection → Collapsed. ConverterParameter="Invert" flips it.</summary>
public sealed class HasValueToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var has = value switch
        {
            null => false,
            string s => !string.IsNullOrWhiteSpace(s),
            ICollection c => c.Count > 0,
            _ => true,
        };

        if (parameter is string p && p.Equals("Invert", StringComparison.OrdinalIgnoreCase))
            has = !has;

        return has ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;
}

/// <summary>"Maria Souza" → "M". Used by the avatar circle.</summary>
public sealed class InitialConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string name && !string.IsNullOrWhiteSpace(name)
            ? name.Trim()[0].ToString().ToUpper(culture)
            : "?";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Available width → number of grid columns, so cards fill the whole row (UniformGrid.Columns).
/// ConverterParameter = minimum card width in pixels (default 220). Includes the 14 px gap between cards.
/// </summary>
public sealed class WidthToColumnsConverter : IValueConverter
{
    private const double Gap = 14;

    // Room left for the overlay scrollbar.
    private const double ScrollbarReserve = 8;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var minWidth = parameter is string p && double.TryParse(p, NumberStyles.Float, CultureInfo.InvariantCulture, out var m) ? m : 220;

        if (value is not double width || double.IsNaN(width) || width <= 0)
            return 1;

        return Math.Max(1, (int)Math.Floor((width - ScrollbarReserve) / (minWidth + Gap)));
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
