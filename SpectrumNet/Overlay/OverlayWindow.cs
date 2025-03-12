#nullable enable

using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfWindow = System.Windows.Window;
using WpfWindowState = System.Windows.WindowState;
using WpfWindowStyle = System.Windows.WindowStyle;

namespace SpectrumNet
{
    public sealed record OverlayConfiguration(
        int RenderInterval = 16,
        bool IsTopmost = true,
        bool ShowInTaskbar = false,
        WpfWindowStyle Style = WpfWindowStyle.None,
        WpfWindowState State = WpfWindowState.Normal,
        bool EnableEscapeToClose = true,
        bool EnableHardwareAcceleration = true
    );

    public sealed class OverlayWindow : WpfWindow, IDisposable
    {
        private readonly MainWindow _mainWindow;
        private readonly OverlayConfiguration _configuration;
        private readonly CancellationTokenSource _disposalTokenSource = new();
        private bool _isDisposed, _isGlInitialized;
        private IntPtr _hwnd;
        private HwndSource? _hwndSource;
        private GLWpfControl? _glControl;
        private DispatcherTimer? _renderTimer;
        private SpectrumPlaceholder? _overlayPlaceholder;

        public new bool IsInitialized => _glControl != null && !_isDisposed && _isGlInitialized;

        public OverlayWindow(MainWindow mainWindow, OverlayConfiguration? configuration = null)
        {
            _mainWindow = mainWindow ?? throw new ArgumentNullException(nameof(mainWindow));
            _configuration = configuration ?? new();

            SmartLogger.Safe(() =>
            {
                if (_configuration.EnableHardwareAcceleration)
                {
                    RenderOptions.ProcessRenderMode = RenderMode.Default;
                    this.SetValue(RenderOptions.BitmapScalingModeProperty, BitmapScalingMode.NearestNeighbor);
                }
                InitializeOverlay();
            }, "OverlayWindow", "Failed to initialize overlay window");
        }

        private void InitializeOverlay()
        {
            ConfigureWindowProperties();
            InitializeGlControl();
            InitializeRenderTimer();
            SubscribeToEvents();
        }

        private void InitializeGlControl()
        {
            GLWpfControl _glControl = new GLWpfControl
            {
                VerticalAlignment = System.Windows.VerticalAlignment.Stretch,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
                SnapsToDevicePixels = true,
                UseLayoutRounding = true
            };
            this.Content = _glControl;
        }

        private void InitializeRenderTimer()
        {
            _renderTimer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(_configuration.RenderInterval)
            };
            _renderTimer.Tick += RenderTimerTick;
        }

        private void ConfigureWindowProperties()
        {
            this.WindowStyle = _configuration.Style;
            this.AllowsTransparency = false;
            this.Background = System.Windows.Media.Brushes.Black;
            this.Topmost = _configuration.IsTopmost;
            this.WindowState = _configuration.State;
            this.ShowInTaskbar = _configuration.ShowInTaskbar;
            this.ResizeMode = ResizeMode.NoResize;
        }

        private void SubscribeToEvents()
        {
            this.Closing += OnClosing;
            this.SourceInitialized += OnSourceInitialized;
            this.Loaded += OnWindowLoaded;
            this.SizeChanged += OnSizeChanged;

            if (_configuration.EnableEscapeToClose)
                this.KeyDown += OnKeyDown;
        }

        private void OnWindowLoaded(object sender, RoutedEventArgs e) =>
            SmartLogger.Safe(() =>
            {
                SetFullscreenDimensions();
                InitializeOpenGL();
            }, "OverlayWindow", "Error in OnWindowLoaded");

        private void SetFullscreenDimensions()
        {
            WindowState = WpfWindowState.Normal;
            Left = 0;
            Top = 0;
            Width = SystemParameters.PrimaryScreenWidth;
            Height = SystemParameters.PrimaryScreenHeight;
        }

