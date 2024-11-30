#nullable enable

namespace SpectrumNet
{
    public sealed record OverlayConfiguration(
        int RenderInterval = 16,
        bool IsTopmost = true,
        bool ShowInTaskbar = false,
        WindowStyle Style = WindowStyle.None,
        WindowState State = WindowState.Maximized
    );

    public sealed class OverlayWindow : Window, IDisposable
    {
        private record struct RenderContext(MainWindow MainWindow, SKElement SkElement, DispatcherTimer RenderTimer);

        private readonly OverlayConfiguration _configuration;
        private readonly CancellationTokenSource _disposalTokenSource = new();
        private RenderContext? _renderContext;
        private bool _isDisposed;

        public bool IsInitialized => _renderContext != null;

        public OverlayWindow(MainWindow mainWindow, OverlayConfiguration? configuration = null)
        {
            if (mainWindow == null) throw new ArgumentNullException(nameof(mainWindow));
            _configuration = configuration ?? new();
            InitializeOverlay(mainWindow);
        }

        private void InitializeOverlay(MainWindow mainWindow)
        {
            ConfigureWindowProperties();
            var skElement = new SKElement { VerticalAlignment = VerticalAlignment.Stretch, HorizontalAlignment = HorizontalAlignment.Stretch };
            var renderTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(_configuration.RenderInterval) };
            _renderContext = new(mainWindow, skElement, renderTimer);
            Content = skElement;
            SubscribeToEvents();
            Log.Information("[OverlayWindow] Overlay window initialized");
        }

        private void ConfigureWindowProperties()
        {
            (WindowStyle, AllowsTransparency, Background, Topmost, WindowState, ShowInTaskbar, ResizeMode) =
                (_configuration.Style, true, null, _configuration.IsTopmost, _configuration.State, _configuration.ShowInTaskbar, ResizeMode.NoResize);
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                new SystemBackdrop().SetTransparentBackground(this);
                Log.Debug("[OverlayWindow] Transparent background set for Windows");
            }
        }

        private void SubscribeToEvents()
        {
            if (_renderContext is null)
            {
                Log.Error("[OverlayWindow] Failed to subscribe to events: RenderContext is null");
                return;
            }
            _renderContext.Value.SkElement.PaintSurface += HandlePaintSurface;
            _renderContext.Value.RenderTimer.Tick += (_, _) => _renderContext?.SkElement.InvalidateVisual();
            Closing += (_, _) =>
            {
                _renderContext?.RenderTimer.Stop();
                Dispose();
                Log.Information("[OverlayWindow] Overlay window closing");
            };
            SourceInitialized += (_, _) =>
            {
                ConfigureWindowStyleEx();
                _renderContext?.RenderTimer.Start();
                Log.Information("[OverlayWindow] Overlay window source initialized");
            };
            KeyDown += (_, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Escape)
                {
                    Close();
                    e.Handled = true;
                    Log.Information("[OverlayWindow] Overlay window closed by Escape key");
                }
            };
            DpiChanged += (_, _) => _renderContext?.SkElement.InvalidateVisual();
        }

        private void HandlePaintSurface(object? sender, SKPaintSurfaceEventArgs args)
        {
            if (_isDisposed)
            {
                Log.Warning("[OverlayWindow] Attempted to paint surface after disposal");
                return;
            }
            if (_renderContext is null)
            {
                Log.Error("[OverlayWindow] Failed to handle paint surface: RenderContext is null");
                return;
            }
            _renderContext.Value.MainWindow.OnPaintSurface(sender, args);
        }

        private void ConfigureWindowStyleEx()
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero)
            {
                Log.Error("[OverlayWindow] Failed to configure window style: Invalid window handle");
                return;
            }
            var extendedStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
            _ = NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, extendedStyle | NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_LAYERED);
            Log.Debug("[OverlayWindow] Window style configured");
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            _disposalTokenSource.Cancel();
            _renderContext?.RenderTimer.Stop();
            _renderContext = null;
            _disposalTokenSource.Dispose();
            Log.Information("[OverlayWindow] Overlay window disposed");
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

        [DllImport("user32.dll")]
        public static extern int GetWindowLong(IntPtr hwnd, int index);

        [DllImport("user32.dll")]
        public static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

        [DllImport("user32.dll")]
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