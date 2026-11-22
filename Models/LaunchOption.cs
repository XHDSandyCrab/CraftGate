using System.Windows.Media.Imaging;
using CraftGate.ViewModels;

namespace CraftGate.Models
{
    /// <summary>启动页版本下拉菜单的一项：带实例图标和显示名称</summary>
    public class VersionDisplayItem : ViewModelBase
    {
        public string Version { get; set; }          // 版本目录名（实际启动用）
        public string DisplayName { get; set; }      // 显示名（元数据名，没有则用版本名）
        public BitmapImage IconImage { get; set; }   // 实例图标（没有则用默认草方块）
    }

    public class LaunchOption : ViewModelBase
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string AdditionalArgs { get; set; }
        public string ButtonText { get; set; }
        public bool IsNewWorld { get; set; } // 👈 新增：是否为新建世界模式

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set => Set(ref _isSelected, value);
        }
    }
}