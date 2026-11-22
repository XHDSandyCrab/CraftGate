using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using CraftGate.Services;

namespace CraftGate.Core.Game
{
    public static class AuthlibService
    {
        private static readonly HttpClient _client = new HttpClient();
        private static readonly string AuthlibJarPath = Path.Combine(PathManager.LauncherDir, "authlib-injector.jar");

        public static async Task EnsureDownloadedAsync(Action<string> logCallback)
        {
            if (File.Exists(AuthlibJarPath)) return;

            try
            {
                logCallback("[皮肤] 正在下载底层换肤组件 (Authlib-Injector)...");

                // 👇 修复：修正了正确的 Maven 坐标，并提供了两个备用地址
                string[] urls = {
                    "https://bmclapi2.bangbang93.com/maven/org/yggdrasil/authlib-injector/1.2.5/authlib-injector-1.2.5.jar",
                    "https://ghproxy.net/https://github.com/yushijinhun/authlib-injector/releases/download/v1.2.5/authlib-injector-1.2.5.jar"
                };

                bool success = false;
                foreach (var url in urls)
                {
                    try
                    {
                        var data = await _client.GetByteArrayAsync(url).ConfigureAwait(false);
                        await Task.Run(() => File.WriteAllBytes(AuthlibJarPath, data)).ConfigureAwait(false);
                        success = true;
                        break;
                    }
                    catch { }
                }

                if (success)
                {
                    logCallback("[皮肤] 换肤组件下载完成！");
                }
                else
                {
                    logCallback("[警告] 所有下载源均失败，请检查网络。");
                }
            }
            catch (Exception ex)
            {
                logCallback("[警告] 下载换肤组件失败: " + ex.Message);
            }
        }

        public static string GetJvmArgs()
        {
            if (!File.Exists(AuthlibJarPath))
                return string.Empty;

            string localApiRoot = "http://127.0.0.1:25565/api/yggdrasil";
            return $"-javaagent:\"{AuthlibJarPath}\"={localApiRoot}";
        }
    }
}