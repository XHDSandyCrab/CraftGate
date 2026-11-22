using System;
using System.Globalization;
using System.Windows.Data;

namespace CraftGate.Helpers
{
    public class PercentageToWidthConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null || values.Length < 2) return 0.0;

            if (values[0] is double progress && values[1] is double actualWidth)
            {
                // 强制限制范围，确保 100% 时就是满宽
                progress = Math.Max(0, Math.Min(100, progress));
                return actualWidth * (progress / 100.0);
            }
            return 0.0;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}