#nullable enable

namespace SpectrumNet.SN.Visualization.Abstract.Processing;

public class SpectrumProcessingCoordinator(
    ISpectrumScaler? scaler = null,
    ISpectrumSmoother? smoother = null) : ISpectrumProcessingCoordinator
{
    private readonly ISmartLogger _logger = Instance;
    private readonly SemaphoreSlim _spectrumSemaphore = new(1, 1);
    private readonly object _spectrumLock = new();
    private readonly ISpectrumScaler _scaler = scaler ?? new SpectrumScaler();
    private readonly ISpectrumSmoother _smoother = smoother ?? new SpectrumSmoother();

    private float[]? _processedSpectrum;
    private bool _disposed;

    public void SetSmoothingFactor(float factor) =>
        _smoother.SmoothingFactor = factor;

    public (bool isValid, float[]? processedSpectrum) PrepareSpectrum(
        float[]? spectrum,
        int targetCount,
        int spectrumLength)
    {
        if (spectrum == null || spectrum.Length == 0 || targetCount <= 0)
            return (false, null);

        if (_spectrumSemaphore.Wait(0))
        {
            try
            {
                var scaled = _scaler.ScaleSpectrum(spectrum, targetCount, spectrumLength);
                _processedSpectrum = _smoother.SmoothSpectrum(scaled, targetCount);
            }
            finally
            {
                _spectrumSemaphore.Release();
            }
        }

        lock (_spectrumLock)
        {
            if (_processedSpectrum?.Length != targetCount)
            {
                var scaled = _scaler.ScaleSpectrum(spectrum, targetCount, spectrumLength);
                _processedSpectrum = _smoother.SmoothSpectrum(scaled, targetCount);
            }
            return (true, _processedSpectrum);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        _logger.Safe(() =>
        {
            _spectrumSemaphore.Dispose();
            _smoother.Reset();
            _processedSpectrum = null;
            _disposed = true;
        }, nameof(SpectrumProcessingCoordinator), "Error during disposal");

        GC.SuppressFinalize(this);
    }
}