#nullable enable

namespace SpectrumNet.Views.Abstract;

public abstract class BaseSpectrumRenderer : ISpectrumRenderer, IDisposable
{
    private const string LOG_PREFIX = nameof(BaseSpectrumRenderer);

    private const float 
        DEFAULT_SMOOTHING_FACTOR = 0.3f,
        OVERLAY_SMOOTHING_FACTOR = 0.5f;

    private const int PARALLEL_BATCH_SIZE = 32;

    protected const float MIN_MAGNITUDE_THRESHOLD = 0.01f;

    protected bool
        _isInitialized,
        _disposed,
        _isOverlayActive,
        _updatingQuality;

    protected float[]? _previousSpectrum;
    protected float[]? _processedSpectrum;
    protected float _smoothingFactor = DEFAULT_SMOOTHING_FACTOR;

    protected bool _useAntiAlias = true;
    protected SKSamplingOptions _samplingOptions = new(
        SKFilterMode.Linear,
        SKMipmapMode.Linear);
    protected bool _useAdvancedEffects = true;
    protected RenderQuality _quality = RenderQuality.Medium;

    protected readonly SemaphoreSlim _spectrumSemaphore = new(1, 1);
    protected readonly object _spectrumLock = new();

    public RenderQuality Quality
    {
        get => _quality;
        set
        {
            if (_quality != value && !_updatingQuality)
            {
                try
                {
                    _updatingQuality = true;
                    _quality = value;
                    ApplyQualitySettings();
                }
                finally
                {
                    _updatingQuality = false;
                }
            }
        }
    }

    public bool IsOverlayActive => _isOverlayActive;
    protected bool UseAntiAlias => _useAntiAlias;
    protected bool UseAdvancedEffects => _useAdvancedEffects;
    protected SKSamplingOptions SamplingOptions => _samplingOptions;

    public virtual void Initialize() => ExecuteSafely(
        () =>
        {
            if (!_isInitialized)
            {
                _isInitialized = true;
                Log(LogLevel.Debug,
                    GetType().Name,
                    "Initialized");
            }
        },
        nameof(Initialize),
        "Failed to initialize renderer");

    public virtual void Configure(
        bool isOverlayActive,
        RenderQuality quality = RenderQuality.Medium) => ExecuteSafely(
        () =>
        {
            _isOverlayActive = isOverlayActive;
            _smoothingFactor = isOverlayActive
                ? OVERLAY_SMOOTHING_FACTOR
                : DEFAULT_SMOOTHING_FACTOR;

            if (_quality != quality && !_updatingQuality)
            {
                try
                {
                    _updatingQuality = true;
                    _quality = quality;
                    ApplyQualitySettings();
                }
                finally
                {
                    _updatingQuality = false;
                }
            }
        },
        nameof(Configure),
        "Failed to configure renderer");

    public abstract void Render(
        SKCanvas? canvas,
        float[]? spectrum,
        SKImageInfo info,
        float barWidth,
        float barSpacing,
        int barCount,
        SKPaint? paint,
        Action<SKCanvas, SKImageInfo>? drawPerformanceInfo);

    public virtual void Dispose() => ExecuteSafely(
        () =>
        {
            if (!_disposed)
            {
                _spectrumSemaphore.Dispose();
                _previousSpectrum = null;
                _processedSpectrum = null;
                Log(LogLevel.Debug,
                    GetType().Name,
                    "Disposed");
                _disposed = true;
                GC.SuppressFinalize(this);
            }
        },
        nameof(Dispose),
        "Error during base disposal");

    protected static void ExecuteSafely(
        Action action,
        string source,
        string errorMessage) =>
        Safe(
            action,
            new ErrorHandlingOptions
            {
                Source = $"{LOG_PREFIX}.{source}",
                ErrorMessage = errorMessage
            });

    protected virtual void ApplyQualitySettings() => ExecuteSafely(
        () =>
        {
            if (_updatingQuality)
                return;

            try
            {
                _updatingQuality = true;
                (_useAntiAlias, _useAdvancedEffects) = QualityBasedSettings();
                _samplingOptions = QualityBasedSamplingOptions();
            }
            finally
            {
                _updatingQuality = false;
            }
        },
        nameof(ApplyQualitySettings),
        "Failed to apply quality settings");

    protected virtual (bool useAntiAlias, bool useAdvancedEffects) QualityBasedSettings() =>
        Quality switch
        {
            RenderQuality.Low => (false, false),
            RenderQuality.Medium => (true, true),
            RenderQuality.High => (true, true),
            _ => (true, true)
        };

