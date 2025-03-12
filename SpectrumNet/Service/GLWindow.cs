//namespace SpectrumNet
//{
//    public class GLSizeChangedEventArgs : EventArgs
//    {
//        public Size PreviousSize { get; }
//        public Size NewSize { get; }
//        public bool WidthChanged => PreviousSize.Width != NewSize.Width;
//        public bool HeightChanged => PreviousSize.Height != NewSize.Height;

//        public GLSizeChangedEventArgs(Size previousSize, Size newSize) =>
//            (PreviousSize, NewSize) = (previousSize, newSize);
//    }

//    public interface IGLControl
//    {
//        void Start(GLWpfControlSettings settings);
//        event Action<TimeSpan> Render;
//        void InvalidateVisual();
//        event EventHandler<GLSizeChangedEventArgs> SizeChanged;
//        double ActualWidth { get; }
//        double ActualHeight { get; }
//        bool IsInitialized { get; }
//        void InitializeOpenGLBindings(IOpenGLService glService);
//        bool MakeContextCurrent();
//        System.Windows.VerticalAlignment VerticalAlignment { get; set; }
//        System.Windows.HorizontalAlignment HorizontalAlignment { get; set; }
//        bool SnapsToDevicePixels { get; set; }
//        bool UseLayoutRounding { get; set; }
//    }

//    public class GLWpfControlSettings
//    {
//        public int MajorVersion { get; set; } = 4;
//        public int MinorVersion { get; set; } = 0;
//        public bool RenderContinuously { get; set; } = false;
//    }

//    public class GLHostControl : FrameworkElement, IGLControl, IDisposable
//    {
//        private const string LogPrefix = "GLHostControl";
//        private IntPtr _glWindowHandle = IntPtr.Zero, _parentWindowHandle;
//        private GLWindow? _glWindow;
//        private CancellationTokenSource? _cancellationSource;
//        private int _width = 100, _height = 100;
//        private SpectrumAnalyzer? _analyzer;
//        private GLWpfControlSettings _glSettings = new();
//        private bool _isInitialized, _isDisposed, _isOpenGLInitialized;
//        private DateTime _lastRenderTime = DateTime.Now;
//        private DispatcherTimer? _renderTimer;
//        private bool _isPositionUpdatePending;

//        public event Action<TimeSpan> Render = delegate { };
//        public new event EventHandler<GLSizeChangedEventArgs> SizeChanged = delegate { };
//        public new double ActualWidth => _width;
//        public new double ActualHeight => _height;
//        public new bool IsInitialized => _isInitialized && _glWindow != null && !_isDisposed;
//        public new VerticalAlignment VerticalAlignment { get => base.VerticalAlignment; set => base.VerticalAlignment = value; }
//        public new HorizontalAlignment HorizontalAlignment { get => base.HorizontalAlignment; set => base.HorizontalAlignment = value; }
//        public new bool SnapsToDevicePixels { get => base.SnapsToDevicePixels; set => base.SnapsToDevicePixels = value; }
//        public new bool UseLayoutRounding { get => base.UseLayoutRounding; set => base.UseLayoutRounding = value; }

//        public GLHostControl()
//        {
//            SmartLogger.Log(LogLevel.Information, LogPrefix, "Creating GLHostControl instance");
//            Loaded += OnLoaded;
//            Unloaded += OnUnloaded;
//            base.SizeChanged += OnSizeChangedInternal;

//            // Устанавливаем базовые свойства для лучшей интеграции с WPF
//            SnapsToDevicePixels = true;
//            UseLayoutRounding = true;

//            // Подписываемся на событие изменения положения окна
//            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
//            timer.Tick += (s, e) => {
//                if (_isPositionUpdatePending && !_isDisposed)
//                {
//                    UpdateWindowPosition();
//                    _isPositionUpdatePending = false;
//                }
//            };
//            timer.Start();
//        }

//        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
//        {
//            base.OnRenderSizeChanged(sizeInfo);
//            _isPositionUpdatePending = true;
//        }

//        protected override Size MeasureOverride(Size availableSize)
//        {
//            // Возвращаем желаемый размер или доступный размер
//            return new Size(
//                double.IsInfinity(availableSize.Width) ? 100 : availableSize.Width,
//                double.IsInfinity(availableSize.Height) ? 100 : availableSize.Height);
//        }

