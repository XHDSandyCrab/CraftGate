using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using CraftGate.Services;

namespace CraftGate.Controls
{
    public partial class SkinPreview3D : UserControl
    {
        public static readonly DependencyProperty SkinProperty =
            DependencyProperty.Register(nameof(Skin), typeof(ImageSource), typeof(SkinPreview3D),
                new PropertyMetadata(null, (d, e) =>
                {
                    var c = (SkinPreview3D)d;
                    c._modelBuilt = false;
                    if (c.IsActive) c.BuildModel();  // 不可见时跳过，等滚到可见再渲染
                }));

        public ImageSource Skin
        {
            get => (ImageSource)GetValue(SkinProperty);
            set => SetValue(SkinProperty, value);
        }

        // 是否播放手臂摆动动画（只有选中项才为 true）
        public static readonly DependencyProperty AnimateProperty =
            DependencyProperty.Register(nameof(Animate), typeof(bool), typeof(SkinPreview3D),
                new PropertyMetadata(false, (d, e) => ((SkinPreview3D)d).OnAnimateChanged()));
        public bool Animate
        {
            get => (bool)GetValue(AnimateProperty);
            set => SetValue(AnimateProperty, value);
        }

        // 视口懒渲染开关：只有卡片滚动到可见区域时才为 true
        public static readonly DependencyProperty IsActiveProperty =
            DependencyProperty.Register(nameof(IsActive), typeof(bool), typeof(SkinPreview3D),
                new PropertyMetadata(false, (d, e) => ((SkinPreview3D)d).OnIsActiveChanged()));
        public bool IsActive
        {
            get => (bool)GetValue(IsActiveProperty);
            set => SetValue(IsActiveProperty, value);
        }

        /// <summary>
        /// 常驻可见的预览（如账户详情面板）设为 true：加载后自动激活，无需视口检测。
        /// 皮肤卡片列表保持 false，由外部按滚动视口激活。
        /// </summary>
        public static readonly DependencyProperty AutoActivateProperty =
            DependencyProperty.Register(nameof(AutoActivate), typeof(bool), typeof(SkinPreview3D),
                new PropertyMetadata(false));
        public bool AutoActivate
        {
            get => (bool)GetValue(AutoActivateProperty);
            set => SetValue(AutoActivateProperty, value);
        }

        // 是否展示背面（披风卡片用：转成背朝相机，才能看到披在背后的披风）
        public static readonly DependencyProperty ShowBackProperty =
            DependencyProperty.Register(nameof(ShowBack), typeof(bool), typeof(SkinPreview3D),
                new PropertyMetadata(false, (d, e) =>
                {
                    var c = (SkinPreview3D)d;
                    c._modelBuilt = false;
                    if (c.IsActive) c.BuildModel();
                }));
        public bool ShowBack
        {
            get => (bool)GetValue(ShowBackProperty);
            set => SetValue(ShowBackProperty, value);
        }

        private bool _modelBuilt;

        private void OnIsActiveChanged()
        {
            if (IsActive)
            {
                // 进入可见区域：构建模型（只构建一次）
                if (!_modelBuilt) BuildModel();
                UpdateIdleAnimation();
            }
            else
            {
                // 离开可见区域：停动画、释放 3D 模型，省渲染开销
                StopIdle();
                if (Dispatcher.CheckAccess())
                {
                    ModelHost.Content = null;
                    _modelBuilt = false;
                }
            }
        }

        private void OnAnimateChanged()
        {
            UpdateIdleAnimation();
        }

        /// <summary>
        /// 综合视口可见性(IsActive)、动画开关(Animate)与窗口可见性决定是否播放手臂动画。
        /// 窗口不可见（最小化/切走）时停掉 RepeatBehavior.Forever 动画，省渲染开销。
        /// </summary>
        private void UpdateIdleAnimation()
        {
            if (IsActive && Animate && _modelBuilt && VisibilityService.IsAppVisible)
                StartIdle();
            else
                StopIdle();
        }

