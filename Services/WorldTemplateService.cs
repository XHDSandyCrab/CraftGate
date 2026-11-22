using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using fNbt;
using Newtonsoft.Json.Linq;

namespace CraftGate.Services
{
    public static class WorldTemplateService
    {
        // 👇 彻底修改：不再做大版本分组，直接精确到版本号，比如 .CG/Templates/1.21.10/
        private static string GetTemplateDir(string version)
        {
            string dir = Path.Combine(PathManager.LauncherDir, "Templates", version);
            Directory.CreateDirectory(dir);
            return dir;
        }

        public static bool HasTemplate(string version)
        {
            string templateDir = GetTemplateDir(version);
            return File.Exists(Path.Combine(templateDir, "level.dat"));
        }

        public static string ExtractLatestSaveAsTemplate(string version, string savesDir)
        {
            try
            {
                if (!Directory.Exists(savesDir)) return "未找到存档目录";

                // 找最新修改且包含 level.dat 的世界文件夹
                var latestWorldDir = new DirectoryInfo(savesDir).GetDirectories()
                    .Where(d => File.Exists(Path.Combine(d.FullName, "level.dat")))
                    .OrderByDescending(d => d.LastWriteTime)
                    .FirstOrDefault();

                if (latestWorldDir == null) return "未找到有效存档";

                string sourceLevelDat = Path.Combine(latestWorldDir.FullName, "level.dat");
                string templateDir = GetTemplateDir(version);
                Directory.CreateDirectory(templateDir);
                string destLevelDat = Path.Combine(templateDir, "level.dat");

                File.Copy(sourceLevelDat, destLevelDat, true);
                return null; // 返回 null 表示成功
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        public static string CreateQuickPlayWorld(string version, string savesDir, string worldName, string userSeed, int gameType, bool hardcore, string generator)
        {
            try
            {
                string templateDir = GetTemplateDir(version);
                string templateLevelDat = Path.Combine(templateDir, "level.dat");

                if (!File.Exists(templateLevelDat))
                    throw new Exception("模板缺失");

                string newWorldPath = Path.Combine(savesDir, worldName);
                if (Directory.Exists(newWorldPath)) Directory.Delete(newWorldPath, true);
                Directory.CreateDirectory(newWorldPath);

                string newLevelDat = Path.Combine(newWorldPath, "level.dat");
                File.Copy(templateLevelDat, newLevelDat, true);

                var file = new NbtFile();
                file.LoadFromFile(newLevelDat);

                var dataTag = file.RootTag.Get<NbtCompound>("Data");
                if (dataTag != null)
                {
                    var levelNameTag = dataTag.Get<NbtString>("LevelName");
                    if (levelNameTag != null) levelNameTag.Value = worldName;

                    var gameTypeTag = dataTag.Get<NbtInt>("GameType");
                    if (gameTypeTag != null) gameTypeTag.Value = gameType;

                    var hardcoreTag = dataTag.Get<NbtByte>("hardcore");
                    if (hardcoreTag != null) hardcoreTag.Value = (byte)(hardcore ? 1 : 0);

                    var timeTag = dataTag.Get<NbtLong>("Time");
                    if (timeTag != null) timeTag.Value = 0;
                    var dayTimeTag = dataTag.Get<NbtLong>("DayTime");
                    if (dayTimeTag != null) dayTimeTag.Value = 0;

                    dataTag.Remove("Player");
                    dataTag.Remove("SpawnX");
                    dataTag.Remove("SpawnY");
                    dataTag.Remove("SpawnZ");

                    var initTag = dataTag.Get<NbtByte>("initialized");
                    if (initTag != null) initTag.Value = 0;
                    else dataTag.Add(new NbtByte("initialized", 0));

                    long finalSeed;
                    if (string.IsNullOrEmpty(userSeed))
                    {
                        var rand = new Random();
                        finalSeed = ((long)rand.Next() << 32) | (uint)rand.Next();
                    }
                    else if (long.TryParse(userSeed, out long parsedSeed))
                    {
                        finalSeed = parsedSeed;
                    }
                    else
                    {
                        finalSeed = userSeed.GetHashCode();
                    }

                    var randomSeedTag = dataTag.Get<NbtLong>("RandomSeed");
                    if (randomSeedTag != null) randomSeedTag.Value = finalSeed;
                    else dataTag.Add(new NbtLong("RandomSeed", finalSeed));

                    var worldGen = dataTag.Get<NbtCompound>("WorldGenSettings");
                    if (worldGen != null)
                    {
                        var seedTag = worldGen.Get<NbtLong>("seed");
                        if (seedTag != null) seedTag.Value = finalSeed;
                        else worldGen.Add(new NbtLong("seed", finalSeed));

                        if (!string.IsNullOrEmpty(generator))
                        {
                            var genTag = worldGen.Get<NbtString>("generatorName");
                            if (genTag != null) genTag.Value = generator;
                            else worldGen.Add(new NbtString("generatorName", generator));
                        }
                    }
                }

                file.SaveToFile(newLevelDat, NbtCompression.GZip);
                return worldName;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"世界创建失败: {ex.Message}");
                return null;
            }
        }
    }
}