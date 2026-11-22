namespace CraftGate.Models
{
    public class SettingsModel
    {
        public bool UseMirror { get; set; } = true;
        public int DownloadThreads { get; set; } = 8;
        public int MaxMemoryMB { get; set; } = 4096;
        public bool EnableVersionIsolation { get; set; } = true;
        public string JavaPath { get; set; } = "";
        public bool IsDarkMode { get; set; } = true;

        // 窗口背景材质：None / Mica / Acrylic / MicaAlt / Image
        public string BackgroundMaterial { get; set; } = "None";
        // 自定义图片背景的文件路径（仅 BackgroundMaterial=Image 时使用）
        public string BackgroundImagePath { get; set; } = "";

        // 游戏启动进度条旁显示的 GIF 动画路径（留空则不显示，后续填入自制 GIF）
        public string LaunchGifPath { get; set; } = "";

        // 微软正版登录的 Client ID。内置公共 ID 作为默认，用户可在设置里覆盖为自己的。
        // （此 ID 为社区常用公共客户端；若微软调整白名单导致失效，请在设置里换成自己申请并过 mce 白名单的 ID）
        public string MicrosoftClientId { get; set; } = "00000000402b5328";

        // ===== 更新相关 =====
        // GitHub 仓库 owner/repo（如 "owner/repo"），用于检查更新。留空则用内置默认。
        public string UpdateRepo { get; set; } = "";
        // 是否启动时自动检查更新
        public bool AutoUpdate { get; set; } = true;
        // 暂停更新：只暂停"自动检查"，不阻止手动点"检查更新"
        public bool PauseUpdate { get; set; } = false;
    }
}