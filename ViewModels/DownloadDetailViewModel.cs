using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using CraftGate.Models;
using CraftGate.Services;
using CraftGate.Core.Game;

namespace CraftGate.ViewModels
{
    public class DownloadDetailViewModel : ViewModelBase
    {
        private bool _isLoading = true;
        private bool _isDownloading;
        private double _downloadProgress;
        private string _downloadStatus;

        private readonly string _versionId;
        private readonly string _versionUrl;

        public string VersionId => _versionId;
        public bool IsLoading { get => _isLoading; set => Set(ref _isLoading, value); }
        public bool IsDownloading { get => _isDownloading; set => Set(ref _isDownloading, value); }
        public double DownloadProgress { get => _downloadProgress; set => Set(ref _downloadProgress, value); }
        public string DownloadStatus { get => _downloadStatus; set => Set(ref _downloadStatus, value); }

        public ObservableCollection<LoaderInfo> Loaders { get; } = new ObservableCollection<LoaderInfo>();

        // ===== 向导：基本信息 =====
        public ObservableCollection<WizardIconOption> IconOptions { get; } = new ObservableCollection<WizardIconOption>();
        private string _instanceName;
        public string InstanceName { get => _instanceName; set => Set(ref _instanceName, value); }
        public ICommand PickLocalIconCommand { get; }

        // ===== 向导：附带模块 =====
        public ObservableCollection<WizardModule> Modules { get; } = new ObservableCollection<WizardModule>();

        // ===== 向导：版本选择子页 =====
        private bool _isVersionPageOpen;
        public bool IsVersionPageOpen { get => _isVersionPageOpen; set => Set(ref _isVersionPageOpen, value); }
        private string _versionPageTitle;
        public string VersionPageTitle { get => _versionPageTitle; set => Set(ref _versionPageTitle, value); }
        public ObservableCollection<string> VersionPageItems { get; } = new ObservableCollection<string>();
        // 当前正在编辑版本的目标：null=加载器，否则为模块
        private LoaderInfo _versionTargetLoader;
        private WizardModule _versionTargetModule;
        public ICommand VersionItemClickCommand { get; }
        public ICommand CloseVersionPageCommand { get; }

        public ICommand StartDownloadCommand { get; }

        private static readonly HttpClient _imageClient = new HttpClient();

        public DownloadDetailViewModel(string versionId, string versionUrl)
        {
            _versionId = versionId;
            _versionUrl = versionUrl;
            InstanceName = versionId;
            StartDownloadCommand = new RelayCommand(_ => _ = StartDownloadAsync(), _ => !IsDownloading);
            PickLocalIconCommand = new RelayCommand(_ => PickLocalIcon());
            VersionItemClickCommand = new RelayCommand(p => OnVersionItemSelected(p as string));
            CloseVersionPageCommand = new RelayCommand(_ => IsVersionPageOpen = false);
        }

        public async Task InitializeAsync()
        {
            IsLoading = true;

            // 1. 基本信息：内置图标
            InitIcons();

            // 2. 加载器（所有：vanilla/forge/neoforge/optifine/fabric/fabric-api/quilt/qsl）
            var loaders = await LoaderApiService.GetAvailableLoadersAsync(VersionId);
            Loaders.Clear();
            foreach (var l in loaders)
            {
                // 默认勾选 Fabric（若存在），并选中其首个版本
                if (l.Id == "fabric")
                {
                    var first = l.AvailableVersions.FirstOrDefault();
                    if (first != null) { first.IsSelected = true; l.IsChecked = true; }
                }
                l.OnEditVersion = li => OpenVersionPage(li, null);
                l.OnEditVersionCommand = new RelayCommand(_ => OpenVersionPage(l, null));
                Loaders.Add(l);
            }

            // 3. 附带模块（光影/小地图/模组列表）
            await InitModulesAsync();

            IsLoading = false;
        }

