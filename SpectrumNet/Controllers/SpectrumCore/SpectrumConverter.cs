#nullable enable

namespace SpectrumNet.Controllers.SpectrumCore;

public sealed class SpectrumConverter(IGainParametersProvider? parameters) 
    : ISpectrumConverter
{
    private const string LOG_SOURCE = nameof(SpectrumConverter);

    private readonly IGainParametersProvider _params = parameters ?? 
        throw new ArgumentNullException(nameof(parameters));

    private readonly ParallelOptions _parallelOpts = new() { MaxDegreeOfParallelism = ProcessorCount };
    private readonly ArrayPool<float> _floatArrayPool = ArrayPool<float>.Shared;

    public float[] ConvertToSpectrum(
        Complex[] fft,
        int sampleRate,
        SpectrumScale scale,
        CancellationToken cancellationToken = default)
    {
        ValidateInputParameters(fft, sampleRate);

        int nBins = fft.Length / 2 + 1;
        float[] spectrum = _floatArrayPool.Rent(nBins);
        Array.Clear(spectrum, 0, nBins);

        SpectrumParameters spectrumParams = SpectrumParameters.FromProvider(_params);
        _parallelOpts.CancellationToken = cancellationToken;

        try
        {
            return ProcessSpectrum(fft, spectrum, nBins, sampleRate, scale, spectrumParams);
        }
        catch
        {
            _floatArrayPool.Return(spectrum);
            throw;
        }
    }

    private static void ValidateInputParameters(Complex[] fft, int sampleRate)
    {
        ArgumentNullException.ThrowIfNull(fft);

        if (sampleRate <= 0)
            throw new ArgumentException("Invalid sample rate", nameof(sampleRate));
    }

    private float[] ProcessSpectrum(
        Complex[] fft,
        float[] spectrum,
        int nBins,
        int sampleRate,
        SpectrumScale scale,
        SpectrumParameters spectrumParams)
    {
        return scale switch
        {
            SpectrumScale.Linear =>
                ProcessLinear(fft, spectrum, nBins, spectrumParams),

            SpectrumScale.Logarithmic =>
                ProcessLogarithmic(fft, spectrum, nBins, sampleRate, spectrumParams),

            SpectrumScale.Mel =>
                ProcessMel(fft, spectrum, nBins, sampleRate, spectrumParams),

            SpectrumScale.Bark =>
                ProcessBark(fft, spectrum, nBins, sampleRate, spectrumParams),

            SpectrumScale.ERB =>
                ProcessERB(fft, spectrum, nBins, sampleRate, spectrumParams),

            _ => ProcessLinear(fft, spectrum, nBins, spectrumParams)
        };
    }

    private float[] ProcessLinear(
        Complex[] fft,
        float[] spectrum,
        int nBins,
        SpectrumParameters spectrumParams) =>
        SafeResult(() =>
        {
            ComputeLinearSpectrum(fft, spectrum, nBins, spectrumParams);
            return FinalizeSpectrumResult(spectrum, nBins);
        },
        defaultValue: [],
        options: new ErrorHandlingOptions
        {
            Source = LOG_SOURCE,
            ErrorMessage = "Error processing linear spectrum",
            IgnoreExceptions = [typeof(OperationCanceledException)]
        });

    private void ComputeLinearSpectrum(
        Complex[] fft,
        float[] spectrum,
        int nBins,
        SpectrumParameters spectrumParams)
    {
        if (nBins < MIN_PARALLEL_SIZE)
        {
            ComputeSerially(fft, spectrum, nBins, spectrumParams);
        }
        else
        {
            ComputeInParallel(fft, spectrum, nBins, spectrumParams);
        }
    }

    private void ComputeSerially(
        Complex[] fft,
        float[] spectrum,
        int nBins,
        SpectrumParameters spectrumParams)
    {
        for (int i = 0; i < nBins; i++)
        {
            if (_parallelOpts.CancellationToken.IsCancellationRequested)
                break;

            spectrum[i] = InterpolateSpectrumValue(fft, i, nBins, spectrumParams);
        }
    }

    private void ComputeInParallel(
        Complex[] fft,
        float[] spectrum,
        int nBins,
        SpectrumParameters spectrumParams)
    {
        Parallel.For(0, nBins, _parallelOpts, i =>
            spectrum[i] = InterpolateSpectrumValue(fft, i, nBins, spectrumParams));
    }

    private float[] ProcessLogarithmic(
        Complex[] fft,
        float[] spectrum,
        int nBins,
        int sampleRate,
        SpectrumParameters spectrumParams) =>
        ProcessScale(
            fft, spectrum, nBins, sampleRate,
            MathF.Log10(1f), MathF.Log10(sampleRate / 2f),
            x => MathF.Pow(10, x), spectrumParams);

    private float[] ProcessMel(
        Complex[] fft,
        float[] spectrum,
        int nBins,
        int sampleRate,
        SpectrumParameters spectrumParams) =>
        ProcessScale(
            fft, spectrum, nBins, sampleRate,
            FreqToMel(1f), FreqToMel(sampleRate / 2f),
            MelToFreq, spectrumParams);

    private float[] ProcessBark(
        Complex[] fft,
        float[] spectrum,
        int nBins,
        int sampleRate,
        SpectrumParameters spectrumParams) =>
        ProcessScale(
            fft, spectrum, nBins, sampleRate,
            FreqToBark(1f), FreqToBark(sampleRate / 2f),
            BarkToFreq, spectrumParams);

    private float[] ProcessERB(
        Complex[] fft,
        float[] spectrum,
        int nBins,
        int sampleRate,
        SpectrumParameters spectrumParams) =>
        ProcessScale(
            fft, spectrum, nBins, sampleRate,
            FreqToERB(1f), FreqToERB(sampleRate / 2f),
            ERBToFreq, spectrumParams);

    private float[] ProcessScale(
        Complex[] fft,
        float[] spectrum,
        int nBins,
        int sampleRate,
        float minDomain,
        float maxDomain,
        Func<float, float> domainToFreq,
        SpectrumParameters spectrumParams) =>
        SafeResult(() =>
        {
            ComputeScaledSpectrum(
                fft, spectrum, nBins, sampleRate,
                minDomain, maxDomain, domainToFreq, spectrumParams);

            return FinalizeSpectrumResult(spectrum, nBins);
        },
        defaultValue: [],
        options: new ErrorHandlingOptions
        {
            Source = LOG_SOURCE,
            ErrorMessage = "Error processing non-linear spectrum",
            IgnoreExceptions = [typeof(OperationCanceledException)]
        });

    private void ComputeScaledSpectrum(
        Complex[] fft,
        float[] spectrum,
        int nBins,
        int sampleRate,
        float minDomain,
        float maxDomain,
        Func<float, float> domainToFreq,
        SpectrumParameters spectrumParams)
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
    }

    private float[] FinalizeSpectrumResult(float[] spectrum, int nBins)
    {
        float[] result = new float[nBins];
        Array.Copy(spectrum, result, nBins);
        _floatArrayPool.Return(spectrum);
        return result;
    }

    private static float InterpolateSpectrumValue(
        Complex[] fft,
        int index,
        int nBins,
        SpectrumParameters spectrumParams)
    {
        float centerMag = Magnitude(fft[index]);
        float leftMag = index > 0 ? Magnitude(fft[index - 1]) : centerMag;
        float rightMag = index < nBins - 1 ? Magnitude(fft[index + 1]) : centerMag;
        float interpolatedMag = (leftMag + centerMag + rightMag) / 3f;

        return interpolatedMag <= 0
            ? 0f
            : NormalizeDb(interpolatedMag, spectrumParams);
    }

    private static float CalcValue(float mag, SpectrumParameters spectrumParams) =>
        mag <= 0f ? 0f : NormalizeDb(mag, spectrumParams);

    private static float NormalizeDb(float magnitude, SpectrumParameters spectrumParams)
    {
        float db = 10f * INV_LOG10 * MathF.Log(magnitude);
        float norm = Clamp(
            (db - spectrumParams.MinDb) / spectrumParams.DbRange, 0f, 1f);

        return norm < 1e-6f ? 0f : MathF.Pow(norm, spectrumParams.AmplificationFactor);
    }

    private static float Magnitude(Complex c) => c.X * c.X + c.Y * c.Y;

    // Frequency transformation functions
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