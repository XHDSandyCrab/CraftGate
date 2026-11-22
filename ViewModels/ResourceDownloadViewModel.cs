using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using CraftGate.Models;
using CraftGate.Services;
using CraftGate.Core.Game;

namespace CraftGate.ViewModels
{
    public class ResourceDownloadViewModel : ViewModelBase
    {
        // 本地已安装版本信息：目录名 + 解析出的 MC 版本 + 加载器
        private class InstalledVersionInfo
        {
            public string DirName { get; set; }
            public string McVersion { get; set; }
            public string Loader { get; set; }
        }

        private string _searchText = "";
        private string _selectedType = "mod";
        private bool _isLoading;
        private bool _isLoadingMore;
        private string _statusText = "正在加载热门资源...";

        private string _currentMcVersion = "1.21.10";
        private string _currentLoader = ""; // 👈 新增：当前加载器

        private bool _isVersionSelectVisible;
        private bool _isDependencyPopupVisible;

        private ModrinthProject _selectedProjectForDownload;
        private string _lastInstallDir;   // 最近一次下载的安装目录，用于完成后提示用户装到哪了
        private HashSet<string> _installedModNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>扫描当前账户所有版本目录下已安装的 mod 文件名（含账户隔离目录）。每次进版本页都重扫，避免缓存导致识别不准。</summary>
        private void RefreshInstalledMods()
        {
            _installedModNames.Clear();
            try
            {
                string versionsRoot = PathManager.VersionsDir;
                string accountId = CraftGate.Services.AccountService.CurrentAccount?.Id;
                if (!Directory.Exists(versionsRoot) || string.IsNullOrEmpty(accountId)) return;

                // 账户隔离目录下所有版本的 mods
                foreach (var verDir in Directory.GetDirectories(versionsRoot))
                {
                    string mods = Path.Combine(verDir, "accounts", accountId, "mods");
                    if (!Directory.Exists(mods)) continue;
                    foreach (var f in Directory.GetFiles(mods, "*.jar"))
                        _installedModNames.Add(NormalizeModName(Path.GetFileNameWithoutExtension(f)));
                }
                // 非隔离模式：版本目录根下的 mods
                foreach (var verDir in Directory.GetDirectories(versionsRoot))
                {
                    string mods = Path.Combine(verDir, "mods");
                    if (!Directory.Exists(mods)) continue;
                    foreach (var f in Directory.GetFiles(mods, "*.jar"))
                        _installedModNames.Add(NormalizeModName(Path.GetFileNameWithoutExtension(f)));
                }
            }
            catch { }
        }

        /// <summary>把 mod 文件名归一化为可比较的标识：去掉版本号/loader 前缀等，只留 mod 主名。</summary>
        private static string NormalizeModName(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return "";
            var s = fileName.ToLowerInvariant();
            // 去掉常见 loader 前缀与版本号尾部，例如 iris-fabric-1.9.6+mc1.21.8 -> iris
            foreach (var loader in new[] { "-fabric-", "-forge-", "-neoforge-", "-quilt-" })
            {
                int i = s.IndexOf(loader);
                if (i > 0) { s = s.Substring(0, i); break; }
            }
            // 去掉 -数字 及之后
            var m = System.Text.RegularExpressions.Regex.Match(s, @"-\d");
            if (m.Success) s = s.Substring(0, m.Index);
            return s.Trim('-', '_', ' ');
        }
        private string _popupTitle;
        private bool _isLoadingVersions;

        private int _currentOffset = 0;
        private const int PageLimit = 50;
        private CancellationTokenSource _searchCts;
        private static readonly HttpClient _imageClient = new HttpClient();
        private static SemaphoreSlim _translateSemaphore = new SemaphoreSlim(3);
        private static SemaphoreSlim _iconSemaphore = new SemaphoreSlim(4); // 图标并发限流，避免瞬间几十个请求导致界面"太忙"

        static ResourceDownloadViewModel()
        {
            _imageClient.DefaultRequestHeaders.Add("User-Agent", "CraftGate/1.0 (contact@craftgate.com)");
            _imageClient.Timeout = TimeSpan.FromSeconds(15);
        }

