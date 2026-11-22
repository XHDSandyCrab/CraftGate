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
        private static readonly HttpClient _client = new HttpClient();
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

            string downloadUrl = $"https://api.adoptium.net/v3/binary/latest/{majorVersion}/ga/windows/x64/jre/hotspot/normal/eclipse";
            string tempZipPath = Path.Combine(targetDir, $"jre-{majorVersion}.zip");

            try
            {
                using (var response = await _client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();

                    using (var fs = new FileStream(tempZipPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        await response.Content.CopyToAsync(fs).ConfigureAwait(false);
                    }
                }

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