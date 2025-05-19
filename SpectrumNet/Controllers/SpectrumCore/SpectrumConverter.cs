#nullable enable

using static SpectrumNet.Controllers.SpectrumCore.FastFourierTransformHelper;
using static SpectrumNet.Controllers.SpectrumCore.FrequencyConverter;

namespace SpectrumNet.Controllers.SpectrumCore;

public sealed class SpectrumConverter : ISpectrumConverter
{
    private const string LogPrefix = nameof(SpectrumConverter);
    private readonly ISmartLogger _logger = Instance;

    private readonly IGainParametersProvider _params;
    private readonly ParallelOptions _parallelOpts = new() { MaxDegreeOfParallelism = ProcessorCount };

    public SpectrumConverter(IGainParametersProvider parameters)
    {
        _params = parameters ?? throw new ArgumentNullException(nameof(parameters));
    }

    public float[] ConvertToSpectrum(
        Complex[] fft,
        int sampleRate,
        SpectrumScale scale,
        CancellationToken cancellationToken = default) =>
        _logger.SafeResult(() => HandleConvertToSpectrum(fft, sampleRate, scale, cancellationToken),
            Array.Empty<float>(),
            LogPrefix,
            "Error converting to spectrum");

    private float[] HandleConvertToSpectrum(
        Complex[] fft,
        int sampleRate,
        SpectrumScale scale,
        CancellationToken cancellationToken)
    {
        ValidateInputParameters(fft, sampleRate);

        int nBins = fft.Length / 2 + 1;
        float[] spectrum = new float[nBins];

        SpectrumParameters spectrumParams = SpectrumParameters.FromProvider(_params);
        _parallelOpts.CancellationToken = cancellationToken;

        return ProcessSpectrum(fft, spectrum, nBins, sampleRate, scale, spectrumParams);
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
                ProcessScaleWithFrequencyConverter(
                    fft, spectrum, nBins, sampleRate,
                    FreqToMel, MelToFreq,
                    spectrumParams),

            SpectrumScale.Bark =>
                ProcessScaleWithFrequencyConverter(
                    fft, spectrum, nBins, sampleRate,
                    FreqToBark, BarkToFreq,
                    spectrumParams),

            SpectrumScale.ERB =>
                ProcessScaleWithFrequencyConverter(
                    fft, spectrum, nBins, sampleRate,
                    FreqToERB, ERBToFreq,
                    spectrumParams),

            _ => ProcessLinear(fft, spectrum, nBins, spectrumParams)
        };
    }

    private float[] ProcessLinear(
        Complex[] fft,
        float[] spectrum,
        int nBins,
        SpectrumParameters spectrumParams) =>
        _logger.SafeResult<float[]>(() =>
        {
            ComputeLinearSpectrum(fft, spectrum, nBins, spectrumParams);
            return spectrum;
        },
        [],
        LogPrefix,
        "Error processing linear spectrum");

    private void ComputeLinearSpectrum(
        Complex[] fft,
        float[] spectrum,
        int nBins,
        SpectrumParameters spectrumParams)
    {
        if (nBins < MIN_PARALLEL_SIZE)
            ComputeSerially(fft, spectrum, nBins, spectrumParams);
        else
            ComputeInParallel(fft, spectrum, nBins, spectrumParams);
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

    private float[] ProcessScaleWithFrequencyConverter(
        Complex[] fft,
        float[] spectrum,
        int nBins,
        int sampleRate,
        Func<float, float> freqToScale,
        Func<float, float> scaleToFreq,
        SpectrumParameters spectrumParams) =>
        ProcessScale(
            fft, spectrum, nBins, sampleRate,
            freqToScale(1f), freqToScale(sampleRate / 2f),
            scaleToFreq, spectrumParams);

    private float[] ProcessScale(
        Complex[] fft,
        float[] spectrum,
        int nBins,
        int sampleRate,
        float minDomain,
        float maxDomain,
        Func<float, float> domainToFreq,
        SpectrumParameters spectrumParams) =>
        _logger.SafeResult<float[]>(() =>
        {
            ComputeScaledSpectrum(
                fft, spectrum, nBins, sampleRate,
                minDomain, maxDomain, domainToFreq, spectrumParams);

            return spectrum;
        },
        [],
        LogPrefix,
        "Error processing non-linear spectrum");

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
                (int)Round(freq / halfSampleRate * (nBins - 1)),
                0, nBins - 1);

            spectrum[i] = CalcValue(Magnitude(fft[bin]), spectrumParams);
        });
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
}