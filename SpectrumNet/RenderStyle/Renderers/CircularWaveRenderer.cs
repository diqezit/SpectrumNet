#nullable enable

namespace SpectrumNet
{
    /// <summary>
    /// Renders spectrum data as a circular wave with rotation and glow effects.
    /// </summary>
    public sealed class CircularWaveRenderer : BaseSpectrumRenderer
    {
        #region Constants
        private static class Constants
        {
            public const string LOG_PREFIX = "CircularWaveRenderer";
            public const float ROTATION_SPEED = 0.5f;
            public const float RADIUS_PROPORTION = 0.4f;
            public const float AMPLITUDE_SCALE = 0.5f;
            public const float MIN_MAGNITUDE_THRESHOLD = 0.01f;
            public const int MAX_POINT_COUNT = 180;
            public const int MIN_POINT_COUNT = 12;
            public const float GLOW_INTENSITY = 0.5f;
            public const float HIGH_INTENSITY_THRESHOLD = 0.7f;
            public const float WAVE_ALPHA_MULTIPLIER = 1.2f;
            public const float INNER_GLOW_ALPHA = 0.3f;
            public const float FILL_ALPHA_RATIO = 0.4f;
            public const float RADIUS_SCALE_FACTOR = 0.1f;
            public const float AMPLITUDE_SCALE_FACTOR = 0.2f;
            public const float BAR_COUNT_NORMALIZATION = 100f;
            public const float MIN_BLUR_RADIUS = 4f;
            public const float MAX_BLUR_RADIUS = 12f;
            public const float BLUR_WIDTH_RATIO = 0.8f;
            public const float OUTLINE_WIDTH_RATIO = 0.2f;

            // Performance settings by quality
            public const int LOW_QUALITY_POINTS = 60;
            public const int MEDIUM_QUALITY_POINTS = 120;
            public const int HIGH_QUALITY_POINTS = 180;
        }
        #endregion

        #region Configuration Records
        public record RenderConfig(
            float RotationSpeed = Constants.ROTATION_SPEED,
            float RadiusProportion = Constants.RADIUS_PROPORTION,
            float AmplitudeScale = Constants.AMPLITUDE_SCALE,
            float MinMagnitudeThreshold = Constants.MIN_MAGNITUDE_THRESHOLD,
            int MaxPointCount = Constants.MAX_POINT_COUNT);
        #endregion

        #region Fields
        private static readonly Lazy<CircularWaveRenderer> _instance = new(() => new CircularWaveRenderer());
        private float _rotation;
        private float _rotationSpeed = Constants.ROTATION_SPEED;
        private float _radiusProportion = Constants.RADIUS_PROPORTION;
        private float _amplitudeScale = Constants.AMPLITUDE_SCALE;
        private float _minMagnitudeThreshold = Constants.MIN_MAGNITUDE_THRESHOLD;
        private int _maxPointCount = Constants.MAX_POINT_COUNT;
        private readonly SKPath _wavePath = new();
        private SKPaint? _glowPaint, _innerGlowPaint, _outlinePaint, _fillPaint;
        private SKPoint[]? _wavePoints;
        private bool _useGlowEffects = true;
        private bool _useInnerGlow = true;
        #endregion

        #region Constructor and Initialization
        private CircularWaveRenderer() { }

        public static CircularWaveRenderer GetInstance() => _instance.Value;

        public override void Initialize() => SmartLogger.Safe(() =>
        {
            base.Initialize();
            InitializeRenderResources();
            SmartLogger.Log(LogLevel.Debug, Constants.LOG_PREFIX, "Initialized");
        }, new SmartLogger.ErrorHandlingOptions
        {
            Source = $"{Constants.LOG_PREFIX}.Initialize",
            ErrorMessage = "Failed to initialize renderer"
        });
        #endregion

        #region Configuration
        public override void Configure(bool isOverlayActive, RenderQuality quality = RenderQuality.Medium) =>
            SmartLogger.Safe(() =>
            {
                base.Configure(isOverlayActive, quality);
                ApplyQualitySettings();
            }, new SmartLogger.ErrorHandlingOptions
            {
                Source = $"{Constants.LOG_PREFIX}.Configure",
                ErrorMessage = "Failed to configure renderer"
            });

