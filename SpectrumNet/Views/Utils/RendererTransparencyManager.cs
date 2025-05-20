#nullable enable

using SpectrumNet.Controllers.Interfaces.FactoryCore;
using static SpectrumNet.Views.Utils.RendererTransparencyManager.Constants;

namespace SpectrumNet.Views.Utils;

public sealed partial class RendererTransparencyManager : ITransparencyManager, IDisposable
{
    public record Constants
    {
        public const string LOG_PREFIX = nameof(RendererTransparencyManager);
        public const float OVERLAY_TIMEOUT_SECONDS = 3.0f;

        public const float
            ACTIVE_TRANSPARENCY = 0.75f,
            INACTIVE_TRANSPARENCY = 1.0f;

        // Константы для хука мыши
        public const int WH_MOUSE_LL = 14;
        public const int WM_MOUSEMOVE = 0x0200;
    }

    private static readonly Lazy<RendererTransparencyManager> _instance =
        new(() => new RendererTransparencyManager());

    public static RendererTransparencyManager Instance => _instance.Value;

    private readonly ISmartLogger _logger = SmartLogger.Instance;
    private DateTime _lastMouseActivity = DateTime.MinValue;
    private bool _isTransparencyActive;

    private readonly object _stateLock = new();
    private readonly DispatcherTimer _transparencyTimer;
    private IRendererFactory? _rendererFactory;
    private bool _disposed;
    private float _currentTransparencyValue = ACTIVE_TRANSPARENCY;