        private void StopIdle()
        {
            _leftArmRot.BeginAnimation(AxisAngleRotation3D.AngleProperty, null);
            _rightArmRot.BeginAnimation(AxisAngleRotation3D.AngleProperty, null);
            _leftArmRot.Angle = 0;
            _rightArmRot.Angle = 0;
            StopCapeWave();
        }

        // 披风纹理（64x32），穿在角色背后
        public static readonly DependencyProperty CapeProperty =
            DependencyProperty.Register(nameof(Cape), typeof(ImageSource), typeof(SkinPreview3D),
                new PropertyMetadata(null, (d, e) =>
                {
                    var c = (SkinPreview3D)d;
                    c._modelBuilt = false;
                    if (c.IsActive) c.BuildModel();
                }));
        public ImageSource Cape
        {
            get => (ImageSource)GetValue(CapeProperty);
            set => SetValue(CapeProperty, value);
        }

        // 站姿：正面朝向相机（不再侧身）
        private readonly AxisAngleRotation3D _sway =
            new AxisAngleRotation3D(new Vector3D(0, 1, 0), 0);
        // 手臂微摆：绕肩部小幅前后/左右摆（游戏里站立的 idle 感）
        private readonly AxisAngleRotation3D _leftArmRot =
            new AxisAngleRotation3D(new Vector3D(0, 0, 1), 0);
        private readonly AxisAngleRotation3D _rightArmRot =
            new AxisAngleRotation3D(new Vector3D(0, 0, 1), 0);

        // ===== 披风飘动：分段网格 + 逐帧更新顶点（仿游戏里前后飘动）=====
        private MeshGeometry3D _capeMesh;          // 披风网格（顶点每帧更新）
        private Point3D[] _capeBasePositions;      // 披风静止时的基准顶点
        private double[] _capePhase;               // 每个顶点按高度分配的相位（越靠下摆幅越大）
        private static int _capeAnimSubscriberCount; // CompositionTarget 订阅计数（多个预览控件共享一个循环）
        private DateTime _capeAnimStart;

        public SkinPreview3D()
        {
            InitializeComponent();
            RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.NearestNeighbor);
            // 不在这里渲染：由 IsActive（视口可见）触发，避免一次性构建几十个 3D 模型
            // 常驻面板（AutoActivate=true）加载后自动激活
            Loaded += (_, __) => { if (AutoActivate) IsActive = true; else if (IsActive && !_modelBuilt) BuildModel(); };

            // 窗口不可见时停掉常驻手臂动画，恢复可见时再启动
            VisibilityService.VisibilityChanged += _ => Dispatcher.BeginInvoke(new Action(UpdateIdleAnimation));
        }

        private void StartIdle()
        {
            // 手臂微摆：左右手臂对称、反相，绕肩小幅摆动；身体保持站立不动
            var ease = new SineEase { EasingMode = EasingMode.EaseInOut };
            _leftArmRot.BeginAnimation(AxisAngleRotation3D.AngleProperty, new DoubleAnimation
            {
                From = -7, To = 7, Duration = TimeSpan.FromSeconds(2.6),
                AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = ease
            });
            _rightArmRot.BeginAnimation(AxisAngleRotation3D.AngleProperty, new DoubleAnimation
            {
                From = 7, To = -7, Duration = TimeSpan.FromSeconds(2.6),
                AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = ease
            });
            StartCapeWave();
        }

        // ===== 披风飘动动画实现 =====
        private void StartCapeWave()
        {
            if (_capeMesh == null || _capeBasePositions == null) return;
            if (_capeWaveActive) return;
            _capeWaveActive = true;
            _capeAnimStart = DateTime.Now;
            _activeCapes.Add(this);
            if (_capeAnimSubscriberCount == 0)
                CompositionTarget.Rendering += OnCapeRendering;
            _capeAnimSubscriberCount++;
        }

        private void StopCapeWave()
        {
            if (!_capeWaveActive) return;
            _capeWaveActive = false;
            _activeCapes.Remove(this);
            _capeAnimSubscriberCount = Math.Max(0, _capeAnimSubscriberCount - 1);
            if (_capeAnimSubscriberCount == 0)
                CompositionTarget.Rendering -= OnCapeRendering;
            // 恢复静止顶点
            if (_capeMesh != null && _capeBasePositions != null)
                _capeMesh.Positions = new Point3DCollection(_capeBasePositions);
        }

