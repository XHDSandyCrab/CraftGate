using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CraftGate.Models;
using Newtonsoft.Json.Linq;

namespace CraftGate.Services
{
    public static class LoaderApiService
    {
        public static async Task<List<LoaderInfo>> GetAvailableLoadersAsync(string mcVersion)
        {
            var list = new List<LoaderInfo>
            {
                new LoaderInfo { Id = "vanilla", Name = "Minecraft", IconCode = "\uE7FC", IsSupported = true,
                    HintText = "原版游戏本体" },
                new LoaderInfo { Id = "forge", Name = "Forge", IconCode = "\uE90F", HintText = "经典模组加载器" },
                new LoaderInfo { Id = "neoforge", Name = "NeoForge", IconCode = "\uE90F", HintText = "Forge 的现代分支" },
                new LoaderInfo { Id = "optifine", Name = "OptiFine", IconCode = "\uE9D9", HintText = "高清修复 / 性能优化" },
                new LoaderInfo { Id = "fabric", Name = "Fabric", IconCode = "\uE81E", HintText = "轻量模组加载器" },
                new LoaderInfo { Id = "fabric-api", Name = "Fabric API", IconCode = "\uE81E", IsAddon = true,
                    HintText = "Fabric 生态的基础 API，多数 Fabric 模组依赖它" },
                new LoaderInfo { Id = "quilt", Name = "Quilt", IconCode = "\uE8A5", HintText = "Fabric 的社区分支" },
                new LoaderInfo { Id = "qsl", Name = "QSL/QFAPI", IconCode = "\uE8A5", IsAddon = true,
                    HintText = "Quilt 生态的基础 API（Quilt Standard Libraries / QFAPI）" }
            };

            // 绑定加载器图标（Assets 里已就位的图标）
            var iconMap = new Dictionary<string, string>
            {
                ["vanilla"] = "Grass.png",       // 原版=草方块
                ["forge"] = "Forge.png",
                ["neoforge"] = "NeoForge.png",
                ["optifine"] = "Optifine.png",
                ["fabric"] = "Fabric.png",
                ["fabric-api"] = "Fabric.png",
                ["quilt"] = "Quilt.png",
                ["qsl"] = "Quilt.png"
            };
            foreach (var loader in list)
                if (iconMap.TryGetValue(loader.Id, out var icon))
                    loader.IconPath = "pack://application:,,,/CraftGate;component/Assets/" + icon;

            var vanilla = list.First(x => x.Id == "vanilla");
            vanilla.AvailableVersions.Add(new VersionItem { Version = mcVersion }); // 原版=当前 MC 版本

            // 每个加载器独立 try + 并行请求：各方法只写普通 List，不碰 UI 集合/PropertyChanged，线程安全。
            // 原来串行 await 7 个境外 API 会累加到 60 秒，并行后取最长那个的时间。
            await Task.WhenAll(
                Safe(() => LoadFabric(list, mcVersion), "Fabric"),
                Safe(() => LoadForge(list, mcVersion), "Forge"),
                Safe(() => LoadNeoForge(list, mcVersion), "NeoForge"),
                Safe(() => LoadQuilt(list, mcVersion), "Quilt"),
                Safe(() => LoadFabricApi(list, mcVersion), "Fabric API"),
                Safe(() => LoadQsl(list, mcVersion), "QSL"),
                Safe(() => LoadOptiFine(list, mcVersion), "OptiFine"));

            // 给每个版本项绑定所属加载器（供选中时判断是否为配套 mod）
            foreach (var loader in list)
                foreach (var v in loader.AvailableVersions)
                    v.Owner = loader;

            return list;
        }

        private static async Task Safe(Func<Task> action, string name)
        {
            try { await action(); }
            catch (Exception ex) { Console.WriteLine($"获取 {name} 版本失败: {ex.Message}"); }
        }

        private static LoaderInfo L(List<LoaderInfo> list, string id) => list.First(x => x.Id == id);

