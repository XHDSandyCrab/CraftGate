using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Input;
using CraftGate.Models;
using CraftGate.Services;

namespace CraftGate.ViewModels
{
    public class AccountViewModel : ViewModelBase
    {
        // 编辑页由 MainWindow 切换（独立页面，像设置页那样）
        public event Action EditPageRequested;
        public event Action EditPageClosed;

        // 内置公共 Client ID（用户在设置里填了自己的则用用户的）
        private const string PublicClientId = "00000000402b5328";

        private string _playerName;
        private ImageSource _skinImage;
        private string _currentSkinPath;

        public string PlayerName { get => _playerName; set => Set(ref _playerName, value); }
        public ImageSource SkinImage { get => _skinImage; set => Set(ref _skinImage, value); }
        public ICommand SaveCommand { get; }

        // ===== 账户列表 / 切换 =====
        public ObservableCollection<OfflineAccount> Accounts { get; } = new ObservableCollection<OfflineAccount>();

        private OfflineAccount _selectedAccount;
        public OfflineAccount SelectedAccount
        {
            get => _selectedAccount;
            set
            {
                if (Set(ref _selectedAccount, value) && value != null && !_suppressSwitch)
                    SwitchTo(value);
            }
        }

        private bool _suppressSwitch; // 刷新列表时避免触发切换
        public bool HasAccount => Accounts.Count > 0;
        public string AccountTypeText => AccountService.CurrentAccount.IsMicrosoft ? "微软正版账户" : "离线账户";

        private bool _isBusy;
        public bool IsBusy { get => _isBusy; set => Set(ref _isBusy, value); }

        // ===== 创建账户选择窗口 =====
        private bool _isCreateWindowVisible;
        public bool IsCreateWindowVisible { get => _isCreateWindowVisible; set { if (Set(ref _isCreateWindowVisible, value)) { OnPropertyChanged(nameof(HasAccount)); OnPropertyChanged(nameof(IsAnyModalOpen)); } } }

        // 离线账户创建输入
        private string _newOfflineName = "Steve";
        public string NewOfflineName { get => _newOfflineName; set => Set(ref _newOfflineName, value); }

        public ICommand OpenCreateWindowCommand { get; }
        public ICommand CloseCreateWindowCommand { get; }
        public ICommand CreateOfflineCommand { get; }
        public ICommand StartMicrosoftCommand { get; }
        public ICommand DeleteAccountCommand { get; }

        // ===== 编辑账户弹窗 =====
        private bool _isEditWindowVisible;
        public bool IsEditWindowVisible { get => _isEditWindowVisible; set { if (Set(ref _isEditWindowVisible, value)) OnPropertyChanged(nameof(IsAnyModalOpen)); } }
        private string _editName = "";
        public string EditName { get => _editName; set => Set(ref _editName, value); }
        public ICommand OpenEditCommand { get; }
        public ICommand CloseEditCommand { get; }
        public ICommand SaveEditCommand { get; }
        public ICommand SelectAccountCommand { get; }
        public ICommand ConvertToOfflineCommand { get; }
        public ICommand ConvertToMicrosoftCommand { get; }
        // 带滑动动画的类型切换：先滑滑块，再登录/切换
        public ICommand RequestSwitchToMicrosoftCommand { get; }
        public ICommand RequestSwitchToOfflineCommand { get; }
        public ICommand LoginNowCommand { get; }

        // 大头（从当前皮肤 UV 裁脸部）
        private ImageSource _editHeadImage;
        public ImageSource EditHeadImage { get => _editHeadImage; set => Set(ref _editHeadImage, value); }

        // 当前账户披风纹理（穿在 3D 人像身上）
        private ImageSource _currentCapeImage;
        public ImageSource CurrentCapeImage { get => _currentCapeImage; set => Set(ref _currentCapeImage, value); }

        // 账户类型滑块：true=正版（左），false=离线（右）
        // SliderIsMicrosoft 是滑块的动画目标位，切换时先改它让 thumb 滑过去，再执行登录
        private bool _sliderIsMicrosoft;
        public bool SliderIsMicrosoft
        {
            get => _sliderIsMicrosoft;
            set => Set(ref _sliderIsMicrosoft, value);
        }
        public bool IsMicrosoftMode
        {
            get => AccountService.CurrentAccount.IsMicrosoft;
            set => OnPropertyChanged(nameof(IsMicrosoftMode));
        }

        // 是否有任意弹窗打开（控制主界面背景模糊）
        // 删除确认弹窗：背景平滑模糊
        private bool _blurBackground;
        public bool BlurBackground { get => _blurBackground; set => Set(ref _blurBackground, value); }

        // 皮肤上传后的命名弹窗
        private bool _isSkinNameModalOpen;
        public bool IsSkinNameModalOpen { get => _isSkinNameModalOpen; set => Set(ref _isSkinNameModalOpen, value); }
        private string _newSkinName = "我的皮肤";
        public string NewSkinName { get => _newSkinName; set => Set(ref _newSkinName, value); }
        public ICommand ConfirmSkinNameCommand { get; }
        public ICommand CancelSkinNameCommand { get; }
        private string _pendingSkinPath;

        public bool IsAnyModalOpen => IsEditWindowVisible || IsCreateWindowVisible || IsDeviceLoginVisible || IsNoticeVisible || IsLoadingSkinModal;
        private bool _isLoadingSkinModal = false;
        public bool IsLoadingSkinModal { get => _isLoadingSkinModal; set { if (Set(ref _isLoadingSkinModal, value)) OnPropertyChanged(nameof(IsAnyModalOpen)); } }

        // ===== 应用内提示弹窗（替代系统 MessageBox）=====
        private bool _isNoticeVisible;
        public bool IsNoticeVisible
        {
            get => _isNoticeVisible;
            set { if (Set(ref _isNoticeVisible, value)) OnPropertyChanged(nameof(IsAnyModalOpen)); }
        }
        private string _noticeTitle = "提示";
        public string NoticeTitle { get => _noticeTitle; set => Set(ref _noticeTitle, value); }
        private string _noticeMessage = "";
        public string NoticeMessage { get => _noticeMessage; set => Set(ref _noticeMessage, value); }
        private bool _isNoticeConfirm;
        public bool IsNoticeConfirm { get => _isNoticeConfirm; set => Set(ref _isNoticeConfirm, value); }
        public ICommand CloseNoticeCommand { get; }
        public ICommand ConfirmNoticeCommand { get; }
        private Action _noticeConfirmAction;

        // 智能按钮文字：离线显示"改用正版"，正版显示"改用离线"
        public string ConvertTypeText => AccountService.CurrentAccount.IsMicrosoft ? "改用离线" : "改用正版";
        public bool IsCurrentlyMicrosoft => AccountService.CurrentAccount.IsMicrosoft;

        // 滑到正版后：等待用户登录（PCL2 风格，只留登录按钮，不自动登录）
        private bool _needMicrosoftLogin;
        public bool NeedMicrosoftLogin
        {
            get => _needMicrosoftLogin;
            set => Set(ref _needMicrosoftLogin, value);
        }