        private void InitializeOpenGL()
        {
            if (_glControl == null || _isGlInitialized) return;

            _glControl.Start(_mainWindow.GlSettings);
            _glControl.Render += OnGlControlRender;
            _isGlInitialized = true;

            _overlayPlaceholder = SmartLogger.Safe<SpectrumPlaceholder>(
                () => new SpectrumPlaceholder(new OpenGLService()),
                defaultValue: null
            );

            if (_overlayPlaceholder != null)
                SmartLogger.Safe(() => _overlayPlaceholder.UpdateDimensions((float)Width, (float)Height));

            _renderTimer?.Start();
            _mainWindow.IsOverlayActive = true;
        }

        private void OnGlControlRender(TimeSpan delta)
        {
            if (_glControl == null || !_isGlInitialized) return;

            SmartLogger.Safe(() =>
            {
                GL.ClearColor(0, 0, 0, 1.0f);
                GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
                GL.Viewport(0, 0, (int)_glControl.ActualWidth, (int)_glControl.ActualHeight);

                var hasValidDimensions = _glControl.ActualWidth > 0 && _glControl.ActualHeight > 0;

                if (_overlayPlaceholder != null && hasValidDimensions)
                    _overlayPlaceholder.UpdateDimensions((float)_glControl.ActualWidth, (float)_glControl.ActualHeight);

                if (_overlayPlaceholder != null)
                {
                    bool renderSuccess = SmartLogger.Safe(() =>
                    {
                        _overlayPlaceholder.Render();
                        return true;
                    }, defaultValue: false);

                    if (!renderSuccess)
                        SetErrorBackgroundColor();
                }
                else
                    SetErrorBackgroundColor();
            });
        }

        private void SetErrorBackgroundColor()
        {
            GL.ClearColor(0.1f, 0.1f, 0.1f, 1.0f);
            GL.Clear(ClearBufferMask.ColorBufferBit);
        }

        private void RenderTimerTick(object? sender, EventArgs e)
        {
            if (_isGlInitialized && _glControl != null)
                _glControl.InvalidateVisual();
        }

