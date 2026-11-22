using System;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace CraftGate.Services
{
    /// <summary>
    /// 检查更新服务：读取 GitHub Releases 最新版本号，与当前程序版本比较。
    /// 只做"检查 + 提示"，不自动下载替换（避免自更新覆盖正在运行的 exe 引发问题）。
    /// </summary>
    public static class UpdateService
    {
        // 内置默认仓库；用户可在设置里用 UpdateRepo 覆盖（格式 owner/repo）
        public const string DefaultRepo = "XHDSandyCrab/CraftGate";

        private static string Repo
        {
            get
            {
                string configured = ConfigManager.Settings?.UpdateRepo;
                return string.IsNullOrWhiteSpace(configured) ? DefaultRepo : configured.Trim();
            }
        }
        public static string RepoDisplay => Repo;
        private static string ReleaseApi => $"https://api.github.com/repos/{Repo}/releases/latest";

        /// <summary>当前程序版本（读 AssemblyVersion），形如 1.0.0.0 → 显示为 1.0.0。</summary>
        public static string CurrentVersion
        {
            get
            {
                var v = Assembly.GetExecutingAssembly().GetName().Version;
                return v == null ? "1.0.0" : $"{v.Major}.{v.Minor}.{v.Build}";
            }
        }

        public class UpdateResult
        {
            public bool Success;          // 是否成功获取（网络/解析）
            public bool HasUpdate;        // 是否有新版本
            public string LatestVersion;  // 最新版本号
            public string ReleaseUrl;     // 发布页地址
            public string ReleaseNotes;   // 更新日志（GitHub Release body）
            public bool IsMandatory;      // 是否强制更新（日志含 [强制更新]/MUST_UPDATE 标记时，不显示关闭叉叉）
            public string DownloadUrl;    // exe 附件直链（Release assets）
            public string AssetName;      // 附件文件名
            public long AssetSize;        // 附件字节数
            public string Error;          // 失败原因
        }

        /// <summary>异步检查更新。延迟由调用方控制，本方法只发一个请求。</summary>
        public static async Task<UpdateResult> CheckAsync()
        {
            var result = new UpdateResult();
            try
            {
                // GitHub API 必须带 User-Agent，否则 403；缓存 1 天避免频繁请求
                string json = await HttpService.GetStringCachedAsync(ReleaseApi, 1);
                if (string.IsNullOrWhiteSpace(json))
                {
                    result.Error = "无法连接更新服务器";
                    return result;
                }

                var obj = JObject.Parse(json);
                string tag = obj["tag_name"]?.ToString();
                string latest = NormalizeVersion(tag);
                result.LatestVersion = string.IsNullOrEmpty(latest) ? tag : latest;
                result.ReleaseUrl = obj["html_url"]?.ToString();
                result.ReleaseNotes = obj["body"]?.ToString() ?? "";
                // 更新日志里含 [强制更新] 或 MUST_UPDATE 即视为必须更新 → 弹窗不显示关闭叉叉
                result.IsMandatory = System.Text.RegularExpressions.Regex.IsMatch(
                    result.ReleaseNotes, @"(\[强制更新\]|\[必须更新\]|MUST_UPDATE)",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                // 解析 Release 附件里的 exe（立即更新要下载它）
                if (obj["assets"] is JArray assets)
                {
                    foreach (var asset in assets)
                    {
                        string name = asset["name"]?.ToString() ?? "";
                        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                        {
                            result.AssetName = name;
                            result.AssetSize = asset["size"]?.Value<long>() ?? 0;
                            result.DownloadUrl = asset["browser_download_url"]?.ToString();
                            break;
                        }
                    }
                }
                if (string.IsNullOrEmpty(latest))
                {
                    result.Error = "更新信息格式异常";
                    return result;
                }

                result.Success = true;
                result.HasUpdate = Compare(latest, CurrentVersion) > 0;
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
            }
            return result;
        }

        /// <summary>规范化版本号：去掉 v 前缀与预发布后缀，只留 x.y.z。</summary>
        private static string NormalizeVersion(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag)) return null;
            string v = tag.Trim().TrimStart('v', 'V');
            int dash = v.IndexOf('-');
            if (dash >= 0) v = v.Substring(0, dash);
            // 只保留数字和点
            var sb = new System.Text.StringBuilder();
            bool dot = false;
            foreach (char c in v)
            {
                if (char.IsDigit(c)) { sb.Append(c); dot = false; }
                else if (c == '.' && !dot) { sb.Append(c); dot = true; }
            }
            return sb.Length > 0 ? sb.ToString() : null;
        }

        /// <summary>版本比较：a>b 返回正数，相等 0，a&lt;b 负数。按段逐位比较。</summary>
        private static int Compare(string a, string b)
        {
            var pa = a.Split('.');
            var pb = b.Split('.');
            int n = Math.Max(pa.Length, pb.Length);
            for (int i = 0; i < n; i++)
            {
                int ia = i < pa.Length && int.TryParse(pa[i], out var x) ? x : 0;
                int ib = i < pb.Length && int.TryParse(pb[i], out var y) ? y : 0;
                if (ia != ib) return ia.CompareTo(ib);
            }
            return 0;
        }
    }
}
