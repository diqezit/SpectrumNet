#nullable enable

namespace SpectrumNet.Controllers.RenderCore;

public sealed class OverlayWindow : Window, IDisposable
{
    private const string LogPrefix = nameof(OverlayWindow);
    private readonly ISmartLogger _logger = Instance;

    private readonly record struct RenderContext(
        IMainController Controller,
        SKElement SkElement,
        IRendererFactory RendererFactory);

    private readonly OverlayConfiguration _configuration;
    private readonly CancellationTokenSource _disposalTokenSource = new();
    private RenderContext? _renderContext;
    private bool _isDisposed;

    private readonly FpsLimiter _fpsLimiter = FpsLimiter.Instance;
    private readonly Stopwatch _frameTimeWatch = new();
    private readonly ITransparencyManager _transparencyManager;
    private readonly IPerformanceMetricsManager _performanceMetricsManager = PerformanceMetricsManager.Instance;
    private nint _windowHandle;

    public new bool IsInitialized => _renderContext != null && !_isDisposed;

    public new bool Topmost
    {
        get => base.Topmost;
        set => _logger.Safe(() => HandleSetTopmost(value),
            LogPrefix, "Error setting window topmost property");
    }

    private void HandleSetTopmost(bool value)
    {
        if (base.Topmost == value) return;
        base.Topmost = value;

        ForceRedraw();

        _logger.Log(LogLevel.Debug, LogPrefix, $"Overlay topmost state changed to: {value}");
    }

    public OverlayWindow(
        IMainController controller,
        OverlayConfiguration? configuration = null)
    {
        ArgumentNullException.ThrowIfNull(controller);
        _configuration = configuration ?? new();
        _transparencyManager = RendererTransparencyManager.Instance;

        try
        {
            ConfigureRenderingOptions();
            InitializeOverlay(controller);
            _frameTimeWatch.Start();

            controller.InputController.RegisterWindow(this);

            _transparencyManager.TransparencyChanged += OnTransparencyChanged;
        }
        catch (Exception ex)
        {
            _logger.Error(LogPrefix, "Failed to initialize overlay window", ex);
            throw;
        }
    }

    public void ForceRedraw() =>
        _logger.Safe(() => HandleForceRedraw(), LogPrefix, "Error forcing window redraw");

    private void HandleForceRedraw()
    {
        if (_isDisposed || _renderContext is null)
            return;

        var element = _renderContext.Value.SkElement;
        element?.InvalidateVisual();
    }

    private void OnTransparencyChanged(float level) =>
        _logger.Safe(() =>
        {
            Dispatcher.Invoke(() =>
            {
                Opacity = level;
                ForceRedraw();
            });
        }, LogPrefix, "Error handling transparency change");

    private void ConfigureRenderingOptions() =>
        _logger.Safe(() =>
        {
            SetHardwareAccelerationOptions();
            SetRenderQualityOptions();
        }, LogPrefix, "Error configuring rendering options");

    private void SetHardwareAccelerationOptions()
    {
        RenderOptions.ProcessRenderMode = RenderMode.Default;
        SetValue(RenderOptions.BitmapScalingModeProperty, BitmapScalingMode.HighQuality);
    }

    private void SetRenderQualityOptions()
    {
        SetValue(TextOptions.TextRenderingModeProperty, TextRenderingMode.Auto);
        SetValue(TextOptions.TextFormattingModeProperty, TextFormattingMode.Ideal);
        SetValue(RenderOptions.EdgeModeProperty, EdgeMode.Aliased);
        SetValue(RenderOptions.ClearTypeHintProperty, ClearTypeHint.Enabled);
    }

    private void InitializeOverlay(IMainController controller) =>
        _logger.Safe(() =>
        {
            ConfigureWindowProperties();
            CreateRenderContext(controller);
            SubscribeToEvents();
        }, LogPrefix, "Error initializing overlay");

