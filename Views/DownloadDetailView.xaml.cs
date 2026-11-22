using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CraftGate.ViewModels;

namespace CraftGate.Views
{
    public partial class DownloadDetailView : UserControl
    {
        public event Action OnBackRequested;
        private DownloadDetailViewModel _vm;

        // 👇 构造函数必须接收两个参数
        public DownloadDetailView(string versionId, string versionUrl)
        {
            InitializeComponent();
            _vm = new DownloadDetailViewModel(versionId, versionUrl);
            DataContext = _vm;
            Loaded += async (_, __) => await _vm.InitializeAsync();
        }

        private void BtnBack_Click(object sender, RoutedEventArgs e) => OnBackRequested?.Invoke();
        private void VersionBack_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is DownloadDetailViewModel vm) vm.CloseVersionPageCommand.Execute(null);
        }

        // 点击图标：沿 VisualTree 向上找到 DataContext 为 WizardIconOption 的任意元素并选中
        private void IconPanel_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            var fe = e.OriginalSource as DependencyObject;
            while (fe != null && !(fe is FrameworkElement f && f.DataContext is CraftGate.Models.WizardIconOption))
                fe = VisualTreeHelper.GetParent(fe);
            if (fe is FrameworkElement el && el.DataContext is CraftGate.Models.WizardIconOption opt && _vm != null)
            {
                foreach (var o in _vm.IconOptions) o.IsSelected = false;
                opt.IsSelected = true;
            }
        }

        // 点击版本项：把字符串传给 VM
        private void VersionList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            var fe = e.OriginalSource as DependencyObject;
            while (fe != null && !(fe is FrameworkElement f && f.DataContext is string))
                fe = VisualTreeHelper.GetParent(fe);
            if (fe is FrameworkElement el && el.DataContext is string version && _vm != null)
                _vm.VersionItemClickCommand.Execute(version);
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