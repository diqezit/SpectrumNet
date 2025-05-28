#nullable enable

namespace SpectrumNet.SN.Visualization.Abstract.Processing;

public class SpectrumScaler : ISpectrumScaler
{
    private const int PARALLEL_BATCH_SIZE = 32;
    private static readonly bool _isHardwareAcceleratedCached = IsHardwareAccelerated;
    private static readonly ParallelOptions _parallelOptions = new()
    {
        MaxDegreeOfParallelism = Environment.ProcessorCount
    };

    public float[] ScaleSpectrum(float[] spectrum, int targetCount, int spectrumLength)
    {
        var result = new float[targetCount];
        float blockSize = spectrumLength / (float)targetCount;

        if (targetCount >= PARALLEL_BATCH_SIZE && _isHardwareAcceleratedCached)
        {
            Parallel.For(0, targetCount, _parallelOptions, i =>
                ProcessBlock(spectrum, result, i, spectrumLength, blockSize));
        }
        else
        {
            for (int i = 0; i < targetCount; i++)
                ProcessBlock(spectrum, result, i, spectrumLength, blockSize);
        }

        return result;
    }

    private static void ProcessBlock(
        float[] spectrum,
        float[] target,
        int index,
        int spectrumLength,
        float blockSize)
    {
        int start = (int)(index * blockSize);
        int end = Min((int)((index + 1) * blockSize), spectrumLength);

        if (end <= start)
        {
            target[index] = 0f;
            return;
        }

        float sum = 0f;
        for (int i = start; i < end && i < spectrum.Length; i++)
            sum += spectrum[i];

        target[index] = sum / (end - start);
    }
}

