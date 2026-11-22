using System;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace CraftGate.Helpers
{
    /// <summary>
    /// 给 ItemsControl / ListBox 挂上后，数据项（尤其是异步加载进来的）会逐个"淡入 + 上移"入场。
    /// 通过监听 ItemsSource 集合变化 + LayoutUpdated 兜底，兼容异步/增量填充。
    /// 用法：&lt;ListBox helpers:StaggerPanel.Enabled="True" helpers:StaggerPanel.ItemDelay="0:0:0.05"/&gt;
    /// </summary>
    public static class StaggerPanel
    {
        public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
            "Enabled", typeof(bool), typeof(StaggerPanel),
            new PropertyMetadata(false, OnEnabledChanged));

        public static readonly DependencyProperty ItemDelayProperty = DependencyProperty.RegisterAttached(
            "ItemDelay", typeof(TimeSpan), typeof(StaggerPanel),
            new PropertyMetadata(TimeSpan.FromMilliseconds(45)));

        public static readonly DependencyProperty ShiftYProperty = DependencyProperty.RegisterAttached(
            "ShiftY", typeof(double), typeof(StaggerPanel),
            new PropertyMetadata(12.0));

        public static void SetEnabled(DependencyObject element, bool value) => element.SetValue(EnabledProperty, value);
        public static bool GetEnabled(DependencyObject element) => (bool)element.GetValue(EnabledProperty);

        public static void SetItemDelay(DependencyObject element, TimeSpan value) => element.SetValue(ItemDelayProperty, value);
        public static TimeSpan GetItemDelay(DependencyObject element) => (TimeSpan)element.GetValue(ItemDelayProperty);

        public static void SetShiftY(DependencyObject element, double value) => element.SetValue(ShiftYProperty, value);
        public static double GetShiftY(DependencyObject element) => (double)element.GetValue(ShiftYProperty);

        private static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (!(d is ItemsControl itemsControl)) return;
            if ((bool)e.NewValue)
                itemsControl.Loaded += ItemsControl_Loaded;
            else
                itemsControl.Loaded -= ItemsControl_Loaded;
        }

        private static void ItemsControl_Loaded(object sender, RoutedEventArgs e)
        {
            var items = sender as ItemsControl;
            if (items == null) return;
            items.Loaded -= ItemsControl_Loaded;

            // 记录已播放过动画的项容器，避免 LayoutUpdated 反复触发
            var animated = new System.Collections.Generic.HashSet<DependencyObject>();

            ScanItems(items, animated);

            // 数据异步/增量加入时，靠 LayoutUpdated 兜底扫描
            EventHandler layoutHandler = null;
            layoutHandler = (s, _) =>
            {
                if (!items.IsLoaded) return;
                ScanItems(items, animated);
            };
            items.LayoutUpdated += layoutHandler;

            // 集合 Reset（清空/重载）时移除记录，允许重新入场
            if (items.ItemsSource is INotifyCollectionChanged incc)
            {
                NotifyCollectionChangedEventHandler ccHandler = null;
                ccHandler = (s, args) =>
                {
                    if (args.Action == NotifyCollectionChangedAction.Reset) animated.Clear();
                };
                incc.CollectionChanged += ccHandler;
                items.Unloaded += (_, __) => incc.CollectionChanged -= ccHandler;
            }
        }

        private static void ScanItems(ItemsControl items, System.Collections.Generic.HashSet<DependencyObject> animated)
        {
            TimeSpan delay = GetItemDelay(items);
            double shiftY = GetShiftY(items);
            int newIndex = 0;

            foreach (object dataItem in items.Items)
            {
                var container = items.ItemContainerGenerator.ContainerFromItem(dataItem) as UIElement;
                if (container == null || animated.Contains(container)) continue;

                animated.Add(container);
                // 逐个入场：按本批新项的顺序递增延迟
                TimeSpan begin = TimeSpan.FromMilliseconds(delay.TotalMilliseconds * newIndex);
                PlayEntrance(container, shiftY, begin);
                newIndex++;
            }
        }

        private static void PlayEntrance(UIElement element, double shiftY, TimeSpan begin)
        {
            // 防御：模板加载期/异常状态下对元素做动画可能抛
            // "无法对不可变对象'(0),(1)'进行动画处理"。入场动画是纯装饰，
            // 失败就跳过动画直接显示，绝不能中断界面加载（否则表现为"卡住/崩溃"）。
            if (element == null) return;
            // IsLoaded 属于 FrameworkElement，UIElement 没有；ItemContainer 几乎都是 FrameworkElement
            if (element is System.Windows.FrameworkElement fe && !fe.IsLoaded) return;
            try
            {
                var transform = new TranslateTransform(0, shiftY);
                element.RenderTransform = transform;

                var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
                var moveAnim = new DoubleAnimation(shiftY, 0, TimeSpan.FromMilliseconds(340))
                {
                    BeginTime = begin, EasingFunction = ease, FillBehavior = FillBehavior.Stop
                };
                var opacityAnim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300))
                {
                    BeginTime = begin, EasingFunction = ease, FillBehavior = FillBehavior.Stop
                };

                moveAnim.Completed += (s, _) =>
                {
                    element.RenderTransform = null;
                    element.Opacity = 1;
                };

                element.Opacity = 0;
                transform.BeginAnimation(TranslateTransform.YProperty, moveAnim);
                element.BeginAnimation(UIElement.OpacityProperty, opacityAnim);
            }
            catch
            {
                // 动画失败时确保元素可见，不影响功能
                try { element.Opacity = 1; element.RenderTransform = null; } catch { }
            }
        }
    }
}
