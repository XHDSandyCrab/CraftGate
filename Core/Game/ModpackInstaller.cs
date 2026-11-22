using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using CraftGate.Services;
using Newtonsoft.Json.Linq;

namespace CraftGate.Core.Game
{
    public static class ModpackInstaller
    {
        private static readonly HttpClient _client = new HttpClient();

        public static async Task<bool> InstallModpackAsync(string mrpackPath, string targetVersionName, Action<string, double> progressCallback)
        {
            try
            {
                progressCallback?.Invoke($"[整合包] 开始安装: {Path.GetFileName(mrpackPath)}", 0);

                // 1. 创建隔离目录
                string versionDir = Path.Combine(PathManager.VersionsDir, targetVersionName);
                Directory.CreateDirectory(versionDir);
                progressCallback?.Invoke($"[路径] 游戏实例将保存至: {versionDir}", 2);

                string tempExtractDir = Path.Combine(PathManager.CacheDir, "ModpackTemp", Guid.NewGuid().ToString());
                Directory.CreateDirectory(tempExtractDir);

                // 2. 解压 .mrpack
                progressCallback?.Invoke("[整合包] 正在解压清单文件...", 5);
                ZipFile.ExtractToDirectory(mrpackPath, tempExtractDir);

                string indexJsonPath = Path.Combine(tempExtractDir, "modrinth.index.json");
                if (!File.Exists(indexJsonPath)) return false;

                // 3. 解析 JSON 获取版本信息
                var indexObj = JObject.Parse(File.ReadAllText(indexJsonPath));
                string mcVersion = indexObj["dependencies"]?["minecraft"]?.ToString();
                string fabricVersion = indexObj["dependencies"]?["fabric-loader"]?.ToString();

                progressCallback?.Invoke($"[整合包] 目标 MC 版本: {mcVersion}", 8);

                // 👇 核心补丁 1：检查并自动下载原版游戏核心（如果没有的话）
                string vanillaDir = Path.Combine(PathManager.VersionsDir, mcVersion);
                string vanillaJar = Path.Combine(vanillaDir, $"{mcVersion}.jar");
                if (!File.Exists(vanillaJar))
                {
                    progressCallback?.Invoke($"[原版] 本地缺少 {mcVersion} 核心，正在自动拉取...", 10);
                    // 从 Mojang 获取原版 URL 并调用 VersionDownloader
                    string versionUrl = await GetVanillaVersionUrlAsync(mcVersion);
                    if (!string.IsNullOrEmpty(versionUrl))
                    {
                        await VersionDownloader.DownloadVanillaAsync(mcVersion, versionUrl, (msg, p) =>
                        {
                            progressCallback?.Invoke(msg, 10 + p * 0.2); // 占用 10%~30% 的进度
                        });
                    }
                }

                // 👇 核心补丁 2：如果整合包需要 Fabric，自动下载 Fabric 核心
                if (!string.IsNullOrEmpty(fabricVersion))
                {
                    progressCallback?.Invoke($"[Fabric] 正在自动安装 Fabric 加载器: {fabricVersion}", 32);
                    await InstallFabricAsync(mcVersion, fabricVersion, targetVersionName, progressCallback);
                }

                // 4. 下载所有 Mod 文件 (分配 40%~90% 的进度)
                var files = indexObj["files"] as JArray;
                if (files != null && files.Count > 0)
                {
                    int totalFiles = files.Count;
                    int completed = 0;
                    progressCallback?.Invoke($"[整合包] 共有 {totalFiles} 个 Mod 文件需要下载...", 40);

                    foreach (var file in files)
                    {
                        string downloadUrl = file["downloads"]?.FirstOrDefault()?.ToString();
                        string relativePath = file["path"]?.ToString();
                        if (string.IsNullOrEmpty(downloadUrl) || string.IsNullOrEmpty(relativePath)) continue;

                        string savePath = Path.Combine(versionDir, relativePath.Replace('/', '\\'));
                        await ModrinthService.DownloadFileAsync(downloadUrl, Path.GetDirectoryName(savePath), Path.GetFileName(savePath), p =>
                        {
                            double totalPercent = 40 + ((completed * 100.0 / totalFiles) * 0.5);
                            progressCallback?.Invoke($"[下载] {Path.GetFileName(relativePath)} ({completed + 1}/{totalFiles})", totalPercent);
                        });
                        completed++;
                    }
                }

                // 5. 解压 overrides (配置和材质包)
                string overridesDir = Path.Combine(tempExtractDir, "overrides");
                if (Directory.Exists(overridesDir))
                {
                    progressCallback?.Invoke("[整合包] 正在覆盖配置和资源文件...", 95);
                    foreach (var file in Directory.GetFiles(overridesDir, "*.*", SearchOption.AllDirectories))
                    {
                        string relativePath = file.Substring(overridesDir.Length + 1);
                        string dest = Path.Combine(versionDir, relativePath);
                        Directory.CreateDirectory(Path.GetDirectoryName(dest));
                        File.Copy(file, dest, true);
                    }
                }

                try { Directory.Delete(tempExtractDir, true); } catch { }
                progressCallback?.Invoke("[整合包] 全部安装完成！去启动页启动体验吧！", 100);
                return true;
            }
            catch (Exception ex)
            {
                progressCallback?.Invoke($"[错误] 整合包安装失败: {ex.Message}", 0);
                return false;
            }
        }

        // 辅助方法：获取原版 JSON 的 URL
        private static async Task<string> GetVanillaVersionUrlAsync(string mcVersion)
        {
            try
            {
                string manifestJson = await _client.GetStringAsync("https://launchermeta.mojang.com/mc/game/version_manifest_v2.json");
                var manifest = JObject.Parse(manifestJson);
                var versionObj = manifest["versions"]?.FirstOrDefault(v => v["id"]?.ToString() == mcVersion);
                return versionObj?["url"]?.ToString();
            }
            catch { return null; }
        }

        // 辅助方法：下载并安装 Fabric
        private static async Task<bool> InstallFabricAsync(string mcVersion, string fabricVersion, string targetVersionName, Action<string, double> progressCallback)
        {
            try
            {
                string apiUrl = $"https://meta.fabricmc.net/v2/versions/loader/{mcVersion}/{fabricVersion}/profile/json";
                string profileJson = await _client.GetStringAsync(apiUrl);
                var profileObj = JObject.Parse(profileJson);
                var libraries = profileObj["libraries"] as JArray;

                string versionDir = Path.Combine(PathManager.VersionsDir, targetVersionName);
                string finalJsonPath = Path.Combine(versionDir, $"{targetVersionName}.json");

                // 修改 JSON 内的 id 字段，保持一致
                profileObj["id"] = targetVersionName;
                File.WriteAllText(finalJsonPath, profileObj.ToString());

                if (libraries != null)
                {
                    int totalLibs = libraries.Count;
                    int completed = 0;
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

                            await ModrinthService.DownloadFileAsync(downloadUrl, Path.GetDirectoryName(savePath), Path.GetFileName(savePath));
                            completed++;
                            double progress = 32 + (completed * 8.0 / totalLibs);
                            progressCallback?.Invoke($"[Fabric] 依赖库 {completed}/{totalLibs}", progress);
                        }
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                progressCallback?.Invoke($"[Fabric] 安装失败: {ex.Message}", 0);
                return false;
            }
        }
    }
}