        // ---------- Fabric ----------
        private static async Task LoadFabric(List<LoaderInfo> list, string mcVersion)
        {
            var fabric = L(list, "fabric");
            var gamesJson = await HttpService.GetStringCachedAsync("https://meta.fabricmc.net/v2/versions/game");
            if (!gamesJson.Contains($"\"{mcVersion}\"")) { fabric.IsSupported = false; return; }
            var json = await HttpService.GetStringCachedAsync($"https://meta.fabricmc.net/v2/versions/loader/{mcVersion}");
            var arr = JArray.Parse(json);
            foreach (var item in arr)
            {
                string v = item["loader"]?["version"]?.ToString();
                if (!string.IsNullOrEmpty(v)) fabric.AvailableVersions.Add(new VersionItem { Version = v });
            }
            fabric.IsSupported = fabric.AvailableVersions.Count > 0;
        }

        // ---------- Forge ----------
        private static async Task LoadForge(List<LoaderInfo> list, string mcVersion)
        {
            var forge = L(list, "forge");
            var json = await HttpService.GetStringCachedAsync("https://files.minecraftforge.net/net/minecraftforge/forge/promotions_slim.json");
            var promos = JObject.Parse(json)["promos"] as JObject;
            if (promos != null && promos.ContainsKey($"{mcVersion}-latest"))
            {
                forge.IsSupported = true;
                string latest = promos[$"{mcVersion}-latest"]?.ToString();
                string recommended = promos[$"{mcVersion}-recommended"]?.ToString();
                if (!string.IsNullOrEmpty(recommended)) forge.AvailableVersions.Add(new VersionItem { Version = $"{mcVersion}-{recommended}" });
                if (!string.IsNullOrEmpty(latest) && latest != recommended) forge.AvailableVersions.Add(new VersionItem { Version = $"{mcVersion}-{latest}" });
            }
            else forge.IsSupported = false;
        }

        // ---------- NeoForge ----------
        private static async Task LoadNeoForge(List<LoaderInfo> list, string mcVersion)
        {
            var neo = L(list, "neoforge");
            // 官方 maven.neoforged.net 国内连不上，改用 BMCLAPI 镜像
            string json;
            try
            {
                json = await HttpService.GetStringCachedAsync(
                    "https://bmclapi2.bangbang93.com/maven/net/neoforged/neoforge/maven-metadata.xml", 1);
            }
            catch { neo.IsSupported = false; return; }

            // 新版 NeoForge 版本号去掉了开头的 "1."：MC 1.20.2 → 20.2.93；MC 1.21.1 → 21.1.267
            // 旧版则是 1.20.1-xxx 格式。两种都兼容。
            var all = System.Text.RegularExpressions.Regex.Matches(json, @"<version>([^<]+)</version>")
                .Cast<System.Text.RegularExpressions.Match>()
                .Select(m => m.Groups[1].Value)
                .ToList();

            // 新格式：去掉 mcVersion 开头的 "1."，如 1.20.2 → "20.2."
            string shortMc = mcVersion.StartsWith("1.") ? mcVersion.Substring(2) : mcVersion;
            var versions = all
                .Where(v => v.StartsWith(shortMc + ".", StringComparison.OrdinalIgnoreCase)
                            && System.Text.RegularExpressions.Regex.IsMatch(v, @"^[\d.]+$"))  // 纯数字点分，排除 alpha/pre
                .OrderByDescending(v => v, Comparer<string>.Create(CompareVer))
                .Take(10)
                .Select(v => "1." + v)   // 显示成 1.20.2-20.2.93 更直观
                .ToList();

            // 兼容旧格式 1.20.2-xx
            var legacy = all.Where(v => v.StartsWith(mcVersion + "-", StringComparison.OrdinalIgnoreCase)).Take(10);
            versions.AddRange(legacy);

            foreach (var v in versions.Distinct()) neo.AvailableVersions.Add(new VersionItem { Version = v });
            neo.IsSupported = neo.AvailableVersions.Count > 0;
        }

        /// <summary>版本号自然排序：20.2.93 > 20.2.9（按点分段数值比较）</summary>
        private static int CompareVer(string a, string b)
        {
            var pa = a.Split('.'); var pb = b.Split('.');
            int n = Math.Min(pa.Length, pb.Length);
            for (int i = 0; i < n; i++)
            {
                int xa = int.TryParse(pa[i], out int va) ? va : 0;
                int xb = int.TryParse(pb[i], out int vb) ? vb : 0;
                if (xa != xb) return xa.CompareTo(xb);
            }
            return pa.Length.CompareTo(pb.Length);
        }