//        protected override Size ArrangeOverride(Size finalSize)
//        {
//            // Обновляем размер при изменении расположения
//            UpdateSize((int)finalSize.Width, (int)finalSize.Height);
//            _isPositionUpdatePending = true;
//            return finalSize;
//        }

//        private void OnLoaded(object sender, RoutedEventArgs e)
//        {
//            SmartLogger.Log(LogLevel.Information, LogPrefix, "OnLoaded event triggered");

//            var window = SmartLogger.Safe(() => Window.GetWindow(this), defaultValue: null);
//            if (window == null)
//            {
//                SmartLogger.Log(LogLevel.Error, LogPrefix, "Parent window not found");
//                return;
//            }

//            _parentWindowHandle = SmartLogger.Safe(() => new WindowInteropHelper(window).Handle, defaultValue: IntPtr.Zero);
//            if (_parentWindowHandle == IntPtr.Zero)
//            {
//                SmartLogger.Log(LogLevel.Error, LogPrefix, "Parent window handle is zero");
//                return;
//            }

//            SmartLogger.Log(LogLevel.Information, LogPrefix, $"Parent window handle obtained: {_parentWindowHandle}");

//            // Подписываемся на события изменения положения
//            window.LocationChanged += (s, args) => _isPositionUpdatePending = true;
//            window.StateChanged += (s, args) => _isPositionUpdatePending = true;

//            InitializeOpenGL();
//        }

//        private void OnSizeChangedInternal(object sender, SizeChangedEventArgs e)
//        {
//            SmartLogger.Safe(() => {
//                UpdateSize((int)e.NewSize.Width, (int)e.NewSize.Height);
//                _isPositionUpdatePending = true;
//            }, LogPrefix, "Error handling size change");
//        }

//        public void InitializeOpenGLBindings(IOpenGLService glService)
//        {
//            SmartLogger.Log(LogLevel.Information, LogPrefix, "Initializing OpenGL bindings");

//            if (_glWindow == null)
//            {
//                SmartLogger.Log(LogLevel.Warning, LogPrefix, "GLWindow is null, attempting to initialize OpenGL");
//                InitializeOpenGL();

//                if (_glWindow == null)
//                {
//                    SmartLogger.Log(LogLevel.Error, LogPrefix, "Failed to initialize OpenGL window");
//                    return;
//                }
//            }

//            SmartLogger.Safe(() => _glWindow.EnqueueCommand(() =>
//            {
//                SmartLogger.Log(LogLevel.Debug, LogPrefix, "Running OpenGL bindings initialization command");
//                _glWindow.MakeCurrent();
//                glService.InitializeBindings();
//                SmartLogger.Log(LogLevel.Information, LogPrefix, "OpenGL bindings initialized successfully");
//            }), LogPrefix, "Error initializing OpenGL bindings");
//        }

//        public bool MakeContextCurrent()
//        {
//            if (_glWindow == null)
//            {
//                SmartLogger.Log(LogLevel.Warning, LogPrefix, "Cannot make context current: GLWindow is null");
//                return false;
//            }

//            return SmartLogger.Safe(() =>
//            {
//                SmartLogger.Log(LogLevel.Debug, LogPrefix, "Making OpenGL context current");
//                _glWindow.ProcessCommands();
//                return _glWindow.MakeCurrent();
//            }, defaultValue: false, LogPrefix, "Error making OpenGL context current");
//        }

//        private void OnUnloaded(object sender, RoutedEventArgs e)
//        {
//            SmartLogger.Log(LogLevel.Information, LogPrefix, "OnUnloaded event triggered, disposing resources");
//            Dispose();
//        }

//        protected override void OnRender(DrawingContext drawingContext)
//        {
//            base.OnRender(drawingContext);

//            if (!_isOpenGLInitialized || _glWindow == null)
//            {
//                SmartLogger.Log(LogLevel.Debug, LogPrefix, "Drawing black rectangle because OpenGL is not initialized");
//                drawingContext.DrawRectangle(
//                    new SolidColorBrush(Colors.Black),
//                    null,
//                    new Rect(0, 0, ActualWidth, ActualHeight));
//                return;
//            }

//            RenderOpenGL();
//        }

