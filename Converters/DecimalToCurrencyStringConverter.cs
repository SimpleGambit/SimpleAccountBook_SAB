using System;
using System.Globalization;
using System.Windows.Data;

namespace SimpleAccountBook.Converters;

public class DecimalToCurrencyStringConverter : IValueConverter
{
    public string Format { get; set; } = "#,0";

    public string? Suffix { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null)
        {
            return string.Empty;
        }

        if (value is IFormattable formattable)
        {
            var cultureToUse = culture ?? CultureInfo.CurrentCulture;
            var formatted = formattable.ToString(Format, cultureToUse);
            var suffix = parameter as string ?? Suffix;
            return string.IsNullOrEmpty(suffix) ? formatted : formatted + suffix;
        }

        return value.ToString() ?? string.Empty;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}