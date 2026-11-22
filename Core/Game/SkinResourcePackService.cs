using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace CraftGate.Core.Game
{
    /// <summary>
    /// 为 Minecraft 1.19.3+ 生成"换肤资源包"并自动启用。
    /// 原理：把用户的皮肤原样放进资源包的玩家贴图路径，覆盖默认 steve/alex。
    /// 不依赖任何 mod，跨版本通用，且保留皮肤透明通道（不会变黑炭）。
    /// </summary>
    public static class SkinResourcePackService
    {
        public const string PackFolderName = "CraftGateSkin";

        // 1.19.3+ 的玩家皮肤路径：9 个内置默认角色(steve/alex/sunny/kai/makena/zuri/efe/ari/noor)
        // × 2 种体型(wide/slim) = 18 个文件，全部覆盖。
        // 关键：离线账号会按名字哈希被随机分配某个默认角色，只覆盖 steve/alex 时
        // 若分到 Sunny 等其它角色就看不到换肤——必须 18 个全替换。
        private static readonly string[] SkinPaths =
        {
            "steve", "alex", "sunny", "kai", "makena", "zuri", "efe", "ari", "noor"
        };

        private static IEnumerable<string> SkinTexturePaths =>
            from model in new[] { "wide", "slim" }
            from skin in SkinPaths
            select $"assets/minecraft/textures/entity/player/{model}/{skin}.png";

        // 生成 pack.mcmeta 内容。1.21.9+ 用 min_format/max_format（带小数），
        // 更早版本用 pack_format（整数）。数值查证自 Minecraft Wiki 的资源包格式表。
        private static string BuildMcmeta(string version, out string formatDesc)
        {
            string v = ExtractNumber(version);
            string formatLine;

            // 新体系：1.21.9 及以后（含 26.x）
            string fmt = LookupNewFormat(version);
            if (fmt != null)
            {
                formatLine = $"    \"min_format\": {fmt},\n    \"max_format\": {fmt}";
                formatDesc = "min/max_format=" + fmt;
            }
            else
            {
                int pf = LookupOldFormat(v);
                formatLine = $"    \"pack_format\": {pf}";
                formatDesc = "pack_format=" + pf;
            }

            return "{\n  \"pack\": {\n" + formatLine + ",\n" +
                   "    \"description\": \"CraftGate 自动换肤\"\n  }\n}";
        }

        // 1.21.9+ 新格式号（带小数，min/max）；26.x 版本名单独匹配
        private static string LookupNewFormat(string version)
        {
            string s = (version ?? "").Trim().ToLowerInvariant();
            // 26.x 系列（版本名形如 26.1 / 26.2 / 26.3）
            if (s.StartsWith("26.3")) return "97.1";
            if (s.StartsWith("26.2")) return "88.0";
            if (s.StartsWith("26.1")) return "84.0";
            // 1.21.9+ 走新体系，但要先按小版本给不同号
            string v = ExtractNumber(version);
            if (IsAtLeast(v, "1.21.11")) return "75.0";
            if (IsAtLeast(v, "1.21.9")) return "69.0";
            return null; // 更早版本用旧体系
        }

        // 旧体系 pack_format（1.19.3 ~ 1.21.8）
        private static int LookupOldFormat(string v)
        {
            if (IsAtLeast(v, "1.21.7")) return 64; // 1.21.7 / 1.21.8
            if (IsAtLeast(v, "1.21.6")) return 63;
            if (IsAtLeast(v, "1.21.5")) return 55;
            if (IsAtLeast(v, "1.21.4")) return 46;
            if (IsAtLeast(v, "1.21.2")) return 32;
            if (IsAtLeast(v, "1.21")) return 34;
            if (IsAtLeast(v, "1.20.5")) return 41;
            if (IsAtLeast(v, "1.20.3")) return 26;
            if (IsAtLeast(v, "1.20.2")) return 18;
            if (IsAtLeast(v, "1.20")) return 15;
            if (IsAtLeast(v, "1.19.4")) return 12;
            return 11; // 1.19.3
        }

        /// <summary>是否应使用资源包方案（1.19.3+）。</summary>
        public static bool UseResourcePack(string version)
        {
            return IsAtLeast(ExtractNumber(version), "1.19.3");
        }

        /// <summary>
        /// 生成换肤资源包并写入皮肤图片（保留原图透明通道）。
        /// </summary>
        public static void EnsurePack(string gameDir, string version, string skinPath, Action<string> log)
        {
            if (string.IsNullOrEmpty(skinPath) || !File.Exists(skinPath))
            {
                log?.Invoke("[皮肤] 皮肤文件不存在，跳过换肤资源包生成");
                return;
            }

            try
            {
                string rpDir = Path.Combine(gameDir, "resourcepacks", PackFolderName);
                Directory.CreateDirectory(rpDir);

                // pack.mcmeta（按版本写正确的格式号，否则 MC 判为不兼容而不加载）
                string mcmeta = BuildMcmeta(version, out string formatDesc);
                File.WriteAllText(Path.Combine(rpDir, "pack.mcmeta"),
                    mcmeta, new UTF8Encoding(false));

                // 皮肤原样复制到 4 个路径（保留 PNG 透明，不变黑）
                foreach (string rel in SkinTexturePaths)
                {
                    string dest = Path.Combine(rpDir, rel.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(dest));
                    File.Copy(skinPath, dest, true);
                }

                log?.Invoke($"[皮肤] 换肤资源包已生成（{formatDesc}）");

                // 自动启用
                EnablePack(gameDir, log);
            }
            catch (Exception ex)
            {
                log?.Invoke("[警告] 生成换肤资源包失败: " + ex.Message);
            }
        }

        /// <summary>把资源包写进 options.txt 的 resourcePacks，保留已有项并去重。</summary>
        private static void EnablePack(string gameDir, Action<string> log)
        {
            try
            {
                string optionsPath = Path.Combine(gameDir, "options.txt");
                // 首次启动时 MC 还没生成 options.txt（退出时才写），这里主动创建最小文件，
                // 让 resourcePacks 在本次启动就生效；MC 会读取并补全其余默认项。
                var lines = File.Exists(optionsPath)
                    ? File.ReadAllLines(optionsPath).ToList()
                    : new List<string>();
                string entry = "file/" + PackFolderName;
                int idx = lines.FindIndex(l => l.StartsWith("resourcePacks:", StringComparison.Ordinal));

                if (idx < 0)
                {
                    lines.Insert(0, $"resourcePacks:[\"vanilla\",\"{entry}\"]");
                }
                else
                {
                    string line = lines[idx];
                    string value = line.Substring("resourcePacks:".Length).Trim();
                    var packs = new List<string>();
                    if (value.StartsWith("[") && value.EndsWith("]"))
                    {
                        string inner = value.Substring(1, value.Length - 2);
                        packs.AddRange(inner.Split(',')
                            .Select(s => s.Trim().Trim('"'))
                            .Where(s => !string.IsNullOrEmpty(s)));
                    }
                    if (!packs.Contains("vanilla")) packs.Insert(0, "vanilla");
                    if (!packs.Contains(entry)) packs.Add(entry); // 放末尾=最高优先级
                    lines[idx] = "resourcePacks:[" + string.Join(",", packs.Select(p => "\"" + p + "\"")) + "]";
                }

                File.WriteAllLines(optionsPath, lines, new UTF8Encoding(false));
                log?.Invoke("[皮肤] 换肤资源包已自动启用");
            }
            catch (Exception ex)
            {
                log?.Invoke("[警告] 自动启用资源包失败: " + ex.Message);
            }
        }

        // 从版本名里提取 x.y.z 数字部分（兼容 "1.21.1-fabric" 之类）
        private static string ExtractNumber(string version)
        {
            if (string.IsNullOrEmpty(version)) return "0.0.0";
            var m = System.Text.RegularExpressions.Regex.Match(version, @"(\d+)\.(\d+)(?:\.(\d+))?");
            if (!m.Success) return "0.0.0";
            return m.Groups[3].Success
                ? $"{m.Groups[1]}.{m.Groups[2]}.{m.Groups[3]}"
                : $"{m.Groups[1]}.{m.Groups[2]}.0";
        }

        private static bool IsAtLeast(string version, string target)
        {
            string[] a = version.Split('.');
            string[] b = target.Split('.');
            for (int i = 0; i < 3; i++)
            {
                int ai = i < a.Length ? ParseInt(a[i]) : 0;
                int bi = i < b.Length ? ParseInt(b[i]) : 0;
                if (ai > bi) return true;
                if (ai < bi) return false;
            }
            return true;
        }

        private static int ParseInt(string s) => int.TryParse(s, out int v) ? v : 0;
    }
}