//        private void RenderOpenGL()
//        {
//            if (_isDisposed || !_isInitialized || _glWindow == null)
//            {
//                SmartLogger.Log(LogLevel.Debug, LogPrefix, "Skipping render: control is disposed or not initialized");
//                return;
//            }

//            SmartLogger.Safe(() =>
//            {
//                var now = DateTime.Now;
//                var delta = now - _lastRenderTime;
//                _lastRenderTime = now;

//                SmartLogger.Log(LogLevel.Trace, LogPrefix, $"RenderOpenGL: processing commands, delta={delta.TotalMilliseconds}ms");
//                _glWindow.ProcessCommands();
//                _glWindow.RenderFrameManual(delta);
//            }, LogPrefix, "Error in RenderOpenGL");
//        }

//        public void Initialize(IntPtr parentHandle, int width, int height, SpectrumAnalyzer? analyzer)
//        {
//            SmartLogger.Log(LogLevel.Information, LogPrefix, $"Initialize called with size {width}x{height}");

//            _parentWindowHandle = parentHandle;
//            _width = Math.Max(width, 1);
//            _height = Math.Max(height, 1);
//            _analyzer = analyzer;
//            _cancellationSource = new CancellationTokenSource();

//            if (_isOpenGLInitialized)
//            {
//                SmartLogger.Log(LogLevel.Information, LogPrefix, "OpenGL already initialized, updating position");
//                _isPositionUpdatePending = true;
//                return;
//            }

//            if (IsLoaded)
//                InitializeOpenGL();
//            else
//                SmartLogger.Log(LogLevel.Information, LogPrefix, "Control is not loaded, OpenGL will be initialized on Load event");
//        }

//        private void InitializeOpenGL()
//        {
//            if (_isOpenGLInitialized)
//            {
//                SmartLogger.Log(LogLevel.Information, LogPrefix, "OpenGL already initialized, skipping");
//                return;
//            }

//            SmartLogger.Log(LogLevel.Information, LogPrefix, $"Initializing OpenGL with size {_width}x{_height}");

//            try
//            {
//                _glWindow = new GLWindow(_width, _height, _analyzer);

//                if (_glWindow == null)
//                {
//                    SmartLogger.Log(LogLevel.Error, LogPrefix, "Failed to create GLWindow");
//                    return;
//                }

//                _glWindowHandle = _glWindow.GlfwWindow;
//                if (_glWindowHandle == IntPtr.Zero)
//                {
//                    SmartLogger.Log(LogLevel.Error, LogPrefix, "GLWindow handle is zero");
//                    _glWindow = null;
//                    return;
//                }

//                SmartLogger.Log(LogLevel.Information, LogPrefix, $"OpenGL window handle: {_glWindowHandle}, Size: {_width}x{_height}");

//                // Убедимся, что окно правильно настроено перед продолжением
//                bool configSuccess = SmartLogger.Safe(() => {
//                    ConfigureAsChildWindow();
//                    return true;
//                }, defaultValue: false, LogPrefix, "Failed to configure as child window");

//                if (!configSuccess)
//                {
//                    SmartLogger.Log(LogLevel.Error, LogPrefix, "Failed to configure OpenGL window");
//                    _glWindow = null;
//                    return;
//                }

//                SmartLogger.Safe(() => {
//                    Interop.ShowWindow(_glWindowHandle, Interop.SW_SHOW);
//                    Interop.SetWindowPos(_glWindowHandle, Interop.HWND_TOP, 0, 0, _width, _height,
//                        Interop.SWP_NOMOVE | Interop.SWP_NOSIZE | Interop.SWP_FRAMECHANGED);
//                }, LogPrefix, "Failed to show and position window");

//                _glWindow.OnRender += HandleRender;
//                _glWindow.OnSizeChanged += HandleSizeChanged;

//                SmartLogger.Log(LogLevel.Information, LogPrefix, "Setting up render timer");
//                _renderTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
//                _renderTimer.Tick += (s, e) => InvokeInvalidateVisual();
//                _renderTimer.Start();

//                _isOpenGLInitialized = _isInitialized = true;
//                SmartLogger.Log(LogLevel.Information, LogPrefix, "OpenGL initialization completed successfully");
//            }
//            catch (Exception ex)
//            {
//                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Exception during OpenGL initialization: {ex.Message}");
//                _glWindow = null;
//                _isOpenGLInitialized = _isInitialized = false;
//            }
//        }

