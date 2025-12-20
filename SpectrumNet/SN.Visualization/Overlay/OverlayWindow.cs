using WpfColors = System.Windows.Media.Colors;

namespace SpectrumNet.SN.Visualization.Overlay;

public sealed class TransparencyEventArgs(float level) : EventArgs
{
    public float Level { get; } = level;
}

public sealed class StateChangedEventArgs(bool isActive) : EventArgs
{
    public bool IsActive { get; } = isActive;
}

public sealed record OverlayConfiguration(
    bool IsTopmost = true,
    bool ShowInTaskbar = false,
    bool EnableEscapeToClose = true,
    bool DisableWindowAnimations = true);

public interface IOverlayManager : IAsyncDisposable
{
    bool IsActive { get; }
    bool IsTopmost { get; set; }

    Task OpenAsync();
    Task CloseAsync();
    Task ToggleAsync();
    void SetTransparency(float level);
    void ForceRedraw();

    event EventHandler<StateChangedEventArgs>? StateChanged;
}

public interface ITransparencyManager : IDisposable
{
    float CurrentTransparency { get; }
    bool IsActive { get; }

    void OnMouseEnter();
    void OnMouseLeave();
    void OnMouseMove();
    void ActivateTransparency();
    void DeactivateTransparency();
    void EnableGlobalMouseTracking();
    void DisableGlobalMouseTracking();
    void SetRendererFactory(IRendererFactory factory);

    event EventHandler<TransparencyEventArgs>? TransparencyChanged;
}

public sealed class TransparencyManager : ITransparencyManager
{
    private const float TimeoutSec = 3.0f,
                        ActiveAlpha = 0.75f,
                        InactiveAlpha = 1.0f;

    private static readonly Lazy<TransparencyManager> _lazy = new(() => new TransparencyManager());
    public static TransparencyManager Instance => _lazy.Value;

    private readonly object _lock = new();
    private readonly DispatcherTimer _timer;
    private readonly NativeHook.MouseHookProc _hookDel;

    private IRendererFactory? _rf;
    private DateTime _lastActivity = DateTime.MinValue;
    private bool _hookActive, _disposed;
    private nint _hook;

    public float CurrentTransparency { get; private set; } = ActiveAlpha;
    public bool IsActive { get; private set; }
    public event EventHandler<TransparencyEventArgs>? TransparencyChanged;

    private TransparencyManager()
    {
        _hookDel = HookCallback;
        _timer = new DispatcherTimer { Interval = FromMilliseconds(100) };
        _timer.Tick += CheckTimeout;
        _timer.Start();
    }

    public void SetRendererFactory(IRendererFactory factory) => _rf ??= factory;

    public void OnMouseEnter() { RecordActivity(); ActivateTransparency(); }
    public void OnMouseLeave() => RecordActivity();
    public void OnMouseMove() { RecordActivity(); ActivateTransparency(); }

    public void ActivateTransparency()
    {
        lock (_lock) { IsActive = true; SetAlpha(ActiveAlpha); }
    }

    public void DeactivateTransparency()
    {
        lock (_lock) { IsActive = false; SetAlpha(InactiveAlpha); }
    }

    public void EnableGlobalMouseTracking()
    {
        if (_hookActive) return;
        _hook = NativeHook.SetMouseHook(_hookDel);
        _hookActive = _hook != 0;
    }

    public void DisableGlobalMouseTracking()
    {
        if (!_hookActive || _hook == 0) return;
        NativeHook.RemoveHook(_hook);
        _hook = 0;
        _hookActive = false;
    }

    private void RecordActivity() { lock (_lock) _lastActivity = Now; }

    private void SetAlpha(float level)
    {
        if (Abs(CurrentTransparency - level) < 0.0001f) return;
        CurrentTransparency = level;
        TransparencyChanged?.Invoke(this, new TransparencyEventArgs(level));

        if (_rf is null) return;
        foreach (ISpectrumRenderer r in _rf.GetAllRenderers())
            r.SetOverlayTransparency(level);
    }

