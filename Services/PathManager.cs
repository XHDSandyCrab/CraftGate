using System;
using System.IO;

namespace CraftGate.Services
{
    public static class PathManager
    {
        public static string LauncherDir { get; } = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".CG");
        public static string CacheDir { get; } = Path.Combine(LauncherDir, "Cache");
        public static string SkinsCacheDir { get; } = Path.Combine(CacheDir, "Skins");
        // 用户上传的皮肤（持久化，重启后仍在内置库）
        public static string UploadedSkinsDir { get; } = Path.Combine(SkinsCacheDir, "uploads");
        public static string ConfigDir { get; } = Path.Combine(LauncherDir, "Config");
        public static string DataDir { get; } = Path.Combine(LauncherDir, "Data");
        public static string LogsDir { get; } = Path.Combine(LauncherDir, "Logs");

        public static string GameDir { get; } = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".minecraft");
        public static string McRoot => GameDir;

        public static string VersionsDir { get; } = Path.Combine(GameDir, "versions");
        public static string LibrariesDir { get; } = Path.Combine(GameDir, "libraries");
        public static string AssetsDir { get; } = Path.Combine(GameDir, "assets");
        public static string AssetsIndexesDir { get; } = Path.Combine(AssetsDir, "indexes");
        public static string AssetsObjectsDir { get; } = Path.Combine(AssetsDir, "objects");

        // 👇 新增：版本隔离的专属目录
        public static string GetVersionDir(string versionId) => Path.Combine(VersionsDir, versionId);
        public static string GetIsolatedGameDir(string versionId) => Path.Combine(VersionsDir, versionId);

        /// <summary>
        /// 账户级隔离目录：每个账户有独立的 saves/mods/config/options.txt，游戏文件(libraries/assets/versions)仍全局共享。
        /// 未开启版本隔离时用 .minecraft/accounts/{accountId}；开启时在该版本目录下再分账户。
        /// </summary>
        public static string GetAccountGameDir(string versionId, string accountId)
        {
            string parent = ConfigManager.Settings.EnableVersionIsolation
                ? GetIsolatedGameDir(versionId)
                : GameDir;
            string safeId = string.IsNullOrWhiteSpace(accountId) ? "default" : SanitizeDirName(accountId);
            return Path.Combine(parent, "accounts", safeId);
        }

        /// <summary>取当前账户的游戏目录（启动器各处统一调用）</summary>
        public static string GetCurrentAccountGameDir(string versionId)
        {
            var acc = AccountService.CurrentAccount;
            string dir = GetAccountGameDir(versionId, acc?.Id);
            EnsureAccountGameDir(dir, ConfigManager.Settings.EnableVersionIsolation ? GetIsolatedGameDir(versionId) : GameDir);
            return dir;
        }

        private static string SanitizeDirName(string s)
        {
            foreach (char c in System.IO.Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s;
        }

        /// <summary>
        /// 账户隔离目录：每个账户从全新状态开始（空存档），不复制其它账户/共享目录的内容，
        /// 这样切换账户看到的是完全不同的世界，真正隔离。只建空目录 + 标记。
        /// </summary>
        private static void EnsureAccountGameDir(string accountDir, string parentDir)
        {
            Directory.CreateDirectory(accountDir);
            string marker = Path.Combine(accountDir, ".isolated_init");
            if (!File.Exists(marker)) File.WriteAllText(marker, System.DateTime.Now.ToString("O"));
        }

        public static void EnsureDirectoriesCreated()
        {
            try
            {
                Directory.CreateDirectory(LauncherDir);
                Directory.CreateDirectory(CacheDir);
                Directory.CreateDirectory(SkinsCacheDir);
                Directory.CreateDirectory(UploadedSkinsDir);
                Directory.CreateDirectory(ConfigDir);
                Directory.CreateDirectory(DataDir);
                Directory.CreateDirectory(LogsDir);

                Directory.CreateDirectory(GameDir);
                Directory.CreateDirectory(VersionsDir);
                Directory.CreateDirectory(LibrariesDir);
                Directory.CreateDirectory(AssetsDir);
                Directory.CreateDirectory(AssetsIndexesDir);
                Directory.CreateDirectory(AssetsObjectsDir);
            }
            catch { }
        }
    }
}