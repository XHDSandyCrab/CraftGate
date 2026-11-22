using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using CraftGate.Models;
using CraftGate.Services;

namespace CraftGate.ViewModels
{
    public class DownloadListViewModel : ViewModelBase
    {
        private bool _isLoading;
        private VersionManifest _manifest;
        private string _selectedCategory = "release";

        public ObservableCollection<VersionDisplayModel> Versions { get; } = new ObservableCollection<VersionDisplayModel>();

        // 分类命令
        public ICommand FilterCommand { get; }

        public bool IsLoading
        {
            get => _isLoading;
            set => Set(ref _isLoading, value);
        }

        public string SelectedCategory
        {
            get => _selectedCategory;
            set
            {
                if (Set(ref _selectedCategory, value))
                {
                    ApplyFilter();
                }
            }
        }

        public DownloadListViewModel()
        {
            FilterCommand = new RelayCommand(param =>
            {
                if (param is string category)
                {
                    SelectedCategory = category;
                }
            });
        }

        public async Task InitializeAsync()
        {
            if (_manifest != null) return;

            IsLoading = true;
            _manifest = await VersionService.GetVersionManifestAsync();
            IsLoading = false;

            ApplyFilter();
        }

        private void ApplyFilter()
        {
            if (_manifest?.Versions == null) return;

            Versions.Clear();

            var filtered = _manifest.Versions.Where(v =>
            {
                if (SelectedCategory == "release") return v.Type == "release";
                if (SelectedCategory == "snapshot") return v.Type == "snapshot";
                if (SelectedCategory == "old") return v.Type == "old_beta" || v.Type == "old_alpha";

                // 👇 愚人节版特殊处理：通常被包含在快照版中，但根据特定的 ID 特征可以抓出来
                if (SelectedCategory == "april_fools")
                {
                    return VersionDisplayModel.AprilFoolsIds.Contains(v.Id);
                }

                return false;
            }).ToList();

            // 如果过滤后为空，且是愚人节分类，说明可能有遗漏，就用特殊方式补充
            if (SelectedCategory == "april_fools" && filtered.Count == 0)
            {
                // 兜底逻辑：有些版本在官方被标记为 snapshot，但可以直接从大列表找 ID
                foreach (var id in VersionDisplayModel.AprilFoolsIds)
                {
                    var match = _manifest.Versions.FirstOrDefault(v => v.Id == id);
                    if (match != null) filtered.Add(match);
                }
            }

            // 按照发布时间倒序（新的在上面）
            foreach (var v in filtered.OrderByDescending(x => x.Time))
            {
                Versions.Add(new VersionDisplayModel
                {
                    Id = v.Id,
                    Type = v.Type,
                    Url = v.Url,
                    Time = v.Time
                });
            }
        }
    }
}