    private void ConfigureWindowProperties() =>
        _logger.Safe(() =>
        {
            ConfigureWindowStyle();
            ConfigureWindowVisibility();
            ConfigureWindowInteraction();

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                SystemBackdrop.SetTransparentBackground(this);
        }, LogPrefix, "Error configuring window properties");

    private void ConfigureWindowStyle()
    {
        WindowStyle = _configuration.Style;
        WindowState = _configuration.State;
        ResizeMode = ResizeMode.NoResize;
    }

    private void ConfigureWindowVisibility()
    {
        AllowsTransparency = true;
        Background = null;
        Topmost = _configuration.IsTopmost;
        ShowInTaskbar = _configuration.ShowInTaskbar;
    }

    private void ConfigureWindowInteraction() => IsHitTestVisible = true;

    private void CreateRenderContext(IMainController controller) =>
        _logger.Safe(() =>
        {
            var skElement = CreateSkElement();
            var rendererFactory = RendererFactory.Instance;

            _renderContext = new(controller, skElement, rendererFactory);
            Content = skElement;

            controller.PropertyChanged += OnControllerPropertyChanged;
        }, LogPrefix, "Error creating render context");

    private SKElement CreateSkElement() =>
        _logger.SafeResult(() =>
        {
            var element = new SKElement
            {
                VerticalAlignment = VerticalAlignment.Stretch,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
                SnapsToDevicePixels = true,
                UseLayoutRounding = true
            };

            OptimizeElementForRender(element);

            element.MouseMove += (s, e) => _transparencyManager.OnMouseMove();
            element.MouseEnter += (s, e) => _transparencyManager.OnMouseEnter();
            element.MouseLeave += (s, e) => _transparencyManager.OnMouseLeave();

            return element;
        }, new SKElement(), LogPrefix, "Error creating SK element");

    private void OptimizeElementForRender(FrameworkElement element) =>
        _logger.Safe(() =>
        {
            RenderOptions.SetCachingHint(element, CachingHint.Cache);
            element.CacheMode = new BitmapCache
            {
                EnableClearType = false,
                SnapsToDevicePixels = true,
                RenderAtScale = 1.0
            };
        }, LogPrefix, "Error optimizing element for render");

    private void SubscribeToEvents() =>
        _logger.Safe(() =>
        {
            if (_renderContext is null) return;

            RegisterElementEvents();
            RegisterWindowEvents();

            CompositionTarget.Rendering += OnRendering;
        }, LogPrefix, "Error subscribing to events");

    private void RegisterElementEvents() =>
        _logger.Safe(() =>
        {
            if (_renderContext is null) return;
            _renderContext.Value.SkElement.PaintSurface += HandlePaintSurface;
        }, LogPrefix, "Error registering element events");

    private void RegisterWindowEvents() =>
        _logger.Safe(() =>
        {
            Closing += OnClosing;
            SourceInitialized += OnSourceInitialized;

            if (_configuration.EnableEscapeToClose)
                KeyDown += OnKeyDown;

            DpiChanged += OnDpiChanged;
            IsVisibleChanged += OnIsVisibleChanged;

            MouseMove += OnMouseMove;
            MouseEnter += OnMouseEnter;
            MouseLeave += OnMouseLeave;
        }, LogPrefix, "Error registering window events");

    private void OnMouseMove(object sender, MouseEventArgs e) =>
        _logger.Safe(() =>
        {
            _transparencyManager.OnMouseMove();
            e.Handled = true;
        }, LogPrefix, "Error handling mouse move");

    private void OnMouseEnter(object sender, MouseEventArgs e) =>
        _logger.Safe(() =>
        {
            _transparencyManager.OnMouseEnter();
            e.Handled = true;
        }, LogPrefix, "Error handling mouse enter");

    private void OnMouseLeave(object sender, MouseEventArgs e) =>
        _logger.Safe(() =>
        {
            _transparencyManager.OnMouseLeave();
            e.Handled = true;
        }, LogPrefix, "Error handling mouse leave");