        private bool _capeWaveActive;

        private static void OnCapeRendering(object sender, EventArgs e)
        {
            // 静态循环，遍历所有激活的预览控件更新披风顶点
            foreach (var c in _activeCapes)
                c.UpdateCapeWave();
        }

        private static readonly System.Collections.Generic.HashSet<SkinPreview3D> _activeCapes =
            new System.Collections.Generic.HashSet<SkinPreview3D>();

        private void UpdateCapeWave()
        {
            if (!_capeWaveActive || _capeMesh == null || _capeBasePositions == null || _capePhase == null) return;
            double t = (DateTime.Now - _capeAnimStart).TotalSeconds;
            int n = _capeBasePositions.Length;
            var pts = new Point3D[n];
            for (int i = 0; i < n; i++)
            {
                var basePt = _capeBasePositions[i];
                // 越靠下摆幅越大：用 phase 数组（顶部 0 → 底部 1）
                double amp = 0.55 * _capePhase[i];
                // 前后飘动（Z 方向正弦）+ 轻微左右（X 方向）、上下的涟漪
                double wave = Math.Sin(t * 2.6 + _capePhase[i] * 3.0) * amp;
                double sway = Math.Sin(t * 1.7 + _capePhase[i] * 2.2) * amp * 0.35;
                pts[i] = new Point3D(basePt.X + sway, basePt.Y, basePt.Z + wave);
            }
            // 直接替换 Positions 集合，触发 WPF 3D 重绘
            _capeMesh.Positions = new Point3DCollection(pts);
        }

