using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using CraftGate.Models;
using CraftGate.Services;
using Newtonsoft.Json.Linq;

namespace CraftGate.Core.Game
{
    public static class VersionDownloader
    {
        private static readonly HttpClient _httpClient = new HttpClient();

        static VersionDownloader()
        {
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "CraftGate/1.0");
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
        }

        public static async Task<bool> DownloadVanillaAsync(string versionId, string versionUrl, Action<string, double> progressCallback)
        {
            try
            {
                progressCallback?.Invoke($"[开始] 正在获取版本信息 {versionId}...", 0);

                string versionDir = PathManager.GetVersionDir(versionId);
                Directory.CreateDirectory(versionDir);
                string jsonPath = Path.Combine(versionDir, $"{versionId}.json");

                string jsonText = await HttpService.GetStringAsync(versionUrl);
                File.WriteAllText(jsonPath, jsonText);
                progressCallback?.Invoke($"[解析] 正在分析全部所需文件...", 2);

                var jsonObj = JObject.Parse(jsonText);

                var allTasks = new List<FileDownloadTask>();

                string clientUrl = jsonObj["downloads"]?["client"]?["url"]?.ToString();
                long clientSize = jsonObj["downloads"]?["client"]?["size"]?.Value<long>() ?? 0;
                if (!string.IsNullOrEmpty(clientUrl))
                {
                    allTasks.Add(new FileDownloadTask { Url = clientUrl, SavePath = Path.Combine(versionDir, $"{versionId}.jar"), FileName = $"{versionId}.jar", FileSize = clientSize, Category = "游戏核心" });
                }

                if (jsonObj["libraries"] is JArray libraries)
                {
                    foreach (var lib in libraries)
                    {
                        if (lib["rules"] is JToken rules && !CheckRulesAllowed(rules)) continue;

                        if (lib["downloads"]?["artifact"] is JToken artifact)
                        {
                            string path = artifact["path"]?.ToString();
                            string url = artifact["url"]?.ToString();
                            long size = artifact["size"]?.Value<long>() ?? 0;
                            if (!string.IsNullOrEmpty(path) && !string.IsNullOrEmpty(url))
                            {
                                string savePath = Path.Combine(PathManager.LibrariesDir, path.Replace('/', '\\'));
                                allTasks.Add(new FileDownloadTask { Url = url, SavePath = savePath, FileName = Path.GetFileName(savePath), FileSize = size, Category = "依赖库" });
                            }
                        }

                        // 【关键】 natives-windows 里有 lwjgl.dll 等原生库，启动器启动时解压到 natives 目录。
                        // 之前只下 artifact 漏了它，导致 lwjgl.dll 找不到、游戏启动崩溃。
                        if (lib["downloads"]?["classifiers"]?["natives-windows"] is JToken native)
                        {
                            string nPath = native["path"]?.ToString();
                            string nUrl = native["url"]?.ToString();
                            long nSize = native["size"]?.Value<long>() ?? 0;
                            if (!string.IsNullOrEmpty(nPath) && !string.IsNullOrEmpty(nUrl))
                            {
                                string savePath = Path.Combine(PathManager.LibrariesDir, nPath.Replace('/', '\\'));
                                allTasks.Add(new FileDownloadTask { Url = nUrl, SavePath = savePath, FileName = Path.GetFileName(savePath), FileSize = nSize, Category = "依赖库" });
                            }
                        }
                    }
                }

                var assetTasks = new List<FileDownloadTask>();
                string assetIndexUrl = jsonObj["assetIndex"]?["url"]?.ToString();
                string assetIndexId = jsonObj["assetIndex"]?["id"]?.ToString();
                if (!string.IsNullOrEmpty(assetIndexUrl) && !string.IsNullOrEmpty(assetIndexId))
                {
                    string indexFilePath = Path.Combine(PathManager.AssetsIndexesDir, $"{assetIndexId}.json");
                    if (!File.Exists(indexFilePath))
                    {
                        string indexJson = await HttpService.GetStringAsync(assetIndexUrl);
                        Directory.CreateDirectory(PathManager.AssetsIndexesDir);
                        File.WriteAllText(indexFilePath, indexJson);
                    }

                    if (JObject.Parse(File.ReadAllText(indexFilePath))["objects"] is JObject objects)
                    {
                        foreach (var prop in objects.Properties())
                        {
                            string hash = prop.Value["hash"]?.ToString();
                            if (string.IsNullOrEmpty(hash)) continue;
                            string subDir = hash.Substring(0, 2);
                            string savePath = Path.Combine(PathManager.AssetsObjectsDir, subDir, hash);
                            // 用 BMCLAPI 国内镜像，官方 resources.download.minecraft.net 国内极慢
                            string url = $"https://bmclapi2.bangbang93.com/assets/{subDir}/{hash}";
                            // 友好显示名：hash 改成"资源_前8位"，避免下载中心一排乱码
                            assetTasks.Add(new FileDownloadTask { Url = url, SavePath = savePath, FileName = $"资源_{hash.Substring(0, Math.Min(8, hash.Length))}", FileSize = -1, Category = "游戏资源" });
                        }
                    }
                }

                var missingTasks = new List<FileDownloadTask>();
                var missingAssets = new List<FileDownloadTask>();

                // 检测是否存在
                foreach (var task in allTasks)
                {
                    if (File.Exists(task.SavePath) && (task.FileSize == -1 || new FileInfo(task.SavePath).Length == task.FileSize))
                    {
                        task.Progress = 100; // 触发内部状态为已完成
                        DownloadManager.Instance.AddTask(task);
                    }
                    else
                    {
                        task.Status = "等待中";
                        missingTasks.Add(task);
                        DownloadManager.Instance.AddTask(task);
                    }
                }

                foreach (var task in assetTasks)
                {
                    if (File.Exists(task.SavePath))
                    {
                        task.Progress = 100;
                        DownloadManager.Instance.AddTask(task);
                    }
                    else
                    {
                        task.Status = "等待中";
                        missingAssets.Add(task);
                        DownloadManager.Instance.AddTask(task);
                    }
                }

                progressCallback?.Invoke($"[检测] 需下载核心库 {missingTasks.Count} 个，资源 {missingAssets.Count} 个", 5);

                int totalTasks = missingTasks.Count;
                int completedTasks = 0;

                if (totalTasks > 0)
                {
                    // 👇 降低并发，保持网络顺畅
                    using (var semaphore = new SemaphoreSlim(8))
                    {
                        var downloadTasks = missingTasks.Select(async task =>
                        {
                            await semaphore.WaitAsync();
                            try
                            {
                                task.Status = "下载中";
                                await DownloadWithRetryAsync(task);
                                task.Progress = 100; // 绝对闭环
                            }
                            catch (Exception ex)
                            {
                                task.Status = "下载失败";
                                progressCallback?.Invoke($"[警告] 核心库 {task.FileName} 下载失败: {ex.Message}", 10);
                            }
                            finally
                            {
                                semaphore.Release();
                                int current = Interlocked.Increment(ref completedTasks);
                                progressCallback?.Invoke($"[下载] 核心库 {current}/{totalTasks}", 5 + (current * 20.0 / totalTasks));
                            }
                        });
                        await Task.WhenAll(downloadTasks);
                    }
                }

                int totalAssets = missingAssets.Count;
                int completedAssets = 0;

                if (totalAssets > 0)
                {
                    using (var assetSemaphore = new SemaphoreSlim(12))
                    {
                        var assetDownloadTasks = missingAssets.Select(async task =>
                        {
                            await assetSemaphore.WaitAsync();
                            try
                            {
                                task.Status = "下载中";
                                await DownloadWithRetryAsync(task);
                                task.Progress = 100; // 绝对闭环
                            }
                            catch
                            {
                                task.Status = "下载失败";
                            }
                            finally
                            {
                                assetSemaphore.Release();
                                int current = Interlocked.Increment(ref completedAssets);
                                if (current % 50 == 0 || current == totalAssets)
                                {
                                    double percent = 25 + (current * 70.0 / totalAssets);
                                    progressCallback?.Invoke($"[资源] 下载进度 {current}/{totalAssets}", percent);
                                }
                            }
                        });
                        await Task.WhenAll(assetDownloadTasks);
                    }
                }

                progressCallback?.Invoke($"[完成] 游戏和资源全部下载完毕！", 100);
                return true;
            }
            catch (Exception ex)
            {
                progressCallback?.Invoke($"[致命错误] {ex.Message}", 0);
                return false;
            }
        }