        private void InitIcons()
        {
            IconOptions.Clear();
            var builtin = new (string name, string file)[]
            {
                ("草方块", "Grass.png"),
                ("圆石", "CobbleStone.png"),
                ("Fabric", "Fabric.png"),
                ("Forge", "Forge.png"),
                ("NeoForge", "NeoForge.png"),
                ("Quilt", "Quilt.png"),
                ("命令方块", "CommandBlock.png")
            };
            foreach (var (name, file) in builtin)
            {
                var opt = new WizardIconOption
                {
                    Name = name,
                    IconPath = "pack://application:,,,/CraftGate;component/Assets/" + file,
                    IconFile = file,
                    IsBuiltin = true
                };
                if (name == "草方块") opt.IsSelected = true;
                opt.SelectCommand = new RelayCommand(_ =>
                {
                    foreach (var o in IconOptions) o.IsSelected = false;
                    opt.IsSelected = true;
                });
                IconOptions.Add(opt);
            }
        }

        private void PickLocalIcon()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "选择实例图标（PNG）",
                Filter = "图片文件|*.png;*.jpg;*.jpeg;*.bmp"
            };
            if (dlg.ShowDialog() != true) return;
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(dlg.FileName);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.DecodePixelWidth = 64;
                bmp.EndInit();
                bmp.Freeze();
                foreach (var o in IconOptions) o.IsSelected = false;
                IconOptions.Add(new WizardIconOption
                {
                    Name = "自定义",
                    IconPath = dlg.FileName,
                    IconFile = dlg.FileName,
                    IconImage = bmp,
                    IsBuiltin = false,
                    IsSelected = true
                });
            }
            catch (Exception ex) { ShowToastErr("读取图片失败：" + ex.Message); }
        }

        private async Task InitModulesAsync()
        {
            Modules.Clear();
            // 模块定义：光影(钠+虹膜+ComplementaryUnbound，图标=ComplementaryUnbound)、
            // 小地图(Xaero)、模组列表(Mod Menu)
            var defs = new[]
            {
                new
                {
                    Id = "shader", Name = "光影", Desc = "钠 + 虹膜光影 + ComplementaryUnbound",
                    IconKeyword = "ComplementaryUnbound", IconFile = "Light_Shadow.png",
                    Mods = new[]
                    {
                        (Name:"钠（渲染优化）", Keyword:"Sodium"),
                        (Name:"虹膜光影", Keyword:"Iris"),
                        (Name:"ComplementaryUnbound", Keyword:"ComplementaryUnbound")
                    }
                },
                new
                {
                    Id = "minimap", Name = "小地图", Desc = "Xaero 小地图",
                    IconKeyword = "Xaero's Minimap", IconFile = "Xaero_Minimap.png",
                    Mods = new[] { (Name:"Xaero 小地图", Keyword:"Xaero's Minimap") }
                },
                new
                {
                    Id = "modlist", Name = "模组列表", Desc = "Mod Menu 模组菜单",
                    IconKeyword = "Mod Menu", IconFile = "Mod_Menu.png",
                    Mods = new[] { (Name:"模组菜单", Keyword:"Mod Menu") }
                }
            };

            foreach (var def in defs)
            {
                var module = new WizardModule
                {
                    Id = def.Id,
                    Name = def.Name,
                    Desc = def.Desc,
                    IconPath = "pack://application:,,,/CraftGate;component/Assets/" + def.IconFile,
                    OnEditVersion = m => OpenVersionPage(null, m)
                };
                module.OnEditVersionCommand = new RelayCommand(_ => OpenVersionPage(null, module));
                foreach (var md in def.Mods)
                {
                    // ComplementaryUnbound 是真正的光影包(shaderpack)，装到 shaderpacks 目录
                    bool isShader = def.Id == "shader" && md.Keyword == "ComplementaryUnbound";
                    module.Mods.Add(new WizardModItem
                    {
                        Name = md.Name,
                        SearchKeyword = md.Keyword,
                        IsShaderPack = isShader
                    });
                }
                Modules.Add(module);

                // 异步搜主 mod 拿图标 + 版本（不阻塞 UI 初始化）
                _ = Task.Run(async () => await LoadModuleInfoAsync(module, def.IconKeyword));
            }

            // 轻量级 Java：无版本选择（HasVersionSelector=false），自动匹配当前 MC 版本所需 Java 主版本，
            // 名称显示"轻量级java"，版本文字显示匹配到的主版本号（如 21）。不是 Modrinth mod，不进 checkedMods。
            var javaMajor = JavaDetector.GetRequiredJavaMajorForMc(VersionId);
            Modules.Add(new WizardModule
            {
                Id = "lightjava",
                Name = "轻量级java",
                Desc = "自动匹配本实例所需 Java，无需手动选择版本",
                IconPath = "pack://application:,,,/CraftGate;component/Assets/Java_Light.png",
                VersionText = javaMajor.ToString(),
                HasVersionSelector = false,
                OnEditVersion = null,
                OnEditVersionCommand = null
            });
        }

        private async Task LoadModuleInfoAsync(WizardModule module, string iconKeyword)
        {
            try
            {
                var hits = await ModrinthService.SearchAsync(iconKeyword, "mod", VersionId, "fabric", 0, 5, System.Threading.CancellationToken.None);
                var main = hits.FirstOrDefault();
                // 图标改用项目内置 Assets（IconPath），不再从 Modrinth 抓取

                // 主 mod 的版本列表（供版本选择子页）
                if (main != null)
                {
                    var versions = await ModrinthService.GetVersionsAsync(main.ProjectId, VersionId, "fabric");
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        foreach (var v in versions.Select(v => v.VersionNumber).Distinct().Take(40))
                            module.AvailableVersions.Add(v);
                        if (module.AvailableVersions.Count > 0)
                            module.SelectedVersion = module.AvailableVersions[0];
                    });
                }

                // 为每个子 mod 搜 project + 图标
                foreach (var mod in module.Mods.ToList())
                {
                    var mhits = await ModrinthService.SearchAsync(mod.SearchKeyword, mod.ProjectType, VersionId, mod.IsShaderPack ? null : "fabric", 0, 3, System.Threading.CancellationToken.None);
                    var proj = mhits.FirstOrDefault();
                    if (proj != null)
                    {
                        mod.ProjectId = proj.ProjectId;
                        if (!string.IsNullOrEmpty(proj.IconUrl))
                            mod.IconImage = await LoadBitmapAsync(proj.IconUrl, 32);
                    }
                }
            }
            catch { }
        }

        /// <summary>
        /// 解析模块 mod 的 required 前置依赖（如 Fabric API），取兼容当前 MC 版本 + 加载器的最新版。
        /// 返回去重后的 (projectId, 名称) 列表。
        /// </summary>
        private async Task<List<(string projectId, string name)>> ResolveDependenciesAsync(
            List<string> mainProjectIds, HashSet<string> existingIds)
        {
            var result = new List<(string projectId, string name)>();
            var added = new HashSet<string>(existingIds, StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var pid in mainProjectIds)
                {
                    // 严格按当前 MC 版本取依赖的基准版本，不回退最新版（否则依赖树也是错的版本）
                    var vers = await ModrinthService.GetVersionsAsync(pid, VersionId, "fabric");
                    var mainVer = vers.FirstOrDefault(v => v.GameVersions != null && v.GameVersions.Contains(VersionId));
                    if (mainVer == null)
                    {
                        var all = await ModrinthService.GetVersionsAsync(pid, null, null);
                        mainVer = all.FirstOrDefault(v => v.GameVersions != null && v.GameVersions.Contains(VersionId));
                    }
                    if (mainVer?.Dependencies == null) continue;

                    var required = mainVer.Dependencies
                        .Where(d => d.DependencyType == "required")
                        .Where(d => !string.IsNullOrEmpty(d.ProjectId) || !string.IsNullOrEmpty(d.VersionId))
                        .ToList();

                    foreach (var dep in required)
                    {
                        string depPid = dep.ProjectId;
                        if (string.IsNullOrEmpty(depPid) && !string.IsNullOrEmpty(dep.VersionId))
                            depPid = await ModrinthService.GetProjectIdByVersionIdAsync(dep.VersionId);
                        if (string.IsNullOrEmpty(depPid) || !added.Add(depPid)) continue;

                        string name = depPid;
                        var info = await ModrinthService.GetProjectInfoAsync(depPid);
                        if (info != null) name = info.DisplayName ?? info.Title ?? depPid;
                        result.Add((depPid, name));
                    }
                }
            }
            catch { }
            return result;
        }

        private static async Task<BitmapSource> LoadBitmapAsync(string url, int width)
        {
            try
            {
                var bytes = await _imageClient.GetByteArrayAsync(url);
                using (var ms = new MemoryStream(bytes))
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.StreamSource = ms;
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.DecodePixelWidth = width;
                    bmp.EndInit();
                    bmp.Freeze();
                    return bmp;
                }
            }
            catch { return null; }
        }

        // ===== 版本选择子页 =====
        private void OpenVersionPage(LoaderInfo loader, WizardModule module)
        {
            _versionTargetLoader = loader;
            _versionTargetModule = module;
            VersionPageItems.Clear();

            if (loader != null)
            {
                VersionPageTitle = $"选择 {loader.Name} 版本";
                foreach (var v in loader.AvailableVersions.Select(v => v.Version).Where(v => !string.IsNullOrEmpty(v)))
                    VersionPageItems.Add(v);
            }
            else if (module != null)
            {
                VersionPageTitle = $"选择 {module.Name} 版本";
                foreach (var v in module.AvailableVersions) VersionPageItems.Add(v);
            }
            IsVersionPageOpen = true;
        }

        private void OnVersionItemSelected(string version)
        {
            if (string.IsNullOrEmpty(version)) return;
            if (_versionTargetLoader != null)
            {
                foreach (var v in _versionTargetLoader.AvailableVersions) v.IsSelected = (v.Version == version);
                _versionTargetLoader.IsChecked = true;
                OnPropertyChanged(nameof(Loaders));
            }
            else if (_versionTargetModule != null)
            {
                _versionTargetModule.SelectedVersion = version;
            }
            IsVersionPageOpen = false;
        }

        private void ShowToastErr(string msg)
        {
            if (Application.Current.MainWindow is MainWindow mw) mw.ShowToastError(msg);
        }

        // ===== 创建流程 =====
        private async Task StartDownloadAsync()
        {
            // 1. 找勾选的加载器（配套 mod 不算本体）
            LoaderInfo chosen = null;
            string loaderVer = null;
            foreach (var l in Loaders)
            {
                if (!l.IsChecked || l.IsAddon) continue;
                chosen = l;
                loaderVer = l.SelectedVersionItem?.Version;
                break;
            }
            // 勾选了模块但没勾加载器 → 提示必须先装加载器
            // 轻量级java 不需要模组加载器，必须排除，否则只勾 Java 也会误报。
            bool anyModule = Modules.Any(m => m.IsChecked && m.Id != "lightjava");
            if (anyModule && chosen == null)
            {
                ShowToastErr("请先勾选一个模组加载器，再安装附带模块！");
                return;
            }

            IsDownloading = true;
            DownloadProgress = 0;
            var mainWindow = Application.Current.MainWindow as MainWindow;
            mainWindow?.ShowToast("开始创建", $"正在准备 {InstanceName}...", isDownload: true);

            // 2. 下载原版
            DownloadStatus = "正在下载原版核心...";
            bool vanillaOk = await VersionDownloader.DownloadVanillaAsync(VersionId, _versionUrl, (msg, progress) =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    // VersionDownloader 传来的 progress 已经是整体 0~100，不要二次缩放（否则未满就爆表）
                    DownloadStatus = msg; DownloadProgress = progress;
                    mainWindow?.UpdateToast((int)progress, msg);
                });
            });
            if (!vanillaOk)
            {
                IsDownloading = false;
                mainWindow?.ShowToastError("原版下载失败！");
                return;
            }

            string finalVersionName = InstanceName;
            string modsTargetVersion = InstanceName;

            // 3. 安装加载器（目前 Fabric 全自动）
            if (chosen != null && chosen.Id == "fabric" && !string.IsNullOrEmpty(loaderVer))
            {
                DownloadStatus = $"正在安装 Fabric {loaderVer}...";
                var built = await LoaderInstaller.InstallFabricAsync(VersionId, loaderVer, (msg, progress) =>
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        // progress 是 0~100，映射到整体 40~70（不要 ×30，否则爆表）
                        DownloadStatus = msg;
                        DownloadProgress = 40 + progress * 0.3;
                        mainWindow?.UpdateToast(40 + (int)(progress * 0.3), msg);
                    });
                });
                if (built != null) modsTargetVersion = built;
                else mainWindow?.ShowToastError("Fabric 安装失败，模块可能装到原版目录。");
            }
            else if (chosen != null)
            {
                DownloadStatus = $"原版下载完成。{chosen.Name} 暂不支持自动安装。";
            }

            // 4. 下载勾选的模块 mod 及其必需前置（如 fabric-api）到实例 mods 目录
            // 【修复】异步搜索可能没填 ProjectId（快照版本/加载器过滤太严），下载前现场补搜，
            // 否则光影等模块"勾了却一个都没下"。
            var allCheckedMods = Modules.Where(m => m.IsChecked).SelectMany(m => m.Mods).ToList();
            foreach (var mod in allCheckedMods.Where(m => string.IsNullOrEmpty(m.ProjectId)))
            {
                DownloadStatus = $"正在解析 {mod.Name}...";
                try
                {
                    var hits = await ModrinthService.SearchAsync(mod.SearchKeyword, mod.ProjectType, null, null, 0, 3, System.Threading.CancellationToken.None);
                    if (hits.Count > 0) mod.ProjectId = hits.First().ProjectId;
                }
                catch { }
            }
            var checkedMods = allCheckedMods.Where(m => !string.IsNullOrEmpty(m.ProjectId)).ToList();
            if (checkedMods.Count > 0)
            {
                string modsDir = Path.Combine(PathManager.GetIsolatedGameDir(modsTargetVersion), "accounts",
                    CraftGate.Services.AccountService.CurrentAccount?.Id ?? "default", "mods");
                Directory.CreateDirectory(modsDir);

                DownloadStatus = "正在分析模块依赖（如 Fabric API）...";
                mainWindow?.UpdateToast(70, DownloadStatus);

                // 光影包目录（ComplementaryUnbound 等 .zip 装这里）
                string shaderDir = Path.Combine(PathManager.GetIsolatedGameDir(modsTargetVersion), "accounts",
                    CraftGate.Services.AccountService.CurrentAccount?.Id ?? "default", "shaderpacks");
                Directory.CreateDirectory(shaderDir);

                // 构建下载清单：主 mod + required 依赖，按 projectId 去重。项 = (id, name, 是否光影包, 目标目录)
                var manifest = new List<(string projectId, string name, bool shader, string dir)>();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var mod in checkedMods.Where(m => !m.IsShaderPack))
                    if (seen.Add(mod.ProjectId)) manifest.Add((mod.ProjectId, mod.Name, false, modsDir));
                // 光影包单独加入，装到 shaderpacks（不参与 mod 依赖解析）
                foreach (var mod in checkedMods.Where(m => m.IsShaderPack))
                    if (seen.Add(mod.ProjectId)) manifest.Add((mod.ProjectId, mod.Name, true, shaderDir));

                // 解析每个 mod 当前版本的 required 依赖，取兼容 MC 版本的最新版
                var deps = await ResolveDependenciesAsync(checkedMods.Where(m => !m.IsShaderPack).Select(m => m.ProjectId).ToList(), seen);
                foreach (var d in deps)
                    if (seen.Add(d.projectId)) manifest.Add((d.projectId, d.name, false, modsDir));

                int done = 0;
                var skippedIncompatible = new List<string>();
                foreach (var item in manifest)
                {
                    DownloadStatus = $"正在下载：{item.name} ({done + 1}/{manifest.Count})";
                    try
                    {
                        // 光影包用 shader 类型查版本、不带加载器过滤；普通 mod 用 fabric
                        // 【严格版本匹配】只按当前 MC 版本精确查询，绝不回退到"不带版本过滤"拿最新版——
                        // 否则 Sodium 0.9.3+mc26.3 会被装进 20w14infinite 导致游戏启动崩溃。
                        var vers = item.shader
                            ? await ModrinthService.GetVersionsAsync(item.projectId, VersionId, null)
                            : await ModrinthService.GetVersionsAsync(item.projectId, VersionId, "fabric");
                        // 二次校验：GameVersions 必须明确包含当前版本（防止 API 参数被忽略）
                        var pick = vers.FirstOrDefault(v => v.GameVersions != null && v.GameVersions.Contains(VersionId));
                        if (pick == null)
                        {
                            // 精确查询无果时，拉全量版本再本地严格筛选一次（兼容 Modrinth 对快照版本 filter 的怪异行为）
                            var all = await ModrinthService.GetVersionsAsync(item.projectId, null, null);
                            pick = all.FirstOrDefault(v => v.GameVersions != null && v.GameVersions.Contains(VersionId));
                        }
                        if (pick == null)
                        {
                            // 找不到兼容版本：跳过，绝不拿最新版硬装
                            skippedIncompatible.Add(item.name);
                        }
                        else if (pick.Files != null)
                        {
                            var file = pick.Files.FirstOrDefault(f => f.Primary) ?? pick.Files.FirstOrDefault();
                            if (file != null)
                                await ModrinthService.DownloadFileAsync(file.Url, item.dir, file.Filename);
                        }
                    }
                    catch { }
                    done++;
                    double p = 70 + done * 30.0 / Math.Max(1, manifest.Count);
                    Application.Current.Dispatcher.Invoke(() => { DownloadProgress = p; mainWindow?.UpdateToast((int)p, DownloadStatus); });
                }

                // 有被跳过的项就明确提示用户，避免"选了却没装、启动报不兼容"的困惑
                if (skippedIncompatible.Count > 0)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        mainWindow?.ShowToast($"{skippedIncompatible.Count} 个模组已跳过（版本不兼容）",
                            string.Join("、", skippedIncompatible.Take(4)) + (skippedIncompatible.Count > 4 ? " 等" : "") + $" 不支持 {VersionId}");
                    });
                }
            }

            // 保存实例元数据（名称+图标）到实际版本目录，供启动页版本菜单读取
            try
            {
                var selectedIcon = IconOptions.FirstOrDefault(i => i.IsSelected);
                string versionDir = Path.Combine(PathManager.VersionsDir, modsTargetVersion);
                InstanceMetaService.Save(versionDir, new InstanceMeta
                {
                    Name = string.IsNullOrWhiteSpace(InstanceName) ? modsTargetVersion : InstanceName.Trim(),
                    Icon = selectedIcon?.IconFile ?? "Grass.png"
                });
            }
            catch { }

            DownloadStatus = $"完成！实例「{finalVersionName}」已就绪，请到启动页选择。";
            DownloadProgress = 100;
            mainWindow?.ShowToastSuccess($"创建完成：{finalVersionName}");
            mainWindow?.RefreshVersions();
            IsDownloading = false;
        }
    }
}
