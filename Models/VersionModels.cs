using System.Collections.Generic;

namespace CraftGate.Models
{
    public class VersionManifest
    {
        public LatestVersions Latest { get; set; }
        public List<VersionSummary> Versions { get; set; }
    }

    public class LatestVersions
    {
        public string Release { get; set; }
        public string Snapshot { get; set; }
    }

    public class VersionSummary
    {
        public string Id { get; set; }
        public string Type { get; set; }
        public string Url { get; set; }
        public string Time { get; set; }
    }

    // 兼容你代码里可能写了的 VersionDisplayModel
    public class VersionDisplayModel : VersionSummary
    {
        // 愚人节版本的特殊 ID（官方归类在 snapshot 里，需按 ID 识别）
        public static readonly HashSet<string> AprilFoolsIds = new HashSet<string>(System.StringComparer.Ordinal)
        {
            "15w14a", "1.RV-Pre1", "3D Shareware v1.34", "20w14infinite",
            "22w13oneBlockAtATime", "23w13a_or_b", "24w14potato"
        };

        // 版本图标：正式版=草方块，快照=命令方块，远古版(old_beta/old_alpha)=原石，愚人节版=金块
        public string IconPath
        {
            get
            {
                string t = Type ?? "";
                // 愚人节版优先（它们通常被标成 snapshot）
                if (!string.IsNullOrEmpty(Id) && AprilFoolsIds.Contains(Id))
                    return "pack://application:,,,/CraftGate;component/Assets/GoldBlock.png";
                // 远古版：old_beta / old_alpha → 原石
                if (t == "old_beta" || t == "old_alpha")
                    return "pack://application:,,,/CraftGate;component/Assets/CobbleStone.png";
                // 快照 → 命令方块
                if (t == "snapshot")
                    return "pack://application:,,,/CraftGate;component/Assets/CommandBlock.png";
                // 正式版 → 草方块
                return "pack://application:,,,/CraftGate;component/Assets/Grass.png";
            }
        }
    }
}