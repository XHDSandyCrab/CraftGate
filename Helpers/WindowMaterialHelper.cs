using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace CraftGate.Helpers
{
    /// <summary>
    /// 通过 DWM API 为窗口设置系统背景材质（Mica / Acrylic / Mica Alt）。
    /// 这些材质由 DWM 在窗口后方绘制，要求窗口背景透明（本 MainWindow 已是 Transparent）。
    /// </summary>
    public static class WindowMaterialHelper
    {
        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out int attrValue, int attrSize);

        // RtlGetVersion：直接读系统真实版本，不受 app.manifest / Environment.OSVersion 的兼容层谎言影响
        [DllImport("ntdll.dll", EntryPoint = "RtlGetVersion")]
        private static extern int RtlGetVersion(ref OSVERSIONINFOEX info);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct OSVERSIONINFOEX
        {
            public uint dwOSVersionInfoSize;
            public uint dwMajorVersion;
            public uint dwMinorVersion;
            public uint dwBuildNumber;
            public uint dwPlatformId;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string szCSDVersion;
            public ushort wServicePackMajor;
            public ushort wServicePackMinor;
            public ushort wSuiteMask;
            public byte wProductType;
            public byte wReserved;
        }

        // DWMWA_SYSTEMBACKDROP_TYPE = 38（微软文档确认）
        private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;

        // DWM_SYSTEMBACKDROP_TYPE 枚举（微软文档确认，从 0 开始）
        public const int DWMSBT_AUTO = 0;
        public const int DWMSBT_NONE = 1;      // 原始：不绘制任何系统材质
        public const int DWMSBT_MICA = 2;      // 云母：MAINWINDOW
        public const int DWMSBT_ACRYLIC = 3;   // 亚克力：TRANSIENTWINDOW
        public const int DWMSBT_MICA_ALT = 4;  // 毛玻璃：TABBEDWINDOW（Mica Alt）

        // DWMWA_SYSTEMBACKDROP_TYPE 自 Windows 11 Build 22621 起支持
        private const int MIN_SUPPORTED_BUILD = 22621;

        private static int GetRealBuildNumber()
        {
            try
            {
                var info = new OSVERSIONINFOEX();
                info.dwOSVersionInfoSize = (uint)Marshal.SizeOf(typeof(OSVERSIONINFOEX));
                if (RtlGetVersion(ref info) == 0) return (int)info.dwBuildNumber;
            }
            catch { }
            return 0;
        }

        /// <summary>
        /// 当前系统是否支持 DWM 系统背景材质（Win11 22H2 / Build 22621+）。
        /// 用 RtlGetVersion 读取真实版本，避免 Environment.OSVersion 在无 manifest 时谎报 6.2。
        /// </summary>
        public static bool IsSupported()
        {
            return GetRealBuildNumber() >= MIN_SUPPORTED_BUILD;
        }

        /// <summary>
        /// 应用系统背景材质。
        /// 判定成败以 API 实际返回值为准，不依赖版本号（版本只用于 IsSupported() 的信息展示）。
        /// </summary>
        /// <param name="window">目标窗口</param>
        /// <param name="backdropType">DWMSBT_* 常量之一</param>
        /// <returns>true=成功；false=系统不支持或调用失败</returns>
        public static bool Apply(Window window, int backdropType)
        {
            IntPtr hwnd = new WindowInteropHelper(window).EnsureHandle();
            if (hwnd == IntPtr.Zero) return false;

            int value = backdropType;
            int hr = DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref value, sizeof(int));

            // SUCCEEDED(hr) == hr >= 0；低版本系统不支持该 attribute 会返回失败 HRESULT
            if (hr < 0) return false;

            // 回读确认，防止某些系统"调用成功但实际没生效"
            int actual = -1;
            int getHr = DwmGetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, out actual, sizeof(int));
            return getHr >= 0 && actual == backdropType;
        }
    }
}
