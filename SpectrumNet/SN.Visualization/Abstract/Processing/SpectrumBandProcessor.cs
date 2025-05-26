#nullable enable

namespace SpectrumNet.SN.Visualization.Abstract.Processing;

public interface ISpectrumBandProcessor
{
    float[] ProcessBands(float[] spectrum, int bandCount);
    float GetBandAverage(float[] spectrum, int start, int end);
}

public class SpectrumBandProcessor : ISpectrumBandProcessor
{
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
            bands[i] = GetBandAverage(spectrum, start, end);
        }

        return bands;
    }

    public float GetBandAverage(float[] spectrum, int start, int end)
    {
        if (start >= end) return 0f;

        float sum = 0f;
        for (int i = start; i < end; i++)
            sum += spectrum[i];

        return sum / (end - start);
    }
}