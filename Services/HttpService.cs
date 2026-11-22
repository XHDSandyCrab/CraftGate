using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace CraftGate.Services
{
    public static class HttpService
    {
        private static readonly HttpClient _client;

        static HttpService()
        {
            _client = new HttpClient();
            _client.DefaultRequestHeaders.Add("User-Agent", "CraftGate/1.0");
            _client.Timeout = TimeSpan.FromSeconds(15);
        }

        public static string GetUrl(string originalUrl)
        {
            if (!ConfigManager.Settings.UseMirror) return originalUrl;

            if (originalUrl.Contains("launchermeta.mojang.com"))
                return originalUrl.Replace("launchermeta.mojang.com", "bmclapi2.bangbang93.com");

            if (originalUrl.Contains("launcher.mojang.com"))
                return originalUrl.Replace("launcher.mojang.com", "bmclapi2.bangbang93.com");

            if (originalUrl.Contains("resources.download.minecraft.net"))
                return originalUrl.Replace("resources.download.minecraft.net", "bmclapi2.bangbang93.com/assets");

            if (originalUrl.Contains("libraries.minecraft.net"))
                return originalUrl.Replace("libraries.minecraft.net", "bmclapi2.bangbang93.com/maven");

            return originalUrl;
        }

        public static async Task<string> GetStringAsync(string url)
        {
            return await _client.GetStringAsync(GetUrl(url));
        }

        /// <summary>
        /// 带磁盘缓存的 GET，用于不常变化的元数据 API（加载器版本列表等）。
        /// 缓存按 URL 哈希命名，默认 1 天有效；过期或请求失败时回退到旧缓存，避免进向导每次都等境外 API。
        /// </summary>
        public static async Task<string> GetStringCachedAsync(string url, double cacheDays = 1)
        {
            string realUrl = GetUrl(url);
            string cacheDir = Path.Combine(PathManager.CacheDir, "HttpCache");
            string cacheFile = Path.Combine(cacheDir, Hash(realUrl) + ".txt");

            try
            {
                Directory.CreateDirectory(cacheDir);
                // 命中未过期缓存则直接返回
                if (File.Exists(cacheFile) && (DateTime.Now - File.GetLastWriteTime(cacheFile)).TotalDays < cacheDays)
                    return File.ReadAllText(cacheFile);

                string content = await _client.GetStringAsync(realUrl);
                File.WriteAllText(cacheFile, content);   // 写入新缓存
                return content;
            }
            catch
            {
                // 网络失败时回退到旧缓存（哪怕过期），总比卡住强
                if (File.Exists(cacheFile)) return File.ReadAllText(cacheFile);
                throw;
            }
        }

        private static string Hash(string s)
        {
            using (var md5 = MD5.Create())
            {
                var bytes = md5.ComputeHash(Encoding.UTF8.GetBytes(s));
                var sb = new StringBuilder();
                foreach (var b in bytes) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }
    }
}