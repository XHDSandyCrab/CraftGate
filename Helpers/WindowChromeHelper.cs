using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace CraftGate.Helpers
{
    public static class WindowChromeHelper
    {
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr handle, int flags);

        [DllImport("user32.dll")]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        // ---- 用于恢复 WindowStyle=None 抹掉的窗口样式位（DWM 动画依赖它们）----
        [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
        private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
        private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
        private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
        private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
            int x, int y, int cx, int cy, uint uFlags);

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int left, top, right, bottom; }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int x, y; }

        [StructLayout(LayoutKind.Sequential)]
        public struct MINMAXINFO
        {
            public POINT ptReserved;
            public POINT ptMaxSize;
            public POINT ptMaxPosition;
            public POINT ptMinTrackSize;
            public POINT ptMaxTrackSize;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        public struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        private const int WM_NCCALCSIZE = 0x0083;
        private const int WM_GETMINMAXINFO = 0x0024;
        private const int MONITOR_DEFAULTTONEAREST = 0x00000002;

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;

        private const int GWL_STYLE = -16;
        private const int WS_CAPTION = 0x00C00000;
        private const int WS_SYSMENU = 0x00080000;
        private const int WS_THICKFRAME = 0x00040000;
        private const int WS_MINIMIZEBOX = 0x00020000;
        private const int WS_MAXIMIZEBOX = 0x00010000;

        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_FRAMECHANGED = 0x0020;

        public static void Apply(Window window, bool isDarkMode = true)
        {
            var helper = new WindowInteropHelper(window);
            helper.EnsureHandle();
            IntPtr hwnd = helper.Handle;

            HwndSource source = HwndSource.FromHwnd(hwnd);
            source.AddHook(WndProc);

            ApplyAppearance(hwnd, isDarkMode);
        }

        /// <summary>
        /// 仅设置 DWM 外观（深色模式边框 + Win11 圆角），不接管任何窗口消息。
        /// 适用于已经由原生 WindowChrome 接管非客户区的窗口——两者职责不重叠，不会冲突。
        /// </summary>
        public static void ApplyAppearance(IntPtr hwnd, bool isDarkMode = true)
        {
            // 1. 开启深色模式边框
            int darkMode = isDarkMode ? 1 : 0;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));

            // 2. 强制要求系统绘制平滑圆角
            if (Environment.OSVersion.Version.Build >= 22000)
            {
                int cornerPref = 2; // DWMWCP_ROUND = 2
                DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPref, sizeof(int));
            }
        }

        private static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex)
        {
            return IntPtr.Size == 8
                ? GetWindowLongPtr64(hWnd, nIndex)
                : new IntPtr(GetWindowLong32(hWnd, nIndex));
        }

        private static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
        {
            return IntPtr.Size == 8
                ? SetWindowLongPtr64(hWnd, nIndex, dwNewLong)
                : new IntPtr(SetWindowLong32(hWnd, nIndex, dwNewLong.ToInt32()));
        }

        /// <summary>
        /// 恢复 WindowStyle="None" 抹掉的窗口样式位（缩放/最小化/最大化所需）。
        ///
        /// 【定案】刻意【不】加回 WS_CAPTION / WS_SYSMENU：
        /// 在 GlassFrameThickness="-1"（DWM 系统材质与圆角必需）下，
        /// WS_CAPTION 会让系统绘制原生标题栏，与自绘标题栏叠加产生双标题栏重影。
        /// 经多轮实验验证四者不可兼得，最终取舍 = 材质 + 圆角 + 无重影，放弃系统窗口动画。
        /// 只保留 WS_THICKFRAME / WS_MINIMIZEBOX / WS_MAXIMIZEBOX，不影响材质与圆角。
        /// </summary>
        public static void RestoreWindowAnimations(IntPtr hwnd)
        {
            const int restoredStyles = WS_THICKFRAME | WS_MINIMIZEBOX | WS_MAXIMIZEBOX;

            IntPtr stylePtr = GetWindowLongPtr(hwnd, GWL_STYLE);
            long style = stylePtr.ToInt64();
            style |= restoredStyles;

            // 确保系统不画原生标题栏（无重影）
            style &= ~WS_CAPTION;
            style &= ~WS_SYSMENU;

            SetWindowLongPtr(hwnd, GWL_STYLE, new IntPtr(style));

            // 让 DWM 重新计算非客户区帧，否则改动不会立即生效。
            SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
        }

        /// <summary>
        /// 只接管 WM_GETMINMAXINFO，让窗口最大化时停在工作区、露出任务栏。
        /// 刻意不处理 WM_NCCALCSIZE——那是原生 WindowChrome 的职责，碰了会冲突。
        /// 可安全地与 XAML 中的原生 WindowChrome 共存。
        /// </summary>
        public static void ApplyMaximizeBounds(IntPtr hwnd)
        {
            HwndSource source = HwndSource.FromHwnd(hwnd);
            if (source != null && !source.IsDisposed)
            {
                source.AddHook(MaximizeBoundsHook);
            }
        }

        private static IntPtr MaximizeBoundsHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_GETMINMAXINFO)
            {
                WmGetMinMaxInfo(hwnd, lParam);
                handled = true;
            }
            return IntPtr.Zero;
        }

        private static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            switch (msg)
            {
                case WM_NCCALCSIZE:
                    // 去掉原生标题栏，但通过 DWM 保留圆角和系统动画
                    if (wParam != IntPtr.Zero)
                    {
                        handled = true;
                        return IntPtr.Zero;
                    }
                    break;

                case WM_GETMINMAXINFO:
                    // 处理最大化时的边界，确保露出任务栏
                    WmGetMinMaxInfo(hwnd, lParam);
                    handled = true;
                    break;
            }
            return IntPtr.Zero;
        }

        private static void WmGetMinMaxInfo(IntPtr hwnd, IntPtr lParam)
        {
            MINMAXINFO mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);
            IntPtr monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);

            if (monitor != IntPtr.Zero)
            {
                MONITORINFO monitorInfo = new MONITORINFO();
                monitorInfo.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
                GetMonitorInfo(monitor, ref monitorInfo);

                RECT rcWork = monitorInfo.rcWork;
                RECT rcMonitor = monitorInfo.rcMonitor;

                mmi.ptMaxPosition.x = rcWork.left - rcMonitor.left;
                mmi.ptMaxPosition.y = rcWork.top - rcMonitor.top;
                mmi.ptMaxSize.x = rcWork.right - rcWork.left;
                mmi.ptMaxSize.y = rcWork.bottom - rcWork.top;

                mmi.ptMinTrackSize.x = 1000;
                mmi.ptMinTrackSize.y = 640;
            }
            Marshal.StructureToPtr(mmi, lParam, true);
        }
    }
}