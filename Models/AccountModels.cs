using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using Newtonsoft.Json;

namespace CraftGate.Models
{
    /// <summary>
    /// 一个账户（离线或微软正版）。Id 用于在账户列表中唯一标识。
    /// </summary>
    public class OfflineAccount : INotifyPropertyChanged
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        public string Username { get; set; } = "Steve";
        public string SkinPath { get; set; } = "";

        // 外置登录皮肤站 API 地址。为空时走内置 Mod 方案；填了走 Authlib-Injector。
        public string AuthServerUrl { get; set; } = "https://littleskin.cn/api/yggdrasil";

        // 微软正版登录信息（离线账户下 IsMicrosoft=false）
        public bool IsMicrosoft { get; set; } = false;
        public string AccessToken { get; set; } = "";
        public string Uuid { get; set; } = "";

        // 披风纹理路径（可选）
        public string CapePath { get; set; } = "";

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        // 显示用：账户类型标签
        public string TypeLabel => IsMicrosoft ? "微软正版" : "离线账户";

        // UI 用：从皮肤 UV 裁出的扁平大头（不序列化）
        [JsonIgnore]
        public ImageSource HeadImage { get; set; }

        // UI 用：是否为当前选中的账户（单选，由 ViewModel 维护，不序列化）
        private bool _isSelected;
        [JsonIgnore]
        public bool IsSelected
        {
            get => _isSelected;
            set { if (_isSelected != value) { _isSelected = value; OnPropertyChanged(); } }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
