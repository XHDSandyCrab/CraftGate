using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Threading;
using CraftGate.Models;
using CraftGate.ViewModels;

namespace CraftGate.Services
{
    public class DownloadManager : ViewModelBase
    {
        private static readonly Lazy<DownloadManager> _instance = new Lazy<DownloadManager>(() => new DownloadManager());
        public static DownloadManager Instance => _instance.Value;

        public ObservableCollection<FileDownloadTask> Tasks { get; } = new ObservableCollection<FileDownloadTask>();
        public ObservableCollection<DownloadCategoryGroup> CategoryGroups { get; } = new ObservableCollection<DownloadCategoryGroup>();

        private double _overallProgress;
        public double OverallProgress
        {
            get => _overallProgress;
            set => Set(ref _overallProgress, value);
        }

        private string _overallSpeed = "0 KB/s";
        public string OverallSpeed
        {
            get => _overallSpeed;
            set => Set(ref _overallSpeed, value);
        }

        private DispatcherTimer _speedTimer;

        private DownloadManager()
        {
            _speedTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _speedTimer.Tick += (_, __) => UpdateStats(0.1);

            // 窗口不可见时停掉每秒 10 次的统计刷新（省 UI 线程），可见时恢复
            _speedTimer.IsEnabled = VisibilityService.IsAppVisible;
            VisibilityService.VisibilityChanged += visible =>
            {
                if (visible) { if (!_speedTimer.IsEnabled) _speedTimer.Start(); }
                else { if (_speedTimer.IsEnabled) _speedTimer.Stop(); }
            };
        }

        public void AddTask(FileDownloadTask task)
        {
            if (!Tasks.Contains(task))
            {
                Tasks.Add(task);
                UpdateGroups();
            }
        }

        public void Clear()
        {
            Tasks.Clear();
            CategoryGroups.Clear();
            FlatRows.Clear();
            _headerRows.Clear();
            OverallProgress = 0;
            OverallSpeed = "0 KB/s";
        }

        public void UpdateStats(double intervalSeconds)
        {
            if (Tasks.Count == 0) return;

            // 👇 核心修复：使用强类型统计，只要不是 100% 就算没完成
            int total = Tasks.Count;
            int completed = Tasks.Count(t => t.Progress >= 100);

            // 如果有未完成的，进度强制限制在 99.9% 以内
            if (completed < total)
            {
                double sum = Tasks.Sum(t => t.Progress);
                OverallProgress = Math.Min(99.9, sum / total);
            }
            else
            {
                OverallProgress = 100;
            }

            double totalSpeed = 0;
            foreach (var task in Tasks)
            {
                if (Math.Abs(task.DisplayProgress - task.Progress) > 0.1)
                {
                    task.DisplayProgress += (task.Progress - task.DisplayProgress) * 0.2;
                }
                else
                {
                    task.DisplayProgress = task.Progress;
                }

                task.UpdateSpeed(intervalSeconds);
                totalSpeed += task.SpeedKbps;
            }

            OverallSpeed = CraftGate.Models.SpeedFormatter.Format(totalSpeed);

            foreach (var group in CategoryGroups) group.UpdateProgress();
            RefreshHeaderCounts();   // 实时刷新弹窗分组的完成数 x/y
        }

        private void UpdateGroups()
        {
            var groups = Tasks.GroupBy(t => t.Category);
            CategoryGroups.Clear();
            foreach (var g in groups)
            {
                CategoryGroups.Add(new DownloadCategoryGroup(g.Key, g.ToList()));
            }
            RebuildFlatRows();
        }

        // ===== 详情弹窗用的扁平列表：分组标题行 + 任务行，供虚拟化 ListBox 只渲染可见项 =====
        public ObservableCollection<DownloadRow> FlatRows { get; } = new ObservableCollection<DownloadRow>();

        // 用户约定的分组展示顺序
        private static readonly string[] PreferredCategories =
        {
            "游戏本体", "Java", "模组加载器", "其他配件"
        };

