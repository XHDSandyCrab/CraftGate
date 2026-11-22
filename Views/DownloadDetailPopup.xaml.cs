using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace CraftGate.Views
{
    /// <summary>
    /// 通知详情弹窗：由右下角通知"变形"而来——从通知位置缩放平移到屏幕中央，
    /// 背景遮罩淡入、主内容模糊；点击弹窗外反向缩回通知，通知本体始终不消失。
    /// </summary>
    public partial class DownloadDetailPopup : UserControl
    {
        private bool _isOpen;

        public DownloadDetailPopup()
        {
            InitializeComponent();
        }

        public bool IsOpen => _isOpen;

        /// <summary>
        /// 从通知位置展开到中央。参数为通知在"弹窗所在容器"坐标系里的中心点。
        /// </summary>
        // 弹窗最终尺寸
        private const double TargetWidth = 520;
        private const double TargetHeight = 480;

        public void OpenFrom(Point toastCenter, double toastWidth, double toastHeight)
        {
            if (_isOpen) return;
            _isOpen = true;

            // 【关键】清除上次关闭动画的 HoldEnd 锁！否则 Opacity/Width/Height 被旧动画锁住，
            // 下面直接赋值不生效——弹窗本体不可见，只剩遮罩和模糊（用户截图现象）。
            ClearAnimations();

            Visibility = Visibility.Visible;
            IsHitTestVisible = true;
            // 关键：刚从 Collapsed 变 Visible 还没布局，ActualWidth=0；必须强制布局，
            // 否则回退到 Width/2（UserControl 未设 Width=NaN）→ startX=NaN → DoubleAnimation 崩溃。
            UpdateLayout();

            // 起点：卡片宽高 = 通知宽高；因卡片居中对齐，中心在屏幕正中，
            // 用 Translate 把它挪到通知中心，形变起点就精确覆盖通知那个小圆角矩形。
            double popupCenterX = RootLayout.ActualWidth > 0 ? RootLayout.ActualWidth / 2 : TargetWidth / 2;
            double popupCenterY = RootLayout.ActualHeight > 0 ? RootLayout.ActualHeight / 2 : TargetHeight / 2;
            double startX = toastCenter.X - popupCenterX;
            double startY = toastCenter.Y - popupCenterY;

            PopupCard.Width = toastWidth > 0 ? toastWidth : 340;
            PopupCard.Height = toastHeight > 0 ? toastHeight : 72;
            PopupCard.CornerRadius = new CornerRadius(8);   // 起点圆角 = 通知圆角
            PopupTranslate.X = startX;
            PopupTranslate.Y = startY;
            Backdrop.Opacity = 0;
            PopupCard.Opacity = 1;                          // 卡片本体全程可见（形变连续）
            PopupContent.Opacity = 0;                       // 内容延迟淡入，避免小尺寸时挤压

            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var dur = TimeSpan.FromMilliseconds(520);

            // 宽 + 高 属性动画：矩形真实地从通知尺寸长成弹窗尺寸
            AnimateDouble(PopupCard, FrameworkElement.WidthProperty, PopupCard.Width, TargetWidth, dur, ease);
            AnimateDouble(PopupCard, FrameworkElement.HeightProperty, PopupCard.Height, TargetHeight, dur, ease);
            // 位置：从通知中心滑到屏幕正中
            AnimateDouble(PopupTranslate, TranslateTransform.XProperty, startX, 0, dur, ease);
            AnimateDouble(PopupTranslate, TranslateTransform.YProperty, startY, 0, dur, ease);
            // 圆角 8 → 14
            AnimateCorner(PopupCard, new CornerRadius(8), new CornerRadius(14), dur, ease);
            // 背景模糊层淡入
            AnimateDouble(Backdrop, UIElement.OpacityProperty, 0, 1, TimeSpan.FromMilliseconds(350), ease);
            // 内容在后半程淡入
            var contentAnim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260))
            {
                BeginTime = TimeSpan.FromMilliseconds(220),
                EasingFunction = ease,
                FillBehavior = FillBehavior.HoldEnd
            };
            var contentSb = new Storyboard();
            contentSb.Children.Add(contentAnim);
            Storyboard.SetTarget(contentAnim, PopupContent);
            Storyboard.SetTargetProperty(contentAnim, new PropertyPath(UIElement.OpacityProperty));
            contentSb.Begin();
        }

        /// <summary>反向缩回通知位置：宽高/位置/圆角连续变回通知，结束后隐藏并通知 MainWindow 恢复通知。</summary>
        public void CloseTo(Point toastCenter, double toastWidth, double toastHeight)
        {
            if (!_isOpen) return;
            _isOpen = false;
            ClearAnimations();   // 清掉打开动画的 HoldEnd 锁，读取当前值才准确

            double popupCenterX = RootLayout.ActualWidth > 0 ? RootLayout.ActualWidth / 2 : TargetWidth / 2;
            double popupCenterY = RootLayout.ActualHeight > 0 ? RootLayout.ActualHeight / 2 : TargetHeight / 2;
            double endX = toastCenter.X - popupCenterX;
            double endY = toastCenter.Y - popupCenterY;

            var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
            var dur = TimeSpan.FromMilliseconds(360);

            // 内容先快速淡出
            AnimateDouble(PopupContent, UIElement.OpacityProperty, PopupContent.Opacity, 0, TimeSpan.FromMilliseconds(140), ease);
            // 矩形连续缩回通知尺寸 + 滑回通知位置 + 圆角回到 8
            AnimateDouble(PopupCard, FrameworkElement.WidthProperty, PopupCard.ActualWidth, toastWidth > 0 ? toastWidth : 340, dur, ease);
            AnimateDouble(PopupCard, FrameworkElement.HeightProperty, PopupCard.ActualHeight, toastHeight > 0 ? toastHeight : 72, dur, ease);
            AnimateDouble(PopupTranslate, TranslateTransform.XProperty, PopupTranslate.X, endX, dur, ease);
            AnimateDouble(PopupTranslate, TranslateTransform.YProperty, PopupTranslate.Y, endY, dur, ease);
            AnimateCorner(PopupCard, PopupCard.CornerRadius, new CornerRadius(8), dur, ease);
            AnimateDouble(Backdrop, UIElement.OpacityProperty, Backdrop.Opacity, 0, TimeSpan.FromMilliseconds(260), ease);
            // 最后整卡淡出并收起，再通知 MainWindow 让通知重新出现
            var fade = new DoubleAnimation(1, 0, dur) { EasingFunction = ease, FillBehavior = FillBehavior.HoldEnd };
            var fadeSb = new Storyboard();
            fadeSb.Children.Add(fade);
            Storyboard.SetTarget(fade, PopupCard);
            Storyboard.SetTargetProperty(fade, new PropertyPath(UIElement.OpacityProperty));
            fadeSb.Completed += (_, __) =>
            {
                Visibility = Visibility.Collapsed;
                IsHitTestVisible = false;
                if (App.Current.MainWindow is MainWindow mw) mw.OnToastDetailClosed();
            };
            fadeSb.Begin();
        }

        private void Backdrop_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            // 点击弹窗外 → 缩回通知；通知本体不消失
            if (App.Current.MainWindow is MainWindow mw) mw.CloseToastDetail();
        }

        /// <summary>清除所有 ongoing 动画（尤其 HoldEnd 会锁住属性值），让下面直接赋值生效。</summary>
        private void ClearAnimations()
        {
            Backdrop.BeginAnimation(UIElement.OpacityProperty, null);
            PopupCard.BeginAnimation(UIElement.OpacityProperty, null);
            PopupCard.BeginAnimation(FrameworkElement.WidthProperty, null);
            PopupCard.BeginAnimation(FrameworkElement.HeightProperty, null);
            PopupContent.BeginAnimation(UIElement.OpacityProperty, null);
            PopupTranslate.BeginAnimation(TranslateTransform.XProperty, null);
            PopupTranslate.BeginAnimation(TranslateTransform.YProperty, null);
        }

        private static void AnimateCorner(Border border, CornerRadius from, CornerRadius to, TimeSpan duration, IEasingFunction ease)
        {
            var anim = new ObjectAnimationUsingKeyFrames { Duration = duration };
            int steps = 12;
            for (int i = 0; i <= steps; i++)
            {
                double t = i / (double)steps;
                double eased = ease.Ease(t);
                double tl = from.TopLeft + (to.TopLeft - from.TopLeft) * eased;
                double tr = from.TopRight + (to.TopRight - from.TopRight) * eased;
                double br = from.BottomRight + (to.BottomRight - from.BottomRight) * eased;
                double bl = from.BottomLeft + (to.BottomLeft - from.BottomLeft) * eased;
                anim.KeyFrames.Add(new DiscreteObjectKeyFrame(new CornerRadius(tl, tr, br, bl),
                    KeyTime.FromTimeSpan(TimeSpan.FromTicks(duration.Ticks * i / steps))));
            }
            var sb = new Storyboard();
            sb.Children.Add(anim);
            Storyboard.SetTarget(anim, border);
            Storyboard.SetTargetProperty(anim, new PropertyPath(Border.CornerRadiusProperty));
            sb.Begin();
        }

        private static void AnimateDouble(DependencyObject target, DependencyProperty prop,
                                          double from, double to, TimeSpan duration, IEasingFunction ease)
        {
            // NaN/Infinity 会让 DoubleAnimation 构造抛 ArgumentException（"NaN不是From的有效值"），
            // 布局未就绪时坐标可能算成 NaN，这里兜底：非法 from 用 to，非法 to 直接跳过。
            if (double.IsNaN(from) || double.IsInfinity(from)) from = to;
            if (double.IsNaN(to) || double.IsInfinity(to)) return;
            var anim = new DoubleAnimation(from, to, duration) { EasingFunction = ease, FillBehavior = FillBehavior.HoldEnd };
            // Transform(ScaleTransform/TranslateTransform) 是 Animatable，直接 BeginAnimation 才可靠；
            // 之前用 Storyboard.SetTarget 对 Transform 不生效，导致弹窗停在通知位置没到中间。
            if (target is Animatable animatable)
                animatable.BeginAnimation(prop, anim);
            else
            {
                var sb = new Storyboard();
                sb.Children.Add(anim);
                Storyboard.SetTarget(anim, target);
                Storyboard.SetTargetProperty(anim, new PropertyPath(prop));
                sb.Begin();
            }
        }
    }
}
