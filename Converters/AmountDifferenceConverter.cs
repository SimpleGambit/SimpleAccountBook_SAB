using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SimpleAccountBook.Converters
{

    public class AmountDifferenceValueConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values is null || values.Length < 2)
            {
                return Binding.DoNothing;
            }

            if (!AmountDifferenceConversionHelper.TryGetDecimal(values[0], out var transactionAmount) ||
                !AmountDifferenceConversionHelper.TryGetDecimal(values[1], out var memoTotal))
            {
                return Binding.DoNothing;
            }

            return transactionAmount - memoTotal;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class AmountDifferenceVisibilityConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values is null || values.Length < 3)
            {
                return Visibility.Collapsed;
            }

            if (!AmountDifferenceConversionHelper.TryGetDecimal(values[0], out var transactionAmount) ||
                !AmountDifferenceConversionHelper.TryGetDecimal(values[1], out var memoTotal))
            {
                return Visibility.Collapsed;
            }

            if (!AmountDifferenceConversionHelper.TryGetInt(values[2], out var detailCount) || detailCount <= 0)
            {
                return Visibility.Collapsed;
            }

            var difference = transactionAmount - memoTotal;
            return difference == 0m ? Visibility.Collapsed : Visibility.Visible;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    internal static class AmountDifferenceConversionHelper
    {
        public static bool TryGetDecimal(object value, out decimal result)
        {
            switch (value)
            {
                case null:
                    result = 0m;
                    return false;
                case decimal decimalValue:
                    result = decimalValue;
                    return true;
                case double doubleValue:
                    result = (decimal)doubleValue;
                    return true;
                case float floatValue:
                    result = (decimal)floatValue;
                    return true;
                case int intValue:
                    result = intValue;
                    return true;
                case long longValue:
                    result = longValue;
                    return true;
                case string stringValue when decimal.TryParse(stringValue, NumberStyles.Number, CultureInfo.CurrentCulture, out var parsed):
                    result = parsed;
                    return true;
                default:
                    try
                    {
                        result = System.Convert.ToDecimal(value, CultureInfo.CurrentCulture);
                        return true;
                    }
                    catch (Exception)
                    {
                        result = 0m;
                        return false;
                    }
            }
        }
        public static bool TryGetInt(object value, out int result)
        {
            switch (value)
            {
                case null:
                    result = 0;
                    return false;
                case int intValue:
                    result = intValue;
                    return true;
                case long longValue:
                    result = (int)longValue;
                    return true;
                case double doubleValue:
                    result = (int)doubleValue;
                    return true;
                case float floatValue:
                    result = (int)floatValue;
                    return true;
                case string stringValue when int.TryParse(stringValue, NumberStyles.Integer, CultureInfo.CurrentCulture, out var parsed):
                    result = parsed;
                    return true;
                default:
                    try
                    {
                        result = System.Convert.ToInt32(value, CultureInfo.CurrentCulture);
                        return true;
                    }
                    catch (Exception)
                    {
                        result = 0;
                        return false;
                    }
            }
        }
    }
}