        public void ConfigureAdvanced(RenderConfig config) => SmartLogger.Safe(() =>
        {
            (_rotationSpeed, _radiusProportion, _amplitudeScale, _minMagnitudeThreshold) =
                (config.RotationSpeed, config.RadiusProportion, config.AmplitudeScale,
                 config.MinMagnitudeThreshold);

            // Apply quality-based point count limitation
            _maxPointCount = LimitPointCountByQuality(config.MaxPointCount);

            SmartLogger.Log(LogLevel.Debug, Constants.LOG_PREFIX, "Advanced configuration applied");
        }, new SmartLogger.ErrorHandlingOptions
        {
            Source = $"{Constants.LOG_PREFIX}.ConfigureAdvanced",
            ErrorMessage = "Failed to apply advanced configuration"
        });

        private int LimitPointCountByQuality(int requestedCount) => _quality switch
        {
            RenderQuality.Low => Math.Min(requestedCount, Constants.LOW_QUALITY_POINTS),
            RenderQuality.Medium => Math.Min(requestedCount, Constants.MEDIUM_QUALITY_POINTS),
            RenderQuality.High => Math.Min(requestedCount, Constants.HIGH_QUALITY_POINTS),
            _ => requestedCount
        };

        private void InitializeRenderResources() => SmartLogger.Safe(() =>
        {
            DisposeRenderPaints();

            _glowPaint = new SKPaint { IsAntialias = _useAntiAlias, FilterQuality = _filterQuality, Style = SKPaintStyle.Fill };
            _innerGlowPaint = new SKPaint { IsAntialias = _useAntiAlias, FilterQuality = _filterQuality, Style = SKPaintStyle.Fill, Color = SKColors.White };
            _outlinePaint = new SKPaint { IsAntialias = _useAntiAlias, FilterQuality = _filterQuality, Style = SKPaintStyle.Stroke };
            _fillPaint = new SKPaint { IsAntialias = _useAntiAlias, FilterQuality = _filterQuality, Style = SKPaintStyle.Fill };
        }, new SmartLogger.ErrorHandlingOptions
        {
            Source = $"{Constants.LOG_PREFIX}.InitializeRenderResources",
            ErrorMessage = "Failed to initialize render resources"
        });

        private void DisposeRenderPaints() => SmartLogger.Safe(() =>
        {
            SmartLogger.SafeDispose(_glowPaint, "glowPaint");
            SmartLogger.SafeDispose(_innerGlowPaint, "innerGlowPaint");
            SmartLogger.SafeDispose(_outlinePaint, "outlinePaint");
            SmartLogger.SafeDispose(_fillPaint, "fillPaint");

            _glowPaint = _innerGlowPaint = _outlinePaint = _fillPaint = null;
        }, new SmartLogger.ErrorHandlingOptions
        {
            Source = $"{Constants.LOG_PREFIX}.DisposeRenderPaints",
            ErrorMessage = "Failed to dispose render paints"
        });
        #endregion

        #region Quality Settings
        protected override void ApplyQualitySettings()
        {
            base.ApplyQualitySettings();

            // Key performance optimizations based on quality level
            switch (_quality)
            {
                case RenderQuality.Low:
                    _useGlowEffects = false;
                    _useInnerGlow = false;
                    _maxPointCount = Constants.LOW_QUALITY_POINTS;
                    break;

                case RenderQuality.Medium:
                    _useGlowEffects = true;
                    _useInnerGlow = false; // Disable inner glow for medium as it's expensive
                    _maxPointCount = Constants.MEDIUM_QUALITY_POINTS;
                    break;

                case RenderQuality.High:
                    _useGlowEffects = true;
                    _useInnerGlow = true;
                    _maxPointCount = Constants.HIGH_QUALITY_POINTS;
                    break;

                default:
                    _useGlowEffects = true;
                    _useInnerGlow = false;
                    break;
            }
            InitializeRenderResources();
        }
        #endregion