    private void OnControllerPropertyChanged(
        object? sender,
        PropertyChangedEventArgs e) =>
        _logger.Safe(() =>
        {
            if (string.Equals(e.PropertyName, nameof(IMainController.LimitFpsTo60)))
            {
                UpdateFpsLimit();
            }
        }, LogPrefix, "Error handling controller property changed");

    private void UpdateFpsLimit() =>
        _logger.Safe(() =>
        {
            _fpsLimiter.IsEnabled = _renderContext?.Controller.LimitFpsTo60 ?? false;
            _fpsLimiter.Reset();
            ForceRedraw();
        }, LogPrefix, "Error updating FPS limit");

    private void OnRendering(object? sender, EventArgs e) =>
        _logger.Safe(() => HandleRendering(sender, e), LogPrefix, "Error handling rendering event");

    private void HandleRendering(object? sender, EventArgs e)
    {
        if (_isDisposed) return;

        if (ShouldRender())
            ForceRedraw();
    }

    private bool ShouldRender()
    {
        if (_transparencyManager.IsActive)
            return true;

        if (_renderContext?.Controller is IMainController controller)
        {
            return !controller.LimitFpsTo60 || _fpsLimiter.ShouldRenderFrame();
        }

        return !_fpsLimiter.IsEnabled || _fpsLimiter.ShouldRenderFrame();
    }

    private void OnClosing(object? sender, CancelEventArgs e) =>
        _logger.Safe(() =>
        {
            CompositionTarget.Rendering -= OnRendering;
            Dispose();
        }, LogPrefix, "Error handling window closing");

    private void OnSourceInitialized(object? sender, EventArgs e) =>
        _logger.Safe(() =>
        {
            InitializeWindowHandle();
            ApplyWindowOptimizations();
            _transparencyManager.ActivateTransparency();
            ForceRedraw();
        }, LogPrefix, "Error handling source initialized");

    private void InitializeWindowHandle() =>
        _windowHandle = new WindowInteropHelper(this).Handle;

    private void ApplyWindowOptimizations() =>
        _logger.Safe(() =>
        {
            if (_windowHandle == nint.Zero) return;

            ConfigureWindowStyleEx();

            if (_configuration.DisableWindowAnimations)
                SystemBackdrop.DisableWindowAnimations(_windowHandle);
        }, LogPrefix, "Error applying window optimizations");

    private void ConfigureWindowStyleEx() =>
        _logger.Safe(() =>
        {
            if (_windowHandle == nint.Zero) return;

            var extendedStyle = NativeMethods.GetWindowLong(_windowHandle, NativeMethods.GWL_EXSTYLE);

            _ = NativeMethods.SetWindowLong(
                _windowHandle,
                NativeMethods.GWL_EXSTYLE,
                extendedStyle | NativeMethods.WS_EX_LAYERED | NativeMethods.WS_EX_TRANSPARENT
            );
        }, LogPrefix, "Error configuring window style extended");

    private void OnKeyDown(object sender, KeyEventArgs e) =>
        _logger.Safe(() => HandleOnKeyDown(sender, e), LogPrefix, "Error handling key down");

