using System;
using System.Collections.Generic;
using System.Linq;

namespace CraftGate.Services
{
    /// <summary>
    /// 模组兼容性判定：加载器兼容矩阵 + MC 版本匹配 + 版本号比较。
    /// 对齐真实模组生态：Fabric↔Quilt 互通，NeoForge 与 Forge 部分互通。
    /// </summary>
    public static class ModCompatibilityService
    {
        // 规范化后的加载器标识：fabric / forge / neoforge / quilt / ""
        public static string NormalizeLoader(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            string s = raw.ToLowerInvariant().Trim();
            if (s.Contains("neoforge")) return "neoforge";
            if (s.Contains("fabric")) return "fabric";
            if (s.Contains("quilt")) return "quilt";
            if (s.Contains("forge")) return "forge";
            return "";
        }

        /// <summary>从版本目录名（如 1.21.10-fabric / 26.1-neoforge）解析出 MC 版本和加载器。</summary>
        public static void ParseVersionId(string rawVersion, out string mcVersion, out string loader)
        {
            mcVersion = rawVersion ?? "";
            loader = "";
            if (string.IsNullOrWhiteSpace(rawVersion)) return;

            var m = System.Text.RegularExpressions.Regex.Match(rawVersion, @"\d+\.\d+(?:\.\d+)?");
            if (m.Success) mcVersion = m.Value;
            loader = NormalizeLoader(rawVersion);
        }

        /// <summary>
        /// 加载器是否兼容：完全相等 或 互通组合。
        /// 环境返回的 loader（env）与模组声明的 loader（mod）比较。
        /// </summary>
        public static bool IsLoaderCompatible(string envLoader, string modLoader)
        {
            string env = NormalizeLoader(envLoader);
            string mod = NormalizeLoader(modLoader);

            // 原版环境：只接受原版/通用模组（mod loader 为空）
            if (string.IsNullOrEmpty(env)) return string.IsNullOrEmpty(mod);

            // 模组未声明加载器（universal）→ 任何环境都能用
            if (string.IsNullOrEmpty(mod)) return true;

            if (env == mod) return true;

            // Fabric ↔ Quilt 互通
            if ((env == "fabric" && mod == "quilt") || (env == "quilt" && mod == "fabric")) return true;

            // NeoForge 环境可用 Forge 模组（1.20.5+ 多数 Forge mod 兼容 NeoForge 生态）；
            // Forge 环境也可用 NeoForge 模组（视具体 mod，此处宽松处理以扩大兼容面）
            if ((env == "neoforge" && mod == "forge") || (env == "forge" && mod == "neoforge")) return true;

            return false;
        }

        /// <summary>用于搜索 facets 的加载器列表：把互通的加载器一并发给 Modrinth（OR 组）。</summary>
        public static string[] GetSearchLoaders(string envLoader)
        {
            string env = NormalizeLoader(envLoader);
            if (string.IsNullOrEmpty(env)) return Array.Empty<string>();
            switch (env)
            {
                case "fabric": return new[] { "fabric", "quilt" };
                case "quilt": return new[] { "quilt", "fabric" };
                case "neoforge": return new[] { "neoforge", "forge" };
                case "forge": return new[] { "forge", "neoforge" };
                default: return new[] { env };
            }
        }

        /// <summary>模组版本是否兼容指定环境：MC 版本命中 且 加载器兼容。</summary>
        public static bool IsVersionCompatible(
            IEnumerable<string> modGameVersions,
            IEnumerable<string> modLoaders,
            string envMcVersion,
            string envLoader)
        {
            bool versionOk = modGameVersions != null &&
                modGameVersions.Any(gv => string.Equals(gv, envMcVersion, StringComparison.OrdinalIgnoreCase));
            if (!versionOk) return false;

            if (modLoaders == null || !modLoaders.Any()) return string.IsNullOrEmpty(NormalizeLoader(envLoader));
            return modLoaders.Any(ml => IsLoaderCompatible(envLoader, ml));
        }

        /// <summary>版本号转可比较数组：1.21.10 → [1,21,10]。用于排序。</summary>
        public static int[] ParseVersion(string v)
        {
            if (string.IsNullOrEmpty(v)) return new[] { 0, 0, 0 };
            var m = System.Text.RegularExpressions.Regex.Match(v, @"(\d+)\.(\d+)(?:\.(\d+))?");
            if (!m.Success) return new[] { 0, 0, 0 };
            return new[]
            {
                int.TryParse(m.Groups[1].Value, out int a) ? a : 0,
                int.TryParse(m.Groups[2].Value, out int b) ? b : 0,
                m.Groups[3].Success && int.TryParse(m.Groups[3].Value, out int c) ? c : 0
            };
        }

        public static int CompareVersion(string a, string b)
        {
            var va = ParseVersion(a);
            var vb = ParseVersion(b);
            for (int i = 0; i < 3; i++)
            {
                if (va[i] != vb[i]) return va[i].CompareTo(vb[i]);
            }
            return 0;
        }
    }
}
