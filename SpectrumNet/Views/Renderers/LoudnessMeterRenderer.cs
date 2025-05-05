#nullable enable

using static SpectrumNet.Views.Renderers.LoudnessMeterRenderer.Constants;

namespace SpectrumNet.Views.Renderers;

public sealed class LoudnessMeterRenderer : EffectSpectrumRenderer
{
    private static readonly Lazy<LoudnessMeterRenderer> _instance = new(() => new LoudnessMeterRenderer());

    private LoudnessMeterRenderer() { }

    public static LoudnessMeterRenderer GetInstance() => _instance.Value;

    public record Constants
    {
        public const string LOG_PREFIX = "LoudnessMeterRenderer";

        public const float
            MIN_LOUDNESS_THRESHOLD = 0.001f,
            SMOOTHING_FACTOR_NORMAL = 0.3f,
            SMOOTHING_FACTOR_OVERLAY = 0.5f,
            PEAK_DECAY_RATE = 0.05f;

        public const float
            GLOW_INTENSITY = 0.4f,
            HIGH_LOUDNESS_THRESHOLD = 0.7f,
            MEDIUM_LOUDNESS_THRESHOLD = 0.4f,
            BORDER_WIDTH = 1.5f,
            BLUR_SIGMA = 10f,
            PEAK_RECT_HEIGHT = 4f,
            GLOW_HEIGHT_FACTOR = 1f / 3f;

        public const int MARKER_COUNT = 10;
        public const float GRADIENT_ALPHA_FACTOR = 0.8f;

        public static class Quality
        {
            public const bool
                LOW_USE_ADVANCED_EFFECTS = false,
                MEDIUM_USE_ADVANCED_EFFECTS = true,
                HIGH_USE_ADVANCED_EFFECTS = true;

            public const bool
                LOW_USE_ANTIALIASING = false,
                MEDIUM_USE_ANTIALIASING = true,
                HIGH_USE_ANTIALIASING = true;
        }
    }

    private new bool 
        _useAdvancedEffects,
        _useAntiAlias;

    private float 
        _previousLoudness,
        _peakLoudness;

    private float? 
        _cachedLoudness;

    private int 
        _currentWidth,
        _currentHeight;

    private SKPaint? _backgroundPaint;
    private SKPaint? _markerPaint;
    private SKPaint? _fillPaint;
    private SKPaint? _glowPaint;
    private SKPaint? _peakPaint;
    private SKPicture? _staticPicture;

    private readonly SemaphoreSlim _loudnessSemaphore = new(1, 1);
    private readonly object _loudnessLock = new();

    protected override void OnInitialize()
    {
        ExecuteSafely(
            () =>
            {
                base.OnInitialize();
                InitializeStateValues();
                CreatePaints();
                ApplyQualitySettings();
                Log(LogLevel.Debug, LOG_PREFIX, "Initialized");
            },
            nameof(OnInitialize),
            "Failed to initialize renderer"
        );
    }

    private void InitializeStateValues()
    {
        _previousLoudness = 0f;
        _peakLoudness = 0f;
        _cachedLoudness = null;
        _currentWidth = 0;
        _currentHeight = 0;
    }

    private void CreatePaints()
    {
        CreateBackgroundPaint();
        CreateMarkerPaint();
        CreateFillPaint();
        CreateGlowPaint();
        CreatePeakPaint();
    }

