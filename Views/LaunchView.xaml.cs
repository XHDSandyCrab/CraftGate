using System;
using System.Collections.Specialized;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using CraftGate.ViewModels;
using CraftGate.Services;

namespace CraftGate.Views
{
    public partial class LaunchView : UserControl
    {
        private readonly LaunchViewModel _vm = new LaunchViewModel();

        public LaunchView()
        {
            InitializeComponent();
            DataContext = _vm;

            _vm.Logs.CollectionChanged += OnLogsChanged;
            Loaded += OnLoaded;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            // 每次进入启动页都重新扫描一次本地版本，防止刚下载完没刷新
            _vm.ScanVersions();
            await _vm.InitializeAsync();
        }

        private void OnLogsChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(() => LogPanel.ScrollToEnd()), DispatcherPriority.Background);
        }

        private void TabLogs_Checked(object sender, RoutedEventArgs e)
        {
            if (LogPanel == null || ModsPanel == null) return;
            LogPanel.Visibility = Visibility.Visible;
            ModsPanel.Visibility = Visibility.Collapsed;
            CopyLogBtn.Visibility = Visibility.Visible;
        }

        private async void TabMods_Checked(object sender, RoutedEventArgs e)
        {
            if (LogPanel == null || ModsPanel == null) return;
            LogPanel.Visibility = Visibility.Collapsed;
            ModsPanel.Visibility = Visibility.Visible;
            CopyLogBtn.Visibility = Visibility.Collapsed;
            await RefreshModsAsync();
        }

        /// <summary>扫描当前账户+版本的模组列表（账户切换后由 MainWindow 调用）</summary>
        public async System.Threading.Tasks.Task RefreshModsAsync()
        {
            if (ModsCountText == null || _vm == null) return;
            // 扫描当前账户的 mods 目录（中文名优先显示）
            ModsCountText.Text = "正在扫描已安装模组...";
            try
            {
                string gameDir = PathManager.GetCurrentAccountGameDir(_vm.SelectedVersion);
                var mods = await InstalledModService.ScanAsync(gameDir);
                ModsList.ItemsSource = mods;
                if (mods.Count > 0)
                {
                    ModsCountText.Text = $"共 {mods.Count} 个模组（中文名优先，未收录显示原名）";
                }
                else
                {
                    // 版本隔离下当前版本没模组：检查是不是装到了别的版本目录，给出明确提示
                    string hint = "未找到已安装模组（mods 文件夹为空）";
                    if (ConfigManager.Settings.EnableVersionIsolation && Directory.Exists(PathManager.VersionsDir))
                    {
                        var otherVersions = new System.Collections.Generic.List<string>();
                        foreach (var dir in Directory.GetDirectories(PathManager.VersionsDir))
                        {
                            if (string.Equals(Path.GetFileName(dir), _vm.SelectedVersion, StringComparison.OrdinalIgnoreCase))
                                continue;
                            if (Directory.Exists(Path.Combine(dir, "mods")) &&
                                Directory.GetFiles(Path.Combine(dir, "mods"), "*.jar").Length > 0)
                                otherVersions.Add(Path.GetFileName(dir));
                        }
                        if (otherVersions.Count > 0)
                            hint = $"当前版本无模组；[{string.Join("、", otherVersions)}] 有模组，请在上方切换版本查看";
                    }
                    ModsCountText.Text = hint;
                }
            }
            catch (Exception ex)
            {
                ModsCountText.Text = "扫描失败: " + ex.Message;
            }
        }

        private void CopyLogBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_vm.Logs == null || _vm.Logs.Count == 0) return;

            try
            {
                string logText = string.Join(Environment.NewLine, _vm.Logs);
                Clipboard.SetText(logText);

                if (Application.Current.MainWindow is MainWindow mainWin)
                {
                    mainWin.ShowToastSuccess("所有运行日志已复制到剪贴板！");
                }
            }
            catch (Exception ex)
            {
                if (Application.Current.MainWindow is MainWindow mainWin)
                {
                    mainWin.ShowToastError("复制失败: " + ex.Message);
                }
            }
        }
    }
}