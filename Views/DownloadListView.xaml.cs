using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CraftGate.ViewModels;

namespace CraftGate.Views
{
    public partial class DownloadListView : UserControl
    {
        private readonly DownloadListViewModel _vm = new DownloadListViewModel();

        private double _targetOffset = 0;
        private double _currentOffset = 0;
        private bool _isRendering = false;
        private ScrollViewer _scrollViewer;

        // 👇 核心：事件必须定义为两个参数
        public event Action<string, string> OnVersionSelected;

        public DownloadListView()
        {
            InitializeComponent();
            DataContext = _vm;

            Loaded += async (_, __) => await _vm.InitializeAsync();
        }

        private void VersionList_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (Math.Abs(e.Delta) < 120) return;

            e.Handled = true;

            if (_scrollViewer == null)
            {
                _scrollViewer = FindVisualChild<ScrollViewer>(VersionList);
            }

            if (_scrollViewer != null)
            {
                if (!_isRendering)
                {
                    _targetOffset = _scrollViewer.VerticalOffset;
                    _currentOffset = _scrollViewer.VerticalOffset;
                    _isRendering = true;
                    CompositionTarget.Rendering += SmoothScroll;
                }

                double scrollAmount = (e.Delta / 120.0) * 50;
                _targetOffset -= scrollAmount;
                _targetOffset = Math.Max(0, Math.Min(_targetOffset, _scrollViewer.ScrollableHeight));
            }
        }

        private void SmoothScroll(object sender, EventArgs e)
        {
            if (_scrollViewer == null)
            {
                CompositionTarget.Rendering -= SmoothScroll;
                _isRendering = false;
                return;
            }

            // 窗口不可见时没必要逐帧渲染：直接落到目标位置并停止帧回调
            if (!CraftGate.Services.VisibilityService.IsAppVisible)
            {
                _currentOffset = _targetOffset;
                _scrollViewer.ScrollToVerticalOffset(_currentOffset);
                CompositionTarget.Rendering -= SmoothScroll;
                _isRendering = false;
                return;
            }

            double lerpFactor = 0.1;
            _currentOffset += (_targetOffset - _currentOffset) * lerpFactor;

            if (Math.Abs(_targetOffset - _currentOffset) < 0.5)
            {
                _currentOffset = _targetOffset;
                _scrollViewer.ScrollToVerticalOffset(_currentOffset);
                CompositionTarget.Rendering -= SmoothScroll;
                _isRendering = false;
                return;
            }

            _scrollViewer.ScrollToVerticalOffset(_currentOffset);
        }

        private void Card_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            // 👇 触发时传入两个参数
            if (sender is Border border && border.DataContext is CraftGate.Models.VersionDisplayModel version)
            {
                OnVersionSelected?.Invoke(version.Id, version.Url);
            }
        }

        private static T FindVisualChild<T>(DependencyObject obj) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(obj); i++)
            {
                var child = VisualTreeHelper.GetChild(obj, i);
                if (child is T t) return t;
                var childOfChild = FindVisualChild<T>(child);
                if (childOfChild != null) return childOfChild;
            }
            return null;
        }
    }
}