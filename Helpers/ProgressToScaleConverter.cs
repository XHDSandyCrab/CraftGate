using System;
using System.Globalization;
using System.Windows.Data;

namespace CraftGate.Helpers
{
    /// <summary>把 0-100 的百分比转成 0-1 的缩放比，用于进度条 ScaleTransform.X（绕开 ProgressBar 模板测宽问题）。</summary>
    public class ProgressToScaleConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            double p = 0;
            if (value is double d) p = d;
            else if (value is int i) p = i;
            else double.TryParse(value?.ToString(), out p);
            p = Math.Max(0, Math.Min(100, p));
            return p / 100.0;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