//        private void HandleRender(TimeSpan delta)
//        {
//            if (_isDisposed) return;

//            SmartLogger.Safe(() =>
//            {
//                var dispatcher = Application.Current?.Dispatcher;
//                if (dispatcher?.CheckAccess() == true)
//                    Render(delta);
//                else
//                    dispatcher?.Invoke(() => Render(delta));
//            }, LogPrefix, "Error invoking Render event");
//        }

//        private void HandleSizeChanged(int w, int h)
//        {
//            if (_isDisposed) return;

//            SmartLogger.Safe(() =>
//            {
//                SmartLogger.Log(LogLevel.Debug, LogPrefix, $"HandleSizeChanged: {w}x{h}");
//                Application.Current?.Dispatcher?.Invoke(() =>
//                {
//                    var oldSize = new Size(_width, _height);
//                    (_width, _height) = (w, h);
//                    SizeChanged?.Invoke(this, new GLSizeChangedEventArgs(oldSize, new Size(w, h)));
//                });
//            }, LogPrefix, "Error invoking SizeChanged event");
//        }

//        public void Start(GLWpfControlSettings settings)
//        {
//            SmartLogger.Log(LogLevel.Information, LogPrefix, $"Start called with API version {settings.MajorVersion}.{settings.MinorVersion}");
//            _glSettings = settings;

//            _glWindow?.EnqueueCommand(() => SmartLogger.Safe(() =>
//            {
//                _isInitialized = true;
//                SmartLogger.Log(LogLevel.Information, LogPrefix, "OpenGL initialized flag set to true");
//            }, LogPrefix, "Failed to start OpenGL"));
//        }

//        public void InvalidateVisual() => InvokeInvalidateVisual();

//        private void InvokeInvalidateVisual() =>
//                    SmartLogger.Safe(() => base.InvalidateVisual(), LogPrefix, "Error invoking InvalidateVisual");

//        public void UpdateSize(int width, int height)
//        {
//            width = Math.Max(width, 1);
//            height = Math.Max(height, 1);

//            if (width <= 0 || height <= 0)
//            {
//                SmartLogger.Log(LogLevel.Warning, LogPrefix, $"Invalid dimensions: {width}x{height}");
//                return;
//            }

//            SmartLogger.Log(LogLevel.Information, LogPrefix, $"UpdateSize: {width}x{height}");

//            SmartLogger.Safe(() =>
//            {
//                var oldSize = new Size(_width, _height);
//                (_width, _height) = (width, height);

//                if (_glWindow != null)
//                {
//                    SmartLogger.Log(LogLevel.Debug, LogPrefix, "Setting new bounding box for GLWindow");
//                    _glWindow.SetBoundingBox(new Rect(0, 0, width, height));
//                }

//                if (oldSize.Width != width || oldSize.Height != height)
//                {
//                    SmartLogger.Log(LogLevel.Debug, LogPrefix, "Size changed, triggering SizeChanged event");
//                    SizeChanged?.Invoke(this, new GLSizeChangedEventArgs(oldSize, new Size(width, height)));
//                }
//            }, LogPrefix, "Error in UpdateSize");
//        }

//        private void UpdateWindowPosition()
//        {
//            if (_isDisposed || _glWindow == null || _glWindowHandle == IntPtr.Zero)
//                return;

//            SmartLogger.Safe(() =>
//            {
//                Point screenPoint = PointToScreen(new Point(0, 0));

//                SmartLogger.Log(LogLevel.Debug, LogPrefix, $"Updating window position to {screenPoint.X},{screenPoint.Y}");

//                Interop.SetWindowPos(_glWindowHandle, Interop.HWND_TOP,
//                    (int)screenPoint.X, (int)screenPoint.Y,
//                    _width, _height,
//                    Interop.SWP_SHOWWINDOW);

//                Interop.InvalidateRect(_glWindowHandle, IntPtr.Zero, true);
//                Interop.UpdateWindow(_glWindowHandle);
//            }, LogPrefix, "Error updating window position");
//        }

//        private void ConfigureAsChildWindow()
//        {
//            if (_parentWindowHandle == IntPtr.Zero || _glWindowHandle == IntPtr.Zero)
//            {
//                SmartLogger.Log(LogLevel.Error, LogPrefix, "Cannot configure child window: invalid handles");
//                return;
//            }

