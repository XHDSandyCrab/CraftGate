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
            var m = System.Text.RegularExpressions.Regex.Match(v, @"(\d+)\.(\d+)");
            if (!m.Success) return 8;
            int major = int.Parse(m.Groups[1].Value);
            int minor = int.Parse(m.Groups[2].Value);
            if (major > 1) return 21;          // 形如 26.1 的新编号
            if (minor >= 21) return 21;
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

            // 3. 检查默认安装路径（兜底）
            string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string[] commonPaths = {
                Path.Combine(pf, "Java", "jdk-21", "bin", "java.exe"),
                Path.Combine(pf, "Java", "jdk-17", "bin", "java.exe"),
                Path.Combine(pf, "Java", "jdk1.8.0_291", "bin", "java.exe"),
                Path.Combine(pf, "BellSoft", "LibericaJDK-17", "bin", "java.exe")
            };

            foreach (var p in commonPaths)
            {
                if (File.Exists(p)) return p;
            }

            return null; // 没找到
        }
    }
}