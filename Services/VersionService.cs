using System;
using System.IO;
using System.Threading.Tasks;
using CraftGate.Models;
using Newtonsoft.Json;

namespace CraftGate.Services
{
    public static class VersionService
    {
        private static string CacheFile => Path.Combine(PathManager.CacheDir, "version_manifest.json");

        public static async Task<VersionManifest> GetVersionManifestAsync()
        {
            try
            {
                // 1. 如果有本地缓存，直接读取返回 (改为同步读取)
                if (File.Exists(CacheFile))
                {
                    string cachedJson = File.ReadAllText(CacheFile);
                    var cachedManifest = JsonConvert.DeserializeObject<VersionManifest>(cachedJson);
                    if (cachedManifest != null && cachedManifest.Versions.Count > 0)
                    {
                        return cachedManifest;
                    }
                }

                // 2. 没有缓存，从网络拉取 (网络请求保持异步)
                string url = "https://launchermeta.mojang.com/mc/game/version_manifest_v2.json";
                string json = await HttpService.GetStringAsync(url);

                // 3. 保存到本地缓存 (改为同步写入)
                Directory.CreateDirectory(PathManager.CacheDir);
                File.WriteAllText(CacheFile, json);

                return JsonConvert.DeserializeObject<VersionManifest>(json);
            }
            catch (Exception)
            {
                // 网络失败，尝试降级读取缓存
                if (File.Exists(CacheFile))
                {
                    string cachedJson = File.ReadAllText(CacheFile);
                    return JsonConvert.DeserializeObject<VersionManifest>(cachedJson);
                }
                return null;
            }
        }
    }
}