        public string SearchText { get => _searchText; set => Set(ref _searchText, value); }
        public bool IsLoading { get => _isLoading; set => Set(ref _isLoading, value); }
        public bool IsLoadingMore { get => _isLoadingMore; set => Set(ref _isLoadingMore, value); }
        public string StatusText { get => _statusText; set => Set(ref _statusText, value); }
        public string PopupTitle { get => _popupTitle; set => Set(ref _popupTitle, value); }

        public bool IsVersionSelectVisible { get => _isVersionSelectVisible; set => Set(ref _isVersionSelectVisible, value); }
        public bool IsDependencyPopupVisible { get => _isDependencyPopupVisible; set => Set(ref _isDependencyPopupVisible, value); }
        public bool IsLoadingVersions { get => _isLoadingVersions; set => Set(ref _isLoadingVersions, value); }

        public ObservableCollection<ModrinthProject> Results { get; } = new ObservableCollection<ModrinthProject>();
        public ObservableCollection<VersionGroup> GroupedVersions { get; } = new ObservableCollection<VersionGroup>();
        // 版本选择界面分两块：兼容（支持本地已安装 MC 版本）/ 其他
        public ObservableCollection<VersionGroup> CompatibleGroups { get; } = new ObservableCollection<VersionGroup>();
        public ObservableCollection<VersionGroup> OtherGroups { get; } = new ObservableCollection<VersionGroup>();
        public ObservableCollection<DependencyDisplayItem> DownloadConfirmationList { get; } = new ObservableCollection<DependencyDisplayItem>();

        public ICommand SearchCommand { get; }
        public ICommand LoadMoreCommand { get; }
        public ICommand FilterCommand { get; }
        public ICommand ShowVersionPageCommand { get; }
        public ICommand CloseVersionPageCommand { get; }
        public ICommand PrepareDownloadCommand { get; }
        public ICommand ConfirmDependenciesCommand { get; }
        public ICommand CloseDependencyPopupCommand { get; }
        public ICommand ToggleAllDependenciesCommand { get; }

        public ResourceDownloadViewModel()
        {
            SearchCommand = new RelayCommand(_ => _ = SearchAsync());
            LoadMoreCommand = new RelayCommand(_ => _ = LoadMoreAsync());
            FilterCommand = new RelayCommand(type =>
            {
                if (type != null) { _selectedType = type.ToString(); _ = SearchAsync(); }
            });

            ShowVersionPageCommand = new RelayCommand(param => _ = ShowVersionPageAsyncSafely(param as ModrinthProject));
            CloseVersionPageCommand = new RelayCommand(_ => IsVersionSelectVisible = false);
            PrepareDownloadCommand = new RelayCommand(async param => await RunSafely(() => PrepareDownloadAsync(param as ModrinthVersion)));
            ConfirmDependenciesCommand = new RelayCommand(async _ => await RunSafely(ExecuteDownloadAsync));
            CloseDependencyPopupCommand = new RelayCommand(_ => IsDependencyPopupVisible = false);

            ToggleAllDependenciesCommand = new RelayCommand(_ =>
            {
                bool allSelected = DownloadConfirmationList.All(d => d.IsSelected);
                foreach (var dep in DownloadConfirmationList) dep.IsSelected = !allSelected;
            });

            // 窗口不可见时不发起自动搜索，恢复可见时补跑（省网络/CPU）
            VisibilityService.RunWhenVisible(() => _ = SearchAsync());
        }

        // 下载流程诊断日志：写到 Logs/download_debug.log，卡住时看最后一行即可定位
        private static void DbgLog(string msg)
        {
            string line = $"[{DateTime.Now:HH:mm:ss.fff}] {msg}{Environment.NewLine}";
            // 双保险：程序目录 + 系统临时目录，总有一处可写
            try
            {
                Directory.CreateDirectory(PathManager.LogsDir);
                File.AppendAllText(Path.Combine(PathManager.LogsDir, "download_debug.log"), line);
            }
            catch { }
            try
            {
                File.AppendAllText(Path.Combine(Path.GetTempPath(), "craftgate_download_debug.log"), line);
            }
            catch { }
        }

