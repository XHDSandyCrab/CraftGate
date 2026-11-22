using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Interop;
using System.Windows.Shell;
using System.IO;
using System.Windows.Threading;
using Microsoft.Win32;
using CraftGate.Views;
using CraftGate.Services;
using CraftGate.Helpers;

namespace CraftGate
{
    public partial class MainWindow : Window
    {
        private LaunchView _launchView;
        private DownloadListView _downloadListView;
        private DownloadDetailView _downloadDetailView;
        private ResourceDownloadView _resourceDownloadView;
        private AccountView _accountView;
        private AccountEditView _accountEditView;
        private ViewModels.AccountViewModel _accountVm;
        private SettingsView _settingsView;
        private ToastNotification _currentToast;
        // morph 打开时记录通知位置，关闭时直接用它（通知可能已被移除，不能再 TransformToVisual）
        private Point _morphToastCenter;
        private double _morphToastWidth = 340;
        private double _morphToastHeight = 72;

        // 背景材质相关：是否处于图片背景模式
        private bool _isImageMode;
        private Brush _originalCardBrush;
        private Brush _originalCardBorderBrush;

        public MainWindow()
        {
            InitializeComponent();

            // 全局可见性服务：窗口不可见（最小化/失焦/切到别的应用）时，
            // 各后台模块自动暂停定时器、渲染动画与网络拉取，省 CPU / 省流量。
            VisibilityService.Initialize(this);

            Loaded += async (_, __) =>
            {
                ShowPage("launch");
                // 自动更新：仅在"自动更新开启且未暂停"时静默检查；暂停只影响这里，手动检查不受限。
                if (CraftGate.Services.ConfigManager.Settings.AutoUpdate && !CraftGate.Services.ConfigManager.Settings.PauseUpdate)
                {
                    var autoCheckTask = System.Threading.Tasks.Task.Run(async () =>
                    {
                        await System.Threading.Tasks.Task.Delay(3000); // 延迟3秒，避开启动高峰
                        var r = await CraftGate.Services.UpdateService.CheckAsync();
                        // 失败/无更新：完全静默，不弹任何错误通知
                        if (r.Success && r.HasUpdate)
                            Dispatcher.Invoke(() => ShowUpdateDialog(r));
                    });
                }
            };

            // 窗口句柄创建后：
            // ① 设置 DWM 外观（深色边框 + Win11 圆角）；
            // ② 接管 WM_GETMINMAXINFO，使最大化时停在工作区、露出任务栏；
            // ③ 窗口动画改由 XAML 上的 FluentWpfCore:DwmAnimation.EnableDwmAnimation 接管——
            //    它内部协调了"去原生标题栏 + 保留系统动画"，替代了原来手写的
            //    RestoreWindowAnimations（后者加回 WS_CAPTION 会导致双标题栏重影）。
            // 注意：不要再调用 RestoreWindowAnimations，避免两套逻辑重复操作样式位。
            SourceInitialized += (_, __) =>
            {
                IntPtr hwnd = new WindowInteropHelper(this).Handle;
                WindowChromeHelper.ApplyAppearance(hwnd);
                WindowChromeHelper.ApplyMaximizeBounds(hwnd);

                // 窗口动画：改用代码调用而非 XAML 附加属性。
                // XAML 上的 fluent:DwmAnimation.EnableDwmAnimation 在模板加载阶段执行，
                // 内部动画会碰到冻结对象，抛"无法对不可变对象(0),(1)进行动画处理"。
                // 放到这里（句柄已创建、窗口已初始化）调用，避开 XAML 解析阶段。
                try
                {
                    FluentWpfCore.AttachedProperties.DwmAnimation.EnableDwmAnimationHwnd(hwnd, ResizeMode);
                }
                catch { /* 动画启用失败不影响窗口主体功能 */ }

                // 启动时应用已保存的背景材质；不弹失败通知（避免一开程序就报错扰民）
                ApplyBackgroundMaterial(ConfigManager.Settings.BackgroundMaterial, silent: true);
            };

            DownloadCenter.OnBackRequested += () =>
            {
                var sb = new Storyboard();
                var scaleX = new DoubleAnimation(1, 0.1, TimeSpan.FromMilliseconds(300)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
                var scaleY = new DoubleAnimation(1, 0.1, TimeSpan.FromMilliseconds(300)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
                var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(300));

                Storyboard.SetTarget(scaleX, DownloadCenter);
                Storyboard.SetTargetProperty(scaleX, new PropertyPath("(UIElement.RenderTransform).(ScaleTransform.ScaleX)"));
                Storyboard.SetTarget(scaleY, DownloadCenter);
                Storyboard.SetTargetProperty(scaleY, new PropertyPath("(UIElement.RenderTransform).(ScaleTransform.ScaleY)"));
                Storyboard.SetTarget(fade, DownloadCenter);
                Storyboard.SetTargetProperty(fade, new PropertyPath("Opacity"));

                sb.Children.Add(scaleX);
                sb.Children.Add(scaleY);
                sb.Children.Add(fade);
                sb.Completed += (s, e) => DownloadCenter.Visibility = Visibility.Collapsed;
                sb.Begin();
            };
        }

        // ================== 窗口背景材质 ==================
        /// <summary>
        /// 应用窗口背景材质。material: None / Mica / Acrylic / MicaAlt / Image。
        /// silent=true 时不弹失败通知（用于启动时静默恢复）。
        /// </summary>
        public bool ApplyBackgroundMaterial(string material, bool silent = false)
        {
            if (string.IsNullOrEmpty(material)) material = "None";

            // 非"原始"材质需要让内容区透明，否则不透明背景会完全盖住材质/图片
            bool translucent = material != "None";
            _isImageMode = material == "Image";
            SetContentTransparency(translucent, _isImageMode);

            // 图片背景走 WPF 层，与 DWM 系统材质互斥
            if (material == "Image")
            {
                ImageBackground.Visibility = Visibility.Visible;
                LoadBackgroundImage(ConfigManager.Settings.BackgroundImagePath);

                // 关掉系统材质，避免叠加
                WindowMaterialHelper.Apply(this, WindowMaterialHelper.DWMSBT_NONE);
                return true;
            }

            // 非图片：隐藏图片层
            ImageBackground.Visibility = Visibility.Collapsed;
            ImageBackground.Background = null;

            int backdrop;
            switch (material)
            {
                case "Mica": backdrop = WindowMaterialHelper.DWMSBT_MICA; break;
                case "Acrylic": backdrop = WindowMaterialHelper.DWMSBT_ACRYLIC; break;
                case "MicaAlt": backdrop = WindowMaterialHelper.DWMSBT_MICA_ALT; break;
                case "None":
                default: backdrop = WindowMaterialHelper.DWMSBT_NONE; break;
            }

            // "原始"任何时候都能用，不算需要系统支持
            if (backdrop == WindowMaterialHelper.DWMSBT_NONE)
            {
                bool ok = WindowMaterialHelper.Apply(this, backdrop);
                if (!ok && !silent) ShowToastUnsupported("系统不支持设置窗口背景材质");
                return ok;
            }

            bool applied = WindowMaterialHelper.Apply(this, backdrop);
            if (!applied && !silent)
            {
                ShowToastUnsupported("当前系统不支持该背景材质（需 Windows 11 22H2 及以上）");
                // 回退到原始材质
                WindowMaterialHelper.Apply(this, WindowMaterialHelper.DWMSBT_NONE);
            }
            return applied;
        }

        /// <summary>
        /// 切换内容区背景：
        /// - 系统材质（云母/亚克力/毛玻璃）：导航与主内容都设为 Transparent，让 DWM 材质透出；
        /// - 图片模式：只有主内容区透明以显示图片，侧边栏保留原来的不透明深色（避免太透）；
        /// - 原始：都恢复为不透明深色。
        /// 各卡片自带背景，文字可读性由卡片保证。
        /// </summary>
        private void SetContentTransparency(bool translucent, bool imageMode = false)
        {
            // 侧边栏：系统材质时完全透明让材质透出；
            // 图片模式半透明磨砂（约 77%，与卡片一致）；原始时完全不透明
            LeftNavBorder.Background = (translucent && !imageMode)
                ? Brushes.Transparent
                : new SolidColorBrush(Color.FromArgb(imageMode ? (byte)0xC4 : (byte)0xFF, 0x1A, 0x1C, 0x21));

            // 主内容区：系统材质时完全透明；图片模式半透明磨砂；原始时不透明
            RightContentBorder.Background = imageMode
                ? new SolidColorBrush(Color.FromArgb(0xC4, 0x15, 0x16, 0x1A)) // 图片模式 ~77%
                : translucent
                    ? Brushes.Transparent
                    : new SolidColorBrush(Color.FromArgb(0xFF, 0x15, 0x16, 0x1A));

            // 卡片框：图片模式换成半透明磨砂，其余模式恢复原样
            ApplyCardTransparency(imageMode);
        }

        /// <summary>
        /// 图片模式下把卡片背景/边框画刷换成半透明（磨砂感），其它模式恢复原画刷。
        /// CardStyle 使用 DynamicResource，因此运行时替换资源即可全局生效。
        /// </summary>
        private void ApplyCardTransparency(bool frost)
        {
            var res = Application.Current.Resources;
            const string bgKey = "CardBackgroundBrush";
            const string borderKey = "CardBorderBrush";

            if (frost)
            {
                if (_originalCardBrush == null)
                {
                    _originalCardBrush = res[bgKey] as Brush;
                    _originalCardBorderBrush = res[borderKey] as Brush;
                }
                res[bgKey] = new SolidColorBrush(Color.FromArgb(0xC4, 0x1E, 0x20, 0x25));   // #1E2025 @77%
                res[borderKey] = new SolidColorBrush(Color.FromArgb(0xB3, 0x2A, 0x2D, 0x34)); // #2A2D34 @70%
            }
            else if (_originalCardBrush != null)
            {
                res[bgKey] = _originalCardBrush;
                res[borderKey] = _originalCardBorderBrush;
                _originalCardBrush = null;
                _originalCardBorderBrush = null;
            }
        }

        private void LoadBackgroundImage(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    ImageBackground.Background = null;
                    return;
                }

                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad; // 加载完释放文件锁
                bitmap.UriSource = new Uri(path, UriKind.Absolute);
                bitmap.EndInit();

                ImageBackground.Background = new ImageBrush
                {
                    ImageSource = bitmap,
                    Stretch = Stretch.UniformToFill,
                    AlignmentX = AlignmentX.Center,
                    AlignmentY = AlignmentY.Center
                };
            }
            catch
            {
                ImageBackground.Background = null;
            }
        }