//            SmartLogger.Log(LogLevel.Information, LogPrefix, $"Configuring as child window. Parent: {_parentWindowHandle}, Child: {_glWindowHandle}");

//            SmartLogger.Safe(() =>
//            {
//                // Получаем позицию в экранных координатах
//                Point screenPoint = PointToScreen(new Point(0, 0));

//                // Устанавливаем стиль окна
//                uint style = Interop.GetWindowLong(_glWindowHandle, Interop.GWL_STYLE);
//                style = (style | Interop.WS_CHILD | Interop.WS_VISIBLE) &
//                        ~(Interop.WS_POPUP | Interop.WS_BORDER | Interop.WS_CAPTION);
//                Interop.SetWindowLong(_glWindowHandle, Interop.GWL_STYLE, style);

//                // Устанавливаем расширенный стиль
//                uint exStyle = Interop.GetWindowLong(_glWindowHandle, Interop.GWL_EXSTYLE);
//                exStyle = (exStyle | Interop.WS_EX_NOACTIVATE) &
//                          ~(Interop.WS_EX_LAYERED | Interop.WS_EX_TRANSPARENT);
//                Interop.SetWindowLong(_glWindowHandle, Interop.GWL_EXSTYLE, exStyle);

//                // Устанавливаем родителя
//                Interop.SetParent(_glWindowHandle, _parentWindowHandle);

//                // Позиционируем окно
//                Interop.SetWindowPos(_glWindowHandle, Interop.HWND_TOP,
//                    (int)screenPoint.X, (int)screenPoint.Y,
//                    _width, _height,
//                    Interop.SWP_SHOWWINDOW);

//                // Обновляем окна
//                Interop.InvalidateRect(_parentWindowHandle, IntPtr.Zero, true);
//                Interop.UpdateWindow(_parentWindowHandle);
//                Interop.InvalidateRect(_glWindowHandle, IntPtr.Zero, true);
//                Interop.UpdateWindow(_glWindowHandle);

//                SmartLogger.Log(LogLevel.Debug, LogPrefix, $"Child window configured at {screenPoint.X},{screenPoint.Y} with size {_width}x{_height}");
//            }, LogPrefix, "Error configuring as child window");
//        }

//        public void Dispose()
//        {
//            if (_isDisposed)
//            {
//                SmartLogger.Log(LogLevel.Debug, LogPrefix, "Already disposed, skipping");
//                return;
//            }

//            SmartLogger.Log(LogLevel.Information, LogPrefix, "Disposing GLHostControl");
//            _isDisposed = true;

//            if (_renderTimer != null)
//            {
//                SmartLogger.Log(LogLevel.Debug, LogPrefix, "Stopping render timer");
//                SmartLogger.Safe(() => _renderTimer.Stop(), LogPrefix, "Error stopping render timer");
//                _renderTimer = null;
//            }

//            SmartLogger.Safe(() => _cancellationSource?.Cancel(), LogPrefix, "Error cancelling operations");

//            SmartLogger.Safe(() =>
//            {
//                if (_glWindow != null)
//                {
//                    SmartLogger.Log(LogLevel.Debug, LogPrefix, "Cleaning up GLWindow");
//                    _glWindow.Cleanup();
//                    _glWindow = null;
//                }
//            }, LogPrefix, "Error disposing GLWindow");

//            SmartLogger.SafeDispose(_cancellationSource, "CancellationTokenSource");
//            _cancellationSource = null;

//            _isOpenGLInitialized = _isInitialized = false;
//            SmartLogger.Log(LogLevel.Information, LogPrefix, "GLHostControl disposed successfully");
//            GC.SuppressFinalize(this);
//        }

//        ~GLHostControl() => Dispose();
//    }

//    public unsafe class GLWindow : IDisposable
//    {
//        private readonly ConcurrentQueue<Action> _commands = new();
//        private readonly SpectrumAnalyzer? _analyzer;
//        private GameWindow? _gameWindow;
//        private bool _isDisposed;
//        private const string LogPrefix = "GLWindow";

//        public event Action<TimeSpan>? OnRender;
//        public event Action<int, int>? OnSizeChanged;

//        public IntPtr GlfwWindow => _gameWindow != null ? new IntPtr(_gameWindow.WindowPtr) : IntPtr.Zero;

