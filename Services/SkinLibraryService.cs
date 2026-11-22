using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Newtonsoft.Json.Linq;
using CraftGate.ViewModels;

namespace CraftGate.Services
{
    /// <summary>皮肤库条目：名字 + 皮肤纹理路径 + 头像预览。</summary>
    public class SkinEntry : ViewModelBase
    {
        public string Name { get; set; }
        public string TexturePath { get; set; }
        public string Source { get; set; } = "本地";
        public bool IsUpload { get; set; }
        public ImageSource HeadImage { get; set; }
        // 完整皮肤放大图（用于 3D 立体预览）
        public ImageSource SkinImage { get; set; }
        private bool _isSelected;
        public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }
    }

    /// <summary>披风条目。</summary>
    public class CapeEntry : ViewModelBase
    {
        public string Name { get; set; }
        public string TexturePath { get; set; }
        public ImageSource PreviewImage { get; set; }
        // 原始披风图（64x32），供 3D 预览穿在角色身上。
        // 【必须触发通知】卡片在数据填充前已 Loaded，普通 auto 属性绑定拿不到后来的值 → 3D 不显示。
        private ImageSource _capeImage;
        public ImageSource CapeImage { get => _capeImage; set => Set(ref _capeImage, value); }
        // 3D 预览用的角色皮肤（1280 放大图），由 ViewModel 填充（当前皮肤或默认 Steve）
        private ImageSource _modelSkin;
        public ImageSource ModelSkin { get => _modelSkin; set => Set(ref _modelSkin, value); }
        public string Source { get; set; } = "本地";
        private bool _isSelected;
        public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }
    }

    /// <summary>
    /// 皮肤库：内置精选皮肤 + 在线皮肤站（minecraftskins.com），失败自动回退内置。
    /// 披风：内置示例 + 可选在线。
    /// </summary>
    public static class SkinLibraryService
    {
        private static readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        static SkinLibraryService()
        {
            // skinmc 等站点对无 UA 的请求会 403，补一个浏览器 UA
            _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Safari/537.36");
        }

        private static string AssetsDir => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets");

        /// <summary>获取皮肤列表（内置 + 在线）。</summary>
        public static async Task<List<SkinEntry>> GetSkinsAsync()
        {
            var list = new List<SkinEntry>();

            // 内置精选皮肤
            string skinsDir = Path.Combine(AssetsDir, "skins");
            if (Directory.Exists(skinsDir))
            {
                foreach (var f in Directory.GetFiles(skinsDir, "*.png").OrderBy(x => x))
                {
                    string name = Path.GetFileNameWithoutExtension(f);
                    list.Add(new SkinEntry
                    {
                        Name = name,
                        TexturePath = f,
                        Source = "本地",
                        HeadImage = SkinHeadService.GetHead(f, 6),
                        SkinImage = LoadScaledSkin(f)
                    });
                }
            }

            // 用户上传的皮肤（持久化：文件名即显示名）
            if (Directory.Exists(PathManager.UploadedSkinsDir))
            {
                foreach (var f in Directory.GetFiles(PathManager.UploadedSkinsDir, "*.png").OrderBy(x => x))
                {
                    string name = Path.GetFileNameWithoutExtension(f);
                    list.Add(new SkinEntry
                    {
                        Name = name,
                        TexturePath = f,
                        Source = "本地",
                        HeadImage = SkinHeadService.GetHead(f, 6),
                        SkinImage = LoadScaledSkin(f)
                    });
                }
            }

            // 云端皮肤缓存（离线可用）：之前联网拉到的皮肤已存盘，直接加载，
            // 避免每次启动都依赖不稳定的皮肤站 API（国内常 404/超时导致只剩几个）
            string cloudDir = Path.Combine(PathManager.SkinsCacheDir, "library");
            var cachedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (Directory.Exists(cloudDir))
            {
                foreach (var f in Directory.GetFiles(cloudDir, "*.png").OrderBy(x => x))
                {
                    string raw = Path.GetFileNameWithoutExtension(f);
                    // online_{offset}_{i}_{name}.png 或 featured_{name}.png → 取真实名字
                    string name = raw;
                    if (name.StartsWith("featured_")) name = name.Substring("featured_".Length);
                    else if (name.StartsWith("online_"))
                    {
                        var parts = name.Split('_');
                        name = parts.Length >= 4 ? string.Join("_", parts.Skip(3)) : name;
                    }
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    if (!cachedNames.Add(name)) continue;  // 同名去重
                    list.Add(new SkinEntry
                    {
                        Name = name,
                        TexturePath = f,
                        Source = "云端",
                        HeadImage = SkinHeadService.GetHead(f, 6),
                        SkinImage = LoadScaledSkin(f)
                    });
                }
            }

            // 缓存已充足时跳过联网：皮肤站 API 在国内常 404/超时，
            // 每次开编辑页都等 10 秒超时体验很差；缓存天然离线可用。
            // 需要更新时由界面上的"刷新/加载更多"显式触发（SearchSkinsAsync）。
            if (cachedNames.Count >= 12) return list;

            // 缓存不足时才尝试联网补充（双源：minecraftskins API → minotar 精选兜底）
            var online = await ScanOnlineSkinsAsync().ConfigureAwait(false);
            foreach (var s in online)
            {
                if (cachedNames.Contains(s.Name)) continue;  // 缓存已有就不重复
                list.Add(s);
            }
            return list;
        }

        // minotar 精选玩家名（都是真实存在的知名玩家/皮肤，云端拉取）
        private static readonly string[] FeaturedNames =
        {
            "Notch", "jeb_", "Dinnerbone", "Grumm", "Herobrine", "Alex", "Steve",
            "technoblade", "Dream", "GeorgeNotFound", "Sapnap", "BadBoyHalo",
            "sk1er", "neat", "Glowing", "TBNRfrags", "StampyLongNose",
            "Etho", "Grian", "MumboJumbo", "ImpulseSV", "Skizzleman", "TangoTek",
            "BdoubleO100", "GenerikB", "KurtJMac", "VintageBeef", "Zisteau",
            "Pakratt", "SethBling", "AvidyaZEN", "Docm77", "Wels", "Xisuma",
            "TFC", "DanTDM", "Stampy", "CaptainSparklez", "Syndicate",
            "SkyDoesMinecraft", "BajanCanadian", "JeromeASF", "Vikkstar123",
            "AchievementHunter", "Husky", "Mutiny", "PewDiePie"
        };

        /// <summary>
        /// 联网扫描皮肤站，返回云端皮肤列表（带本地缓存，模组式）。
        /// 先试 minecraftskins.com API；失败则用 minotar 按精选玩家名拉取。
        /// </summary>
        public static async Task<List<SkinEntry>> ScanOnlineSkinsAsync(string query = "", int offset = 0)
        {
            var result = new List<SkinEntry>();
            string cacheDir = Path.Combine(PathManager.SkinsCacheDir, "library");
            Directory.CreateDirectory(cacheDir);

            // 源1：minecraftskins.com 搜索 API
            try
            {
                string q = string.IsNullOrWhiteSpace(query) ? "" : Uri.EscapeDataString(query);
                string url = string.IsNullOrWhiteSpace(query)
                    ? $"https://api.minecraftskins.com/api/search?mode=all&skin=&sort=trending&start={offset}"
                    : $"https://api.minecraftskins.com/api/search?mode=all&skin={q}&sort=trending&start={offset}";
                string json = await _http.GetStringAsync(url).ConfigureAwait(false);
                var arr = JObject.Parse(json)["skins"] as JArray;
                if (arr != null)
                {
                    int added = 0;
                    foreach (var item in arr)
                    {
                        if (added >= 48) break;
                        string texture = item["texture"]?.ToString();
                        string name = item["name"]?.ToString() ?? $"皮肤{added + 1}";
                        if (string.IsNullOrEmpty(texture)) continue;
                        string file = Path.Combine(cacheDir, $"online_{offset}_{added}_{Sanitize(name)}.png");
                        if (!File.Exists(file))
                        {
                            byte[] data = await _http.GetByteArrayAsync(texture).ConfigureAwait(false);
                            File.WriteAllBytes(file, data);
                        }
                        result.Add(MakeEntry(name, file, "云端"));
                        added++;
                    }
                    if (result.Count > 0) return result;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("minecraftskins API 不可用: " + ex.Message);
            }

            // 源2：minotar 按精选玩家名拉取（国内可访问，保证有云端皮肤）
            if (!string.IsNullOrWhiteSpace(query)) return result; // 搜索模式下源2不适用
            foreach (var name in FeaturedNames)
            {
                try
                {
                    string file = Path.Combine(cacheDir, $"featured_{name}.png");
                    if (!File.Exists(file))
                    {
                        byte[] data = await _http.GetByteArrayAsync($"https://minotar.net/skin/{name}").ConfigureAwait(false);
                        File.WriteAllBytes(file, data);
                    }
                    result.Add(MakeEntry(name, file, "云端"));
                }
                catch { }
            }
            return result;
        }

        private static SkinEntry MakeEntry(string name, string file, string source) => new SkinEntry
        {
            Name = name,
            TexturePath = file,
            Source = source,
            HeadImage = SkinHeadService.GetHead(file, 6),
            SkinImage = LoadScaledSkin(file)
        };

        /// <summary>
        /// 联网扫描 skinmc.net 免费披风库（失败回退空列表）。
        /// 列表页 HTML 里形如 /cape/{id} 的链接 → 原图 https://skinmc.net/capes/{id}/download（64x32 PNG）。
        /// </summary>
        public static Task<List<CapeEntry>> ScanOnlineCapesAsync(int offset = 0)
            => ScanOnlineCapesPageAsync(offset / 48 + 1);

        /// <summary>按 skinmc 页码加载披风（每页约 48 张）。onItem: 每加载一张回调一次（渐进显示用）。</summary>
        public static async Task<List<CapeEntry>> ScanOnlineCapesPageAsync(int page, Action<CapeEntry> onItem = null)
        {
            var result = new List<CapeEntry>();
            string cacheDir = Path.Combine(PathManager.SkinsCacheDir, "library", "capes");
            Directory.CreateDirectory(cacheDir);
            try
            {
                string listUrl = $"https://skinmc.net/capes/custom?page={page}";
                string html = await _http.GetStringAsync(listUrl).ConfigureAwait(false);

                // 抓 /cape/{id} 形式的披风详情链接，去重保序
                var ids = new List<string>();
                foreach (System.Text.RegularExpressions.Match m in
                    System.Text.RegularExpressions.Regex.Matches(html, @"/cape/(\d+)"))
                {
                    string id = m.Groups[1].Value;
                    if (!ids.Contains(id)) ids.Add(id);
                    if (ids.Count >= 48) break;
                }

                int added = 0;
                foreach (string id in ids)
                {
                    string file = Path.Combine(cacheDir, $"skinmc_{id}.png");
                    try
                    {
                        if (!File.Exists(file))
                        {
                            byte[] data = await _http.GetByteArrayAsync($"https://skinmc.net/capes/{id}/download").ConfigureAwait(false);
                            // 校验确实是 PNG 且 64x32，避免把错误页当图片存下来
                            if (data.Length < 24 || !(data[0] == 0x89 && data[1] == 'P' && data[2] == 'N' && data[3] == 'G')) continue;
                            File.WriteAllBytes(file, data);
                        }
                        var entry = new CapeEntry
                        {
                            Name = $"skinmc #{id}",
                            TexturePath = file,
                            Source = "skinmc",
                            PreviewImage = LoadCapePreview(file),
                            CapeImage = LoadCapeImage(file)
                        };
                        result.Add(entry);
                        onItem?.Invoke(entry); // 渐进显示：下载一张通知一张
                        added++;
                    }
                    catch { /* 单张失败跳过，不影响整批 */ }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("skinmc 披风站扫描失败: " + ex.Message);
            }
            return result;
        }

        /// <summary>按关键词搜索 skinmc 披风（搜索页 ?search=关键词&amp;tab=capes）。</summary>
        public static async Task<List<CapeEntry>> SearchCapesAsync(string keyword)
        {
            var result = new List<CapeEntry>();
            if (string.IsNullOrWhiteSpace(keyword)) keyword = "cape";
            string cacheDir = Path.Combine(PathManager.SkinsCacheDir, "library", "capes");
            Directory.CreateDirectory(cacheDir);
            try
            {
                string url = $"https://skinmc.net/s?search={Uri.EscapeDataString(keyword)}&tab=capes";
                string html = await _http.GetStringAsync(url).ConfigureAwait(false);
                var ids = new System.Collections.Generic.List<string>();
                foreach (System.Text.RegularExpressions.Match m in
                    System.Text.RegularExpressions.Regex.Matches(html, @"/cape/(\d+)"))
                {
                    string id = m.Groups[1].Value;
                    if (!ids.Contains(id)) ids.Add(id);
                    if (ids.Count >= 48) break;
                }
                foreach (string id in ids)
                {
                    string file = Path.Combine(cacheDir, $"skinmc_{id}.png");
                    try
                    {
                        if (!File.Exists(file))
                        {
                            byte[] data = await _http.GetByteArrayAsync($"https://skinmc.net/capes/{id}/download").ConfigureAwait(false);
                            if (data.Length < 24 || !(data[0] == 0x89 && data[1] == 'P' && data[2] == 'N' && data[3] == 'G')) continue;
                            File.WriteAllBytes(file, data);
                        }
                        result.Add(new CapeEntry
                        {
                            Name = $"skinmc #{id}",
                            TexturePath = file,
                            Source = "skinmc",
                            PreviewImage = LoadCapePreview(file),
                            CapeImage = LoadCapeImage(file)
                        });
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("skinmc 披风搜索失败: " + ex.Message);
            }
            return result;
        }

        /// <summary>获取披风列表（内置）。</summary>
        public static List<CapeEntry> GetCapes()
        {
            var list = new List<CapeEntry>();
            string capesDir = Path.Combine(AssetsDir, "capes");
            if (Directory.Exists(capesDir))
            {
                foreach (var f in Directory.GetFiles(capesDir, "*.png").OrderBy(x => x))
                {
                    list.Add(new CapeEntry
                    {
                        Name = Path.GetFileNameWithoutExtension(f),
                        TexturePath = f,
                        PreviewImage = LoadCapePreview(f),
                        CapeImage = LoadCapeImage(f)
                    });
                }
            }
            return list;
        }

        /// <summary>把披风 UV（64x32 取正面 10x16 区域）做成竖条预览。</summary>
        private static ImageSource LoadCapePreview(string path)
        {
            try
            {
                var bmp = LoadBitmap(path);
                if (bmp == null) return null;
                // 披风背面 UV 通常在 (0,0)-(16,16)，取前 12x16 做预览
                int w = Math.Min(12, bmp.PixelWidth), h = Math.Min(16, bmp.PixelHeight);
                int stride = bmp.PixelWidth * 4;
                var px = new byte[bmp.PixelHeight * stride];
                bmp.CopyPixels(px, stride, 0);
                var outPx = new byte[h * w * 4];
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        int s = (y * bmp.PixelWidth + x) * 4;
                        int d = (y * w + x) * 4;
                        outPx[d] = px[s]; outPx[d + 1] = px[s + 1]; outPx[d + 2] = px[s + 2]; outPx[d + 3] = px[s + 3];
                    }
                var crop = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, outPx, w * 4);
                crop.Freeze();
                return SkinService.ScaleBitmapNearestNeighbor(crop, 6);
            }
            catch { return null; }
        }

        /// <summary>加载原始披风图（64x32，不放大），供 3D 预览使用。</summary>
        private static ImageSource LoadCapeImage(string path)
        {
            try { return LoadBitmap(path); }
            catch { return null; }
        }

        private static BitmapSource LoadBitmap(string path)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                var dec = BitmapDecoder.Create(fs, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                var f = new FormatConvertedBitmap(dec.Frames[0], PixelFormats.Bgra32, null, 0);
                f.Freeze();
                return f;
            }
        }

        /// <summary>
        /// 皮肤文件内容指纹（取若干像素采样），用于把"账户当前皮肤副本"匹配回皮肤库里的原始条目。
        /// 账户的 SkinPath 是复制出来的副本，文件名不同，只能靠内容识别到底选了哪张。
        /// </summary>
        public static string GetSkinFingerprint(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    var dec = BitmapDecoder.Create(fs, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                    var src = dec.Frames[0];
                    if (src.PixelWidth < 64 || src.PixelHeight < 32) return null;
                    var fmt = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
                    fmt.Freeze();
                    int w = fmt.PixelWidth, h = fmt.PixelHeight;
                    int stride = w * 4;
                    var px = new byte[h * stride];
                    fmt.CopyPixels(px, stride, 0);
                    var sb = new System.Text.StringBuilder();
                    // 在整张纹理上均匀取 24 个采样点（覆盖脸/身/四肢）
                    for (int i = 0; i < 24; i++)
                    {
                        int sx = (i * 37) % 64, sy = (i * 23) % Math.Min(64, h / (w / 64));
                        int ix = sx * (w / 64), iy = sy * (w / 64);
                        if (iy >= h) continue;
                        int o = iy * stride + ix * 4;
                        sb.Append(px[o]).Append(',').Append(px[o + 1]).Append(',').Append(px[o + 2]).Append(',').Append(px[o + 3]).Append(';');
                    }
                    return sb.ToString();
                }
            }
            catch { return null; }
        }

        // 把皮肤 UV 放大 20 倍（64→1280），供 SkinPreview3D 使用
        public static ImageSource LoadScaledSkin(string path)
        {
            try
            {
                var bmp = LoadBitmap(path);
                if (bmp == null || bmp.PixelWidth < 64) return null;
                return SkinService.ScaleBitmapNearestNeighbor(bmp, 20);
            }
            catch { return null; }
        }

        private static string Sanitize(string s)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s.Length > 30 ? s.Substring(0, 30) : s;
        }
    }
}
