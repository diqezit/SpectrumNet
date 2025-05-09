#nullable enable

using static SpectrumNet.Views.Renderers.FireRenderer.Constants;
using static SpectrumNet.Views.Renderers.FireRenderer.Constants.Quality;

namespace SpectrumNet.Views.Renderers;

public sealed class FireRenderer : EffectSpectrumRenderer
{
    private static readonly Lazy<FireRenderer> _instance = new(() => new FireRenderer());

    private FireRenderer() { }

    public static FireRenderer GetInstance() => _instance.Value;

    public record Constants
    {
        public const string LOG_PREFIX = "FireRenderer";

        public const float
            TIME_STEP = 0.016f,
            DECAY_RATE = 0.08f,
            CONTROL_POINT_PROPORTION = 0.4f,
            RANDOM_OFFSET_PROPORTION = 0.5f,
            RANDOM_OFFSET_CENTER = 0.25f,
            FLAME_BOTTOM_PROPORTION = 0.25f,
            FLAME_BOTTOM_MAX = 6.0f,
            MIN_BOTTOM_ALPHA = 0.3f,
            WAVE_SPEED = 2.0f,
            WAVE_AMPLITUDE = 0.2f,
            HORIZONTAL_WAVE_FACTOR = 0.15f,
            CUBIC_CONTROL_POINT1 = 0.33f,
            CUBIC_CONTROL_POINT2 = 0.66f,
            OPACITY_WAVE_SPEED = 3.0f,
            OPACITY_PHASE_SHIFT = 0.2f,
            OPACITY_WAVE_AMPLITUDE = 0.1f,
            OPACITY_BASE = 0.9f,
            POSITION_PHASE_SHIFT = 0.5f,
            GLOW_INTENSITY = 0.3f,
            HIGH_INTENSITY_THRESHOLD = 0.7f;

        public const int
            MIN_BAR_COUNT = 10,
            SPECTRUM_PROCESSING_CHUNK_SIZE = 128;

        public const byte MAX_ALPHA_BYTE = 255;

        public static class Quality
        {
            public const bool
                LOW_USE_ADVANCED_EFFECTS = false,
                MEDIUM_USE_ADVANCED_EFFECTS = true,
                HIGH_USE_ADVANCED_EFFECTS = true;

            public const bool
                LOW_USE_ANTI_ALIAS = false,
                MEDIUM_USE_ANTI_ALIAS = true,
                HIGH_USE_ANTI_ALIAS = true;

            public const int
                LOW_MAX_DETAIL_LEVEL = 2,
                MEDIUM_MAX_DETAIL_LEVEL = 4,
                HIGH_MAX_DETAIL_LEVEL = 8;

            public const float
                LOW_GLOW_RADIUS = 1.5f,
                MEDIUM_GLOW_RADIUS = 3.0f,
                HIGH_GLOW_RADIUS = 5.0f;
        }
    }

    private readonly record struct FlameParameters(
        float X,
        float CurrentHeight,
        float PreviousHeight,
        float BarWidth,
        float CanvasHeight,
        int Index,
        float BaselinePosition
    );

    private readonly Random _random = new();
    private float[] _flameHeights = [];
    private SKPicture? _cachedBasePicture;
    private bool _dataReady;
    private float _glowRadius = MEDIUM_GLOW_RADIUS;
    private int _maxDetailLevel = MEDIUM_MAX_DETAIL_LEVEL;
    private volatile bool _isConfiguring;

    protected override void OnInitialize()
    {
        ExecuteSafely(
            () =>
            {
                base.OnInitialize();
                _time = 0f;
                InitializeResources();
                InitializeQualityParams();
                Log(LogLevel.Debug, LOG_PREFIX, "Initialized");
            },
            nameof(OnInitialize),
            "Failed to initialize renderer"
        );
    }

    private void InitializeQualityParams()
    {
        ExecuteSafely(
            () =>
            {
                ApplyQualitySettingsInternal();
            },
            nameof(InitializeQualityParams),
            "Failed to initialize quality parameters"
        );
    }

