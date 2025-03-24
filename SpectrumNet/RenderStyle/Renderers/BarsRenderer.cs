#nullable enable

namespace SpectrumNet
{
    /// <summary>
    /// Renderer that visualizes spectrum data as vertical bars with effects.
    /// </summary>
    public sealed class BarsRenderer : BaseSpectrumRenderer
    {
        #region Constants
        private static class Constants
        {
            // Logging
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
            public const float GLOW_BLUR_RADIUS_LOW = 1.0f;
            public const float GLOW_BLUR_RADIUS_MEDIUM = 2.0f;
            public const float GLOW_BLUR_RADIUS_HIGH = 3.0f;
        }
        #endregion

        #region Structures
        /// <summary>
        /// Configuration parameters for bar rendering.
        /// </summary>
        public readonly record struct RenderConfig(float BarWidth, float BarSpacing, int BarCount);
        #endregion

        #region Fields
        private static readonly Lazy<BarsRenderer> _instance = new(() => new BarsRenderer());

        // Object pools for efficient resource management
        private readonly ObjectPool<SKPath> _pathPool = new(() => new SKPath(), path => path.Reset(), 5);
        private readonly ObjectPool<SKPaint> _paintPool = new(() => new SKPaint(), paint => paint.Reset(), 5);

        // Reusable paint objects
        private readonly SKPaint _barPaint;
        private readonly SKPaint _highlightPaint;
        private readonly SKPaint _glowPaint;

        // Quality-dependent settings
        private new bool _useAntiAlias = true;
        private SKSamplingOptions _samplingOptions = new(SKFilterMode.Linear, SKMipmapMode.Linear);
        private new bool _useAdvancedEffects = true;
        private bool _useGlowEffect = true;
        private float _glowRadius = Constants.GLOW_BLUR_RADIUS_MEDIUM;

        // Thread safety
        private readonly object _renderLock = new();
        private new bool _disposed;
        #endregion

        #region Singleton Pattern
        /// <summary>
        /// Private constructor to enforce Singleton pattern.
        /// </summary>
        private BarsRenderer()
        {
            _barPaint = CreateBasicPaint(SKColors.White);
            _highlightPaint = CreateBasicPaint(SKColors.White, SKPaintStyle.Fill);
            _glowPaint = CreateGlowPaint(
                SKColors.White,
                Constants.GLOW_BLUR_RADIUS_MEDIUM,
                CalculateGlowAlpha());
        }

        /// <summary>
        /// Gets the singleton instance of the bars renderer.
        /// </summary>
        public static BarsRenderer GetInstance() => _instance.Value;
        #endregion

        #region Initialization and Configuration
        /// <summary>
        /// Initializes the bars renderer and prepares rendering resources.
        /// </summary>
        public override void Initialize()
        {
            SmartLogger.Safe(() =>
            {
                base.Initialize();

                // Apply initial quality settings
                ApplyQualitySettings();

                SmartLogger.Log(LogLevel.Debug, Constants.LOG_PREFIX, "Initialized");
            }, new SmartLogger.ErrorHandlingOptions
            {
                Source = $"{Constants.LOG_PREFIX}.Initialize",
                ErrorMessage = "Failed to initialize renderer"
            });
        }

        /// <summary>
        /// Configures the renderer with overlay status and quality settings.
        /// </summary>
        /// <param name="isOverlayActive">Indicates if the renderer is used in overlay mode.</param>
        /// <param name="quality">The rendering quality level.</param>
        public override void Configure(bool isOverlayActive, RenderQuality quality = RenderQuality.Medium)
        {
            SmartLogger.Safe(() =>
            {
                base.Configure(isOverlayActive, quality);

                // Apply quality settings if changed
                if (_quality != quality)
                {
                    ApplyQualitySettings();
                }
            }, new SmartLogger.ErrorHandlingOptions
            {
                Source = $"{Constants.LOG_PREFIX}.Configure",
                ErrorMessage = "Failed to configure renderer"
            });
        }

        /// <summary>
        /// Applies quality settings based on the current quality level.
        /// </summary>
        protected override void ApplyQualitySettings()
        {
            SmartLogger.Safe(() =>
            {
                base.ApplyQualitySettings();

                switch (_quality)
                {
                    case RenderQuality.Low:
                        _useAntiAlias = false;
                        _samplingOptions = new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None);
                        _useAdvancedEffects = false;
                        _useGlowEffect = false;
                        _glowRadius = Constants.GLOW_BLUR_RADIUS_LOW;
                        break;

                    case RenderQuality.Medium:
                        _useAntiAlias = true;
                        _samplingOptions = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);
                        _useAdvancedEffects = true;
                        _useGlowEffect = true;
                        _glowRadius = Constants.GLOW_BLUR_RADIUS_MEDIUM;
                        break;

                    case RenderQuality.High:
                        _useAntiAlias = true;
                        _samplingOptions = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);
                        _useAdvancedEffects = true;
                        _useGlowEffect = true;
                        _glowRadius = Constants.GLOW_BLUR_RADIUS_HIGH;
                        break;
                }

