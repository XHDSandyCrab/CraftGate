using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CraftGate.Services
{
    /// <summary>
    /// 微软正版登录（设备码流程，PCL 风格）。
    /// 流程：申请设备码 → 显示验证码让用户去浏览器授权 → 轮询令牌
    ///      → Xbox Live → XSTS → Minecraft 令牌 → 查 profile（验证拥有游戏）。
    /// 不依赖 CmlLib/WebView2，纯 HTTP；Client ID 由用户在设置页填写。
    /// </summary>
    public static class MicrosoftLoginService
    {
        private static readonly HttpClient _http = new HttpClient();

        private const string DeviceCodeUrl = "https://login.microsoftonline.com/consumers/oauth2/v2.0/devicecode";
        private const string TokenUrl = "https://login.microsoftonline.com/consumers/oauth2/v2.0/token";
        private const string Scope = "service::user.auth.xboxlive.com::MBI_SSL openid profile offline_access";

        private static string AccountFile => Path.Combine(PathManager.DataDir, "account_ms.json");

        public class MicrosoftSession
        {
            public string Username { get; set; }
            public string AccessToken { get; set; }
            public string Uuid { get; set; }
            public string UserType { get; set; } = "msa";
        }

        private class CachedSession : MicrosoftSession
        {
            public DateTime SavedAt { get; set; }
        }

        // 设备码流程第一步的返回
        public class DeviceCodeInfo
        {
            public string UserCode { get; set; }
            public string VerificationUri { get; set; }
            public string Message { get; set; }
            public int ExpiresIn { get; set; }
            public int Interval { get; set; }
            internal string DeviceCode { get; set; }
        }

        /// <summary>读取本地缓存的 session；没有则返回 null。</summary>
        public static MicrosoftSession TryRestore()
        {
            try
            {
                if (!File.Exists(AccountFile)) return null;
                var c = JsonConvert.DeserializeObject<CachedSession>(File.ReadAllText(AccountFile));
                if (c == null || string.IsNullOrEmpty(c.AccessToken) || string.IsNullOrEmpty(c.Uuid)) return null;
                return c;
            }
            catch { return null; }
        }

        /// <summary>清除缓存。</summary>
        public static void SignOut()
        {
            try { if (File.Exists(AccountFile)) File.Delete(AccountFile); } catch { }
        }

        /// <summary>第一步：申请设备码。拿到后把 UserCode/VerificationUri 显示给用户。</summary>
        public static async Task<DeviceCodeInfo> RequestDeviceCodeAsync(string clientId)
        {
            if (string.IsNullOrWhiteSpace(clientId))
                throw new InvalidOperationException("请先到「设置」里填写微软登录的 Client ID。");

            var form = new FormUrlEncodedContent(new[]
            {
                new System.Collections.Generic.KeyValuePair<string,string>("client_id", clientId.Trim()),
                new System.Collections.Generic.KeyValuePair<string,string>("scope", Scope)
            });
            var resp = await _http.PostAsync(DeviceCodeUrl, form).ConfigureAwait(false);
            string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                throw new InvalidOperationException(ParseMsError(body));

            var j = JObject.Parse(body);
            return new DeviceCodeInfo
            {
                UserCode = j["user_code"]?.ToString(),
                VerificationUri = j["verification_uri"]?.ToString() ?? j["verification_url"]?.ToString(),
                Message = j["message"]?.ToString(),
                ExpiresIn = j["expires_in"]?.Value<int>() ?? 900,
                Interval = j["interval"]?.Value<int>() ?? 5,
                DeviceCode = j["device_code"]?.ToString()
            };
        }

        /// <summary>第二步：轮询等待用户在浏览器授权，拿到微软 access token。</summary>
        public static async Task<string> WaitForTokenAsync(string clientId, DeviceCodeInfo dc,
            Action<string> statusCallback, CancellationToken ct)
        {
            int interval = Math.Max(1, dc.Interval);
            int elapsed = 0;
            while (!ct.IsCancellationRequested && elapsed < dc.ExpiresIn)
            {
                await Task.Delay(TimeSpan.FromSeconds(interval), ct).ConfigureAwait(false);
                elapsed += interval;

                var form = new FormUrlEncodedContent(new[]
                {
                    new System.Collections.Generic.KeyValuePair<string,string>("grant_type", "urn:ietf:params:oauth:grant-type:device_code"),
                    new System.Collections.Generic.KeyValuePair<string,string>("client_id", clientId.Trim()),
                    new System.Collections.Generic.KeyValuePair<string,string>("device_code", dc.DeviceCode)
                });
                var resp = await _http.PostAsync(TokenUrl, form).ConfigureAwait(false);
                string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);

                if (resp.IsSuccessStatusCode)
                {
                    var j = JObject.Parse(body);
                    return j["access_token"]?.ToString();
                }

                var err = JObject.Parse(body)["error"]?.ToString();
                switch (err)
                {
                    case "authorization_pending":
                        statusCallback?.Invoke("等待你在浏览器中完成登录...");
                        break;
                    case "slow_down":
                        interval += 2;
                        break;
                    case "expired_token":
                        throw new InvalidOperationException("验证码已过期，请重新登录。");
                    case "access_denied":
                        throw new InvalidOperationException("你拒绝了授权。");
                    default:
                        throw new InvalidOperationException(ParseMsError(body));
                }
            }
            throw new InvalidOperationException("登录超时，请重试。");
        }

        /// <summary>完整流程：申请码→（调用方显示）→轮询→Xbox→XSTS→Minecraft→profile。</summary>
        public static async Task<MicrosoftSession> LoginAsync(string clientId, Action<DeviceCodeInfo> codeCallback,
            Action<string> statusCallback, CancellationToken ct)
        {
            var dc = await RequestDeviceCodeAsync(clientId).ConfigureAwait(false);
            codeCallback?.Invoke(dc);
            string msToken = await WaitForTokenAsync(clientId, dc, statusCallback, ct).ConfigureAwait(false);

            statusCallback?.Invoke("正在验证 Xbox 账号...");
            var xsts = await AuthenticateXboxAsync(msToken).ConfigureAwait(false);

            statusCallback?.Invoke("正在获取 Minecraft 令牌...");
            string mcToken = await GetMinecraftTokenAsync(xsts.Uhs, xsts.Token).ConfigureAwait(false);

            statusCallback?.Invoke("正在确认游戏所有权...");
            var profile = await GetProfileAsync(mcToken).ConfigureAwait(false);
            string uuid = profile["id"]?.ToString();
            string name = profile["name"]?.ToString();

            var session = new MicrosoftSession
            {
                Username = name,
                AccessToken = mcToken,
                Uuid = uuid,
                UserType = "msa"
            };

            Directory.CreateDirectory(PathManager.DataDir);
            var cached = new CachedSession
            {
                Username = name, AccessToken = mcToken, Uuid = uuid,
                UserType = "msa", SavedAt = DateTime.Now
            };
            File.WriteAllText(AccountFile, JsonConvert.SerializeObject(cached, Formatting.Indented));
            return session;
        }

        private class XstsResult
        {
            public string Uhs { get; set; }
            public string Token { get; set; }
        }

        private static async Task<XstsResult> AuthenticateXboxAsync(string msToken)
        {
            var body = new JObject
            {
                ["Identities"] = new JArray
                {
                    new JObject { ["type"] = "Microsoft", ["username"] = "0", ["password"] = msToken }
                },
                ["RelyingParty"] = "http://auth.xboxlive.com",
                ["TokenType"] = "JWT"
            };
            var req = new HttpRequestMessage(HttpMethod.Post, "https://user.auth.xboxlive.com/user/authenticate")
            {
                Content = new StringContent(body.ToString(Formatting.None), Encoding.UTF8, "application/json")
            };
            req.Headers.Add("x-xbl-contract-version", "1");
            var resp = await _http.SendAsync(req).ConfigureAwait(false);
            string text = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                throw new InvalidOperationException("Xbox 认证失败: " + Trunc(text));
            var userToken = JObject.Parse(text)["Token"]?.ToString();

            var body2 = new JObject
            {
                ["Properties"] = new JObject
                {
                    ["Sandbox"] = "RETAIL",
                    ["SiteName"] = "user.auth.xboxlive.com",
                    ["RelyingParty"] = "rp://api.minecraftservices.com/"
                },
                ["UserTokens"] = new JArray { userToken },
                ["TokenType"] = "JWT"
            };
            var req2 = new HttpRequestMessage(HttpMethod.Post, "https://xsts.auth.xboxlive.com/xsts/authorize")
            {
                Content = new StringContent(body2.ToString(Formatting.None), Encoding.UTF8, "application/json")
            };
            req2.Headers.Add("x-xbl-contract-version", "1");
            var resp2 = await _http.SendAsync(req2).ConfigureAwait(false);
            string text2 = await resp2.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!resp2.IsSuccessStatusCode)
            {
                var errCode = JObject.Parse(text2)["XErr"]?.Value<int>();
                if (errCode == 2148916233) throw new InvalidOperationException("这个微软账号还没有 Xbox 账号，请先在 xbox.com 创建。");
                throw new InvalidOperationException("XSTS 认证失败: " + Trunc(text2));
            }
            var xstsObj = JObject.Parse(text2);
            string uhs = xstsObj["DisplayClaims"]?["xui"]?[0]?["uhs"]?.ToString();
            return new XstsResult { Uhs = uhs, Token = xstsObj["Token"]?.ToString() };
        }

        private static async Task<string> GetMinecraftTokenAsync(string uhs, string xstsToken)
        {
            var j = new JObject
            {
                ["identityToken"] = $"XBL3.0 x={uhs};{xstsToken}"
            };
            var resp = await _http.PostAsync("https://api.minecraftservices.com/authentication/login_with_xbox",
                new StringContent(j.ToString(Formatting.None), Encoding.UTF8, "application/json")).ConfigureAwait(false);
            string text = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                throw new InvalidOperationException("Minecraft 认证失败: " + Trunc(text));
            return JObject.Parse(text)["access_token"]?.ToString();
        }

        private static async Task<JObject> GetProfileAsync(string mcToken)
        {
            var req = new HttpRequestMessage(HttpMethod.Get, "https://api.minecraftservices.com/minecraft/profile");
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", mcToken);
            var resp = await _http.SendAsync(req).ConfigureAwait(false);
            string text = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                throw new InvalidOperationException("该账号未拥有 Minecraft Java 版，或 Client ID 未通过 Mojang 白名单审核。");
            return JObject.Parse(text);
        }

        private static string ParseMsError(string body)
        {
            try { return JObject.Parse(body)["error_description"]?.ToString() ?? body; }
            catch { return body; }
        }

        private static string Trunc(string s) => s.Length > 120 ? s.Substring(0, 120) + "..." : s;
    }
}
