#nullable enable

namespace SpectrumNet.Views.Utils;

public class RendererTransparencyManager : ITransparencyManager, IDisposable
{
    private static readonly Lazy<RendererTransparencyManager> _instance =
        new(() => new RendererTransparencyManager());

    private const string LOG_PREFIX = "RendererTransparencyManager";
    private const float OVERLAY_TIMEOUT_SECONDS = 3.0f;

    public const float
        ACTIVE_TRANSPARENCY = 0.75f,
        INACTIVE_TRANSPARENCY = 1.0f;

    public static RendererTransparencyManager Instance => _instance.Value;

    private DateTime _lastMouseActivity = DateTime.MinValue;
    private bool _isTransparencyActive;

    private readonly object _stateLock = new();
    private readonly DispatcherTimer _transparencyTimer;
    private IRendererFactory? _rendererFactory;
    private bool _disposed;
    private float _currentTransparencyValue = ACTIVE_TRANSPARENCY;

    public float CurrentTransparency => _currentTransparencyValue;
    public bool IsActive => _isTransparencyActive;

    public event Action<float>? TransparencyChanged;

    private RendererTransparencyManager()
    {
        _transparencyTimer = new DispatcherTimer
        {
            Interval = FromMilliseconds(100)
        };
        _transparencyTimer.Tick += CheckTransparencyTimeout;
        _transparencyTimer.Start();

        Log(LogLevel.Information,
            LOG_PREFIX,
            "Initialized");
    }

    public void SetRendererFactory(IRendererFactory factory) => 
        _rendererFactory ??= factory;

    public void OnMouseEnter()
    {
        RecordMouseActivity();
        ActivateTransparency();
    }

    public void OnMouseLeave() => RecordMouseActivity();

    public void OnMouseMove()
    {
        RecordMouseActivity();
        ActivateTransparency();
    }

    public void ActivateTransparency()
    {
        lock (_stateLock)
        {
            _isTransparencyActive = true;
            UpdateTransparency(ACTIVE_TRANSPARENCY);
        }
    }

    private void RecordMouseActivity()
    {
        lock (_stateLock)
        {
            _lastMouseActivity = Now;
        }
    }

    private void CheckTransparencyTimeout(object? sender, EventArgs e)
    {
        if (_disposed) return;

        lock (_stateLock)
        {
            if (!_isTransparencyActive)
                return;

            TimeSpan elapsed = Now - _lastMouseActivity;
            if (elapsed.TotalSeconds >= OVERLAY_TIMEOUT_SECONDS)
            {
                _isTransparencyActive = false;
                UpdateTransparency(INACTIVE_TRANSPARENCY);
            }
        }
    }

    private void UpdateTransparency(float transparencyLevel)
    {
        try
        {
            if (Math.Abs(_currentTransparencyValue - transparencyLevel) < float.Epsilon)
                return;

            Log(LogLevel.Debug,
                LOG_PREFIX,
                $"Updating transparency to {transparencyLevel}");

            _currentTransparencyValue = transparencyLevel;
            TransparencyChanged?.Invoke(transparencyLevel);

            NotifyRenderers(transparencyLevel);
        }
        catch (Exception ex)
        {
            Log(LogLevel.Error,
                LOG_PREFIX,
                $"Error setting transparency: {ex.Message}");
        }
    }

    private void NotifyRenderers(float transparencyLevel)
    {
        if (_rendererFactory == null)
            return;

        var renderers = _rendererFactory.GetAllRenderers();
        foreach (var renderer in renderers)
        {
            renderer.SetOverlayTransparency(transparencyLevel);
        }
    }

    public static bool RequiresRedraw() => true;

    public void Dispose()
    {
        if (_disposed) return;
        _transparencyTimer?.Stop();
        _disposed = true;
        Log(LogLevel.Debug, LOG_PREFIX, "Disposed");
    }
}