                // Update paint settings
                _barPaint.IsAntialias = _useAntiAlias;
                _highlightPaint.IsAntialias = _useAntiAlias;
                _glowPaint.IsAntialias = _useAntiAlias;
                _glowPaint.MaskFilter = _useGlowEffect ?
                    SKMaskFilter.CreateBlur(SKBlurStyle.Normal, _glowRadius) : null;

                SmartLogger.Log(LogLevel.Debug, Constants.LOG_PREFIX, $"Quality set to {_quality}");
            }, new SmartLogger.ErrorHandlingOptions
            {
                Source = $"{Constants.LOG_PREFIX}.ApplyQualitySettings",
                ErrorMessage = "Failed to apply quality settings"
            });
        }
        #endregion

        #region Rendering
        /// <summary>
        /// Renders the bars visualization on the canvas using spectrum data.
        /// </summary>
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

            // Define render bounds and quick reject if not visible
            SKRect renderBounds = new(0, 0, info.Width, info.Height);
            if (canvas!.QuickReject(renderBounds))
            {
                drawPerformanceInfo?.Invoke(canvas, info);
                return;
            }

            SmartLogger.Safe(() =>
            {
                lock (_renderLock)
                {
                    RenderWithTemplate(
                        canvas,
                        spectrum,
                        info,
                        barWidth,
                        barSpacing,
                        barCount,
                        paint,
                        drawPerformanceInfo,
                        RenderSpectrumBar);
                }
            }, new SmartLogger.ErrorHandlingOptions
            {
                Source = $"{Constants.LOG_PREFIX}.Render",
                ErrorMessage = "Error during rendering"
            });

            drawPerformanceInfo?.Invoke(canvas!, info);
        }

        /// <summary>
        /// Renders the spectrum as bars.
        /// </summary>
        private void RenderSpectrumBar(
            SKCanvas canvas,
            float[] spectrum,
            SKImageInfo info,
            float barWidth,
            float barSpacing,
            SKPaint basePaint)
        {
            float totalBarWidth = barWidth + barSpacing;
            float canvasHeight = info.Height;
            float cornerRadius = CalculateCornerRadius(barWidth);

            for (int i = 0; i < spectrum.Length; i++)
            {
                float magnitude = spectrum[i];
                if (magnitude < MIN_MAGNITUDE_THRESHOLD) continue;

                float barHeight = CalculateBarHeight(magnitude, canvasHeight);
                byte alpha = CalculateBarAlpha(magnitude);

                using var tempBarPaint = _paintPool.Get();
                tempBarPaint.Color = basePaint.Color.WithAlpha(alpha);
                tempBarPaint.IsAntialias = _useAntiAlias;

                float x = i * totalBarWidth;

                if (IsRenderAreaVisible(canvas, x, 0, barWidth, canvasHeight))
                {
                    RenderBarWithEffects(
                        canvas,
                        x,
                        barWidth,
                        barHeight,
                        canvasHeight,
                        cornerRadius,
                        tempBarPaint,
                        magnitude);
                }
            }
        }
        #endregion

        #region Bar Rendering Methods
        /// <summary>
        /// Renders a bar with all applicable effects based on quality settings.
        /// </summary>
        private void RenderBarWithEffects(
            SKCanvas canvas,
            float x,
            float barWidth,
            float barHeight,
            float canvasHeight,
            float cornerRadius,
            SKPaint barPaint,
            float magnitude)
        {
            // Render glow effect for high intensity bars if enabled
            if (_useGlowEffect && _useAdvancedEffects &&
                magnitude > Constants.HIGH_INTENSITY_THRESHOLD)
            {
                RenderGlowEffect(canvas, x, barWidth, barHeight, canvasHeight, cornerRadius, magnitude);
            }

            // Render main bar
            RenderBar(canvas, x, barWidth, barHeight, canvasHeight, cornerRadius, barPaint);

            // Render highlight on top of bar if quality allows
            if (barHeight > cornerRadius * 2 && _quality != RenderQuality.Low)
            {
                RenderBarHighlight(
                    canvas,
                    x,
                    barWidth,
                    barHeight,
                    canvasHeight,
                    _highlightPaint,
                    barPaint.Color.Alpha);
            }
        }

        /// <summary>
        /// Renders the glow effect behind a bar.
        /// </summary>
        private void RenderGlowEffect(
            SKCanvas canvas,
            float x,
            float barWidth,
            float barHeight,
            float canvasHeight,
            float cornerRadius,
            float magnitude)
        {
            _glowPaint.Color = SKColors.White.WithAlpha(
                (byte)(magnitude * 255f * Constants.GLOW_EFFECT_ALPHA));

            DrawRoundedRect(
                canvas,
                x,
                canvasHeight - barHeight,
                barWidth,
                barHeight,
                cornerRadius,
                _glowPaint);
        }

        /// <summary>
        /// Renders the main bar.
        /// </summary>
        private void RenderBar(
            SKCanvas canvas,
            float x,
            float barWidth,
            float barHeight,
            float canvasHeight,
            float cornerRadius,
            SKPaint barPaint)
        {
            DrawRoundedRect(
                canvas,
                x,
                canvasHeight - barHeight,
                barWidth,
                barHeight,
                cornerRadius,
                barPaint);
        }

        /// <summary>
        /// Renders a highlight at the top of the bar.
        /// </summary>
        private void RenderBarHighlight(
            SKCanvas canvas,
            float x,
            float barWidth,
            float barHeight,
            float canvasHeight,
            SKPaint highlightPaint,
            byte baseAlpha)
        {
            float highlightWidth = barWidth * Constants.HIGHLIGHT_WIDTH_PROPORTION;
            float highlightHeight = MathF.Min(
                barHeight * Constants.HIGHLIGHT_HEIGHT_PROPORTION,
                Constants.MAX_HIGHLIGHT_HEIGHT);

            float highlightX = x + (barWidth - highlightWidth) / 2;

            highlightPaint.Color = highlightPaint.Color.WithAlpha(
                (byte)(baseAlpha / Constants.HIGHLIGHT_ALPHA_DIVISOR));

            canvas.DrawRect(
                highlightX,
                canvasHeight - barHeight,
                highlightWidth,
                highlightHeight,
                highlightPaint);
        }
        #endregion

        #region Helper Methods
        /// <summary>
        /// Calculates the glow alpha value.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static byte CalculateGlowAlpha() =>
            (byte)(255f * Constants.GLOW_EFFECT_ALPHA);

        /// <summary>
        /// Checks if the render area is visible on the canvas.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsRenderAreaVisible(
            SKCanvas canvas,
            float x,
            float y,
            float width,
            float height)
        {
            var clipBounds = canvas.LocalClipBounds;
            return clipBounds.IntersectsWith(new SKRect(x, y, x + width, y + height));
        }

        /// <summary>
        /// Calculates the corner radius for bars.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private float CalculateCornerRadius(float barWidth) =>
            MathF.Min(barWidth * Constants.DEFAULT_CORNER_RADIUS_FACTOR,
                      Constants.MAX_CORNER_RADIUS);

        /// <summary>
        /// Calculates the height of a bar based on magnitude.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private float CalculateBarHeight(float magnitude, float canvasHeight) =>
            MathF.Max(magnitude * canvasHeight, Constants.MIN_BAR_HEIGHT);

        /// <summary>
        /// Calculates the alpha value for a bar based on magnitude.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private byte CalculateBarAlpha(float magnitude) =>
            (byte)MathF.Min(magnitude * Constants.ALPHA_MULTIPLIER * 255f, 255f);

        /// <summary>
        /// Draws a rounded rectangle.
        /// </summary>
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
            canvas.DrawRoundRect(
                new SKRect(x, y, x + width, y + height),
                cornerRadius,
                cornerRadius,
                paint);
        }
        #endregion

        #region Disposal
        /// <summary>
        /// Disposes of resources used by the renderer.
        /// </summary>
        public override void Dispose()
        {
            if (!_disposed)
            {
                SmartLogger.Safe(() =>
                {
                    _barPaint?.Dispose();
                    _highlightPaint?.Dispose();
                    _glowPaint?.Dispose();

                    _pathPool?.Dispose();
                    _paintPool?.Dispose();

                    base.Dispose();

                    _disposed = true;
                    SmartLogger.Log(LogLevel.Debug, Constants.LOG_PREFIX, "Disposed");
                }, new SmartLogger.ErrorHandlingOptions
                {
                    Source = $"{Constants.LOG_PREFIX}.Dispose",
                    ErrorMessage = "Error during disposal"
                });
            }
        }
        #endregion
    }
}