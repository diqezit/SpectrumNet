using static System.MathF;

namespace SpectrumNet.Views.Abstract;

public abstract class BaseSpectrumRenderer 
    : ISpectrumRenderer, IDisposable
{
    protected const float
        DEFAULT_SMOOTHING_FACTOR = 0.3f,
        OVERLAY_SMOOTHING_FACTOR = 0.5f;

    protected const int PARALLEL_BATCH_SIZE = 32;
    protected const float MIN_MAGNITUDE_THRESHOLD = 0.01f;

    protected bool _isInitialized;
    protected bool _disposed;

    protected float[]? _previousSpectrum;
    protected float[]? _processedSpectrum;
    protected float _smoothingFactor = DEFAULT_SMOOTHING_FACTOR;

    protected bool _useAntiAlias = true;
    protected SKSamplingOptions _samplingOptions = new(SKFilterMode.Linear, SKMipmapMode.Linear);
    protected bool _useAdvancedEffects = true;
    protected RenderQuality _quality = RenderQuality.Medium;

    protected readonly SemaphoreSlim _spectrumSemaphore = new(1, 1);
    protected readonly object _spectrumLock = new();

    protected virtual bool IsHardwareAccelerated { get; } = true;

    public RenderQuality Quality
    {
        get => _quality;
        set
        {
            if (_quality != value)
            {
                _quality = value;
                ApplyQualitySettings();
            }
        }
    }

    protected bool UseAntiAlias => _useAntiAlias;
    protected bool UseAdvancedEffects => _useAdvancedEffects;
    protected SKSamplingOptions SamplingOptions => _samplingOptions;

    public virtual void Initialize() => Safe(
        () =>
        {
            if (!_isInitialized)
            {
                _isInitialized = true;
                Log(LogLevel.Debug, $"[{GetType().Name}]", "Initialized");
            }
        },
        new ErrorHandlingOptions
        {
            Source = $"{GetType().Name}.Initialize",
            ErrorMessage = "Failed to initialize renderer"
        });

    public virtual void Configure(
        bool isOverlayActive,
        RenderQuality quality = RenderQuality.Medium) => Safe(
        () =>
        {
            _smoothingFactor = isOverlayActive ? OVERLAY_SMOOTHING_FACTOR : DEFAULT_SMOOTHING_FACTOR;
            Quality = quality;
        },
        new ErrorHandlingOptions
        {
            Source = $"{GetType().Name}.Configure",
            ErrorMessage = "Failed to configure renderer"
        });

    public abstract void Render(
        SKCanvas? canvas,
        float[]? spectrum,
        SKImageInfo info,
        float barWidth,
        float barSpacing,
        int barCount,
        SKPaint? paint,
        Action<SKCanvas, SKImageInfo>? drawPerformanceInfo);

    public virtual void Dispose()
    {
        if (!_disposed)
        {
            Safe(
                () =>
                {
                    _spectrumSemaphore?.Dispose();
                    _previousSpectrum = null;
                    _processedSpectrum = null;
                    Log(LogLevel.Debug, $"[{GetType().Name}]", "Disposed");
                },
                new ErrorHandlingOptions
                {
                    Source = $"{GetType().Name}.Dispose",
                    ErrorMessage = "Error during base disposal"
                }
            );
            _disposed = true;
        }
    }

    protected (bool isValid, float[]? processedSpectrum) PrepareRender(
        SKCanvas? canvas,
        float[]? spectrum,
        SKImageInfo info,
        int barCount,
        SKPaint? paint)
    {
        if (!QuickValidate(canvas, spectrum, info, paint))
        {
            return (false, null);
        }

        if (canvas!.QuickReject(new SKRect(0, 0, info.Width, info.Height)))
        {
            return (false, null);
        }

        int spectrumLength = spectrum!.Length;
        int actualBarCount = Min(spectrumLength, barCount);

        float[] processedSpectrum = PrepareSpectrum(
            spectrum,
            actualBarCount,
            spectrumLength);

        return (true, processedSpectrum);
    }

    protected float[] ScaleSpectrum(
        float[] spectrum,
        int targetCount,
        int spectrumLength)
    {
        float[] scaledSpectrum = new float[targetCount];
        float blockSize = (float)spectrumLength / targetCount;

        if (targetCount >= PARALLEL_BATCH_SIZE && IsHardwareAccelerated)
        {
            ScaleSpectrumParallel(spectrum, scaledSpectrum, targetCount, spectrumLength, blockSize);
        }
        else
        {
            ScaleSpectrumSequential(spectrum, scaledSpectrum, targetCount, spectrumLength, blockSize);
        }

        return scaledSpectrum;
    }

    protected float[] SmoothSpectrum(
        float[] spectrum,
        int targetCount,
        float? customSmoothingFactor = null)
    {
        float smoothing = customSmoothingFactor ?? _smoothingFactor;
        if (_previousSpectrum == null || _previousSpectrum.Length != targetCount)
        {
            _previousSpectrum = new float[targetCount];
            if (spectrum.Length >= targetCount)
            {
                Array.Copy(spectrum, _previousSpectrum, targetCount);
            }
        }

        float[] smoothedSpectrum = new float[targetCount];

        if (IsHardwareAccelerated && targetCount >= Vector<float>.Count)
        {
            SmoothSpectrumVectorized(spectrum, smoothedSpectrum, targetCount, smoothing);
        }
        else
        {
            SmoothSpectrumSequential(spectrum, smoothedSpectrum, targetCount, smoothing);
        }

        return smoothedSpectrum;
    }

    protected float[] PrepareSpectrum(
        float[] spectrum,
        int targetCount,
        int spectrumLength)
    {
        bool semaphoreAcquired = false;
        try
        {
            semaphoreAcquired = _spectrumSemaphore.Wait(0);
            if (semaphoreAcquired)
            {
                PerformSpectrumProcessing(spectrum, targetCount, spectrumLength);
            }

            lock (_spectrumLock)
            {
                return GetProcessedSpectrum(spectrum, targetCount, spectrumLength);
            }
        }
        finally
        {
            if (semaphoreAcquired)
            {
                _spectrumSemaphore.Release();
            }
        }
    }

    protected virtual void ApplyQualitySettings()
    {
        Safe(
            () =>
            {
                (_useAntiAlias, _useAdvancedEffects) = QualityBasedSettings();
                _samplingOptions = QualityBasedSamplingOptions();
            },
            new ErrorHandlingOptions
            {
                Source = $"{GetType().Name}.ApplyQualitySettings",
                ErrorMessage = "Failed to apply quality settings"
            });
    }

    protected virtual (bool useAntiAlias, bool useAdvancedEffects) QualityBasedSettings() => _quality switch
    {
        RenderQuality.Low => (false, false),
        RenderQuality.Medium => (true, true),
        RenderQuality.High => (true, true),
        _ => (true, true)
    };

    protected virtual SKSamplingOptions QualityBasedSamplingOptions() => _quality switch
    {
        RenderQuality.Low => new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None),
        RenderQuality.Medium => new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear),
        RenderQuality.High => new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear),
        _ => new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear)
    };

    protected bool QuickValidate(
        SKCanvas? canvas,
        float[]? spectrum,
        SKImageInfo info,
        SKPaint? paint) =>
        _isInitialized
        && canvas != null
        && spectrum != null
        && spectrum.Length > 0
        && paint != null
        && info.Width > 0
        && info.Height > 0;

    protected bool IsRenderAreaVisible(
        SKCanvas canvas,
        float x,
        float y,
        float width,
        float height) =>
        !canvas.QuickReject(new SKRect(x, y, x + width, y + height));

    private static void ScaleSpectrumParallel(
        float[] spectrum,
        float[] scaledSpectrum,
        int targetCount,
        int spectrumLength,
        float blockSize)
    {
        Parallel.For(0, targetCount, i =>
        {
            int start = (int)(i * blockSize);
            int end = Min((int)((i + 1) * blockSize), spectrumLength);
            if (end > start)
            {
                scaledSpectrum[i] = CalculateBlockAverage(spectrum, start, end);
            }
            else
            {
                scaledSpectrum[i] = 0;
            }
        });
    }

    private static void ScaleSpectrumSequential(
        float[] spectrum,
        float[] scaledSpectrum,
        int targetCount,
        int spectrumLength,
        float blockSize)
    {
        for (int i = 0; i < targetCount; i++)
        {
            int start = (int)(i * blockSize);
            int end = Min((int)((i + 1) * blockSize), spectrumLength);
            if (end > start)
            {
                scaledSpectrum[i] = CalculateBlockAverage(spectrum, start, end);
            }
            else
            {
                scaledSpectrum[i] = 0;
            }
        }
    }

    private void SmoothSpectrumVectorized(
        float[] spectrum,
        float[] smoothedSpectrum,
        int targetCount,
        float smoothing)
    {
        int vectorSize = Vector<float>.Count;
        int vectorizedLength = targetCount - targetCount % vectorSize;

        for (int i = 0; i < vectorizedLength; i += vectorSize)
        {
            Vector<float> currentValues = new(spectrum, i);
            Vector<float> previousValues = new(_previousSpectrum!, i);
            Vector<float> smoothedValues = previousValues * (1 - smoothing) + currentValues * smoothing;

            smoothedValues.CopyTo(smoothedSpectrum, i);
            smoothedValues.CopyTo(_previousSpectrum!, i);
        }

        SmoothSpectrumSequential(spectrum, smoothedSpectrum, targetCount, smoothing, vectorizedLength);
    }

    private void SmoothSpectrumSequential(
        float[] spectrum,
        float[] smoothedSpectrum,
        int targetCount,
        float smoothing,
        int startIndex = 0)
    {
        for (int i = startIndex; i < targetCount; i++)
        {
            ProcessSingleSpectrumValue(spectrum, smoothedSpectrum, i, smoothing);
        }
    }

    private void PerformSpectrumProcessing(
        float[] spectrum,
        int targetCount,
        int spectrumLength)
    {
        float[] scaledSpectrum = ScaleSpectrum(spectrum, targetCount, spectrumLength);
        _processedSpectrum = SmoothSpectrum(scaledSpectrum, targetCount);
    }

    private float[] GetProcessedSpectrum(
        float[] spectrum,
        int targetCount,
        int spectrumLength)
    {
        if (_processedSpectrum != null && _processedSpectrum.Length == targetCount)
        {
            return _processedSpectrum;
        }

        float[] scaledSpectrum = ScaleSpectrum(spectrum, targetCount, spectrumLength);
        _processedSpectrum = SmoothSpectrum(scaledSpectrum, targetCount);
        return _processedSpectrum;
    }

    [MethodImpl(AggressiveInlining)]
    private static float CalculateBlockAverage(
        float[] spectrum,
        int start,
        int end)
    {
        float sum = 0;
        for (int j = start; j < end; j++)
        {
            sum += spectrum[j];
        }
        return sum / (end - start);
    }

    [MethodImpl(AggressiveInlining)]
    protected void ProcessSingleSpectrumValue(
        float[] spectrum,
        float[] smoothedSpectrum,
        int i,
        float smoothing)
    {
        float currentValue = spectrum[i];
        float smoothedValue = _previousSpectrum![i] * (1 - smoothing) + currentValue * smoothing;

        smoothedSpectrum[i] = smoothedValue;
        _previousSpectrum[i] = smoothedValue;
    }
}