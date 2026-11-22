using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CraftGate.Models;
using CraftGate.ViewModels;

namespace CraftGate.Views
{
    public partial class ResourceDownloadView : UserControl
    {
        private double _targetOffset = 0;
        private double _currentOffset = 0;
        private bool _isRendering = false;
        private ScrollViewer _scrollViewer;

        public ResourceDownloadView()
        {
            InitializeComponent();
            DataContext = new ResourceDownloadViewModel();
        }

        private void SearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && DataContext is ResourceDownloadViewModel vm)
            {
                vm.SearchCommand.Execute(null);
            }
        }

        private void CardContent_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is Grid grid && grid.DataContext is ModrinthProject project)
            {
                if (DataContext is ResourceDownloadViewModel vm)
                {
                    vm.ShowVersionPageCommand.Execute(project);
                }
            }
        }

        private void VersionItem_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border && border.DataContext is ModrinthVersion version)
            {
                if (DataContext is ResourceDownloadViewModel vm)
                {
                    vm.PrepareDownloadCommand.Execute(version);
                }
            }
        }

        private void ResultList_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (Math.Abs(e.Delta) < 120) return;
            e.Handled = true;

            if (_scrollViewer == null)
            {
                _scrollViewer = FindVisualChild<ScrollViewer>(ResultList);
                if (_scrollViewer != null)
                {
                    // 👇 核心：监听滚动事件，判断是否触底
                    _scrollViewer.ScrollChanged += ScrollViewer_ScrollChanged;
                }
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

        // 👇 触底检测
        private void ScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (sender is ScrollViewer sv)
            {
                // 距离底部 100 像素以内，且内容有高度，触发加载更多
                if (sv.ScrollableHeight > 0 && sv.VerticalOffset >= sv.ScrollableHeight - 100)
                {
                    if (DataContext is ResourceDownloadViewModel vm)
                    {
                        vm.LoadMoreCommand.Execute(null);
                    }
                }
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