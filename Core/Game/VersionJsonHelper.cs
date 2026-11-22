using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using CraftGate.Services;

namespace CraftGate.Core.Game
{
    /// <summary>
    /// 终极版：沿 inheritsFrom 链加载并深度合并版本 JSON。
    /// 支持 Fabric / Forge / OptiFine / Quilt / 各种魔改整合包继承。
    /// </summary>
    public static class VersionJsonHelper
    {
        public class MergedVersion
        {
            public JObject Json { get; set; }
            /// <summary>核心 jar 应该从哪个版本目录取（由 jar 字段决定）</summary>
            public string JarVersion { get; set; }
            /// <summary>当前合并后的版本号</summary>
            public string VersionId { get; set; }
            /// <summary>继承链，用于日志输出</summary>
            public List<string> InheritChain { get; set; }
        }

        public static MergedVersion Load(string versionId)
        {
            return LoadRecursive(versionId, 0, new List<string>());
        }

        private static MergedVersion LoadRecursive(string versionId, int depth, List<string> chain)
        {
            if (depth > 20)
                throw new InvalidOperationException("inheritsFrom 链过深，可能存在循环继承: " + string.Join(" -> ", chain) + " -> " + versionId);

            chain.Add(versionId);

            string jsonPath = Path.Combine(PathManager.GetVersionDir(versionId), versionId + ".json");
            if (!File.Exists(jsonPath))
                throw new FileNotFoundException("未找到版本 JSON: " + jsonPath);

            JObject current = JObject.Parse(File.ReadAllText(jsonPath));
            string parentId = current["inheritsFrom"] != null ? current["inheritsFrom"].ToString() : null;

            // ==========================================
            // 递归终止：原版（最底层）
            // ==========================================
            if (string.IsNullOrEmpty(parentId))
            {
                string baseJar = current["jar"] != null ? current["jar"].ToString() : null;
                if (string.IsNullOrEmpty(baseJar)) baseJar = versionId;

                return new MergedVersion
                {
                    Json = current,
                    JarVersion = baseJar,
                    VersionId = versionId,
                    InheritChain = chain
                };
            }

            // ==========================================
            // 递归加载父版本
            // ==========================================
            MergedVersion parent = LoadRecursive(parentId, depth + 1, chain);
            JObject merged = (JObject)parent.Json.DeepClone(); // 以父版本为基底

            // 1. 核心标识：子版本覆盖父版本
            merged["id"] = current["id"] != null ? current["id"].ToString() : versionId;
            if (current["mainClass"] != null) merged["mainClass"] = current["mainClass"];
            if (current["type"] != null) merged["type"] = current["type"];
            if (current["releaseTime"] != null) merged["releaseTime"] = current["releaseTime"];
            if (current["time"] != null) merged["time"] = current["time"];

            // 2. Java 版本需求：子版本没写，就继承父版本的
            if (current["javaVersion"] != null)
                merged["javaVersion"] = current["javaVersion"];

            // 3. 资源索引：若子版本有 assetIndex，则完全覆盖（连 URL 和 hash 一起）
            if (current["assetIndex"] != null)
                merged["assetIndex"] = current["assetIndex"];

            // 4. 资源目录：默认是 "minecraft"，子版本可能有自定义
            if (current["assets"] != null)
                merged["assets"] = current["assets"];

            // 5. 客户端 Jar 下载：如果子版本有（比如 OptiFine 魔改的客户端），则覆盖
            if (current["downloads"] != null && current["downloads"]["client"] != null)
            {
                if (merged["downloads"] == null) merged["downloads"] = new JObject();
                merged["downloads"]["client"] = current["downloads"]["client"];
            }

            // 6. 核心 Jar 来源：子 jar 字段优先，否则继承父
            string jarVersion = current["jar"] != null ? current["jar"].ToString() : null;
            if (string.IsNullOrEmpty(jarVersion)) jarVersion = parent.JarVersion;
            merged["jar"] = jarVersion;

            // 7. Libraries 合并（父在前，子在后，按 name 去重覆盖）
            JArray mergedLibs = MergeLibraries(parent.Json["libraries"] as JArray, current["libraries"] as JArray);
            if (mergedLibs != null) merged["libraries"] = mergedLibs;

            // 8. Arguments 合并
            // 官方规范：1.12 及以下用 minecraftArguments，1.13+ 用 arguments
            JObject parentArgs = parent.Json["arguments"] as JObject;
            JObject currentArgs = current["arguments"] as JObject;

            if (parentArgs != null || currentArgs != null)
            {
                JObject mergedArgs = new JObject();
                foreach (string key in new[] { "game", "jvm" })
                {
                    JArray pArr = parentArgs != null ? parentArgs[key] as JArray : null;
                    JArray cArr = currentArgs != null ? currentArgs[key] as JArray : null;
                    if (pArr == null && cArr == null) continue;

                    JArray arr = new JArray();
                    if (pArr != null) { foreach (var t in pArr) arr.Add(t.DeepClone()); }
                    if (cArr != null) { foreach (var t in cArr) arr.Add(t.DeepClone()); }
                    mergedArgs[key] = arr;
                }
                merged["arguments"] = mergedArgs;
            }

            // 老版本字段：子版本优先
            if (current["minecraftArguments"] != null)
                merged["minecraftArguments"] = current["minecraftArguments"];

            // 清理无用的中间字段
            merged.Remove("inheritsFrom");

            return new MergedVersion
            {
                Json = merged,
                JarVersion = jarVersion,
                VersionId = versionId,
                InheritChain = chain
            };
        }

        /// <summary>
        /// 智能合并 Libraries 数组
        /// </summary>
        private static JArray MergeLibraries(JArray parentLibs, JArray childLibs)
        {
            if (parentLibs == null && childLibs == null) return null;

            var result = new List<JToken>();
            // Key: "group:artifact[:classifier]" -> 在 result 中的索引
            var indexByKey = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            void AddLib(JToken lib)
            {
                string name = lib["name"] != null ? lib["name"].ToString() : null;
                string key = GetLibraryKey(name);

                if (key != null && indexByKey.ContainsKey(key))
                {
                    // 出现冲突：如果是子版本，子版本覆盖父版本（比如 Forge 换掉了原版的 Guava）
                    result[indexByKey[key]] = lib.DeepClone();
                }
                else
                {
                    if (key != null) indexByKey[key] = result.Count;
                    result.Add(lib.DeepClone());
                }
            }

            if (parentLibs != null) { foreach (var l in parentLibs) AddLib(l); }
            if (childLibs != null) { foreach (var l in childLibs) AddLib(l); }

            JArray arr = new JArray();
            foreach (var t in result) arr.Add(t);
            return arr;
        }

        /// <summary>
        /// 解析库的 Name，生成去重用的 Key
        /// 规范格式：group:artifact:version[:classifier][@extension]
        /// </summary>
        private static string GetLibraryKey(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;

            // 去掉 @jar 等后缀
            int atIdx = name.IndexOf('@');
            if (atIdx > 0) name = name.Substring(0, atIdx);

            string[] parts = name.Split(':');
            if (parts.Length < 3) return name; // 异常格式，返回原名

            // 核心去重 Key：group:artifact
            string key = parts[0] + ":" + parts[1];

            // 如果有 classifier (如 natives-windows)，加上它
            if (parts.Length >= 4)
            {
                key += ":" + parts[3];
            }
            return key;
        }
    }
}