#nullable enable

namespace SpectrumNet.SN.Visualization.Core;

public sealed class SpectrumDataProcessor
    (IMainController controller)
    : ISpectrumDataProcessor
{
    private const string LogPrefix = nameof(SpectrumDataProcessor);
    private readonly ISmartLogger _logger = Instance;

    private readonly IMainController _controller = controller ??
        throw new ArgumentNullException(nameof(controller));

    private readonly SemaphoreSlim _spectrumSemaphore = new(1, 1);
    private readonly object _spectrumLock = new();

    private float[]?
        _previousSpectrum,
        _processedSpectrum;

    private float _smoothingFactor = 0.3f;
    private bool _isOverlayActive;

    public void Configure(bool isOverlayActive)
    {
        _isOverlayActive = isOverlayActive;
        _smoothingFactor = isOverlayActive ? 0.5f : 0.3f;
    }

    public SpectralData? GetCurrentSpectrum()
    {
        var analyzer = _controller.GetCurrentAnalyzer();
        return analyzer?.GetCurrentSpectrum();
    }

    public bool RequiresRedraw() => _isOverlayActive || _controller.IsRecording;

    public float[] ProcessSpectrum(float[] spectrum, int targetCount)
    {
        bool locked = false;
        try
        {
            locked = _spectrumSemaphore.Wait(0);
            if (locked)
                PerformSpectrumProcessing(spectrum, targetCount, spectrum.Length);

            lock (_spectrumLock)
                return GetProcessedSpectrum(spectrum, targetCount, spectrum.Length);
        }
        finally
        {
            if (locked)
                _spectrumSemaphore.Release();
        }
    }

    private void PerformSpectrumProcessing(float[] spectrum, int count, int length)
    {
        var scaled = ScaleSpectrum(spectrum, count, length);
        _processedSpectrum = SmoothSpectrum(scaled, count);
    }

    private float[] GetProcessedSpectrum(float[] spectrum, int count, int length)
    {
        if (_processedSpectrum != null && _processedSpectrum.Length == count)
            return _processedSpectrum;

        var scaled = ScaleSpectrum(spectrum, count, length);
        _processedSpectrum = SmoothSpectrum(scaled, count);
        return _processedSpectrum;
    }

    private static float[] ScaleSpectrum(float[] spectrum, int targetCount, int spectrumLength)
    {
        var result = new float[targetCount];
        float blockSize = spectrumLength / (float)targetCount;

        for (int i = 0; i < targetCount; i++)
        {
            int start = (int)(i * blockSize);
            int end = Min((int)((i + 1) * blockSize), spectrumLength);
            result[i] = end > start
                ? CalculateBlockAverage(spectrum, start, end)
                : 0;
        }

        return result;
    }

    private float[] SmoothSpectrum(float[] spectrum, int targetCount)
    {
        if (_previousSpectrum == null || _previousSpectrum.Length != targetCount)
        {
            _previousSpectrum = new float[targetCount];
            if (spectrum.Length >= targetCount)
                Array.Copy(spectrum, _previousSpectrum, targetCount);
        }

        var smoothed = new float[targetCount];

        for (int i = 0; i < targetCount; i++)
        {
            float current = spectrum[i];
            float prevValue = _previousSpectrum[i];
            float result = prevValue * (1 - _smoothingFactor) + current * _smoothingFactor;
            smoothed[i] = result;
            _previousSpectrum[i] = result;
        }

        return smoothed;
    }

    [MethodImpl(AggressiveInlining)]
    private static float CalculateBlockAverage(float[] spectrum, int start, int end)
    {
        float sum = 0;
        for (int i = start; i < end; i++)
            sum += spectrum[i];
        return sum / (end - start);
    }
}