    protected virtual SKSamplingOptions QualityBasedSamplingOptions() =>
        Quality switch
        {
            RenderQuality.Low => new SKSamplingOptions(
                SKFilterMode.Nearest,
                SKMipmapMode.None),
            RenderQuality.Medium => new SKSamplingOptions(
                SKFilterMode.Linear,
                SKMipmapMode.Linear),
            RenderQuality.High => new SKSamplingOptions(
                SKFilterMode.Linear,
                SKMipmapMode.Linear),
            _ => new SKSamplingOptions(
                SKFilterMode.Linear,
                SKMipmapMode.Linear)
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

    protected static bool IsRenderAreaVisible(
        SKCanvas? canvas,
        float x,
        float y,
        float width,
        float height) =>
        canvas == null ||
        !canvas.QuickReject(
            new SKRect(x, y, x + width, y + height));

    protected (bool isValid, float[]? processedSpectrum) PrepareRender(
        SKCanvas? canvas,
        float[]? spectrum,
        SKImageInfo info,
        int barCount,
        SKPaint? paint)
    {
        if (!QuickValidate(canvas,
                spectrum,
                info,
                paint))
        {
            return (false, null);
        }

        var cv = canvas!;
        var spec = spectrum!;
        var rect = new SKRect(
            0,
            0,
            info.Width,
            info.Height);
        if (cv.QuickReject(rect))
        {
            return (false, null);
        }

        int length = spec.Length;
        int count = Min(length, barCount);
        float[] processed = PrepareSpectrum(
            spec,
            count,
            length);
        return (true, processed);
    }

    protected static float[] ScaleSpectrum(
        float[] spectrum,
        int targetCount,
        int spectrumLength)
    {
        var result = new float[targetCount];
        float blockSize = spectrumLength / (float)targetCount;

        if (targetCount >= PARALLEL_BATCH_SIZE && IsHardwareAccelerated)
        {
            ScaleSpectrumParallel(
                spectrum,
                result,
                targetCount,
                spectrumLength,
                blockSize);
        }
        else
        {
            ScaleSpectrumSequential(
                spectrum,
                result,
                targetCount,
                spectrumLength,
                blockSize);
        }

        return result;
    }

    protected float[] SmoothSpectrum(
        float[] spectrum,
        int targetCount,
        float? customFactor = null)
    {
        float factor = customFactor ?? _smoothingFactor;
        if (_previousSpectrum == null ||
            _previousSpectrum.Length != targetCount)
        {
            _previousSpectrum = new float[targetCount];
            if (spectrum.Length >= targetCount)
            {
                Array.Copy(
                    spectrum,
                    _previousSpectrum,
                    targetCount);
            }
        }

        var smoothed = new float[targetCount];
        if (IsHardwareAccelerated &&
            targetCount >= Vector<float>.Count)
        {
            SmoothSpectrumVectorized(
                spectrum,
                smoothed,
                targetCount,
                factor);
        }
        else
        {
            SmoothSpectrumSequential(
                spectrum,
                smoothed,
                targetCount,
                factor);
        }

        return smoothed;
    }

    protected float[] PrepareSpectrum(
        float[] spectrum,
        int targetCount,
        int spectrumLength)
    {
        bool locked = false;
        try
        {
            locked = _spectrumSemaphore.Wait(0);
            if (locked)
            {
                PerformSpectrumProcessing(
                    spectrum,
                    targetCount,
                    spectrumLength);
            }

            lock (_spectrumLock)
            {
                return GetProcessedSpectrum(
                    spectrum,
                    targetCount,
                    spectrumLength);
            }
        }
        finally
        {
            if (locked)
            {
                _spectrumSemaphore.Release();
            }
        }
    }

    private static void ScaleSpectrumParallel(
        float[] spectrum,
        float[] target,
        int count,
        int length,
        float blockSize)
    {
        Parallel.For(
            0,
            count,
            i =>
            {
                int start = (int)(i * blockSize);
                int end = Min(
                    (int)((i + 1) * blockSize),
                    length);
                target[i] = end > start
                    ? CalculateBlockAverage(
                        spectrum,
                        start,
                        end)
                    : 0;
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
            int start = (int)(i * blockSize);
            int end = Min(
                (int)((i + 1) * blockSize),
                length);
            target[i] = end > start
                ? CalculateBlockAverage(
                    spectrum,
                    start,
                    end)
                : 0;
        }
    }

    private void SmoothSpectrumVectorized(
        float[] spectrum,
        float[] smoothed,
        int count,
        float smoothing)
    {
        var previous = _previousSpectrum ?? throw new InvalidOperationException(
            "Previous spectrum not initialized");
        int vecSize = Vector<float>.Count;
        int limit = count - count % vecSize;

        for (int i = 0; i < limit; i += vecSize)
        {
            var curr = new Vector<float>(spectrum, i);
            var prev = new Vector<float>(previous, i);
            var blend = prev * (1 - smoothing) + curr * smoothing;
            blend.CopyTo(smoothed, i);
            blend.CopyTo(previous, i);
        }

        SmoothSpectrumSequential(
            spectrum,
            smoothed,
            count,
            smoothing,
            limit);
    }

    private void SmoothSpectrumSequential(
        float[] spectrum,
        float[] smoothed,
        int count,
        float smoothing,
        int startIndex = 0)
    {
        var previous = _previousSpectrum ?? throw new InvalidOperationException(
            "Previous spectrum not initialized");
        for (int i = startIndex; i < count; i++)
        {
            float current = spectrum[i];
            float prevValue = previous[i];
            float result = prevValue * (1 - smoothing) + current * smoothing;
            smoothed[i] = result;
            previous[i] = result;
        }
    }

    private void PerformSpectrumProcessing(
        float[] spectrum,
        int count,
        int length)
    {
        var scaled = ScaleSpectrum(
            spectrum,
            count,
            length);
        _processedSpectrum = SmoothSpectrum(
            scaled,
            count);
    }

    private float[] GetProcessedSpectrum(
        float[] spectrum,
        int count,
        int length)
    {
        if (_processedSpectrum != null && _processedSpectrum.Length == count)
        {
            return _processedSpectrum;
        }

        var scaled = ScaleSpectrum(
            spectrum,
            count,
            length);
        _processedSpectrum = SmoothSpectrum(
            scaled,
            count);
        return _processedSpectrum;
    }

    [MethodImpl(AggressiveInlining)]
    private static float CalculateBlockAverage(
        float[] spectrum,
        int start,
        int end)
    {
        float sum = 0;
        for (int i = start; i < end; i++)
            sum += spectrum[i];
        return sum / (end - start);
    }
}