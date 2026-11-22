using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using CraftGate.Services;
using Newtonsoft.Json.Linq;

namespace CraftGate.Core.Game
{
    public static class GameLauncher
    {
        private static readonly HttpClient _client = new HttpClient();

        private static void EnsureChineseLanguage(string gameDir)
        {
            string optionsPath = Path.Combine(gameDir, "options.txt");
            try
            {
                if (File.Exists(optionsPath))
                {
                    var lines = new List<string>(File.ReadAllLines(optionsPath));
                    bool hasLang = false;
                    for (int i = 0; i < lines.Count; i++)
                    {
                        if (lines[i].StartsWith("lang:"))
                        {
                            lines[i] = "lang:zh_cn";
                            hasLang = true;
                            break;
                        }
                    }
                    if (!hasLang) lines.Add("lang:zh_cn");
                    File.WriteAllLines(optionsPath, lines);
                }
                else
                {
                    File.WriteAllText(optionsPath, "version:3465\nlang:zh_cn\n");
                }
            }
            catch { }
        }

        private static string GetOfflineUuid(string name)
        {
            if (string.IsNullOrEmpty(name)) name = "Steve";
            string input = "OfflinePlayer:" + name;
            using (var md5 = MD5.Create())
            {
                byte[] data = md5.ComputeHash(Encoding.UTF8.GetBytes(input));
                data[6] = (byte)((data[6] & 0x0f) | 0x30);
                data[8] = (byte)((data[8] & 0x3f) | 0x80);
                return new Guid(data).ToString();
            }
        }

        private static void PatchVanillaJar(string jarPath, string skinPath, Action<string> logCallback)
        {
            if (!File.Exists(jarPath) || !File.Exists(skinPath)) return;

            try
            {
                byte[] skinBytes = File.ReadAllBytes(skinPath);
                using (var zip = ZipFile.Open(jarPath, ZipArchiveMode.Update))
                {
                    string[] pathsToTry = {
                        "assets/minecraft/textures/entity/steve.png",
                        "assets/minecraft/textures/entity/alex.png"
                    };

                    foreach (var path in pathsToTry)
                    {
                        var entry = zip.GetEntry(path);
                        if (entry != null) entry.Delete();

                        var newEntry = zip.CreateEntry(path);
                        using (var stream = newEntry.Open())
                        {
                            stream.Write(skinBytes, 0, skinBytes.Length);
                        }
                    }

                    var sigEntries = zip.Entries
                        .Where(e => e.FullName.StartsWith("META-INF/") &&
                                   (e.FullName.EndsWith(".SF") || e.FullName.EndsWith(".RSA") || e.FullName.EndsWith(".DSA")))
                        .ToList();

                    foreach (var sig in sigEntries) sig.Delete();

                    logCallback("[皮肤] 已成功替换原版 JAR 贴图！");
                }
            }
            catch (Exception ex)
            {
                logCallback("[警告] 替换核心 JAR 贴图失败: " + ex.Message);
            }
        }

        private static async Task EnsureCustomSkinLoaderAsync(string gameDir, string version, Action<string> logCallback)
        {
            try
            {
                string modsDir = Path.Combine(gameDir, "mods");
                Directory.CreateDirectory(modsDir);

                if (Directory.GetFiles(modsDir, "*CustomSkinLoader*.jar").Length > 0)
                    return;

                string mcVersion = version.Split('-')[0];
                string apiUrl = "https://api.modrinth.com/v2/project/customskinloader/version";
                var req = new HttpRequestMessage(HttpMethod.Get, apiUrl);
                req.Headers.Add("User-Agent", "CraftGate/1.0 (contact@craftgate.com)");

                string json = await _client.SendAsync(req).Result.Content.ReadAsStringAsync().ConfigureAwait(false);
                var versions = JArray.Parse(json);

                JObject bestVersion = null;
                foreach (var ver in versions)
                {
                    var gameVersions = ver["game_versions"]?.ToObject<List<string>>();
                    var loaders = ver["loaders"]?.ToObject<List<string>>();

                    if (gameVersions != null && loaders != null && gameVersions.Contains(mcVersion) && (loaders.Contains("fabric") || loaders.Contains("forge")))
                    {
                        bestVersion = ver as JObject;
                        break;
                    }
                }

                if (bestVersion == null && versions.Count > 0)
                    bestVersion = versions[0] as JObject;

                if (bestVersion?["files"] is JArray files)
                {
                    var primaryFile = files.FirstOrDefault(f => f["primary"]?.ToObject<bool>() == true) ?? files.FirstOrDefault();

                    if (primaryFile != null && primaryFile["url"] is JToken urlToken && primaryFile["filename"] is JToken fnToken)
                    {
                        string url = urlToken.ToString();
                        string filename = fnToken.ToString();
                        string savePath = Path.Combine(modsDir, filename);

                        var data = await _client.GetByteArrayAsync(url).ConfigureAwait(false);
                        File.WriteAllBytes(savePath, data);
                        logCallback("[皮肤] 内置换肤引擎配置完毕！");
                    }
                }
            }
            catch { }
        }

