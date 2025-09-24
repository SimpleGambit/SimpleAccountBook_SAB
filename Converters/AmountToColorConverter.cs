using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace SimpleAccountBook.Converters
{
    /// <summary>
    /// 금액의 부호에 따라 색상을 반환하는 Converter
    /// 음수: 빨간색, 양수: 파란색, 0: 기본 색상
    /// </summary>
    public class AmountToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is decimal amount)
            {
                if (amount < 0)
                {
                    // 음수일 때 빨간색
                    return new SolidColorBrush(Color.FromRgb(205, 100, 20));
                }
                else if (amount > 0)
                {
                    // 양수일 때 파란색
                    return new SolidColorBrush(Color.FromRgb(125, 205, 140));
                }
            }

            // 0이거나 변환 불가능한 경우 기본 색상 (검정/흰색)
            return new SolidColorBrush(Colors.Gray);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
