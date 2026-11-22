using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace CraftGate.Services
{
    public class InstalledModInfo
    {
        public string FileName { get; set; }          // jar 文件名
        public string EnglishName { get; set; }       // 从 mod 元数据读到的英文名
        public string DisplayName { get; set; }       // 中文译名（命中词库/在线补漏），否则英文名
        public string Version { get; set; }
        public string Loader { get; set; }            // fabric / forge / neoforge
    }

    /// <summary>扫描游戏 mods 文件夹，读取 mod 元数据并用翻译服务显示中文名。</summary>
    public static class InstalledModService
    {
        /// <summary>扫描指定游戏目录下的 mods，返回已安装模组（中文名优先）。</summary>
        public static async Task<List<InstalledModInfo>> ScanAsync(string gameDir)
        {
            var result = new List<InstalledModInfo>();
            if (string.IsNullOrEmpty(gameDir)) return result;

            string modsDir = Path.Combine(gameDir, "mods");
            if (!Directory.Exists(modsDir)) return result;

            foreach (string jar in Directory.GetFiles(modsDir, "*.jar"))
            {
                var info = ReadModMeta(jar);
                if (info == null) continue;

                // 翻译：先词库即时命中，未命中异步补漏
                string zh = ModTranslationService.TranslateInstant(info.EnglishName);
                info.DisplayName = (zh != info.EnglishName)
                    ? zh
                    : await ModTranslationService.TranslateAsync(info.EnglishName).ConfigureAwait(false);
                if (string.IsNullOrEmpty(info.DisplayName)) info.DisplayName = info.EnglishName;

                result.Add(info);
            }

            return result.OrderBy(m => m.DisplayName).ToList();
        }

        private static InstalledModInfo ReadModMeta(string jarPath)
        {
            try
            {
                using (var zip = ZipFile.OpenRead(jarPath))
                {
                    // Fabric / Quilt
                    var fabricEntry = zip.GetEntry("fabric.mod.json");
                    if (fabricEntry != null)
                    {
                        JObject json;
                        using (var reader = new StreamReader(fabricEntry.Open()))
                            json = JObject.Parse(reader.ReadToEnd());

                        string name = json["name"]?.ToString();
                        string id = json["id"]?.ToString();
                        string ver = json["version"]?.ToString();
                        return new InstalledModInfo
                        {
                            FileName = Path.GetFileName(jarPath),
                            EnglishName = !string.IsNullOrEmpty(name) ? name : (id ?? Path.GetFileNameWithoutExtension(jarPath)),
                            Version = ver,
                            Loader = "Fabric"
                        };
                    }

                    // Forge / NeoForge (mods.toml)
                    var tomlEntry = zip.Entries.FirstOrDefault(e => e.FullName.EndsWith("mods.toml"));
                    if (tomlEntry != null)
                    {
                        string displayName = null;
                        string version = null;
                        using (var reader = new StreamReader(tomlEntry.Open()))
                        {
                            string line;
                            bool inMods = false;
                            while ((line = reader.ReadLine()) != null)
                            {
                                line = line.Trim();
                                if (line.StartsWith("[[mods]]")) { inMods = true; continue; }
                                if (line.StartsWith("[[") && !line.StartsWith("[[mods]]")) inMods = false;
                                if (!inMods) continue;
                                if (displayName == null && line.StartsWith("displayName"))
                                    displayName = line.Substring(line.IndexOf('=') + 1).Trim().Trim('"', '\'');
                                if (version == null && line.StartsWith("version"))
                                    version = line.Substring(line.IndexOf('=') + 1).Trim().Trim('"', '\'');
                            }
                        }
                        return new InstalledModInfo
                        {
                            FileName = Path.GetFileName(jarPath),
                            EnglishName = !string.IsNullOrEmpty(displayName) ? displayName : Path.GetFileNameWithoutExtension(jarPath),
                            Version = version,
                            Loader = "Forge"
                        };
                    }
                }
            }
            catch { }

            // 解析元数据失败：回退用文件名（去掉版本号部分）
            return new InstalledModInfo
            {
                FileName = Path.GetFileName(jarPath),
                EnglishName = CleanFileName(Path.GetFileNameWithoutExtension(jarPath)),
                Loader = "未知"
            };
        }

        private static string CleanFileName(string name)
        {
            // 常见文件名：sodium-fabric-mc1.21.1-0.6.13 → sodium
            string[] seps = { "-mc", "-fabric", "-forge", "-neoforge", "-quilt" };
            foreach (var sep in seps)
            {
                int idx = name.IndexOf(sep, StringComparison.OrdinalIgnoreCase);
                if (idx > 0) name = name.Substring(0, idx);
            }
            // 去掉末尾的纯版本号 "-1.2.3"
            var m = System.Text.RegularExpressions.Regex.Match(name, @"^(.*?)-\d+\.\d+");
            if (m.Success) name = m.Groups[1].Value;
            return name;
        }
    }
}