    private void CreateBackgroundPaint()
    {
        _backgroundPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = BORDER_WIDTH,
            Color = SKColors.White.WithAlpha(100),
            IsAntialias = _useAntiAlias
        };
    }

    private void CreateMarkerPaint()
    {
        _markerPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1,
            Color = SKColors.White.WithAlpha(150),
            IsAntialias = _useAntiAlias
        };
    }

    private void CreateFillPaint()
    {
        _fillPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            IsAntialias = _useAntiAlias
        };
    }

    private void CreateGlowPaint()
    {
        _glowPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            IsAntialias = _useAntiAlias,
            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, BLUR_SIGMA)
        };
    }

    private void CreatePeakPaint()
    {
        _peakPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            IsAntialias = _useAntiAlias
        };
    }

    public override void Configure(
        bool isOverlayActive,
        RenderQuality quality = RenderQuality.Medium)
    {
        ExecuteSafely(
            () =>
            {
                bool configChanged = _isOverlayActive != isOverlayActive || Quality != quality;
                base.Configure(isOverlayActive, quality);

                if (configChanged)
                {
                    OnConfigurationChanged();
                }
            },
            nameof(Configure),
            "Failed to configure renderer"
        );
    }

    protected override void OnConfigurationChanged()
    {
        ExecuteSafely(
            () =>
            {
                base.OnConfigurationChanged();
                UpdateSmoothingFactor();
                Log(LogLevel.Debug,
                    LOG_PREFIX,
                    $"Configuration changed. New Quality: {Quality}, Overlay: {_isOverlayActive}");
            },
            nameof(OnConfigurationChanged),
            "Failed to apply configuration changes"
        );
    }

    private void UpdateSmoothingFactor()
    {
        _smoothingFactor = _isOverlayActive ?
            SMOOTHING_FACTOR_OVERLAY :
            SMOOTHING_FACTOR_NORMAL;
    }

    protected override void ApplyQualitySettings()
    {
        ExecuteSafely(
            () =>
            {
                base.ApplyQualitySettings();
                ApplyQualityBasedSettings();
                Log(LogLevel.Debug, LOG_PREFIX, $"Quality changed to {Quality}");
            },
            nameof(ApplyQualitySettings),
            "Failed to apply quality settings"
        );
    }

    private void ApplyQualityBasedSettings()
    {
        switch (Quality)
        {
            case RenderQuality.Low:
                ApplyLowQualitySettings();
                break;
            case RenderQuality.Medium:
                ApplyMediumQualitySettings();
                break;
            case RenderQuality.High:
                ApplyHighQualitySettings();
                break;
        }

        UpdatePaintProperties();
        ResetStaticCache();
    }

    private void ApplyLowQualitySettings()
    {
        _useAdvancedEffects = Constants.Quality.LOW_USE_ADVANCED_EFFECTS;
        _useAntiAlias = Constants.Quality.LOW_USE_ANTIALIASING;
    }

    private void ApplyMediumQualitySettings()
    {
        _useAdvancedEffects = Constants.Quality.MEDIUM_USE_ADVANCED_EFFECTS;
        _useAntiAlias = Constants.Quality.MEDIUM_USE_ANTIALIASING;
    }

    private void ApplyHighQualitySettings()
    {
        _useAdvancedEffects = Constants.Quality.HIGH_USE_ADVANCED_EFFECTS;
        _useAntiAlias = Constants.Quality.HIGH_USE_ANTIALIASING;
    }

    private void UpdatePaintProperties() => UpdatePaintAntialiasing();

    private void UpdatePaintAntialiasing()
    {
        if (_backgroundPaint != null) _backgroundPaint.IsAntialias = _useAntiAlias;
        if (_markerPaint != null) _markerPaint.IsAntialias = _useAntiAlias;
        if (_fillPaint != null) _fillPaint.IsAntialias = _useAntiAlias;
        if (_glowPaint != null) _glowPaint.IsAntialias = _useAntiAlias;
        if (_peakPaint != null) _peakPaint.IsAntialias = _useAntiAlias;
    }

    private void ResetStaticCache()
    {
        _staticPicture?.Dispose();
        _staticPicture = null;
    }

    protected override void RenderEffect(
        SKCanvas canvas,
        float[] spectrum,
        SKImageInfo info,
        float barWidth,
        float barSpacing,
        int barCount,
        SKPaint paint)
    {
        if (!ValidateRenderParameters(canvas, spectrum, info, paint))
            return;

        ExecuteSafely(
            () =>
            {
                UpdateState(spectrum, info);
                RenderFrame(canvas, info, paint);
            },
            nameof(RenderEffect),
            "Error during rendering"
        );
    }

    private void UpdateState(float[] spectrum, SKImageInfo info)
    {
        ExecuteSafely(
            () =>
            {
                float loudness = ProcessLoudnessData(spectrum);

                if (CheckCanvasDimensionsChanged(info))
                {
                    UpdateCanvasDimensions(info);
                    UpdateStaticElements();
                }
            },
            nameof(UpdateState),
            "Error updating state"
        );
    }

    private void RenderFrame(SKCanvas canvas, SKImageInfo info, SKPaint paint)
    {
        ExecuteSafely(
            () =>
            {
                float loudness = GetCurrentLoudness();
                RenderMeter(canvas, info, loudness, _peakLoudness);
            },
            nameof(RenderFrame),
            "Error rendering frame"
        );
    }

    private float GetCurrentLoudness()
    {
        lock (_loudnessLock)
        {
            return _cachedLoudness ?? 0f;
        }
    }

    private bool ValidateRenderParameters(
        SKCanvas? canvas,
        float[]? spectrum,
        SKImageInfo info,
        SKPaint? paint)
    {
        if (!IsCanvasValid(canvas)) return false;
        if (!IsSpectrumValid(spectrum)) return false;
        if (!IsPaintValid(paint)) return false;
        if (!AreDimensionsValid(info)) return false;
        if (IsDisposed()) return false;

        return true;
    }

    private static bool IsCanvasValid(SKCanvas? canvas)
    {
        if (canvas != null) return true;
        Log(LogLevel.Error, LOG_PREFIX, "Canvas is null");
        return false;
    }

    private static bool IsSpectrumValid(float[]? spectrum)
    {
        if (spectrum != null && spectrum.Length > 0) return true;
        Log(LogLevel.Error, LOG_PREFIX, "Spectrum is null or empty");
        return false;
    }

    private static bool IsPaintValid(SKPaint? paint)
    {
        if (paint != null) return true;
        Log(LogLevel.Error, LOG_PREFIX, "Paint is null");
        return false;
    }

    private static bool AreDimensionsValid(SKImageInfo info)
    {
        if (info.Width > 0 && info.Height > 0) return true;
        Log(LogLevel.Error, LOG_PREFIX, $"Invalid canvas dimensions: {info.Width}x{info.Height}");
        return false;
    }

    private bool IsDisposed()
    {
        if (!_disposed) return false;
        Log(LogLevel.Error, LOG_PREFIX, "Renderer is disposed");
        return true;
    }

    private bool CheckCanvasDimensionsChanged(SKImageInfo info)
    {
        return info.Width != _currentWidth ||
               info.Height != _currentHeight ||
               _staticPicture == null;
    }

    private float ProcessLoudnessData(float[] spectrum)
    {
        float loudness = 0f;
        bool semaphoreAcquired = false;

        try
        {
            semaphoreAcquired = _loudnessSemaphore.Wait(0);

            if (semaphoreAcquired)
            {
                loudness = ProcessAndCacheLoudness(spectrum);
            }
            else
            {
                loudness = GetCachedLoudness(spectrum);
            }
        }
        finally
        {
            if (semaphoreAcquired)
                _loudnessSemaphore.Release();
        }

        return loudness;
    }

    private float ProcessAndCacheLoudness(float[] spectrum)
    {
        float loudness = CalculateAndSmoothLoudness(spectrum);
        _cachedLoudness = loudness;
        UpdatePeakLoudness(loudness);
        return loudness;
    }

    private float GetCachedLoudness(float[] spectrum)
    {
        lock (_loudnessLock)
        {
            return _cachedLoudness ?? CalculateAndSmoothLoudness(spectrum);
        }
    }

    private void UpdatePeakLoudness(float loudness)
    {
        if (loudness > _peakLoudness)
            _peakLoudness = loudness;
        else
            _peakLoudness = Max(0, _peakLoudness - PEAK_DECAY_RATE);
    }

    private void UpdateCanvasDimensions(SKImageInfo info)
    {
        _currentWidth = info.Width;
        _currentHeight = info.Height;
    }

    private void UpdateStaticElements()
    {
        ExecuteSafely(
            () =>
            {
                if (!ValidateCanvasDimensions())
                    return;

                CreateGradientShader();
                RecordStaticElements();
            },
            nameof(UpdateStaticElements),
            "Failed to update static elements"
        );
    }

    private bool ValidateCanvasDimensions()
    {
        if (_currentWidth <= 0 || _currentHeight <= 0)
        {
            Log(
                LogLevel.Warning,
                LOG_PREFIX,
                $"Invalid dimensions for static elements: {_currentWidth}x{_currentHeight}");
            return false;
        }

        return true;
    }

    private void CreateGradientShader()
    {
        if (_fillPaint == null)
            return;

        var gradientColors = CreateGradientColors();
        var colorPositions = new[] { 0f, 0.5f, 1.0f };

        var gradientShader = SKShader.CreateLinearGradient(
            new SKPoint(0, _currentHeight),
            new SKPoint(0, 0),
            gradientColors,
            colorPositions,
            SKShaderTileMode.Clamp);

        _fillPaint.Shader = gradientShader;
    }

    private static SKColor[] CreateGradientColors()
    {
        byte alpha = (byte)(255 * GRADIENT_ALPHA_FACTOR);

        return [
            SKColors.Green.WithAlpha(alpha),
            SKColors.Yellow.WithAlpha(alpha),
            SKColors.Red.WithAlpha(alpha)
        ];
    }

    private void RecordStaticElements()
    {
        using var recorder = new SKPictureRecorder();
        var rect = new SKRect(0, 0, _currentWidth, _currentHeight);
        using var canvas = recorder.BeginRecording(rect);

        DrawStaticBorder(canvas);
        DrawStaticMarkers(canvas);

        _staticPicture?.Dispose();
        _staticPicture = recorder.EndRecording();
    }

    private void DrawStaticBorder(SKCanvas canvas)
    {
        if (_backgroundPaint == null)
            return;

        canvas.DrawRect(
            0,
            0,
            _currentWidth,
            _currentHeight,
            _backgroundPaint);
    }

    private void DrawStaticMarkers(SKCanvas canvas)
    {
        if (_markerPaint == null)
            return;

        for (int i = 1; i < MARKER_COUNT; i++)
        {
            float y = _currentHeight - _currentHeight * i / (float)MARKER_COUNT;
            canvas.DrawLine(
                0,
                y,
                _currentWidth,
                y,
                _markerPaint);
        }
    }

    private void RenderMeter(
        SKCanvas canvas,
        SKImageInfo info,
        float loudness,
        float peakLoudness)
    {
        if (loudness < MIN_LOUDNESS_THRESHOLD)
            return;

        canvas.Save();

        try
        {
            RenderStaticElements(canvas, info);

            float meterHeight = CalculateMeterHeight(
                info,
                loudness);

            float peakHeight = CalculatePeakHeight(
                info,
                peakLoudness);

            RenderMeterFill(canvas, info, meterHeight);
            RenderGlowEffect(canvas, info, loudness, meterHeight);
            RenderPeakIndicator(canvas, info, loudness, peakHeight);
        }
        finally
        {
            canvas.Restore();
        }
    }

    private static float CalculateMeterHeight(
        SKImageInfo info,
        float loudness) =>
        info.Height * loudness;

    private static float CalculatePeakHeight(
        SKImageInfo info,
        float peakLoudness) =>
        info.Height * peakLoudness;

    private void RenderStaticElements(SKCanvas canvas, SKImageInfo info)
    {
        if (_staticPicture != null)
            DrawCachedStaticElements(canvas);
        else
        {
            RenderStaticElementsDirect(canvas, info);
            TryInitializeStaticCache(info);
        }
    }

    private void DrawCachedStaticElements(SKCanvas canvas)
    {
        canvas.DrawPicture(_staticPicture!);
    }

    private void RenderStaticElementsDirect(SKCanvas canvas, SKImageInfo info)
    {
        DrawBorderDirect(canvas, info);
        DrawMarkersDirect(canvas, info);
    }

    private void DrawBorderDirect(SKCanvas canvas, SKImageInfo info)
    {
        if (_backgroundPaint == null)
            return;

        canvas.DrawRect(
            0,
            0,
            info.Width,
            info.Height,
            _backgroundPaint);
    }

    private void DrawMarkersDirect(SKCanvas canvas, SKImageInfo info)
    {
        if (_markerPaint == null)
            return;

        for (int i = 1; i < MARKER_COUNT; i++)
        {
            float y = info.Height - info.Height * i / (float)MARKER_COUNT;
            canvas.DrawLine(
                0,
                y,
                info.Width,
                y,
                _markerPaint);
        }
    }

    private void TryInitializeStaticCache(SKImageInfo info)
    {
        if (_currentWidth <= 0 || _currentHeight <= 0)
        {
            _currentWidth = info.Width;
            _currentHeight = info.Height;
            UpdateStaticElements();
        }
    }

    private void RenderMeterFill(
        SKCanvas canvas,
        SKImageInfo info,
        float meterHeight)
    {
        if (_fillPaint == null)
            return;

        canvas.DrawRect(
            0,
            info.Height - meterHeight,
            info.Width,
            meterHeight,
            _fillPaint);
    }

    private void RenderGlowEffect(
        SKCanvas canvas,
        SKImageInfo info,
        float loudness,
        float meterHeight)
    {
        if (!_useAdvancedEffects ||
            loudness <= HIGH_LOUDNESS_THRESHOLD ||
            _glowPaint == null)
            return;

        byte alpha = CalculateGlowAlpha(loudness);
        _glowPaint.Color = SKColors.Red.WithAlpha(alpha);

        float glowHeight = meterHeight * GLOW_HEIGHT_FACTOR;

        canvas.DrawRect(
            0,
            info.Height - meterHeight,
            info.Width,
            glowHeight,
            _glowPaint);
    }

    private static byte CalculateGlowAlpha(float loudness)
    {
        float normalizedLoudness = (loudness - HIGH_LOUDNESS_THRESHOLD) /
                                 (1 - HIGH_LOUDNESS_THRESHOLD);

        return (byte)(255 * GLOW_INTENSITY * normalizedLoudness);
    }

    private void RenderPeakIndicator(
        SKCanvas canvas,
        SKImageInfo info,
        float loudness,
        float peakHeight)
    {
        if (_peakPaint == null)
            return;

        float peakLineY = info.Height - peakHeight;
        SetPeakColorByLoudness(loudness);

        canvas.DrawRect(
            0,
            peakLineY - PEAK_RECT_HEIGHT / 2,
            info.Width,
            PEAK_RECT_HEIGHT,
            _peakPaint);
    }

    private void SetPeakColorByLoudness(float loudness)
    {
        if (_peakPaint == null)
            return;

        if (loudness > HIGH_LOUDNESS_THRESHOLD)
            _peakPaint.Color = SKColors.Red;
        else if (loudness > MEDIUM_LOUDNESS_THRESHOLD)
            _peakPaint.Color = SKColors.Yellow;
        else
            _peakPaint.Color = SKColors.Green;
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private float CalculateAndSmoothLoudness(float[] spectrum)
    {
        float rawLoudness = CalculateLoudness(spectrum.AsSpan());
        float smoothedLoudness = ApplySmoothingToLoudness(rawLoudness);
        return smoothedLoudness;
    }

    private float ApplySmoothingToLoudness(float rawLoudness)
    {
        float smoothedLoudness = _previousLoudness +
                              (rawLoudness - _previousLoudness) * _smoothingFactor;

        smoothedLoudness = Clamp(smoothedLoudness, MIN_LOUDNESS_THRESHOLD, 1f);
        _previousLoudness = smoothedLoudness;

        return smoothedLoudness;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private float CalculateLoudness(ReadOnlySpan<float> spectrum)
    {
        if (spectrum.IsEmpty)
            return 0f;

        float sum = _useAdvancedEffects && spectrum.Length >= Vector<float>.Count
            ? CalculateLoudnessVectorized(spectrum)
            : CalculateLoudnessSequential(spectrum);

        return Clamp(sum / spectrum.Length, 0f, 1f);
    }

    private static float CalculateLoudnessVectorized(ReadOnlySpan<float> spectrum)
    {
        float sum = 0f;
        int vectorSize = Vector<float>.Count;
        int vectorizedLength = spectrum.Length - spectrum.Length % vectorSize;

        Vector<float> sumVector = Vector<float>.Zero;
        int i = 0;

        for (; i < vectorizedLength; i += vectorSize)
        {
            Vector<float> values = new(spectrum.Slice(i, vectorSize));
            sumVector += System.Numerics.Vector.Abs(values);
        }

        for (int j = 0; j < vectorSize; j++)
            sum += sumVector[j];

        for (; i < spectrum.Length; i++)
            sum += Abs(spectrum[i]);

        return sum;
    }

    private static float CalculateLoudnessSequential(ReadOnlySpan<float> spectrum)
    {
        float sum = 0f;

        for (int i = 0; i < spectrum.Length; i++)
            sum += Abs(spectrum[i]);

        return sum;
    }

    public override void Dispose()
    {
        if (_disposed) return;

        ExecuteSafely(
            () =>
            {
                OnDispose();
            },
            nameof(Dispose),
            "Error during renderer disposal"
        );

        _disposed = true;
        GC.SuppressFinalize(this);
        Log(LogLevel.Debug, LOG_PREFIX, "Disposed");
    }

    protected override void OnDispose()
    {
        ExecuteSafely(
            () =>
            {
                DisposeSynchronizationObjects();
                DisposeRenderingResources();
                ClearCachedData();

                base.OnDispose();
            },
            nameof(OnDispose),
            "Error during disposal"
        );
    }

    private void DisposeSynchronizationObjects() => _loudnessSemaphore?.Dispose();

    private void DisposeRenderingResources()
    {
        DisposeStaticPicture();
        DisposePaints();
    }

    private void DisposeStaticPicture()
    {
        _staticPicture?.Dispose();
        _staticPicture = null;
    }

    private void DisposePaints()
    {
        _backgroundPaint?.Dispose();
        _backgroundPaint = null;

        _markerPaint?.Dispose();
        _markerPaint = null;

        _fillPaint?.Dispose();
        _fillPaint = null;

        _glowPaint?.Dispose();
        _glowPaint = null;

        _peakPaint?.Dispose();
        _peakPaint = null;
    }

    private void ClearCachedData()
    {
        _cachedLoudness = null;
        _previousLoudness = 0;
        _peakLoudness = 0;
    }
}