#nullable enable

namespace SpectrumNet.SN.Spectrum.Utils.Math;

public static class WindowGenerator
{
    private const string LogPrefix = nameof(WindowGenerator);
    private static readonly ISmartLogger _logger = Instance;
    private static readonly ConcurrentDictionary<(int Size, FftWindowType Type), float[]> _windowCache = new();

    public static float[] Generate(int size, FftWindowType type) =>
        _windowCache.GetOrAdd((size, type), key => CreateWindow(key.Size, key.Type));

    private static float[] CreateWindow(int size, FftWindowType type) =>
        _logger.SafeResult(() =>
        {
            float[] window = new float[size];
            (float[] cos, float[] sin) = TrigonometricTables.Get(size);
            var generator = GetWindowGenerator(window, size, type, cos);
            Parallel.For(0, size, generator);
            return window;
        },
        new float[size],
        LogPrefix,
        $"Error generating {type} window");

    private static Action<int> GetWindowGenerator(float[] window, int size, FftWindowType type, float[] cosTable) =>
        type switch
        {
            FftWindowType.Hann =>
                i => window[i] = 0.5f * (1f - cosTable[i]),
            FftWindowType.Hamming =>
                i => window[i] = 0.54f - 0.46f * cosTable[i],
            FftWindowType.Blackman =>
                i => window[i] = 0.42f - 0.5f * cosTable[i] +
                    0.08f * MathF.Cos(TWO_PI * 2 * i / (size - 1)),
            FftWindowType.Bartlett =>
                i => window[i] = 2f / (size - 1) * ((size - 1) / 2f -
                    MathF.Abs(i - (size - 1) / 2f)),
            FftWindowType.Kaiser =>
                i => window[i] = BesselI0(KAISER_BETA *
                    MathF.Sqrt(1 - MathF.Pow(2f * i / (size - 1) - 1, 2))) /
                    BesselI0(KAISER_BETA),
            _ => throw new NotSupportedException($"Unsupported window type: {type}")
        };

    private static float BesselI0(float x)
    {
        float sum = 1f, term = x * x / 4f;
        for (int k = 1; term > BESSEL_EPSILON; k++)
        {
            sum += term;
            term *= x * x / (4f * k * k);
        }
        return sum;
    }
}