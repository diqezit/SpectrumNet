#nullable enable

namespace SpectrumNet.SN.Visualization.Abstract.Processing;

// процесс обработки спектра
public interface ISpectrumProcessingCoordinator : IDisposable
{
    (bool isValid, float[]? processedSpectrum) PrepareSpectrum(
        float[]? spectrum,
        int targetCount,
        int spectrumLength);
    void SetSmoothingFactor(float factor);
}

public class SpectrumProcessingCoordinator(
    ISpectrumScaler? scaler = null,
    ISpectrumSmoother? smoother = null)
    : ISpectrumProcessingCoordinator
{
    private const string LogPrefix = nameof(SpectrumProcessingCoordinator);
    private const int SEMAPHORE_TIMEOUT_MS = 0;

    protected readonly ISmartLogger _logger = Instance;
    protected readonly SemaphoreSlim _spectrumSemaphore = new(1, 1);
    protected readonly object _spectrumLock = new();

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
        if (!ValidateInput(spectrum, targetCount))
            return (false, null);

        float[] processed = ProcessSpectrum(spectrum!, targetCount, spectrumLength);
        return (true, processed);
    }

    private static bool ValidateInput(float[]? spectrum, int targetCount) =>
        spectrum != null &&
        spectrum.Length > 0 &&
        targetCount > 0;

    private float[] ProcessSpectrum(
        float[] spectrum,
        int targetCount,
        int spectrumLength)
    {
        if (TryProcessWithLock(spectrum, targetCount, spectrumLength))
            return GetCachedOrProcessNew(spectrum, targetCount, spectrumLength);

        return ProcessWithoutLock(spectrum, targetCount, spectrumLength);
    }

    private bool TryProcessWithLock(
        float[] spectrum,
        int targetCount,
        int spectrumLength)
    {
        bool lockAcquired = TryAcquireSemaphore();

        if (!lockAcquired)
            return false;

        try
        {
            UpdateProcessedSpectrum(spectrum, targetCount, spectrumLength);
            return true;
        }
        finally
        {
            ReleaseSemaphore();
        }
    }

    private bool TryAcquireSemaphore() =>
        _spectrumSemaphore.Wait(SEMAPHORE_TIMEOUT_MS);

    private void ReleaseSemaphore() =>
        _spectrumSemaphore.Release();

    private void UpdateProcessedSpectrum(
        float[] spectrum,
        int targetCount,
        int spectrumLength)
    {
        var scaled = ScaleSpectrum(spectrum, targetCount, spectrumLength);
        _processedSpectrum = SmoothSpectrum(scaled, targetCount);
    }

    private float[] ScaleSpectrum(
        float[] spectrum,
        int targetCount,
        int spectrumLength) =>
        _scaler.ScaleSpectrum(spectrum, targetCount, spectrumLength);

    private float[] SmoothSpectrum(
        float[] scaled,
        int targetCount) =>
        _smoother.SmoothSpectrum(scaled, targetCount);

    private float[] GetCachedOrProcessNew(
        float[] spectrum,
        int targetCount,
        int spectrumLength)
    {
        lock (_spectrumLock)
        {
            if (IsCachedSpectrumValid(targetCount))
                return _processedSpectrum!;

            return CreateNewProcessedSpectrum(spectrum, targetCount, spectrumLength);
        }
    }

    private bool IsCachedSpectrumValid(int targetCount) =>
        _processedSpectrum != null &&
        _processedSpectrum.Length == targetCount;

    private float[] ProcessWithoutLock(
        float[] spectrum,
        int targetCount,
        int spectrumLength)
    {
        lock (_spectrumLock)
        {
            return CreateNewProcessedSpectrum(spectrum, targetCount, spectrumLength);
        }
    }

    private float[] CreateNewProcessedSpectrum(
        float[] spectrum,
        int targetCount,
        int spectrumLength)
    {
        var scaled = ScaleSpectrum(spectrum, targetCount, spectrumLength);
        _processedSpectrum = SmoothSpectrum(scaled, targetCount);
        return _processedSpectrum;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _logger.Safe(
            DisposeResources,
            LogPrefix,
            "Error during spectrum processor disposal");

        GC.SuppressFinalize(this);
    }

    private void DisposeResources()
    {
        DisposeSemaphore();
        ResetSmoother();
        ClearProcessedSpectrum();
        MarkAsDisposed();
    }

    private void DisposeSemaphore() =>
        _spectrumSemaphore.Dispose();

    private void ResetSmoother() =>
        _smoother.Reset();

    private void ClearProcessedSpectrum() =>
        _processedSpectrum = null;

    private void MarkAsDisposed() =>
        _disposed = true;
}