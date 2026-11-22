using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace CraftGate.Views
{
    public partial class ToastNotification : UserControl
    {
        /// <summary>是否下载类通知：仅此类可点开详情弹窗；只读通知（检查更新/成功/失败）为 false。</summary>
        public bool IsDownload { get; set; }

        private DispatcherTimer _hideTimer;

        // 停留时长：普通提示 4 秒，错误/不支持类 6 秒
        private static readonly TimeSpan NormalDuration = TimeSpan.FromSeconds(4);
        private static readonly TimeSpan WarningDuration = TimeSpan.FromSeconds(6);

        // 进度条圆角：下载中=左圆右直（进度头是直线）；填满/成功/失败=两端圆角（贴合卡片右下圆角）
        private static readonly CornerRadius ProgressCornerRunning = new CornerRadius(8, 0, 0, 8);
        private static readonly CornerRadius ProgressCornerFull = new CornerRadius(8, 8, 8, 8);

        public ToastNotification()
        {
            InitializeComponent();

            _hideTimer = new DispatcherTimer { Interval = NormalDuration };
            _hideTimer.Tick += (s, e) =>
            {
                _hideTimer.Stop();
                Hide();
            };
        }

        public void Show(string title, string message)
        {
            _hideTimer.Stop();
            _hideTimer.Interval = NormalDuration;
            TxtTitle.Text = title;
            TxtMessage.Text = message;
            TxtIcon.Text = "\uE896";
            TxtIcon.Foreground = (Brush)FindResource("AccentBrush");

            if (ProgressFill != null)
            {
                ProgressFill.Background = (Brush)FindResource("AccentBrush");
                ProgressFill.CornerRadius = ProgressCornerRunning;
                ProgressFill.BeginAnimation(WidthProperty, null);
                ProgressFill.Width = 0;
            }

            var anim = new DoubleAnimation(400, 0, TimeSpan.FromMilliseconds(400))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            SlideTransform.BeginAnimation(TranslateTransform.XProperty, anim);
        }

        public void UpdateProgress(double percent, string message)
        {
            _hideTimer.Stop();
            TxtMessage.Text = message;

            if (ProgressFill == null) return;

            double targetWidth = (ActualWidth > 0 ? ActualWidth : 340) * (percent / 100.0);

            var anim = new DoubleAnimation(ProgressFill.Width, targetWidth, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            ProgressFill.BeginAnimation(WidthProperty, anim);
        }

        public void ShowSuccess(string message)
        {
            _hideTimer.Stop();
            _hideTimer.Interval = NormalDuration;
            TxtTitle.Text = "操作成功";
            TxtMessage.Text = message;
            TxtIcon.Text = "\uE930";
            TxtIcon.Foreground = new SolidColorBrush(Color.FromRgb(61, 220, 132));
            ProgressFill.Background = new SolidColorBrush(Color.FromRgb(61, 220, 132));

            ProgressFill.CornerRadius = ProgressCornerFull;
            ProgressFill.BeginAnimation(WidthProperty, null);
            ProgressFill.Width = ActualWidth > 0 ? ActualWidth : 340;

            _hideTimer.Start();
        }

        public void ShowError(string message)
        {
            _hideTimer.Stop();
            _hideTimer.Interval = WarningDuration;
            TxtTitle.Text = "操作失败";
            TxtMessage.Text = message;
            TxtIcon.Text = "\uEA39";
            TxtIcon.Foreground = new SolidColorBrush(Color.FromRgb(255, 80, 80));
            ProgressFill.Background = new SolidColorBrush(Color.FromRgb(255, 80, 80));

            ProgressFill.CornerRadius = ProgressCornerFull;
            ProgressFill.BeginAnimation(WidthProperty, null);
            ProgressFill.Width = ActualWidth > 0 ? ActualWidth : 340;

            _hideTimer.Start();
        }

        public void ShowUnsupported(string message)
        {
            _hideTimer.Stop();
            _hideTimer.Interval = WarningDuration;
            TxtTitle.Text = "系统不支持";
            TxtMessage.Text = message;
            TxtIcon.Text = "\uEA39";
            TxtIcon.Foreground = new SolidColorBrush(Color.FromRgb(255, 80, 80));
            ProgressFill.Background = new SolidColorBrush(Color.FromRgb(255, 80, 80));

            ProgressFill.CornerRadius = ProgressCornerFull;
            ProgressFill.BeginAnimation(WidthProperty, null);
            ProgressFill.Width = ActualWidth > 0 ? ActualWidth : 340;

            _hideTimer.Start();
        }

        public void Hide()
        {
            _hideTimer.Stop();
            var anim = new DoubleAnimation(0, 400, TimeSpan.FromMilliseconds(300))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            anim.Completed += (s, e) =>
            {
                if (Parent is Panel parent) parent.Children.Remove(this);
            };
            SlideTransform.BeginAnimation(TranslateTransform.XProperty, anim);
        }
    }
}