    private void HandleOnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
            return;
        }

        if (_renderContext is not null)
            e.Handled = _renderContext.Value.Controller.HandleKeyDown(e, this);
    }

    private void OnDpiChanged(object? sender, DpiChangedEventArgs e) =>
        _logger.Safe(() =>
        {
            RefreshElementCacheForDpi();
            ForceRedraw();
        }, LogPrefix, "Error handling DPI changed");

    private void RefreshElementCacheForDpi() =>
        _logger.Safe(() =>
        {
            if (_renderContext?.SkElement is not { } element) return;

            element.CacheMode = null;
            element.CacheMode = new BitmapCache
            {
                EnableClearType = false,
                SnapsToDevicePixels = true,
                RenderAtScale = 1.0
            };
        }, LogPrefix, "Error refreshing element cache for DPI");

    private void OnIsVisibleChanged(
        object? sender,
        DependencyPropertyChangedEventArgs e) =>
        _logger.Safe(() => HandleOnIsVisibleChanged(sender, e), LogPrefix, "Error handling is visible changed");

    private void HandleOnIsVisibleChanged(
        object? sender,
        DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible)
        {
            _transparencyManager.ActivateTransparency();
            ForceRedraw();
        }
    }

    private void HandlePaintSurface(object? sender, SKPaintSurfaceEventArgs args) =>
        _logger.Safe(() =>
        {
            if (_isDisposed || _renderContext is null) return;
            PerformRender(sender, args);
        }, LogPrefix, "Error during paint surface handling");

    private void PerformRender(object? sender, SKPaintSurfaceEventArgs args) =>
        _logger.Safe(() =>
        {
            _frameTimeWatch.Restart();

            ClearCanvas(args.Surface.Canvas);
            RenderSpectrum(sender, args);

            _frameTimeWatch.Stop();
            RecordPerformanceMetrics();
        }, LogPrefix, "Error performing render");

    private static void ClearCanvas(SKCanvas canvas) =>
        canvas.Clear(SKColors.Transparent);

    private void RenderSpectrum(object? sender, SKPaintSurfaceEventArgs args) =>
        _logger.Safe(() =>
        {
            if (_renderContext is null || args is null) return;
            _renderContext.Value.Controller.OnPaintSurface(sender, args);
        }, LogPrefix, "Error rendering spectrum");

    private void RecordPerformanceMetrics() =>
        _performanceMetricsManager.RecordFrameTime();

    public void Dispose() =>
        _logger.Safe(() => HandleDispose(), LogPrefix, "Error disposing overlay window");

    private void HandleDispose()
    {
        if (_isDisposed) return;

        _transparencyManager.TransparencyChanged -= OnTransparencyChanged;

        CompositionTarget.Rendering -= OnRendering;

        _isDisposed = true;
        UnsubscribeFromEvents();
        DisposeResources();
    }

    private void UnsubscribeFromEvents() =>
        _logger.Safe(() =>
        {
            UnregisterElementEvents();
            UnregisterWindowEvents();
            UnregisterControllerEvents();
        }, LogPrefix, "Error unsubscribing from events");

    private void UnregisterElementEvents() =>
        _logger.Safe(() =>
        {
            if (_renderContext is null) return;

            var element = _renderContext.Value.SkElement;

            element.PaintSurface -= HandlePaintSurface;
            element.MouseMove -= (s, e) => _transparencyManager.OnMouseMove();
            element.MouseEnter -= (s, e) => _transparencyManager.OnMouseEnter();
            element.MouseLeave -= (s, e) => _transparencyManager.OnMouseLeave();
        }, LogPrefix, "Error unregistering element events");

    private void UnregisterWindowEvents() =>
        _logger.Safe(() =>
        {
            Closing -= OnClosing;
            SourceInitialized -= OnSourceInitialized;

            if (_configuration.EnableEscapeToClose)
                KeyDown -= OnKeyDown;

            DpiChanged -= OnDpiChanged;
            IsVisibleChanged -= OnIsVisibleChanged;

            MouseMove -= OnMouseMove;
            MouseEnter -= OnMouseEnter;
            MouseLeave -= OnMouseLeave;
        }, LogPrefix, "Error unregistering window events");

    private void UnregisterControllerEvents() =>
        _logger.Safe(() =>
        {
            if (_renderContext?.Controller is INotifyPropertyChanged controller)
                controller.PropertyChanged -= OnControllerPropertyChanged;
        }, LogPrefix, "Error unregistering controller events");

    private void DisposeResources() =>
        _logger.Safe(() =>
        {
            _disposalTokenSource.Cancel();
            _disposalTokenSource.Dispose();
            _renderContext = null;
        }, LogPrefix, "Error disposing resources");
}