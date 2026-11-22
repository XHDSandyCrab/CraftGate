using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using CraftGate.Services;

namespace CraftGate.Core.Game
{
    public static class JavaManager
    {
        private static readonly HttpClient _client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        private static readonly string RuntimesRoot = Path.Combine(PathManager.LauncherDir, "runtimes");

        public static int GetRequiredJavaMajorVersion(JObject versionJson)
        {
            var majorVersionToken = versionJson["javaVersion"]?["majorVersion"];
            if (majorVersionToken != null && int.TryParse(majorVersionToken.ToString(), out int major))
            {
                return major;
            }

            string id = versionJson["id"]?.ToString() ?? "";
            if (id.StartsWith("26.")) return 25;
            if (id.StartsWith("1.20.5") || id.StartsWith("1.20.6") || id.StartsWith("1.21")) return 21;
            if (id.StartsWith("1.18") || id.StartsWith("1.19") || id.StartsWith("1.20")) return 17;
            if (id.StartsWith("1.17")) return 16;
            return 8;
        }

        public static async Task<string> EnsureJavaAsync(int majorVersion, Action<string> logCallback)
        {
            string targetDir = Path.Combine(RuntimesRoot, $"jre-{majorVersion}");

            string existingExe = FindJavaExecutable(targetDir);
            if (!string.IsNullOrEmpty(existingExe))
            {
                logCallback($"[Java] 已找到内置于启动器的 JRE {majorVersion}");
                return existingExe;
            }

            logCallback($"[Java] 未检测到 JRE {majorVersion}，正在为您自动下载（这可能需要几分钟）...");
            Directory.CreateDirectory(targetDir);

            string tempZipPath = Path.Combine(targetDir, $"jre-{majorVersion}.zip");

            try
            {
                // 【修复】多源下载：官方 Adoptium API 会跳到 GitHub（国内极慢/卡死），
                // 优先用清华 TUNA 镜像（国内快），失败再回退官方。每个源独立短超时。
                bool downloaded = false;
                Exception lastError = null;

                // 源1：清华 TUNA 镜像（解析目录页拿到最新 zip 文件名）
                try
                {
                    string mirrorDir = $"https://mirrors.tuna.tsinghua.edu.cn/Adoptium/{majorVersion}/jre/x64/windows/";
                    logCallback($"[Java] 正在从国内镜像获取 JRE {majorVersion} 下载链接...");
                    using (var page = await _client.GetAsync(mirrorDir).ConfigureAwait(false))
                    {
                        page.EnsureSuccessStatusCode();
                        string html = await page.Content.ReadAsStringAsync().ConfigureAwait(false);
                        var m = System.Text.RegularExpressions.Regex.Matches(html,
                            @"OpenJDK" + majorVersion + @"U-jre_x64_windows_hotspot_[0-9._]+\.zip");
                        if (m.Count > 0)
                        {
                            string fileName = m[m.Count - 1].Value; // 最后一个通常是最新
                            string mirrorUrl = mirrorDir + fileName;
                            logCallback($"[Java] 国内镜像下载: {fileName}");
                            await DownloadWithProgressAsync(mirrorUrl, tempZipPath, majorVersion, logCallback).ConfigureAwait(false);
                            downloaded = true;
                        }
                    }
                }
                catch (Exception ex) { lastError = ex; logCallback($"[Java] 国内镜像失败，尝试官方源: {ex.Message}"); }

                // 源2：官方 Adoptium API（可能跳 GitHub）
                if (!downloaded)
                {
                    try
                    {
                        string officialUrl = $"https://api.adoptium.net/v3/binary/latest/{majorVersion}/ga/windows/x64/jre/hotspot/normal/eclipse";
                        logCallback($"[Java] 正在从官方源下载 JRE {majorVersion}（可能需要几分钟）...");
                        await DownloadWithProgressAsync(officialUrl, tempZipPath, majorVersion, logCallback).ConfigureAwait(false);
                        downloaded = true;
                    }
                    catch (Exception ex) { lastError = ex; }
                }

                if (!downloaded) throw lastError ?? new Exception("所有下载源均失败");

                logCallback($"[Java] JRE {majorVersion} 下载完成，正在解压...");

                // 👇 修复：使用自定义的兼容 .NET Framework 的安全解压方法
                ExtractZipSafe(tempZipPath, targetDir);

                try { File.Delete(tempZipPath); } catch { }

                string newExe = FindJavaExecutable(targetDir);
                if (string.IsNullOrEmpty(newExe))
                {
                    throw new Exception("解压后未能找到 java.exe，可能下载的压缩包格式有误。");
                }

                logCallback($"[Java] JRE {majorVersion} 安装成功！");
                return newExe;
            }
            catch (Exception ex)
            {
                logCallback($"[Java] 自动下载 JRE {majorVersion} 失败: {ex.Message}");
                throw;
            }
        }

        /// <summary>流式下载 zip 并输出进度百分比。</summary>
        private static async Task DownloadWithProgressAsync(string url, string savePath, int major, Action<string> logCallback)
        {
            using (var response = await _client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                long total = response.Content.Headers.ContentLength ?? 0;
                using (var src = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                using (var fs = new FileStream(savePath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
                {
                    var buffer = new byte[81920];
                    long read = 0;
                    int n;
                    int lastPct = -5;
                    while ((n = await src.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) > 0)
                    {
                        await fs.WriteAsync(buffer, 0, n).ConfigureAwait(false);
                        read += n;
                        int pct = total > 0 ? (int)(read * 100 / total) : (int)(read / 1024 / 1024);
                        if (pct - lastPct >= 10)
                        {
                            lastPct = pct;
                            logCallback(total > 0
                                ? $"[Java] 下载 JRE {major}: {pct}% ({read / 1024 / 1024}/{total / 1024 / 1024} MB)"
                                : $"[Java] 下载 JRE {major}: {read / 1024 / 1024} MB");
                        }
                    }
                }
            }
        }

        // 👇 新增：兼容 .NET Framework 的覆盖解压方法
        private static void ExtractZipSafe(string zipPath, string targetDir)
        {
            using (var archive = ZipFile.OpenRead(zipPath))
            {
                foreach (var entry in archive.Entries)
                {
                    // 如果是文件夹（Entry.Name 为空）
                    if (string.IsNullOrEmpty(entry.Name))
                    {
                        Directory.CreateDirectory(Path.Combine(targetDir, entry.FullName));
                        continue;
                    }

                    string destPath = Path.Combine(targetDir, entry.FullName);
                    Directory.CreateDirectory(Path.GetDirectoryName(destPath));

                    // 如果文件已存在，先尝试删除（防止文件被占用导致解压失败）
                    if (File.Exists(destPath))
                    {
                        try { File.Delete(destPath); } catch { }
                    }

                    // 解压单个文件并覆盖
                    entry.ExtractToFile(destPath, true);
                }
            }
        }

        private static string FindJavaExecutable(string dir)
        {
            if (!Directory.Exists(dir)) return null;

            string directPath = Path.Combine(dir, "bin", "java.exe");
            if (File.Exists(directPath)) return directPath;

            var subDirs = Directory.GetDirectories(dir);
            foreach (var subDir in subDirs)
            {
                string path = Path.Combine(subDir, "bin", "java.exe");
                if (File.Exists(path)) return path;
            }

            var allExes = Directory.GetFiles(dir, "java.exe", SearchOption.AllDirectories);
            if (allExes.Length > 0) return allExes[0];

            return null;
        }
    }
}