        #region Rendering
        public override void Render(
            SKCanvas? canvas,
            float[]? spectrum,
            SKImageInfo info,
            float barWidth,
            float barSpacing,
            int barCount,
            SKPaint? paint,
            Action<SKCanvas, SKImageInfo>? drawPerformanceInfo)
        {
            if (!QuickValidate(canvas, spectrum, info, paint))
            {
                drawPerformanceInfo?.Invoke(canvas!, info);
                return;
            }

            // Limit point count based on quality
            int pointCount = Math.Max(Constants.MIN_POINT_COUNT,
                Math.Min(Math.Min(spectrum!.Length, _maxPointCount), barCount));

            float centerX = info.Width / 2f, centerY = info.Height / 2f;

            float normalizationFactor = 1f - Math.Min(barCount, Constants.BAR_COUNT_NORMALIZATION) /
                                                        Constants.BAR_COUNT_NORMALIZATION;
            float radius = MathF.Min(info.Width, info.Height) * _radiusProportion *
                                            (1f + Constants.RADIUS_SCALE_FACTOR * normalizationFactor);
            float amplitudeScale = _amplitudeScale *
                                                (1f + Constants.AMPLITUDE_SCALE_FACTOR * normalizationFactor);

            float maxRadius = radius * (1f + amplitudeScale);
            SKRect renderBounds = new(
                centerX - maxRadius,
                centerY - maxRadius,
                centerX + maxRadius,
                centerY + maxRadius
            );

            if (canvas!.QuickReject(renderBounds))
            {
                drawPerformanceInfo?.Invoke(canvas, info);
                return;
            }

            SmartLogger.Safe(() =>
            {
                _rotation = (_rotation + _rotationSpeed) % 360f;

                float[] processedSpectrum = PrepareSpectrum(spectrum, pointCount, spectrum.Length);

                RenderCircularWave(canvas, processedSpectrum, pointCount, radius, centerX, centerY, paint!, amplitudeScale, barWidth);
            }, new SmartLogger.ErrorHandlingOptions
            {
                Source = $"{Constants.LOG_PREFIX}.Render",
                ErrorMessage = "Error during rendering"
            });

            drawPerformanceInfo?.Invoke(canvas, info);
        }

        private void RenderCircularWave(
            SKCanvas canvas,
            float[] spectrum,
            int pointCount,
            float radius,
            float centerX,
            float centerY,
            SKPaint basePaint,
            float amplitudeScale,
            float barWidth) => SmartLogger.Safe(() =>
            {
                if (canvas == null || spectrum == null || basePaint == null || _disposed)
                    return;

                if (_wavePoints == null || _wavePoints.Length != pointCount)
                    _wavePoints = new SKPoint[pointCount];

                float rad = _rotation * MathF.PI / 180f;
                float cosDelta = MathF.Cos(rad), sinDelta = MathF.Sin(rad);

                int validPointCount = 0;
                float maxAmplitude = 0f;

                // Performance-optimized point preparation
                PrepareWavePoints(
                    spectrum, pointCount, radius, centerX, centerY,
                    cosDelta, sinDelta, amplitudeScale,
                    ref validPointCount, ref maxAmplitude);

                if (validPointCount == 0)
                    return;

                _wavePath.Reset();
                if (validPointCount > 0)
                {
                    // Use AddPoly for efficient GPU-friendly path creation
                    SKPoint[] validPoints = new SKPoint[validPointCount];
                    Array.Copy(_wavePoints!, 0, validPoints, 0, validPointCount);
                    _wavePath.AddPoly(validPoints, true);
                }

                // Optimized rendering with reduced effects for better performance
                RenderSimplifiedWaveEffects(canvas, _wavePath, basePaint, maxAmplitude, barWidth);
            }, new SmartLogger.ErrorHandlingOptions
            {
                Source = $"{Constants.LOG_PREFIX}.RenderCircularWave",
                ErrorMessage = "Error rendering circular wave"
            });

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private void PrepareWavePoints(
            float[] spectrum, int pointCount, float radius, float centerX, float centerY,
            float cosDelta, float sinDelta, float amplitudeScale,
            ref int validPointCount, ref float maxAmplitude)
        {
            validPointCount = 0;
            maxAmplitude = 0f;
            float angleStep = 2 * MathF.PI / pointCount;

            // Skip points for low quality to improve performance
            int step = _quality == RenderQuality.Low ? 2 : 1;

            for (int i = 0; i < pointCount; i += step)
            {
                float amplitude = spectrum[i];
                maxAmplitude = Math.Max(maxAmplitude, amplitude);

                if (amplitude < _minMagnitudeThreshold)
                    continue;

                float angle = i * angleStep;
                float baseCos = MathF.Cos(angle), baseSin = MathF.Sin(angle);

                float r = radius * (1f + amplitude * amplitudeScale);
                float rotatedCos = baseCos * cosDelta - baseSin * sinDelta;
                float rotatedSin = baseSin * cosDelta + baseCos * sinDelta;

                _wavePoints![validPointCount++] = new SKPoint(
                    centerX + r * rotatedCos,
                    centerY + r * rotatedSin
                );
            }
        }