        private void BuildModel()
        {
            // 皮肤图可能在后台线程生成，强制切回 UI 线程，避免跨线程异常
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new System.Action(BuildModel));
                return;
            }
            if (!IsInitialized) return;

            ModelHost.Content = null; // 先清空
            StopCapeWave();           // 停掉旧披风动画，避免逐帧更新已废弃的 mesh
            _capeMesh = null; _capeBasePositions = null; _capePhase = null;

            var bmp = Skin as BitmapSource;
            if (bmp == null) return;
            if (!IsActive) return;   // 视口懒渲染：不可见就不构建

            // 👇 双重保险：如果放大的图不是 1280x1280 或 1280x640，或者比例不对，直接不渲染
            // 因为原版 64x64 放大 20 倍就是 1280x1280
            if (bmp.PixelWidth != 1280 || (bmp.PixelHeight != 1280 && bmp.PixelHeight != 640))
            {
                return;
            }

            try
            {
                _modelBuilt = true;
                var root = new Model3DGroup();
                var body = new Model3DGroup();
                double scale = bmp.PixelWidth / 64.0; // 这里肯定是 20

                // 头
                AddBox(body, new Point3D(0, 12, 0), 8, 8, 8, new[]
                {
                    new Rect(8, 8, 8, 8), new Rect(24, 8, 8, 8), new Rect(16, 8, 8, 8),
                    new Rect(0, 8, 8, 8), new Rect(8, 0, 8, 8), new Rect(16, 0, 8, 8)
                }, bmp, scale);

                // 躯干
                AddBox(body, new Point3D(0, 2, 0), 8, 12, 4, new[]
                {
                    new Rect(20, 20, 8, 12), new Rect(32, 20, 8, 12), new Rect(28, 20, 4, 12),
                    new Rect(16, 20, 4, 12), new Rect(20, 16, 8, 4), new Rect(28, 16, 8, 4)
                }, bmp, scale);

                // 左臂（单独分组，绕肩微摆）
                var leftArm = new Model3DGroup();
                AddBox(leftArm, new Point3D(-6, 2, 0), 4, 12, 4, new[]
                {
                    new Rect(44, 20, 4, 12), new Rect(52, 20, 4, 12), new Rect(48, 20, 4, 12),
                    new Rect(40, 20, 4, 12), new Rect(44, 16, 4, 4), new Rect(48, 16, 4, 4)
                }, bmp, scale);

                // 右臂（注意 UV：老皮肤 64x32 用 44/52 那组，64x64 用 36/44 那组）
                var rightArm = new Model3DGroup();
                var rightArmRects = (bmp.PixelHeight == 640) ? new[]
                {
                    new Rect(44, 20, 4, 12), new Rect(52, 20, 4, 12), new Rect(48, 20, 4, 12),
                    new Rect(40, 20, 4, 12), new Rect(44, 16, 4, 4), new Rect(48, 16, 4, 4)
                } : new[]
                {
                    new Rect(36, 52, 4, 12), new Rect(44, 52, 4, 12), new Rect(40, 52, 4, 12),
                    new Rect(32, 52, 4, 12), new Rect(36, 48, 4, 4), new Rect(40, 48, 4, 4)
                };
                AddBox(rightArm, new Point3D(6, 2, 0), 4, 12, 4, rightArmRects, bmp, scale);

                // 腿
                AddBox(body, new Point3D(-2, -10, 0), 4, 12, 4, new[]
                {
                    new Rect(4, 20, 4, 12), new Rect(12, 20, 4, 12), new Rect(8, 20, 4, 12),
                    new Rect(0, 20, 4, 12), new Rect(4, 16, 4, 4), new Rect(8, 16, 4, 4)
                }, bmp, scale);
                var rightLegRects = (bmp.PixelHeight == 640) ? new[]
                {
                    new Rect(4, 20, 4, 12), new Rect(12, 20, 4, 12), new Rect(8, 20, 4, 12),
                    new Rect(0, 20, 4, 12), new Rect(4, 16, 4, 4), new Rect(8, 16, 4, 4)
                } : new[]
                {
                    new Rect(20, 52, 4, 12), new Rect(28, 52, 4, 12), new Rect(24, 52, 4, 12),
                    new Rect(16, 52, 4, 12), new Rect(20, 48, 4, 4), new Rect(24, 48, 4, 4)
                };
                AddBox(body, new Point3D(2, -10, 0), 4, 12, 4, rightLegRects, bmp, scale);

                // 手臂绕肩部旋转：先平移到肩为原点 → 旋转 → 平移回
                // 肩部位置：左(-6, 8, 0)、右(6, 8, 0)
                var leftArmTransform = new Transform3DGroup();
                leftArmTransform.Children.Add(new TranslateTransform3D(6, -8, 0));
                leftArmTransform.Children.Add(new RotateTransform3D(_leftArmRot));
                leftArmTransform.Children.Add(new TranslateTransform3D(-6, 8, 0));
                leftArm.Transform = leftArmTransform;

                var rightArmTransform = new Transform3DGroup();
                rightArmTransform.Children.Add(new TranslateTransform3D(-6, -8, 0));
                rightArmTransform.Children.Add(new RotateTransform3D(_rightArmRot));
                rightArmTransform.Children.Add(new TranslateTransform3D(6, 8, 0));
                rightArm.Transform = rightArmTransform;

                root.Children.Add(body);
                root.Children.Add(leftArm);
                root.Children.Add(rightArm);

                // ===== 披风：披在角色背后（-Z 方向）=====
                var capeBmp = Cape as BitmapSource;
                if (capeBmp != null)
                    AddCape(root, capeBmp);

                // ===== 外衣（皮肤第二层）：64x64 皮肤才有，检测到非透明才渲染 =====
                if (bmp.PixelHeight == 1280)
                {
                    double ex = 0.12; // 外层放大量，避免与内层重叠闪烁
                    // 头外层
                    var hatRects = new[]
                    {
                        new Rect(40, 8, 8, 8), new Rect(56, 8, 8, 8), new Rect(48, 8, 8, 8),
                        new Rect(32, 8, 8, 8), new Rect(40, 0, 8, 8), new Rect(48, 0, 8, 8)
                    };
                    if (HasSkinLayer(bmp, hatRects[0], scale))
                        AddBox(body, new Point3D(0, 12, 0), 8, 8, 8, hatRects, bmp, scale, ex);

                    // 身体外层（外套）
                    var jacketRects = new[]
                    {
                        new Rect(20, 36, 8, 12), new Rect(32, 36, 8, 12), new Rect(28, 36, 4, 12),
                        new Rect(16, 36, 4, 12), new Rect(20, 32, 8, 4), new Rect(28, 32, 8, 4)
                    };
                    if (HasSkinLayer(bmp, jacketRects[0], scale))
                        AddBox(body, new Point3D(0, 2, 0), 8, 12, 4, jacketRects, bmp, scale, ex);

                    // 左臂外层
                    var leftArmOuter = new[]
                    {
                        new Rect(44, 52, 4, 12), new Rect(52, 52, 4, 12), new Rect(48, 52, 4, 12),
                        new Rect(40, 52, 4, 12), new Rect(44, 48, 4, 4), new Rect(48, 48, 4, 4)
                    };
                    if (HasSkinLayer(bmp, leftArmOuter[0], scale))
                        AddBox(leftArm, new Point3D(-6, 2, 0), 4, 12, 4, leftArmOuter, bmp, scale, ex);

                    // 右臂外层
                    var rightArmOuter = new[]
                    {
                        new Rect(44, 36, 4, 12), new Rect(52, 36, 4, 12), new Rect(48, 36, 4, 12),
                        new Rect(40, 36, 4, 12), new Rect(44, 32, 4, 4), new Rect(48, 32, 4, 4)
                    };
                    if (HasSkinLayer(bmp, rightArmOuter[0], scale))
                        AddBox(rightArm, new Point3D(6, 2, 0), 4, 12, 4, rightArmOuter, bmp, scale, ex);

                    // 左腿外层
                    var leftLegOuter = new[]
                    {
                        new Rect(4, 52, 4, 12), new Rect(12, 52, 4, 12), new Rect(8, 52, 4, 12),
                        new Rect(0, 52, 4, 12), new Rect(4, 48, 4, 4), new Rect(8, 48, 4, 4)
                    };
                    if (HasSkinLayer(bmp, leftLegOuter[0], scale))
                        AddBox(body, new Point3D(-2, -10, 0), 4, 12, 4, leftLegOuter, bmp, scale, ex);

                    // 右腿外层
                    var rightLegOuter = new[]
                    {
                        new Rect(4, 36, 4, 12), new Rect(12, 36, 4, 12), new Rect(8, 36, 4, 12),
                        new Rect(0, 36, 4, 12), new Rect(4, 32, 4, 4), new Rect(8, 32, 4, 4)
                    };
                    if (HasSkinLayer(bmp, rightLegOuter[0], scale))
                        AddBox(body, new Point3D(2, -10, 0), 4, 12, 4, rightLegOuter, bmp, scale, ex);
                }

                var transform = new Transform3DGroup();
                transform.Children.Add(new RotateTransform3D(_sway));
                // 【披风卡】背面朝向相机：转 180°-32°，这样披在背后(-Z)的披风才可见
                // 普通皮肤卡：-32° 正面 3/4 视角
                double yAngle = ShowBack ? 148 : -32;
                transform.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 1, 0), yAngle)));
                transform.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(1, 0, 0), -10)));

                ModelHost.Content = root;
                ModelHost.Transform = transform;
                _modelBuilt = true;
                // 若动画条件满足，重启披风飘动（指向刚构建的新 mesh）
                if (Animate && VisibilityService.IsAppVisible) StartCapeWave();
            }
            catch (Exception ex)
            {
                _modelBuilt = false;
                System.Diagnostics.Debug.WriteLine("3D皮肤渲染异常: " + ex.Message);
            }
        }

        // 披风：在角色背后（-Z 方向）渲染分段立体网格。
        // 竖直分 8 段、带厚度，形成"长方体"轮廓；每段顶点按高度分配相位，供飘动动画逐帧更新。
        private void AddCape(Model3DGroup group, BitmapSource cape)
        {
            try
            {
                const int segments = 8;                 // 竖向分段数（越多越平滑）
                double cw = 10, ch = 16, z = -2.4, top = 8; // 宽/高/背向偏移/顶边Y
                double thickness = 0.5;                 // 披风厚度，形成侧面（立体感）

                // 【修复】不用 CroppedBitmap（多个 3D 模型间疑似被 WPF 共享缓存，导致所有披风显示成同一张）。
                // 改用完整原图做 ImageBrush + UV 坐标定位到 (0,0)-(12/64,16/32) 区域，每张卡用自己的图。
                var brush = new ImageBrush(cape) { Stretch = Stretch.Fill, Viewport = new Rect(0, 0, 1, 1),
                                                  ViewportUnits = BrushMappingMode.RelativeToBoundingBox };
                RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.NearestNeighbor);
                var material = new DiffuseMaterial(brush);
                // UV 范围：披风主图在 64x32 贴图的左上 12x16 区域
                double uMax = 12.0 / 64.0, vMax = 16.0 / 32.0;

                // ---- 构造正面（朝角色背面）与背面两片网格，加左右侧面 ----
                var positions = new Point3DCollection();
                var texCoords = new PointCollection();
                var indices = new Int32Collection();
                var phases = new System.Collections.Generic.List<double>();

                // 每片网格按 seg+1 行、2 列顶点构建；uvV 从 0(顶) 到 1(底)
                // faceZOffset：正面贴着角色(-2.4)，背面再往后(thickness)
                void AddSheet(double zOffset, bool flipX)
                {
                    int rowStart = positions.Count;
                    for (int s = 0; s <= segments; s++)
                    {
                        double v = (double)s / segments;
                        double y = top - ch * v;
                        // 相位：顶 0 底 1（底部摆幅最大）
                        double[][] row = flipX
                            ? new[] { new double[] { cw / 2, y, zOffset }, new double[] { -cw / 2, y, zOffset } }
                            : new[] { new double[] { -cw / 2, y, zOffset }, new double[] { cw / 2, y, zOffset } };
                        for (int c = 0; c < 2; c++)
                        {
                            positions.Add(new Point3D(row[c][0], row[c][1], row[c][2]));
                            // UV 映射到整图的左上披风区域
                            texCoords.Add(new Point((flipX ? 1 - c : c) * uMax, v * vMax));
                            phases.Add(v); // 顶点相位 = 高度比例
                        }
                    }
                    for (int s = 0; s < segments; s++)
                    {
                        int a = rowStart + s * 2, b = a + 1, c = a + 2, d = a + 3;
                        indices.Add(a); indices.Add(b); indices.Add(c);
                        indices.Add(b); indices.Add(d); indices.Add(c);
                    }
                }

                AddSheet(z, false);              // 正面
                AddSheet(z - thickness, true);   // 背面（镜像UV）

                // 左右侧面：连接正反两片，形成长方体侧边（用边缘 UV）
                void AddSide(double x, double u)
                {
                    int start = positions.Count;
                    for (int s = 0; s <= segments; s++)
                    {
                        double v = (double)s / segments;
                        double y = top - ch * v;
                        positions.Add(new Point3D(x, y, z));
                        texCoords.Add(new Point(u * uMax, v * vMax)); phases.Add(v);
                        positions.Add(new Point3D(x, y, z - thickness));
                        texCoords.Add(new Point(u * uMax + 0.01, v * vMax)); phases.Add(v);
                    }
                    for (int s = 0; s < segments; s++)
                    {
                        int a = start + s * 2, b = a + 1, c = a + 2, d = a + 3;
                        indices.Add(a); indices.Add(c); indices.Add(b);
                        indices.Add(b); indices.Add(c); indices.Add(d);
                    }
                }
                AddSide(-cw / 2, 0.0);
                AddSide(cw / 2, 1.0);

                _capeMesh = new MeshGeometry3D
                {
                    Positions = positions,
                    TextureCoordinates = texCoords,
                    TriangleIndices = indices
                };
                _capeBasePositions = new Point3D[positions.Count];
                positions.CopyTo(_capeBasePositions, 0);
                _capePhase = phases.ToArray();

                group.Children.Add(new GeometryModel3D(_capeMesh, material));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("披风渲染异常: " + ex.Message);
            }
        }

        // 检测皮肤 UV 区域是否有非透明像素（判断有没有穿外衣/帽子等第二层）
        private static bool HasSkinLayer(BitmapSource bmp, Rect rect, double scale)
        {
            try
            {
                int rx = (int)(rect.X * scale), ry = (int)(rect.Y * scale);
                int rw = (int)(rect.Width * scale), rh = (int)(rect.Height * scale);
                if (rx < 0 || ry < 0 || rx + rw > bmp.PixelWidth || ry + rh > bmp.PixelHeight) return false;
                var cropped = new CroppedBitmap(bmp, new Int32Rect(rx, ry, rw, rh));
                var fmt = new FormatConvertedBitmap(cropped, PixelFormats.Bgra32, null, 0);
                int stride = fmt.PixelWidth * 4;
                var px = new byte[fmt.PixelHeight * stride];
                fmt.CopyPixels(px, stride, 0);
                for (int i = 3; i < px.Length; i += 4)
                    if (px[i] > 10) return true;
                return false;
            }
            catch { return false; }
        }

        private static void AddBox(Model3DGroup group, Point3D center, double w, double h, double d, Rect[] uvs, BitmapSource bmp, double scale, double expand = 0)
        {
            // 外衣层略微放大，避免与内层 z-fighting
            w += expand; h += expand; d += expand;
            double hw = w / 2, hh = h / 2, hd = d / 2;
            double x = center.X, y = center.Y, z = center.Z;

            var faces = new[]
            {
                new[] { new Point3D(x - hw, y - hh, z + hd), new Point3D(x + hw, y - hh, z + hd), new Point3D(x + hw, y + hh, z + hd), new Point3D(x - hw, y + hh, z + hd) },
                new[] { new Point3D(x + hw, y - hh, z - hd), new Point3D(x - hw, y - hh, z - hd), new Point3D(x - hw, y + hh, z - hd), new Point3D(x + hw, y + hh, z - hd) },
                new[] { new Point3D(x - hw, y - hh, z - hd), new Point3D(x - hw, y - hh, z + hd), new Point3D(x - hw, y + hh, z + hd), new Point3D(x - hw, y + hh, z - hd) },
                new[] { new Point3D(x + hw, y - hh, z + hd), new Point3D(x + hw, y - hh, z - hd), new Point3D(x + hw, y + hh, z - hd), new Point3D(x + hw, y + hh, z + hd) },
                new[] { new Point3D(x - hw, y + hh, z + hd), new Point3D(x + hw, y + hh, z + hd), new Point3D(x + hw, y + hh, z - hd), new Point3D(x - hw, y + hh, z - hd) },
                new[] { new Point3D(x - hw, y - hh, z - hd), new Point3D(x + hw, y - hh, z - hd), new Point3D(x + hw, y - hh, z + hd), new Point3D(x - hw, y - hh, z + hd) }
            };

            for (int i = 0; i < 6; i++)
            {
                var mesh = new MeshGeometry3D
                {
                    Positions = new Point3DCollection(faces[i]),
                    TextureCoordinates = new PointCollection { new Point(0, 1), new Point(1, 1), new Point(1, 0), new Point(0, 0) },
                    TriangleIndices = new Int32Collection { 0, 1, 2, 0, 2, 3 }
                };

                var rect = uvs[i];
                int rx = (int)(rect.X * scale);
                int ry = (int)(rect.Y * scale);
                int rw = (int)(rect.Width * scale);
                int rh = (int)(rect.Height * scale);

                var cropped = new CroppedBitmap(bmp, new Int32Rect(rx, ry, rw, rh));
                cropped.Freeze();

                var brush = new ImageBrush(cropped) { Stretch = Stretch.Fill };
                RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.NearestNeighbor);
                group.Children.Add(new GeometryModel3D(mesh, new DiffuseMaterial(brush)));
            }
        }
    }
}