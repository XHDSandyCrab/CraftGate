using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace CraftGate.Controls
{
    public partial class MorphProgressButton : UserControl
    {
        private const double ButtonHeight = 48;
        private const double BarHeight = 8;
        private const double ButtonRadius = 8;
        private const double BarRadius = 4;

        public MorphProgressButton()
        {
            InitializeComponent();

            // 初始化 Label 文本（DP 默认值不会触发回调，手动同步一次）
            Label.Text = Text;

            Loaded += (_, __) => UpdateFill(false);
            Track.SizeChanged += (_, __) => UpdateFill(false);
        }

        #region Text
        public static readonly DependencyProperty TextProperty =
            DependencyProperty.Register(nameof(Text), typeof(string), typeof(MorphProgressButton),
                new PropertyMetadata("启动游戏", OnTextChanged));

        private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var ctrl = (MorphProgressButton)d;
            if (ctrl.Label != null)
                ctrl.Label.Text = (e.NewValue as string) ?? string.Empty;
        }

        public string Text
        {
            get => (string)GetValue(TextProperty);
            set => SetValue(TextProperty, value);
        }
        #endregion

        #region Progress
        public static readonly DependencyProperty ProgressProperty =
            DependencyProperty.Register(nameof(Progress), typeof(double), typeof(MorphProgressButton),
                new PropertyMetadata(0d, (d, e) => ((MorphProgressButton)d).UpdateFill(true)));

        public double Progress
        {
            get => (double)GetValue(ProgressProperty);
            set => SetValue(ProgressProperty, value);
        }
        #endregion

        #region IsBusy
        public static readonly DependencyProperty IsBusyProperty =
            DependencyProperty.Register(nameof(IsBusy), typeof(bool), typeof(MorphProgressButton),
                new PropertyMetadata(false, (d, e) => ((MorphProgressButton)d).ApplyBusyState((bool)e.NewValue)));

        public bool IsBusy
        {
            get => (bool)GetValue(IsBusyProperty);
            set => SetValue(IsBusyProperty, value);
        }
        #endregion

        #region Command
        public static readonly DependencyProperty CommandProperty =
            DependencyProperty.Register(nameof(Command), typeof(ICommand), typeof(MorphProgressButton),
                new PropertyMetadata(null));

        public ICommand Command
        {
            get => (ICommand)GetValue(CommandProperty);
            set => SetValue(CommandProperty, value);
        }
        #endregion

        private void HitArea_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (IsBusy) return;
            if (Command != null && Command.CanExecute(null))
                Command.Execute(null);
        }

        private void ApplyBusyState(bool busy)
        {
            if (busy) MorphToProgress();
            else RestoreToButton();
        }

        private void MorphToProgress()
        {
            Cursor = Cursors.Arrow;

            Track.Background = (Brush)FindResource("ProgressTrackBrush");
            Fill.Visibility = Visibility.Visible;

            Label.BeginAnimation(OpacityProperty,
                new DoubleAnimation(0, TimeSpan.FromMilliseconds(120)));

            BeginAnimation(HeightProperty, new DoubleAnimation
            {
                To = BarHeight,
                Duration = TimeSpan.FromMilliseconds(260),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
            });

            Track.BeginAnimation(Border.CornerRadiusProperty,
                CreateRadiusAnimation(ButtonRadius, BarRadius));

            UpdateFill(false);
        }

        private void RestoreToButton()
        {
            Cursor = Cursors.Hand;

            Label.BeginAnimation(OpacityProperty, new DoubleAnimation
            {
                To = 1,
                BeginTime = TimeSpan.FromMilliseconds(140),
                Duration = TimeSpan.FromMilliseconds(200)
            });

            var heightAnim = new DoubleAnimation
            {
                To = ButtonHeight,
                Duration = TimeSpan.FromMilliseconds(260),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
            };
            heightAnim.Completed += (_, __) =>
            {
                BeginAnimation(HeightProperty, null);
                Track.BeginAnimation(Border.CornerRadiusProperty, null);
                Track.CornerRadius = new CornerRadius(ButtonRadius);
                Track.Background = (Brush)FindResource("AccentBrush");

                Fill.Visibility = Visibility.Collapsed;
                Fill.BeginAnimation(WidthProperty, null);
                Fill.Width = 0;
            };

            BeginAnimation(HeightProperty, heightAnim);
            Track.BeginAnimation(Border.CornerRadiusProperty,
                CreateRadiusAnimation(BarRadius, ButtonRadius));
        }

        private static ObjectAnimationUsingKeyFrames CreateRadiusAnimation(double from, double to)
        {
            var anim = new ObjectAnimationUsingKeyFrames();
            anim.KeyFrames.Add(new DiscreteObjectKeyFrame(
                new CornerRadius(from), KeyTime.FromTimeSpan(TimeSpan.Zero)));
            anim.KeyFrames.Add(new DiscreteObjectKeyFrame(
                new CornerRadius(to), KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(200))));
            return anim;
        }

        private void UpdateFill(bool animate)
        {
            if (Track.ActualWidth <= 0) return;

            // Math.Clamp 在 .NET Framework 4.x 不存在，手动 clamp
            double p = Progress;
            if (p < 0) p = 0;
            if (p > 100) p = 100;

            double target = Track.ActualWidth * p / 100.0;

            if (animate)
            {
                Fill.BeginAnimation(WidthProperty, new DoubleAnimation
                {
                    To = target,
                    Duration = TimeSpan.FromMilliseconds(240),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                });
            }
            else
            {
                Fill.BeginAnimation(WidthProperty, null);
                Fill.Width = target;
            }
        }
    }
}