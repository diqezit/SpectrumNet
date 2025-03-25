#nullable enable
using static SpectrumNet.SmartLogger;

namespace SpectrumNet
{
    public sealed record OverlayConfiguration(
        int RenderInterval = 16,
        bool IsTopmost = true,
        bool ShowInTaskbar = false,
        WindowStyle Style = WindowStyle.None,
        WindowState State = WindowState.Maximized,
        bool EnableEscapeToClose = true,
        bool EnableHardwareAcceleration = true
    );

    public sealed class OverlayWindow : Window, IDisposable
    {
        private const string LogSource = "OverlayWindow";

        private readonly record struct RenderContext(
            IAudioVisualizationController Controller,
            SKElement SkElement, // Заменяем SKGLElement на SKElement
            DispatcherTimer RenderTimer);

        private readonly OverlayConfiguration _configuration;
        private readonly CancellationTokenSource _disposalTokenSource = new();
        private RenderContext? _renderContext;
        private bool _isDisposed;
        private SKBitmap? _cacheBitmap;
        private readonly SemaphoreSlim _renderLock = new(1, 1);
        private readonly Stopwatch _frameTimeWatch = new();

        public new bool IsInitialized => _renderContext != null && !_isDisposed;

        public OverlayWindow(IAudioVisualizationController controller, OverlayConfiguration? configuration = null)
        {
            if (controller == null) throw new ArgumentNullException(nameof(controller));
            _configuration = configuration ?? new();

            try
            {
                if (_configuration.EnableHardwareAcceleration)
                {
                    RenderOptions.ProcessRenderMode = RenderMode.Default;
                    SetValue(RenderOptions.BitmapScalingModeProperty, BitmapScalingMode.NearestNeighbor);
                }

                InitializeOverlay(controller);
                _frameTimeWatch.Start();
            }
            catch (Exception ex)
            {
                Error(LogSource, "Failed to initialize overlay window", ex);
                throw;
            }
        }

        public void ForceRedraw()
        {
            if (!_isDisposed && _renderContext != null && _renderLock.Wait(0))
            {
                try { _renderContext.Value.SkElement.InvalidateVisual(); }
                finally { _renderLock.Release(); }
            }
        }

        private void InitializeOverlay(IAudioVisualizationController controller)
        {
            ConfigureWindowProperties();

            var skElement = new SKElement // Заменяем SKGLElement на SKElement
            {
                VerticalAlignment = System.Windows.VerticalAlignment.Stretch,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
                SnapsToDevicePixels = true,
                UseLayoutRounding = true
            };

            var renderTimer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(_configuration.RenderInterval)
            };

            _renderContext = new(controller, skElement, renderTimer);
            Content = skElement;

            SubscribeToEvents();
        }

        private void ConfigureWindowProperties()
        {
            WindowStyle = _configuration.Style;
            AllowsTransparency = true;
            Background = null;
            Topmost = _configuration.IsTopmost;
            WindowState = _configuration.State;
            ShowInTaskbar = _configuration.ShowInTaskbar;
            ResizeMode = ResizeMode.NoResize;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                new SystemBackdrop().SetTransparentBackground(this);
        }

        private void SubscribeToEvents()
        {
            if (_renderContext is null) return;

            _renderContext.Value.SkElement.PaintSurface += HandlePaintSurface;
            _renderContext.Value.RenderTimer.Tick += RenderTimerTick;

            Closing += OnClosing;
            SourceInitialized += OnSourceInitialized;

            if (_configuration.EnableEscapeToClose)
            {
                KeyDown += OnKeyDown;
            }

            DpiChanged += OnDpiChanged;
            IsVisibleChanged += OnIsVisibleChanged;
            SizeChanged += OnSizeChanged;
        }

        private void RenderTimerTick(object? sender, EventArgs e) => ForceRedraw();

