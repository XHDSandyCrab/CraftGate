using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CraftGate.Models;
using CraftGate.Services;
using CraftGate.Core.Game;

namespace CraftGate.ViewModels
{
    public class LaunchViewModel : ViewModelBase
    {
        private string _playerName = "Steve";
        private ImageSource _skinImage;
        private bool _isLaunching;
        private double _progress;
        private string _latestStatusText = "准备就绪";
        private string _selectedVersion = "无版本";
        private bool _isVersionPopupOpen;
        private LaunchOption _selectedLaunchOption;
        private double _displayProgress;
        private string _displayProgressText = "0.0%";
        private readonly DispatcherTimer _progressTickTimer;
        private readonly Random _progressRandom = new Random();

        public string PlayerName { get => _playerName; set => Set(ref _playerName, value); }
        public ImageSource SkinImage { get => _skinImage; set => Set(ref _skinImage, value); }
        public ImageSource CapeImage { get => _capeImage; set => Set(ref _capeImage, value); }
        private ImageSource _capeImage;
        public bool IsLaunching { get => _isLaunching; set => Set(ref _isLaunching, value); }
        public double Progress
        {
            get => _progress;
            set
            {
                if (!Set(ref _progress, value)) return;
                if (value <= 0)
                {
                    // 进度归零时，显示值也立即归零
                    _displayProgress = 0;
                    DisplayProgressText = "0.0%";
                    _progressTickTimer.Stop();
                }
                else
                {
                    // 进度推进：启动显示定时器，让文字按 0.1 逐步逼近
                    _progressTickTimer.Interval = TimeSpan.FromMilliseconds(20);
                    if (!_progressTickTimer.IsEnabled) _progressTickTimer.Start();
                }
            }
        }

        // 进度条下方显示的进度文字：每次只 +0.1，精确到一位小数
        public string DisplayProgressText
        {
            get => _displayProgressText;
            set => Set(ref _displayProgressText, value);
        }

        // 启动进度旁 GIF 动画路径：优先用配置（用户自制），否则用内置 Assets/loading.gif
        public string LaunchGifPath
        {
            get
            {
                string configured = ConfigManager.Settings.LaunchGifPath;
                if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
                    return configured;

                string builtin = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "loading.gif");
                return File.Exists(builtin) ? builtin : "";
            }
        }

        public string LatestStatusText { get => _latestStatusText; set => Set(ref _latestStatusText, value); }
        public bool IsVersionPopupOpen { get => _isVersionPopupOpen; set => Set(ref _isVersionPopupOpen, value); }

        public string SelectedVersion
        {
            get => _selectedVersion;
            set
            {
                if (Set(ref _selectedVersion, value))
                {
                    _ = RefreshAccountAsync();
                }
            }
        }

        public LaunchOption SelectedLaunchOption
        {
            get => _selectedLaunchOption;
            set => Set(ref _selectedLaunchOption, value);
        }

        public ObservableCollection<string> AvailableVersions { get; } = new ObservableCollection<string>();
        // 版本下拉菜单显示项（带实例图标+名称，与创建向导统一）
        public ObservableCollection<VersionDisplayItem> VersionItems { get; } = new ObservableCollection<VersionDisplayItem>();
        public ObservableCollection<LaunchOption> LaunchOptions { get; } = new ObservableCollection<LaunchOption>();
        public ObservableCollection<string> Logs { get; } = new ObservableCollection<string>();

        public ICommand LaunchCommand { get; }
        public ICommand SelectVersionCommand { get; }
        public ICommand SelectLaunchOptionCommand { get; }