        /// <summary>把内部 Category 映射成用户看到的用途分类。</summary>
        private static string DisplayCategory(string category)
        {
            switch (category)
            {
                case "游戏核心":
                case "游戏本体":
                    return "游戏本体";
                case "Java":
                case "java":
                case "轻量级java":
                    return "Java";
                case "模组加载器":
                case "加载器":
                    return "模组加载器";
                default:
                    // 依赖库、游戏资源、光影、小地图、模组列表、mod 等 → 其他配件
                    return "其他配件";
            }
        }

        private void RebuildFlatRows()
        {
            FlatRows.Clear();
            _headerRows.Clear();
            var groups = Tasks.GroupBy(t => DisplayCategory(t.Category)).ToList();
            groups.Sort((a, b) =>
            {
                int ia = Array.IndexOf(PreferredCategories, a.Key);
                int ib = Array.IndexOf(PreferredCategories, b.Key);
                if (ia < 0) ia = int.MaxValue;
                if (ib < 0) ib = int.MaxValue;
                return ia != ib ? ia.CompareTo(ib) : string.CompareOrdinal(a.Key, b.Key);
            });

            foreach (var g in groups)
            {
                var header = DownloadRow.ForHeader(g.Key, g.ToList());
                FlatRows.Add(header);
                _headerRows.Add(header);
                foreach (var task in g.OrderBy(t => t.Progress >= 100 ? 1 : 0))
                    FlatRows.Add(DownloadRow.ForTask(task));
            }
        }

        // 所有分组标题行，UpdateStats 里刷新完成数（解决"0/41 却一堆100%"）
        private readonly System.Collections.Generic.List<DownloadRow> _headerRows = new System.Collections.Generic.List<DownloadRow>();

        private void RefreshHeaderCounts()
        {
            foreach (var h in _headerRows) h.RefreshCount();
        }
    }

    /// <summary>详情弹窗扁平列表的一行：要么是分组标题，要么是一个下载任务。标题行的完成数实时计算。</summary>
    public class DownloadRow : ViewModelBase
    {
        public bool IsHeader { get; private set; }
        private System.Collections.Generic.List<FileDownloadTask> _groupTasks;
        private string _headerName;
        public string HeaderText => IsHeader
            ? $"{_headerName}  ({_groupTasks.Count(t => t.Progress >= 100)}/{_groupTasks.Count})"
            : null;
        public FileDownloadTask Task { get; private set; }

        public static DownloadRow ForHeader(string name, System.Collections.Generic.List<FileDownloadTask> tasks)
            => new DownloadRow { IsHeader = true, _headerName = name, _groupTasks = tasks };
        public static DownloadRow ForTask(FileDownloadTask task) => new DownloadRow { Task = task };

        public void RefreshCount()
        {
            if (IsHeader) OnPropertyChanged(nameof(HeaderText));
        }
    }

    public class DownloadCategoryGroup : ViewModelBase
    {
        public string Name { get; set; }
        public ObservableCollection<FileDownloadTask> Tasks { get; set; }

        public string TitleText => $"{Name} ({Tasks.Count(t => t.Progress >= 100)}/{Tasks.Count})";

        private double _progress;
        public double Progress
        {
            get => _progress;
            set => Set(ref _progress, value);
        }

        public DownloadCategoryGroup(string name, List<FileDownloadTask> tasks)
        {
            Name = name;
            Tasks = new ObservableCollection<FileDownloadTask>(tasks);
            UpdateProgress();
        }

        public void UpdateProgress()
        {
            Progress = Tasks.Count > 0 ? Tasks.Average(t => t.Progress) : 0;

            // 确保下完的沉底
            var sorted = Tasks.OrderBy(t => t.Progress >= 100 ? 1 : 0).ToList();
            for (int i = 0; i < sorted.Count; i++)
            {
                if (Tasks[i] != sorted[i])
                {
                    Tasks.Move(Tasks.IndexOf(sorted[i]), i);
                }
            }
        }
    }
}