    private void CheckTimeout(object? s, EventArgs e)
    {
        if (_disposed) return;
        lock (_lock)
        {
            if (IsActive && (Now - _lastActivity).TotalSeconds >= TimeoutSec)
                DeactivateTransparency();
        }
    }

    private nint HookCallback(int code, nint wParam, nint lParam)
    {
        if (code >= 0 && wParam == NativeHook.WM_MOUSEMOVE)
            OnMouseMove();
        return NativeHook.CallNextHook(_hook, code, wParam, lParam);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _timer.Tick -= CheckTimeout;
        _timer.Stop();
        DisableGlobalMouseTracking();

        SuppressFinalize(this);
    }
}

public sealed class OverlayManager : AsyncDisposableBase, IOverlayManager
{
    private readonly AppController _ctrl;
    private readonly ITransparencyManager _tm;
    private readonly ISettingsService _settings;

    private OverlayWindow? _wnd;
    private OverlayConfiguration _cfg;

    public event EventHandler<StateChangedEventArgs>? StateChanged;
    public bool IsActive { get; private set; }

    public bool IsTopmost
    {
        get => _cfg.IsTopmost;
        set
        {
            if (_cfg.IsTopmost == value) return;

            _cfg = _cfg with { IsTopmost = value };
            _settings.UpdateGeneral(g => g with { IsOverlayTopmost = value });

            if (_wnd is { IsInitialized: true })
                _wnd.Topmost = value;

            StateChanged?.Invoke(this, new StateChangedEventArgs(IsActive));
        }
    }

    public OverlayManager(AppController ctrl, ITransparencyManager tm, ISettingsService settings)
    {
        _ctrl = ctrl ?? throw new ArgumentNullException(nameof(ctrl));
        _tm = tm ?? throw new ArgumentNullException(nameof(tm));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _cfg = new(IsTopmost: _settings.Current.General.IsOverlayTopmost);
    }

    public async Task OpenAsync()
    {
        if (IsActive && _wnd is { IsInitialized: true })
        {
            AppController.Dispatcher.Invoke(() =>
            {
                _wnd.Show();
                _wnd.Topmost = IsTopmost;
            });
            return;
        }

        await Task.Run(() => AppController.Dispatcher.Invoke(CreateAndShow));
    }

    public async Task CloseAsync()
    {
        if (!IsActive) return;
        await AppController.Dispatcher.InvokeAsync(() => _wnd?.Close()).Task;
    }

    public async Task ToggleAsync() =>
        await (IsActive ? CloseAsync() : OpenAsync());

    public void SetTransparency(float level)
    {
        if (_wnd is { IsInitialized: true })
            AppController.Dispatcher.Invoke(() => _wnd.Opacity = level);
    }

    public void ForceRedraw() => _wnd?.ForceRedraw();

    private void CreateAndShow()
    {
        _wnd = new OverlayWindow(_ctrl, _tm, _cfg);
        _wnd.Closed += OnWindowClosed;
        _wnd.Show();

        _tm.EnableGlobalMouseTracking();
        IsActive = true;

        _ctrl.View.SpectrumCanvas.InvalidateVisual();
        StateChanged?.Invoke(this, new StateChangedEventArgs(true));

        _ctrl.View.Renderer?.UpdateRenderDimensions(
            (int)SystemParameters.PrimaryScreenWidth,
            (int)SystemParameters.PrimaryScreenHeight);
    }

    private void OnWindowClosed(object? s, EventArgs e)
    {
        _tm.DisableGlobalMouseTracking();
        DisposeWindow();

        IsActive = false;
        StateChanged?.Invoke(this, new StateChangedEventArgs(false));

        Collect(2, GCCollectionMode.Optimized, false);

        if (Application.Current?.MainWindow is { IsVisible: true } owner)
        {
            owner.Activate();
            owner.Focus();
        }
    }