        private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            _renderContext?.RenderTimer.Stop();
            Dispose();
        }

        private void OnSourceInitialized(object? sender, EventArgs e)
        {
            ConfigureWindowStyleEx();
            _renderContext?.RenderTimer.Start();
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                Close();
            }
        }

        private void OnDpiChanged(object? sender, DpiChangedEventArgs e)
        {
            _cacheBitmap?.Dispose();
            _cacheBitmap = null;
            ForceRedraw();
        }

        private void OnIsVisibleChanged(object? sender, DependencyPropertyChangedEventArgs e)
        {
            if (IsVisible) _renderContext?.RenderTimer.Start();
            else _renderContext?.RenderTimer.Stop();
        }

        private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
        {
            _cacheBitmap?.Dispose();
            _cacheBitmap = null;
        }

        private void HandlePaintSurface(object? sender, SKPaintSurfaceEventArgs args) // Заменяем SKPaintGLSurfaceEventArgs на SKPaintSurfaceEventArgs
        {
            if (_isDisposed || _renderContext is null || !_renderLock.Wait(0)) return;

            try
            {
                _frameTimeWatch.Restart();

                var info = args.Info;
                var canvas = args.Surface.Canvas;
                canvas.Clear(SKColors.Transparent);

                if (_frameTimeWatch.ElapsedMilliseconds > _configuration.RenderInterval * 2)
                {
                    _renderContext.Value.Controller.OnPaintSurface(sender, args);
                    return;
                }

                if (_configuration.EnableHardwareAcceleration)
                {
                    // Прямой рендеринг без кэширования
                    _renderContext.Value.Controller.OnPaintSurface(sender, args);
                }
                else
                {
                    // Кэширование для случаев, когда аппаратное ускорение отключено
                    if (_cacheBitmap == null || _cacheBitmap.Width != info.Width || _cacheBitmap.Height != info.Height)
                    {
                        _cacheBitmap?.Dispose();
                        _cacheBitmap = new SKBitmap(info.Width, info.Height, info.ColorType, info.AlphaType);
                    }

                    using (var tempSurface = SKSurface.Create(info, _cacheBitmap.GetPixels(), _cacheBitmap.RowBytes))
                    {
                        tempSurface.Canvas.Clear(SKColors.Transparent);
                        _renderContext.Value.Controller.OnPaintSurface(sender, args);
                    }

                    canvas.DrawBitmap(_cacheBitmap, 0, 0);
                }
            }
            catch (Exception ex)
            {
                Error(LogSource, "Error during paint surface handling", ex);
            }
            finally { _renderLock.Release(); }
        }

        private void ConfigureWindowStyleEx()
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;

            var extendedStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
            _ = NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE,
                extendedStyle | NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_LAYERED);
        }

        public void Dispose()
        {
            if (_isDisposed) return;

            _isDisposed = true;

            if (_renderContext != null)
            {
                _renderContext.Value.SkElement.PaintSurface -= HandlePaintSurface;
                _renderContext.Value.RenderTimer.Tick -= RenderTimerTick;
                _renderContext.Value.RenderTimer.Stop();

                Closing -= OnClosing;
                SourceInitialized -= OnSourceInitialized;

                if (_configuration.EnableEscapeToClose)
                {
                    KeyDown -= OnKeyDown;
                }

                DpiChanged -= OnDpiChanged;
                IsVisibleChanged -= OnIsVisibleChanged;
                SizeChanged -= OnSizeChanged;
            }

            _disposalTokenSource.Cancel();
            _disposalTokenSource.Dispose();
            _cacheBitmap?.Dispose();
            _renderLock.Dispose();
            _renderContext = null;
        }
    }

    internal sealed class SystemBackdrop
    {
        public void SetTransparentBackground(Window window)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;

            var accent = new NativeMethods.ACCENT_POLICY { nAccentState = 2, nColor = 0x00000000 };
            var accentStructSize = Marshal.SizeOf(accent);
            var accentPtr = Marshal.AllocHGlobal(accentStructSize);

            try
            {
                Marshal.StructureToPtr(accent, accentPtr, false);
                var nativeData = new NativeMethods.WINDOWCOMPOSITIONATTRIBDATA
                {
                    Attrib = NativeMethods.WINDOWCOMPOSITIONATTRIB.WCA_ACCENT_POLICY,
                    pvData = accentPtr,
                    cbData = accentStructSize
                };
                _ = NativeMethods.SetWindowCompositionAttribute(hwnd, ref nativeData);
            }
            finally { Marshal.FreeHGlobal(accentPtr); }
        }
    }

    internal static class NativeMethods
    {
        public const int GWL_EXSTYLE = -20;
        public const int WS_EX_TRANSPARENT = 0x00000020;
        public const int WS_EX_LAYERED = 0x00080000;

        [DllImport("user32.dll", SetLastError = true)]
        public static extern int GetWindowLong(IntPtr hwnd, int index);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WINDOWCOMPOSITIONATTRIBDATA data);

        public enum WINDOWCOMPOSITIONATTRIB { WCA_ACCENT_POLICY = 19 }

        [StructLayout(LayoutKind.Sequential)]
        public struct WINDOWCOMPOSITIONATTRIBDATA
        {
            public WINDOWCOMPOSITIONATTRIB Attrib;
            public IntPtr pvData;
            public int cbData;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct ACCENT_POLICY
        {
            public int nAccentState;
            public int nFlags;
            public int nColor;
            public int nAnimationId;
        }
    }
}