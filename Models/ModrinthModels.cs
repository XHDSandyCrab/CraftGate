using CraftGate.ViewModels;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows.Media;

namespace CraftGate.Models
{
    public class ModrinthSearchResponse
    {
        [JsonProperty("hits")] public List<ModrinthProject> Hits { get; set; }
    }

    public class ModrinthProject : ViewModelBase
    {
        [JsonProperty("project_id")] public string ProjectId { get; set; }
        [JsonProperty("title")] public string Title { get; set; }

        // 显示名：优先中文译名（词库命中即时返回，未命中由 ViewModel 异步补漏），否则英文原名
        private string _displayName;
        [JsonIgnore]
        public string DisplayName
        {
            get => string.IsNullOrEmpty(_displayName) ? (Title ?? "") : _displayName;
            set => Set(ref _displayName, value);
        }

        private string _description;
        [JsonProperty("description")]
        public string Description
        {
            get => _description;
            set => Set(ref _description, value);
        }

        [JsonProperty("icon_url")] public string IconUrl { get; set; }
        [JsonProperty("downloads")] public long Downloads { get; set; }
        [JsonProperty("author")] public string Author { get; set; }
        [JsonProperty("project_type")] public string ProjectType { get; set; }

        // 搜索结果自带的兼容性信息（Modrinth search hit 返回）
        [JsonProperty("game_versions")] public List<string> GameVersions { get; set; }
        [JsonProperty("loaders")] public List<string> Loaders { get; set; }

        // 是否兼容当前环境（由 VM 在加载后计算填充，用于排序与标注）
        [JsonIgnore] public bool IsCompatible { get; set; }

        private ImageSource _iconImage;
        [JsonIgnore]
        public ImageSource IconImage
        {
            get => _iconImage;
            set => Set(ref _iconImage, value);
        }

        [JsonIgnore] public string OriginalDescription { get; set; }
        [JsonIgnore] public bool IsTranslated { get; set; }
    }

    public class ModrinthVersion
    {
        [JsonProperty("id")] public string Id { get; set; }
        [JsonProperty("project_id")] public string ProjectId { get; set; }
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("version_number")] public string VersionNumber { get; set; }
        [JsonProperty("game_versions")] public List<string> GameVersions { get; set; }
        [JsonProperty("loaders")] public List<string> Loaders { get; set; }
        [JsonProperty("files")] public List<ModrinthFile> Files { get; set; }
        [JsonProperty("dependencies")] public List<ModrinthDependency> Dependencies { get; set; }
    }

    public class ModrinthFile
    {
        [JsonProperty("url")] public string Url { get; set; }
        [JsonProperty("filename")] public string Filename { get; set; }
        [JsonProperty("primary")] public bool Primary { get; set; }
    }

    public class ModrinthDependency
    {
        [JsonProperty("version_id")] public string VersionId { get; set; }
        [JsonProperty("project_id")] public string ProjectId { get; set; }
        [JsonProperty("dependency_type")] public string DependencyType { get; set; }
    }

    public class DependencyDisplayItem : ViewModelBase
    {
        public ModrinthProject ProjectInfo { get; set; }
        public ModrinthVersion VersionInfo { get; set; }
        public bool IsMainMod { get; set; } = false;

        private bool _isSelected = true;
        public bool IsSelected
        {
            get => _isSelected;
            set => Set(ref _isSelected, value);
        }

        // 已安装到当前账户：隐藏勾选框、显示"已下载"标记、默认不选中
        private bool _isInstalled;
        public bool IsInstalled
        {
            get => _isInstalled;
            set => Set(ref _isInstalled, value);
        }
    }

    // 👇 新增：按游戏版本分组的模型
    public class VersionGroup : ViewModelBase
    {
        public string GameVersion { get; set; }

        private bool _isExpanded;
        public bool IsExpanded
        {
            get => _isExpanded;
            set => Set(ref _isExpanded, value);
        }

        public ObservableCollection<ModrinthVersion> Versions { get; } = new ObservableCollection<ModrinthVersion>();
    }
}