        // Simplified rendering with fewer effects for better performance
        private void RenderSimplifiedWaveEffects(
            SKCanvas canvas,
            SKPath path,
            SKPaint basePaint,
            float maxAmplitude,
            float barWidth) => SmartLogger.Safe(() =>
            {
                byte alpha = (byte)(basePaint.Color.Alpha * Math.Min(maxAmplitude * Constants.WAVE_ALPHA_MULTIPLIER, 1.0f));

                // Reduce blur radius significantly for better performance
                float blurRadius = _quality switch
                {
                    RenderQuality.Low => 2f,
                    RenderQuality.Medium => 3f,
                    RenderQuality.High => 4f,
                    _ => 3f
                };

                // Render in optimal order for GPU (fill first, then outline, then effects)

                // 1. Fill (most efficient)
                if (_fillPaint != null)
                {
                    _fillPaint.Color = basePaint.Color.WithAlpha((byte)(alpha * Constants.FILL_ALPHA_RATIO));
                    canvas.DrawPath(path, _fillPaint);
                }

                // 2. Outline
                if (_outlinePaint != null)
                {
                    _outlinePaint.Color = basePaint.Color.WithAlpha(alpha);
                    // Reduce outline width for better performance
                    _outlinePaint.StrokeWidth = Math.Max(1f, barWidth * (_quality == RenderQuality.High ?
                        Constants.OUTLINE_WIDTH_RATIO : Constants.OUTLINE_WIDTH_RATIO * 0.7f));
                    canvas.DrawPath(path, _outlinePaint);
                }

                // 3. Glow effects - only if needed and performance allows
                if (_useGlowEffects && _useAdvancedEffects && _glowPaint != null)
                {
                    _glowPaint.Color = basePaint.Color.WithAlpha((byte)(alpha * Constants.GLOW_INTENSITY));

                    // Use ImageFilter for high quality, MaskFilter for medium (more efficient)
                    if (_quality == RenderQuality.High)
                    {
                        _glowPaint.ImageFilter = SKImageFilter.CreateBlur(blurRadius, blurRadius);
                        _glowPaint.MaskFilter = null;
                    }
                    else
                    {
                        _glowPaint.MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, blurRadius);
                        _glowPaint.ImageFilter = null;
                    }

                    canvas.DrawPath(path, _glowPaint);
                }

                // 4. Inner glow - only for high quality and high intensity
                if (_useInnerGlow && _useAdvancedEffects &&
                    maxAmplitude > Constants.HIGH_INTENSITY_THRESHOLD &&
                    _innerGlowPaint != null && _quality == RenderQuality.High)
                {
                    _innerGlowPaint.Color = _innerGlowPaint.Color.WithAlpha((byte)(alpha * Constants.INNER_GLOW_ALPHA));
                    _innerGlowPaint.MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, blurRadius * 0.5f);
                    canvas.DrawPath(path, _innerGlowPaint);
                }
            }, new SmartLogger.ErrorHandlingOptions
            {
                Source = $"{Constants.LOG_PREFIX}.RenderSimplifiedWaveEffects",
                ErrorMessage = "Error rendering wave effects"
            });
        #endregion

        #region Disposal
        public override void Dispose()
        {
            if (!_disposed)
            {
                SmartLogger.Safe(() =>
                {
                    SmartLogger.SafeDispose(_wavePath, "wavePath");
                    DisposeRenderPaints();
                    _wavePoints = null;
                    base.Dispose();
                }, new SmartLogger.ErrorHandlingOptions
                {
                    Source = $"{Constants.LOG_PREFIX}.Dispose",
                    ErrorMessage = "Error disposing renderer"
                });

                _disposed = true;
                SmartLogger.Log(LogLevel.Debug, Constants.LOG_PREFIX, "Disposed");
            }
        }
        #endregion
    }
}