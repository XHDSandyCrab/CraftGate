using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using CraftGate.Models;
using CraftGate.Services;

namespace CraftGate.Views
{
    public partial class DownloadCenterView : UserControl
    {
        public event Action OnBackRequested;
        private FileDownloadTask _currentTask;

        public DownloadCenterView()
        {
            InitializeComponent();
            DataContext = DownloadManager.Instance;
        }

        private void Card_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is FileDownloadTask task)
            {
                _currentTask = task;
                TxtFileName.Text = task.FileName;
                TxtUrl.Text = task.Url;
                TxtPath.Text = task.SavePath;

                DetailModal.Visibility = Visibility.Visible;

                var sb = new Storyboard();

                // 遮罩层：颜色平滑加深（从透明到黑色 0xAA）
                var bgFade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300));
                Storyboard.SetTarget(bgFade, ModalBg);
                Storyboard.SetTargetProperty(bgFade, new PropertyPath("Opacity"));

                // 内容层：淡入并放大
                var contentFade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250));
                var scaleX = new DoubleAnimation(0.9, 1, TimeSpan.FromMilliseconds(250)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                var scaleY = new DoubleAnimation(0.9, 1, TimeSpan.FromMilliseconds(250)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };

                Storyboard.SetTarget(contentFade, ModalContent);
                Storyboard.SetTargetProperty(contentFade, new PropertyPath("Opacity"));
                Storyboard.SetTarget(scaleX, ModalContent);
                Storyboard.SetTargetProperty(scaleX, new PropertyPath("(UIElement.RenderTransform).(ScaleTransform.ScaleX)"));
                Storyboard.SetTarget(scaleY, ModalContent);
                Storyboard.SetTargetProperty(scaleY, new PropertyPath("(UIElement.RenderTransform).(ScaleTransform.ScaleY)"));

                sb.Children.Add(bgFade);
                sb.Children.Add(contentFade);
                sb.Children.Add(scaleX);
                sb.Children.Add(scaleY);
                sb.Begin();
            }
        }

        private void BtnBack_Click(object sender, RoutedEventArgs e)
        {
            OnBackRequested?.Invoke();
        }

        private void BtnCloseModal_Click(object sender, RoutedEventArgs e)
        {
            var sb = new Storyboard();

            var bgFade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200));
            Storyboard.SetTarget(bgFade, ModalBg);
            Storyboard.SetTargetProperty(bgFade, new PropertyPath("Opacity"));

            var contentFade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200));
            var scaleX = new DoubleAnimation(1, 0.9, TimeSpan.FromMilliseconds(200)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
            var scaleY = new DoubleAnimation(1, 0.9, TimeSpan.FromMilliseconds(200)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };

            Storyboard.SetTarget(contentFade, ModalContent);
            Storyboard.SetTargetProperty(contentFade, new PropertyPath("Opacity"));
            Storyboard.SetTarget(scaleX, ModalContent);
            Storyboard.SetTargetProperty(scaleX, new PropertyPath("(UIElement.RenderTransform).(ScaleTransform.ScaleX)"));
            Storyboard.SetTarget(scaleY, ModalContent);
            Storyboard.SetTargetProperty(scaleY, new PropertyPath("(UIElement.RenderTransform).(ScaleTransform.ScaleY)"));

            sb.Children.Add(bgFade);
            sb.Children.Add(contentFade);
            sb.Children.Add(scaleX);
            sb.Children.Add(scaleY);
            sb.Completed += (s, ev) => DetailModal.Visibility = Visibility.Collapsed;
            sb.Begin();
        }

        private void ModalBg_Click(object sender, MouseButtonEventArgs e)
        {
            BtnCloseModal_Click(this, new RoutedEventArgs());
        }

        private void ModalContent_Click(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
        }

        private void BtnCopyUrl_Click(object sender, RoutedEventArgs e)
        {
            if (_currentTask != null)
            {
                try
                {
                    Clipboard.SetText(_currentTask.Url);
                    if (Application.Current.MainWindow is MainWindow mainWin)
                        mainWin.ShowToastSuccess("下载链接已复制到剪贴板！");
                }
                catch { }
            }
        }
    }
}