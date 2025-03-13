#nullable enable

namespace SpectrumNet
{
    /// <summary>
    /// Renders spectrum data as vertical bars with optional glow effects and highlights.
    /// </summary>
    public sealed class BarsRenderer : BaseSpectrumRenderer
    {
        #region Constants
        private static class Constants
        {
            public const string LOG_PREFIX = "BarsRenderer";

            // Rendering parameters
            public const float MAX_CORNER_RADIUS = 10f;
            public const float DEFAULT_CORNER_RADIUS_FACTOR = 5.0f;
            public const float MIN_BAR_HEIGHT = 1f;

            // Highlight settings
            public const float HIGHLIGHT_WIDTH_PROPORTION = 0.6f;
            public const float HIGHLIGHT_HEIGHT_PROPORTION = 0.1f;
            public const float MAX_HIGHLIGHT_HEIGHT = 5f;
            public const float HIGHLIGHT_ALPHA_DIVISOR = 3f;

            // Color and effect settings
            public const float ALPHA_MULTIPLIER = 1.5f;
            public const float HIGH_INTENSITY_THRESHOLD = 0.6f;

            // Glow effect settings
            public const float GLOW_EFFECT_ALPHA = 0.25f;
            public const float GLOW_BLUR_RADIUS = 5f;
        }
        #endregion

        #region Records
        public readonly record struct RenderConfig(float BarWidth, float BarSpacing, int BarCount);
        #endregion

        #region Fields
        private static readonly Lazy<BarsRenderer> _lazyInstance = new(
            () => new BarsRenderer(), System.Threading.LazyThreadSafetyMode.ExecutionAndPublication);

        private readonly SKPaint _barPaint;
        private readonly SKPaint _highlightPaint;
        private readonly SKPaint _glowPaint;
        private bool _useGlowEffect = true;
        #endregion

        #region Singleton
        private BarsRenderer()
        {
            _barPaint = CreateBasicPaint(SKColors.White);
            _highlightPaint = CreateBasicPaint(SKColors.White, SKPaintStyle.Fill);
            _glowPaint = CreateGlowPaint(SKColors.White, Constants.GLOW_BLUR_RADIUS, (byte)(255f * Constants.GLOW_EFFECT_ALPHA));
        }

        public static BarsRenderer GetInstance() => _lazyInstance.Value;
        #endregion

        #region Initialization
        public override void Initialize() => SmartLogger.Safe(
            () =>
            {
                base.Initialize();
                SmartLogger.Log(LogLevel.Debug, Constants.LOG_PREFIX, "Initialized");
            },
            new SmartLogger.ErrorHandlingOptions
            {
                Source = $"{Constants.LOG_PREFIX}.Initialize",
                ErrorMessage = "Failed to initialize renderer"
            });
        #endregion

        #region Rendering
        public override void Render(
            SKCanvas? canvas,
            float[]? spectrum,
            SKImageInfo info,
            float barWidth,
            float barSpacing,
            int barCount,
            SKPaint? basePaint,
            Action<SKCanvas, SKImageInfo> drawPerformanceInfo) => RenderWithTemplate(
                canvas,
                spectrum,
                info,
                barWidth,
                barSpacing,
                barCount,
                basePaint,
                drawPerformanceInfo,
                RenderBarsImplementation);

        private void RenderBarsImplementation(
            SKCanvas canvas,
            float[] spectrum,
            SKImageInfo info,
            float barWidth,
            float barSpacing,
            SKPaint basePaint) => SmartLogger.Safe(
            () =>
            {
                if (spectrum is null || basePaint is null) return;

                float totalBarWidth = barWidth + barSpacing;
                float canvasHeight = info.Height;
                float cornerRadius = MathF.Min(barWidth * Constants.DEFAULT_CORNER_RADIUS_FACTOR, Constants.MAX_CORNER_RADIUS);

                for (int i = 0; i < spectrum.Length; i++)
                {
                    float magnitude = spectrum[i];
                    if (magnitude < MIN_MAGNITUDE_THRESHOLD) continue;

                    float barHeight = MathF.Max(magnitude * canvasHeight, Constants.MIN_BAR_HEIGHT);
                    byte alpha = (byte)MathF.Min(magnitude * Constants.ALPHA_MULTIPLIER * 255f, 255f);

                    _barPaint.Color = basePaint.Color.WithAlpha(alpha);

                    float x = i * totalBarWidth;

                    if (IsRenderAreaVisible(canvas, x, 0, barWidth, canvasHeight))
                    {
                        if (_useGlowEffect && _useAdvancedEffects && magnitude > Constants.HIGH_INTENSITY_THRESHOLD)
                            RenderGlowEffect(canvas, x, barWidth, barHeight, canvasHeight, cornerRadius, magnitude);

                        RenderBar(canvas, x, barWidth, barHeight, canvasHeight, cornerRadius, _barPaint);

                        if (barHeight > cornerRadius * 2 && _quality != RenderQuality.Low)
                            RenderBarHighlight(canvas, x, barWidth, barHeight, canvasHeight, _highlightPaint, alpha);
                    }
                }
            },
            new SmartLogger.ErrorHandlingOptions
            {
                Source = $"{Constants.LOG_PREFIX}.RenderBarsImplementation",
                ErrorMessage = "Error rendering bars"
            });