        // 统一包裹异步命令：任何异常都写进状态栏并提示，避免"无声卡住"
        private async Task RunSafely(Func<Task> action)
        {
            try { await action().ConfigureAwait(true); }
            catch (Exception ex)
            {
                DbgLog("【异常】" + ex.GetType().Name + ": " + ex.Message + "\n" + ex.StackTrace);
                StatusText = "操作失败: " + ex.Message;
                if (Application.Current.MainWindow is MainWindow mw)
                    mw.ShowToastError("操作失败: " + ex.Message);
            }
        }

        private Task ShowVersionPageAsyncSafely(ModrinthProject project)
        {
            return RunSafely(() => ShowVersionPageAsync(project));
        }

        // 👇 核心修复：用正则分离纯版本号和加载器
        public void SetMcVersion(string rawVersion)
        {
            if (string.IsNullOrEmpty(rawVersion) || rawVersion == "无可用版本") return;

            // 用兼容服务统一解析版本号和加载器（含 NeoForge 识别）
            ModCompatibilityService.ParseVersionId(rawVersion, out string mcVer, out string loader);
            _currentMcVersion = mcVer;
            _currentLoader = loader;

            string loaderText = string.IsNullOrEmpty(_currentLoader) ? "原版" : _currentLoader;
            StatusText = $"已切换筛选: MC {_currentMcVersion} ({loaderText})";

            VisibilityService.RunWhenVisible(() => _ = SearchAsync());
        }

        private async Task SearchAsync()
        {
            _searchCts?.Cancel();
            _searchCts = new CancellationTokenSource();
            var token = _searchCts.Token;

            IsLoading = true;
            _currentOffset = 0;
            Results.Clear();
            StatusText = "正在处理搜索请求...";

            string query = SearchText.Trim();
            if (!string.IsNullOrEmpty(query))
            {
                string translatedQuery = await ModrinthService.TranslateToEnglishViaMCModAsync(query);
                if (translatedQuery != query)
                {
                    StatusText = $"已识别“{query}”为“{translatedQuery}”，正在搜索...";
                    query = translatedQuery;
                    await Task.Delay(300);
                }
            }

            await FetchNextPageAsync(query, token, isFirstPage: true);
            IsLoading = false;
        }

        private async Task LoadMoreAsync()
        {
            if (IsLoading || IsLoadingMore) return;

            IsLoadingMore = true;
            _searchCts?.Cancel();
            _searchCts = new CancellationTokenSource();
            var token = _searchCts.Token;

            string query = SearchText.Trim();
            string translatedQuery = await ModrinthService.TranslateToEnglishViaMCModAsync(query);
            if (translatedQuery != query) query = translatedQuery;

            await FetchNextPageAsync(query, token, isFirstPage: false);
            IsLoadingMore = false;
        }

        private async Task FetchNextPageAsync(string query, CancellationToken token, bool isFirstPage)
        {
            // 👇 传入 loader 参数（搜索已放宽，这里本地计算兼容性并排序）
            var hits = await ModrinthService.SearchAsync(query, _selectedType, _currentMcVersion, _currentLoader, _currentOffset, PageLimit, token);

            // 计算每个结果对当前环境的兼容性，兼容的排在前面
            foreach (var hit in hits)
            {
                hit.IsCompatible = ModCompatibilityService.IsVersionCompatible(
                    hit.GameVersions, hit.Loaders, _currentMcVersion, _currentLoader);
            }
            hits = hits.OrderByDescending(h => h.IsCompatible).ToList();

            if (token.IsCancellationRequested || hits == null || hits.Count == 0)
            {
                if (hits != null && hits.Count == 0 && !token.IsCancellationRequested)
                {
                    if (isFirstPage) StatusText = $"没有找到匹配 MC {_currentMcVersion} 的资源，请尝试清空搜索框或切换版本。";
                    else StatusText = "已经到底啦，没有更多结果了。";
                }
                return;
            }

            _currentOffset += hits.Count;

            for (int i = 0; i < hits.Count; i++)
            {
                if (token.IsCancellationRequested) break;

                var hit = hits[i];
                Application.Current.Dispatcher.Invoke(() => { Results.Add(hit); });

                _ = LoadIconAsync(hit);
                _ = AutoTranslateAsync(hit);

                if (i > 0 && i % 5 == 0)
                {
                    StatusText = $"已加载 {Results.Count} 个结果...";
                    await Task.Delay(50, token);
                }
            }

            if (!token.IsCancellationRequested)
            {
                StatusText = $"已加载 {Results.Count} 个结果。";
            }
        }