        private static async Task DownloadWithRetryAsync(FileDownloadTask task, int maxRetries = 3)
        {
            string url = ToMirrorUrl(task.Url);
            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    await Downloader.DownloadFileAsync(url, task.SavePath, null, task);
                    return;
                }
                catch
                {
                    if (i == maxRetries - 1) throw;
                    await Task.Delay(1000 * (i + 1));
                }
            }
        }

        /// <summary>把 Mojang 官方下载源替换为 BMCLAPI 国内镜像，大幅提速。</summary>
        private static string ToMirrorUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return url;
            const string mirror = "https://bmclapi2.bangbang93.com";
            // 核心 jar / meta：BMCLAPI 镜像路径与官方一致（去掉域名，保留路径）
            if (url.StartsWith("https://piston-data.mojang.com/", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("https://launchermeta.mojang.com/", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("https://piston-meta.mojang.com/", StringComparison.OrdinalIgnoreCase))
            {
                var u = new Uri(url);
                return mirror + u.AbsolutePath;
            }
            // 依赖库
            if (url.StartsWith("https://libraries.minecraft.net/", StringComparison.OrdinalIgnoreCase))
                return mirror + "/maven/" + url.Substring("https://libraries.minecraft.net/".Length);
            // 资源
            if (url.StartsWith("https://resources.download.minecraft.net/", StringComparison.OrdinalIgnoreCase))
                return mirror + "/assets/" + url.Substring("https://resources.download.minecraft.net/".Length);
            return url;
        }

        private static bool CheckRulesAllowed(JToken rules)
        {
            bool allowed = false;
            foreach (var rule in rules)
            {
                string action = rule["action"]?.ToString();
                string osName = rule["os"]?["name"]?.ToString();
                if (action == "allow" && (osName == null || osName == "windows")) allowed = true;
                else if (action == "disallow" && (osName == null || osName == "windows")) allowed = false;
            }
            return allowed;
        }
    }
}