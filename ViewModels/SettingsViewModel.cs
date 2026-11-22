using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using CraftGate.Services;

namespace CraftGate.ViewModels
{
    public class MaterialOption : ViewModelBase
    {
        public string Name { get; }
        public string Value { get; }
        public string IconCode { get; }   // 效果1悬浮图标（Segoe 图标码，每个材质不同）
        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set => Set(ref _isSelected, value);
        }
        public MaterialOption(string name, string value, string iconCode)
        {
            Name = name;
            Value = value;
            IconCode = iconCode;
        }
    }

    public class SettingsViewModel : ViewModelBase
    {
        private bool _useMirror;
        private int _downloadThreads;
        private int _maxMemoryMB;
        private bool _enableVersionIsolation;
        private string _backgroundMaterial;
        private string _backgroundImagePath;
        private string _microsoftClientId;

        // 背景材质选项
        public IReadOnlyList<MaterialOption> MaterialOptions { get; } = new[]
        {
            new MaterialOption("原始", "None",     "\uE71D"),  // 对比/原始
            new MaterialOption("云母", "Mica",     "\uE7C4"),  // 云母层
            new MaterialOption("亚克力", "Acrylic", "\uE7B3"),  // 透明层
            new MaterialOption("毛玻璃", "MicaAlt", "\uE7EF"),  // 毛玻璃
            new MaterialOption("图片", "Image",     "\uE91B"),  // 图片
        };

        public string BackgroundImagePath => _backgroundImagePath;

        public string BackgroundMaterial
        {
            get => _backgroundMaterial;
            set
            {
                if (!Set(ref _backgroundMaterial, value)) return;
                SyncMaterialSelection(value);
                ConfigManager.Settings.BackgroundMaterial = value;
                ConfigManager.Save();
                ApplyMaterialToWindow(value);
            }
        }

        public ICommand SelectImageCommand { get; }
        public ICommand SelectMaterialCommand { get; }

        // 打开账户/游戏数据文件夹（存档、模组、配置按账户+版本隔离）
        public ICommand OpenAccountFolderCommand { get; }
        public ICommand OpenSavesFolderCommand { get; }
        public ICommand OpenModsFolderCommand { get; }
        public ICommand OpenConfigFolderCommand { get; }
        public ICommand OpenGameRootFolderCommand { get; }

        public bool UseMirror
        {
            get => _useMirror;
            set { if (Set(ref _useMirror, value)) SaveSettings(); }
        }

        public int DownloadThreads
        {
            get => _downloadThreads;
            set
            {
                if (value < 1) value = 1;
                if (value > 64) value = 64;
                if (Set(ref _downloadThreads, value)) SaveSettings();
            }
        }

        public int MaxMemoryMB
        {
            get => _maxMemoryMB;
            set
            {
                if (value < 1024) value = 1024;
                if (Set(ref _maxMemoryMB, value)) SaveSettings();
            }
        }

        public bool EnableVersionIsolation
        {
            get => _enableVersionIsolation;
            set { if (Set(ref _enableVersionIsolation, value)) SaveSettings(); }
        }

        public string MicrosoftClientId
        {
            get => _microsoftClientId;
            set
            {
                if (Set(ref _microsoftClientId, value))
                {
                    ConfigManager.Settings.MicrosoftClientId = value?.Trim() ?? "";
                    ConfigManager.Save();
                }
            }
        }

        public ICommand ManageLaunchOptionsCommand { get; }
        public ICommand CheckUpdateCommand { get; }
        public ICommand ToggleUpdateMenuCommand { get; }
        public ICommand SetUpdateModeCommand { get; }

        // 当前版本文字（供设置页显示）
        public string CurrentVersionText => "当前版本 v" + UpdateService.CurrentVersion;

        // 更新状态上拉菜单
        private bool _isUpdateMenuOpen;
        public bool IsUpdateMenuOpen
        {
            get => _isUpdateMenuOpen;
            set => Set(ref _isUpdateMenuOpen, value);
        }

        // 自动更新 / 暂停更新（暂停只停自动检查，不阻止手动检查）
        public bool IsAutoUpdate
        {
            get => !ConfigManager.Settings.PauseUpdate;
            set
            {
                ConfigManager.Settings.PauseUpdate = !value;
                ConfigManager.Settings.AutoUpdate = value;
                ConfigManager.Save();
                OnPropertyChanged();
                OnPropertyChanged(nameof(UpdateStatusText));
            }
        }
        public bool IsPausedUpdate => ConfigManager.Settings.PauseUpdate;
        public string UpdateStatusText => ConfigManager.Settings.PauseUpdate ? "更新已暂停（手动检查仍可用）" : "自动更新已开启";

        // 更新源仓库（owner/repo）
        public string UpdateRepo
        {
            get => ConfigManager.Settings.UpdateRepo;
            set
            {
                ConfigManager.Settings.UpdateRepo = value;
                ConfigManager.Save();
            }
        }

        public SettingsViewModel()
        {
            var s = ConfigManager.Settings;
            _useMirror = s.UseMirror;
            _downloadThreads = s.DownloadThreads;
            _maxMemoryMB = s.MaxMemoryMB;
            _enableVersionIsolation = s.EnableVersionIsolation;
            _backgroundMaterial = string.IsNullOrEmpty(s.BackgroundMaterial) ? "None" : s.BackgroundMaterial;
            _backgroundImagePath = s.BackgroundImagePath ?? "";
            _microsoftClientId = s.MicrosoftClientId ?? "";

            ManageLaunchOptionsCommand = new RelayCommand(_ =>
            {
                if (Application.Current.MainWindow is MainWindow mainWin)
                {
                    mainWin.ShowToastSuccess("自定义启动项管理将在后续版本开放！");
                }
            });

            // 检查更新：调 GitHub Releases，结果用 Toast 反馈
            ToggleUpdateMenuCommand = new RelayCommand(_ => IsUpdateMenuOpen = !IsUpdateMenuOpen);
            SetUpdateModeCommand = new RelayCommand(param =>
            {
                // 参数 true=自动更新，false=暂停更新（XAML 传的是字符串 "True"/"False"）
                bool auto;
                if (param is bool b) auto = b;
                else if (param is string str) auto = string.Equals(str, "True", StringComparison.OrdinalIgnoreCase);
                else return;

                IsAutoUpdate = auto;
                IsUpdateMenuOpen = false;
                OnPropertyChanged(nameof(IsPausedUpdate));
                if (Application.Current.MainWindow is MainWindow mwMode)
                    mwMode.ShowToastSuccess(auto ? "已开启自动更新" : "已暂停自动更新（手动检查仍可用）");
            });

            CheckUpdateCommand = new RelayCommand(async _ =>
            {
                if (Application.Current.MainWindow is MainWindow mainWin)
                    mainWin.ShowToast("检查更新", "正在连接更新服务器...");

                var r = await UpdateService.CheckAsync();
                if (!r.Success)
                {
                    if (Application.Current.MainWindow is MainWindow mwErr)
                        mwErr.ShowToastError("检查更新失败：" + (r.Error ?? "未知错误"));
                    return;
                }
                if (r.HasUpdate)
                {
                    if (Application.Current.MainWindow is MainWindow mwNew)
                        mwNew.ShowUpdateDialog(r);
                }
                else
                {
                    if (Application.Current.MainWindow is MainWindow mwOk)
                        mwOk.ShowToastSuccess($"已是最新版本 v{UpdateService.CurrentVersion}");
                }
            });

            // 打开账户/游戏数据文件夹
            OpenAccountFolderCommand = new RelayCommand(_ => OpenFolder(GetAccountVersionDir()));
            OpenSavesFolderCommand = new RelayCommand(_ => OpenFolder(Path.Combine(GetAccountVersionDir(), "saves")));
            OpenModsFolderCommand = new RelayCommand(_ => OpenFolder(Path.Combine(GetAccountVersionDir(), "mods")));
            OpenConfigFolderCommand = new RelayCommand(_ => OpenFolder(Path.Combine(GetAccountVersionDir(), "config")));
            OpenGameRootFolderCommand = new RelayCommand(_ => OpenFolder(PathManager.GameDir));

            SelectImageCommand = new RelayCommand(_ => SelectBackgroundImage());
            SelectMaterialCommand = new RelayCommand(param =>
            {
                if (param is string value) BackgroundMaterial = value;
            });

            SyncMaterialSelection(_backgroundMaterial);
        }

        private void SyncMaterialSelection(string value)
        {
            foreach (var option in MaterialOptions)
                option.IsSelected = option.Value == value;
        }

        private void ApplyMaterialToWindow(string material)
        {
            if (Application.Current.MainWindow is MainWindow mainWin)
            {
                mainWin.ApplyBackgroundMaterial(material);
            }
        }

        /// <summary>
        /// 找当前账户"最近使用"的版本隔离目录：扫描 versions/*/accounts/{id}，取最后写入的那个。
        /// 没启用版本隔离时退回 .minecraft/accounts/{id}。
        /// </summary>
        private string GetAccountVersionDir()
        {
            string accId = CraftGate.Services.AccountService.CurrentAccount?.Id;
            if (string.IsNullOrEmpty(accId)) return PathManager.GameDir;

            string best = null;
            DateTime bestTime = DateTime.MinValue;
            try
            {
                if (Directory.Exists(PathManager.VersionsDir))
                {
                    foreach (var verDir in Directory.GetDirectories(PathManager.VersionsDir))
                    {
                        string accDir = Path.Combine(verDir, "accounts", accId);
                        if (!Directory.Exists(accDir)) continue;
                        var t = Directory.GetLastWriteTime(accDir);
                        if (t > bestTime) { bestTime = t; best = accDir; }
                    }
                }
            }
            catch { }
            return best ?? Path.Combine(PathManager.GameDir, "accounts", accId);
        }

        private void OpenFolder(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
                    Directory.CreateDirectory(path);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                if (Application.Current.MainWindow is MainWindow mw)
                    mw.ShowToastError("打开文件夹失败：" + ex.Message);
            }
        }

        private void SelectBackgroundImage()
        {
            var dialog = new OpenFileDialog
            {
                Title = "选择背景图片",
                Filter = "图片文件|*.png;*.jpg;*.jpeg;*.bmp;*.webp|所有文件|*.*",
                CheckFileExists = true
            };
            if (dialog.ShowDialog() != true) return;

            try
            {
                // 复制到配置目录，避免原图被移动/删除后失效
                string dir = Path.Combine(PathManager.ConfigDir, "Background");
                Directory.CreateDirectory(dir);
                string ext = Path.GetExtension(dialog.FileName);
                string dest = Path.Combine(dir, "background" + ext);
                File.Copy(dialog.FileName, dest, true);

                _backgroundImagePath = dest;
                ConfigManager.Settings.BackgroundImagePath = dest;
                ConfigManager.Save();
                OnPropertyChanged(nameof(BackgroundImagePath));

                // 选完图片自动切换到"图片"材质。
                // 注意：即便当前值已经是 Image，也必须强制刷新图片层（setter 在值未变时会短路），
                // 否则会出现"选了图片不生效、要切到别的效果再切回来才行"的问题。
                if (_backgroundMaterial == "Image")
                {
                    if (Application.Current.MainWindow is MainWindow currentWin)
                        currentWin.ApplyBackgroundMaterial("Image");
                }
                else
                {
                    BackgroundMaterial = "Image";
                }
            }
            catch (Exception ex)
            {
                if (Application.Current.MainWindow is MainWindow mainWin)
                    mainWin.ShowToastError("设置图片背景失败：" + ex.Message);
            }
        }

        private void SaveSettings()
        {
            ConfigManager.Settings.UseMirror = UseMirror;
            ConfigManager.Settings.DownloadThreads = DownloadThreads;
            ConfigManager.Settings.MaxMemoryMB = MaxMemoryMB;
            ConfigManager.Settings.EnableVersionIsolation = EnableVersionIsolation;
            ConfigManager.Save();
        }
    }
}