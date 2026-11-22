using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Media.Imaging;
using Newtonsoft.Json;

namespace CraftGate.Services
{
    /// <summary>单个游戏实例的显示元数据（名称 + 图标）。持久化在版本目录下的 instance.meta.json。</summary>
    public class InstanceMeta
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("icon")]
        public string Icon { get; set; }   // 内置图标文件名 或 本地图片绝对路径
    }

    /// <summary>实例元数据的读写与加载（内置图标 + 本地图片）。</summary>
    public static class InstanceMetaService
    {
        private const string MetaFile = "instance.meta.json";

        // 内置图标文件名白名单（Assets 下）
        private static readonly HashSet<string> BuiltinIcons = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Grass.png", "CobbleStone.png", "Fabric.png", "Forge.png", "NeoForge.png",
            "Quilt.png", "CommandBlock.png", "Light_Shadow.png", "Xaero_Minimap.png", "Mod_Menu.png"
        };

        public static string GetMetaPath(string versionDir) => Path.Combine(versionDir, MetaFile);

        /// <summary>保存实例元数据到版本目录。</summary>
        public static void Save(string versionDir, InstanceMeta meta)
        {
            try
            {
                Directory.CreateDirectory(versionDir);
                File.WriteAllText(GetMetaPath(versionDir), JsonConvert.SerializeObject(meta, Formatting.Indented));
            }
            catch { }
        }

        /// <summary>读取版本目录的实例元数据，没有则返回 null。</summary>
        public static InstanceMeta Load(string versionDir)
        {
            try
            {
                string path = GetMetaPath(versionDir);
                if (!File.Exists(path)) return null;
                return JsonConvert.DeserializeObject<InstanceMeta>(File.ReadAllText(path));
            }
            catch { return null; }
        }

        /// <summary>把图标标识（内置文件名或本地路径）转成可显示的 ImageSource。</summary>
        public static BitmapImage LoadIcon(string icon)
        {
            if (string.IsNullOrEmpty(icon)) return null;
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                if (BuiltinIcons.Contains(Path.GetFileName(icon)) && !Path.IsPathRooted(icon))
                    bmp.UriSource = new Uri("pack://application:,,,/CraftGate;component/Assets/" + icon);
                else if (File.Exists(icon))
                    bmp.UriSource = new Uri(icon);
                else
                    bmp.UriSource = new Uri("pack://application:,,,/CraftGate;component/Assets/Grass.png");
                bmp.DecodePixelWidth = 48;
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch { return null; }
        }
    }
}
