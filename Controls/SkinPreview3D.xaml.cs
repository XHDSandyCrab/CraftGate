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

        // ===== 披风：刚体长方体 + 整体绕肩部前后摆动（不是曲面波浪）=====
        // 参考 skinview3d：BoxGeometry(10,16,1)，pivot 在肩部，rotation.x 前后摆
        private AxisAngleRotation3D _capeSwing;    // 披风绕 X 轴前后摆（刚体）

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

        // ===== 披风飘动：刚体整体绕肩部前后摆（游戏同款，不是曲面波浪）=====
        // skinview3d 默认披风向后倾 10.8°，摆动就是绕肩部 pivot 改 rotation.x
        private const double CapeBaseAngle = 10.8;  // 默认后倾角
        private void StartCapeWave()
        {
            if (_capeSwing == null) return;
            var ease = new SineEase { EasingMode = EasingMode.EaseInOut };
            // 绕肩部 X 轴前后摆：从后倾到前摆，整体刚体运动，保持长方体不变形
            _capeSwing.BeginAnimation(AxisAngleRotation3D.AngleProperty, new DoubleAnimation
            {
                From = CapeBaseAngle - 8, To = CapeBaseAngle + 14,
                Duration = TimeSpan.FromSeconds(2.2), AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever, EasingFunction = ease
            });
        }

        private void StopCapeWave()
        {
            if (_capeSwing == null) return;
            _capeSwing.BeginAnimation(AxisAngleRotation3D.AngleProperty, null);
            _capeSwing.Angle = CapeBaseAngle; // 静止时保持默认后倾
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
            StopCapeWave();           // 停掉旧披风刚体摆动动画
            _capeSwing = null;

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

        // 披风：刚体长方体盒子 10x16x1（skinview3d BoxGeometry(10,16,1)），以肩部为 pivot 前后摆。
        // 【关键】不是逐顶点曲面波浪——顶点固定，靠整个模型绕肩部旋转实现游戏同款飘动。
        private void AddCape(Model3DGroup group, BitmapSource cape)
        {
            try
            {
                const double cw = 10, ch = 16, depth = 1; // 宽10 高16 厚1（标准长方体）
                // 位置：披在角色背后。skinview3d: cape pivot (y=8,z=-2)，box 中心 (y=-8,z=0.5)
                // → 盒顶在 y=8(肩)，底在 y=-8，Z 中心 -2+0.5=-1.5（身体后面，身体厚4 => 背面 z=-2）
                double pivotY = 8, pivotZ = -2.0;
                double centerY = pivotY - ch / 2.0;   // 0
                double centerZ = pivotZ + depth / 2.0; // -1.5

                var brush = new ImageBrush(cape) { Stretch = Stretch.Fill };
                RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.NearestNeighbor);
                var material = new DiffuseMaterial(brush);

                // Minecraft 标准披风 UV（64x32）：外层 x=1..11,y=1..17；内层 x=11..21,y=1..17；
                // 左侧 x=0..1；右侧 x=21..22；顶 x=1..11,y=0..1；底 x=11..21,y=0..1
                double U(double x) => x / 64.0;
                double V(double y) => y / 32.0;

                var positions = new Point3DCollection();
                var texCoords = new PointCollection();
                var indices = new Int32Collection();

                // 加一个矩形面：4 顶点 2 三角形。pts 按左上、右上、右下、左下顺序，uv 对应。
                void AddQuad(Point3D p0, Point3D p1, Point3D p2, Point3D p3,
                             double u0, double v0, double u1, double v1)
                {
                    int b = positions.Count;
                    positions.Add(p0); positions.Add(p1); positions.Add(p2); positions.Add(p3);
                    texCoords.Add(new Point(u0, v0)); texCoords.Add(new Point(u1, v0));
                    texCoords.Add(new Point(u1, v1)); texCoords.Add(new Point(u0, v1));
                    indices.Add(b); indices.Add(b + 1); indices.Add(b + 2);
                    indices.Add(b); indices.Add(b + 2); indices.Add(b + 3);
                }

                double xL = -cw / 2, xR = cw / 2;
                double yT = centerY + ch / 2, yB = centerY - ch / 2; // 8 / -8
                double zFront = centerZ + depth / 2, zBack = centerZ - depth / 2; // -1.0 / -2.0

                // 外层(玩家看到的外面，朝 -Z)：x=1..11, y=1..17
                AddQuad(new Point3D(xR, yT, zBack), new Point3D(xL, yT, zBack),
                        new Point3D(xL, yB, zBack), new Point3D(xR, yB, zBack),
                        U(1), V(1), U(11), V(17));
                // 内层(贴身，朝 +Z)：x=11..21
                AddQuad(new Point3D(xL, yT, zFront), new Point3D(xR, yT, zFront),
                        new Point3D(xR, yB, zFront), new Point3D(xL, yB, zFront),
                        U(11), V(1), U(21), V(17));
                // 左侧边 x=0..1
                AddQuad(new Point3D(xL, yT, zFront), new Point3D(xL, yT, zBack),
                        new Point3D(xL, yB, zBack), new Point3D(xL, yB, zFront),
                        U(0), V(1), U(1), V(17));
                // 右侧边 x=21..22
                AddQuad(new Point3D(xR, yT, zBack), new Point3D(xR, yT, zFront),
                        new Point3D(xR, yB, zFront), new Point3D(xR, yB, zBack),
                        U(21), V(1), U(22), V(17));
                // 顶边 x=1..11,y=0..1
                AddQuad(new Point3D(xL, yT, zBack), new Point3D(xR, yT, zBack),
                        new Point3D(xR, yT, zFront), new Point3D(xL, yT, zFront),
                        U(1), V(0), U(11), V(1));
                // 底边 x=11..21,y=0..1
                AddQuad(new Point3D(xR, yB, zBack), new Point3D(xL, yB, zBack),
                        new Point3D(xL, yB, zFront), new Point3D(xR, yB, zFront),
                        U(11), V(0), U(21), V(1));

                var mesh = new MeshGeometry3D
                {
                    Positions = positions,
                    TextureCoordinates = texCoords,
                    TriangleIndices = indices
                };
                mesh.Freeze(); // 刚体：顶点不再每帧改，冻结省开销

                // 绕肩部 pivot 的刚体旋转（平移到肩→绕X摆→平移回）
                _capeSwing = new AxisAngleRotation3D(new Vector3D(1, 0, 0), CapeBaseAngle);
                var swingTransform = new Transform3DGroup();
                swingTransform.Children.Add(new TranslateTransform3D(0, -pivotY, -pivotZ));
                swingTransform.Children.Add(new RotateTransform3D(_capeSwing));
                swingTransform.Children.Add(new TranslateTransform3D(0, pivotY, pivotZ));

                var capeGroup = new Model3DGroup();
                capeGroup.Children.Add(new GeometryModel3D(mesh, material));
                capeGroup.Transform = swingTransform;
                group.Children.Add(capeGroup);
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