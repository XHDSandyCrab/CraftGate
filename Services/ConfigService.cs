using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CraftGate.Services
{
    public class AppSettings
    {
        [JsonPropertyName("username")]
        public string Username { get; set; } = "Player";
    }

    public static class ConfigService
    {
        private static string ConfigFilePath => Path.Combine(PathManager.ConfigDir, "settings.json");

        // 读取配置
        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(ConfigFilePath))
                {
                    string json = File.ReadAllText(ConfigFilePath);
                    var settings = JsonSerializer.Deserialize<AppSettings>(json);
                    if (settings != null) return settings;
                }
            }
            catch { }

            // 如果读取失败或文件不存在，返回默认配置
            return new AppSettings();
        }

        // 保存配置
        public static void Save(AppSettings settings)
        {
            try
            {
                string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(ConfigFilePath, json);
            }
            catch { }
        }
    }
}