    private void DisposeWindow()
    {
        (_wnd as IDisposable)?.Dispose();
        _wnd = null;
    }

    protected override void DisposeManaged()
    {
        if (IsActive)
            AppController.Dispatcher.Invoke(() => _wnd?.Close());
        DisposeWindow();
    }

    protected override async ValueTask DisposeAsyncManagedResources() =>
        await CloseAsync();
}

public sealed class OverlayWindow : Window, IDisposable
{
    private readonly AppController _ctrl;
    private readonly ITransparencyManager _tm;
    private readonly OverlayConfiguration _cfg;
    private readonly SKElement _el = new();

    private DispatcherTimer? _fpsTimer;
    private bool _disposed, _renderFaulted;

    public OverlayWindow(AppController ctrl, ITransparencyManager tm, OverlayConfiguration cfg)
    {
        _ctrl = ctrl ?? throw new ArgumentNullException(nameof(ctrl));
        _tm = tm ?? throw new ArgumentNullException(nameof(tm));
        _cfg = cfg ?? new();

        ConfigureWindow();
        SetupEvents();
        UpdateRenderLoop();
    }

    public void ForceRedraw() => _el.InvalidateVisual();

    private void ConfigureWindow()
    {
        AllowsTransparency = false;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        WindowState = WindowState.Normal;

        Top = SystemParameters.VirtualScreenTop;
        Left = SystemParameters.VirtualScreenLeft;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;

        Background = Brushes.Transparent;
        Topmost = _cfg.IsTopmost;
        ShowInTaskbar = _cfg.ShowInTaskbar;
        ShowActivated = false;
        IsHitTestVisible = false;

        RenderOptions.SetEdgeMode(this, EdgeMode.Aliased);
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);

        Content = _el;
    }

    private void SetupEvents()
    {
        _el.PaintSurface += OnPaint;
        SourceInitialized += OnSourceInit;
        PreviewKeyDown += OnKeyDown;
        _tm.TransparencyChanged += OnTransparencyChanged;
        _ctrl.PropertyChanged += OnCtrlPropertyChanged;
        _ctrl.Input.RegisterWindow(this);
    }

    private void UpdateRenderLoop()
    {
        CompositionTarget.Rendering -= OnFrame;
        _fpsTimer?.Stop();

        if (_disposed) return;

        if (_ctrl.LimitFpsTo60)
        {
            _fpsTimer ??= new DispatcherTimer(DispatcherPriority.Render, Dispatcher)
            {
                Interval = FromSeconds(1.0 / 60.0)
            };
            _fpsTimer.Tick -= OnFrame;
            _fpsTimer.Tick += OnFrame;
            _fpsTimer.Start();
        }
        else
        {
            CompositionTarget.Rendering += OnFrame;
        }
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        if (!_disposed && !_renderFaulted)
            ForceRedraw();
    }

    private void OnPaint(object? s, SKPaintSurfaceEventArgs e)
    {
        e.Surface.Canvas.Clear(SKColors.Transparent);
        if (_renderFaulted) return;

        try
        {
            _ctrl.View.OnPaintSurface(s, e);
        }
        catch
        {
            _renderFaulted = true;
            CompositionTarget.Rendering -= OnFrame;
            _fpsTimer?.Stop();
        }
    }

    private void OnSourceInit(object? s, EventArgs e)
    {
        nint hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == 0) return;

        try
        {
            HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);
            if (HwndSource.FromHwnd(hwnd)?.CompositionTarget is { } ct)
                ct.BackgroundColor = WpfColors.Transparent;

            NativeWindow.ExtendFrameIntoClientArea(hwnd);
        }
        catch { }

        NativeWindow.MakeTransparent(hwnd);

        if (_cfg.DisableWindowAnimations)
            NativeWindow.DisableAnimations(hwnd);

        _tm.ActivateTransparency();
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == 0x0084) { handled = true; return -1; }
        return 0;
    }

    private void OnTransparencyChanged(object? sender, TransparencyEventArgs e) =>
        Dispatcher.InvokeAsync(() => { Opacity = e.Level; ForceRedraw(); }, DispatcherPriority.Render);

    private void OnCtrlPropertyChanged(object? s, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppController.LimitFpsTo60))
            Dispatcher.InvokeAsync(UpdateRenderLoop);
    }

    private void OnKeyDown(object s, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _cfg.EnableEscapeToClose)
        {
            e.Handled = true;
            Close();
            return;
        }
        _ctrl.Input.HandleKeyDown(s, e);
    }

    protected override void OnClosed(EventArgs e)
    {
        Dispose();
        base.OnClosed(e);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        CompositionTarget.Rendering -= OnFrame;

        if (_fpsTimer is not null)
        {
            _fpsTimer.Stop();
            _fpsTimer.Tick -= OnFrame;
        }

        _tm.TransparencyChanged -= OnTransparencyChanged;
        _ctrl.PropertyChanged -= OnCtrlPropertyChanged;
        _el.PaintSurface -= OnPaint;

        (_el as IDisposable)?.Dispose();
        Content = null;

        SuppressFinalize(this);
    }
}