    private bool _isMouseHookActive;
    private nint _mouseHook;
    private readonly MouseHookProc _mouseHookDelegate;

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern nint SetWindowsHookEx(int idHook, MouseHookProc lpfn, nint hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool UnhookWindowsHookEx(nint hhk);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern nint CallNextHookEx(nint hhk, int nCode, nint wParam, nint lParam);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandle(string? lpModuleName);

    private delegate nint MouseHookProc(int nCode, nint wParam, nint lParam);

    public float CurrentTransparency => _currentTransparencyValue;
    public bool IsActive => _isTransparencyActive;

    public event Action<float>? TransparencyChanged;

    private RendererTransparencyManager()
    {
        _mouseHookDelegate = MouseHookCallback;
        _transparencyTimer = CreateTransparencyTimer();
        StartTransparencyTimer();

        LogInitialization();
    }

    private DispatcherTimer CreateTransparencyTimer() =>
        new()
        {
            Interval = FromMilliseconds(100)
        };

    private void StartTransparencyTimer()
    {
        _transparencyTimer.Tick += CheckTransparencyTimeout;
        _transparencyTimer.Start();
    }

    private void LogInitialization() =>
        _logger.Log(LogLevel.Information, LOG_PREFIX, "Initialized");

    public void SetRendererFactory(IRendererFactory factory) =>
        _rendererFactory ??= factory;

    public void OnMouseEnter()
    {
        RecordMouseActivity();
        ActivateTransparency();
    }

    public void OnMouseLeave() =>
        RecordMouseActivity();

    public void OnMouseMove()
    {
        RecordMouseActivity();
        ActivateTransparency();
    }

    private void RecordMouseActivity()
    {
        lock (_stateLock)
        {
            _lastMouseActivity = Now;
        }
    }

    public void ActivateTransparency()
    {
        lock (_stateLock)
        {
            _isTransparencyActive = true;
            UpdateTransparency(ACTIVE_TRANSPARENCY);
        }
    }

    private void UpdateTransparency(float transparencyLevel) =>
        _logger.Safe(() => HandleUpdateTransparency(transparencyLevel), LOG_PREFIX, "Error updating transparency");

    private void HandleUpdateTransparency(float transparencyLevel)
    {
        if (ShouldSkipTransparencyUpdate(transparencyLevel))
            return;

        LogTransparencyUpdate(transparencyLevel);
        SetTransparencyValue(transparencyLevel);
        NotifyTransparencyChanged();
        UpdateRendererTransparency();
    }

    private static bool ShouldSkipTransparencyUpdate(float desired, float current) =>
        Abs(current - desired) < 0.0001f;

    private bool ShouldSkipTransparencyUpdate(float desired) =>
        ShouldSkipTransparencyUpdate(desired, _currentTransparencyValue);

    private void LogTransparencyUpdate(float transparencyLevel) =>
        _logger.Log(LogLevel.Debug, LOG_PREFIX, $"Updating transparency to {transparencyLevel}");

    private void SetTransparencyValue(float transparencyLevel) =>
        _currentTransparencyValue = transparencyLevel;

    private void NotifyTransparencyChanged() =>
        TransparencyChanged?.Invoke(_currentTransparencyValue);

    private void UpdateRendererTransparency()
    {
        if (_rendererFactory == null)
            return;

        var renderers = _rendererFactory.GetAllRenderers();
        UpdateAllRenderersTransparency(renderers);
    }

    private void UpdateAllRenderersTransparency(IEnumerable<ISpectrumRenderer> renderers)
    {
        foreach (var renderer in renderers)
        {
            renderer.SetOverlayTransparency(_currentTransparencyValue);
        }
    }

    private void CheckTransparencyTimeout(object? sender, EventArgs e) =>
        _logger.Safe(() => HandleCheckTransparencyTimeout(sender, e), LOG_PREFIX, "Error checking transparency timeout");

    private void HandleCheckTransparencyTimeout(object? sender, EventArgs e)
    {
        if (_disposed) return;

        lock (_stateLock)
        {
            if (!_isTransparencyActive) return;
            if ((Now - _lastMouseActivity).TotalSeconds >= OVERLAY_TIMEOUT_SECONDS)
                DeactivateTransparency();
        }
    }

    public void DeactivateTransparency()
    {
        lock (_stateLock)
        {
            _isTransparencyActive = false;
            UpdateTransparency(INACTIVE_TRANSPARENCY);
        }
    }

    public void EnableGlobalMouseTracking() =>
        _logger.Safe(() => HandleEnableGlobalMouseTracking(), LOG_PREFIX, "Error enabling global mouse tracking");

    private void HandleEnableGlobalMouseTracking()
    {
        if (_isMouseHookActive) return;

        _mouseHook = SetWindowsHookEx(
            WH_MOUSE_LL,
            _mouseHookDelegate,
            GetModuleHandle(null),
            0);

        if (_mouseHook != IntPtr.Zero)
        {
            _isMouseHookActive = true;
            LogMouseTrackingEnabled();
        }
    }

    public void DisableGlobalMouseTracking() =>
        _logger.Safe(() => HandleDisableGlobalMouseTracking(), LOG_PREFIX, "Error disabling global mouse tracking");

    private void HandleDisableGlobalMouseTracking()
    {
        if (!_isMouseHookActive || _mouseHook == IntPtr.Zero) return;
        UnhookWindowsHookEx(_mouseHook);
        _mouseHook = IntPtr.Zero;
        _isMouseHookActive = false;
        LogMouseTrackingDisabled();
    }

    private void LogMouseTrackingEnabled() =>
        _logger.Log(LogLevel.Debug, LOG_PREFIX, "Global mouse tracking enabled");

    private void LogMouseTrackingDisabled() =>
        _logger.Log(LogLevel.Debug, LOG_PREFIX, "Global mouse tracking disabled");

    private nint MouseHookCallback(int nCode, nint wParam, nint lParam)
    {
        _logger.Safe(() => ProcessMouseMessage(nCode, wParam), LOG_PREFIX, "Error processing mouse message");
        return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    private void ProcessMouseMessage(int nCode, nint wParam)
    {
        if (nCode >= 0 && wParam == (nint)WM_MOUSEMOVE)
        {
            OnMouseMove();
        }
    }

    public void Dispose() =>
        _logger.Safe(() => HandleDispose(), LOG_PREFIX, "Error during disposal");

    private void HandleDispose()
    {
        if (_disposed) return;

        _transparencyTimer.Tick -= CheckTransparencyTimeout;
        _transparencyTimer.Stop();

        if (_isMouseHookActive && _mouseHook != IntPtr.Zero)
            UnhookWindowsHookEx(_mouseHook);

        _disposed = true;
        GC.SuppressFinalize(this);
        _logger.Log(LogLevel.Debug, LOG_PREFIX, "Disposed");
    }
}