        private async Task AutoTranslateAsync(ModrinthProject project)
        {
            if (project == null) return;

            // ① 模组名：先同步查词库，命中则立即显示中文（无网络延迟）
            string instantName = ModTranslationService.TranslateInstant(project.Title);
            if (instantName != project.Title)
            {
                Application.Current.Dispatcher.Invoke(() => project.DisplayName = instantName);
            }

            await _translateSemaphore.WaitAsync();
            try
            {
                // ② 模组名：词库未命中则在线补漏
                if (string.Equals(project.DisplayName, project.Title, StringComparison.OrdinalIgnoreCase))
                {
                    string nameZh = await ModTranslationService.TranslateAsync(project.Title);
                    if (!string.IsNullOrEmpty(nameZh) && nameZh != project.Title)
                    {
                        Application.Current.Dispatcher.Invoke(() => project.DisplayName = nameZh);
                    }
                }

                // ③ 简介翻译（原有逻辑）
                if (!project.IsTranslated && !string.IsNullOrEmpty(project.Description))
                {
                    string translated = await ModrinthService.TranslateToChineseAsync(project.Description);
                    if (!string.IsNullOrEmpty(translated) && translated != project.Description)
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            project.Description = translated;
                            project.IsTranslated = true;
                        });
                    }
                }
            }
            catch { }
            finally { _translateSemaphore.Release(); }
        }

        private async Task LoadIconAsync(ModrinthProject project)
        {
            if (string.IsNullOrEmpty(project.IconUrl)) return;
            await _iconSemaphore.WaitAsync();
            try
            {
                var bytes = await _imageClient.GetByteArrayAsync(project.IconUrl);
                using (var stream = new MemoryStream(bytes))
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.StreamSource = stream;
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.DecodePixelWidth = 80;
                    bitmap.EndInit();
                    bitmap.Freeze();
                    Application.Current.Dispatcher.Invoke(() => { project.IconImage = bitmap; });
                }
            }
            catch { }
            finally { _iconSemaphore.Release(); }
        }

        /// <summary>扫描本地已安装的游戏版本。前置：必须有模组加载器，纯原版不纳入。</summary>
        private static List<InstalledVersionInfo> GetInstalledMcVersions()
        {
            var list = new List<InstalledVersionInfo>();
            try
            {
                if (Directory.Exists(PathManager.VersionsDir))
                {
                    foreach (var dir in Directory.GetDirectories(PathManager.VersionsDir))
                    {
                        string v = Path.GetFileName(dir);
                        if (!File.Exists(Path.Combine(dir, $"{v}.json"))) continue;
                        ModCompatibilityService.ParseVersionId(v, out string mc, out string loader);
                        // 前置条件：必须有模组加载器（原版装不了 mod，不参与兼容判定）
                        if (string.IsNullOrEmpty(loader)) continue;
                        list.Add(new InstalledVersionInfo { DirName = v, McVersion = mc, Loader = loader });
                    }
                }
            }
            catch { }
            return list;
        }

        /// <summary>找到与指定 MC 版本+加载器匹配的真实已装版本目录名；找不到返回 null。</summary>
        private static string FindInstalledDir(string mcVersion, string loader)
        {
            var installed = GetInstalledMcVersions();
            // 优先：MC 版本相同 + 加载器兼容
            var exact = installed.FirstOrDefault(i =>
                string.Equals(i.McVersion, mcVersion, StringComparison.OrdinalIgnoreCase) &&
                ModCompatibilityService.IsLoaderCompatible(loader, i.Loader));
            if (exact != null) return exact.DirName;
            // 回退：仅 MC 版本相同（loader 目录名可能不带后缀）
            var loose = installed.FirstOrDefault(i =>
                string.Equals(i.McVersion, mcVersion, StringComparison.OrdinalIgnoreCase));
            return loose?.DirName;
        }

        /// <summary>
        /// 扫描所有已装版本目录（含不带 loader 后缀的），用于下载目标定位。
        /// 注意：游戏启动用的 gameDir = versions/&lt;versionId&gt;，与是否有 loader 无关，
        /// 所以下载/扫描必须看全部目录——不能套用"兼容判定"的 loader 前置过滤。
        /// </summary>
        private static List<InstalledVersionInfo> GetAllInstalledDirs()
        {
            var list = new List<InstalledVersionInfo>();
            try
            {
                if (Directory.Exists(PathManager.VersionsDir))
                {
                    foreach (var dir in Directory.GetDirectories(PathManager.VersionsDir))
                    {
                        string v = Path.GetFileName(dir);
                        if (!File.Exists(Path.Combine(dir, $"{v}.json"))) continue;
                        ModCompatibilityService.ParseVersionId(v, out string mc, out string loader);
                        list.Add(new InstalledVersionInfo { DirName = v, McVersion = mc, Loader = loader });
                    }
                }
            }
            catch { }
            return list;
        }

        /// <summary>
        /// 根据具体模组版本自带的 game_versions/loaders，找到应安装到的已装版本目录。
        /// 每个版本各自判定——主 mod 与依赖可能是不同加载器/版本，装到不同目录。
        /// 优先级：MC版本+加载器都兼容 > MC版本相同 > 当前筛选环境兜底。
        /// </summary>
        private static string FindDirForVersion(ModrinthVersion version)
        {
            if (version == null) return null;
            // 用全部已装目录（含无 loader 后缀），因为游戏就从这些目录启动
            var installed = GetAllInstalledDirs();
            var gvs = version.GameVersions ?? new List<string>();
            var loaders = version.Loaders ?? new List<string>();

            // mod 明确带 loader 时（如 fabric/forge），必须装到同 loader 的目录，
            // 不能装到无 loader 的原版目录——否则游戏从 fabric 目录启动，根本读不到。
            bool hasLoader = loaders.Count > 0;

            // 1) 优先：版本 + loader 都匹配（且目录确实带 loader）
            var exact = new List<InstalledVersionInfo>();
            foreach (var inst in installed)
            {
                bool verOk = gvs.Any(g => string.Equals(g, inst.McVersion, StringComparison.OrdinalIgnoreCase));
                bool loaderOk = !string.IsNullOrEmpty(inst.Loader) &&
                    loaders.Any(l => ModCompatibilityService.IsLoaderCompatible(inst.Loader, l));
                if (verOk && loaderOk) exact.Add(inst);
            }
            if (exact.Count > 0)
                return exact.OrderByDescending(i => i.McVersion,
                    Comparer<string>.Create(ModCompatibilityService.CompareVersion)).First().DirName;

            // 2) 无 loader 信息的资源（如资源包/数据包）：匹配同 MC 版本目录，原版目录也算
            if (!hasLoader)
            {
                var loose = installed.Where(i => gvs.Any(g => string.Equals(g, i.McVersion, StringComparison.OrdinalIgnoreCase)))
                    .OrderByDescending(i => i.McVersion, Comparer<string>.Create(ModCompatibilityService.CompareVersion)).FirstOrDefault();
                return loose?.DirName;
            }

            // 3) mod 带 loader 但没找到对应 loader 目录：不装到原版目录，返回 null 让调用方回退
            return null;
        }

        private async Task ShowVersionPageAsync(ModrinthProject project)
        {
            if (project == null) return;
            _selectedProjectForDownload = project;
            RefreshInstalledMods();   // 每次点进版本页都重新扫描已装模组，避免重复识别
            // 标题用中文译名（词库命中即时返回，否则沿用英文名）
            string displayName = ModTranslationService.TranslateInstant(project.Title);
            PopupTitle = $"选择 {displayName} 的下载版本";

            IsVersionSelectVisible = true;
            IsLoadingVersions = true;
            GroupedVersions.Clear();
            CompatibleGroups.Clear();
            OtherGroups.Clear();
            var installedVersions = GetInstalledMcVersions();

            try
            {
                var allVersions = await ModrinthService.GetVersionsAsync(project.ProjectId, null, null);

                if (allVersions.Count == 0)
                {
                    StatusText = "该资源没有可用的下载版本。";
                }
                else
                {
                    // 【多版本展开】一个模组版本支持多个 MC 版本时，展开进每一个版本组。
                    // 例：Iris 1.9.6-neoforge 的 game_versions=[1.21.6,1.21.7,1.21.8]，
                    // 应同时出现在 1.21.6/1.21.7/1.21.8 组——这正是 PCL 能显示"兼容 1.21.8"的原因。
                    var versionByGame = new Dictionary<string, List<ModrinthVersion>>(StringComparer.OrdinalIgnoreCase);
                    foreach (var v in allVersions.Where(v => v.GameVersions != null && v.GameVersions.Count > 0))
                    {
                        foreach (string gv in v.GameVersions.Distinct())
                        {
                            if (!versionByGame.TryGetValue(gv, out var bucket))
                                versionByGame[gv] = bucket = new List<ModrinthVersion>();
                            bucket.Add(v);
                        }
                    }

                    var sortedKeys = versionByGame.Keys.OrderByDescending(k => ParseVersion(k)).ToList();
                    foreach (string gv in sortedKeys)
                    {
                        var bucket = versionByGame[gv];
                        var versionGroup = new VersionGroup
                        {
                            GameVersion = gv,
                            IsExpanded = (gv == _currentMcVersion)
                        };
                        foreach (var v in bucket.OrderByDescending(v => v.VersionNumber))
                            versionGroup.Versions.Add(v);
                        // 注意：XAML 已改为两个独立区块（兼容/其他），不再绑定 GroupedVersions。
                        // 同一个 VersionGroup 对象只加入一个集合，避免跨集合渲染导致视觉错乱。

                        // 兼容判定：这个 MC 版本组里，有任意版本被已装环境（版本+加载器）兼容
                        bool compatible = bucket.Any(v =>
                            installedVersions.Any(inst =>
                                ModCompatibilityService.IsVersionCompatible(
                                    v.GameVersions, v.Loaders, inst.McVersion, inst.Loader)));
                        (compatible ? CompatibleGroups : OtherGroups).Add(versionGroup);
                    }
                    StatusText = $"共 {allVersions.Count} 个版本：兼容 {CompatibleGroups.Count} 组 / 其他 {OtherGroups.Count} 组。";
                }
            }
            catch (Exception)
            {
                StatusText = "获取版本列表失败，请重试。";
            }

            IsLoadingVersions = false;
        }

        private Version ParseVersion(string version)
        {
            try
            {
                var match = Regex.Match(version, @"(\d+)(\.\d+)*");
                if (match.Success) return Version.Parse(match.Value);
            }
            catch { }
            return new Version(0, 0);
        }

        // 分析单个依赖（供并行调用）；返回 null 表示该依赖无需/无法下载
        private async Task<DependencyDisplayItem> AnalyzeDependencyAsync(ModrinthDependency dep)
        {
            try
            {
                string depProjectId = dep.ProjectId;
                if (string.IsNullOrEmpty(depProjectId) && !string.IsNullOrEmpty(dep.VersionId))
                {
                    depProjectId = await ModrinthService.GetProjectIdByVersionIdAsync(dep.VersionId);
                    dep.ProjectId = depProjectId;
                }
                if (string.IsNullOrEmpty(depProjectId)) return null;

                var projInfo = await ModrinthService.GetProjectInfoAsync(depProjectId);
                if (projInfo == null) return null;
                if (projInfo.ProjectId == _selectedProjectForDownload.ProjectId) return null;

                var depVersions = await ModrinthService.GetVersionsAsync(depProjectId, null, null);
                var installed = GetInstalledMcVersions();
                var bestVersion = depVersions.FirstOrDefault(v =>
                        installed.Any(inst =>
                            ModCompatibilityService.IsVersionCompatible(
                                v.GameVersions, v.Loaders, inst.McVersion, inst.Loader)))
                    ?? depVersions.FirstOrDefault(v =>
                        ModCompatibilityService.IsVersionCompatible(
                            v.GameVersions, v.Loaders, _currentMcVersion, _currentLoader))
                    ?? depVersions.FirstOrDefault();
                if (bestVersion == null) return null;

                projInfo.DisplayName = ModTranslationService.TranslateInstant(projInfo.Title);
                _ = LoadIconAsync(projInfo);
                return new DependencyDisplayItem
                {
                    ProjectInfo = projInfo,
                    VersionInfo = bestVersion,
                    IsSelected = true,
                    IsMainMod = false
                };
            }
            catch { return null; }
        }

        private async Task PrepareDownloadAsync(ModrinthVersion version)
        {
            DbgLog("=== PrepareDownloadAsync 开始 ===");
            if (version == null || _selectedProjectForDownload == null) { DbgLog("  参数为空,返回"); return; }
            DownloadConfirmationList.Clear();
            StatusText = "正在分析下载内容...";

            // 已安装则默认不勾选（隐藏勾选框、标记已下载）
            bool mainInstalled = _installedModNames.Contains(NormalizeModName(_selectedProjectForDownload.Title));
            DownloadConfirmationList.Add(new DependencyDisplayItem
            {
                ProjectInfo = _selectedProjectForDownload,
                VersionInfo = version,
                IsSelected = !mainInstalled,
                IsInstalled = mainInstalled,
                IsMainMod = true
            });

            if (_selectedProjectForDownload.ProjectType != "modpack")
            {
                if (version.Dependencies != null && version.Dependencies.Count > 0)
                {
                    // 去重 + 并行分析：所有依赖同时发请求（配合本地缓存），
                    // 避免串行 await 导致多个依赖累积等待数秒、表现为"卡住"。
                    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    {
                        _selectedProjectForDownload.ProjectId
                    };
                    var requiredDeps = version.Dependencies
                        .Where(d => d.DependencyType == "required")
                        .Where(d => !string.IsNullOrEmpty(d.ProjectId) || !string.IsNullOrEmpty(d.VersionId))
                        .Where(d => seen.Add(!string.IsNullOrEmpty(d.ProjectId) ? d.ProjectId : d.VersionId))
                        .ToList();

                    var depTasks = requiredDeps.Select(dep => AnalyzeDependencyAsync(dep));
                    DbgLog($"  依赖数 {depTasks.Count()}, 开始并发分析...");
                    var depResults = await Task.WhenAll(depTasks);
                    DbgLog($"  依赖分析完成");

                    // 二次去重：按 ProjectId（显示名可能相同但 id 不同 → 重复识别），并标记已安装
                    var addedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    {
                        _selectedProjectForDownload.ProjectId
                    };
                    foreach (var depItem in depResults)
                    {
                        if (depItem == null) continue;
                        string pid = depItem.ProjectInfo?.ProjectId;
                        if (!string.IsNullOrEmpty(pid) && !addedIds.Add(pid)) continue;  // 同一 mod 只出现一次
                        depItem.IsInstalled = _installedModNames.Contains(
                            NormalizeModName(depItem.ProjectInfo?.Title ?? depItem.ProjectInfo?.DisplayName ?? ""));
                        if (depItem.IsInstalled) depItem.IsSelected = false;
                        DownloadConfirmationList.Add(depItem);
                    }
                }
            }

            DbgLog($"=== PrepareDownloadAsync 结束, 项数 {DownloadConfirmationList.Count} ===");
            IsDependencyPopupVisible = true;
            StatusText = $"请确认下载内容（共 {DownloadConfirmationList.Count} 个文件）。";
        }

        private async Task ExecuteDownloadAsync()
        {
            DbgLog("=== ExecuteDownloadAsync 开始 ===");
            IsDependencyPopupVisible = false;
            IsVersionSelectVisible = false;


            var itemsToDownload = DownloadConfirmationList.Where(d => d.IsSelected).ToList();
            int totalCount = itemsToDownload.Count;
            int successCount = 0;

            if (totalCount == 0) return;

            // 统一总进度：解析阶段 + 所有文件的下载阶段融合成一个单调递增的百分比。
            // 解析准备占前 10%，其余 90% 按文件数均摊（含每个文件内部下载进度）。
            const double prepareShare = 10.0;
            const double downloadShare = 90.0;
            double overallP = 0;
            if (Application.Current.MainWindow is MainWindow mainWin)
            {
                mainWin.ShowToast("下载任务", $"正在解析 {totalCount} 个文件...", isDownload: true);
                mainWin.UpdateToast(0, "正在解析下载内容...");
            }

            int fileIndex = 0;
            foreach (var item in itemsToDownload)
            {
                var project = item.ProjectInfo;
                var mainVersion = item.VersionInfo;
                DbgLog($"处理项: {project?.Title} type={project?.ProjectType} ver={mainVersion?.VersionNumber}");

                // 每个下载项按自己的版本(loader+MC版本)决定装到哪个游戏目录——
                // 主 mod 与依赖可能是不同加载器/版本，各自归位（PCL 风格）。
                // 资源装到当前账户的隔离目录（每个账户独立的 mods/config/资源包）
                string versionForDir = project.ProjectType != "modpack" && ConfigManager.Settings.EnableVersionIsolation
                    ? (FindDirForVersion(mainVersion) ?? mainVersion?.VersionNumber)
                    : mainVersion?.VersionNumber;
                string gameDir = PathManager.GetCurrentAccountGameDir(versionForDir);

                string targetDir = Path.Combine(gameDir, "mods");
                if (project.ProjectType == "shader") targetDir = Path.Combine(gameDir, "shaderpacks");
                else if (project.ProjectType == "datapack") targetDir = Path.Combine(gameDir, "datapacks");
                else if (project.ProjectType == "resourcepack") targetDir = Path.Combine(gameDir, "resourcepacks");
                else if (project.ProjectType == "modpack")
                {
                    targetDir = Path.Combine(PathManager.LauncherDir, "Downloads", "Modpacks");
                }

                var mainFile = mainVersion.Files.FirstOrDefault(f => f.Primary) ?? mainVersion.Files.FirstOrDefault();
                if (mainFile == null) { DbgLog("  无文件,跳过"); continue; }

                _lastInstallDir = targetDir;
                DbgLog($"  开始下载 -> {targetDir}");
                StatusText = $"正在下载: {project.Title}...";

                // 解析完文件信息：先给该文件的基础进度
                overallP = prepareShare + fileIndex * downloadShare / Math.Max(1, totalCount);
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (Application.Current.MainWindow is MainWindow mw0)
                        mw0.UpdateToast((int)overallP, $"解析完成，准备下载 {project.Title}...");
                });

                bool success = await ModrinthService.DownloadFileAsync(mainFile.Url, targetDir, mainFile.Filename, p =>
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        // 总进度 = 10%（解析） + 当前文件在总量中的占比 * 该文件内部进度
                        double pct = prepareShare + (fileIndex + p / 100.0) * downloadShare / Math.Max(1, totalCount);
                        pct = Math.Min(99, pct);
                        StatusText = $"下载 {project.Title} 中... 总进度 {pct:F0}%";
                        if (Application.Current.MainWindow is MainWindow mw)
                            mw.UpdateToast((int)pct, $"下载 {project.Title}... 总进度 {pct:F0}%");
                    });
                });
                fileIndex++;
                overallP = prepareShare + fileIndex * downloadShare / Math.Max(1, totalCount);

                DbgLog($"  下载结果 success={success}");
                if (success) successCount++;

                if (success && project.ProjectType == "modpack")
                {
                    string mrpackPath = Path.Combine(targetDir, mainFile.Filename);
                    string newVersionName = project.Title.Replace(" ", "_") + "_" + mainVersion.VersionNumber;

                    if (Application.Current.MainWindow is MainWindow mwInstall)
                        mwInstall.ShowToast("整合包安装", $"正在解压并安装 {project.Title}...");

                    await ModpackInstaller.InstallModpackAsync(mrpackPath, newVersionName, (msg, p) =>
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            StatusText = msg;
                            // 整合包安装进度也并入总进度，不回退
                            double pct = overallP + p / 100.0 * (downloadShare / Math.Max(1, totalCount));
                            if (Application.Current.MainWindow is MainWindow mw)
                                mw.UpdateToast((int)Math.Min(99, pct), msg);
                        });
                    });
                }
            }

            DbgLog($"=== ExecuteDownloadAsync 结束 success={successCount}/{totalCount} ===");
            if (successCount == totalCount)
            {
                if (Application.Current.MainWindow is MainWindow mwDone)
                    mwDone.UpdateToast(100, "全部完成");
                StatusText = $"全部 {totalCount} 个文件处理完成！";
                if (Application.Current.MainWindow is MainWindow mw)
                {
                    string dirHint = string.IsNullOrEmpty(_lastInstallDir) ? "" : $"\n安装位置: {_lastInstallDir}";
                    mw.ShowToastSuccess($"全部操作已完成！{dirHint}");
                }
            }
            else
            {
                StatusText = $"处理结束，成功 {successCount} / {totalCount}。";
                if (Application.Current.MainWindow is MainWindow mw)
                    mw.ShowToastError($"部分文件操作失败！");
            }
        }
    }
}