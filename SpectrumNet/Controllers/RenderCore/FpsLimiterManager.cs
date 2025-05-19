#nullable enable

namespace SpectrumNet.Controllers.RenderCore;

public sealed class FpsLimiterManager : IFpsLimiter
{
    private const string LogPrefix = nameof(FpsLimiterManager);
    private readonly ISmartLogger _logger = Instance;

    private readonly ISettings _settings;
    private readonly FpsLimiter _fpsLimiter = FpsLimiter.Instance;

    public event EventHandler<bool>? LimitChanged;

    public FpsLimiterManager(ISettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _fpsLimiter = FpsLimiter.Instance;
        _fpsLimiter.IsEnabled = _settings.LimitFpsTo60;
    }

    public bool IsLimited => _settings.LimitFpsTo60;

    public void SetLimit(bool enabled)
    {
        if (_settings.LimitFpsTo60 == enabled)
            return;

        _logger.Safe(() =>
        {
            _settings.LimitFpsTo60 = enabled;
            _fpsLimiter.IsEnabled = enabled;
            if (enabled)
                _fpsLimiter.Reset();
            LimitChanged?.Invoke(this, enabled);
            _logger.Log(LogLevel.Information,
                LogPrefix,
                $"FPS limit changed to: {(enabled ? "enabled" : "disabled")}");
        },
        LogPrefix,
        "Error setting FPS limit");
    }

    public void Reset() => _fpsLimiter.Reset();

    public bool ShouldRenderFrame() =>
        !_fpsLimiter.IsEnabled
        || _fpsLimiter.ShouldRenderFrame();
}