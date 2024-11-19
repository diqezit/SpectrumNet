using SkiaSharp.Views.WPF;
using System.Windows.Interop;
using System.Windows.Threading;
using System.Windows;
using Serilog;
using System.ComponentModel;
using System.Runtime.InteropServices;

#nullable enable

namespace SpectrumNet
{
    /// <summary>
    /// Configuration options for the overlay window.
    /// </summary>
    public sealed record OverlayConfiguration
    {
        public int RenderInterval { get; init; } = 16;
        public bool IsTopmost { get; init; } = true;
        public bool ShowInTaskbar { get; init; } = false;
        public WindowStyle Style { get; init; } = WindowStyle.None;
        public WindowState State { get; init; } = WindowState.Maximized;
    }

    /// <summary>
    /// Represents a transparent overlay window that displays the spectrum visualization.
    /// Implements IDisposable pattern for proper resource cleanup.
    /// </summary>
    public sealed class OverlayWindow : Window, IDisposable
    {
        private readonly record struct RenderContext(
            MainWindow MainWindow,
            SKElement SkElement,
            DispatcherTimer RenderTimer
        );

        private readonly OverlayConfiguration _configuration;
        private readonly CancellationTokenSource _disposalTokenSource = new();
        private RenderContext? _renderContext;
        private bool _isDisposed;
        private readonly ILogger _logger;

        /// <summary>
        /// Gets a value indicating whether the window is currently initialized and ready for rendering.
        /// </summary>
        public bool IsInitialized => _renderContext != null;

        #region Construction & Initialization

        /// <summary>
        /// Initializes a new instance of the OverlayWindow class with custom configuration.
        /// </summary>
        /// <param name="mainWindow">The main application window.</param>
        /// <param name="configuration">Configuration options for the overlay.</param>
        /// <param name="logger">Logger instance for diagnostic purposes.</param>
        public OverlayWindow(
            MainWindow mainWindow,
            OverlayConfiguration? configuration = null,
            ILogger? logger = null)
        {
            ArgumentNullException.ThrowIfNull(mainWindow, nameof(mainWindow));

            _configuration = configuration ?? new OverlayConfiguration();
            _logger = logger ?? Log.Logger.ForContext<OverlayWindow>();

            try
            {
                InitializeOverlay(mainWindow);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to initialize overlay window");
                throw new InvalidOperationException("Failed to initialize overlay window", ex);
            }
        }

        private void InitializeOverlay(MainWindow mainWindow)
        {
            ConfigureWindowProperties();

            var skElement = CreateSkElement();
            var renderTimer = CreateRenderTimer();

            _renderContext = new RenderContext(
                MainWindow: mainWindow,
                SkElement: skElement,
                RenderTimer: renderTimer
            );

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

            // Enable Windows DWM transparency
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var backdrop = new SystemBackdrop();
                backdrop.SetTransparentBackground(this);
            }
        }

