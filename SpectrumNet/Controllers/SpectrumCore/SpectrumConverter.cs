#nullable enable

namespace SpectrumNet.Controllers.SpectrumCore;

public sealed class SpectrumConverter : ISpectrumConverter
{
    private const string LogSource = nameof(SpectrumConverter);
    private readonly IGainParametersProvider _params;
    private readonly ParallelOptions _parallelOpts = new()
    {
        MaxDegreeOfParallelism = ProcessorCount
    };
    private readonly ArrayPool<float> _floatArrayPool = ArrayPool<float>.Shared;

    public SpectrumConverter(IGainParametersProvider? parameters) =>
        _params = parameters
            ?? throw new ArgumentNullException(nameof(parameters));

    public float[] ConvertToSpectrum(Complex[] fft, int sampleRate,
        SpectrumScale scale, CancellationToken cancellationToken = default)
    {
        if (fft is null)
            throw new ArgumentNullException(nameof(fft));
        if (sampleRate <= 0)
            throw new ArgumentException("Invalid sample rate", nameof(sampleRate));
        int nBins = fft.Length / 2 + 1;
        float[] spectrum = _floatArrayPool.Rent(nBins);
        Array.Clear(spectrum, 0, nBins);
        SpectrumParameters spectrumParams =
            SpectrumParameters.FromProvider(_params);
        _parallelOpts.CancellationToken = cancellationToken;
        try
        {
            return scale switch
            {
                SpectrumScale.Linear =>
                    ProcessLinear(fft, spectrum, nBins, spectrumParams),
                SpectrumScale.Logarithmic =>
                    ProcessScale(fft, spectrum, nBins, sampleRate,
                        MathF.Log10(1f), MathF.Log10(sampleRate / 2f),
                        x => MathF.Pow(10, x), spectrumParams),
                SpectrumScale.Mel =>
                    ProcessScale(fft, spectrum, nBins, sampleRate,
                        FreqToMel(1f), FreqToMel(sampleRate / 2f),
                        MelToFreq, spectrumParams),
                SpectrumScale.Bark =>
                    ProcessScale(fft, spectrum, nBins, sampleRate,
                        FreqToBark(1f), FreqToBark(sampleRate / 2f),
                        BarkToFreq, spectrumParams),
                SpectrumScale.ERB =>
                    ProcessScale(fft, spectrum, nBins, sampleRate,
                        FreqToERB(1f), FreqToERB(sampleRate / 2f),
                        ERBToFreq, spectrumParams),
                _ => ProcessLinear(fft, spectrum, nBins, spectrumParams)
            };
        }
        catch
        {
            _floatArrayPool.Return(spectrum);
            throw;
        }
    }

    private float[] ProcessLinear(
        Complex[] fft,
        float[] spectrum,
        int nBins,
        SpectrumParameters spectrumParams
    ) =>
        SafeResult(() =>
        {
            if (nBins < 100)
            {
                for (int i = 0; i < nBins; i++)
                {
                    if (_parallelOpts.CancellationToken.IsCancellationRequested)
                        break;
                    spectrum[i] = InterpolateSpectrumValue(
                        fft, i, nBins, spectrumParams);
                }
            }
            else
            {
                Parallel.For(0, nBins, _parallelOpts, i =>
                    spectrum[i] = InterpolateSpectrumValue(
                        fft, i, nBins, spectrumParams));
            }
            float[] result = new float[nBins];
            Array.Copy(spectrum, result, nBins);
            _floatArrayPool.Return(spectrum);
            return result;
        },
        defaultValue: Array.Empty<float>(),
        options: new ErrorHandlingOptions
        {
            Source = LogSource,
            ErrorMessage = "Error processing linear spectrum",
            IgnoreExceptions = new[] { typeof(OperationCanceledException) }
        });

    private float[] ProcessScale(
        Complex[] fft,
        float[] spectrum,
        int nBins,
        int sampleRate,
        float minDomain,
        float maxDomain,
        Func<float, float> domainToFreq,
        SpectrumParameters spectrumParams
    ) =>
        SafeResult(() =>
        {
            float step = (maxDomain - minDomain) / (nBins - 1);
            float halfSampleRate = sampleRate * 0.5f;
            Parallel.For(0, nBins, _parallelOpts, i =>
            {
                if (_parallelOpts.CancellationToken.IsCancellationRequested)
                    return;
                float domainValue = minDomain + i * step;
                float freq = domainToFreq(domainValue);
                int bin = Clamp(
                    (int)MathF.Round(freq / halfSampleRate * (nBins - 1)),
                    0, nBins - 1);
                spectrum[i] = CalcValue(Magnitude(fft[bin]), spectrumParams);
            });
            float[] result = new float[nBins];
            Array.Copy(spectrum, result, nBins);
            _floatArrayPool.Return(spectrum);
            return result;
        },
        defaultValue: Array.Empty<float>(),
        options: new ErrorHandlingOptions
        {
            Source = LogSource,
            ErrorMessage = "Error processing non-linear spectrum",
            IgnoreExceptions = new[] { typeof(OperationCanceledException) }
        });

    private float InterpolateSpectrumValue(
        Complex[] fft,
        int index,
        int nBins,
        SpectrumParameters spectrumParams
    )
    {
        float centerMag = Magnitude(fft[index]);
        float leftMag = index > 0 ? Magnitude(fft[index - 1]) : centerMag;
        float rightMag = index < nBins - 1 ? Magnitude(fft[index + 1]) : centerMag;
        float interpolatedMag = (leftMag + centerMag + rightMag) / 3f;
        return interpolatedMag <= 0
            ? 0f
            : NormalizeDb(interpolatedMag, spectrumParams);
    }

    private static float CalcValue(
        float mag,
        SpectrumParameters spectrumParams
    ) =>
        mag <= 0f ? 0f : NormalizeDb(mag, spectrumParams);

    private static float NormalizeDb(
        float magnitude,
        SpectrumParameters spectrumParams
    )
    {
        float db = 10f * Constants.InvLog10 * MathF.Log(magnitude);
        float norm = Clamp(
            (db - spectrumParams.MinDb) / spectrumParams.DbRange, 0f, 1f);
        return norm < 1e-6f ? 0f : MathF.Pow(norm, spectrumParams.AmplificationFactor);
    }

    private static float Magnitude(Complex c) =>
        c.X * c.X + c.Y * c.Y;

    private static float FreqToMel(float freq) =>
        2595f * MathF.Log10(1 + freq / 700f);

    private static float MelToFreq(float mel) =>
        700f * (MathF.Pow(10, mel / 2595f) - 1);

    private static float FreqToBark(float freq) =>
        13f * MathF.Atan(0.00076f * freq) +
        3.5f * MathF.Atan(MathF.Pow(freq / 7500f, 2));

    private static float BarkToFreq(float bark) =>
        1960f * (bark + 0.53f) / (26.28f - bark);

    private static float FreqToERB(float freq) =>
        21.4f * MathF.Log10(0.00437f * freq + 1);

    private static float ERBToFreq(float erb) =>
        (MathF.Pow(10, erb / 21.4f) - 1) / 0.00437f;
}