using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace CraftGate.Services
{
    /// <summary>
    /// 模组名翻译：内置常见模组「英文→中文」词库（命中即用，离线、准确），
    /// 未命中时用在线翻译补漏并缓存。对齐 HMCL/PCL2 的"常见模组中文名"体验。
    /// </summary>
    public static class ModTranslationService
    {
        private static readonly HttpClient _client = new HttpClient();
        private static readonly ConcurrentDictionary<string, string> _cache =
            new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // 英文 → 中文（键统一小写便于匹配；值为官方/通用中文译名）
        private static readonly Dictionary<string, string> Names =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // —— 性能优化 / 客户端 ——
            ["Sodium"] = "钠（渲染优化）", ["Sodium Extra"] = "钠扩展",
            ["Iris"] = "虹膜（光影）", ["Iris Shaders"] = "虹膜光影",
            ["Lithium"] = "锂（游戏逻辑优化）", ["Phosphor"] = "磷（光照优化）",
            ["Starlight"] = "星光（光照优化）", ["Entity Culling"] = "实体剔除",
            ["Xaero's Minimap"] = "Xaero 小地图", ["Xaero's World Map"] = "Xaero 世界地图",
            ["JourneyMap"] = "旅行地图",
            ["OptiFine"] = "高清修复", ["Rubidium"] = "铷（渲染优化）",
            ["Oculus"] = "目镜（光影）", ["Embeddium"] = "Embed 渲染优化",
            ["Modern Indicator"] = "现代指示器", ["Neat"] = "简洁血条",
            ["Jade"] = "翡翠（物品提示）", ["WTHIT"] = "物品提示",
            ["HWYLA"] = "物品提示（旧版）", ["REI"] = "REI 物品查看",
            ["Roughly Enough Items"] = "REI 物品查看", ["Just Enough Items"] = "JEI 物品查看",
            ["JEI"] = "JEI 物品查看", ["Inventory HUD+"] = "背包信息显示",
            ["AppleSkin"] = "苹果皮（饥饿提示）", ["Saturation Hud"] = "饱和度显示",

            // —— 前置 / 库 ——
            ["Fabric API"] = "Fabric API（前置）", ["Quilt Loader"] = "Quilt 加载器",
            ["Forge"] = "Forge 加载器", ["NeoForge"] = "NeoForge 加载器",
            ["Architectury API"] = "Architectury 前置", ["Cloth Config API"] = "布艺配置前置",
            ["Cloth Config"] = "布艺配置前置", ["Mod Menu"] = "模组菜单",
            ["Fabric Loader"] = "Fabric 加载器", ["Forge Config API Port"] = "Forge 配置接口",
            ["Curve API"] = "Curve 前置", ["Cardinal Components API"] = "组件 API 前置",
            ["Extra API"] = "Extra 前置", ["LambDynamicLights"] = "动态光源",

            // —— 科技 / 魔法大 mod ——
            ["Create"] = "机械动力", ["Industrial Craft 2"] = "工业2", ["IC2"] = "工业2",
            ["GregTech CEu"] = "格雷科技 CEu", ["Thermal Expansion"] = "热力膨胀",
            ["Thermal Foundation"] = "热力基础", ["Mekanism"] = "通用机械",
            ["Mekanism Tools"] = "通用机械工具", ["Mekanism Generators"] = "通用机械发电机",
            ["Applied Energistics 2"] = "应用能源2", ["AE2"] = "应用能源2",
            ["Refined Storage"] = "精致存储", ["Botania"] = "植物魔法",
            ["Blood Magic"] = "血魔法", ["Thaumcraft"] = "神秘时代",
            ["Astral Sorcery"] = "星辉魔法", ["Immersive Engineering"] = "沉浸工程",
            ["Tinkers Construct"] = "匠魂", ["Tinker's Construct"] = "匠魂",
            ["Twilight Forest"] = "暮色森林", ["Avaritia"] = "无尽贪婪",
            ["Draconic Evolution"] = "龙之进化", ["Extreme Reactors"] = "极限反应堆",
            ["Big Reactors"] = "大型反应堆", ["Ender IO"] = "末界科技",
            ["Skins Restorer"] = "皮肤恢复", ["Actually Additions"] = "实用扩展",
            ["Cyclic"] = "循环模组", ["Extra Utilities 2"] = "额外工具2",
            ["RFTools"] = "RF工具", ["Thermal Dynamics"] = "热力流体管道",
            ["Flux Networks"] = "通量网络", ["Compact Storage"] = "紧凑存储",

            // —— 冒险 / 探索 ——
            ["Biomes O' Plenty"] = "群系丰富", ["BiomesYou'llGo"] = "群系你来去",
            ["Oh The Biomes You'll Go"] = "群系你来去", ["Atmospheric"] = "大气环境",
            ["Terralith"] = "大地之石", ["The Midnight"] = "午夜",
            ["Blue Skies"] = "蓝天", ["Advent of Ascension"] = "飞升冒险",
            ["Runic Dungeons"] = "符文地牢", ["Doomlike Dungeons"] = "末日地牢",
            ["Better Dungeons"] = "更好的地牢", ["YUNG's Better Mineshafts"] = "YUNG 更好的矿井",
            ["YUNG's Better Strongholds"] = "YUNG 更好的要塞", ["YUNG's Better Dungeons"] = "YUNG 更好的地牢",
            ["Village Jobs"] = "村庄职业", ["Guard Villagers"] = "守卫村民",

            // —— 仙侠 / 东方 ——
            ["SlashBlade"] = "拔刀剑", ["Fluffy Dragon Block"] = "龙珠",
            ["Touhou Little Maid"] = "东方小女仆", ["Touhou"] = "东方",
            ["MythicMobs"] = "神话生物", ["Elephant"] = "大象工具",

            // —— 客户端美化 / 小工具 ——
            ["Map Atlas"] = "地图集",
            ["Falling Tree"] = "砍树", ["Ore Excavation"] = "连锁挖矿",
            ["Vein Mining"] = "连锁挖矿", ["FTB Ultimine"] = "FTB 连锁挖矿",
            ["No Chat Reports"] = "禁用聊天举报", ["Chat Heads"] = "聊天头像",
            ["Skyblock Catalog"] = "空岛目录", ["Waila"] = "物品提示（旧）",
            ["Tips"] = "提示", ["Mouse Tweaks"] = "鼠标调整",
            ["Inventory Hud"] = "背包显示", ["Shulker Box Tooltip"] = "潜影盒提示",
            ["Equipment Hud"] = "装备显示", ["Fps Reducer"] = "帧率优化",
            ["Dynamicshader"] = "动态光影", ["Celestial Shaders"] = "天空光影",
            ["MakeUp"] = "化妆（UI 美化）", ["CleanView"] = "清爽视野",
            ["3D Skin Layers"] = "3D 皮肤层", ["Skin Layers 3D"] = "3D 皮肤层",
            ["Sound Filters"] = "声音过滤", ["Presence Footsteps"] = "脚步声",
            ["Better Foliage"] = "更好的树叶", ["Connection Leak"] = "连接修复",

            // —— 世界生成 / 地形 ——
            ["Ore Genesis"] = "矿石生成", ["Geolosys"] = "地质系统",
            ["Coarse Dirt"] = "粗土", ["No More"] = "改进世界",
            ["Dungeons Arise"] = "地牢崛起", ["Better Strongholds"] = "更好的要塞",

            // —— 常用前缀/后缀兜底 ——
            ["Shaders"] = "光影", ["Shader"] = "光影", ["Optimization"] = "优化",
        };

        static ModTranslationService()
        {
            _client.DefaultRequestHeaders.Add("User-Agent", "CraftGate/1.0 (contact@craftgate.com)");
            _client.Timeout = TimeSpan.FromSeconds(8);
        }

        /// <summary>翻译单个模组名：命中词库→中文；否则在线补漏并缓存；都失败→原文。</summary>
        public static async Task<string> TranslateAsync(string englishName)
        {
            if (string.IsNullOrWhiteSpace(englishName)) return englishName;

            if (Names.TryGetValue(englishName.Trim(), out string zh)) return zh;
            if (_cache.TryGetValue(englishName, out string cached)) return cached;

            // 纯英文/数字/缩写才做机翻；含中文的直接返回
            if (Regex.IsMatch(englishName, @"[\u4e00-\u9fa5]")) return englishName;

            string translated = await OnlineTranslateAsync(englishName).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(translated) && translated != englishName)
            {
                _cache[englishName] = translated;
                return translated;
            }
            return englishName;
        }

        /// <summary>同步版本：只查词库和缓存，不触发网络（用于 UI 即时显示）。</summary>
        public static string TranslateInstant(string englishName)
        {
            if (string.IsNullOrWhiteSpace(englishName)) return englishName;
            if (Names.TryGetValue(englishName.Trim(), out string zh)) return zh;
            if (_cache.TryGetValue(englishName, out string cached)) return cached;
            return englishName;
        }

        private static async Task<string> OnlineTranslateAsync(string text)
        {
            try
            {
                string url = $"https://api.mymemory.translated.net/get?q={Uri.EscapeDataString(text)}&langpair=en|zh-CN";
                var resp = await _client.GetAsync(url).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode) return null;
                var json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                var result = JObject.Parse(json);
                var t = result["responseData"]?["translatedText"]?.ToString();
                if (string.IsNullOrEmpty(t) || t == text || t.Contains("MYMEMORY")) return null;
                return t;
            }
            catch { return null; }
        }
    }
}
