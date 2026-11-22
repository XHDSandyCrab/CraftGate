using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CraftGate.ViewModels;

namespace CraftGate.Views
{
    public partial class SettingsView : UserControl
    {
        public SettingsView()
        {
            InitializeComponent();
            DataContext = new SettingsViewModel();
        }

        private void UpdateCard_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is SettingsViewModel vm && vm.CheckUpdateCommand.CanExecute(null))
                vm.CheckUpdateCommand.Execute(null);
        }
    }
}