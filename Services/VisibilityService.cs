using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace CraftGate.Services
{
    /// <summary>
    /// 全局窗口可见性服务：集中判断"用户当前是否正在看这个应用"。
    /// 综合依据：窗口最小化 / 隐藏 / 应用失去焦点（前台窗口不属于本进程）。
    /// 各后台模块（定时器、渲染动画、网络拉取）订阅 VisibilityChanged，
    /// 不可见时暂停、可见时恢复，从而省 CPU / 省流量。
    /// </summary>
    public static class VisibilityService
    {
        private static bool _isAppVisible = true;
        private static bool _initialized;
        private static Window _window;

        /// <summary>用户当前是否正在看这个应用（可见且有焦点）。</summary>
        public static bool IsAppVisible
        {
            get => _isAppVisible;
            private set
            {
                if (_isAppVisible == value) return;
                _isAppVisible = value;
                VisibilityChanged?.Invoke(value);
                // 回到可见时补跑不可见期间暂存的自动拉取
                if (value) FlushPending();
            }
        }

        /// <summary>可见性变化事件：参数 true=变为可见，false=变为不可见。</summary>
        public static event Action<bool> VisibilityChanged;

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentProcessId();

        /// <summary>在 MainWindow 构造后调用一次，挂上所有可见性钩子。</summary>
        public static void Initialize(Window window)
        {
            if (_initialized || window == null) return;
            _initialized = true;
            _window = window;

            window.Activated += (_, __) => Refresh();
            window.Deactivated += (_, __) => Refresh();
            window.StateChanged += (_, __) => Refresh();
            window.IsVisibleChanged += (_, __) => Refresh();

            // 窗口句柄创建后再刷一次（构造函数阶段句柄可能尚未就绪）
            if (window.IsInitialized)
                window.Dispatcher.BeginInvoke(new Action(Refresh), DispatcherPriority.Loaded);
            else
                window.Loaded += (_, __) => Refresh();

            Refresh();
        }

        /// <summary>重新计算可见性。任何钩子触发都走这里。</summary>
        public static void Refresh()
        {
            var window = _window ?? Application.Current?.MainWindow;
            if (window == null) return;

            bool visible = window.IsVisible && window.WindowState != WindowState.Minimized;
            if (visible)
                visible = IsForegroundOurProcess();

            // 强制同步到 UI 线程，避免非 UI 线程调用时事件处理出错
            var dispatcher = window.Dispatcher;
            if (dispatcher.CheckAccess())
                IsAppVisible = visible;
            else
                dispatcher.Invoke(new Action(() => IsAppVisible = visible));
        }

        /// <summary>
        /// 调度一个自动后台动作：窗口可见时立即执行；不可见时暂存，
        /// 等重新可见时只执行最后一次登记的动作（避免堆积多次网络请求）。
        /// 适合页面初始化/刷新这类"自动拉取"，不触碰用户主动发起的下载。
        /// </summary>
        private static Action _pendingAction;
        public static void RunWhenVisible(Action action)
        {
            if (action == null) return;
            if (IsAppVisible)
            {
                action();
                return;
            }
            // 不可见：只保留最后一个待办，恢复时执行
            _pendingAction = action;
        }

        private static void FlushPending()
        {
            var pending = _pendingAction;
            _pendingAction = null;
            pending?.Invoke();
        }

        /// <summary>前台窗口是否属于本进程（判断用户是否切到了别的应用）。</summary>
        private static bool IsForegroundOurProcess()
        {
            try
            {
                IntPtr fg = GetForegroundWindow();
                if (fg == IntPtr.Zero) return false;
                GetWindowThreadProcessId(fg, out uint fgPid);
                return fgPid == GetCurrentProcessId();
            }
            catch
            {
                // 取前台窗口失败时保守认为可见，避免误暂停
                return true;
            }
        }
    }
}
