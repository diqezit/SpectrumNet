#nullable enable

using static SpectrumNet.Views.Renderers.FireRenderer.Constants;

namespace SpectrumNet.Views.Renderers;

public sealed class FireRenderer : EffectSpectrumRenderer
{
    private static readonly Lazy<FireRenderer> _instance = new(() => new FireRenderer());

    // Заменяем стандартный пул ObjectPool из базового класса
    private new readonly ObjectPool<SKPath> _pathPool = new(
        () => new SKPath(),
        path => path.Reset(),
        10);

    private float[] _flameHeights = [];
    private readonly Random _random = new();

    private SKPicture? _cachedBasePicture;
    private readonly object _renderDataLock = new();
    private new readonly object _spectrumLock = new();

    private bool _dataReady;
    private new float[]? _processedSpectrum;
    private int _maxDetailLevel = 4;
    private float _glowRadius = GLOW_RADIUS_MEDIUM;

    public record Constants
    {
        public const string LOG_PREFIX = "FireRenderer";

        public const float
            TIME_STEP = 0.016f,
            DECAY_RATE = 0.08f;

        public const float
            CONTROL_POINT_PROPORTION = 0.4f,
            RANDOM_OFFSET_PROPORTION = 0.5f,
            RANDOM_OFFSET_CENTER = 0.25f,
            FLAME_BOTTOM_PROPORTION = 0.25f,
            FLAME_BOTTOM_MAX = 6f,
            MIN_BOTTOM_ALPHA = 0.3f;

        public const float
            WAVE_SPEED = 2.0f,
            WAVE_AMPLITUDE = 0.2f,
            HORIZONTAL_WAVE_FACTOR = 0.15f;

        public const float
            CUBIC_CONTROL_POINT1 = 0.33f,
            CUBIC_CONTROL_POINT2 = 0.66f;

        public const float
            OPACITY_WAVE_SPEED = 3.0f,
            OPACITY_PHASE_SHIFT = 0.2f,
            OPACITY_WAVE_AMPLITUDE = 0.1f,
            OPACITY_BASE = 0.9f;

        public const float POSITION_PHASE_SHIFT = 0.5f;
        public const int MIN_BAR_COUNT = 10;

        public const float
            GLOW_INTENSITY = 0.3f,
            HIGH_INTENSITY_THRESHOLD = 0.7f;

        public const float GLOW_RADIUS_LOW = 1.5f;
        public const int MAX_DETAIL_LEVEL_LOW = 2;

        public const float GLOW_RADIUS_MEDIUM = 3f;
        public const int MAX_DETAIL_LEVEL_MEDIUM = 4;

        public const float GLOW_RADIUS_HIGH = 5f;
        public const int MAX_DETAIL_LEVEL_HIGH = 8;

        public const int SPECTRUM_PROCESSING_CHUNK_SIZE = 128;
    }

    private FireRenderer() { }

    public static FireRenderer GetInstance() => _instance.Value;

    public override void Initialize()
    {
        Safe(
            () =>
            {
                base.Initialize();
                _time = 0f;
                InitializeResources();
                ApplyQualitySettings();
                Log(LogLevel.Debug, LOG_PREFIX, "Initialized");
            },
            new ErrorHandlingOptions
            {
                Source = $"{LOG_PREFIX}.Initialize",
                ErrorMessage = "Failed to initialize renderer"
            }
        );
    }

    private void InitializeResources()
    {
        Safe(
            () =>
            {
                _flameHeights = [];
                InvalidateCachedResources();
            },
            new ErrorHandlingOptions
            {
                Source = $"{LOG_PREFIX}.InitializeResources",
                ErrorMessage = "Failed to initialize resources"
            }
        );
    }

    public override void Configure(
        bool isOverlayActive,
        RenderQuality quality = RenderQuality.Medium)
    {
        Safe(
            () =>
            {
                bool configChanged = _isOverlayActive != isOverlayActive || Quality != quality;

                base.Configure(isOverlayActive, quality);

                if (configChanged)
                {
                    OnConfigurationChanged();
                }
            },
            new ErrorHandlingOptions
            {
                Source = $"{LOG_PREFIX}.Configure",
                ErrorMessage = "Failed to configure renderer"
            }
        );
    }

