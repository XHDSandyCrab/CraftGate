using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using CraftGate.Controls;
using CraftGate.ViewModels;

namespace CraftGate.Views
{
    public partial class AccountEditView : UserControl
    {
        // 视口懒渲染：只保留当前可见（含少量预加载余量）的 3D 预览处于激活状态
        private readonly List<SkinPreview3D> _previews = new List<SkinPreview3D>();
        private bool _refreshQueued;
        private const double PreloadMargin = 120;   // 视口上下各多渲染 120px，滚动更顺

        public AccountEditView()
        {
            InitializeComponent();
            // 打开编辑页时自动聚焦名称输入框，确保能直接输入
            Loaded += (_, __) => Dispatcher.BeginInvoke(new System.Action(() =>
            {
                NameBox.Focus();
                NameBox.SelectAll();
                QueueRefreshVisible();
            }));
            // 布局/tab/分页变化后重新计算可见项
            LayoutUpdated += (_, __) => QueueRefreshVisible();
        }

        // 由 MainWindow 注入共享的 ViewModel
        public AccountEditView(AccountViewModel vm) : this()
        {
            DataContext = vm;
        }

        // 命名框回车 = 确定
        private void SkinNameBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter && DataContext is AccountViewModel vm)
            {
                if (vm.ConfirmSkinNameCommand.CanExecute(null))
                    vm.ConfirmSkinNameCommand.Execute(null);
                e.Handled = true;
            }
        }

        private void CapeSearchBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter && DataContext is AccountViewModel vm)
            {
                if (vm.SearchCapeCommand.CanExecute(null))
                    vm.SearchCapeCommand.Execute(null);
                e.Handled = true;
            }
        }

        private void SkinPreview_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is SkinPreview3D p && !_previews.Contains(p))
            {
                _previews.Add(p);
                p.Unloaded += (_, __) => _previews.Remove(p);
                QueueRefreshVisible();
            }
        }

        private void PageScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            QueueRefreshVisible();
        }

        // 节流：一帧内多次触发只算一次
        private void QueueRefreshVisible()
        {
            if (_refreshQueued) return;
            _refreshQueued = true;
            Dispatcher.BeginInvoke(new Action(RefreshVisible), DispatcherPriority.Background);
        }

        private void RefreshVisible()
        {
            _refreshQueued = false;
            if (!IsLoaded || PageScroll == null) return;

            double viewportH = PageScroll.ViewportHeight;
            if (viewportH <= 0) return;

            foreach (var p in _previews.ToArray())
            {
                if (!p.IsLoaded || p.Visibility != Visibility.Visible) continue;
                try
                {
                    // 控件相对 ScrollViewer 内容原点的垂直位置
                    var topLeft = p.TransformToAncestor(PageScroll).Transform(new Point(0, 0));
                    bool visible = topLeft.Y + p.ActualHeight > -PreloadMargin
                                   && topLeft.Y < viewportH + PreloadMargin;
                    // 只在状态变化时赋值，避免反复触发重建
                    if (p.IsActive != visible) p.IsActive = visible;
                }
                catch
                {
                    // 控件可能刚好被卸载/换页，跳过
                }
            }
        }
    }
}