        // 皮肤库 / 披风库
        public ObservableCollection<SkinEntry> SkinLibrary { get; } = new ObservableCollection<SkinEntry>();
        // 两栏：内置皮肤（本地）/ 皮肤网站（云端）
        public ObservableCollection<SkinEntry> BuiltinSkins { get; } = new ObservableCollection<SkinEntry>();
        public ObservableCollection<SkinEntry> OnlineSkins { get; } = new ObservableCollection<SkinEntry>();
        // 内置皮肤分页：每页两排（12个）
        public ObservableCollection<SkinEntry> BuiltinPageSkins { get; } = new ObservableCollection<SkinEntry>();
        public const int BuiltinPageSize = 12;
        private int _builtinPageIndex = 0;
        public int BuiltinPageIndex { get => _builtinPageIndex; set { if (Set(ref _builtinPageIndex, value)) UpdateBuiltinPage(); } }
        public int TotalBuiltinPages => BuiltinSkins.Count == 0 ? 1 : (int)Math.Ceiling(BuiltinSkins.Count / (double)BuiltinPageSize);
        public string BuiltinSkinCountText => $"共 {Math.Max(0, BuiltinSkins.Count - 1)} 款";
        public ObservableCollection<PageItem> BuiltinPageNumbers { get; } = new ObservableCollection<PageItem>();
        public ICommand GoBuiltinPageCommand { get; }
        // 皮肤网站分页：每页两排（12个）
        public ObservableCollection<SkinEntry> OnlinePageSkins { get; } = new ObservableCollection<SkinEntry>();
        public const int OnlinePageSize = 12;
        private int _onlinePageIndex = 0;
        public int OnlinePageIndex { get => _onlinePageIndex; set { if (Set(ref _onlinePageIndex, value)) UpdateOnlinePage(); } }
        public int TotalOnlinePages => OnlineSkins.Count == 0 ? 1 : (int)Math.Ceiling(OnlineSkins.Count / (double)OnlinePageSize);
        public string OnlineSkinCountText => $"共 {OnlineSkins.Count} 款";
        public ObservableCollection<PageItem> OnlinePageNumbers { get; } = new ObservableCollection<PageItem>();
        public ICommand GoOnlinePageCommand { get; }
        private bool _isBuiltinTab = true;
        public bool IsBuiltinTab
        {
            get => _isBuiltinTab;
            set { if (Set(ref _isBuiltinTab, value)) OnPropertyChanged(nameof(IsOnlineTab)); }
        }
        public bool IsOnlineTab => !_isBuiltinTab;
        public ICommand ShowBuiltinTabCommand { get; }
        public ICommand ShowOnlineTabCommand { get; }
        public ObservableCollection<CapeEntry> CapeLibrary { get; } = new ObservableCollection<CapeEntry>();

        // 披风库两栏：内置 / 披风网站（与皮肤区一致）
        private bool _isBuiltinCapeTab = true;
        public bool IsBuiltinCapeTab
        {
            get => _isBuiltinCapeTab;
            set { if (Set(ref _isBuiltinCapeTab, value)) OnPropertyChanged(nameof(IsOnlineCapeTab)); }
        }
        public bool IsOnlineCapeTab => !_isBuiltinCapeTab;
        public ObservableCollection<CapeEntry> BuiltinCapes { get; } = new ObservableCollection<CapeEntry>();
        public ObservableCollection<CapeEntry> OnlineCapes { get; } = new ObservableCollection<CapeEntry>();
        public ICommand ShowBuiltinCapeTabCommand { get; }
        public ICommand ShowOnlineCapeTabCommand { get; }

        // 披风分页（每页 12 个，两排）
        public const int CapePageSize = 12;
        public ObservableCollection<CapeEntry> BuiltinCapePage { get; } = new ObservableCollection<CapeEntry>();
        public ObservableCollection<CapeEntry> OnlineCapePage { get; } = new ObservableCollection<CapeEntry>();
        public ObservableCollection<PageItem> BuiltinCapePageNumbers { get; } = new ObservableCollection<PageItem>();
        public ObservableCollection<PageItem> OnlineCapePageNumbers { get; } = new ObservableCollection<PageItem>();
        private int _builtinCapePageIndex = 0;
        private int _onlineCapePageIndex = 0;
        // 懒加载：skinmc 已加载的批次数（每批48张=4个显示页），翻到最后一批时自动加载下一批
        private int _loadedCapeBatches = 0;
        private bool _isLoadingMoreCapes;
        public bool IsLoadingMoreCapes { get => _isLoadingMoreCapes; set => Set(ref _isLoadingMoreCapes, value); }
        public int TotalBuiltinCapePages => BuiltinCapes.Count == 0 ? 1 : (int)Math.Ceiling(BuiltinCapes.Count / (double)CapePageSize);
        public int TotalOnlineCapePages => OnlineCapes.Count == 0 ? 1 : (int)Math.Ceiling(OnlineCapes.Count / (double)CapePageSize);
        public ICommand GoBuiltinCapePageCommand { get; }
        public ICommand GoOnlineCapePageCommand { get; }
        // 披风网站搜索
        private string _capeQuery = "";
        public string CapeQuery { get => _capeQuery; set => Set(ref _capeQuery, value); }
        public ICommand SearchCapeCommand { get; }
        // 皮肤站搜索
        private string _skinQuery = "";
        public string SkinQuery { get => _skinQuery; set => Set(ref _skinQuery, value); }
        public ICommand SearchSkinCommand { get; }
        public ICommand RefreshSkinCommand { get; }
        public ICommand LoadMoreSkinCommand { get; }
        public ICommand PrevSkinCommand { get; }
        public ICommand NextSkinCommand { get; }
        private int _skinOffset = 0;
        // 当前选中皮肤名（皮肤区上方显示）
        public string SelectedSkinName => SelectedSkin?.Name ?? "未选择";
        private SkinEntry _selectedSkin;
        public SkinEntry SelectedSkin
        {
            get => _selectedSkin;
            set
            {
                if (Set(ref _selectedSkin, value) && value != null && !value.IsUpload)
                {
                    OnPropertyChanged(nameof(SelectedSkinName));
                    // 变更即保存：立即应用到账户并刷新预览/大头
                    ApplySkin(value);
                    // 披风卡的角色也要跟着换成当前选中的皮肤
                    if (CapeLibrary.Count > 0) RefreshCapeModelSkin();
                }
            }
        }

        // 只高亮选中，不应用皮肤（用于打开编辑页时默认选第一个）
        private void SelectSkinSilently(SkinEntry skin)
        {
            foreach (var item in SkinLibrary) item.IsSelected = (item == skin);
            _selectedSkin = skin;
            OnPropertyChanged(nameof(SelectedSkin));
            OnPropertyChanged(nameof(SelectedSkinName));
        }