        // ---------- Quilt ----------
        private static async Task LoadQuilt(List<LoaderInfo> list, string mcVersion)
        {
            var quilt = L(list, "quilt");
            var gamesJson = await HttpService.GetStringCachedAsync("https://meta.quiltmc.org/v3/versions/game");
            if (!gamesJson.Contains($"\"{mcVersion}\"")) { quilt.IsSupported = false; return; }
            var json = await HttpService.GetStringCachedAsync("https://meta.quiltmc.org/v3/versions/loader");
            var arr = JArray.Parse(json);
            foreach (var item in arr.Take(10))
            {
                string v = item["version"]?.ToString();
                if (!string.IsNullOrEmpty(v)) quilt.AvailableVersions.Add(new VersionItem { Version = v });
            }
            quilt.IsSupported = quilt.AvailableVersions.Count > 0;
        }

        // ---------- Fabric API（配套 mod，来自 Modrinth）----------
        private static async Task LoadFabricApi(List<LoaderInfo> list, string mcVersion)
        {
            var api = L(list, "fabric-api");
            string url = $"https://api.modrinth.com/v2/project/fabric-api/version?loaders=%5B%22fabric%22%5D&game_versions=%5B%22{mcVersion}%22%5D";
            var json = await HttpService.GetStringCachedAsync(url);
            var arr = JArray.Parse(json);
            foreach (var item in arr.Take(12))
            {
                string v = item["version_number"]?.ToString();
                if (!string.IsNullOrEmpty(v)) api.AvailableVersions.Add(new VersionItem { Version = v });
            }
            api.IsSupported = api.AvailableVersions.Count > 0;
        }

        // ---------- QSL / QFAPI（配套 mod，来自 Modrinth）----------
        private static async Task LoadQsl(List<LoaderInfo> list, string mcVersion)
        {
            var qsl = L(list, "qsl");
            string url = $"https://api.modrinth.com/v2/project/qsl/version?loaders=%5B%22quilt%22%5D&game_versions=%5B%22{mcVersion}%22%5D";
            var json = await HttpService.GetStringCachedAsync(url);
            var arr = JArray.Parse(json);
            foreach (var item in arr.Take(12))
            {
                string v = item["version_number"]?.ToString();
                if (!string.IsNullOrEmpty(v)) qsl.AvailableVersions.Add(new VersionItem { Version = v });
            }
            qsl.IsSupported = qsl.AvailableVersions.Count > 0;
        }

        // ---------- OptiFine ----------
        private static async Task LoadOptiFine(List<LoaderInfo> list, string mcVersion)
        {
            var opt = L(list, "optifine");
            // OptiFine 官方无公开 API，使用 BMCLAPI（HMCL 等主流启动器同款镜像）列出真实版本
            string url = $"https://bmclapi2.bangbang93.com/optifine/{mcVersion}";
            try
            {
                var json = await HttpService.GetStringCachedAsync(url, 1);
                var arr = JArray.Parse(json);
                foreach (var item in arr)
                {
                    string type = item["type"]?.ToString();       // 如 HD_U_I7（已含 patch）
                    string filename = item["filename"]?.ToString();
                    string patch = item["patch"]?.ToString();
                    if (string.IsNullOrEmpty(type)) continue;
                    // 预览版标注（filename 以 preview_ 开头或 patch 含 pre）
                    bool preview = (filename?.StartsWith("preview_", StringComparison.OrdinalIgnoreCase) ?? false)
                                   || (patch?.Contains("pre") ?? false);
                    string display = type + (preview ? " (预览)" : "");
                    opt.AvailableVersions.Add(new VersionItem
                    {
                        Version = display,
                        Extra = filename   // 存 jar 文件名，供安装时用
                    });
                }
            }
            catch { /* 请求失败则保持空列表，由下方兜底 */ }

            if (opt.AvailableVersions.Count == 0)
                opt.AvailableVersions.Add(new VersionItem { Version = "暂无可用版本（稍后重试）" });
            opt.IsSupported = true;
        }
    }
}
