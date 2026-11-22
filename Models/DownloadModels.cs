using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CraftGate.Models
{
    /// <summary>速度单位智能格式化：输入 KB/s，按大小自动选 B/s、KB/s、MB/s、GB/s。</summary>
    public static class SpeedFormatter
    {
        public static string Format(double kbps)
        {
            if (kbps <= 0) return "0 KB/s";
            if (kbps < 1) return $"{kbps * 1024:F0} B/s";            // <1 KB/s 显示 B/s
            if (kbps < 1024) return $"{kbps:F1} KB/s";
            double mb = kbps / 1024.0;
            if (mb < 1024) return $"{mb:F2} MB/s";
            return $"{mb / 1024.0:F2} GB/s";
        }
    }

    public class FileDownloadTask : INotifyPropertyChanged
    {
        public string Url { get; set; }
        public string SavePath { get; set; }

        private string _fileName;
        public string FileName
        {
            get => _fileName;
            set { _fileName = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayFileName)); }
        }

        public string DisplayFileName => string.IsNullOrEmpty(FileName) ? "" : (FileName.Length > 25 ? FileName.Substring(0, 22) + "..." : FileName);

        public string Category { get; set; } = "其他文件";
        public long FileSize { get; set; } = -1;

        private double _progress;
        public double Progress
        {
            get => _progress;
            set
            {
                // 抛弃浮点数误差
                if (value > 99.99) value = 100;
                _progress = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsWaiting));
                OnPropertyChanged(nameof(SpeedText));
                if (value == 100)
                {
                    DisplayProgress = 100;
                    Status = "已完成";
                }
            }
        }

        private double _displayProgress;
        public double DisplayProgress
        {
            get => _displayProgress;
            set { _displayProgress = value; OnPropertyChanged(); }
        }

        private double _speed;
        public double SpeedKbps
        {
            get => _speed;
            set { _speed = value; OnPropertyChanged(); OnPropertyChanged(nameof(SpeedText)); }
        }

        public bool IsWaiting => Status == "等待中" && Progress < 100;

        public string SpeedText
        {
            get
            {
                if (Status == "已完成" || Progress >= 100) return "已完成";
                if (Status == "解析中" || Status == "等待中") return Status;
                return SpeedKbps > 0 ? SpeedFormatter.Format(SpeedKbps) : "下载中...";
            }
        }

        private string _status = "等待中";
        public string Status
        {
            get => _status;
            set { _status = value; OnPropertyChanged(); OnPropertyChanged(nameof(SpeedText)); OnPropertyChanged(nameof(IsWaiting)); }
        }

        private double _lastProgress = 0;
        public void UpdateSpeed(double intervalSeconds)
        {
            if (FileSize > 0 && intervalSeconds > 0 && Progress < 100)
            {
                double diff = Progress - _lastProgress;
                if (diff > 0)
                {
                    double bytesDiff = (diff / 100.0) * FileSize;
                    SpeedKbps = (bytesDiff / 1024.0) / intervalSeconds;
                }
                else SpeedKbps = 0;
            }
            else SpeedKbps = 0;
            _lastProgress = Progress;
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}