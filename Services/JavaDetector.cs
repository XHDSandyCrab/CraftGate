using System;
using System.IO;

namespace CraftGate.Services
{
    public static class JavaDetector
    {
        /// <summary>
        /// 根据 Minecraft 版本字符串推断所需 Java 主版本（用于向导"轻量级java"自动匹配显示）。
        /// 规则：1.21+/快照26+ → 21；1.18~1.20.x → 17；1.17 → 16；1.16 及更早 → 8。
        /// </summary>
        public static int GetRequiredJavaMajorForMc(string mcVersion)
        {
            if (string.IsNullOrWhiteSpace(mcVersion)) return 8;
            string v = mcVersion.Trim();

            // ① 快照版本：YYwWWx（如 23w13a_or_b、24w45a）→ 按快照年份推断
            //    2024+ → Java 21；2022~2023 → Java 17；2021 → Java 16；2020 及更早 → Java 8
            var snap = System.Text.RegularExpressions.Regex.Match(v, @"^(\d{2})w\d{2}", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (snap.Success)
            {
                int year = int.Parse(snap.Groups[1].Value);
                if (year >= 24) return 21;
                if (year >= 22) return 17;
                if (year == 21) return 16;
                return 8;
            }

            // ② 愚人节/特殊快照串：20w14infinite、1.16-alpha.20.13.inf、rd-132211 等
            //    含 20w / 20.13 / rd- 视为 1.16 时代 → Java 8
            if (System.Text.RegularExpressions.Regex.IsMatch(v, @"(20w|20\.13|rd-|infinite)", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                return 8;

            // ③ 正式版 1.x.y
            // 新编号方案 26.x（如 26.3）→ Java 25（Mojang 官方 javaVersion.majorVersion=25）
            var newScheme = System.Text.RegularExpressions.Regex.Match(v, @"^(\d{2})\.\d+", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (newScheme.Success)
            {
                int ny = int.Parse(newScheme.Groups[1].Value);
                if (ny >= 26) return 25;   // MC 26.x 起升级到 Java 25
                return 21;                 // 25.x 及更早的新编号 → Java 21
            }

            var m = System.Text.RegularExpressions.Regex.Match(v, @"(\d+)\.(\d+)(?:\.(\d+))?");
            if (!m.Success) return 8;
            int major = int.Parse(m.Groups[1].Value);
            int minor = int.Parse(m.Groups[2].Value);
            int patch = m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : 0;
            if (major > 1) return 21;          // 形如 25.1 的新编号
            if (minor >= 21) return 21;
            if (minor == 20 && patch >= 5) return 21;  // 1.20.5+ 需要 Java 21（原代码漏判，修正）
            if (minor >= 18) return 17;
            if (minor == 17) return 16;
            return 8;
        }

        public static string DetectJava()
        {
            // 1. 优先检查环境变量 JAVA_HOME
            string javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
            if (!string.IsNullOrEmpty(javaHome))
            {
                string exe = Path.Combine(javaHome, "bin", "java.exe");
                if (File.Exists(exe)) return exe;
            }

            // 2. 检查环境变量 Path 中的 java
            string pathEnv = Environment.GetEnvironmentVariable("Path");
            if (!string.IsNullOrEmpty(pathEnv))
            {
                foreach (var path in pathEnv.Split(';'))
                {
                    try
                    {
                        string exe = Path.Combine(path.Trim(), "java.exe");
                        if (File.Exists(exe)) return exe;
                    }
                    catch { }
                }
            }

            // 3. 常见安装根目录下的所有 java.exe（jdk-17 / jdk-21.0.3 / jre1.8.0_391 等通配）
            foreach (var found in ScanCommonRoots())
                if (File.Exists(found)) return found;

            // 4. 注册表兜底
            try
            {
                using (var reg = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\JavaSoft\Java Development Kit"))
                {
                    if (reg != null)
                        foreach (var sub in reg.GetSubKeyNames())
                        {
                            using (var sk = reg.OpenSubKey(sub))
                            {
                                string home = sk?.GetValue("JavaHome") as string;
                                if (!string.IsNullOrEmpty(home))
                                {
                                    string exe = Path.Combine(home, "bin", "java.exe");
                                    if (File.Exists(exe)) return exe;
                                }
                            }
                        }
                }
            }
            catch { }

            return null; // 没找到
        }

        /// <summary>扫描常见 JDK/JRE 安装根目录，返回所有可能的 java.exe 路径。</summary>
        private static System.Collections.Generic.IEnumerable<string> ScanCommonRoots()
        {
            var roots = new System.Collections.Generic.List<string>();
            foreach (var pf in new[] {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles).Replace("Program Files", "Program Files (x86)")
            })
            {
                if (string.IsNullOrEmpty(pf)) continue;
                roots.Add(Path.Combine(pf, "Java"));
                roots.Add(Path.Combine(pf, "Eclipse Adoptium"));
                roots.Add(Path.Combine(pf, "Microsoft"));
                roots.Add(Path.Combine(pf, "BellSoft"));
                roots.Add(Path.Combine(pf, "Amazon Corretto"));
                roots.Add(Path.Combine(pf, "Zulu"));
            }
            foreach (var root in roots)
            {
                if (!Directory.Exists(root)) continue;
                // 每个安装目录直接查 bin\java.exe（如 Eclipse Adoptium\jdk-25-hotspot\bin\java.exe）
                foreach (var dir in Directory.GetDirectories(root))
                {
                    string exe = Path.Combine(dir, "bin", "java.exe");
                    if (File.Exists(exe)) yield return exe;
                    // 再下一层（如 Microsoft\jdk-25.0.1\bin）
                    foreach (var sub in Directory.GetDirectories(dir))
                    {
                        string exe2 = Path.Combine(sub, "bin", "java.exe");
                        if (File.Exists(exe2)) yield return exe2;
                    }
                }
            }
        }

        /// <summary>读取指定 java.exe 的主版本号（如 25），失败返回 0。</summary>
        public static int GetJavaMajor(string javaExe)
        {
            if (string.IsNullOrEmpty(javaExe) || !File.Exists(javaExe)) return 0;
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo(javaExe, "-version")
                {
                    RedirectStandardError = true, RedirectStandardOutput = true,
                    UseShellExecute = false, CreateNoWindow = true
                };
                using (var p = System.Diagnostics.Process.Start(psi))
                {
                    string output = p.StandardError.ReadToEnd() + p.StandardOutput.ReadToEnd();
                    p.WaitForExit(3000);
                    // openjdk version "25.0.1" / java version "1.8.0_391"
                    var m = System.Text.RegularExpressions.Regex.Match(output, @"version\s+""(\d+)(?:\.(\d+))?");
                    if (!m.Success) return 0;
                    int first = int.Parse(m.Groups[1].Value);
                    return first == 1 && m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : first;
                }
            }
            catch { return 0; }
        }
    }
}