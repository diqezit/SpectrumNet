namespace SpectrumNet.SN.Spectrum;

public static class StereoProcessor
{
    public static float[] ToMono(ReadOnlySpan<float> stereo, int channels, StereoMode mode = StereoMode.Mid)
    {
        if (channels == 1) return stereo.ToArray();
        if (channels <= 0) throw new ArgumentException("Invalid channels", nameof(channels));

        int frames = stereo.Length / channels;
        float[] res = new float[frames];

        for (int i = 0; i < frames; i++)
        {
            float l = stereo[i * channels], r = stereo[i * channels + 1];
            res[i] = mode switch
            {
                StereoMode.Left => l,
                StereoMode.Right => r,
                StereoMode.Max => Max(Abs(l), Abs(r)) * Sign(l + r),
                StereoMode.RMS => MathF.Sqrt((l * l + r * r) * 0.5f),
                _ => (l + r) * 0.5f
            };
        }

        return res;
    }
}

internal static class SpectrumMath
{
    public static float[] Convert(double[] mag, int rate, SpectrumScale scale, IGainParametersProvider gain)
    {
        int n = mag.Length;
        float[] res = new float[n];
        float minDb = gain.MinDbValue, amp = gain.AmplificationFactor;
        float dbRange = Max(gain.MaxDbValue - minDb, float.Epsilon);

        float Norm(double m)
        {
            if (m <= 0) return 0;
            float v = (float)Clamp((20 * Log10(m + 1e-10) - minDb) / dbRange, 0, 1);
            return v < 1e-6f ? 0 : MathF.Pow(v, amp);
        }

        if (scale == SpectrumScale.Linear)
        {
            for (int i = 0; i < n; i++)
            {
                double prev = i > 0 ? mag[i - 1] : mag[i];
                double next = i < n - 1 ? mag[i + 1] : mag[i];
                res[i] = Norm((prev + mag[i] + next) / 3.0);
            }
            return res;
        }

        double maxF = rate / 2.0, binW = maxF / n;
        double sMin = ToScale(20.0, scale), sMax = ToScale(maxF, scale);
        double step = (sMax - sMin) / (n - 1);

        for (int i = 0; i < n; i++)
        {
            int bin = Clamp((int)(FromScale(sMin + i * step, scale) / binW), 0, n - 1);
            res[i] = Norm(mag[bin]);
        }

        return res;
    }

    private static double ToScale(double f, SpectrumScale s) => s switch
    {
        SpectrumScale.Logarithmic => Log10(f),
        SpectrumScale.Mel => 2595.0 * Log10(1 + f / 700.0),
        SpectrumScale.Bark => 13 * Atan(0.00076 * f) + 3.5 * Atan(Pow(f / 7500, 2)),
        SpectrumScale.ERB => 21.4 * Log10(0.00437 * f + 1),
        _ => f
    };

    private static double FromScale(double v, SpectrumScale s) => s switch
    {
        SpectrumScale.Logarithmic => Pow(10, v),
        SpectrumScale.Mel => 700.0 * (Pow(10, v / 2595.0) - 1),
        SpectrumScale.Bark => 1960 * (v + 0.53) / (26.28 - v),
        SpectrumScale.ERB => (Pow(10, v / 21.4) - 1) / 0.00437,
        _ => v
    };
}

internal sealed class SpectrumDataProcessor
{
    private readonly AppController _ctrl;
    private bool _isOverlay;

    public SpectrumDataProcessor(AppController ctrl) =>
        _ctrl = ctrl ?? throw new ArgumentNullException(nameof(ctrl));

    public SpectrumAnalyzer Analyzer =>
        _ctrl.Audio.GetCurrentProvider() as SpectrumAnalyzer
            ?? throw new InvalidOperationException("Analyzer not available");

    public void Configure(bool isOverlay) => _isOverlay = isOverlay;

    public SpectralData? GetCurrentSpectrum() =>
        (_ctrl.Audio.GetCurrentProvider() as SpectrumAnalyzer)?.GetSpectrum();

    public bool RequiresRedraw() => _isOverlay;
}
