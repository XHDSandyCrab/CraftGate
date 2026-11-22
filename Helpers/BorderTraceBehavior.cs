using System;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace CraftGate.Helpers
{
    /// <summary>
    /// 效果2：鼠标悬浮时，绿色描边从边框上"离鼠标最近的那个点"开始，沿整个周长向正反两个方向
    /// 同时发射，在对侧交汇于一点；缓入缓出，500ms。
    /// 描边路径为【圆角矩形】（含四段圆弧），并显式开启抗锯齿（EdgeMode=Unspecified，合法值）。
    /// 用法：helpers:BorderTraceBehavior.Enabled="True"，可选 helpers:BorderTraceBehavior.TraceRadius="12"
    /// </summary>
    public static class BorderTraceBehavior
    {
        public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
            "Enabled", typeof(bool), typeof(BorderTraceBehavior), new PropertyMetadata(false, OnEnabledChanged));

        public static readonly DependencyProperty TraceColorProperty = DependencyProperty.RegisterAttached(
            "TraceColor", typeof(Color), typeof(BorderTraceBehavior),
            new PropertyMetadata(Color.FromArgb(255, 61, 220, 132))); // #3DDC84

        // 默认 -1 = 自动读取被装饰 Border 的 CornerRadius（描边与控件圆角严格贴合）
        public static readonly DependencyProperty TraceRadiusProperty = DependencyProperty.RegisterAttached(
            "TraceRadius", typeof(double), typeof(BorderTraceBehavior), new PropertyMetadata(-1.0));

        public static void SetEnabled(DependencyObject element, bool value) => element.SetValue(EnabledProperty, value);
        public static bool GetEnabled(DependencyObject element) => (bool)element.GetValue(EnabledProperty);
        public static void SetTraceColor(DependencyObject element, Color value) => element.SetValue(TraceColorProperty, value);
        public static Color GetTraceColor(DependencyObject element) => (Color)element.GetValue(TraceColorProperty);
        public static void SetTraceRadius(DependencyObject element, double value) => element.SetValue(TraceRadiusProperty, value);
        public static double GetTraceRadius(DependencyObject element) => (double)element.GetValue(TraceRadiusProperty);

        private static readonly DependencyProperty AdornerProperty = DependencyProperty.RegisterAttached(
            "__traceAdorner", typeof(TraceAdorner), typeof(BorderTraceBehavior), new PropertyMetadata(null));

        private static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var fe = d as FrameworkElement;
            if (fe == null) return;

            RoutedEventHandler loaded = null;
            loaded = (s, _) =>
            {
                fe.Loaded -= loaded;
                if ((bool)e.NewValue)
                {
                    fe.MouseEnter -= OnEnter;
                    fe.MouseLeave -= OnLeave;
                    fe.MouseEnter += OnEnter;
                    fe.MouseLeave += OnLeave;
                }
                else
                {
                    fe.MouseEnter -= OnEnter;
                    fe.MouseLeave -= OnLeave;
                    RemoveAdorner(fe);
                }
            };
            if (fe.IsLoaded) { fe.Loaded -= loaded; loaded(fe, null); }
            else fe.Loaded += loaded;
        }

        private static void OnEnter(object sender, MouseEventArgs e)
        {
            var fe = sender as FrameworkElement;
            if (fe == null || fe.ActualWidth <= 0 || fe.ActualHeight <= 0) return;
            var layer = AdornerLayer.GetAdornerLayer(fe);
            if (layer == null) return;

            RemoveAdorner(fe);
            var adorner = new TraceAdorner(fe, GetTraceColor(fe), GetTraceRadius(fe));
            fe.SetValue(AdornerProperty, adorner);
            layer.Add(adorner);

            Point p = e.GetPosition(fe);
            double start = adorner.NearestPerimeter(p.X, p.Y);
            adorner.Start(start);
        }

        private static void OnLeave(object sender, MouseEventArgs e)
        {
            var fe = sender as FrameworkElement;
            if (fe != null) RemoveAdorner(fe);
        }

        private static void RemoveAdorner(FrameworkElement fe)
        {
            if (fe.GetValue(AdornerProperty) is TraceAdorner old)
            {
                try { AdornerLayer.GetAdornerLayer(fe)?.Remove(old); } catch { }
                old.Stop();
                fe.SetValue(AdornerProperty, null);
            }
        }

        /// <summary>沿圆角矩形边框周长生长的描边 Adorner（含圆弧采样 + 抗锯齿）</summary>
        private sealed class TraceAdorner : Adorner
        {
            private readonly Pen _pen;
            private readonly DispatcherTimer _timer;
            private readonly CubicEase _ease = new CubicEase { EasingMode = EasingMode.EaseInOut };

            private double _start;
            private double _progress;
            private double _grown;
            private double _total;

            private double _w, _h, _r;
            private double _radiusInput;
            private double _vTop, _vRight, _vBottom, _vLeft;   // 4 条直边的长度
            private double _arc;                                // 单个 90° 圆弧长度

            public TraceAdorner(UIElement adorned, Color color, double radius) : base(adorned)
            {
                IsHitTestVisible = false;
                _radiusInput = radius;
                // 显式声明抗锯齿（Unspecified = 使用默认抗锯齿，合法值；AntiAlias 非法会崩溃）
                RenderOptions.SetEdgeMode(this, EdgeMode.Unspecified);
                RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
                _pen = new Pen(new SolidColorBrush(color), 2.5)
                {
                    LineJoin = PenLineJoin.Round,
                    StartLineCap = PenLineCap.Round,
                    EndLineCap = PenLineCap.Round
                };
                _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
                _timer.Tick += Tick;
            }

            private bool _prepared;

            private void EnsurePrepared()
            {
                if (_prepared) return;
                var fe = AdornedElement as FrameworkElement;
                if (fe == null) return;
                _w = fe.RenderSize.Width;
                _h = fe.RenderSize.Height;

                // 自动读取被装饰 Border 的圆角，让描边严格贴合控件轮廓
                double radiusInput = _radiusInput;
                if (radiusInput < 0 && AdornedElement is System.Windows.Controls.Border border)
                    radiusInput = border.CornerRadius.TopLeft;
                if (radiusInput < 0) radiusInput = 0;

                double radius = Math.Max(0, Math.Min(radiusInput, Math.Min(_w, _h) / 2));
                _r = radius;
                double arc = Math.PI * radius / 2;
                _vTop = _vBottom = Math.Max(0, _w - 2 * radius);
                _vRight = _vLeft = Math.Max(0, _h - 2 * radius);
                _arc = arc;
                _total = _vTop + _vRight + _vBottom + _vLeft + 4 * _arc;
                _prepared = true;
            }

            public void Start(double start)
            {
                EnsurePrepared();
                _start = Math.Max(0, Math.Min(_total, start));
                _progress = 0;
                _grown = 0;
                InvalidateVisual();
                _timer.Start();
            }

            private void Tick(object sender, EventArgs e)
            {
                _progress += 16.0 / 500.0;
                if (_progress >= 1) { _progress = 1; _timer.Stop(); }
                double eased = _ease.Ease(_progress);
                _grown = eased * _total / 2.0;   // 双边各走一半，交汇于对侧
                InvalidateVisual();
            }

            public void Stop() => _timer.Stop();

            protected override Size MeasureOverride(Size constraint)
            {
                var fe = AdornedElement as FrameworkElement;
                return fe != null ? fe.RenderSize : base.MeasureOverride(constraint);
            }

            protected override Size ArrangeOverride(Size finalSize)
            {
                var fe = AdornedElement as FrameworkElement;
                return fe != null ? fe.RenderSize : base.ArrangeOverride(finalSize);
            }

            /// <summary>
            /// 圆角矩形周长参数化：从"上边左端起点"顺时针走 d 长度返回坐标。
            /// 段顺序：上直边 → 右上弧 → 右直边 → 右下弧 → 下直边 → 左下弧 → 左直边 → 左上弧。
            /// </summary>
            private Point PointAt(double d)
            {
                d = ((d % _total) + _total) % _total;
                double r = _r;

                // 上直边 y=0, x: r → w-r
                if (d <= _vTop) return new Point(r + d, 0);
                d -= _vTop;

                // 右上弧：圆心(w-r, r)，角度 -90°→0°
                if (d <= _arc)
                {
                    double a = -Math.PI / 2 + (d / _arc) * (Math.PI / 2);
                    return new Point(_w - r + r * Math.Cos(a), r + r * Math.Sin(a));
                }
                d -= _arc;

                // 右直边 x=w, y: r → h-r
                if (d <= _vRight) return new Point(_w, r + d);
                d -= _vRight;

                // 右下弧：圆心(w-r, h-r)，角度 0°→90°
                if (d <= _arc)
                {
                    double a = (d / _arc) * (Math.PI / 2);
                    return new Point(_w - r + r * Math.Cos(a), _h - r + r * Math.Sin(a));
                }
                d -= _arc;

                // 下直边 y=h, x: w-r → r
                if (d <= _vBottom) return new Point(_w - r - d, _h);
                d -= _vBottom;

                // 左下弧：圆心(r, h-r)，角度 90°→180°
                if (d <= _arc)
                {
                    double a = Math.PI / 2 + (d / _arc) * (Math.PI / 2);
                    return new Point(r + r * Math.Cos(a), _h - r + r * Math.Sin(a));
                }
                d -= _arc;

                // 左直边 x=0, y: h-r → r
                if (d <= _vLeft) return new Point(0, _h - r - d);
                d -= _vLeft;

                // 左上弧：圆心(r, r)，角度 180°→270°
                double a2 = Math.PI + (Math.Min(d, _arc) / _arc) * (Math.PI / 2);
                return new Point(r + r * Math.Cos(a2), r + r * Math.Sin(a2));
            }

            /// <summary>求鼠标位置在圆角矩形周长上最近的起点长度</summary>
            public double NearestPerimeter(double px, double py)
            {
                EnsurePrepared();
                // 密集采样周长取最近点
                const int samples = 240;
                double best = 0, bestDist = double.MaxValue;
                for (int i = 0; i < samples; i++)
                {
                    double d = _total * i / samples;
                    Point p = PointAt(d);
                    double dx = p.X - px, dy = p.Y - py;
                    double dist = dx * dx + dy * dy;
                    if (dist < bestDist) { bestDist = dist; best = d; }
                }
                return best;
            }

            protected override void OnRender(DrawingContext dc)
            {
                base.OnRender(dc);
                if (_total <= 0 || _grown <= 0) return;

                DrawArc(dc, _start, _grown, forward: true);
                DrawArc(dc, _start, _grown, forward: false);
            }

            private void DrawArc(DrawingContext dc, double start, double length, bool forward)
            {
                int segments = Math.Max(2, (int)(length / 3) + 2);
                Point prev = PointAt(start);
                for (int i = 1; i <= segments; i++)
                {
                    double signed = forward ? start + length * i / segments : start - length * i / segments;
                    Point cur = PointAt(signed);
                    dc.DrawLine(_pen, prev, cur);
                    prev = cur;
                }
            }
        }
    }
}