        public LaunchViewModel()
        {
            // 进度显示定时器：每次推进 0.1%~0.5% 的随机步长，差值大时步长大、频率高，
            // 接近目标时步长与频率都收紧，避免冲过头，整体显得自然不死板。
            _progressTickTimer = new DispatcherTimer();
            _progressTickTimer.Tick += (s, e) =>
            {
                double diff = _progress - _displayProgress;
                if (diff <= 0.0001)
                {
                    _displayProgress = _progress;
                    DisplayProgressText = $"{_displayProgress:F1}%";
                    _progressTickTimer.Stop();
                    return;
                }

                // 差值大 → 步长 0.1~0.5 随机；接近目标(<=0.5) → 收敛到 0.1~0.2
                double minStep = 0.1;
                double maxStep = diff > 2 ? 0.5 : (diff > 0.5 ? 0.3 : 0.2);
                double raw = minStep + _progressRandom.NextDouble() * (maxStep - minStep);
                double step = Math.Round(raw, 1); // 保留一位小数
                if (step < 0.1) step = 0.1;

                _displayProgress = Math.Min(_progress, _displayProgress + step);
                DisplayProgressText = $"{_displayProgress:F1}%";

                // 剩余差距大 → 高频(8ms)，接近目标 → 低频(45ms)
                double remain = _progress - _displayProgress;
                _progressTickTimer.Interval = TimeSpan.FromMilliseconds(remain > 3 ? 8 : (remain > 0.5 ? 20 : 45));
            };

            LaunchOptions.Add(new LaunchOption { Id = "normal", Name = "正常启动", ButtonText = "启动游戏", IsSelected = true });
            LaunchOptions.Add(new LaunchOption { Id = "safe", Name = "安全模式", ButtonText = "安全启动 (无Mod)" });
            LaunchOptions.Add(new LaunchOption { Id = "pure", Name = "纯净启动", ButtonText = "纯净启动" });
            LaunchOptions.Add(new LaunchOption { Id = "backup", Name = "备份启动", ButtonText = "备份并启动" });
            LaunchOptions.Add(new LaunchOption { Id = "new_survival", Name = "新建生存", ButtonText = "新建生存游戏", IsNewWorld = true });

            SelectedLaunchOption = LaunchOptions[0];

            LaunchCommand = new RelayCommand(_ => { if (!IsLaunching) _ = LaunchAsync(); });

            SelectVersionCommand = new RelayCommand(version =>
            {
                string v = version is VersionDisplayItem item ? item.Version : version as string;
                if (v != null)
                {
                    SelectedVersion = v;
                    IsVersionPopupOpen = false;
                    AddLog($"[版本] 已切换至 {v}");
                }
            });

            SelectLaunchOptionCommand = new RelayCommand(option =>
            {
                if (option is LaunchOption opt && opt != SelectedLaunchOption)
                {
                    foreach (var o in LaunchOptions) o.IsSelected = false;
                    opt.IsSelected = true;
                    SelectedLaunchOption = opt;
                    AddLog($"[启动项] 已切换至 {opt.Name}");
                }
            });
        }

        public async Task InitializeAsync()
        {
            AddLog("[CraftGate] 启动器已就绪");
            ScanVersions();
            await RefreshAccountAsync();
        }