    protected override void OnConfigurationChanged()
    {
        Safe(
            () =>
            {
                InvalidateCachedResources();
            },
            new ErrorHandlingOptions
            {
                Source = $"{LOG_PREFIX}.OnConfigurationChanged",
                ErrorMessage = "Failed to handle configuration change"
            }
        );
    }

    protected override void ApplyQualitySettings()
    {
        Safe(
            () =>
            {
                base.ApplyQualitySettings();

                ConfigureQualityDependentSettings();

                InvalidateCachedResources();
            },
            new ErrorHandlingOptions
            {
                Source = $"{LOG_PREFIX}.ApplyQualitySettings",
                ErrorMessage = "Failed to apply quality settings"
            }
        );
    }

    private void ConfigureQualityDependentSettings()
    {
        switch (Quality)
        {
            case RenderQuality.Low:
                _maxDetailLevel = MAX_DETAIL_LEVEL_LOW;
                _glowRadius = GLOW_RADIUS_LOW;
                break;

            case RenderQuality.Medium:
                _maxDetailLevel = MAX_DETAIL_LEVEL_MEDIUM;
                _glowRadius = GLOW_RADIUS_MEDIUM;
                break;

            case RenderQuality.High:
                _maxDetailLevel = MAX_DETAIL_LEVEL_HIGH;
                _glowRadius = GLOW_RADIUS_HIGH;
                break;
        }
    }