        /// <summary>
        /// 弹出"系统不支持"的红色通知。
        /// </summary>
        private void ShowToastUnsupported(string message)
        {
            var toast = new ToastNotification();
            ToastContainer.Children.Add(toast);
            toast.ShowUnsupported(message);
        }

        // ================== 系统原生按钮命令 ==================
        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            // 👇 强制使用 System.Windows 下的 SystemCommands，避免 ControlzEx 冲突
            System.Windows.SystemCommands.MinimizeWindow(this);
        }

        private void Maximize_Click(object sender, RoutedEventArgs e)
        {
            if (WindowState == WindowState.Maximized)
                System.Windows.SystemCommands.RestoreWindow(this);
            else
                System.Windows.SystemCommands.MaximizeWindow(this);
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            System.Windows.SystemCommands.CloseWindow(this);
        }

        private void Window_StateChanged(object sender, EventArgs e)
        {
            // 【已移除旧的防重影过渡】原先这里会在状态变化时把内容临时切为不透明，
            // 再用 420ms 定时器恢复，导致每次最大化/还原都"闪回原始材质再变回来"。
            // 现在窗口动画由 FluentWpfCore 的 DwmAnimation 接管，它已协调好标题栏与动画，
            // 不再需要这段补丁——移除后材质在窗口状态变化全程保持稳定。

            // 原生 WindowChrome 最大化时，隐藏的 resize border 会溢出屏幕边缘，
            // 用等量 Margin 把内容收回，避免边缘被裁切。
            Thickness chromeBorder = WindowChrome.GetWindowChrome(this).ResizeBorderThickness;

            if (WindowState == WindowState.Maximized)
            {
                BtnMaximize.Content = "\uE923";
                RootBorder.BorderThickness = new Thickness(0);
                RootGrid.Margin = new Thickness(
                    chromeBorder.Left, chromeBorder.Top, chromeBorder.Right, chromeBorder.Bottom);
            }
            else
            {
                BtnMaximize.Content = "\uE922";
                RootBorder.BorderThickness = new Thickness(1);
                RootGrid.Margin = new Thickness(0);
            }
        }

        // ================== 页面与 Toast 逻辑 ==================
        /// <summary>显示通知。isDownload=true 表示下载类通知，点击可展开详情弹窗；只读通知不响应点击。</summary>
        public void ShowToast(string title, string message, bool isDownload = false)
        {
            if (_currentToast != null) ToastContainer.Children.Remove(_currentToast);
            _currentToast = new ToastNotification();
            _currentToast.IsDownload = isDownload;
            // 只读通知鼠标不变手型，明确告知"不可点开详情"
            ToastContainer.Cursor = isDownload ? System.Windows.Input.Cursors.Hand : System.Windows.Input.Cursors.Arrow;
            ToastContainer.Children.Add(_currentToast);
            _currentToast.Show(title, message);
        }

        public void UpdateToast(double progress, string message) => _currentToast?.UpdateProgress(progress, message);

        /// <summary>显示新版本提示弹窗（自动检查与手动检查共用）。</summary>
        public void ShowUpdateDialog(CraftGate.Services.UpdateService.UpdateResult result)
        {
            UpdatePromptDialog?.Show(result);
        }

        public void ShowToastSuccess(string message)
        {
            if (_currentToast == null)
            {
                _currentToast = new ToastNotification();
                ToastContainer.Children.Add(_currentToast);
                _currentToast.Show("操作成功", message);
            }
            // 成功是终态：即便之前是下载通知，也切成只读，避免点开空详情
            _currentToast.IsDownload = false;
            ToastContainer.Cursor = System.Windows.Input.Cursors.Arrow;
            _currentToast.ShowSuccess(message);
        }

        public void ShowToastError(string message)
        {
            if (_currentToast == null)
            {
                _currentToast = new ToastNotification();
                ToastContainer.Children.Add(_currentToast);
                _currentToast.Show("操作失败", message);
            }
            _currentToast.IsDownload = false;
            ToastContainer.Cursor = System.Windows.Input.Cursors.Arrow;
            _currentToast.ShowError(message);
        }

        public void HideToast()
        {
            bool popupOpen = ToastDetailPopup != null && ToastDetailPopup.IsOpen;
            if (_currentToast != null)
            {
                // 弹窗开着时通知已被形变接管并隐藏，不要再播它的滑出动画
                if (!popupOpen) _currentToast.Hide();
                _currentToast = null;
            }
            // 通知消失时若详情弹窗开着，一并收起
            if (popupOpen) CloseToastDetail();
        }

        // ================== 通知 → 详情弹窗 morph ==================
        private void ToastContainer_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            // 只有"下载类"通知才可点开详情；检查更新/成功/失败等只读通知不响应
            if (_currentToast != null && _currentToast.IsDownload && !ToastDetailPopup.IsOpen)
                OpenToastDetail();
        }

        private void OpenToastDetail()
        {
            if (_currentToast == null || ToastDetailPopup.IsOpen) return;

            // 先显示弹窗（透明），才能拿到实际尺寸做坐标换算
            ToastDetailPopup.Visibility = Visibility.Visible;

            // 通知中心点换算到内容区坐标系（ContentRootGrid 一直布局稳定），作为 morph 起点
            Point center = new Point(_currentToast.ActualWidth / 2, _currentToast.ActualHeight / 2);
            Point toastCenter = new Point(ContentRootGrid.ActualWidth - 200, ContentRootGrid.ActualHeight - 60);
            double toastWidth = _currentToast.ActualWidth > 0 ? _currentToast.ActualWidth : 340;
            try
            {
                GeneralTransform transform = _currentToast.TransformToVisual(ContentRootGrid);
                toastCenter = transform.Transform(center);
                // ContentRootGrid 原点在标题栏之上；弹窗在 Grid.Row=1，需减去标题栏高度
                toastCenter.Y -= TitleBar.ActualHeight;
            }
            catch { /* 通知不在可视树时用右下角兜底 */ }

            // 保存 morph 起点，关闭时复用（此时通知可能已被移除）
            _morphToastCenter = toastCenter;
            _morphToastWidth = toastWidth;
            _morphToastHeight = _currentToast.ActualHeight > 0 ? _currentToast.ActualHeight : 72;

            ToastDetailPopup.OpenFrom(toastCenter, toastWidth, _morphToastHeight);

            // 通知本体瞬间消失——视觉上由弹窗从它原位"变形"接管，而不是留下一份变灰的副本
            _currentToast.Visibility = Visibility.Collapsed;
            _morphToastHidden = _currentToast;
            // 主内容平滑模糊
            AnimateBlur(LeftNavBlur, 0, 6, 380);
            AnimateBlur(ContentBlur, 0, 8, 380);
        }

        // 形变期间被隐藏的通知；弹窗收起动画结束后恢复它
        private ToastNotification _morphToastHidden;

        /// <summary>弹窗收起动画结束时回调：让形变期间隐藏的通知重新出现。</summary>
        public void OnToastDetailClosed()
        {
            if (_morphToastHidden != null)
            {
                _morphToastHidden.Visibility = Visibility.Visible;
                _morphToastHidden.Opacity = 1;
                _morphToastHidden = null;
            }
            AnimateBlur(LeftNavBlur, LeftNavBlur.Radius, 0, 300);
            AnimateBlur(ContentBlur, ContentBlur.Radius, 0, 300);
        }

        /// <summary>点击弹窗外 / 程序内部触发：缩回到通知，恢复主内容，通知本体保留。</summary>
        public void CloseToastDetail()
        {
            if (ToastDetailPopup == null || !ToastDetailPopup.IsOpen) return;

            // 用打开时保存的位置，绝不重新 TransformToVisual——通知此时可能已被移除，
            // 与弹窗不再共享上级，调用会抛 InvalidOperationException（用户实测崩溃）。
            ToastDetailPopup.CloseTo(_morphToastCenter, _morphToastWidth, _morphToastHeight);
            // 通知恢复 + 模糊消失在弹窗收起动画结束时由 OnToastDetailClosed 统一处理，
            // 否则通知会在弹窗还没缩回去时就冒出来，破坏"形变连续"的观感。
        }

        private static void AnimateDouble(DependencyObject target, DependencyProperty prop,
                                          double from, double to, int ms)
        {
            var anim = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(ms))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.HoldEnd
            };
            var sb = new Storyboard();
            sb.Children.Add(anim);
            Storyboard.SetTarget(anim, target);
            Storyboard.SetTargetProperty(anim, new PropertyPath(prop));
            sb.Begin();
        }

        private static void AnimateBlur(BlurEffect blur, double from, double to, int ms)
        {
            var anim = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(ms))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.HoldEnd
            };
            blur.BeginAnimation(BlurEffect.RadiusProperty, anim);
        }

        private void Nav_Checked(object sender, RoutedEventArgs e)
        {
            if (ContentHost == null) return;
            if (DownloadCenter.Visibility == Visibility.Visible)
            {
                DownloadCenter.Visibility = Visibility.Collapsed;
            }
            if (sender is RadioButton rb && rb.Tag is string tag) ShowPage(tag);
        }

        private void ShowPage(string tag)
        {
            switch (tag)
            {
                case "launch":
                    if (_launchView == null) _launchView = new LaunchView();
                    if (_launchView.DataContext is ViewModels.LaunchViewModel launchVm) _ = launchVm.RefreshAccountAsync();
                    ContentHost.Content = _launchView;
                    break;
                case "download":
                    if (_downloadListView == null)
                    {
                        _downloadListView = new DownloadListView();
                        _downloadListView.OnVersionSelected += ShowDownloadDetail;
                    }
                    ContentHost.Content = _downloadListView;
                    break;
                case "resource":
                    if (_resourceDownloadView == null) _resourceDownloadView = new ResourceDownloadView();
                    if (_launchView != null && _launchView.DataContext is ViewModels.LaunchViewModel launchResVm)
                    {
                        if (_resourceDownloadView.DataContext is ViewModels.ResourceDownloadViewModel resVm)
                        {
                            resVm.SetMcVersion(launchResVm.SelectedVersion);
                        }
                    }
                    ContentHost.Content = _resourceDownloadView;
                    break;
                case "account":
                    if (_accountVm == null)
                    {
                        _accountVm = new ViewModels.AccountViewModel();
                        _accountVm.EditPageRequested += () => ShowPage("accountedit");
                        _accountVm.EditPageClosed += () => ShowPage("account");
                    }
                    if (_accountView == null) _accountView = new AccountView(_accountVm);
                    ContentHost.Content = _accountView;
                    break;
                case "accountedit":
                    if (_accountVm == null)
                    {
                        _accountVm = new ViewModels.AccountViewModel();
                        _accountVm.EditPageRequested += () => ShowPage("accountedit");
                        _accountVm.EditPageClosed += () => ShowPage("account");
                    }
                    if (_accountEditView == null) _accountEditView = new AccountEditView(_accountVm);
                    ContentHost.Content = _accountEditView;
                    break;
                case "settings":
                    if (_settingsView == null) _settingsView = new SettingsView();
                    ContentHost.Content = _settingsView;
                    break;
            }
        }

        public void SyncAccountToLaunchPage()
        {
            if (_launchView != null && _launchView.DataContext is ViewModels.LaunchViewModel launchVm)
            {
                _ = launchVm.RefreshAccountAsync();
                // 账户切换后存档/模组目录变了，重扫模组列表
                _ = _launchView.RefreshModsAsync();
            }
        }

        /// <summary>创建实例完成后刷新启动页版本列表</summary>
        public void RefreshVersions()
        {
            if (_launchView?.DataContext is ViewModels.LaunchViewModel launchVm)
                launchVm.ScanVersions();
        }

        private void ShowDownloadDetail(string versionId, string versionUrl)
        {
            _downloadDetailView = new DownloadDetailView(versionId, versionUrl);
            _downloadDetailView.OnBackRequested += () =>
            {
                ContentHost.Content = _downloadListView;
            };

            ContentHost.Content = _downloadDetailView;
        }
    }
}