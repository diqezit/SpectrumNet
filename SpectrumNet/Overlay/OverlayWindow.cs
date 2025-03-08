#nullable enable

using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using OpenTK.Wpf;
using WpfWindow = System.Windows.Window;
using WpfWindowState = System.Windows.WindowState;
using WpfWindowStyle = System.Windows.WindowStyle;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfDpiChangedEventArgs = System.Windows.DpiChangedEventArgs;

namespace SpectrumNet
{
    public sealed record OverlayConfiguration(
        int RenderInterval = 16,
        bool IsTopmost = true,
        bool ShowInTaskbar = false,
        WpfWindowStyle Style = WpfWindowStyle.None,
        WpfWindowState State = WpfWindowState.Maximized,
        bool EnableEscapeToClose = true,
        bool EnableHardwareAcceleration = true
    );

    public sealed class OverlayWindow : WpfWindow, IDisposable
    {
        private readonly record struct RenderContext(MainWindow MainWindow, GLWpfControl GlControl, DispatcherTimer RenderTimer);

        private readonly OverlayConfiguration _configuration;
        private readonly CancellationTokenSource _disposalTokenSource = new();
        private RenderContext? _renderContext;
        private bool _isDisposed;

        public new bool IsInitialized => _renderContext != null && !_isDisposed;

        public OverlayWindow(MainWindow mainWindow, OverlayConfiguration? configuration = null)
        {
            if (mainWindow == null) throw new ArgumentNullException(nameof(mainWindow));
            _configuration = configuration ?? new();

            try
            {
                if (_configuration.EnableHardwareAcceleration)
                {
                    RenderOptions.ProcessRenderMode = RenderMode.Default;
                    this.SetValue(RenderOptions.BitmapScalingModeProperty, BitmapScalingMode.NearestNeighbor);
                }

                InitializeOverlay(mainWindow);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to initialize overlay window");
                throw;
            }
        }

        public void ForceRedraw()
        {
            if (!_isDisposed && _renderContext != null)
            {
                _renderContext.Value.GlControl.InvalidateVisual();
            }
        }

        private void InitializeOverlay(MainWindow mainWindow)
        {
            ConfigureWindowProperties();

            var glControl = new GLWpfControl
            {
                VerticalAlignment = VerticalAlignment.Stretch,
                HorizontalAlignment = WpfHorizontalAlignment.Stretch,
                SnapsToDevicePixels = true,
                UseLayoutRounding = true
            };

            var renderTimer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(_configuration.RenderInterval)
            };

            _renderContext = new(mainWindow, glControl, renderTimer);
            this.Content = glControl;

            SubscribeToEvents();

            glControl.Start(mainWindow.GlSettings);
            glControl.Render += (delta) => _renderContext?.MainWindow.Renderer?.RenderFrame(delta);
        }

        private void ConfigureWindowProperties()
        {
            this.WindowStyle = _configuration.Style;
            this.AllowsTransparency = true;
            this.Background = System.Windows.Media.Brushes.Transparent;
            this.Topmost = _configuration.IsTopmost;
            this.WindowState = _configuration.State;
            this.ShowInTaskbar = _configuration.ShowInTaskbar;
            this.ResizeMode = ResizeMode.NoResize;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                new SystemBackdrop().SetTransparentBackground(this);
        }

        private void SubscribeToEvents()
        {
            if (_renderContext is null) return;

            _renderContext.Value.RenderTimer.Tick += RenderTimerTick;

            this.Closing += OnClosing;
            this.SourceInitialized += OnSourceInitialized;

            if (_configuration.EnableEscapeToClose)
            {
                this.KeyDown += OnKeyDown;
            }

            this.DpiChanged += OnDpiChanged;
            this.IsVisibleChanged += OnIsVisibleChanged;
            this.SizeChanged += OnSizeChanged;
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

        private void OnKeyDown(object sender, WpfKeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                this.Close();
            }
        }

        private void OnDpiChanged(object? sender, WpfDpiChangedEventArgs e)
        {
            _renderContext?.GlControl.InvalidateVisual();
        }

        private void OnIsVisibleChanged(object? sender, DependencyPropertyChangedEventArgs e)
        {
            if (this.IsVisible) _renderContext?.RenderTimer.Start();
            else _renderContext?.RenderTimer.Stop();
        }

        private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
        {
            if (_renderContext != null)
            {
                _renderContext.Value.MainWindow.Renderer?.UpdateRenderDimensions((int)e.NewSize.Width, (int)e.NewSize.Height);
                _renderContext.Value.GlControl.InvalidateVisual();
            }
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
                _renderContext.Value.RenderTimer.Tick -= RenderTimerTick;
                _renderContext.Value.RenderTimer.Stop();

                this.Closing -= OnClosing;
                this.SourceInitialized -= OnSourceInitialized;

                if (_configuration.EnableEscapeToClose)
                {
                    this.KeyDown -= OnKeyDown;
                }

                this.DpiChanged -= OnDpiChanged;
                this.IsVisibleChanged -= OnIsVisibleChanged;
                this.SizeChanged -= OnSizeChanged;
            }

            _disposalTokenSource.Cancel();
            _disposalTokenSource.Dispose();
            _renderContext = null;
        }
    }

    internal sealed class SystemBackdrop
    {
        public void SetTransparentBackground(WpfWindow window)
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