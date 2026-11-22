using System.Windows;
using System.Windows.Controls;

namespace CraftGate.Helpers
{
    public static class ScrollViewerHelper
    {
        // 注册一个可以被动画控制的附加属性
        public static readonly DependencyProperty VerticalOffsetProperty =
            DependencyProperty.RegisterAttached(
                "VerticalOffset",
                typeof(double),
                typeof(ScrollViewerHelper),
                new PropertyMetadata(0.0, OnVerticalOffsetChanged));

        public static void SetVerticalOffset(DependencyObject element, double value)
            => element.SetValue(VerticalOffsetProperty, value);

        public static double GetVerticalOffset(DependencyObject element)
            => (double)element.GetValue(VerticalOffsetProperty);

        private static void OnVerticalOffsetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ScrollViewer scrollViewer)
            {
                // 将附加属性的值同步给真正的滚动偏移
                scrollViewer.ScrollToVerticalOffset((double)e.NewValue);
            }
        }
    }
}