        // 皮肤区左右箭头：切换选中的皮肤（循环）
        private void StepSkin(int step)
        {
            if (SkinLibrary.Count == 0) return;
            int cur = SelectedSkin != null ? SkinLibrary.IndexOf(SelectedSkin) : -1;
            int next = (cur + step + SkinLibrary.Count) % SkinLibrary.Count;
            var target = SkinLibrary[next];
            foreach (var item in SkinLibrary) item.IsSelected = (item == target);
            SelectedSkin = target;
        }
        private CapeEntry _selectedCape;
        public CapeEntry SelectedCape
        {
            get => _selectedCape;
            set { if (Set(ref _selectedCape, value) && value != null) ApplyCape(value); }
        }
        public ICommand PickSkinCommand { get; }
        public ICommand PickCapeCommand { get; }

        // ===== 微软设备码登录 =====
        public ICommand MicrosoftLoginCommand { get; }
        private string _deviceCode;
        public string DeviceCode { get => _deviceCode; set => Set(ref _deviceCode, value); }
        private string _verifyUri;
        public string VerifyUri { get => _verifyUri; set => Set(ref _verifyUri, value); }
        private string _loginStatus;
        public string LoginStatus { get => _loginStatus; set => Set(ref _loginStatus, value); }
        private bool _isDeviceLoginVisible;
        public bool IsDeviceLoginVisible { get => _isDeviceLoginVisible; set { if (Set(ref _isDeviceLoginVisible, value)) OnPropertyChanged(nameof(IsAnyModalOpen)); } }
        public ICommand CopyCodeCommand { get; }
        public ICommand CancelLoginCommand { get; }

        private System.Threading.CancellationTokenSource _loginCts;

        public AccountViewModel()
        {
            SaveCommand = new RelayCommand(_ => SaveAccount());

            OpenCreateWindowCommand = new RelayCommand(_ =>
            {
                NewOfflineName = "Steve";
                LoginStatus = "";
                IsCreateWindowVisible = true;
            });
            CloseCreateWindowCommand = new RelayCommand(_ =>
            {
                _loginCts?.Cancel();
                IsCreateWindowVisible = false;
                IsDeviceLoginVisible = false;
                IsBusy = false;
            });
            CreateOfflineCommand = new RelayCommand(_ => CreateOfflineAccount());
            StartMicrosoftCommand = new RelayCommand(async _ => await StartMicrosoftLogin());
            DeleteAccountCommand = new RelayCommand(param => DeleteAccount(param as string));

            OpenEditCommand = new RelayCommand(param => OpenEdit(param as string));
            CloseEditCommand = new RelayCommand(_ =>
            {
                // 返回时保存名称（输入过程中不打断）
                if (!string.IsNullOrWhiteSpace(EditName)) RenameCurrentAccount(EditName);
                IsEditWindowVisible = false;
                EditPageClosed?.Invoke();
            });
            SelectAccountCommand = new RelayCommand(p => { if (p is string id) SwitchAccountById(id); });
            SaveEditCommand = new RelayCommand(_ => SaveEdit());
            ConvertToOfflineCommand = new RelayCommand(_ => ConvertCurrentToOffline());
            ConvertToMicrosoftCommand = new RelayCommand(async _ => await ConvertCurrentToMicrosoft());
            RequestSwitchToMicrosoftCommand = new RelayCommand(async _ => await AnimatedSwitchAsync(true));
            RequestSwitchToOfflineCommand = new RelayCommand(async _ => await AnimatedSwitchAsync(false));
            LoginNowCommand = new RelayCommand(async _ =>
            {
                EditPageClosed?.Invoke();  // 先切回账户页，再由账户页弹设备码
                await ConvertCurrentToMicrosoft();
            });
            ShowBuiltinTabCommand = new RelayCommand(_ => IsBuiltinTab = true);
            ShowOnlineTabCommand = new RelayCommand(_ => IsBuiltinTab = false);
            ShowBuiltinCapeTabCommand = new RelayCommand(_ => IsBuiltinCapeTab = true);
            ShowOnlineCapeTabCommand = new RelayCommand(_ => IsBuiltinCapeTab = false);
            SearchCapeCommand = new RelayCommand(async _ => await SearchCapesAsync());
            GoBuiltinCapePageCommand = new RelayCommand(p =>
            {
                if (p is PageItem page && page.Number >= 0 && page.Number < TotalBuiltinCapePages)
                    _builtinCapePageIndex = page.Number;
                UpdateCapePages();
            });
            GoOnlineCapePageCommand = new RelayCommand(async p =>
            {
                if (p is PageItem page && page.Number >= 0 && page.Number < TotalOnlineCapePages)
                {
                    _onlineCapePageIndex = page.Number;
                    // 翻到已加载的最后一页 → 自动加载下一批（每批12张=1页）
                    if (_onlineCapePageIndex >= _loadedCapeBatches - 1)
                        await LoadMoreCapesAsync();
                }
                UpdateCapePages();
            });
            ConfirmSkinNameCommand = new RelayCommand(_ => ConfirmSkinName());
            CancelSkinNameCommand = new RelayCommand(_ => { IsSkinNameModalOpen = false; _pendingSkinPath = null; });
            GoBuiltinPageCommand = new RelayCommand(p =>
            {
                int page = p is PageItem pi ? pi.Number : (p is int i ? i : 0);
                if (page >= 0 && page < TotalBuiltinPages) BuiltinPageIndex = page;
            });
            GoOnlinePageCommand = new RelayCommand(async p =>
            {
                int page = p is PageItem pi ? pi.Number : (p is int i ? i : 0);
                if (page >= 0 && page < TotalOnlinePages) OnlinePageIndex = page;
                // 翻到最后一页 → 自动加载 skinmc 下一页（随取随用，像披风）
                if (page >= TotalOnlinePages - 1)
                    await LoadMoreOnlineSkinsAsync();
            });
            SearchSkinCommand = new RelayCommand(async _ => await SearchSkinsAsync(reset: true));
            RefreshSkinCommand = new RelayCommand(async _ => await SearchSkinsAsync(reset: true));
            LoadMoreSkinCommand = new RelayCommand(async _ => await SearchSkinsAsync(reset: false));
            PrevSkinCommand = new RelayCommand(_ => StepSkin(-1));
            NextSkinCommand = new RelayCommand(_ => StepSkin(1));
            PickSkinCommand = new RelayCommand(param =>
            {
                if (param is SkinEntry s)
                {
                    if (s.Name == "__upload__")  // 本地上传
                    {
                        var ofd = new Microsoft.Win32.OpenFileDialog
                        {
                            Filter = "皮肤图片 (*.png)|*.png|所有文件 (*.*)|*.*",
                            Title = "选择 Minecraft 皮肤"
                        };
                        if (ofd.ShowDialog() == true) SetSkin(ofd.FileName);
                        return;
                    }
                    foreach (var item in SkinLibrary) item.IsSelected = (item == s);
                    SelectedSkin = s;
                }
            });
            PickCapeCommand = new RelayCommand(param =>
            {
                if (param is CapeEntry c)
                {
                    foreach (var item in CapeLibrary) item.IsSelected = (item == c);
                    SelectedCape = c;
                }
            });
            CloseNoticeCommand = new RelayCommand(_ => CloseNotice());
            ConfirmNoticeCommand = new RelayCommand(_ => { var a = _noticeConfirmAction; CloseNotice(); a?.Invoke(); });

            MicrosoftLoginCommand = new RelayCommand(async _ => await MicrosoftLoginAsync());
            CopyCodeCommand = new RelayCommand(_ => { try { Clipboard.SetText(DeviceCode ?? ""); } catch { } });
            CancelLoginCommand = new RelayCommand(_ =>
            {
                _loginCts?.Cancel();
                IsDeviceLoginVisible = false;
                IsBusy = false;
            });
        }

