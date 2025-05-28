#nullable enable

namespace SpectrumNet.SN.Visualization.Abstract.Processing;

public class SpectrumSmoother : ISpectrumSmoother
{
    private const float DEFAULT_SMOOTHING_FACTOR = 0.3f;
    private static readonly bool _isHardwareAcceleratedCached = IsHardwareAccelerated;

    private float[]? _previousSpectrum;
    private float _smoothingFactor = DEFAULT_SMOOTHING_FACTOR;
    private readonly object _previousSpectrumLock = new();

    public float SmoothingFactor
    {
        get => _smoothingFactor;
        set => _smoothingFactor = Clamp(value, 0f, 1f);
    }

    public float[] SmoothSpectrum(
        float[] spectrum,
        int targetCount,
        float? customFactor = null)
    {
        float factor = customFactor ?? _smoothingFactor;
        var smoothed = new float[targetCount];

        lock (_previousSpectrumLock)
        {
            if (_previousSpectrum?.Length != targetCount)
            {
                _previousSpectrum = new float[targetCount];
                Array.Copy(spectrum, _previousSpectrum,
                    Math.Min(spectrum.Length, targetCount));
            }

            if (_isHardwareAcceleratedCached && targetCount >= Vector<float>.Count)
            {
                ApplyVectorizedSmoothing(spectrum, smoothed, targetCount, factor);
            }
            else
            {
                ApplySequentialSmoothing(spectrum, smoothed, targetCount, factor);
            }
        }

        return smoothed;
    }

    private void ApplyVectorizedSmoothing(
        float[] spectrum,
        float[] smoothed,
        int count,
        float smoothing)
    {
        int vecSize = Vector<float>.Count;
        int limit = count - (count % vecSize);
        float invSmoothing = 1 - smoothing;

        for (int i = 0; i < limit; i += vecSize)
        {
            var current = new Vector<float>(spectrum, i);
            var previous = new Vector<float>(_previousSpectrum!, i);
            var blended = previous * invSmoothing + current * smoothing;

            blended.CopyTo(smoothed, i);
            blended.CopyTo(_previousSpectrum!, i);
        }

        for (int i = limit; i < count; i++)
        {
            smoothed[i] = _previousSpectrum![i] =
                _previousSpectrum[i] * invSmoothing + spectrum[i] * smoothing;
        }
    }

    private void ApplySequentialSmoothing(
        float[] spectrum,
        float[] smoothed,
        int count,
        float smoothing)
    {
        float invSmoothing = 1 - smoothing;

        for (int i = 0; i < count; i++)
        {
            smoothed[i] = _previousSpectrum![i] =
                _previousSpectrum[i] * invSmoothing + spectrum[i] * smoothing;
        }
    }

    public void Reset()
    {
        lock (_previousSpectrumLock)
        {
            _previousSpectrum = null;
        }
    }
}