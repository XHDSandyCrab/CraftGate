using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace CraftGate.Views
{
    /// <summary>
    /// 发现新版本时的更新提示弹窗：显示更新日志 + 立即更新按钮 + 关闭叉叉。
    /// 若更新日志标记"必须更新"（IsMandatory=true），隐藏叉叉与"稍后"，只能点立即更新。
    /// </summary>
    public partial class UpdateDialog : UserControl
    {
        private CraftGate.Services.UpdateService.UpdateResult _result;

        public UpdateDialog()
        {
            InitializeComponent();
        }

        public bool IsOpen => Visibility == Visibility.Visible;

        public void Show(CraftGate.Services.UpdateService.UpdateResult result)
        {
            _result = result;
            VersionText.Text = $"v{result.LatestVersion}  （当前 v{CraftGate.Services.UpdateService.CurrentVersion}）";
            NotesText.Text = string.IsNullOrWhiteSpace(result.ReleaseNotes) ? "暂无更新日志" : result.ReleaseNotes.Trim();

            // 强制更新：隐藏叉叉和"稍后"，只能立即更新
            CloseBtn.Visibility = result.IsMandatory ? Visibility.Collapsed : Visibility.Visible;
            LaterBtn.Visibility = result.IsMandatory ? Visibility.Collapsed : Visibility.Visible;

            Visibility = Visibility.Visible;
            IsHitTestVisible = true;
            UpdateLayout();

            CardScale.ScaleX = 0.85;
            CardScale.ScaleY = 0.85;
            Card.Opacity = 0;
            Backdrop.Opacity = 0;

            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var dur = TimeSpan.FromMilliseconds(280);
            Animate(CardScale, ScaleTransform.ScaleXProperty, 0.85, 1, dur, ease);
            Animate(CardScale, ScaleTransform.ScaleYProperty, 0.85, 1, dur, ease);
            Animate(Card, UIElement.OpacityProperty, 0, 1, dur, ease);
            Animate(Backdrop, UIElement.OpacityProperty, 0, 1, dur, ease);
        }

        public void Close()
        {
            if (!IsOpen) return;
            var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
            var dur = TimeSpan.FromMilliseconds(200);
            Animate(CardScale, ScaleTransform.ScaleXProperty, CardScale.ScaleX, 0.9, dur, ease);
            Animate(CardScale, ScaleTransform.ScaleYProperty, CardScale.ScaleY, 0.9, dur, ease);
            var fade = new DoubleAnimation(Card.Opacity, 0, dur) { EasingFunction = ease, FillBehavior = FillBehavior.HoldEnd };
            var sb = new Storyboard();
            sb.Children.Add(fade);
            Storyboard.SetTarget(fade, Card);
            Storyboard.SetTargetProperty(fade, new PropertyPath(UIElement.OpacityProperty));
            sb.Completed += (_, __) =>
            {
                Visibility = Visibility.Collapsed;
                IsHitTestVisible = false;
                // 清掉 HoldEnd 锁，避免下次打开时赋值无效
                Card.BeginAnimation(UIElement.OpacityProperty, null);
                Backdrop.BeginAnimation(UIElement.OpacityProperty, null);
                CardScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                CardScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            };
            sb.Begin();
            Animate(Backdrop, UIElement.OpacityProperty, Backdrop.Opacity, 0, dur, ease);
        }

        private void UpdateBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_result != null && !string.IsNullOrEmpty(_result.ReleaseUrl))
                    Process.Start(new ProcessStartInfo(_result.ReleaseUrl) { UseShellExecute = true });
            }
            catch { }
        }

        private void CloseBtn_Click(object sender, RoutedEventArgs e) => Close();
        private void LaterBtn_Click(object sender, RoutedEventArgs e) => Close();
        private void Backdrop_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // 强制更新时点遮罩不关闭
            if (_result != null && _result.IsMandatory) return;
            Close();
        }

        private static void Animate(DependencyObject target, DependencyProperty prop, double from, double to,
                                    TimeSpan duration, IEasingFunction ease)
        {
            if (double.IsNaN(from) || double.IsInfinity(from)) from = to;
            if (double.IsNaN(to) || double.IsInfinity(to)) return;
            var anim = new DoubleAnimation(from, to, duration) { EasingFunction = ease, FillBehavior = FillBehavior.HoldEnd };
            if (target is System.Windows.Media.Animation.Animatable a) a.BeginAnimation(prop, anim);
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
