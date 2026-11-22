using System;
using System.IO;
using CraftGate.Models;
using Newtonsoft.Json;

namespace CraftGate.Services
{
    public static class ConfigManager
    {
        private static string SettingsFile => Path.Combine(PathManager.ConfigDir, "settings.json");

        public static SettingsModel Settings { get; private set; } = new SettingsModel();

        public static void Load()
        {
            try
            {
                Directory.CreateDirectory(PathManager.ConfigDir);
                if (File.Exists(SettingsFile))
                {
                    string json = File.ReadAllText(SettingsFile);
                    Settings = JsonConvert.DeserializeObject<SettingsModel>(json) ?? new SettingsModel();
                }
                else
                {
                    Save(); // 如果没有配置，生成一份默认的
                }
            }
            catch
            {
                Settings = new SettingsModel();
            }
        }

        public static void Save()
        {
            try
            {
                Directory.CreateDirectory(PathManager.ConfigDir);
                string json = JsonConvert.SerializeObject(Settings, Formatting.Indented);
                File.WriteAllText(SettingsFile, json);
            }
            catch { }
        }
    }
}