//        public GLWindow(int width, int height, SpectrumAnalyzer? analyzer)
//        {
//            _analyzer = analyzer;
//            width = Math.Max(width, 10);
//            height = Math.Max(height, 10);

//            SmartLogger.Log(LogLevel.Information, LogPrefix, $"Creating OpenGL window with size: {width}x{height}");

//            var nativeSettings = new NativeWindowSettings
//            {
//                ClientSize = new Vector2i(width, height),
//                WindowBorder = WindowBorder.Hidden,
//                API = ContextAPI.OpenGL,
//                APIVersion = new Version(4, 0),
//                Profile = ContextProfile.Core,
//                Flags = ContextFlags.ForwardCompatible,
//                StartVisible = false,
//                StartFocused = false,
//                WindowState = OpenTK.Windowing.Common.WindowState.Normal
//            };

//            _gameWindow = SmartLogger.Safe(() => {
//                var window = new GameWindow(GameWindowSettings.Default, nativeSettings);
//                window.UpdateFrame += OnUpdateFrame;
//                window.RenderFrame += OnRenderFrame;
//                window.Resize += OnResize;

//                window.MakeCurrent();

//                try
//                {
//                    GL.LoadBindings(new GLFWBindingsContext());

//                    // Базовая настройка OpenGL
//                    GL.ClearColor(0.0f, 0.0f, 0.0f, 0.0f);
//                    GL.Enable(EnableCap.Blend);
//                    GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

//                    return window;
//                }
//                catch (Exception ex)
//                {
//                    SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error initializing OpenGL context: {ex.Message}");
//                    window.Dispose();
//                    throw;
//                }
//            }, defaultValue: null, LogPrefix, "Error creating GameWindow");
//        }

//        public bool MakeCurrent() =>
//            SmartLogger.Safe(() =>
//            {
//                if (_gameWindow != null)
//                {
//                    _gameWindow.MakeCurrent();
//                    return true;
//                }
//                return false;
//            }, defaultValue: false, LogPrefix, "Error making OpenGL context current");

//        public void SetBoundingBox(Rect bounds) =>
//            EnqueueCommand(() => SmartLogger.Safe(() =>
//            {
//                if (_gameWindow == null) return;

//                IntPtr hWnd = GlfwWindow;
//                Interop.MoveWindow(hWnd, (int)bounds.X, (int)bounds.Y, (int)bounds.Width, (int)bounds.Height, true);
//                _gameWindow.ClientSize = new Vector2i((int)bounds.Width, (int)bounds.Height);

//                GL.Viewport(0, 0, (int)bounds.Width, (int)bounds.Height);

//                OnSizeChanged?.Invoke((int)bounds.Width, (int)bounds.Height);
//                Interop.InvalidateRect(hWnd, IntPtr.Zero, true);
//                Interop.UpdateWindow(hWnd);

//                SmartLogger.Log(LogLevel.Debug, LogPrefix, $"Set bounding box to {bounds.X},{bounds.Y} {bounds.Width}x{bounds.Height}");
//            }, LogPrefix, "Error setting bounding box"));

//        public void Cleanup() =>
//            EnqueueCommand(() =>
//            {
//                if (_isDisposed) return;
//                _isDisposed = true;

//                SmartLogger.Safe(() =>
//                {
//                    if (_gameWindow == null) return false;

//                    _gameWindow.MakeCurrent();

//                    // Очищаем ресурсы OpenGL
//                    GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
//                    GL.BindBuffer(BufferTarget.ElementArrayBuffer, 0);
//                    GL.BindVertexArray(0);
//                    GL.UseProgram(0);

//                    _gameWindow.Close();
//                    return true;
//                }, defaultValue: false, LogPrefix, "Error during OpenGL cleanup");
//            });

//        public void EnqueueCommand(Action command)
//        {
//            if (!_isDisposed) _commands.Enqueue(command);
//        }

//        public void ProcessCommands()
//        {
//            while (_commands.TryDequeue(out Action? command))
//                SmartLogger.Safe(() => command?.Invoke(), LogPrefix, "Error executing command");
//        }

//        public void RenderFrameManual(TimeSpan delta) =>
//            SmartLogger.Safe(() =>
//            {
//                if (_gameWindow == null || _isDisposed) return;

//                _gameWindow.MakeCurrent();

//                GL.ClearColor(0.0f, 0.0f, 0.0f, 0.0f);
//                GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

