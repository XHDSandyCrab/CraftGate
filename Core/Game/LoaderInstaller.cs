using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using CraftGate.Services;
using Newtonsoft.Json.Linq;

namespace CraftGate.Core.Game
{
    public static class LoaderInstaller
    {
        private static readonly HttpClient _client = new HttpClient();

        // 👇 核心：下载并安装 Fabric 加载器
        public static async Task<string> InstallFabricAsync(string mcVersion, string loaderVersion, Action<string, double> progressCallback)
        {
            try
            {
                string newVersionName = $"{mcVersion}-fabric-{loaderVersion}";
                progressCallback?.Invoke($"[Fabric] 正在获取 Fabric {loaderVersion} 配置...", 0);

                string apiUrl = $"https://meta.fabricmc.net/v2/versions/loader/{mcVersion}/{loaderVersion}/profile/json";
                string profileJson = await _client.GetStringAsync(apiUrl);

                var profileObj = JObject.Parse(profileJson);
                var libraries = profileObj["libraries"] as JArray;

                string newVersionDir = Path.Combine(PathManager.VersionsDir, newVersionName);
                Directory.CreateDirectory(newVersionDir);

                // 修改 JSON 内的 id 字段，保持一致
                profileObj["id"] = newVersionName;
                profileObj["inheritsFrom"] = mcVersion; // 关键：告诉启动器去原版找核心

                string jsonPath = Path.Combine(newVersionDir, $"{newVersionName}.json");
                File.WriteAllText(jsonPath, profileObj.ToString());

                // 下载 Fabric 的依赖库
                if (libraries != null)
                {
                    int totalLibs = libraries.Count;
                    int completed = 0;
                    progressCallback?.Invoke($"[Fabric] 正在下载 {totalLibs} 个核心库...", 0);

                    foreach (var lib in libraries)
                    {
                        string artifactPath = lib["name"]?.ToString();
                        if (string.IsNullOrEmpty(artifactPath)) continue;

                        string[] parts = artifactPath.Split(':');
                        if (parts.Length >= 3)
                        {
                            string group = parts[0].Replace('.', '/');
                            string artifact = parts[1];
                            string version = parts[2];
                            string path = $"{group}/{artifact}/{version}/{artifact}-{version}.jar";
                            string savePath = Path.Combine(PathManager.LibrariesDir, path.Replace('/', '\\'));

                            string downloadUrl = $"https://maven.fabricmc.net/{path}";

                            // 复用之前写好的单文件下载器
                            await ModrinthService.DownloadFileAsync(downloadUrl, Path.GetDirectoryName(savePath), Path.GetFileName(savePath));

                            completed++;
                            double percent = (completed * 100.0 / totalLibs);
                            progressCallback?.Invoke($"[Fabric] 核心库 {completed}/{totalLibs}", percent);
                        }
                    }
                }

                progressCallback?.Invoke($"[Fabric] {newVersionName} 安装完成！", 100);
                return newVersionName; // 返回新生成的版本号，方便 UI 提示
            }
            catch (Exception ex)
            {
                progressCallback?.Invoke($"[错误] Fabric 安装失败: {ex.Message}", 0);
                return null;
            }
        }
    }
}