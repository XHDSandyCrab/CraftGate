using System;
using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using CraftGate.ViewModels;

namespace CraftGate.Models
{
    /// <summary>基本信息里的一个可选图标</summary>
    public class WizardIconOption : ViewModelBase
    {
        public string Name { get; set; }
        public string IconPath { get; set; }               // pack URI 或本地文件绝对路径
        public string IconFile { get; set; }               // 内置图标文件名 或 本地绝对路径（用于持久化）
        public BitmapSource IconImage { get; set; }        // 本地图片加载后的位图（内置图标为 null，用 IconPath）
        public bool IsBuiltin { get; set; } = true;
        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set => Set(ref _isSelected, value);
        }
        public ICommand SelectCommand { get; set; }
    }

    /// <summary>附带模块里的一个 mod（光影=钠/虹膜/ComplementaryUnbound，小地图=Xaero，模组列表=ModMenu）</summary>
    public class WizardModItem : ViewModelBase
    {
        public string Name { get; set; }                   // 显示名（中文）
        public string SearchKeyword { get; set; }          // Modrinth 搜索词
        public string ProjectId { get; set; }              // 搜到后填充
        public BitmapSource IconImage { get; set; }
        public string IconUrl { get; set; }
        public string Version { get; set; }                // 选中的版本号
        public string Loader { get; set; }                 // 该 mod 支持的加载器
        public bool IsDownloaded { get; set; }
        // 是否为光影包（shaderpack）：下载到 shaderpacks 目录而非 mods，搜索用 shader 类型
        public bool IsShaderPack { get; set; }
        public string ProjectType => IsShaderPack ? "shader" : "mod";
    }

    /// <summary>一个附带模块方块（光影 / 小地图 / 模组列表）</summary>
    public class WizardModule : ViewModelBase
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Desc { get; set; }
        public string IconPath { get; set; }               // 模块图标（主 mod 的图标 URL 或内置）
        public string IconUrl { get; set; }
        public BitmapSource IconImage { get; set; }

        private bool _isChecked;
        public bool IsChecked
        {
            get => _isChecked;
            set => Set(ref _isChecked, value);
        }

        // 该模块包含的 mod
        public ObservableCollection<WizardModItem> Mods { get; } = new ObservableCollection<WizardModItem>();

        // 版本显示 + 编辑回调
        private string _versionText = "自动";
        public string VersionText
        {
            get => _versionText;
            set => Set(ref _versionText, value);
        }
        // 是否显示版本编辑笔（轻量级Java等自动匹配的模块没有版本选择）
        public bool HasVersionSelector { get; set; } = true;
        public Action<WizardModule> OnEditVersion { get; set; }
        public ICommand OnEditVersionCommand { get; set; }

        // 该模块所有可选版本（由主 mod 的版本列表填充，供子页选择）
        public ObservableCollection<string> AvailableVersions { get; } = new ObservableCollection<string>();
        private string _selectedVersion;
        public string SelectedVersion
        {
            get => _selectedVersion;
            set
            {
                if (Set(ref _selectedVersion, value)) VersionText = string.IsNullOrEmpty(value) ? "自动" : value;
            }
        }
    }
}