//                OnRender?.Invoke(delta);

//                _gameWindow.SwapBuffers();
//                Interop.UpdateWindow(GlfwWindow);
//            }, LogPrefix, "Error in RenderFrameManual");

//        private void OnUpdateFrame(FrameEventArgs args) =>
//            SmartLogger.Safe(() => ProcessCommands(), LogPrefix, "Error in OnUpdateFrame");

//        private void OnRenderFrame(FrameEventArgs args) =>
//            SmartLogger.Safe(() =>
//            {
//                if (_isDisposed) return;

//                GL.ClearColor(0.0f, 0.0f, 0.0f, 0.0f);
//                GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

//                OnRender?.Invoke(TimeSpan.FromSeconds(args.Time));

//                _gameWindow?.SwapBuffers();
//                Interop.UpdateWindow(GlfwWindow);
//            }, LogPrefix, "Error in OnRenderFrame");

//        private void OnResize(ResizeEventArgs args) =>
//            SmartLogger.Safe(() =>
//            {
//                if (_isDisposed) return;

//                GL.Viewport(0, 0, args.Width, args.Height);
//                OnSizeChanged?.Invoke(args.Width, args.Height);

//                Interop.InvalidateRect(GlfwWindow, IntPtr.Zero, true);
//                Interop.UpdateWindow(GlfwWindow);

//                SmartLogger.Log(LogLevel.Debug, LogPrefix, $"Resized to {args.Width}x{args.Height}");
//            }, LogPrefix, "Error in OnResize");

//        public void Dispose()
//        {
//            if (!_isDisposed)
//            {
//                _isDisposed = true;
//                SmartLogger.SafeDispose(_gameWindow, "GameWindow");
//                GC.SuppressFinalize(this);
//            }
//        }
//    }

//    internal static class Interop
//    {
//        public const int GWL_STYLE = -16, GWL_EXSTYLE = -20, SW_SHOW = 5;
//        public const uint WS_CHILD = 0x40000000, WS_POPUP = 0x80000000, WS_VISIBLE = 0x10000000,
//                          WS_BORDER = 0x00800000, WS_CAPTION = 0x00C00000,
//                          WS_EX_TOOLWINDOW = 0x00000080, WS_EX_LAYERED = 0x00080000,
//                          WS_EX_TRANSPARENT = 0x00000020, WS_EX_NOACTIVATE = 0x08000000,
//                          LWA_COLORKEY = 0x00000001,
//                          SWP_NOMOVE = 0x0002, SWP_NOSIZE = 0x0001, SWP_FRAMECHANGED = 0x0020,
//                          SWP_SHOWWINDOW = 0x0040;

//        public static readonly IntPtr HWND_TOP = new(0), HWND_BOTTOM = new(1),
//                                     HWND_TOPMOST = new(-1), HWND_NOTOPMOST = new(-2);

//        [DllImport("user32.dll")] public static extern uint GetWindowLong(IntPtr hWnd, int nIndex);
//        [DllImport("user32.dll")] public static extern int SetWindowLong(IntPtr hWnd, int nIndex, uint dwNewLong);
//        [DllImport("user32.dll")] public static extern bool SetParent(IntPtr hWndChild, IntPtr hWndNewParent);
//        [DllImport("user32.dll")] public static extern bool EnableWindow(IntPtr hWnd, bool bEnable);
//        [DllImport("user32.dll")] public static extern bool SetLayeredWindowAttributes(IntPtr hWnd, uint crKey, byte bAlpha, uint dwFlags);
//        [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);
//        [DllImport("user32.dll")] public static extern bool UpdateWindow(IntPtr hWnd);
//        [DllImport("user32.dll")] public static extern bool InvalidateRect(IntPtr hWnd, IntPtr lpRect, bool bErase);
//        [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);
//        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
//        [DllImport("user32.dll", SetLastError = true)] public static extern bool ShowWindow([In] IntPtr hWnd, int nCmdShow);
//        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
//        [DllImport("user32.dll")] public static extern bool RedrawWindow(IntPtr hWnd, IntPtr lprcUpdate, IntPtr hrgnUpdate, uint flags);

//        [StructLayout(LayoutKind.Sequential)]
//        public struct RECT
//        {
//            public int left, top, right, bottom;
//        }
//    }
//}