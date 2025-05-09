#nullable enable

namespace SpectrumNet.Controllers.RenderCore;

public sealed class OverlayWindow : Window, IDisposable
{
    private const string LogSource = "OverlayWindow";

    private readonly record struct RenderContext(
        IMainController Controller,
        SKElement SkElement,
        DispatcherTimer RenderTimer,
        IRendererFactory RendererFactory);

    private readonly OverlayConfiguration _configuration;
    private readonly CancellationTokenSource _disposalTokenSource = new();
    private RenderContext? _renderContext;
    private bool _isDisposed;
    private SKBitmap? _cacheBitmap;
    private readonly SemaphoreSlim _renderLock = new(1, 1);
    private readonly Stopwatch _frameTimeWatch = new();
    private readonly FpsLimiter _fpsLimiter = FpsLimiter.Instance;

    public new bool IsInitialized => _renderContext != null && !_isDisposed;

    public OverlayWindow(
        IMainController controller,
        OverlayConfiguration? configuration = null)
    {
        ArgumentNullException.ThrowIfNull(controller);
        _configuration = configuration ?? new();

        try
        {
            ConfigureRenderingOptions();
            InitializeOverlay(controller);
            _frameTimeWatch.Start();

            controller.InputController.RegisterWindow(this);
        }
        catch (Exception ex)
        {
            Error(LogSource, "Failed to initialize overlay window", ex);
            throw;
        }
    }

    public void ForceRedraw()
    {
        if (!_isDisposed && _renderContext != null)
        {
            _renderContext.Value.SkElement.InvalidateVisual();
        }
    }

    private void ConfigureRenderingOptions()
    {
        if (_configuration.EnableHardwareAcceleration)
        {
            RenderOptions.ProcessRenderMode = RenderMode.Default;
            SetValue(RenderOptions.BitmapScalingModeProperty,
                     BitmapScalingMode.NearestNeighbor);
        }
    }

    private void InitializeOverlay(IMainController controller)
    {
        ConfigureWindowProperties();
        CreateRenderContext(controller);
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

    private void CreateRenderContext(IMainController controller)
    {
        var skElement = CreateSkElement();
        var renderTimer = CreateRenderTimer();
        var rendererFactory = RendererFactory.Instance;

        _renderContext = new(controller, skElement, renderTimer, rendererFactory);
        Content = skElement;

        controller.PropertyChanged += OnControllerPropertyChanged;
    }

    private static SKElement CreateSkElement() =>
        new()
        {
            VerticalAlignment = System.Windows.VerticalAlignment.Stretch,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
            SnapsToDevicePixels = true,
            UseLayoutRounding = true
        };

    private static DispatcherTimer CreateRenderTimer() =>
        new(DispatcherPriority.Render)
        {
            Interval = FromMilliseconds(1000.0 / 60.0)
        };

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
        SizeChanged += OnSizeChanged;
    }

    private void OnControllerPropertyChanged(object? sender, PropertyChangedEventArgs e) =>
        HandleFpsLimitingPropertyChange(e);

