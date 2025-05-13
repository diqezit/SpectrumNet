#nullable enable

using MouseEventArgs = System.Windows.Input.MouseEventArgs;

namespace SpectrumNet.Controllers.RenderCore;

public sealed class OverlayWindow : Window, IDisposable
{
    private const string LogSource = "OverlayWindow";
    private const int MaxConsecutiveRenderSkips = 3;

    private readonly record struct RenderContext(
        IMainController Controller,
        SKElement SkElement,
        DispatcherTimer RenderTimer,
        IRendererFactory RendererFactory);

    private readonly OverlayConfiguration _configuration;
    private readonly CancellationTokenSource _disposalTokenSource = new();
    private RenderContext? _renderContext;
    private bool _isDisposed;

    private readonly FpsLimiter _fpsLimiter = FpsLimiter.Instance;
    private readonly Stopwatch _frameTimeWatch = new();
    private readonly ITransparencyManager _transparencyManager;

    private int _consecutiveSkips;
    private nint _windowHandle;

    public new bool IsInitialized => _renderContext != null && !_isDisposed;

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
            Error(LogSource, "Failed to initialize overlay window", ex);
            throw;
        }
    }

    private void OnTransparencyChanged(float level)
    {
        Dispatcher.Invoke(() => {
            Opacity = level;
            ForceRedraw();
        });
    }

    public void ForceRedraw()
    {
        if (_isDisposed || _renderContext is null) return;
        _renderContext.Value.SkElement.InvalidateVisual();
    }

    private void ConfigureRenderingOptions()
    {
        SetHardwareAccelerationOptions();
        SetRenderQualityOptions();
    }

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

    private void InitializeOverlay(IMainController controller)
    {
        ConfigureWindowProperties();
        CreateRenderContext(controller);
        SubscribeToEvents();
    }

    private void ConfigureWindowProperties()
    {
        ConfigureWindowStyle();
        ConfigureWindowVisibility();
        ConfigureWindowInteraction();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            SystemBackdrop.SetTransparentBackground(this);
    }

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

    private void CreateRenderContext(IMainController controller)
    {
        var skElement = CreateSkElement();
        var renderTimer = CreateRenderTimer();
        var rendererFactory = RendererFactory.Instance;

        _renderContext = new(controller, skElement, renderTimer, rendererFactory);
        Content = skElement;

        controller.PropertyChanged += OnControllerPropertyChanged;
    }

    private SKElement CreateSkElement()
    {
        var element = new SKElement
        {
            VerticalAlignment = System.Windows.VerticalAlignment.Stretch,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
            SnapsToDevicePixels = true,
            UseLayoutRounding = true
        };

        OptimizeElementForRender(element);

        // Важно: передаем события мыши в менеджер прозрачности
        element.MouseMove += (s, e) => _transparencyManager.OnMouseMove();
        element.MouseEnter += (s, e) => _transparencyManager.OnMouseEnter();
        element.MouseLeave += (s, e) => _transparencyManager.OnMouseLeave();

        return element;
    }

    private static void OptimizeElementForRender(FrameworkElement element)
    {
        RenderOptions.SetCachingHint(element, CachingHint.Cache);
        element.CacheMode = new BitmapCache
        {
            EnableClearType = false,
            SnapsToDevicePixels = true,
            RenderAtScale = 1.0
        };
    }

    private DispatcherTimer CreateRenderTimer()
    {
        return new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = FromMilliseconds(_configuration.RenderInterval)
        };
    }

    private void SubscribeToEvents()
    {
        if (_renderContext is null) return;

        RegisterElementEvents();
        RegisterWindowEvents();
    }

    private void RegisterElementEvents()
    {
        if (_renderContext is null) return;
        _renderContext.Value.SkElement.PaintSurface += HandlePaintSurface;
        _renderContext.Value.RenderTimer.Tick += RenderTimerTick;
    }

    private void RegisterWindowEvents()
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
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        _transparencyManager.OnMouseMove();
        e.Handled = true;
    }

    private void OnMouseEnter(object sender, MouseEventArgs e)
    {
        _transparencyManager.OnMouseEnter();
        e.Handled = true;
    }

    private void OnMouseLeave(object sender, MouseEventArgs e)
    {
        _transparencyManager.OnMouseLeave();
        e.Handled = true;
    }

    private void OnControllerPropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (string.Equals(e.PropertyName, nameof(IMainController.LimitFpsTo60)))
        {
            UpdateFpsLimit();
        }
    }

    private void UpdateFpsLimit()
    {
        _fpsLimiter.IsEnabled = _renderContext?.Controller.LimitFpsTo60 ?? false;
        _fpsLimiter.Reset();
        _consecutiveSkips = 0;
        ForceRedraw();
    }

    private void RenderTimerTick(object? sender, EventArgs e)
    {
        if (_isDisposed) return;

        if (ShouldRender())
        {
            _consecutiveSkips = 0;
            ForceRedraw();
        }
        else
        {
            _consecutiveSkips++;
        }
    }

    private bool ShouldRender()
    {
        if (_consecutiveSkips >= MaxConsecutiveRenderSkips)
            return true;

        if (_transparencyManager.IsActive)
            return true;

        return !_fpsLimiter.IsEnabled || _fpsLimiter.ShouldRenderFrame();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        StopRenderTimer();
        Dispose();
    }

    private void StopRenderTimer()
    {
        if (_renderContext?.RenderTimer is { IsEnabled: true } timer)
            timer.Stop();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        InitializeWindowHandle();
        ApplyWindowOptimizations();
        StartRenderTimer();

        // Активируем прозрачность при запуске
        _transparencyManager.ActivateTransparency();

        ForceRedraw();
    }

    private void InitializeWindowHandle() =>
        _windowHandle = new WindowInteropHelper(this).Handle;

    private void ApplyWindowOptimizations()
    {
        if (_windowHandle == nint.Zero) return;

        ConfigureWindowStyleEx();

        if (_configuration.DisableWindowAnimations)
            SystemBackdrop.DisableWindowAnimations(_windowHandle);
    }

    private void ConfigureWindowStyleEx()
    {
        if (_windowHandle == nint.Zero) return;

        var extendedStyle = NativeMethods.GetWindowLong(_windowHandle, NativeMethods.GWL_EXSTYLE);

        // Включаем WS_EX_TRANSPARENT для пропускания кликов мыши вместе с WS_EX_LAYERED
        _ = NativeMethods.SetWindowLong(
            _windowHandle,
            NativeMethods.GWL_EXSTYLE,
            extendedStyle | NativeMethods.WS_EX_LAYERED | NativeMethods.WS_EX_TRANSPARENT
        );
    }

    private void StartRenderTimer()
    {
        if (_renderContext?.RenderTimer is { IsEnabled: false } timer)
            timer.Start();
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
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

    private void OnDpiChanged(object? sender, DpiChangedEventArgs e)
    {
        RefreshElementCacheForDpi();
        ForceRedraw();
    }

    private void RefreshElementCacheForDpi()
    {
        if (_renderContext?.SkElement is not { } element) return;

        element.CacheMode = null;
        element.CacheMode = new BitmapCache
        {
            EnableClearType = false,
            SnapsToDevicePixels = true,
            RenderAtScale = 1.0
        };
    }

    private void OnIsVisibleChanged(
        object? sender,
        DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible)
        {
            StartRenderTimer();
            _transparencyManager.ActivateTransparency();
            ForceRedraw();
        }
        else
        {
            StopRenderTimer();
        }
    }

    private void HandlePaintSurface(object? sender, SKPaintSurfaceEventArgs args)
    {
        if (_isDisposed || _renderContext is null) return;

        try
        {
            PerformRender(sender, args);
        }
        catch (Exception ex)
        {
            Error(LogSource,
                  "Error during paint surface handling",
                  ex);
        }
    }

    private void PerformRender(object? sender, SKPaintSurfaceEventArgs args)
    {
        _frameTimeWatch.Restart();

        ClearCanvas(args.Surface.Canvas);
        RenderSpectrum(sender, args);

        _frameTimeWatch.Stop();
        RecordPerformanceMetrics();
    }

    private static void ClearCanvas(SKCanvas canvas) =>
        canvas.Clear(SKColors.Transparent);

    private void RenderSpectrum(object? sender, SKPaintSurfaceEventArgs args)
    {
        if (_renderContext is null || args is null) return;
        _renderContext.Value.Controller.OnPaintSurface(sender, args);
    }

    private static void RecordPerformanceMetrics() =>
        PerformanceMetricsManager.RecordFrameTime();

    public void Dispose()
    {
        if (_isDisposed) return;

        _transparencyManager.TransparencyChanged -= OnTransparencyChanged;

        _isDisposed = true;
        UnsubscribeFromEvents();
        DisposeResources();
    }

    private void UnsubscribeFromEvents()
    {
        UnregisterElementEvents();
        UnregisterWindowEvents();
        UnregisterControllerEvents();
    }

    private void UnregisterElementEvents()
    {
        if (_renderContext is null) return;

        var element = _renderContext.Value.SkElement;

        element.PaintSurface -= HandlePaintSurface;
        element.MouseMove -= (s, e) => _transparencyManager.OnMouseMove();
        element.MouseEnter -= (s, e) => _transparencyManager.OnMouseEnter();
        element.MouseLeave -= (s, e) => _transparencyManager.OnMouseLeave();

        _renderContext.Value.RenderTimer.Tick -= RenderTimerTick;
        _renderContext.Value.RenderTimer.Stop();
    }

    private void UnregisterWindowEvents()
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
    }

    private void UnregisterControllerEvents()
    {
        if (_renderContext?.Controller is INotifyPropertyChanged controller)
            controller.PropertyChanged -= OnControllerPropertyChanged;
    }

    private void DisposeResources()
    {
        _disposalTokenSource.Cancel();
        _disposalTokenSource.Dispose();
        _renderContext = null;
    }
}