        public async Task RefreshAccountAsync()
        {
            PlayerName = AccountService.CurrentAccount.Username;
            string skinPath = AccountService.CurrentAccount.SkinPath;

            AddLog($"[皮肤] 尝试刷新，读取路径: {skinPath}");

            try
            {
                SkinImage = null;
                var img = await SkinService.GetSkinAsync(PlayerName, skinPath, SelectedVersion);
                SkinImage = img;
                // 加载当前账户披风
                string capePath = AccountService.CurrentAccount.CapePath;
                CapeImage = null;
                if (!string.IsNullOrEmpty(capePath) && File.Exists(capePath))
                {
                    using (var fs = new FileStream(capePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                        var dec = BitmapDecoder.Create(fs, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                        var cf = new FormatConvertedBitmap(dec.Frames[0], PixelFormats.Bgra32, null, 0);
                        cf.Freeze();
                        CapeImage = cf;
                    }
                }
            }
            catch (Exception ex)
            {
                AddLog($"[皮肤] 严重错误: {ex.Message}");
                SkinImage = await SkinService.LoadDefaultAsync();
            }
        }

        public void ScanVersions()
        {
            AvailableVersions.Clear();
            VersionItems.Clear();
            if (Directory.Exists(PathManager.VersionsDir))
            {
                foreach (var dir in Directory.GetDirectories(PathManager.VersionsDir))
                {
                    string v = Path.GetFileName(dir);
                    if (!File.Exists(Path.Combine(dir, $"{v}.json"))) continue;
                    AvailableVersions.Add(v);

                    // 读取实例元数据（名称+图标），没有则用默认值
                    var meta = InstanceMetaService.Load(dir);
                    VersionItems.Add(new VersionDisplayItem
                    {
                        Version = v,
                        DisplayName = !string.IsNullOrWhiteSpace(meta?.Name) ? meta.Name : v,
                        IconImage = InstanceMetaService.LoadIcon(meta?.Icon ?? "Grass.png")
                    });
                }
            }
            if (AvailableVersions.Count > 0)
            {
                if (!AvailableVersions.Contains(SelectedVersion)) SelectedVersion = AvailableVersions[0];
                AddLog($"[版本] 已扫描到本地版本: {string.Join(", ", AvailableVersions)}");
            }
            else
            {
                SelectedVersion = "无可用版本";
                AddLog("[版本] 未找到版本，请先下载。");
            }
        }

        private bool SupportsQuickPlay(string version)
        {
            try
            {
                var match = Regex.Match(version, @"\d+(\.\d+)+");
                if (match.Success)
                {
                    var ver = new Version(match.Value);
                    if (ver.Major > 1) return true;
                    if (ver.Major == 1 && ver.Minor >= 20) return true;
                }
            }
            catch { }
            return false;
        }

        private async Task LaunchAsync()
        {
            if (SelectedVersion == "无可用版本" || string.IsNullOrEmpty(SelectedVersion))
            {
                AddLog("[错误] 请先选择游戏版本！");
                LatestStatusText = "启动失败：未选择版本";
                await Task.Delay(2000);
                return;
            }

            IsLaunching = true;
            IsVersionPopupOpen = false;
            Progress = 0;
            Logs.Clear();
            _logIndex = 0;
            AddLog("========== 启动流程开始 ==========");

            string gameDir = PathManager.GetCurrentAccountGameDir(SelectedVersion);
            string savesDir = Path.Combine(gameDir, "saves");
            Directory.CreateDirectory(savesDir);

            string additionalArgs = SelectedLaunchOption.AdditionalArgs ?? "";
            bool disableMods = SelectedLaunchOption.Id == "safe" || SelectedLaunchOption.Id == "pure";

            if (SelectedLaunchOption.IsNewWorld)
            {
                string newWorldName = $"NewWorld_{DateTime.Now:yyyyMMdd_HHmmss}";
                AddLog($"[世界] 正在通过模板生成全新生存世界: {newWorldName}...");

                string result = WorldTemplateService.CreateQuickPlayWorld(
                    version: SelectedVersion,
                    savesDir: savesDir,
                    worldName: newWorldName,
                    userSeed: "",
                    gameType: 0,
                    hardcore: false,
                    generator: "minecraft:noise"
                );

                if (result != null)
                {
                    if (SupportsQuickPlay(SelectedVersion))
                    {
                        additionalArgs = $"--quickPlaySingleplayer \"{newWorldName}\"";
                        AddLog("[世界] 世界生成完毕，已注入快速启动参数！");
                    }
                    else
                    {
                        AddLog("[世界] 世界生成完毕。当前版本不支持原生快速直达，将正常进入主菜单。");
                    }
                }
            }

            LatestStatusText = "检测 Java 环境中...";
            await AnimateProgressToAsync(15);

            // 这里的 javaPath 只是作为兜底，因为 GameLauncher 内部会优先使用 JavaManager 自动匹配的
            string javaPath = JavaDetector.DetectJava();
            if (string.IsNullOrEmpty(javaPath))
            {
                AddLog("[提示] 未检测到系统 Java 环境，启动器将尝试为您自动下载内置 JRE...");
            }

            LatestStatusText = "校验核心文件中...";
            await AnimateProgressToAsync(40);
            LatestStatusText = "拼装参数并拉起游戏...";
            await AnimateProgressToAsync(70);

            if (SelectedLaunchOption.Id == "backup")
            {
                AddLog("[备份] 正在备份存档...");
                if (Directory.Exists(savesDir))
                {
                    string backupDir = Path.Combine(PathManager.DataDir, "Backups", $"{SelectedVersion}_{DateTime.Now:yyyyMMdd_HHmmss}");
                    Directory.CreateDirectory(backupDir);
                    foreach (var file in Directory.GetFiles(savesDir, "*.*", SearchOption.AllDirectories))
                    {
                        string dest = Path.Combine(backupDir, file.Substring(savesDir.Length + 1));
                        Directory.CreateDirectory(Path.GetDirectoryName(dest));
                        File.Copy(file, dest, true);
                    }
                    AddLog($"[备份] 存档已备份至 {backupDir}");
                }
            }

            await RefreshAccountAsync();

            // 兜底：旧账户名可能含空格等非法字符，传给游戏会被服务器踢（连接中断）
            string launchName = AccountService.SanitizeUsername(PlayerName);
            if (launchName != PlayerName)
            {
                PlayerName = launchName;
                AddLog($"[修正] 玩家名含非法字符，启动名已改为 {launchName}");
            }

            bool isStarted = await GameLauncher.StartAsync(
                version: SelectedVersion,
                javaPath: javaPath,
                playerName: launchName,
                additionalArgs: additionalArgs,
                disableMods: disableMods,
                logCallback: (msg) => AddLog(msg),
                onGameExit: () =>
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        LatestStatusText = "游戏已关闭";
                        AddLog("[信息] 游戏已关闭。");
                        IsLaunching = false;
                        Progress = 0;
                        Task.Delay(2000).ContinueWith(_ =>
                        {
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                if (!IsLaunching) LatestStatusText = "准备就绪";
                            });
                        });
                    });
                }
            );

            if (isStarted)
            {
                await AnimateProgressToAsync(100);
                LatestStatusText = "游戏运行中";
                AddLog("[信息] 游戏正在后台运行。");
            }
            else
            {
                LatestStatusText = "启动失败：请检查日志";
                await Task.Delay(2000);
                ResetState();
            }
        }

        private async Task AnimateProgressToAsync(double target)
        {
            while (Progress < target - 0.1)
            {
                await Task.Delay(30);
                Progress = Math.Min(target, Progress + Math.Max(0.5, (target - Progress) * 0.2));
            }
            Progress = target;
        }

        private void ResetState()
        {
            Progress = 0;
            LatestStatusText = "准备就绪";
            IsLaunching = false;
            AddLog("========== 启动流程结束 ==========");
        }

        private int _logIndex = 0;
        private void AddLog(string text)
        {
            // 每条日志开头用递增数字序号（不清零，Logs.Clear 时也重置序号）
            string logLine = $"[{DateTime.Now:HH:mm:ss}] {++_logIndex}. {text}";
            if (Application.Current.Dispatcher.CheckAccess()) Logs.Add(logLine);
            else Application.Current.Dispatcher.Invoke(() => Logs.Add(logLine));
        }
    }
}