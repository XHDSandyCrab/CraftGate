using System.Windows;
using CraftGate.Services;

namespace CraftGate
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            // 捕获未处理的 UI 异常并写日志，避免 WPF 静默崩溃难以排查
            DispatcherUnhandledException += (s, args) =>
            {
                try
                {
                    string log = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "CraftGate_crash.log");
                    var ex = args.Exception;
                    var sb = new System.Text.StringBuilder();
                    while (ex != null)
                    {
                        sb.AppendLine($"[{ex.GetType().Name}] {ex.Message}");
                        sb.AppendLine(ex.StackTrace);
                        sb.AppendLine("----");
                        ex = ex.InnerException;
                    }
                    System.IO.File.AppendAllText(log, $"=== {System.DateTime.Now} ===\n{sb}\n");
                }
                catch { }
            };
            // 远程/无GPU会话下 WPF 渲染线程可能静默失败（窗口响应但一帧不画 → 全黑）。
            // 设置环境变量 CRAFTGATE_SOFTWARE_RENDER=1 可强制软件渲染兜底；平时不设置则完全不影响。
            var forceSoftware = System.Environment.GetEnvironmentVariable("CRAFTGATE_SOFTWARE_RENDER");
            if (forceSoftware == "1")
                System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;

            base.OnStartup(e);

            PathManager.EnsureDirectoriesCreated();
            ConfigManager.Load();     // 👈 加载设置
            AccountService.Load();    // 👈 加载账户
        }
    }
}