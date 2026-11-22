using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CraftGate.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CraftGate.Services
{
    public static class ModrinthService
    {
        private static readonly HttpClient _client = new HttpClient();          // 文件下载（允许长时间）
        private static readonly HttpClient _apiClient = new HttpClient();       // API 请求（短超时，避免卡死）
        private const string BaseUrl = "https://api.modrinth.com/v2";

        private static readonly Dictionary<string, string> ModDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "钠", "Sodium" }, { "纳", "Sodium" }, { "优化", "Optimization" },
            { "铱", "Iris" }, { "光影", "Shader" }, { "光影前置", "Iris" },
            { "实体渲染机制优化", "Entity Culling" }, { "实体渲染", "Entity Culling" },
            { "布艺", "Cloth Config" }, { "布艺配置", "Cloth Config" }, { "前置", "API" },
            { "机械动力", "Create" }, { "暮色森林", "Twilight Forest" }, { "苹果皮", "AppleSkin" },
            { "小地图", "Xaero's Minimap" }, { "旅行地图", "Journeymap" }, { "饰品", "Curios" },
            { "高清修复", "OptiFine" }, { "工业2", "Industrial Craft 2" }, { "星辉魔法", "Astral Sorcery" },
            { "无尽贪婪", "Avaritia" }, { "热力膨胀", "Thermal Expansion" }, { "通用机械", "Mekanism" },
            { "应用能源2", "Applied Energistics 2" }, { "植物魔法", "Botania" }, { "血魔法", "Blood Magic" },
            { "沉浸工程", "Immersive Engineering" }, { "匠魂", "Tinkers Construct" }, { "拔刀剑", "SlashBlade" }
        };

        static ModrinthService()
        {
            string ua = "CraftGate/1.0 (contact@craftgate.com)";
            _client.DefaultRequestHeaders.Add("User-Agent", ua);
            _client.Timeout = TimeSpan.FromMinutes(5);          // 下载大文件需要长时间

            _apiClient.DefaultRequestHeaders.Add("User-Agent", ua);
            _apiClient.Timeout = TimeSpan.FromSeconds(20);      // API 请求 20 秒超时，挂起也能快速失败
        }

        /// <summary>
        /// 把中文查询用词条库转成英文：精确命中直接用；否则把查询里出现的中文词条逐段替换成英文。
        /// 例如"光影模组"→"Shader 模组"；"钠"→"Sodium"。
        /// </summary>
        private static string BuildSearchQuery(string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return query;
            string q = query.Trim();
            // 1) 精确命中词条库
            if (ModDict.TryGetValue(q, out var exact)) return exact;
            // 2) 部分匹配：把查询里出现的每个中文词条替换成对应英文（长的优先，避免"光影"先于"光影前置"）
            string result = q;
            foreach (var kv in ModDict.OrderByDescending(k => k.Key.Length))
            {
                if (result.IndexOf(kv.Key, StringComparison.OrdinalIgnoreCase) >= 0)
                    result = result.Replace(kv.Key, kv.Value);
            }
            return result;
        }

        /// <summary>按匹配度从高到低排序（稳定排序，同分保持原顺序）。</summary>
        private static List<ModrinthProject> SortByMatch(List<ModrinthProject> hits, string query)
        {
            if (hits == null || hits.Count <= 1 || string.IsNullOrWhiteSpace(query)) return hits;
            return hits.OrderByDescending(h => MatchScore(h, query)).ToList();
        }

        /// <summary>按与查询词的匹配程度打分：标题完全匹配 > 标题开头 > 标题包含 > 描述包含。</summary>
        private static int MatchScore(ModrinthProject hit, string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return 0;
            string q = query.Trim();
            string title = (hit.Title ?? "").Trim();
            string display = (hit.DisplayName ?? "").Trim();
            string desc = hit.Description ?? "";
            int score = 0;
            if (string.Equals(title, q, StringComparison.OrdinalIgnoreCase)
                || string.Equals(display, q, StringComparison.OrdinalIgnoreCase)) score += 100;
            else if (title.StartsWith(q, StringComparison.OrdinalIgnoreCase)
                     || display.StartsWith(q, StringComparison.OrdinalIgnoreCase)) score += 60;
            else if (title.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
                     || display.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) score += 30;
            if (desc.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) score += 10;
            return score;
        }

        // 👇 核心修复：增加 loader 参数
        public static async Task<List<ModrinthProject>> SearchAsync(string query, string projectType, string mcVersion, string loader, int offset, int limit, CancellationToken token)
        {
            try
            {
                // 中文查询先过词条库转成英文，再用英文去搜（否则中文直接搜 Modrinth 基本无结果）
                string searchQuery = BuildSearchQuery(query);

                string cacheDir = Path.Combine(PathManager.CacheDir, "ModrinthSearch");
                Directory.CreateDirectory(cacheDir);
                // 用稳定哈希替代 GetHashCode()——后者跨进程随机，会让缓存每次重启都失效
                string cacheKey = $"{projectType}_{mcVersion}_{loader}_{offset}_{StableHash(searchQuery ?? "")}.json";
                string cacheFile = Path.Combine(cacheDir, SafeFileName(cacheKey));

                // 缓存 7 天过期：模组会持续更新，旧缓存会漏掉新版本
                if (File.Exists(cacheFile) && (DateTime.Now - File.GetLastWriteTime(cacheFile)).TotalDays < 7)
                {
                    string cachedJson = File.ReadAllText(cacheFile);
                    var cachedResult = JsonConvert.DeserializeObject<ModrinthSearchResponse>(cachedJson);
                    if (cachedResult != null && cachedResult.Hits != null)
                    {
                        foreach (var hit in cachedResult.Hits) hit.OriginalDescription = hit.Description;
                        return SortByMatch(cachedResult.Hits, searchQuery);
                    }
                }

                // 【放宽】不再用 versions 硬过滤——26.x 等新版本绝大多数模组还没标注，
                // 硬过滤会导致结果极少。改为搜全部，由本地按兼容性排序/标注（PCL2 风格）。
                // loader 用 OR 组（Fabric↔Quilt、Forge↔NeoForge 互通），扩大覆盖面。
                string facets = $"[ [\"project_type:{projectType}\"]";
                // 【修复】光影(shader)/资源包(resourcepack)/数据包(datapack)/整合包(modpack)不依赖 loader，
                // 加 categories:fabric 过滤会把绝大多数光影排除（只剩打标签的少数几个）。只对 mod 加 loader 过滤。
                bool needsLoader = string.Equals(projectType, "mod", StringComparison.OrdinalIgnoreCase);
                string[] loaders = needsLoader ? ModCompatibilityService.GetSearchLoaders(loader) : new string[0];
                if (loaders.Length > 0)
                {
                    facets += ", [" + string.Join(",", loaders.Select(l => $"\"categories:{l}\"")) + "]";
                }
                facets += " ]";

                string queryStr = string.IsNullOrWhiteSpace(searchQuery) ? "" : $"query={Uri.EscapeDataString(searchQuery)}&";
                string url = $"{BaseUrl}/search?{queryStr}facets={Uri.EscapeDataString(facets)}&limit={limit}&offset={offset}&index=downloads";

                using (var response = await _apiClient.GetAsync(url, token))
                {
                    response.EnsureSuccessStatusCode();
                    string json = await response.Content.ReadAsStringAsync();
                    var result = JsonConvert.DeserializeObject<ModrinthSearchResponse>(json);

                    if (result != null && result.Hits != null)
                    {
                        try { File.WriteAllText(cacheFile, json); } catch { }
                        foreach (var hit in result.Hits) hit.OriginalDescription = hit.Description;
                    }
                    return SortByMatch(result?.Hits ?? new List<ModrinthProject>(), searchQuery);
                }
            }
            catch (OperationCanceledException) { return new List<ModrinthProject>(); }
            catch { return new List<ModrinthProject>(); }
        }

        /// <summary>
        /// 通用 API 缓存：命中且未过期则读本地，否则联网并写回。
        /// 这样依赖分析/搜索二次访问都是本地读取，不再每次都走网络（解决"卡住"）。
        /// </summary>
        private static async Task<string> GetCachedJsonAsync(string cacheKey, string url, int expireDays)
        {
            string cacheDir = Path.Combine(PathManager.CacheDir, "ModrinthApi");
            Directory.CreateDirectory(cacheDir);
            string cacheFile = Path.Combine(cacheDir, SafeFileName(cacheKey) + ".json");

            if (File.Exists(cacheFile) && (DateTime.Now - File.GetLastWriteTime(cacheFile)).TotalDays < expireDays)
            {
                try { return File.ReadAllText(cacheFile); } catch { }
            }

            string json = await _apiClient.GetStringAsync(url);
            try { File.WriteAllText(cacheFile, json); } catch { }
            return json;
        }

        private static string SafeFileName(string s)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s.Replace('?', '_').Replace('&', '_').Replace(':', '_');
        }

        // 跨进程稳定的字符串哈希（FNV-1a），用于缓存文件名
        private static string StableHash(string s)
        {
            unchecked
            {
                uint hash = 2166136261;
                foreach (char c in s) { hash ^= c; hash *= 16777619; }
                return hash.ToString("x8");
            }
        }

        public static async Task<List<ModrinthVersion>> GetVersionsAsync(string projectId, string mcVersion, string loader)
        {
            try
            {
                string url = $"{BaseUrl}/project/{projectId}/version";
                var queryParams = new List<string>();
                if (!string.IsNullOrEmpty(mcVersion)) queryParams.Add("game_versions=" + Uri.EscapeDataString($"[\"{mcVersion}\"]"));
                if (!string.IsNullOrEmpty(loader)) queryParams.Add("loaders=" + Uri.EscapeDataString($"[\"{loader}\"]"));
                if (queryParams.Count > 0) url += "?" + string.Join("&", queryParams);

                // 版本列表变动较快，缓存 1 天
                string json = await GetCachedJsonAsync($"versions_{projectId}_{mcVersion}_{loader}", url, 1);
                return JsonConvert.DeserializeObject<List<ModrinthVersion>>(json) ?? new List<ModrinthVersion>();
            }
            catch { return new List<ModrinthVersion>(); }
        }

        /// <summary>通过 version_id 反查其所属 project_id（依赖只给了 version_id 时用）。</summary>
        public static async Task<string> GetProjectIdByVersionIdAsync(string versionId)
        {
            try
            {
                // version→project 映射不变，长期缓存
                string json = await GetCachedJsonAsync($"verproj_{versionId}", $"{BaseUrl}/version/{versionId}", 30);
                return JObject.Parse(json)["project_id"]?.ToString();
            }
            catch { return null; }
        }

        public static async Task<ModrinthProject> GetProjectInfoAsync(string projectId)
        {
            try
            {
                // 项目信息缓存 7 天
                string json = await GetCachedJsonAsync($"project_{projectId}", $"{BaseUrl}/project/{projectId}", 7);
                return JsonConvert.DeserializeObject<ModrinthProject>(json);
            }
            catch { return null; }
        }

        public static async Task<string> TranslateToEnglishViaMCModAsync(string chineseQuery)
        {
            if (ModDict.TryGetValue(chineseQuery, out var englishName)) return englishName;
            if (Regex.IsMatch(chineseQuery, @"[\u4e00-\u9fa5]"))
            {
                try
                {
                    string url = $"https://api.mymemory.translated.net/get?q={Uri.EscapeDataString(chineseQuery)}&langpair=zh-CN|en";
                    var response = await _apiClient.GetAsync(url);
                    if (response.IsSuccessStatusCode)
                    {
                        var json = await response.Content.ReadAsStringAsync();
                        var result = JObject.Parse(json);
                        var translated = result["responseData"]?["translatedText"]?.ToString();
                        if (!string.IsNullOrEmpty(translated) && translated != chineseQuery && !translated.Contains("MYMEMORY")) return translated;
                    }
                }
                catch { }
            }
            return chineseQuery;
        }

        public static async Task<string> TranslateToChineseAsync(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;
            try
            {
                string url = $"https://api.mymemory.translated.net/get?q={Uri.EscapeDataString(text)}&langpair=en|zh-CN";
                var response = await _apiClient.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var result = JObject.Parse(json);
                    var translated = result["responseData"]?["translatedText"]?.ToString();
                    if (!string.IsNullOrEmpty(translated) && translated != text && !translated.Contains("MYMEMORY")) return translated;
                }
            }
            catch { }
            return text;
        }

        public static async Task<bool> DownloadFileAsync(string url, string saveDir, string filename, Action<double> progressCallback = null)
        {
            Directory.CreateDirectory(saveDir);
            string savePath = Path.Combine(saveDir, filename);

            var urlsToTry = new List<string> { url };
            if (url.Contains("cdn.modrinth.com") || url.Contains("github.com"))
            {
                urlsToTry.Add($"https://ghproxy.net/{url}");
                urlsToTry.Add($"https://mirror.ghproxy.com/{url}");
            }

            foreach (var tryUrl in urlsToTry)
            {
                try
                {
                    using (var response = await _client.GetAsync(tryUrl, HttpCompletionOption.ResponseHeadersRead))
                    {
                        if (!response.IsSuccessStatusCode) continue;
                        var total = response.Content.Headers.ContentLength ?? -1;
                        var received = 0L;
                        var buffer = new byte[8192];

                        using (var fileStream = new FileStream(savePath, FileMode.Create, FileAccess.Write, FileShare.None))
                        using (var stream = await response.Content.ReadAsStreamAsync())
                        {
                            int read;
                            while ((read = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                            {
                                await fileStream.WriteAsync(buffer, 0, read);
                                received += read;
                                if (total > 0 && progressCallback != null)
                                    progressCallback((double)received / total * 100);
                            }
                        }
                    }
                    return true;
                }
                catch
                {
                    if (File.Exists(savePath)) try { File.Delete(savePath); } catch { }
                }
            }
            return false;
        }
    }
}