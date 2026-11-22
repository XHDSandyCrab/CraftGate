using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using CraftGate.ViewModels;

namespace CraftGate.Models
{
    public class VersionItem : ViewModelBase
    {
        public string Version { get; set; }
        // 附加数据（如 OptiFine 的 jar 文件名，供安装时使用）
        public string Extra { get; set; }

        // 所属加载器（用于配套 mod 选中提醒）
        public LoaderInfo Owner { get; set; }
        // 选中状态变化回调（参数：本项、是否选中）
        public Action<VersionItem, bool> OnToggled { get; set; }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (Set(ref _isSelected, value))
                    OnToggled?.Invoke(this, value);
            }
        }

        public ICommand ToggleCommand { get; }

        public VersionItem()
        {
            ToggleCommand = new RelayCommand(_ => IsSelected = !IsSelected);
        }
    }

    public class LoaderInfo : ViewModelBase
    {
        private bool _isSupported = true;
        private bool _isExpanded;
        private bool _isChecked;

        public string Id { get; set; }
        // 原版(Minecraft)特殊：默认下载，不需要勾选框与版本编辑笔
        public bool IsVanilla => string.Equals(Id, "vanilla", StringComparison.OrdinalIgnoreCase);
        public string Name { get; set; }
        public string IconCode { get; set; }
        // 图标图片路径（Assets 里的加载器图标）
        public string IconPath { get; set; }

        // 向导方块：勾选状态 + 当前选中版本 + 编辑版本回调
        public bool IsChecked
        {
            get => _isChecked;
            set
            {
                // 不支持的加载器（置灰）禁止勾选
                if (value && !IsSupported) return;
                if (Set(ref _isChecked, value) && value && SelectedVersionItem == null)
                {
                    // 勾选后自动选中首个版本，方块下方才会显示版本号
                    var first = AvailableVersions.FirstOrDefault();
                    if (first != null) first.IsSelected = true;
                    OnPropertyChanged(nameof(SelectedVersionItem));
                    OnPropertyChanged(nameof(SelectedVersionText));
                }
            }
        }
        public VersionItem SelectedVersionItem
        {
            get => AvailableVersions.FirstOrDefault(v => v.IsSelected);
        }
        public string SelectedVersionText => SelectedVersionItem?.Version ?? "";
        // 点击"编辑版本"笔的回调（由 ViewModel 注入，进入版本选择子页）
        public Action<LoaderInfo> OnEditVersion { get; set; }
        public ICommand OnEditVersionCommand { get; set; }

        // 配套 Mod（Fabric API / QSL 等）：不是加载器本体，选中时弹窗提醒需手动安装
        public bool IsAddon { get; set; } = false;
        // 悬停/选中时显示的说明
        public string HintText { get; set; } = "";

        public bool IsSupported
        {
            get => _isSupported;
            set => Set(ref _isSupported, value);
        }

        public bool IsExpanded
        {
            get => _isExpanded;
            set => Set(ref _isExpanded, value);
        }

        public ObservableCollection<VersionItem> AvailableVersions { get; } = new ObservableCollection<VersionItem>();

        // 👇 改为显示共有多少个版本
        public string VersionCountText => $"共 {AvailableVersions.Count} 个可用版本";
    }
}