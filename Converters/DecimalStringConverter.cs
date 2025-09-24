using System;
using System.Globalization;
using System.Windows.Data;

namespace SimpleAccountBook.Converters
{
    /// <summary>
    /// Converts decimal values to formatted strings and back while treating empty text as zero.
    /// </summary>
    public class DecimalStringConverter : IValueConverter
    {
        private static readonly NumberStyles AllowedStyles = NumberStyles.Number | NumberStyles.AllowCurrencySymbol;

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return value switch
            {
                null => string.Empty,
                decimal decimalValue => FormatDecimal(decimalValue, culture),
                double doubleValue => FormatDecimal((decimal)doubleValue, culture),
                float floatValue => FormatDecimal((decimal)floatValue, culture),
                IFormattable formattable => formattable.ToString("#,0", culture),
                _ => value.ToString()
            };
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is null)
            {
                return 0m;
            }

            if (value is string text)
            {
                if (string.IsNullOrWhiteSpace(text))
                {
                    return 0m;
                }

                if (decimal.TryParse(RemoveNumberGroupSeparators(text, culture), AllowedStyles, culture, out var result))
                {
                    return result;
                }

                return Binding.DoNothing;
            }

            return Binding.DoNothing;
        }

        private static string RemoveNumberGroupSeparators(string value, CultureInfo culture)
        {
            var separator = culture.NumberFormat.NumberGroupSeparator;
            return string.IsNullOrEmpty(separator) ? value : value.Replace(separator, string.Empty);
        }

        private static string FormatDecimal(decimal value, CultureInfo culture)
        {
            return value.ToString("#,0", culture);
        }
    }
}