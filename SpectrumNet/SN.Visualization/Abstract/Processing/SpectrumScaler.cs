#nullable enable

namespace SpectrumNet.SN.Visualization.Abstract.Processing;

public interface ISpectrumScaler
{
    float[] ScaleSpectrum(float[] spectrum, int targetCount, int spectrumLength);
}

public class SpectrumScaler : ISpectrumScaler
{
    private const int PARALLEL_BATCH_SIZE = 32;
    private const int MIN_PARALLEL_SIZE = 32;

    private static readonly bool _isHardwareAcceleratedCached = IsHardwareAccelerated;
    private static readonly ParallelOptions _parallelOptions = new()
    {
        MaxDegreeOfParallelism = Environment.ProcessorCount
    };

    public float[] ScaleSpectrum(float[] spectrum, int targetCount, int spectrumLength)
    {
        var result = CreateResultArray(targetCount);
        float blockSize = CalculateBlockSize(spectrumLength, targetCount);

        ProcessSpectrum(spectrum, result, targetCount, spectrumLength, blockSize);

        return result;
    }

    private static float[] CreateResultArray(int targetCount) =>
        new float[targetCount];

    private static float CalculateBlockSize(int spectrumLength, int targetCount) =>
        spectrumLength / (float)targetCount;

    private static void ProcessSpectrum(
        float[] spectrum,
        float[] result,
        int targetCount,
        int spectrumLength,
        float blockSize)
    {
        if (ShouldUseParallelProcessing(targetCount))
            ScaleSpectrumParallel(spectrum, result, targetCount, spectrumLength, blockSize);
        else
            ScaleSpectrumSequential(spectrum, result, targetCount, spectrumLength, blockSize);
    }

    private static bool ShouldUseParallelProcessing(int targetCount) =>
        targetCount >= PARALLEL_BATCH_SIZE && _isHardwareAcceleratedCached;

    private static void ScaleSpectrumParallel(
        float[] spectrum,
        float[] target,
        int count,
        int length,
        float blockSize)
    {
        if (!IsParallelProcessingViable(count))
        {
            ScaleSpectrumSequential(spectrum, target, count, length, blockSize);
            return;
        }

        ExecuteParallelScaling(spectrum, target, count, length, blockSize);
    }

    private static bool IsParallelProcessingViable(int count) =>
        count >= MIN_PARALLEL_SIZE;

    private static void ExecuteParallelScaling(
        float[] spectrum,
        float[] target,
        int count,
        int length,
        float blockSize)
    {
        Parallel.For(0, count, _parallelOptions, i =>
        {
            ProcessSingleBlock(spectrum, target, i, length, blockSize);
        });
    }

    private static void ScaleSpectrumSequential(
        float[] spectrum,
        float[] target,
        int count,
        int length,
        float blockSize)
    {
        for (int i = 0; i < count; i++)
        {
            ProcessSingleBlock(spectrum, target, i, length, blockSize);
        }
    }

    private static void ProcessSingleBlock(
        float[] spectrum,
        float[] target,
        int index,
        int spectrumLength,
        float blockSize)
    {
        var (start, end) = CalculateBlockBounds(index, blockSize, spectrumLength);
        target[index] = CalculateBlockValue(spectrum, start, end);
    }

    private static (int start, int end) CalculateBlockBounds(
        int index,
        float blockSize,
        int spectrumLength)
    {
        int start = CalculateBlockStart(index, blockSize);
        int end = CalculateBlockEnd(index, blockSize, spectrumLength);
        return (start, end);
    }

    private static int CalculateBlockStart(int index, float blockSize) =>
        (int)(index * blockSize);

    private static int CalculateBlockEnd(int index, float blockSize, int spectrumLength) =>
        Min((int)((index + 1) * blockSize), spectrumLength);

    private static float CalculateBlockValue(float[] spectrum, int start, int end) =>
        IsValidBlock(start, end)
            ? CalculateBlockAverage(spectrum, start, end)
            : 0f;

    private static bool IsValidBlock(int start, int end) =>
        end > start;

    [MethodImpl(AggressiveInlining)]
    private static float CalculateBlockAverage(
        float[] spectrum,
        int start,
        int end)
    {
        float sum = CalculateBlockSum(spectrum, start, end);
        int count = CalculateBlockCount(start, end);
        return count > 0 ? sum / count : 0f;
    }

    private static float CalculateBlockSum(
        float[] spectrum,
        int start,
        int end)
    {
        if (start < 0 || start >= spectrum.Length) return 0f;

        float sum = 0;
        int safeEnd = Min(end, spectrum.Length);
        for (int i = start; i < safeEnd; i++)
            sum += spectrum[i];
        return sum;
    }

    private static int CalculateBlockCount(int start, int end) =>
        end - start;
}