#pragma warning disable CA5392, SYSLIB1054, CA1806

internal static class NativeWindow
{
    private const int GWL_EXSTYLE = -20;
    private const nint WS_EX_TRANSPARENT = 0x20, WS_EX_LAYERED = 0x80000, WS_EX_NOACTIVATE = 0x8000000;
    private const uint LWA_ALPHA = 0x02, SWP_FLAGS = 0x37;

    [StructLayout(LayoutKind.Sequential)]
    private struct MARGINS { public int L, R, T, B; }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern nint GetWindowLongPtr(nint hwnd, int idx);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern nint SetWindowLongPtr(nint hwnd, int idx, nint val);

    [DllImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern bool SetWindowPos(nint h, nint after, int x, int y, int cx, int cy, uint f);

    [DllImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern bool SetLayeredWindowAttributes(nint h, uint key, byte alpha, uint flags);

    [DllImport("dwmapi.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int DwmExtendFrameIntoClientArea(nint hwnd, ref MARGINS m);

    [DllImport("dwmapi.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attr, ref int val, int size);

    public static void MakeTransparent(nint hwnd)
    {
        nint ex = GetWindowLongPtr(hwnd, GWL_EXSTYLE) | WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE;
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, ex);
        SetLayeredWindowAttributes(hwnd, 0, 255, LWA_ALPHA);
        SetWindowPos(hwnd, 0, 0, 0, 0, 0, SWP_FLAGS);
    }

    public static void ExtendFrameIntoClientArea(nint hwnd)
    {
        MARGINS m = new() { L = -1 };
        DwmExtendFrameIntoClientArea(hwnd, ref m);
    }

    public static void DisableAnimations(nint hwnd)
    {
        int val = 1;
        DwmSetWindowAttribute(hwnd, 3, ref val, 4);
    }
}

internal static class NativeHook
{
    public const int WM_MOUSEMOVE = 0x200;
    private const int WH_MOUSE_LL = 14;

    public delegate nint MouseHookProc(int code, nint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern nint SetWindowsHookEx(int id, MouseHookProc proc, nint hMod, uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern bool UnhookWindowsHookEx(nint hhk);

    [DllImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern nint CallNextHookEx(nint hhk, int code, nint wParam, nint lParam);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern nint GetModuleHandle(string? name);

    public static nint SetMouseHook(MouseHookProc proc) =>
        SetWindowsHookEx(WH_MOUSE_LL, proc, GetModuleHandle(null), 0);

    public static void RemoveHook(nint hook) =>
        UnhookWindowsHookEx(hook);

    public static nint CallNextHook(nint hook, int code, nint wParam, nint lParam) =>
        CallNextHookEx(hook, code, wParam, lParam);
}

#pragma warning restore CA5392, SYSLIB1054, CA1806