        private SKElement CreateSkElement() => new()
        {
            VerticalAlignment = VerticalAlignment.Stretch,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        private DispatcherTimer CreateRenderTimer() => new(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(_configuration.RenderInterval)
        };

        #endregion

        #region Event Handling

        private void SubscribeToEvents()
        {
            if (_renderContext is null) return;

            _renderContext.Value.SkElement.PaintSurface += HandlePaintSurface;
            _renderContext.Value.RenderTimer.Tick += HandleRenderTick;

            Closing += OnClosing;
            SourceInitialized += OnSourceInitialized;
            KeyDown += OnKeyDown;

            // Handle DPI changes
            DpiChanged += OnDpiChanged;
        }

        private void UnsubscribeFromEvents()
        {
            if (_renderContext is null) return;

            _renderContext.Value.SkElement.PaintSurface -= HandlePaintSurface;
            _renderContext.Value.RenderTimer.Tick -= HandleRenderTick;

            Closing -= OnClosing;
            SourceInitialized -= OnSourceInitialized;
            KeyDown -= OnKeyDown;
            DpiChanged -= OnDpiChanged;
        }

        private void HandleRenderTick(object? sender, EventArgs e)
        {
            if (_isDisposed || _renderContext is null) return;
            _renderContext.Value.SkElement.InvalidateVisual();
        }

        private void HandlePaintSurface(object? sender, SkiaSharp.Views.Desktop.SKPaintSurfaceEventArgs args)
        {
            if (_isDisposed || _renderContext is null) return;

            try
            {
                _renderContext.Value.MainWindow.OnPaintSurface(sender, args);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error during surface painting");
                // Consider implementing a fallback rendering mechanism
            }
        }

        private void OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Escape)
            {
                Close();
                e.Handled = true;
            }
        }

        private void OnClosing(object? sender, CancelEventArgs e)
        {
            if (_renderContext?.RenderTimer is not null)
            {
                _renderContext.Value.RenderTimer.Stop();
            }

            Dispose();
        }

        private void OnSourceInitialized(object? sender, EventArgs e)
        {
            if (_isDisposed) return;

            try
            {
                ConfigureWindowStyleEx();
                StartRendering();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to initialize window source");
                Close();
            }
        }

        private void OnDpiChanged(object? sender, DpiChangedEventArgs e)
        {
            // Adjust rendering for new DPI settings
            if (_renderContext?.SkElement is not null)
            {
                _renderContext.Value.SkElement.InvalidateVisual();
            }
        }

        #endregion

        #region Window Configuration

        private void ConfigureWindowStyleEx()
        {
            var helper = new WindowInteropHelper(this);
            var hwnd = helper.Handle;

            if (hwnd == IntPtr.Zero)
            {
                throw new InvalidOperationException("Window handle not initialized");
            }

            var extendedStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
            _ = NativeMethods.SetWindowLong(
                hwnd,
                NativeMethods.GWL_EXSTYLE,
                extendedStyle | NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_LAYERED
            );
        }

        private void StartRendering()
        {
            if (_renderContext?.RenderTimer is not null && !_isDisposed)
            {
                _renderContext.Value.RenderTimer.Start();
            }
        }

        #endregion

        #region IDisposable Implementation

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (!disposing || _isDisposed) return;

            _isDisposed = true;
            _disposalTokenSource.Cancel();

            UnsubscribeFromEvents();

            if (_renderContext is not null)
            {
                _renderContext.Value.RenderTimer.Stop();
                _renderContext = null;
            }

            _disposalTokenSource.Dispose();
            _logger.Debug("Overlay window disposed successfully");
        }

        #endregion
    }

    /// <summary>
    /// Helper class for Windows-specific backdrop configuration.
    /// </summary>
    internal sealed class SystemBackdrop
    {
        public void SetTransparentBackground(Window window)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

            var windowHelper = new WindowInteropHelper(window);
            var hwnd = windowHelper.Handle;

            if (hwnd != IntPtr.Zero)
            {
                // Настройка прозрачности окна
                var accent = new AccentPolicy
                {
                    AccentState = AccentState.ACCENT_ENABLE_TRANSPARENTGRADIENT,
                    GradientColor = 0x00000000
                };

                var accentStructSize = Marshal.SizeOf(accent);
                var accentPtr = Marshal.AllocHGlobal(accentStructSize);

                try
                {
                    Marshal.StructureToPtr(accent, accentPtr, false);
                    var data = new WindowCompositionAttributeData
                    {
                        Attribute = WindowCompositionAttribute.WCA_ACCENT_POLICY,
                        SizeOfData = accentStructSize,
                        Data = accentPtr
                    };

                    // Создаем экземпляр NativeMethods.WINDOWCOMPOSITIONATTRIBDATA
                    var nativeData = new NativeMethods.WINDOWCOMPOSITIONATTRIBDATA
                    {
                        Attrib = (NativeMethods.WINDOWCOMPOSITIONATTRIB)data.Attribute,
                        pvData = data.Data,
                        cbData = data.SizeOfData
                    };

                    // Передаем nativeData в SetWindowCompositionAttribute
                    _ = NativeMethods.SetWindowCompositionAttribute(hwnd, ref nativeData);
                }
                finally
                {
                    Marshal.FreeHGlobal(accentPtr);
                }
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct AccentPolicy
        {
            public AccentState AccentState;
            public int AccentFlags;
            public int GradientColor;
            public int AnimationId;
        }

        private enum AccentState
        {
            ACCENT_DISABLED = 0,
            ACCENT_ENABLE_TRANSPARENTGRADIENT = 2,
            ACCENT_ENABLE_BLURBEHIND = 3,
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WindowCompositionAttributeData
        {
            public WindowCompositionAttribute Attribute;
            public IntPtr Data;
            public int SizeOfData;
        }

        private enum WindowCompositionAttribute
        {
            WCA_ACCENT_POLICY = 19
        }
    }
}