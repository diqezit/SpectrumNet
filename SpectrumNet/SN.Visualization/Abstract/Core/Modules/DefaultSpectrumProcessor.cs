#nullable enable

namespace SpectrumNet.SN.Visualization.Abstract.Core.Modules;

public class DefaultSpectrumProcessor : ISpectrumProcessor
{
    private float _smoothingFactor = 0.3f;
    private float[]? _previousSpectrum;
    private readonly object _lock = new();

    public float SmoothingFactor
    {
        get => _smoothingFactor;
        set => _smoothingFactor = Clamp(value, 0f, 1f);
    }

    public (bool isValid, float[]? processedSpectrum) ProcessSpectrum(
        float[]? spectrum,
        int targetCount,
        int spectrumLength,
        float? customSmoothingFactor = null)
    {
        if (spectrum == null || spectrum.Length == 0 || targetCount <= 0)
            return (false, null);

        var result = new float[targetCount];
        float blockSize = spectrumLength / (float)targetCount;

        for (int i = 0; i < targetCount; i++)
        {
            int start = (int)(i * blockSize);
            int end = Min((int)((i + 1) * blockSize), spectrumLength);

            if (end > start)
            {
                float sum = 0f;
                for (int j = start; j < end && j < spectrum.Length; j++)
                    sum += spectrum[j];
                result[i] = sum / (end - start);
            }
        }

        lock (_lock)
        {
            if (_previousSpectrum?.Length != targetCount)
            {
                _previousSpectrum = new float[targetCount];
                Array.Copy(result, _previousSpectrum, Min(result.Length, targetCount));
            }

            float factor = customSmoothingFactor ?? _smoothingFactor;
            float invFactor = 1 - factor;

            for (int i = 0; i < targetCount; i++)
            {
                _previousSpectrum[i] = _previousSpectrum[i] * invFactor + result[i] * factor;
                result[i] = _previousSpectrum[i];
            }
        }

        return (true, result);
    }

    public float[] ProcessBands(float[] spectrum, int bandCount)
    {
        if (spectrum.Length == 0 || bandCount <= 0)
            return [];

        var bands = new float[bandCount];
        int bandSize = Max(1, spectrum.Length / bandCount);

        for (int i = 0; i < bandCount; i++)
        {
            int start = i * bandSize;
            int end = Min((i + 1) * bandSize, spectrum.Length);

            float sum = 0f;
            for (int j = start; j < end; j++)
                sum += spectrum[j];

            bands[i] = sum / (end - start);
        }

        return bands;
    }

    public void Dispose()
    {
        _previousSpectrum = null;
        SuppressFinalize(this);
    }
}
