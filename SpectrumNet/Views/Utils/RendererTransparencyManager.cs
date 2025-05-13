#nullable enable

using static SpectrumNet.Views.Utils.RendererTransparencyManager.Constants;

namespace SpectrumNet.Views.Utils;

public sealed partial class RendererTransparencyManager : ITransparencyManager, IDisposable
{
    public record Constants
    {
        public const string LOG_PREFIX = "RendererTransparencyManager";
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

    private static DispatcherTimer CreateTransparencyTimer() =>
        new()
        {
            Interval = FromMilliseconds(100)
        };

    private void StartTransparencyTimer()
    {
        _transparencyTimer.Tick += CheckTransparencyTimeout;
        _transparencyTimer.Start();
    }

    private static void LogInitialization() =>
        Log(LogLevel.Information, LOG_PREFIX, "Initialized");

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

    private void UpdateTransparency(float transparencyLevel)
    {
        try
        {
            if (ShouldSkipTransparencyUpdate(transparencyLevel))
                return;

            LogTransparencyUpdate(transparencyLevel);
            SetTransparencyValue(transparencyLevel);
            NotifyTransparencyChanged();
            UpdateRendererTransparency();
        }
        catch (Exception ex)
        {
            LogTransparencyUpdateError(ex);
        }
    }

    private static bool ShouldSkipTransparencyUpdate(float transparencyLevel, float currentValue) =>
        Math.Abs(currentValue - transparencyLevel) < float.Epsilon;

    private bool ShouldSkipTransparencyUpdate(float transparencyLevel) =>
        ShouldSkipTransparencyUpdate(transparencyLevel, _currentTransparencyValue);

    private static void LogTransparencyUpdate(float transparencyLevel) =>
        Log(LogLevel.Debug, LOG_PREFIX, $"Updating transparency to {transparencyLevel}");

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

    private static void LogTransparencyUpdateError(Exception ex) =>
        Log(LogLevel.Error, LOG_PREFIX, $"Error setting transparency: {ex.Message}");

    private void CheckTransparencyTimeout(object? sender, EventArgs e)
    {
        if (_disposed) return;

        lock (_stateLock)
        {
            if (!_isTransparencyActive)
                return;

            if (HasTimeoutExpired())
            {
                DeactivateTransparency();
            }
        }
    }

    private bool HasTimeoutExpired()
    {
        TimeSpan elapsed = Now - _lastMouseActivity;
        return elapsed.TotalSeconds >= OVERLAY_TIMEOUT_SECONDS;
    }

    private void DeactivateTransparency()
    {
        _isTransparencyActive = false;
        UpdateTransparency(INACTIVE_TRANSPARENCY);
    }

    public void EnableGlobalMouseTracking()
    {
        if (_isMouseHookActive) return;

        InstallMouseHook();
        MarkMouseHookActive();
        LogMouseTrackingEnabled();
    }

    public void DisableGlobalMouseTracking()
    {
        if (!_isMouseHookActive) return;

        UninstallMouseHook();
        MarkMouseHookInactive();
        LogMouseTrackingDisabled();
    }

    private void InstallMouseHook() =>
        _mouseHook = SetWindowsHookEx(WH_MOUSE_LL, _mouseHookDelegate, GetModuleHandle(null), 0);

    private void UninstallMouseHook() =>
        UnhookWindowsHookEx(_mouseHook);

    private void MarkMouseHookActive() =>
        _isMouseHookActive = true;

    private void MarkMouseHookInactive() =>
        _isMouseHookActive = false;

    private static void LogMouseTrackingEnabled() =>
        Log(LogLevel.Debug, LOG_PREFIX, "Global mouse tracking enabled");

    private static void LogMouseTrackingDisabled() =>
        Log(LogLevel.Debug, LOG_PREFIX, "Global mouse tracking disabled");

    private nint MouseHookCallback(int nCode, nint wParam, nint lParam)
    {
        ProcessMouseMessage(nCode, wParam);
        return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    private void ProcessMouseMessage(int nCode, nint wParam)
    {
        if (nCode >= 0 && wParam == (nint)WM_MOUSEMOVE)
        {
            OnMouseMove();
        }
    }

    public static bool RequiresRedraw() => true;

    public void Dispose()
    {
        if (_disposed) return;

        CleanupResources();
        MarkAsDisposed();
        LogDisposal();
    }

    private void CleanupResources()
    {
        DisableGlobalMouseTracking();
        StopTransparencyTimer();
    }

    private void StopTransparencyTimer() =>
        _transparencyTimer?.Stop();

    private void MarkAsDisposed() =>
        _disposed = true;

    private static void LogDisposal() =>
        Log(LogLevel.Debug, LOG_PREFIX, "Disposed");
}