        private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            CleanupOpenGLResources();
            StopAndCleanupTimer();
            UnsubscribeGlControlEvents();
            _mainWindow.IsOverlayActive = false;
            Dispose();
        }

        private void StopAndCleanupTimer()
        {
            if (_renderTimer != null)
            {
                _renderTimer.Stop();
                _renderTimer.Tick -= RenderTimerTick;
            }
        }

        private void UnsubscribeGlControlEvents()
        {
            if (_glControl != null)
                _glControl.Render -= OnGlControlRender;
        }

        private void CleanupOpenGLResources()
        {
            if (_overlayPlaceholder != null)
            {
                SmartLogger.SafeDispose(_overlayPlaceholder, "OverlayPlaceholder");
                _overlayPlaceholder = null;
            }
        }

        private void OnSourceInitialized(object? sender, EventArgs e)
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            _hwndSource = HwndSource.FromHwnd(_hwnd);

            if (_hwndSource != null)
                _hwndSource.AddHook(WndProc);

            ConfigureWindowStyleEx();
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_MOUSEACTIVATE = 0x0021;
            const int MA_NOACTIVATE = 3;
            const int WM_SETFOCUS = 0x0007;

            switch (msg)
            {
                case WM_MOUSEACTIVATE:
                    handled = true;
                    return (IntPtr)MA_NOACTIVATE;

                case WM_SETFOCUS:
                    ReturnFocusToMainWindow();
                    handled = true;
                    return IntPtr.Zero;

                default:
                    return IntPtr.Zero;
            }
        }

        private void ReturnFocusToMainWindow()
        {
            IntPtr mainWindowHandle = new WindowInteropHelper(_mainWindow).Handle;
            if (mainWindowHandle != IntPtr.Zero)
                NativeMethods.SetForegroundWindow(mainWindowHandle);
        }

        private void OnKeyDown(object sender, WpfKeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                this.Close();
            }
        }

        private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
        {
            if (_glControl == null || !_isGlInitialized || _overlayPlaceholder == null ||
                e.NewSize.Width <= 0 || e.NewSize.Height <= 0)
                return;

            SmartLogger.Safe(() =>
            {
                _overlayPlaceholder.UpdateDimensions((float)e.NewSize.Width, (float)e.NewSize.Height);
                _glControl.InvalidateVisual();
            }, "OverlayWindow", "Failed to update dimensions");
        }

        private void ConfigureWindowStyleEx()
        {
            if (_hwnd == IntPtr.Zero) return;

            var extendedStyle = NativeMethods.GetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE);
            _ = NativeMethods.SetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE,
                extendedStyle | NativeMethods.WS_EX_NOACTIVATE);
            RegisterHotKey();
        }

        private void RegisterHotKey()
        {
            const int HOTKEY_ID = 9000;
            const int MOD_NONE = 0x0000;
            const int VK_ESCAPE = 0x1B;

            NativeMethods.RegisterHotKey(_hwnd, HOTKEY_ID, MOD_NONE, VK_ESCAPE);

            if (_hwndSource != null)
            {
                _hwndSource.RemoveHook(WndProc);
                _hwndSource.AddHook(WndProcWithHotkey);
            }
        }

        private IntPtr WndProcWithHotkey(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_HOTKEY = 0x0312;
            const int HOTKEY_ID = 9000;
            const int WM_MOUSEACTIVATE = 0x0021;
            const int MA_NOACTIVATE = 3;
            const int WM_SETFOCUS = 0x0007;

            switch (msg)
            {
                case WM_HOTKEY when wParam.ToInt32() == HOTKEY_ID:
                    Close();
                    handled = true;
                    return IntPtr.Zero;

                case WM_MOUSEACTIVATE:
                    handled = true;
                    return (IntPtr)MA_NOACTIVATE;

                case WM_SETFOCUS:
                    ReturnFocusToMainWindow();
                    handled = true;
                    return IntPtr.Zero;

                default:
                    return IntPtr.Zero;
            }
        }

        private void UnregisterHotKey()
        {
            const int HOTKEY_ID = 9000;
            if (_hwnd != IntPtr.Zero)
                NativeMethods.UnregisterHotKey(_hwnd, HOTKEY_ID);
        }

        public void Dispose()
        {
            if (_isDisposed) return;

            _isDisposed = true;
            UnregisterHotKey();
            UnregisterWindowHooks();
            UnsubscribeEvents();
            ReleaseResources();
        }

        private void UnregisterWindowHooks()
        {
            if (_hwndSource != null)
            {
                _hwndSource.RemoveHook(WndProc);
                SmartLogger.SafeDispose(_hwndSource, "HwndSource");
                _hwndSource = null;
            }
        }

        private void UnsubscribeEvents()
        {
            this.Closing -= OnClosing;
            this.SourceInitialized -= OnSourceInitialized;
            this.Loaded -= OnWindowLoaded;
            this.SizeChanged -= OnSizeChanged;

            if (_configuration.EnableEscapeToClose)
                this.KeyDown -= OnKeyDown;
        }

        private void ReleaseResources()
        {
            SmartLogger.SafeDispose(_disposalTokenSource, "DisposalTokenSource");

            if (_overlayPlaceholder != null)
            {
                SmartLogger.SafeDispose(_overlayPlaceholder, "OverlayPlaceholder");
                _overlayPlaceholder = null;
            }

            _glControl = null;
            _renderTimer = null;
            _isGlInitialized = false;
        }
    }

    internal static class NativeMethods
    {
        public const int GWL_EXSTYLE = -20;
        public const int WS_EX_NOACTIVATE = 0x08000000;

        [DllImport("user32.dll", SetLastError = true)]
        public static extern int GetWindowLong(IntPtr hwnd, int index);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    }
}