        private static async Task InjectLocalSkinAsync(string gameDir, string username, string localSkinPath, Action<string> logCallback)
        {
            try
            {
                string configDir = Path.Combine(gameDir, "config", "CustomSkinLoader");
                string cslDir = Path.Combine(configDir, "LocalSkin", "skins");
                Directory.CreateDirectory(cslDir);
                string targetFile = Path.Combine(cslDir, username + ".png");

                if (!string.IsNullOrEmpty(localSkinPath) && File.Exists(localSkinPath))
                {
                    await SkinService.GenerateGameSkinAsync(localSkinPath, targetFile).ConfigureAwait(false);
                }

                string configFile = Path.Combine(configDir, "CustomSkinLoader.json");
                string jsonContent = @"{
  ""version"": ""15.0.0"",
  ""loadlist"": [
    {
      ""name"": ""LocalSkin"",
      ""type"": ""Legacy"",
      ""checkPNG"": false,
      ""skin"": ""LocalSkin/skins/{USERNAME}.png"",
      ""model"": ""auto"",
      ""cape"": ""LocalSkin/capes/{USERNAME}.png"",
      ""elytra"": ""LocalSkin/elytras/{USERNAME}.png""
    }
  ]
}";
                File.WriteAllText(configFile, jsonContent);

                string[] cacheDirs = {
                    Path.Combine(configDir, "Cache"),
                    Path.Combine(configDir, "CachedImages"),
                    Path.Combine(configDir, "Temp"),
                    Path.Combine(gameDir, "cachedImages")
                };
                foreach (var dir in cacheDirs)
                {
                    if (Directory.Exists(dir)) { try { Directory.Delete(dir, true); } catch { } }
                }
            }
            catch { }
        }

        /// <summary>
        /// 高版本用资源包换肤时，清理旧的 CustomSkinLoader 残留：
        /// 它的配置带 cape/elytra 引用但没对应贴图，会导致外衣自动开启却显示异常。
        /// </summary>
        private static void CleanupLegacySkinConfig(string gameDir)
        {
            try
            {
                string cslConfigDir = Path.Combine(gameDir, "config", "CustomSkinLoader");
                if (Directory.Exists(cslConfigDir))
                {
                    // 只删配置文件与本地皮肤副本，保留目录结构以防用户自有数据
                    string cfg = Path.Combine(cslConfigDir, "CustomSkinLoader.json");
                    if (File.Exists(cfg)) { try { File.Delete(cfg); } catch { } }
                    string localSkinDir = Path.Combine(cslConfigDir, "LocalSkin");
                    if (Directory.Exists(localSkinDir)) { try { Directory.Delete(localSkinDir, true); } catch { } }
                }

                // 删除自动注入的 CustomSkinLoader mod（高版本资源包方案不需要）
                string modsDir = Path.Combine(gameDir, "mods");
                if (Directory.Exists(modsDir))
                {
                    foreach (var f in Directory.GetFiles(modsDir, "*CustomSkinLoader*.jar"))
                    {
                        try { File.Delete(f); } catch { }
                    }
                }
            }
            catch { }
        }

