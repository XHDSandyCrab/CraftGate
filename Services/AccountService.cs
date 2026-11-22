using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CraftGate.Models;
using Newtonsoft.Json;

namespace CraftGate.Services
{
    public static class AccountService
    {
        /// <summary>
        /// 清洗为合法的 Minecraft 用户名：只允许字母、数字、下划线，长度 1-16。
        /// 空格等非法字符替换为下划线，去掉连续下划线。非法名字会被服务器拒绝（连接中断）。
        /// </summary>
        public static string SanitizeUsername(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "Player";
            var sb = new System.Text.StringBuilder();
            foreach (char c in raw.Trim())
            {
                if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_')
                    sb.Append(c);
                else
                    sb.Append('_');   // 空格、中文等 → 下划线
            }
            string s = sb.ToString();
            // 合并连续下划线，去掉首尾下划线
            while (s.Contains("__")) s = s.Replace("__", "_");
            s = s.Trim('_');
            if (string.IsNullOrEmpty(s)) s = "Player";
            if (s.Length > 16) s = s.Substring(0, 16);
            return s;
        }

        private static string AccountFile => Path.Combine(PathManager.DataDir, "account.json");
        // 单独记录上次选中的账户 Id（account.json 只存账户列表，重启要记住选择需单独存）
        private static string CurrentIdFile => Path.Combine(PathManager.DataDir, "current_account.txt");

        // 所有账户
        public static List<OfflineAccount> Accounts { get; private set; } = new List<OfflineAccount>();

        // 当前账户：getter 永远返回一个对象（游戏启动等处依赖它）；setter 切换当前账户
        private static string _currentId;
        private static OfflineAccount _current;
        public static OfflineAccount CurrentAccount
        {
            get
            {
                if (_current != null && Accounts.Any(a => a.Id == _current.Id)) return _current;
                _current = Accounts.FirstOrDefault(a => a.Id == _currentId)
                           ?? Accounts.FirstOrDefault()
                           ?? new OfflineAccount();
                _currentId = _current.Id;
                return _current;
            }
            set
            {
                _current = value;
                _currentId = value?.Id;
            }
        }

        public static bool HasAccount => Accounts.Count > 0;

        public static void Load()
        {
            try
            {
                // 首次运行：创建默认 Steve 账户并立即落盘，避免"启动时没有账户"
                if (!File.Exists(AccountFile))
                {
                    Accounts = new List<OfflineAccount>();
                    EnsureDefault();
                    Save();
                    return;
                }
                string json = File.ReadAllText(AccountFile);
                // 新格式：数组
                if (json.TrimStart().StartsWith("["))
                {
                    Accounts = JsonConvert.DeserializeObject<List<OfflineAccount>>(json) ?? new List<OfflineAccount>();
                }
                else
                {
                    // 兼容旧格式：单个账户对象
                    var one = JsonConvert.DeserializeObject<OfflineAccount>(json);
                    Accounts = one != null ? new List<OfflineAccount> { one } : new List<OfflineAccount>();
                }
                EnsureDefault();
                // 优先恢复上次选中的账户；没有记录或记录失效则默认第一项
                string savedId = null;
                try { if (File.Exists(CurrentIdFile)) savedId = File.ReadAllText(CurrentIdFile).Trim(); } catch { }
                _current = Accounts.FirstOrDefault(a => a.Id == savedId)
                           ?? Accounts.FirstOrDefault();
                _currentId = _current?.Id;
                // 首次运行（无记录）也落盘当前选择，之后切换账户会更新
                try { if (!File.Exists(CurrentIdFile)) File.WriteAllText(CurrentIdFile, _currentId ?? ""); } catch { }
            }
            catch
            {
                EnsureDefault();
            }
        }

        private static void EnsureDefault()
        {
            if (Accounts.Count == 0)
                Accounts.Add(new OfflineAccount { Username = "Steve" });
            if (_current == null)
                _current = Accounts.FirstOrDefault();
            _currentId = _current?.Id;
        }

        public static void Save()
        {
            try
            {
                Directory.CreateDirectory(PathManager.DataDir);
                // 确保当前账户在列表里
                var cur = CurrentAccount;
                if (!Accounts.Any(a => a.Id == cur.Id)) Accounts.Add(cur);
                var json = JsonConvert.SerializeObject(Accounts, Formatting.Indented);
                File.WriteAllText(AccountFile, json);
                // 记住当前选中的账户，下次启动恢复
                File.WriteAllText(CurrentIdFile, cur.Id ?? "");
            }
            catch { }
        }

        /// <summary>添加一个账户并设为当前。</summary>
        public static void AddAccount(OfflineAccount account, bool setCurrent = true)
        {
            if (account == null) return;
            if (string.IsNullOrEmpty(account.Id)) account.Id = Guid.NewGuid().ToString("N");
            Accounts.Add(account);
            if (setCurrent) { _current = account; _currentId = account.Id; }
            Save();
        }

        /// <summary>切换到指定账户。</summary>
        public static void SwitchAccount(string id)
        {
            var acc = Accounts.FirstOrDefault(a => a.Id == id);
            if (acc == null) return;
            _current = acc;
            _currentId = acc.Id;
            Save();
        }

        /// <summary>删除账户；不能删最后一个。返回是否删除成功。</summary>
        public static bool RemoveAccount(string id)
        {
            var acc = Accounts.FirstOrDefault(a => a.Id == id);
            if (acc == null || Accounts.Count <= 1) return false;
            Accounts.Remove(acc);
            if (_currentId == id)
            {
                _current = Accounts.FirstOrDefault();
                _currentId = _current?.Id;
            }
            Save();
            return true;
        }
    }
}