        private void RenderGlowEffect(
            SKCanvas canvas,
            float x,
            float barWidth,
            float barHeight,
            float canvasHeight,
            float cornerRadius,
            float magnitude) => SmartLogger.Safe(
            () =>
            {
                _glowPaint.Color = SKColors.White.WithAlpha((byte)(magnitude * 255f * Constants.GLOW_EFFECT_ALPHA));
                DrawRoundedRect(canvas, x, canvasHeight - barHeight, barWidth, barHeight, cornerRadius, _glowPaint);
            },
            new SmartLogger.ErrorHandlingOptions
            {
                Source = $"{Constants.LOG_PREFIX}.RenderGlowEffect",
                ErrorMessage = "Error rendering glow effect"
            });

        private void RenderBar(
            SKCanvas canvas,
            float x,
            float barWidth,
            float barHeight,
            float canvasHeight,
            float cornerRadius,
            SKPaint barPaint) => SmartLogger.Safe(
            () =>
            {
                DrawRoundedRect(canvas, x, canvasHeight - barHeight, barWidth, barHeight, cornerRadius, barPaint);
            },
            new SmartLogger.ErrorHandlingOptions
            {
                Source = $"{Constants.LOG_PREFIX}.RenderBar",
                ErrorMessage = "Error rendering bar"
            });

        private static void RenderBarHighlight(
            SKCanvas canvas,
            float x,
            float barWidth,
            float barHeight,
            float canvasHeight,
            SKPaint highlightPaint,
            byte alpha) => SmartLogger.Safe(
            () =>
            {
                float highlightWidth = barWidth * Constants.HIGHLIGHT_WIDTH_PROPORTION;
                float highlightHeight = MathF.Min(barHeight * Constants.HIGHLIGHT_HEIGHT_PROPORTION, Constants.MAX_HIGHLIGHT_HEIGHT);
                float highlightX = x + (barWidth - highlightWidth) / 2;
                highlightPaint.Color = highlightPaint.Color.WithAlpha((byte)(alpha / Constants.HIGHLIGHT_ALPHA_DIVISOR));
                canvas.DrawRect(highlightX, canvasHeight - barHeight, highlightWidth, highlightHeight, highlightPaint);
            },
            new SmartLogger.ErrorHandlingOptions
            {
                Source = $"{Constants.LOG_PREFIX}.RenderBarHighlight",
                ErrorMessage = "Error rendering bar highlight"
            });

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void DrawRoundedRect(
            SKCanvas canvas,
            float x,
            float y,
            float width,
            float height,
            float cornerRadius,
            SKPaint paint)
        {
            canvas.DrawRoundRect(new SKRect(x, y, x + width, y + height), cornerRadius, cornerRadius, paint);
        }
        #endregion

        #region Quality Settings
        protected override void ApplyQualitySettings() => SmartLogger.Safe(
            () =>
            {
                base.ApplyQualitySettings();

                _useGlowEffect = _quality switch
                {
                    RenderQuality.Low => false,
                    RenderQuality.Medium or RenderQuality.High => true,
                    _ => _useGlowEffect
                };

                var (useAntiAlias, filterQuality, useAdvancedEffects) = QualityBasedSettings();

                _barPaint.IsAntialias = useAntiAlias;
                _barPaint.FilterQuality = filterQuality;
                _highlightPaint.IsAntialias = useAntiAlias;
                _highlightPaint.FilterQuality = filterQuality;
                _glowPaint.IsAntialias = useAntiAlias;
                _glowPaint.FilterQuality = filterQuality;
                _useAdvancedEffects = useAdvancedEffects;

                SmartLogger.Log(LogLevel.Debug, Constants.LOG_PREFIX, $"Quality set to {_quality}");
            },
            new SmartLogger.ErrorHandlingOptions
            {
                Source = $"{Constants.LOG_PREFIX}.ApplyQualitySettings",
                ErrorMessage = "Error applying quality settings"
            });
        #endregion

        #region Disposal
        public override void Dispose()
        {
            if (!_disposed)
            {
                SmartLogger.Safe(() =>
                {
                    SmartLogger.SafeDispose(_barPaint, "barPaint");
                    SmartLogger.SafeDispose(_highlightPaint, "highlightPaint");
                    SmartLogger.SafeDispose(_glowPaint, "glowPaint");

                    base.Dispose();

                    SmartLogger.Log(LogLevel.Debug, Constants.LOG_PREFIX, "Disposed");
                },
                new SmartLogger.ErrorHandlingOptions
                {
                    Source = $"{Constants.LOG_PREFIX}.Dispose",
                    ErrorMessage = "Error disposing renderer"
                });
            }
        }
        #endregion
    }
}