        public static async Task<bool> StartAsync(
            string version, string javaPath, string playerName, // 注意：这里的 javaPath 参数外部仍然需要传入（作为兜底），但内部会优先使用自动匹配的
            string additionalArgs, bool disableMods,
            Action<string> logCallback, Action onGameExit)
        {
            bool modsMoved = false;
            string modsDir = null;
            string modsDirDisabled = null;

            try
            {
                VersionJsonHelper.MergedVersion merged = VersionJsonHelper.Load(version);
                JObject json = merged.Json;

                if (merged.InheritChain.Count > 1)
                    logCallback("[版本] 继承链: " + string.Join(" -> ", merged.InheritChain));

                // ==========================================
                // 👇 核心新增：自动匹配 Java 版本
                // ==========================================
                int requiredJava = JavaManager.GetRequiredJavaMajorVersion(json);
                string actualJavaPath = javaPath;

                try
                {
                    // 优先使用启动器自动管理的 JRE
                    actualJavaPath = await JavaManager.EnsureJavaAsync(requiredJava, logCallback).ConfigureAwait(false);
                    logCallback($"[Java] 成功匹配并加载 JRE {requiredJava}，路径: {actualJavaPath}");
                }
                catch
                {
                    // 如果下载失败或异常，回退到参数传入的 Java（系统的 Java）
                    logCallback($"[Java] 自动获取 JRE {requiredJava} 失败，正在回退到系统 Java 环境...");
                    if (string.IsNullOrEmpty(actualJavaPath) || !File.Exists(actualJavaPath))
                    {
                        logCallback("[错误] 系统 Java 环境也无效，启动中止！");
                        return false;
                    }
                }

                string versionDir = PathManager.GetVersionDir(version);
                string mainClass = json["mainClass"] is JToken mainToken ? mainToken.ToString() : "net.minecraft.client.main.Main";
                string assetIndex = (json["assetIndex"] is JToken aiToken && aiToken["id"] is JToken aiIdToken) ? aiIdToken.ToString() : "legacy";

                string jarVersion = merged.JarVersion;
                string jarPath = Path.Combine(PathManager.GetVersionDir(jarVersion), jarVersion + ".jar");

                var classpathEntries = new List<string>();
                var nativesList = new List<string>();

                JArray libraries = json["libraries"] as JArray;
                if (libraries != null)
                {
                    foreach (var lib in libraries)
                    {
                        JToken rules = lib["rules"];
                        if (rules != null && !CheckRulesAllowed(rules)) continue;

                        string libName = lib["name"] is JToken nameToken ? nameToken.ToString() : "unknown";
                        string artifactPath = (lib["downloads"] is JToken downloads && downloads["artifact"] is JToken artifact && artifact["path"] is JToken pathToken) ? pathToken.ToString() : null;

                        // 【自愈】natives-windows 含 lwjgl.dll；老实例可能没下这个 jar，
                        // 启动时若缺就按 json 里的 url 现补下，否则 lwjgl.dll 永远找不到。
                        JToken nativeToken = lib["downloads"]?["classifiers"]?["natives-windows"];
                        string nativePath = nativeToken?["path"]?.ToString();
                        string nativeUrl = nativeToken?["url"]?.ToString();
                        if (!string.IsNullOrEmpty(nativePath) && !string.IsNullOrEmpty(nativeUrl))
                        {
                            string np = Path.Combine(PathManager.LibrariesDir, nativePath.Replace('/', '\\'));
                            if (!File.Exists(np))
                            {
                                try
                                {
                                    Directory.CreateDirectory(Path.GetDirectoryName(np));
                                    var bytes = _client.GetByteArrayAsync(nativeUrl).GetAwaiter().GetResult();
                                    File.WriteAllBytes(np, bytes);
                                }
                                catch { }
                            }
                            if (File.Exists(np) && !nativesList.Contains(np)) nativesList.Add(np);
                        }

                        string fullPath = null;
                        if (!string.IsNullOrEmpty(artifactPath))
                            fullPath = Path.Combine(PathManager.LibrariesDir, artifactPath.Replace('/', '\\'));
                        else
                        {
                            string derived = DeriveLibraryPath(libName);
                            if (derived != null) fullPath = Path.Combine(PathManager.LibrariesDir, derived);
                        }

                        if (fullPath != null && File.Exists(fullPath))
                        {
                            bool isNative = libName.Contains(":natives-windows") || fullPath.Contains("-natives-windows.jar")
                                            || (!string.IsNullOrEmpty(nativePath) && fullPath.Replace('/', '\\').EndsWith(nativePath.Replace('/', '\\')));
                            if (isNative)
                            {
                                if (!nativesList.Contains(fullPath)) nativesList.Add(fullPath);
                            }
                            else
                                classpathEntries.Add(fullPath);
                        }
                    }
                }

                if (File.Exists(jarPath)) classpathEntries.Add(jarPath);
                string classPath = string.Join(";", classpathEntries);

                string nativesDir = Path.Combine(versionDir, "natives");
                try { if (Directory.Exists(nativesDir)) Directory.Delete(nativesDir, true); } catch { }
                Directory.CreateDirectory(nativesDir);

                foreach (var nativeJar in nativesList)
                {
                    try
                    {
                        using (var zip = ZipFile.OpenRead(nativeJar))
                        {
                            foreach (var entry in zip.Entries)
                            {
                                if (entry.FullName.EndsWith(".dll") || entry.FullName.EndsWith(".so") || entry.FullName.EndsWith(".dylib"))
                                {
                                    string dest = Path.Combine(nativesDir, Path.GetFileName(entry.FullName));
                                    if (!File.Exists(dest)) entry.ExtractToFile(dest, true);
                                }
                            }
                        }
                    }
                    catch { }
                }

                // 账户级隔离：每个账户独立的 saves/mods/config，游戏文件仍全局共享
                string gameDirToUse = PathManager.GetCurrentAccountGameDir(version);
                Directory.CreateDirectory(gameDirToUse);
                EnsureChineseLanguage(gameDirToUse);
                logCallback($"[隔离] 当前账户存档目录: {gameDirToUse}");

                bool hasCustomSkin = !string.IsNullOrEmpty(AccountService.CurrentAccount.SkinPath) && File.Exists(AccountService.CurrentAccount.SkinPath);

                if (hasCustomSkin)
                {
                    if (SkinResourcePackService.UseResourcePack(version))
                    {
                        // 1.19.3+：用内置换肤资源包（MC 原生机制，跨版本，保留透明，不依赖 mod）
                        SkinResourcePackService.EnsurePack(gameDirToUse, version,
                            AccountService.CurrentAccount.SkinPath, logCallback);
                        // 清理高版本可能残留的旧 CSL 配置——它带 cape/elytra 引用，
                        // 但我们的换肤没生成外衣贴图，会导致"外衣自动开启却无贴图/变黑"。
                        CleanupLegacySkinConfig(gameDirToUse);
                    }
                    else if (version.Contains("fabric") || version.Contains("forge"))
                    {
                        // ≤1.19.2 + Mod 版：保持原有 CustomSkinLoader 方案
                        await EnsureCustomSkinLoaderAsync(gameDirToUse, version, logCallback).ConfigureAwait(false);
                        await InjectLocalSkinAsync(gameDirToUse, playerName, AccountService.CurrentAccount.SkinPath, logCallback).ConfigureAwait(false);
                    }
                    else
                    {
                        // ≤1.19.2 + 原版：保持原有替换 JAR 贴图方案
                        string mergedSkinPath = Path.Combine(PathManager.SkinsCacheDir, "merged_vanilla_skin.png");
                        await SkinService.GenerateGameSkinAsync(AccountService.CurrentAccount.SkinPath, mergedSkinPath).ConfigureAwait(false);
                        PatchVanillaJar(jarPath, mergedSkinPath, logCallback);
                    }
                }

                modsDir = Path.Combine(gameDirToUse, "mods");
                modsDirDisabled = Path.Combine(gameDirToUse, "mods.disabled");
                if (disableMods && Directory.Exists(modsDir))
                {
                    try
                    {
                        if (Directory.Exists(modsDirDisabled)) Directory.Delete(modsDirDisabled, true);
                        Directory.Move(modsDir, modsDirDisabled);
                        modsMoved = true;
                        logCallback("[设置] 安全/纯净模式已开启，Mods 已临时禁用。");
                    }
                    catch (Exception ex) { logCallback("[警告] 禁用 Mods 失败: " + ex.Message); }
                }

                int maxMem = ConfigManager.Settings.MaxMemoryMB;
                if (maxMem < 1024) maxMem = 1024;

                string offlineUuid = GetOfflineUuid(playerName);

                // 正版账户用真实 token/uuid/user_type=msa；离线账户保持原样
                var acc = AccountService.CurrentAccount;
                bool isMicrosoft = acc.IsMicrosoft && !string.IsNullOrEmpty(acc.AccessToken);
                string authUuid = isMicrosoft ? acc.Uuid : offlineUuid;
                string accessToken = isMicrosoft ? acc.AccessToken : "0";
                string userType = isMicrosoft ? "msa" : "legacy";
                string clientId = isMicrosoft ? acc.Uuid : "0";

                var placeholders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    { "auth_player_name", playerName },
                    { "version_name", version },
                    { "game_directory", gameDirToUse },
                    { "assets_root", PathManager.AssetsDir },
                    { "assets_index_name", assetIndex },
                    { "auth_uuid", authUuid },
                    { "auth_access_token", accessToken },
                    { "auth_session", accessToken },
                    { "user_type", userType },
                    { "version_type", json["type"] is JToken typeToken ? typeToken.ToString() : "release" },
                    { "natives_directory", nativesDir },
                    { "launcher_name", "CraftGate" },
                    { "launcher_version", "1.0" },
                    { "classpath", classPath },
                    { "classpath_separator", ";" },
                    { "library_directory", PathManager.LibrariesDir },
                    { "primary_jar", jarPath },
                    { "clientid", clientId },
                    { "auth_xuid", "0" },
                    { "user_properties", "{}" },
                    { "game_assets", PathManager.AssetsDir },
                    { "resolution_width", "1280" },
                    { "resolution_height", "720" }
                };

                JObject jsonArgs = json["arguments"] as JObject;
                string jvmArgs = BuildJvmArguments(jsonArgs, placeholders);
                string legacyMcArgs = json["minecraftArguments"] is JToken mcArgsToken ? mcArgsToken.ToString() : null;
                string gameArgs = BuildGameArguments(jsonArgs, legacyMcArgs, placeholders);

                if (string.IsNullOrEmpty(gameArgs))
                {
                    gameArgs = $"--username {playerName} --version {version} --gameDir \"{gameDirToUse}\" --assetsDir \"{PathManager.AssetsDir}\" --assetIndex {assetIndex} --uuid {authUuid} --accessToken {accessToken} --userType {userType}";
                }

                var sb = new StringBuilder();
                sb.Append("-Xmx").Append(maxMem).Append("m -Xms1024m ");
                sb.Append("-Djava.library.path=\"").Append(nativesDir).Append("\" ");
                sb.Append("-Djava.io.tmpdir=\"").Append(nativesDir).Append("\" ");
                sb.Append("-Dfile.encoding=UTF-8 -Dstdout.encoding=UTF-8 -Dstderr.encoding=UTF-8 ");

                if (!string.IsNullOrEmpty(jvmArgs)) sb.Append(jvmArgs).Append(' ');
                sb.Append("-cp \"").Append(classPath).Append("\" ");
                sb.Append(mainClass).Append(' ');
                sb.Append(gameArgs);
                if (!string.IsNullOrEmpty(additionalArgs)) sb.Append(' ').Append(additionalArgs);

                string arguments = sb.ToString();
                logCallback("[调试] 完整启动命令: \n" + actualJavaPath + " " + arguments);

                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = actualJavaPath, // 👈 使用自动匹配的 Java 路径
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WorkingDirectory = gameDirToUse,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };

