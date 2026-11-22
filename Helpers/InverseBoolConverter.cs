using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace CraftGate.Helpers
{
    /// <summary>
    /// 反相布尔 → Visibility 转换器：
    /// true → Collapsed，false → Visible。
    /// 用于"没有账户时显示创建入口"这类反向可见性场景。
    /// </summary>
    public class InverseBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool b = value is bool v && v;
            return b ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is Visibility vis && vis == Visibility.Visible ? false : true;
        }
    }
}