        // ===== 初始化 =====
        public async Task InitializeAsync()
        {
            var acc = AccountService.CurrentAccount;
            if (acc.IsMicrosoft)
            {
                var session = MicrosoftLoginService.TryRestore();
                if (session != null)
                {
                    acc.AccessToken = session.AccessToken;
                    acc.Uuid = session.Uuid;
                    acc.Username = session.Username;
                    AccountService.Save();
                }
            }
            RefreshAccountList();
            PlayerName = acc.Username;
            _currentSkinPath = acc.SkinPath;
            await LoadSkinAsync();
        }

        private void RefreshAccountList()
        {
            _suppressSwitch = true;
            Accounts.Clear();
            foreach (var a in AccountService.Accounts)
            {
                // 为每个账户加载扁平大头（UV 裁脸）
                var head = SkinHeadService.GetHead(a.SkinPath, 6)
                           ?? SkinHeadService.GetHead(Path.Combine(PathManager.SkinsCacheDir, a.Username + "_original.png"), 6);
                a.HeadImage = head;
                Accounts.Add(a);
            }
            _selectedAccount = AccountService.CurrentAccount;
            // 单选勾选框：只有当前账户被勾选
            foreach (var a in Accounts) a.IsSelected = ReferenceEquals(a, _selectedAccount);
            _suppressSwitch = false;
            OnPropertyChanged(nameof(SelectedAccount));
            OnPropertyChanged(nameof(HasAccount));
            OnPropertyChanged(nameof(AccountTypeText));
        }

        // ===== 创建：离线账户 =====
        private void CreateOfflineAccount()
        {
            // 不再要输入框：用默认名字创建，创建后直接进编辑页改名/选皮肤
            string name = "Player";
            int idx = 2;
            while (AccountService.Accounts.Any(a => a.Username == name))
                name = "Player" + idx++;

            var acc = new OfflineAccount
            {
                Username = name,
                IsMicrosoft = false,
                SkinPath = "",
                AuthServerUrl = "https://littleskin.cn/api/yggdrasil"
            };
            AccountService.AddAccount(acc, setCurrent: true);
            FinishCreateAndSwitch(acc, $"已创建账户：{name}");
            // 创建后直接跳到编辑页
            OpenEdit(acc.Id);
        }

        // ===== 创建：微软正版（先检查 Client ID，再进入设备码登录）=====
        private async Task StartMicrosoftLogin()
        {
            string clientId = ConfigManager.Settings.MicrosoftClientId;
            if (string.IsNullOrWhiteSpace(clientId)) clientId = PublicClientId;

            // 关闭选择窗口，开始登录（登录成功会创建新账户）
            IsCreateWindowVisible = false;
            await MicrosoftLoginAsync(clientId, createNewAccount: true);
        }

        // 保留原入口（设置页/其它地方可能调用）
        private async Task MicrosoftLoginAsync() => await MicrosoftLoginAsync(ConfigManager.Settings.MicrosoftClientId, createNewAccount: false);

        private async Task MicrosoftLoginAsync(string clientId, bool createNewAccount)
        {
            if (IsBusy) return;
            if (string.IsNullOrWhiteSpace(clientId)) clientId = PublicClientId;

            IsBusy = true;
            _loginCts = new System.Threading.CancellationTokenSource();
            IsDeviceLoginVisible = false;
            try
            {
                var session = await MicrosoftLoginService.LoginAsync(clientId,
                    dc =>
                    {
                        DeviceCode = dc.UserCode;
                        VerifyUri = dc.VerificationUri;
                        LoginStatus = "请在浏览器打开上面的网址，输入验证码完成登录";
                        IsDeviceLoginVisible = true;
                    },
                    status => LoginStatus = status,
                    _loginCts.Token);

                OfflineAccount acc;
                if (createNewAccount)
                {
                    acc = new OfflineAccount
                    {
                        Username = session.Username,
                        IsMicrosoft = true,
                        AccessToken = session.AccessToken,
                        Uuid = session.Uuid,
                        SkinPath = ""
                    };
                    AccountService.AddAccount(acc, setCurrent: true);
                    FinishCreateAndSwitch(acc, $"正版登录成功：{session.Username}");
                }
                else
                {
                    acc = AccountService.CurrentAccount;
                    acc.IsMicrosoft = true;
                    acc.Username = session.Username;
                    acc.AccessToken = session.AccessToken;
                    acc.Uuid = session.Uuid;
                    AccountService.Save();
                    RefreshAccountList();
                    PlayerName = acc.Username;
                    await LoadSkinAsync();
                    if (Application.Current.MainWindow is MainWindow mw)
                        mw.ShowToastSuccess($"正版登录成功：{session.Username}");
                }
            }
            catch (OperationCanceledException) { LoginStatus = "已取消登录"; }
            catch (Exception ex)
            {
                ShowNotice("微软登录失败：" + ex.Message, "登录失败");
            }
            finally
            {
                IsDeviceLoginVisible = false;
                IsBusy = false;
                _loginCts = null;
            }
        }

        private void FinishCreateAndSwitch(OfflineAccount acc, string toast)
        {
            RefreshAccountList();
            PlayerName = acc.Username;
            _currentSkinPath = acc.SkinPath;
            IsCreateWindowVisible = false;
            OnPropertyChanged(nameof(HasAccount));
            _ = LoadSkinAsync();
            if (Application.Current.MainWindow is MainWindow mw)
            {
                mw.ShowToastSuccess(toast);
                mw.SyncAccountToLaunchPage();
            }
        }

        // ===== 切换账户 =====
        public void SwitchAccountById(string id)
        {
            var acc = AccountService.Accounts.FirstOrDefault(a => a.Id == id);
            if (acc != null) SwitchTo(acc);
        }

        private void SwitchTo(OfflineAccount acc)
        {
            if (acc == null) return;
            AccountService.SwitchAccount(acc.Id);
            PlayerName = acc.Username;
            _currentSkinPath = acc.SkinPath;
            // 单选勾选框互斥：勾上新的、取消其它
            foreach (var a in Accounts) a.IsSelected = ReferenceEquals(a, acc);
            _selectedAccount = acc;
            OnPropertyChanged(nameof(SelectedAccount));
            CurrentCapeImage = LoadCapeImage(acc.CapePath);
            OnPropertyChanged(nameof(AccountTypeText));
            _ = LoadSkinAsync();
            if (Application.Current.MainWindow is MainWindow mw)
                mw.SyncAccountToLaunchPage();
        }

