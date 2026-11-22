using System.Windows.Controls;
using CraftGate.ViewModels;
using Microsoft.Win32;

namespace CraftGate.Views
{
    public partial class AccountView : UserControl
    {
        private readonly AccountViewModel _vm;

        public AccountView() : this(new AccountViewModel())
        {
        }

        public AccountView(AccountViewModel vm)
        {
            InitializeComponent();
            _vm = vm;
            DataContext = _vm;

            // 👇 加载时自动拉取默认 Steve 皮肤
            Loaded += async (_, __) => await _vm.InitializeAsync();
        }

        private void AccountCard_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            // 点击账户卡片 = 打开编辑；点右上角的删除叉叉(Button)不触发编辑
            var dep = e.OriginalSource as System.Windows.DependencyObject;
            while (dep != null)
            {
                if (dep is Button || dep is System.Windows.Controls.CheckBox) return;
                dep = System.Windows.Media.VisualTreeHelper.GetParent(dep);
            }
            if (sender is Border border && border.Tag is string id)
            {
                if (_vm.OpenEditCommand.CanExecute(id))
                    _vm.OpenEditCommand.Execute(id);
            }
        }

        private void BtnSelectSkin_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            OpenFileDialog ofd = new OpenFileDialog
            {
                Filter = "皮肤图片 (*.png)|*.png|所有文件 (*.*)|*.*",
                Title = "选择 Minecraft 皮肤"
            };

            if (ofd.ShowDialog() == true)
            {
                _vm.SetSkin(ofd.FileName);
            }
        }

        private void BtnEditSkin_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            // 编辑弹窗里换皮肤，逻辑同上（作用于当前正在编辑的账户）
            BtnSelectSkin_Click(sender, e);
        }

        private void BtnOpenSkinSite_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "https://www.minecraftskins.com",
                    UseShellExecute = true
                });
            }
            catch { }
        }
    }
}