    private void HandleFpsLimitingPropertyChange(PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IMainController.LimitFpsTo60))
        {
            _fpsLimiter.IsEnabled = _renderContext?.Controller.LimitFpsTo60 ?? false;
            _fpsLimiter.Reset();
            ForceRedraw();
        }
    }

    private void RenderTimerTick(object? sender, EventArgs e)
    {
        if (ShouldSkipRendering()) return;

        if (_fpsLimiter.ShouldRenderFrame())
        {
            ForceRedraw();
        }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        StopRenderTimer();
        Dispose();
    }

    private void StopRenderTimer() =>
        _renderContext?.RenderTimer.Stop();

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        ConfigureWindowStyleEx();
        StartRenderTimer();
        ForceRedraw();
    }

    private void StartRenderTimer() =>
        _renderContext?.RenderTimer.Start();

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Escape)
        {
            e.Handled = true;
            Close();
            return; 
        }

        if (_renderContext is not null)
        {
            if (_renderContext.Value.Controller.HandleKeyDown(e, this)) 
            {
                e.Handled = true;
            }
        }
    }

    private void OnDpiChanged(object? sender, DpiChangedEventArgs e)
    {
        InvalidateRenderCache();
        ForceRedraw();
    }

    private void OnIsVisibleChanged(object? sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible)
        {
            StartRenderTimer();
            ForceRedraw();
        }
        else
        {
            StopRenderTimer();
        }
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        InvalidateRenderCache();
        ForceRedraw();
    }

    private void InvalidateRenderCache()
    {
        _cacheBitmap?.Dispose();
        _cacheBitmap = null;
    }

    private void HandlePaintSurface(object? sender, SKPaintSurfaceEventArgs args)
    {
        if (ShouldSkipRendering()) return;

        if (!TryAcquireRenderLock()) return;

        try
        {
            _frameTimeWatch.Restart();

            var info = args.Info;
            var canvas = args.Surface.Canvas;

            ClearCanvas(canvas);

            if (IsRenderingTakingTooLong())
            {
                RenderDirectly(sender, args);
            }
            else
            {
                RenderWithOrWithoutCaching(sender, args, info, canvas);
            }

            PerformanceMetricsManager.RecordFrameTime();
        }
        catch (Exception ex)
        {
            HandleRenderingException(ex);
        }
        finally
        {
            ReleaseRenderLock();
        }
    }

    private bool ShouldSkipRendering() => _isDisposed || _renderContext is null;

    private bool TryAcquireRenderLock() => _renderLock.Wait(0);

    private void ReleaseRenderLock() => _renderLock.Release();

    private static void ClearCanvas(SKCanvas canvas) =>
        canvas.Clear(SKColors.Transparent);

    private bool IsRenderingTakingTooLong() =>
        _frameTimeWatch.ElapsedMilliseconds > _configuration.RenderInterval * 2;

    private void RenderDirectly(object? sender, SKPaintSurfaceEventArgs args)
    {
        if (_renderContext is null) return;
        _renderContext.Value.Controller.OnPaintSurface(sender, args);
    }

    private void RenderWithOrWithoutCaching(
        object? sender,
        SKPaintSurfaceEventArgs args,
        SKImageInfo info,
        SKCanvas canvas)
    {
        if (_renderContext is null) return;

        if (_configuration.EnableHardwareAcceleration)
        {
            RenderWithHardwareAcceleration(sender, args);
        }
        else
        {
            RenderWithSoftwareCaching(sender, args, info, canvas);
        }
    }

    private void RenderWithHardwareAcceleration(object? sender, SKPaintSurfaceEventArgs args)
    {
        if (_renderContext is null) return;
        _renderContext.Value.Controller.OnPaintSurface(sender, args);
    }

    private void RenderWithSoftwareCaching(
        object? sender,
        SKPaintSurfaceEventArgs args,
        SKImageInfo info,
        SKCanvas canvas)
    {
        if (_renderContext is null) return;

        EnsureCacheBitmapCreated(info);

        using var tempSurface = SKSurface.Create(info, _cacheBitmap!.GetPixels(), _cacheBitmap.RowBytes);
        tempSurface.Canvas.Clear(SKColors.Transparent);
        _renderContext.Value.Controller.OnPaintSurface(sender, args);


        canvas.DrawBitmap(_cacheBitmap!, 0, 0);
    }

    private void EnsureCacheBitmapCreated(SKImageInfo info)
    {
        if (_cacheBitmap == null || _cacheBitmap.Width != info.Width || _cacheBitmap.Height != info.Height)
        {
            _cacheBitmap?.Dispose();
            _cacheBitmap = new SKBitmap(info.Width, info.Height, info.ColorType, info.AlphaType);
        }
    }

    private static void HandleRenderingException(Exception ex) =>
        Error(LogSource, "Error during paint surface handling", ex);

    private void ConfigureWindowStyleEx()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == nint.Zero) return;

        ApplyExtendedWindowStyle(hwnd);
    }

    private static void ApplyExtendedWindowStyle(nint hwnd)
    {
        var extendedStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
        _ = NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE,
            extendedStyle | NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_LAYERED);
    }

    public void Dispose()
    {
        if (_isDisposed) return;

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
        if (_renderContext != null)
        {
            _renderContext.Value.SkElement.PaintSurface -= HandlePaintSurface;
            _renderContext.Value.RenderTimer.Tick -= RenderTimerTick;
            _renderContext.Value.RenderTimer.Stop();
        }
    }

    private void UnregisterWindowEvents()
    {
        Closing -= OnClosing;
        SourceInitialized -= OnSourceInitialized;

        if (_configuration.EnableEscapeToClose)
            KeyDown -= OnKeyDown;

        DpiChanged -= OnDpiChanged;
        IsVisibleChanged -= OnIsVisibleChanged;
        SizeChanged -= OnSizeChanged;
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
        _cacheBitmap?.Dispose();
        _renderLock.Dispose();
        _renderContext = null;
    }
}