        private void DeleteAccount(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            // 【按用户要求】允许删光所有账户，不再强制保留一个
            var target = Accounts.FirstOrDefault(a => a.Id == id);
            if (target == null) return;
            ShowNotice($"确定删除账户「{target.Username}」吗？", "删除账户", isConfirm: true, blurBackground: true, onConfirm: () =>
            {
                if (AccountService.RemoveAccount(id))
                {
                    RefreshAccountList();
                    if (AccountService.HasAccount)
                    {
                        var cur = AccountService.CurrentAccount;
                        PlayerName = cur.Username;
                        _currentSkinPath = cur.SkinPath;
                        _ = LoadSkinAsync();
                    }
                    else
                    {
                        // 删光了：清空账户详情，不再显示游离的 Steve
                        PlayerName = "";
                        SkinImage = null;
                        CurrentCapeImage = null;
                        _currentSkinPath = "";
                    }
                    if (Application.Current.MainWindow is MainWindow mw)
                        mw.SyncAccountToLaunchPage();
                }
            });
        }

        // ===== 编辑账户 =====
        private async void OpenEdit(string id)
        {
            var acc = AccountService.Accounts.FirstOrDefault(a => a.Id == id);
            if (acc == null) return;
            // 先切到要编辑的账户，复用右侧的皮肤/名称状态
            SwitchTo(acc);
            EditName = acc.Username;
            IsEditWindowVisible = true;
            EditPageRequested?.Invoke();   // 通知 MainWindow 切到独立编辑页
            RefreshEditType();
            RefreshHead();

            // 【随取随用】内置皮肤立即显示；在线 skinmc 皮肤边下边显示（和披风同款渐进加载）
            if (SkinLibrary.Count == 0)
            {
                var builtin = SkinLibraryService.GetBuiltinSkins();
                foreach (var s in builtin) SkinLibrary.Add(s);
                SplitSkins(); // 先显示内置，不让用户干等联网

                // 在线皮肤：下载一张显示一张，回调在后台线程 → 逐张切回 UI 添加
                await SkinLibraryService.ScanOnlineSkinsAsync("", 0, s =>
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        if (SkinLibrary.Any(x => x.Name == s.Name)) return; // 去重
                        SkinLibrary.Add(s);
                        if (SkinLibrary.Count(x => x.Source == "云端") % OnlinePageSize == 1)
                            SplitSkins(); // 边下边刷新两栏
                    });
                }).ConfigureAwait(false);
                Application.Current.Dispatcher.Invoke(() => SplitSkins());
            }
            else
                SplitSkins();
            // 按账户"当前真实皮肤"内容匹配并高亮，而不是无脑选第一个。
            // 账户 SkinPath 是复制到 skin_{id}.png 的副本，文件名对不上，用内容指纹识别。
            var currentFp = SkinLibraryService.GetSkinFingerprint(acc.SkinPath);
            SkinEntry match = null;
            if (currentFp != null)
                match = SkinLibrary.FirstOrDefault(s => !s.IsUpload &&
                    SkinLibraryService.GetSkinFingerprint(s.TexturePath) == currentFp);
            // 若没匹配到（比如是旧版遗留皮肤），把它作为一条"当前皮肤"加入内置库并高亮
            if (match == null && !string.IsNullOrEmpty(acc.SkinPath) && File.Exists(acc.SkinPath))
            {
                match = new SkinEntry
                {
                    Name = "当前皮肤",
                    TexturePath = acc.SkinPath,
                    Source = "本地",
                    HeadImage = SkinHeadService.GetHead(acc.SkinPath, 6),
                    SkinImage = SkinLibraryService.LoadScaledSkin(acc.SkinPath)
                };
                SkinLibrary.Insert(0, match);
                SplitSkins();
            }
            if (match == null) match = BuiltinSkins.FirstOrDefault(s => !s.IsUpload);
            if (match != null) SelectSkinSilently(match);
            try { System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "CraftGate_cape_diag.log"),
                $"{DateTime.Now:HH:mm:ss} [OpenEdit披风] CapeLibrary.Count={CapeLibrary.Count} 开始加载\n"); } catch { }
            if (CapeLibrary.Count == 0)
            {
                // 联网补充云端披风（失败不影响本地）
                // 【修复】原代码用 ConfigureAwait(false) 导致 await 后掉到后台线程，
                // CapeLibrary 绑定 CollectionView，跨线程 Add 抛 NotSupportedException。
                // 这里联网仍可后台跑，但拿到结果后统一切回 UI 线程再改集合。
                var localCapes = SkinLibraryService.GetCapes();
                // 内置披风立即显示
                Application.Current.Dispatcher.Invoke(() =>
                {
                    foreach (var c in localCapes) { CapeLibrary.Add(c); BuiltinCapes.Add(c); }
                });
                // 在线披风：下载一张显示一张（渐进），回调在后台线程，逐张切回 UI 添加
                var modelSkinRef = SelectedSkin?.SkinImage ?? SkinImage;
                // 【随取随用】首批只加载12张(1页)，避免一次性48张放大建3D模型导致内存爆炸/卡死
                await SkinLibraryService.ScanOnlineCapesPageAsync(1, c =>
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        c.ModelSkin = modelSkinRef;
                        CapeLibrary.Add(c);
                        OnlineCapes.Add(c);
                        UpdateCapePages(); // 边下边刷新
                    });
                }, maxCount: CapePageSize).ConfigureAwait(false);
                Application.Current.Dispatcher.Invoke(() =>
                {
                _loadedCapeBatches = 1; // 首批48张=4个显示页
                try { System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "CraftGate_cape_diag.log"),
                    $"{DateTime.Now:HH:mm:ss} [OpenEdit披风完成] OnlineCapes.Count={OnlineCapes.Count} CapeLibrary.Count={CapeLibrary.Count}\n"); } catch { }
                });
            }
            // 披风卡片用 3D 角色预览：给每张卡填充角色皮肤（当前皮肤 or 默认 Steve）。
            // 同样切回 UI 线程改绑定属性。
            Application.Current.Dispatcher.Invoke(() =>
            {
                RefreshCapeModelSkin();
                UpdateCapePages();
            });
        }

        // 翻到末页时加载下一批披风（skinmc 下一页，12张=1显示页）
        private async Task LoadMoreCapesAsync()
        {
            if (_isLoadingMoreCapes) return;
            _isLoadingMoreCapes = true;
            IsLoadingMoreCapes = true;
            try
            {
                int nextBatch = _loadedCapeBatches + 1;
                var modelSkinRef = SelectedSkin?.SkinImage ?? SkinImage;
                int before = OnlineCapes.Count;
                await SkinLibraryService.ScanOnlineCapesPageAsync(nextBatch, c =>
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        if (OnlineCapes.Any(x => x.TexturePath == c.TexturePath)) return;
                        c.ModelSkin = modelSkinRef;
                        OnlineCapes.Add(c);
                        CapeLibrary.Add(c);
                        UpdateCapePages();
                    });
                }, maxCount: CapePageSize).ConfigureAwait(false);
                await Application.Current.Dispatcher.InvokeAsync(new System.Action(() =>
                {
                    if (OnlineCapes.Count > before) _loadedCapeBatches = nextBatch;
                    RefreshCapeModelSkin();
                    UpdateCapePages();
                }));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("加载更多披风失败: " + ex.Message);
            }
            finally
            {
                _isLoadingMoreCapes = false;
                IsLoadingMoreCapes = false;
            }
        }

        // 刷新所有披风卡的角色皮肤（用当前选中的皮肤搭配披风，而不是默认 Steve）
        private void RefreshCapeModelSkin()
        {
            var modelSkinPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "skins", "Steve.png");
            var skin = SelectedSkin?.SkinImage ?? SkinImage ?? SkinLibraryService.LoadScaledSkin(modelSkinPath);
            foreach (var c in CapeLibrary) c.ModelSkin = skin;
            foreach (var c in BuiltinCapes) c.ModelSkin = skin;
            foreach (var c in OnlineCapes) c.ModelSkin = skin;
        }

        // 生成"省略号"页码：首页、末页、当前页±2，中间用 ... 省略（参考常见分页样式）
        // total=总页数(从1计)，current=当前页(从0计)
        private void FillPageNumbers(ObservableCollection<PageItem> target, int total, int current)
        {
            target.Clear();
            if (total <= 0) total = 1;
            var show = new System.Collections.Generic.HashSet<int> { 0, total - 1 };
            for (int d = -2; d <= 2; d++)
            {
                int p = current + d;
                if (p >= 0 && p < total) show.Add(p);
            }
            var sorted = show.OrderBy(x => x).ToList();
            int prev = -1;
            foreach (int p in sorted)
            {
                if (prev >= 0 && p - prev > 1)
                    target.Add(new PageItem { IsEllipsis = true, Number = -1 });
                target.Add(new PageItem { Number = p, IsCurrent = (p == current) });
                prev = p;
            }
        }

        // 更新披风两栏的当前页与页码按钮
        private void UpdateCapePages()
        {
            if (_builtinCapePageIndex >= TotalBuiltinCapePages) _builtinCapePageIndex = 0;
            if (_onlineCapePageIndex >= TotalOnlineCapePages) _onlineCapePageIndex = 0;
            BuiltinCapePage.Clear();
            int bs = _builtinCapePageIndex * CapePageSize;
            for (int i = bs; i < Math.Min(bs + CapePageSize, BuiltinCapes.Count); i++)
                BuiltinCapePage.Add(BuiltinCapes[i]);
            FillPageNumbers(BuiltinCapePageNumbers, TotalBuiltinCapePages, _builtinCapePageIndex);

            OnlineCapePage.Clear();
            int os = _onlineCapePageIndex * CapePageSize;
            for (int i = os; i < Math.Min(os + CapePageSize, OnlineCapes.Count); i++)
                OnlineCapePage.Add(OnlineCapes[i]);
            FillPageNumbers(OnlineCapePageNumbers, TotalOnlineCapePages, _onlineCapePageIndex);
        }

        // 披风网站搜索：按关键词拉 skinmc 披风，切到在线栏显示
        private async Task SearchCapesAsync()
        {
            IsBuiltinCapeTab = false;
            var found = await SkinLibraryService.SearchCapesAsync(CapeQuery).ConfigureAwait(false);
            Application.Current.Dispatcher.Invoke(() =>
            {
                OnlineCapes.Clear();
                foreach (var c in found) OnlineCapes.Add(c);
                RefreshCapeModelSkin();
                _onlineCapePageIndex = 0;
                UpdateCapePages();
            });
        }

        // 皮肤网站翻到末页时加载下一批（随取随用）
        private bool _isLoadingMoreSkins;
        private async Task LoadMoreOnlineSkinsAsync()
        {
            if (_isLoadingMoreSkins) return;
            _isLoadingMoreSkins = true;
            try
            {
                int offset = SkinLibrary.Count(s => s.Source == "云端");
                await SkinLibraryService.ScanOnlineSkinsAsync("", offset, s =>
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        if (SkinLibrary.Any(x => x.Name == s.Name)) return;
                        SkinLibrary.Add(s);
                    });
                }).ConfigureAwait(false);
                await Application.Current.Dispatcher.InvokeAsync(new System.Action(() => SplitSkins()));
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine("加载更多皮肤失败: " + ex.Message); }
            finally { _isLoadingMoreSkins = false; }
        }

        // 皮肤站搜索/刷新/加载更多：reset=true 从头搜并清空旧云端，false 追加下一页
        private async Task SearchSkinsAsync(bool reset = true)
        {
            if (reset) _skinOffset = 0;
            // 刷新时保留本地皮肤 + 已缓存的云端皮肤（联网失败也不会把列表清空）
            var keep = SkinLibrary.ToList();
            if (reset)
            {
                SkinLibrary.Clear();
                foreach (var s in keep) SkinLibrary.Add(s);
            }

            var online = await SkinLibraryService.ScanOnlineSkinsAsync(SkinQuery ?? "", _skinOffset);
            var existing = new HashSet<string>(SkinLibrary.Select(s => s.Name), StringComparer.OrdinalIgnoreCase);
            int added = 0;
            foreach (var s in online)
            {
                if (existing.Contains(s.Name)) continue;  // 去重
                SkinLibrary.Add(s);
                existing.Add(s.Name);
                added++;
            }
            _skinOffset += online.Count;
            SplitSkins();

            if (Application.Current.MainWindow is MainWindow mw)
                mw.ShowToastSuccess($"新增云端皮肤 {added} 个（共 {SkinLibrary.Count(s => s.Source == "云端")} 个）");
        }

        // 上传皮肤占位项（显示在内置皮肤第一个）
        public SkinEntry UploadSkinPlaceholder { get; } = new SkinEntry { Name = "__upload__", Source = "上传", IsUpload = true, IsSelected = false };

        // 把皮肤库按来源拆到两栏
        private void SplitSkins()
        {
            BuiltinSkins.Clear();
            OnlineSkins.Clear();
            // 上传方块放第一个
            BuiltinSkins.Add(UploadSkinPlaceholder);
            foreach (var s in SkinLibrary)
            {
                if (s.Source == "云端") OnlineSkins.Add(s);
                else BuiltinSkins.Add(s);
            }
            OnPropertyChanged(nameof(BuiltinSkinCountText));
            OnPropertyChanged(nameof(OnlineSkinCountText));
            if (_builtinPageIndex >= TotalBuiltinPages) _builtinPageIndex = 0;
            UpdateBuiltinPage();
            if (_onlinePageIndex >= TotalOnlinePages) _onlinePageIndex = 0;
            UpdateOnlinePage();
        }

        // 更新内置皮肤当前页（两排）
        private void UpdateBuiltinPage()
        {
            BuiltinPageSkins.Clear();
            int start = _builtinPageIndex * BuiltinPageSize;
            for (int i = start; i < Math.Min(start + BuiltinPageSize, BuiltinSkins.Count); i++)
                BuiltinPageSkins.Add(BuiltinSkins[i]);
            FillPageNumbers(BuiltinPageNumbers, TotalBuiltinPages, _builtinPageIndex);
            OnPropertyChanged(nameof(TotalBuiltinPages));
        }

        // 更新皮肤网站当前页（两排）
        private void UpdateOnlinePage()
        {
            OnlinePageSkins.Clear();
            int start = _onlinePageIndex * OnlinePageSize;
            for (int i = start; i < Math.Min(start + OnlinePageSize, OnlineSkins.Count); i++)
                OnlinePageSkins.Add(OnlineSkins[i]);
            FillPageNumbers(OnlinePageNumbers, TotalOnlinePages, _onlinePageIndex);
            OnPropertyChanged(nameof(TotalOnlinePages));
        }

        private void RefreshHead()
        {
            var acc = AccountService.CurrentAccount;
            EditHeadImage = SkinHeadService.GetHead(acc.SkinPath, 6)
                            ?? SkinHeadService.GetHead(Path.Combine(PathManager.SkinsCacheDir, acc.Username + "_original.png"), 6);
        }

        private void RefreshEditType()
        {
            SliderIsMicrosoft = AccountService.CurrentAccount.IsMicrosoft;
            NeedMicrosoftLogin = false;
            OnPropertyChanged(nameof(IsMicrosoftMode));
            OnPropertyChanged(nameof(ConvertTypeText));
            OnPropertyChanged(nameof(IsCurrentlyMicrosoft));
        }

        // ===== 应用内提示弹窗 =====
        private void ShowNotice(string message, string title = "提示", bool isConfirm = false, Action onConfirm = null, bool blurBackground = false)
        {
            NoticeMessage = message;
            NoticeTitle = title;
            IsNoticeConfirm = isConfirm;
            _noticeConfirmAction = onConfirm;
            BlurBackground = blurBackground;
            IsNoticeVisible = true;
        }
        private void CloseNotice()
        {
            IsNoticeVisible = false;
            BlurBackground = false;
            _noticeConfirmAction = null;
        }

        // PCL2 风格：先让滑块滑到目标位置，滑动动画完成后再执行登录/切换
        private async Task AnimatedSwitchAsync(bool toMicrosoft)
        {
            // 用滑块当前位置判断，而不是账户实际类型——因为滑到正版后只是"待登录"，账户仍是离线
            bool sliderAt = SliderIsMicrosoft;
            if (sliderAt == toMicrosoft) return; // 滑块已经在这一侧，不动作

            // 1) 先移动滑块 thumb（XAML 里的 DataTrigger 动画随之启动）
            SliderIsMicrosoft = toMicrosoft;
            // 2) 等滑动动画跑完（约 320ms）
            await Task.Delay(340);
            // 3) 离线：直接切换；正版：只滑过去，留一个登录按钮让用户自己点（PCL2 风格）
            if (toMicrosoft)
            {
                NeedMicrosoftLogin = true;   // 页面显示"登录"按钮，不自动登录
            }
            else
            {
                NeedMicrosoftLogin = false;
                // 只有账户真的是正版时才需要切成离线；本来就是离线只回弹滑块即可
                if (AccountService.CurrentAccount.IsMicrosoft)
                    ConvertCurrentToOffline();
                RefreshEditType();
            }
        }

        // 每个账户独立的皮肤文件（按账户ID隔离，互不覆盖）
        private static string GetAccountSkinPath(string accountId)
        {
            Directory.CreateDirectory(PathManager.SkinsCacheDir);
            return Path.Combine(PathManager.SkinsCacheDir, $"skin_{accountId}.png");
        }

        // 应用选中的皮肤：复制到当前账户专属路径，刷新预览和大头
        private void ApplySkin(SkinEntry skin)
        {
            if (skin == null || !File.Exists(skin.TexturePath)) return;
            try
            {
                var acc = AccountService.CurrentAccount;
                string accountSkinPath = GetAccountSkinPath(acc.Id);
                File.Copy(skin.TexturePath, accountSkinPath, true);
                _currentSkinPath = accountSkinPath;
                acc.SkinPath = accountSkinPath;
                AccountService.Save();
                _ = LoadSkinAsync();
                EditHeadImage = SkinHeadService.GetHead(accountSkinPath, 6);
                if (Application.Current.MainWindow is MainWindow mw)
                    mw.ShowToastSuccess($"已选用皮肤：{skin.Name}");
            }
            catch (Exception ex) { ShowNotice("应用皮肤失败：" + ex.Message); }
        }

        // 应用选中的披风
        private void ApplyCape(CapeEntry cape)
        {
            if (cape == null || !File.Exists(cape.TexturePath)) return;
            try
            {
                Directory.CreateDirectory(PathManager.SkinsCacheDir);
                string internalPath = Path.Combine(PathManager.SkinsCacheDir, "user_cape.png");
                File.Copy(cape.TexturePath, internalPath, true);
                AccountService.CurrentAccount.CapePath = internalPath;
                AccountService.Save();
                CurrentCapeImage = LoadCapeImage(internalPath);
                if (Application.Current.MainWindow is MainWindow mw)
                    mw.ShowToastSuccess($"已选用披风：{cape.Name}");
            }
            catch (Exception ex) { ShowNotice("应用披风失败：" + ex.Message); }
        }

        private static ImageSource LoadCapeImage(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    var dec = BitmapDecoder.Create(fs, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                    var frame = new FormatConvertedBitmap(dec.Frames[0], PixelFormats.Bgra32, null, 0);
                    frame.Freeze();
                    return frame;
                }
            }
            catch { return null; }
        }

        // 点击名称就地编辑：只保存名字到当前账户，不切页
        public void RenameCurrentAccount(string name)
        {
            name = (name ?? "").Trim();
            if (string.IsNullOrEmpty(name) || name.Length > 16) return;
            var acc = AccountService.CurrentAccount;
            acc.Username = name;
            EditName = name;
            AccountService.Save();
            PlayerName = name;
            RefreshAccountList();
            if (Application.Current.MainWindow is MainWindow mw)
                mw.SyncAccountToLaunchPage();
        }

        private void SaveEdit()
        {
            string rawName = (EditName ?? "").Trim();
            if (string.IsNullOrEmpty(rawName))
            {
                ShowNotice("玩家名称不能为空。");
                return;
            }
            // Minecraft 用户名只允许字母/数字/下划线；空格等会被服务器踢（连接中断）
            string name = AccountService.SanitizeUsername(rawName);
            if (name != rawName)
                ShowNotice($"名称含非法字符，已自动改为「{name}」\n（Minecraft 用户名只允许字母、数字、下划线）", "名称已修正");

            var acc = AccountService.CurrentAccount;
            acc.Username = name;
            // 皮肤已在选中时由 ApplySkin 立即写入账户（变更即保存），
            // 这里不再从 _selectedSkin 复制，避免把"默认高亮项"误写成账户皮肤。
            acc.SkinPath = _currentSkinPath;
            AccountService.Save();
            PlayerName = name;
            RefreshAccountList();
            OnPropertyChanged(nameof(AccountTypeText));
            RefreshEditType();
            _ = LoadSkinAsync();
            IsEditWindowVisible = false;
            if (Application.Current.MainWindow is MainWindow mw)
            {
                mw.ShowToastSuccess("账户已更新！");
                mw.SyncAccountToLaunchPage();
            }
        }

        // 改用离线：清掉正版信息，保留名字，可自由改名
        private void ConvertCurrentToOffline()
        {
            var acc = AccountService.CurrentAccount;
            if (!acc.IsMicrosoft)
            {
                ShowNotice("当前已经是离线账户。");
                return;
            }
            ShowNotice(
                "改用离线登录后，将清除该账户的正版令牌，之后只能以离线模式启动（正版服务器可能无法进入）。\n\n确定继续吗？",
                "改用离线账户", isConfirm: true, onConfirm: () =>
                {
                    acc.IsMicrosoft = false;
                    acc.AccessToken = "";
                    acc.Uuid = "";
                    MicrosoftLoginService.SignOut();
                    AccountService.Save();
                    RefreshAccountList();
                    OnPropertyChanged(nameof(AccountTypeText));
                    RefreshEditType();
                    EditName = acc.Username;
                    if (Application.Current.MainWindow is MainWindow mw)
                        mw.ShowToastSuccess("已切换为离线账户");
                });
        }

        // 改用正版：走设备码登录，成功后更新当前账户（不新建）
        private async Task ConvertCurrentToMicrosoft()
        {
            string clientId = ConfigManager.Settings.MicrosoftClientId;
            if (string.IsNullOrWhiteSpace(clientId)) clientId = PublicClientId;

            IsEditWindowVisible = false; // 让位给设备码登录页
            await MicrosoftLoginAsync(clientId, createNewAccount: false);
            // 登录成功后刷新编辑框名字
            EditName = AccountService.CurrentAccount.Username;
        }

        // ===== 皮肤相关（保留原有逻辑）=====
        public async void SetSkin(string originalPath)
        {
            try
            {
                var acc = AccountService.CurrentAccount;
                // 应用到当前账户专属路径（账户隔离，互不覆盖）
                string accountSkinPath = GetAccountSkinPath(acc.Id);
                File.Copy(originalPath, accountSkinPath, true);
                _currentSkinPath = accountSkinPath;
                acc.SkinPath = accountSkinPath;
                AccountService.Save();
                await LoadSkinAsync();
                EditHeadImage = SkinHeadService.GetHead(accountSkinPath, 6);

                // 弹出命名框：确认后把原图另存到上传库（持久化）
                _pendingSkinPath = originalPath;   // 用原始文件，确认时另存带名字的副本
                NewSkinName = "我的皮肤";
                IsSkinNameModalOpen = true;
            }
            catch (Exception ex) { ShowNotice("读取图片失败：" + ex.Message); }
        }

        // 确认上传皮肤的名称：归类为内置并选中
        private void ConfirmSkinName()
        {
            if (string.IsNullOrEmpty(_pendingSkinPath)) { IsSkinNameModalOpen = false; return; }
            string name = (NewSkinName ?? "").Trim();
            if (string.IsNullOrEmpty(name)) name = "我的皮肤";
            if (name.Length > 16) name = name.Substring(0, 16);
            // 文件名去掉非法字符，存到上传库（持久化，重启仍在）
            string safeName = string.Concat(name.Split(Path.GetInvalidFileNameChars()));
            if (string.IsNullOrEmpty(safeName)) safeName = "skin";
            Directory.CreateDirectory(PathManager.UploadedSkinsDir);
            string uploadedPath = Path.Combine(PathManager.UploadedSkinsDir, safeName + ".png");
            File.Copy(_pendingSkinPath, uploadedPath, true);

            var entry = new SkinEntry
            {
                Name = name,
                TexturePath = uploadedPath,
                Source = "本地",
                HeadImage = SkinHeadService.GetHead(uploadedPath, 6),
                SkinImage = SkinLibraryService.LoadScaledSkin(uploadedPath)
            };
            SkinLibrary.Add(entry);
            SplitSkins();
            foreach (var item in SkinLibrary) item.IsSelected = (item == entry);
            _selectedSkin = entry;
            OnPropertyChanged(nameof(SelectedSkin));
            OnPropertyChanged(nameof(SelectedSkinName));
            IsBuiltinTab = true;
            // 跳到该皮肤所在页
            int idx = BuiltinSkins.IndexOf(entry);
            if (idx >= 0) BuiltinPageIndex = idx / BuiltinPageSize;
            _pendingSkinPath = null;
            IsSkinNameModalOpen = false;
            if (Application.Current.MainWindow is MainWindow mw)
                mw.ShowToastSuccess($"皮肤「{name}」已添加");
        }

        private async Task LoadSkinAsync()
        {
            // 显示"正在加载"提示（切回 UI 线程设状态）
            await Application.Current.Dispatcher.InvokeAsync(new System.Action(() => IsLoadingSkinModal = true));
            try
            {
                var img = await SkinService.GetSkinAsync(PlayerName, _currentSkinPath).ConfigureAwait(false);
                // 图像在后台线程生成，必须切回 UI 线程赋值，否则 3D 预览跨线程崩溃
                await Application.Current.Dispatcher.InvokeAsync(new System.Action(() =>
                {
                    SkinImage = null;
                    SkinImage = img;
                }));
            }
            finally
            {
                await Application.Current.Dispatcher.InvokeAsync(new System.Action(() => IsLoadingSkinModal = false));
            }
        }

        private void SaveAccount()
        {
            var acc = AccountService.CurrentAccount;
            acc.Username = PlayerName;
            acc.SkinPath = _currentSkinPath;
            AccountService.Save();
            RefreshAccountList();
            if (Application.Current.MainWindow is MainWindow mainWin)
            {
                mainWin.ShowToastSuccess("账户信息与皮肤已更新！");
                mainWin.SyncAccountToLaunchPage();
            }
        }
    }

    // 皮肤网站分页按钮项
    public class PageItem : ViewModelBase
    {
        public int Number { get; set; }
        public bool IsEllipsis { get; set; }
        public string DisplayNumber => IsEllipsis ? "..." : (Number + 1).ToString();
        private bool _isCurrent;
        public bool IsCurrent { get => _isCurrent; set => Set(ref _isCurrent, value); }
    }
}
