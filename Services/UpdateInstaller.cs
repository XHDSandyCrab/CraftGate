using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace CraftGate.Services
{
    /// <summary>
    /// 自动更新安装器（PCL2 风格）：
    /// ① 下载新版 exe 到临时目录；
    /// ② 生成一个 .bat，等待启动器进程退出后，用新 exe 覆盖旧 exe 并重新启动；
    /// ③ 启动 .bat（隐藏窗口）→ 关闭启动器 → bat 接管完成替换。
    /// Windows 下无法覆盖正在运行的 exe，所以必须借助外部脚本在进程退出后替换。
    /// </summary>
    public static class UpdateInstaller
    {
        private static readonly HttpClient _client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };

        /// <summary>下载新版 exe。progress: 0~100。返回保存路径。</summary>
        public static async Task<string> DownloadAsync(string url, string fileName, Action<double, string> progress)
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "CraftGateUpdate");
            Directory.CreateDirectory(tempDir);
            string savePath = Path.Combine(tempDir, fileName ?? "CraftGate_new.exe");

            using (var response = await _client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                long total = response.Content.Headers.ContentLength ?? 0;
                using (var src = await response.Content.ReadAsStreamAsync())
                using (var dst = new FileStream(savePath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
                {
                    var buffer = new byte[81920];
                    long read = 0;
                    int n;
                    while ((n = await src.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        await dst.WriteAsync(buffer, 0, n);
                        read += n;
                        double pct = total > 0 ? (double)read / total * 100 : 0;
                        progress?.Invoke(pct, $"{read / 1024 / 1024} / {total / 1024 / 1024} MB");
                    }
                }
            }
            return savePath;
        }

        /// <summary>启动更新脚本并关闭当前启动器。脚本会等本进程退出后替换 exe 并重启。</summary>
        public static void ApplyAndExit(string newExePath)
        {
            string currentExe = Process.GetCurrentProcess().MainModule.FileName;
            int pid = Process.GetCurrentProcess().Id;
            string batPath = Path.Combine(Path.GetTempPath(), "CraftGate_update.bat");

            // 脚本逻辑：等本 PID 退出 → 覆盖 exe → 重启 → 自删
            // 用 chcp 65001 支持中文路径，延时用 ping 兼容所有 Windows 版本
            string bat =
                "@echo off\r\n" +
                "chcp 65001 >nul\r\n" +
                ":wait\r\n" +
                $"tasklist /FI \"PID eq {pid}\" 2>nul | find /I \"{pid}\" >nul\r\n" +
                "if errorlevel 1 goto replace\r\n" +
                "ping -n 2 127.0.0.1 >nul\r\n" +
                "goto wait\r\n" +
                ":replace\r\n" +
                "ping -n 2 127.0.0.1 >nul\r\n" +
                "copy /Y \"" + newExePath + "\" \"" + currentExe + "\" >nul\r\n" +
                "if errorlevel 1 goto done\r\n" +
                "start \"\" \"" + currentExe + "\"\r\n" +
                ":done\r\n" +
                "del \"%~f0\" >nul\r\n";

            File.WriteAllText(batPath, bat, Encoding.UTF8);

            var psi = new ProcessStartInfo(batPath)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            Process.Start(psi);

            // 关闭启动器，把替换工作交给 bat
            Application.Current?.Shutdown();
        }
    }
}