    private new void InvalidateCachedResources()
    {
        Safe(
            () =>
            {
                _cachedBasePicture?.Dispose();
                _cachedBasePicture = null;
                _dataReady = false;
                _processedSpectrum = null;
            },
            new ErrorHandlingOptions
            {
                Source = $"{LOG_PREFIX}.InvalidateCachedResources",
                ErrorMessage = "Failed to invalidate cached resources"
            }
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
        if (!ValidateRenderParameters(canvas, spectrum, info, paint))
        {
            return;
        }

        Safe(
            () =>
            {
                UpdateState(spectrum, barCount);

                if (_dataReady)
                {
                    RenderFrame(
                        canvas,
                        info,
                        barWidth,
                        barSpacing,
                        barCount,
                        paint);
                }
            },
            new ErrorHandlingOptions
            {
                Source = $"{LOG_PREFIX}.RenderEffect",
                ErrorMessage = "Error in RenderEffect method"
            }
        );
    }

    private bool ValidateRenderParameters(
        SKCanvas canvas,
        float[] spectrum,
        SKImageInfo info,
        SKPaint paint)
    {
        if (!_isInitialized)
        {
            Log(LogLevel.Error, LOG_PREFIX, "Renderer is not initialized");
            return false;
        }

        if (canvas == null || spectrum == null || paint == null)
        {
            Log(
                LogLevel.Error,
                LOG_PREFIX,
                "Invalid render parameters: null values");
            return false;
        }

        if (info.Width <= 0 || info.Height <= 0)
        {
            Log(
                LogLevel.Error,
                LOG_PREFIX,
                $"Invalid image dimensions: {info.Width}x{info.Height}");
            return false;
        }

        if (spectrum.Length == 0)
        {
            Log(LogLevel.Warning, LOG_PREFIX, "Empty spectrum data");
            return false;
        }

        if (_disposed)
        {
            Log(LogLevel.Error, LOG_PREFIX, "Renderer is disposed");
            return false;
        }

        if (canvas.QuickReject(new SKRect(0, 0, info.Width, info.Height)))
        {
            return false;
        }

        return true;
    }

    private void UpdateState(float[] spectrum, int barCount)
    {
        Safe(
            () =>
            {
                _time += TIME_STEP;
                ProcessSpectrumData(spectrum, barCount);
            },
            new ErrorHandlingOptions
            {
                Source = $"{LOG_PREFIX}.UpdateState",
                ErrorMessage = "Error updating renderer state"
            }
        );
    }

    private void ProcessSpectrumData(float[] spectrum, int barCount)
    {
        Safe(
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
                    _processedSpectrum = scaledSpectrum;
                    _dataReady = true;
                }
            },
            new ErrorHandlingOptions
            {
                Source = $"{LOG_PREFIX}.ProcessSpectrumData",
                ErrorMessage = "Error processing spectrum data"
            }
        );
    }

    private void EnsureSpectrumBuffer(int length)
    {
        if (_flameHeights.Length != length)
        {
            _flameHeights = new float[length];
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private new float[] ScaleSpectrum(
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
        int _,  // Unused barCount parameter 
        SKPaint paint)
    {
        Safe(
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
            new ErrorHandlingOptions
            {
                Source = $"{LOG_PREFIX}.RenderFrame",
                ErrorMessage = "Error rendering flame frame"
            }
        );
    }

    private float[]? GetCurrentSpectrum()
    {
        lock (_spectrumLock)
        {
            if (!_dataReady || _processedSpectrum == null)
            {
                return null;
            }
            return _processedSpectrum;
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

    private readonly record struct FlameParameters(
        float X,
        float CurrentHeight,
        float PreviousHeight,
        float BarWidth,
        float CanvasHeight,
        int Index,
        float BaselinePosition
    );

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

            _useAdvancedEffects = renderer.UseAdvancedEffects;
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
            _canvas.Save();
            _canvas.ClipRect(new SKRect(0, 0, _info.Width, _info.Height));

            GroupFlames();
            DrawFlameGroups();

            _canvas.Restore();
        }

        private void GroupFlames()
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
            foreach (var (flames, _) in _flameGroups.OrderBy(g => g.Intensity))
            {
                foreach (var flameParams in flames)
                {
                    RenderSingleFlame(flameParams);
                }
            }
        }

        private void RenderSingleFlame(FlameParameters parameters)
        {
            var path = _renderer._pathPool.Get();

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
                _renderer._pathPool.Return(path);
            }
        }

        private bool ShouldRenderGlow(FlameParameters parameters) => 
            _useAdvancedEffects 
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
            path.Reset();
            path.MoveTo(x, flameBottom);
            path.LineTo(x + _barWidth, flameBottom);

            using var bottomPaint = _renderer._paintPool.Get();
            ConfigureBottomPaint(bottomPaint);

            _canvas.DrawPath(path, bottomPaint);
        }

        private void ConfigureBottomPaint(SKPaint bottomPaint)
        {
            bottomPaint.Color = _workingPaint.Color.WithAlpha(
                (byte)(255 * MIN_BOTTOM_ALPHA));
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
        }

        private void ConfigureGlowPaintForFlame(FlameParameters parameters)
        {
            float intensity = parameters.CurrentHeight / parameters.CanvasHeight;
            byte glowAlpha = (byte)(255 * intensity * GLOW_INTENSITY);
            _glowPaint.Color = _glowPaint.Color.WithAlpha(glowAlpha);
        }

        private void RenderFlameBody(
            SKPath path,
            float x,
            float flameTop,
            float flameBottom,
            FlameParameters parameters)
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

            float detailFactor = (float)_renderer._maxDetailLevel / MAX_DETAIL_LEVEL_HIGH;
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

            byte alpha = (byte)(255 * Min(
                parameters.CurrentHeight / parameters.CanvasHeight * opacityWave,
                1.0f));
            _workingPaint.Color = _workingPaint.Color.WithAlpha(alpha);
        }

        private float CalculateOpacityWave(int index) => 
            MathF.Sin(_renderer._time * OPACITY_WAVE_SPEED + index * OPACITY_PHASE_SHIFT)
            * OPACITY_WAVE_AMPLITUDE
            + OPACITY_BASE;

        public void Dispose()
        {
            _renderer._paintPool.Return(_workingPaint);
            _renderer._paintPool.Return(_glowPaint);
        }
    }

    public override void Dispose()
    {
        if (!_disposed)
        {
            Safe(
                () =>
                {
                    OnDispose();
                },
                new ErrorHandlingOptions
                {
                    Source = $"{LOG_PREFIX}.Dispose",
                    ErrorMessage = "Error during renderer disposal"
                }
            );

            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }

    protected override void OnDispose()
    {
        Safe(
            () =>
            {
                DisposeManagedResources();
                base.OnDispose();
                Log(LogLevel.Debug, LOG_PREFIX, "Disposed");
            },
            new ErrorHandlingOptions
            {
                Source = $"{LOG_PREFIX}.OnDispose",
                ErrorMessage = "Error during OnDispose"
            }
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
}