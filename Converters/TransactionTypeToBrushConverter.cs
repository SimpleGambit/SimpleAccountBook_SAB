using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace SimpleAccountBook.Converters;

public class TransactionTypeToBrushConverter : IValueConverter
{
    public Brush IncomeBrush { get; set; } = Brushes.DarkGreen;
    public Brush ExpenseBrush { get; set; } = Brushes.Firebrick;
    public Brush DefaultBrush { get; set; } = Brushes.Black;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string transactionType)
        {
            if (transactionType.Equals("입금", StringComparison.OrdinalIgnoreCase))
            {
                return IncomeBrush;
            }

            if (transactionType.Equals("출금", StringComparison.OrdinalIgnoreCase))
            {
                return ExpenseBrush;
            }
        }

        return DefaultBrush;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}