                Process process = new Process { StartInfo = psi };
                process.OutputDataReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) logCallback("[游戏] " + e.Data); };
                process.ErrorDataReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) logCallback("[游戏异常] " + e.Data); };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                logCallback("[成功] 游戏已启动！PID: " + process.Id);

                bool capturedModsMoved = modsMoved;
                string capturedModsDir = modsDir;
                string capturedModsDisabled = modsDirDisabled;

                _ = Task.Run(() =>
                {
                    try { process.WaitForExit(); }
                    catch { }
                    finally
                    {
                        if (capturedModsMoved && Directory.Exists(capturedModsDisabled))
                        {
                            try
                            {
                                if (Directory.Exists(capturedModsDir)) Directory.Delete(capturedModsDir, true);
                                Directory.Move(capturedModsDisabled, capturedModsDir);
                                logCallback("[设置] 游戏退出，Mods 已恢复。");
                            }
                            catch { }
                        }
                        process.Dispose();
                        onGameExit?.Invoke();
                    }
                });

                return true;
            }
            catch (Exception ex)
            {
                if (modsMoved && Directory.Exists(modsDirDisabled))
                {
                    try
                    {
                        if (Directory.Exists(modsDir)) Directory.Delete(modsDir, true);
                        Directory.Move(modsDirDisabled, modsDir);
                    }
                    catch { }
                }
                logCallback("[错误] 启动游戏失败: " + ex.Message);
                return false;
            }
        }

        private static string BuildJvmArguments(JObject args, Dictionary<string, string> placeholders)
        {
            JArray arr = args?["jvm"] as JArray;
            if (arr == null) return "";
            var parts = new List<string>();
            for (int i = 0; i < arr.Count; i++)
            {
                // 一个 token 可能展开成多个参数（规则 value 是数组时），必须逐个处理
                var tokens = EvaluateArgumentTokens(arr[i], placeholders);
                if (tokens.Count == 0) continue;
                // -cp / -classpath 后面的 ${classpath} 是单独一个参数，跳过这对
                if (tokens[0] == "-cp" || tokens[0] == "-classpath") { i++; continue; }
                foreach (string raw in tokens)
                {
                    if (raw.StartsWith("-Djava.library.path") || raw.StartsWith("-Djava.io.tmpdir")
                        || raw.StartsWith("-Xmx") || raw.StartsWith("-Xms")) continue;
                    parts.Add(QuoteArgument(raw));
                }
            }
            return string.Join(" ", parts);
        }

        private static string BuildGameArguments(JObject args, string legacyMcArgs, Dictionary<string, string> placeholders)
        {
            JArray arr = args?["game"] as JArray;
            if (arr != null)
            {
                var parts = new List<string>();
                foreach (var token in arr)
                {
                    foreach (string raw in EvaluateArgumentTokens(token, placeholders))
                        parts.Add(QuoteArgument(raw));
                }
                return string.Join(" ", parts);
            }
            if (!string.IsNullOrEmpty(legacyMcArgs)) return ReplacePlaceholders(legacyMcArgs, placeholders);
            return null;
        }

        /// <summary>把一个 JSON 参数 token 展开成多个独立参数（规则 value 为数组时逐个返回，绝不 Join）。</summary>
        private static List<string> EvaluateArgumentTokens(JToken token, Dictionary<string, string> placeholders)
        {
            var result = new List<string>();
            if (token == null) return result;
            if (token.Type == JTokenType.String)
            {
                result.Add(ReplacePlaceholders(token.ToString(), placeholders));
                return result;
            }
            if (token.Type == JTokenType.Object)
            {
                JToken rules = token["rules"];
                if (rules != null && !CheckRulesAllowed(rules)) return result;
                JToken value = token["value"];
                if (value == null) return result;
                if (value.Type == JTokenType.String) result.Add(ReplacePlaceholders(value.ToString(), placeholders));
                else if (value.Type == JTokenType.Array)
                    foreach (var v in value) result.Add(ReplacePlaceholders(v.ToString(), placeholders));
            }
            return result;
        }

        /// <summary>对一个已经确定的"单个参数"加引号：含空格才加，且保留参数边界。</summary>
        private static string QuoteArgument(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            if (s.StartsWith("\"") && s.EndsWith("\"")) return s; // 已加引号
            if (s.IndexOf(' ') >= 0) return "\"" + s.Replace("\"", "\\\"") + "\"";
            return s;
        }

        private static string ReplacePlaceholders(string s, Dictionary<string, string> map)
        {
            if (string.IsNullOrEmpty(s)) return s;
            foreach (var kv in map)
            {
                // 占位符值不再内层加引号，统一由 QuoteArgument 在参数级别加，避免 -Dkey="a b" 式错误
                s = s.Replace("${" + kv.Key + "}", kv.Value ?? "");
            }
            return s;
        }

        private static string QuoteIfNeeded(string v)
        {
            if (string.IsNullOrEmpty(v)) return v;
            if (v.IndexOf(' ') >= 0 && !v.StartsWith("\"")) return "\"" + v + "\"";
            return v;
        }

        private static string DeriveLibraryPath(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            string[] parts = name.Split(':');
            if (parts.Length < 3) return null;
            string group = parts[0].Replace('.', '\\');
            string artifact = parts[1];
            string ver = parts[2];
            string classifier = parts.Length >= 4 ? "-" + parts[3] : "";
            return Path.Combine(group, artifact, ver, artifact + "-" + ver + classifier + ".jar");
        }

        private static bool CheckRulesAllowed(JToken rules)
        {
            bool allowed = false;
            bool is64 = System.Environment.Is64BitProcess;
            string currentArch = is64 ? "x64" : "x86";
            foreach (var rule in rules)
            {
                string action = rule["action"] is JToken actionToken ? actionToken.ToString() : null;
                string osName = (rule["os"] is JToken osToken && osToken["name"] is JToken osNameToken) ? osNameToken.ToString() : null;
                string osArch = (rule["os"] is JToken osTok2 && osTok2["arch"] is JToken archTok) ? archTok.ToString() : null;
                if (rule["features"] != null) continue;
                // os.name 不符合 → 跳过该规则
                if (osName != null && osName != "windows") continue;
                // os.arch 不符合 → 跳过该规则（如 x86-only 规则在 x64 上不生效）
                if (osArch != null && osArch != currentArch) continue;
                if (action == "allow") allowed = true;
                else if (action == "disallow") allowed = false;
            }
            return allowed;
        }
    }
}