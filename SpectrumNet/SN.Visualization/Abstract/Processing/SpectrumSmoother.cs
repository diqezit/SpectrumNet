// SN.Visualization/Abstract/Processing/SpectrumSmoother.cs
namespace SpectrumNet.SN.Visualization.Abstract.Processing;

// сглаживание спектра
public interface ISpectrumSmoother
{
    float SmoothingFactor { get; set; }
    float[] SmoothSpectrum(float[] spectrum, int targetCount, float? customFactor = null);
    void Reset();
}

public class SpectrumSmoother : ISpectrumSmoother
{
    private const float 
        DEFAULT_SMOOTHING_FACTOR = 0.3f,
        MIN_SMOOTHING_FACTOR = 0.0f,
        MAX_SMOOTHING_FACTOR = 1.0f;

    private static readonly bool _isHardwareAcceleratedCached = IsHardwareAccelerated;

    private float[]? _previousSpectrum;
    private float _smoothingFactor = DEFAULT_SMOOTHING_FACTOR;

    public float SmoothingFactor
    {
        get => _smoothingFactor;
        set => _smoothingFactor = ClampSmoothingFactor(value);
    }

    public float[] SmoothSpectrum(
        float[] spectrum,
        int targetCount,
        float? customFactor = null)
    {
        float factor = GetEffectiveSmoothingFactor(customFactor);

        EnsurePreviousSpectrumInitialized(spectrum, targetCount);

        var smoothed = CreateSmoothingResultArray(targetCount);

        ApplySmoothing(spectrum, smoothed, targetCount, factor);

        return smoothed;
    }

    public void Reset() => ClearPreviousSpectrum();
    
    private static float ClampSmoothingFactor(float value) =>
        Math.Clamp(value, MIN_SMOOTHING_FACTOR, MAX_SMOOTHING_FACTOR);

    private float GetEffectiveSmoothingFactor(float? customFactor) =>
        customFactor ?? _smoothingFactor;

    private void EnsurePreviousSpectrumInitialized(float[] spectrum, int targetCount)
    {
        if (ShouldInitializePreviousSpectrum(targetCount))
            InitializePreviousSpectrum(spectrum, targetCount);
    }

    private bool ShouldInitializePreviousSpectrum(int targetCount) =>
        _previousSpectrum == null || _previousSpectrum.Length != targetCount;

    private void InitializePreviousSpectrum(float[] spectrum, int targetCount)
    {
        _previousSpectrum = CreatePreviousSpectrumArray(targetCount);
        CopyInitialValues(spectrum, _previousSpectrum, targetCount);
    }

    private static float[] CreatePreviousSpectrumArray(int targetCount) =>
        new float[targetCount];

    private static void CopyInitialValues(float[] source, float[] destination, int count)
    {
        int copyCount = Math.Min(source.Length, count);
        Array.Copy(source, destination, copyCount);
    }

    private static float[] CreateSmoothingResultArray(int targetCount) =>
        new float[targetCount];

    private void ApplySmoothing(
        float[] spectrum,
        float[] smoothed,
        int count,
        float factor)
    {
        if (ShouldUseVectorizedSmoothing(count))
            ApplyVectorizedSmoothing(spectrum, smoothed, count, factor);
        else
            ApplySequentialSmoothing(spectrum, smoothed, count, factor);
    }

    private static bool ShouldUseVectorizedSmoothing(int count) =>
        _isHardwareAcceleratedCached && count >= Vector<float>.Count;

    private void ApplyVectorizedSmoothing(
        float[] spectrum,
        float[] smoothed,
        int count,
        float smoothing)
    {
        ValidatePreviousSpectrum();

        int processedCount = ProcessVectorizedBlocks(spectrum, smoothed, count, smoothing);

        if (HasRemainingElements(processedCount, count))
            ProcessRemainingElements(spectrum, smoothed, count, smoothing, processedCount);
    }

    private void ValidatePreviousSpectrum()
    {
        if (_previousSpectrum == null)
            throw new InvalidOperationException("Previous spectrum not initialized");
    }

    private int ProcessVectorizedBlocks(
        float[] spectrum,
        float[] smoothed,
        int count,
        float smoothing)
    {
        int vecSize = Vector<float>.Count;
        int limit = CalculateVectorizedLimit(count, vecSize);

        for (int i = 0; i < limit; i += vecSize)
        {
            ProcessVectorBlock(spectrum, smoothed, i, smoothing);
        }

        return limit;
    }

    private static int CalculateVectorizedLimit(int count, int vectorSize) =>
        count - (count % vectorSize);

    private void ProcessVectorBlock(
        float[] spectrum,
        float[] smoothed,
        int startIndex,
        float smoothing)
    {
        var currentVector = LoadVector(spectrum, startIndex);
        var previousVector = LoadVector(_previousSpectrum!, startIndex);

        var blendedVector = BlendVectors(currentVector, previousVector, smoothing);

        StoreVector(blendedVector, smoothed, startIndex);
        StoreVector(blendedVector, _previousSpectrum!, startIndex);
    }

    private static Vector<float> LoadVector(float[] array, int index) =>
        new(array, index);

    private static Vector<float> BlendVectors(
        Vector<float> current,
        Vector<float> previous,
        float smoothing)
    {
        float inverseSmoothingFactor = 1 - smoothing;
        return previous * inverseSmoothingFactor + current * smoothing;
    }

    private static void StoreVector(Vector<float> vector, float[] array, int index) =>
        vector.CopyTo(array, index);

    private static bool HasRemainingElements(int processedCount, int totalCount) =>
        processedCount < totalCount;

    private void ProcessRemainingElements(
        float[] spectrum,
        float[] smoothed,
        int count,
        float smoothing,
        int startIndex)
    {
        ApplySequentialSmoothing(spectrum, smoothed, count, smoothing, startIndex);
    }

    private void ApplySequentialSmoothing(
        float[] spectrum,
        float[] smoothed,
        int count,
        float smoothing,
        int startIndex = 0)
    {
        ValidatePreviousSpectrum();

        for (int i = startIndex; i < count; i++)
        {
            ProcessSingleElement(spectrum, smoothed, i, smoothing);
        }
    }

    private void ProcessSingleElement(
        float[] spectrum,
        float[] smoothed,
        int index,
        float smoothing)
    {
        float currentValue = spectrum[index];
        float previousValue = _previousSpectrum![index];

        float smoothedValue = CalculateSmoothedValue(currentValue, previousValue, smoothing);

        StoreSmoothedValue(smoothed, index, smoothedValue);
        UpdatePreviousValue(index, smoothedValue);
    }

    private static float CalculateSmoothedValue(
        float current,
        float previous,
        float smoothing)
    {
        float inverseSmoothingFactor = 1 - smoothing;
        return previous * inverseSmoothingFactor + current * smoothing;
    }

    private static void StoreSmoothedValue(float[] array, int index, float value) =>
        array[index] = value;

    private void UpdatePreviousValue(int index, float value) =>
        _previousSpectrum![index] = value;

    private void ClearPreviousSpectrum() =>
        _previousSpectrum = null;
}