    private void InitializeResources()
    {
        ExecuteSafely(
            () =>
            {
                _flameHeights = [];
                InvalidateCachedResources();
            },
            nameof(InitializeResources),
            "Failed to initialize resources"
        );
    }

    public override void Configure(
        bool isOverlayActive,
        RenderQuality quality)
    {
        ExecuteSafely(
            () =>
            {
                if (_isConfiguring) return;

                try
                {
                    _isConfiguring = true;
                    bool configChanged = _isOverlayActive != isOverlayActive
                                      || Quality != quality;

                    _isOverlayActive = isOverlayActive;
                    Quality = quality;
                    _smoothingFactor = isOverlayActive ? 0.5f : 0.3f;

                    if (configChanged)
                    {
                        ApplyQualitySettingsInternal();
                        OnConfigurationChanged();
                    }
                }
                finally
                {
                    _isConfiguring = false;
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
                InvalidateCachedResources();
                Log(LogLevel.Information,
                    LOG_PREFIX,
                    $"Configuration changed. New Quality: {Quality}, " +
                    $"AntiAlias: {_useAntiAlias}, AdvancedEffects: {_useAdvancedEffects}, " +
                    $"GlowRadius: {_glowRadius}, MaxDetailLevel: {_maxDetailLevel}");
            },
            nameof(OnConfigurationChanged),
            "Failed to handle configuration change"
        );
    }

    protected override void ApplyQualitySettings()
    {
        ExecuteSafely(
            () =>
            {
                if (_isConfiguring) return;

                try
                {
                    _isConfiguring = true;
                    base.ApplyQualitySettings();
                    ApplyQualitySettingsInternal();
                }
                finally
                {
                    _isConfiguring = false;
                }
            },
            nameof(ApplyQualitySettings),
            "Failed to apply quality settings"
        );
    }

    private void ApplyQualitySettingsInternal()
    {
        switch (Quality)
        {
            case RenderQuality.Low:
                LowQualitySettings();
                break;
            case RenderQuality.Medium:
                MediumQualitySettings();
                break;
            case RenderQuality.High:
                HighQualitySettings();
                break;
        }

        _samplingOptions = QualityBasedSamplingOptions();
        InvalidateCachedResources();

        Log(LogLevel.Debug, LOG_PREFIX,
            $"Quality settings applied. Quality: {Quality}, " +
            $"AntiAlias: {_useAntiAlias}, AdvancedEffects: {_useAdvancedEffects}, " +
            $"GlowRadius: {_glowRadius}, MaxDetailLevel: {_maxDetailLevel}");
    }

    private void LowQualitySettings()
    {
        _maxDetailLevel = LOW_MAX_DETAIL_LEVEL;
        _glowRadius = LOW_GLOW_RADIUS;
        _useAdvancedEffects = LOW_USE_ADVANCED_EFFECTS;
        _useAntiAlias = LOW_USE_ANTI_ALIAS;
    }

    private void MediumQualitySettings()
    {
        _maxDetailLevel = MEDIUM_MAX_DETAIL_LEVEL;
        _glowRadius = MEDIUM_GLOW_RADIUS;
        _useAdvancedEffects = MEDIUM_USE_ADVANCED_EFFECTS;
        _useAntiAlias = MEDIUM_USE_ANTI_ALIAS;
    }

    private void HighQualitySettings()
    {
        _maxDetailLevel = HIGH_MAX_DETAIL_LEVEL;
        _glowRadius = HIGH_GLOW_RADIUS;
        _useAdvancedEffects = HIGH_USE_ADVANCED_EFFECTS;
        _useAntiAlias = HIGH_USE_ANTI_ALIAS;
    }

    private new void InvalidateCachedResources()
    {
        ExecuteSafely(
            () =>
            {
                _cachedBasePicture?.Dispose();
                _cachedBasePicture = null;
                _dataReady = false;
                base._processedSpectrum = null;
            },
            nameof(InvalidateCachedResources),
            "Failed to invalidate cached resources"
        );
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
        if (!ValidateRenderParameters(canvas, spectrum, info, paint)) return;

        ExecuteSafely(
            () =>
            {
                UpdateState(spectrum, barCount);
                RenderFrame(canvas, info, barWidth, barSpacing, barCount, paint);
            },
            nameof(RenderEffect),
            "Error during rendering"
        );
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
        Log(LogLevel.Error, LOG_PREFIX, $"Invalid image dimensions: {info.Width}x{info.Height}");
        return false;
    }

    private bool IsDisposed()
    {
        if (!_disposed) return false;
        Log(LogLevel.Error, LOG_PREFIX, "Renderer is disposed");
        return true;
    }

    private void UpdateState(float[] spectrum, int barCount)
    {
        ExecuteSafely(
            () =>
            {
                _time += TIME_STEP;
                ProcessSpectrumData(spectrum, barCount);
            },
            nameof(UpdateState),
            "Error updating renderer state"
        );
    }

    private void ProcessSpectrumData(float[] spectrum, int barCount)
    {
        ExecuteSafely(
            () =>
            {
                EnsureSpectrumBuffer(spectrum.Length);

                int spectrumLength = spectrum.Length;
                int actualBarCount = Min(spectrumLength, barCount);

                float[] scaledSpectrum = ScaleSpectrum(
                    spectrum,
                    actualBarCount,
                    spectrumLength);

                UpdatePreviousSpectrum(scaledSpectrum);

                lock (_spectrumLock)
                {
                    // Используем base._processedSpectrum
                    base._processedSpectrum = scaledSpectrum;
                    _dataReady = true;
                }
            },
            nameof(ProcessSpectrumData),
            "Error processing spectrum data"
        );
    }

    private void EnsureSpectrumBuffer(int length)
    {
        if (_flameHeights.Length != length)
        {
            _flameHeights = new float[length];
        }
    }

    [MethodImpl(AggressiveOptimization)]
    private static new float[] ScaleSpectrum(
        float[] spectrum,
        int targetCount,
        int spectrumLength)
    {
        float[] scaledSpectrum = new float[targetCount];
        float blockSize = (float)spectrumLength / targetCount;

        if (IsHardwareAccelerated && targetCount >= SPECTRUM_PROCESSING_CHUNK_SIZE)
        {
            ScaleSpectrumParallel(
                spectrum,
                scaledSpectrum,
                targetCount,
                blockSize,
                spectrumLength);
        }
        else
        {
            ScaleSpectrumSequential(
                spectrum,
                scaledSpectrum,
                targetCount,
                blockSize,
                spectrumLength);
        }

        return scaledSpectrum;
    }

    private static void ScaleSpectrumParallel(
        float[] spectrum,
        float[] scaledSpectrum,
        int targetCount,
        float blockSize,
        int spectrumLength)
    {
        int chunkSize = Min(SPECTRUM_PROCESSING_CHUNK_SIZE, targetCount);

        Parallel.For(
            0,
            (targetCount + chunkSize - 1) / chunkSize,
            chunkIndex =>
            {
                int startIdx = chunkIndex * chunkSize;
                int endIdx = Min(startIdx + chunkSize, targetCount);

                for (int i = startIdx; i < endIdx; i++)
                {
                    ProcessSpectrumBlock(
                        spectrum,
                        scaledSpectrum,
                        i,
                        blockSize,
                        spectrumLength);
                }
            });
    }

    private static void ScaleSpectrumSequential(
        float[] spectrum,
        float[] scaledSpectrum,
        int targetCount,
        float blockSize,
        int spectrumLength)
    {
        for (int i = 0; i < targetCount; i++)
        {
            ProcessSpectrumBlock(
                spectrum,
                scaledSpectrum,
                i,
                blockSize,
                spectrumLength);
        }
    }

    private static void ProcessSpectrumBlock(
        float[] spectrum,
        float[] scaledSpectrum,
        int blockIndex,
        float blockSize,
        int spectrumLength)
    {
        int start = (int)(blockIndex * blockSize);
        int end = (int)((blockIndex + 1) * blockSize);
        end = Min(end, spectrumLength);

        if (end <= start)
        {
            scaledSpectrum[blockIndex] = 0;
            return;
        }

        float sum = 0;
        for (int j = start; j < end; j++)
        {
            sum += spectrum[j];
        }

        scaledSpectrum[blockIndex] = sum / (end - start);
    }

    private void UpdatePreviousSpectrum(float[] spectrum)
    {
        for (int i = 0; i < Min(spectrum.Length, _flameHeights.Length); i++)
        {
            _flameHeights[i] = Max(
                spectrum[i],
                _flameHeights[i] - DECAY_RATE
            );
        }
    }

    private void RenderFrame(
        SKCanvas canvas,
        SKImageInfo info,
        float barWidth,
        float barSpacing,
        int barCount,
        SKPaint paint)
    {
        ExecuteSafely(
            () =>
            {
                float[]? spectrum = GetCurrentSpectrum();
                if (spectrum == null) return;

                var totalBarWidth = CalculateTotalBarWidth(
                    spectrum.Length,
                    barWidth,
                    barSpacing);

                using var renderScope = new FlameRenderScope(
                    this,
                    canvas,
                    spectrum,
                    info,
                    barWidth,
                    totalBarWidth,
                    paint);

                renderScope.RenderFlames();
            },
            nameof(RenderFrame),
            "Error rendering flame frame"
        );
    }

    private float[]? GetCurrentSpectrum()
    {
        lock (_spectrumLock)
        {
            if (!_dataReady || base._processedSpectrum == null)
            {
                return null;
            }
            return base._processedSpectrum;
        }
    }

    private static float CalculateTotalBarWidth(
        int actualBarCount,
        float barWidth,
        float barSpacing)
    {
        float totalBarWidth = barWidth + barSpacing;
        return actualBarCount < MIN_BAR_COUNT
            ? totalBarWidth * MIN_BAR_COUNT / actualBarCount
            : totalBarWidth;
    }

    private class FlameRenderScope : IDisposable
    {
        private readonly FireRenderer _renderer;
        private readonly SKCanvas _canvas;
        private readonly float[] _spectrum;
        private readonly SKImageInfo _info;
        private readonly float _barWidth;
        private readonly float _totalBarWidth;
        private readonly SKPaint _basePaint;
        private readonly SKPaint _workingPaint;
        private readonly SKPaint _glowPaint;
        private readonly bool _useAdvancedEffects;

        private readonly List<(List<FlameParameters> Flames, float Intensity)> _flameGroups = [];

        public FlameRenderScope(
            FireRenderer renderer,
            SKCanvas canvas,
            float[] spectrum,
            SKImageInfo info,
            float barWidth,
            float totalBarWidth,
            SKPaint basePaint)
        {
            _renderer = renderer;
            _canvas = canvas;
            _spectrum = spectrum;
            _info = info;
            _barWidth = barWidth;
            _totalBarWidth = totalBarWidth;
            _basePaint = basePaint;

            _workingPaint = renderer._paintPool.Get();
            ConfigureWorkingPaint();

            _glowPaint = renderer._paintPool.Get();
            ConfigureGlowPaint();

            _useAdvancedEffects = _renderer.UseAdvancedEffects;
        }

        private void ConfigureWorkingPaint()
        {
            _workingPaint.Color = _basePaint.Color;
            _workingPaint.Style = _basePaint.Style;
            _workingPaint.StrokeWidth = _basePaint.StrokeWidth;
            _workingPaint.IsStroke = _basePaint.IsStroke;
            _workingPaint.IsAntialias = _renderer.UseAntiAlias;
            _workingPaint.ImageFilter = _basePaint.ImageFilter;
            _workingPaint.Shader = _basePaint.Shader;
        }

        private void ConfigureGlowPaint()
        {
            _glowPaint.Color = _basePaint.Color;
            _glowPaint.Style = _basePaint.Style;
            _glowPaint.StrokeWidth = _basePaint.StrokeWidth;
            _glowPaint.IsStroke = _basePaint.IsStroke;
            _glowPaint.IsAntialias = _renderer.UseAntiAlias;
            _glowPaint.MaskFilter = SKMaskFilter.CreateBlur(
                SKBlurStyle.Normal,
                _renderer._glowRadius);
        }

        public void RenderFlames()
        {
            ExecuteSafely(
                () =>
                {
                    _canvas.Save();
                    _canvas.ClipRect(new SKRect(0, 0, _info.Width, _info.Height));

                    GroupFlames();
                    DrawFlameGroups();

                    _canvas.Restore();
                },
                nameof(RenderFlames),
                "Error rendering flames"
            );
        }

        private void GroupFlames()
        {
            ExecuteSafely(
                () =>
                {
                    var currentGroup = new List<FlameParameters>();
                    float currentIntensity = 0;

                    for (int i = 0; i < _spectrum.Length; i++)
                    {
                        var spectrumValue = _spectrum[i];
                        if (spectrumValue < 0.01f)
                            continue;

                        var flameParams = CalculateFlameParameters(i, spectrumValue);
                        float intensity = flameParams.CurrentHeight / flameParams.CanvasHeight;

                        if (currentGroup.Count > 0 && Abs(intensity - currentIntensity) > 0.2f)
                        {
                            _flameGroups.Add((new List<FlameParameters>(currentGroup), currentIntensity));
                            currentGroup.Clear();
                        }

                        currentGroup.Add(flameParams);
                        currentIntensity = intensity;
                    }

                    if (currentGroup.Count > 0)
                        _flameGroups.Add((new List<FlameParameters>(currentGroup), currentIntensity));
                },
                nameof(GroupFlames),
                "Error grouping flames"
            );
        }

        private FlameParameters CalculateFlameParameters(int index, float spectrumValue)
        {
            float x = index * _totalBarWidth;
            float waveOffset = CalculateWaveOffset(index);
            float currentHeight = CalculateFlameHeight(spectrumValue, waveOffset);
            float previousHeight = GetPreviousFlameHeight(index);
            float baselinePosition = _info.Height;

            return new FlameParameters(
                x,
                currentHeight,
                previousHeight,
                _barWidth,
                _info.Height,
                index,
                baselinePosition);
        }

        private float CalculateWaveOffset(int index) =>
            MathF.Sin(_renderer._time * WAVE_SPEED + index * POSITION_PHASE_SHIFT)
                * WAVE_AMPLITUDE;

        private float CalculateFlameHeight(float spectrumValue, float waveOffset) =>
            spectrumValue * _info.Height * (1 + waveOffset);

        private float GetPreviousFlameHeight(int index) =>
            _renderer._flameHeights.Length > index
            ? _renderer._flameHeights[index] * _info.Height
            : 0;

        private void DrawFlameGroups()
        {
            ExecuteSafely(
                () =>
                {
                    foreach (var (flames, _) in _flameGroups.OrderBy(g => g.Intensity))
                    {
                        foreach (var flameParams in flames)
                        {
                            RenderSingleFlame(flameParams);
                        }
                    }
                },
                nameof(DrawFlameGroups),
                "Error drawing flame groups"
            );
        }

        private void RenderSingleFlame(FlameParameters parameters)
        {
            var pathPool = _renderer._pathPool;
            if (pathPool == null) return;

            var path = pathPool.Get();
            if (path == null) return;

            try
            {
                var (flameTop, flameBottom) = CalculateFlameVerticalPositions(parameters);
                float x = CalculateHorizontalPosition(parameters);

                if (flameBottom - flameTop < 1)
                {
                    return;
                }

                if (ShouldRenderGlow(parameters))
                {
                    RenderFlameGlow(
                        path,
                        x,
                        flameTop,
                        flameBottom,
                        parameters);
                }

                RenderFlameBase(path, x, flameBottom);
                RenderFlameBody(
                    path,
                    x,
                    flameTop,
                    flameBottom,
                    parameters);
            }
            finally
            {
                pathPool.Return(path);
            }
        }

        private bool ShouldRenderGlow(FlameParameters parameters) =>
            _renderer.UseAdvancedEffects
            && parameters.CurrentHeight
            / parameters.CanvasHeight > HIGH_INTENSITY_THRESHOLD;

        private static (float flameTop, float flameBottom) CalculateFlameVerticalPositions(
            FlameParameters parameters)
        {
            float flameTop = parameters.CanvasHeight -
                Max(parameters.CurrentHeight, parameters.PreviousHeight);
            float flameBottom = parameters.CanvasHeight - FLAME_BOTTOM_MAX;

            return (flameTop, flameBottom);
        }

        private float CalculateHorizontalPosition(FlameParameters parameters)
        {
            float waveOffset = MathF.Sin(
                _renderer._time * WAVE_SPEED +
                parameters.Index * POSITION_PHASE_SHIFT) *
                (parameters.BarWidth * HORIZONTAL_WAVE_FACTOR);
            return parameters.X + waveOffset;
        }

        private void RenderFlameBase(
            SKPath path,
            float x,
            float flameBottom)
        {
            ExecuteSafely(
                () =>
                {
                    path.Reset();
                    path.MoveTo(x, flameBottom);
                    path.LineTo(x + _barWidth, flameBottom);

                    using var bottomPaint = _renderer._paintPool.Get();
                    ConfigureBottomPaint(bottomPaint);

                    _canvas.DrawPath(path, bottomPaint);
                },
                nameof(RenderFlameBase),
                "Error rendering flame base"
            );
        }

        private void ConfigureBottomPaint(SKPaint bottomPaint)
        {
            bottomPaint.Color = _workingPaint.Color.WithAlpha(
                (byte)(MAX_ALPHA_BYTE * MIN_BOTTOM_ALPHA));
            bottomPaint.Style = _workingPaint.Style;
            bottomPaint.StrokeWidth = _workingPaint.StrokeWidth;
            bottomPaint.IsStroke = _workingPaint.IsStroke;
            bottomPaint.IsAntialias = _workingPaint.IsAntialias;
            bottomPaint.ImageFilter = _workingPaint.ImageFilter;
            bottomPaint.Shader = _workingPaint.Shader;
        }

        private void RenderFlameGlow(
            SKPath path,
            float x,
            float flameTop,
            float flameBottom,
            FlameParameters parameters)
        {
            ExecuteSafely(
                () =>
                {
                    path.Reset();
                    path.MoveTo(x, flameBottom);

                    float height = flameBottom - flameTop;
                    var (cp1X, cp1Y, cp2X, cp2Y) = CalculateControlPoints(
                        x,
                        flameBottom,
                        height);

                    DrawCubicBezierPath(
                        path,
                        cp1X,
                        cp1Y,
                        cp2X,
                        cp2Y,
                        x + parameters.BarWidth,
                        flameBottom);

                    ConfigureGlowPaintForFlame(parameters);
                    _canvas.DrawPath(path, _glowPaint);
                },
                nameof(RenderFlameGlow),
                "Error rendering flame glow"
            );
        }

        private void ConfigureGlowPaintForFlame(FlameParameters parameters)
        {
            float intensity = parameters.CurrentHeight / parameters.CanvasHeight;
            byte glowAlpha = (byte)(MAX_ALPHA_BYTE * intensity * GLOW_INTENSITY);
            _glowPaint.Color = _glowPaint.Color.WithAlpha(glowAlpha);
        }

        private void RenderFlameBody(
            SKPath path,
            float x,
            float flameTop,
            float flameBottom,
            FlameParameters parameters)
        {
            ExecuteSafely(
                () =>
                {
                    path.Reset();
                    path.MoveTo(x, flameBottom);

                    float height = flameBottom - flameTop;
                    var (cp1X, cp1Y, cp2X, cp2Y) = CalculateControlPoints(
                        x,
                        flameBottom,
                        height);

                    DrawCubicBezierPath(
                        path,
                        cp1X,
                        cp1Y,
                        cp2X,
                        cp2Y,
                        x + parameters.BarWidth,
                        flameBottom);

                    UpdatePaintForFlame(parameters);
                    _canvas.DrawPath(path, _workingPaint);
                },
                nameof(RenderFlameBody),
                "Error rendering flame body"
            );
        }

        private static void DrawCubicBezierPath(
            SKPath path,
            float cp1X,
            float cp1Y,
            float cp2X,
            float cp2Y,
            float endX,
            float endY)
        {
            path.CubicTo(
                cp1X,
                cp1Y,
                cp2X,
                cp2Y,
                endX,
                endY);
        }

        private (float cp1X, float cp1Y, float cp2X, float cp2Y) CalculateControlPoints(
            float x,
            float flameBottom,
            float height)
        {
            float cp1Y = flameBottom - height * CUBIC_CONTROL_POINT1;
            float cp2Y = flameBottom - height * CUBIC_CONTROL_POINT2;

            float detailFactor = (float)_renderer._maxDetailLevel / HIGH_MAX_DETAIL_LEVEL;
            float randomnessFactor = detailFactor * RANDOM_OFFSET_PROPORTION;

            var (randomOffset1, randomOffset2) = CalculateRandomOffsets(
                randomnessFactor);

            return (
                x + _barWidth * CUBIC_CONTROL_POINT1 + randomOffset1,
                cp1Y,
                x + _barWidth * CUBIC_CONTROL_POINT2 + randomOffset2,
                cp2Y
            );
        }

        private (float offset1, float offset2) CalculateRandomOffsets(
            float randomnessFactor)
        {
            float randomOffset1 = (float)(_renderer._random.NextDouble() *
                _barWidth * randomnessFactor -
                _barWidth * RANDOM_OFFSET_CENTER);

            float randomOffset2 = (float)(_renderer._random.NextDouble() *
                _barWidth * randomnessFactor -
                _barWidth * RANDOM_OFFSET_CENTER);

            return (randomOffset1, randomOffset2);
        }

        private void UpdatePaintForFlame(FlameParameters parameters)
        {
            float opacityWave = CalculateOpacityWave(parameters.Index);

            byte alpha = (byte)(MAX_ALPHA_BYTE * Min(
                parameters.CurrentHeight / parameters.CanvasHeight * opacityWave,
                1.0f));
            _workingPaint.Color = _workingPaint.Color.WithAlpha(alpha);
        }

        private float CalculateOpacityWave(int index) =>
            MathF.Sin(_renderer._time * OPACITY_WAVE_SPEED + index * OPACITY_PHASE_SHIFT)
            * OPACITY_WAVE_AMPLITUDE
            + OPACITY_BASE;

        private static void ExecuteSafely(
            Action action,
            string methodName,
            string errorMessage)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                Log(LogLevel.Error, LOG_PREFIX, $"{errorMessage}: {ex.Message}");
                Log(LogLevel.Debug, LOG_PREFIX, $"{methodName} exception: {ex}");
            }
        }

        public void Dispose()
        {
            _renderer._paintPool?.Return(_workingPaint);
            _renderer._paintPool?.Return(_glowPaint);
        }
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
            "Error during disposal"
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
                DisposeManagedResources();
                base.OnDispose();
            },
            nameof(OnDispose),
            "Error during specific disposal"
        );
    }

    private void DisposeManagedResources()
    {
        _cachedBasePicture?.Dispose();
        _cachedBasePicture = null;

        _pathPool?.Dispose();

        _flameHeights = [];
        _processedSpectrum = null;
    }

    protected override void OnInvalidateCachedResources()
    {
        ExecuteSafely(
            () =>
            {
                base.OnInvalidateCachedResources();
                _cachedBasePicture?.Dispose();
                _cachedBasePicture = null;
                _dataReady = false;
                base._processedSpectrum = null;
                Log(LogLevel.Debug, LOG_PREFIX, "Cached resources invalidated");
            },
            nameof(OnInvalidateCachedResources),
            "Error invalidating cached resources"
        );
    }
}