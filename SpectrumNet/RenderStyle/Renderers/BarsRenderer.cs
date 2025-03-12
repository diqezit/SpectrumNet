namespace SpectrumNet
{
    public class BarsRenderer : BaseSpectrumRenderer
    {
        #region Constants
        private static class Constants
        {
            public const string LOG_PREFIX = "BarsRenderer";
            public const float MAX_CORNER_RADIUS = 10f;
            public const float HIGHLIGHT_WIDTH_PROPORTION = 0.6f;
            public const float HIGHLIGHT_HEIGHT_PROPORTION = 0.1f;
            public const float MAX_HIGHLIGHT_HEIGHT = 5f;
            public const float ALPHA_MULTIPLIER = 1.5f;
            public const float HIGHLIGHT_ALPHA_DIVISOR = 3f;
            public const float DEFAULT_CORNER_RADIUS_FACTOR = 5.0f;
            public const float GLOW_EFFECT_ALPHA = 0.25f;
            public const float MIN_BAR_HEIGHT = 1f;
            public const float HIGH_INTENSITY_THRESHOLD = 0.6f;
        }
        #endregion

        #region Fields
        private static BarsRenderer? _instance;
        private readonly SKPath _path = new();
        private bool _useGlowEffect = true;
        #endregion

        #region Constructor and Initialization
        private BarsRenderer() { }

        public static BarsRenderer GetInstance() => _instance ??= new BarsRenderer();

        public override void Initialize()
        {
            base.Initialize();
            SmartLogger.Log(LogLevel.Debug, Constants.LOG_PREFIX, "BarsRenderer initialized");
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
            Action<SKCanvas, SKImageInfo> drawPerformanceInfo)
        {
            if (!ValidateRenderParameters(canvas, spectrum, info, paint, "BarsRenderer"))
            {
                return;
            }

            float[] renderSpectrum;
            bool semaphoreAcquired = false;
            int renderedBarCount;

            try
            {
                semaphoreAcquired = _spectrumSemaphore.Wait(0);

                if (semaphoreAcquired)
                {
                    int targetBarCount = Math.Min(spectrum!.Length, barCount);
                    float[] scaledSpectrum = ScaleSpectrum(spectrum!, targetBarCount, spectrum!.Length);
                    _processedSpectrum = SmoothSpectrum(scaledSpectrum, targetBarCount);
                }

                lock (_spectrumLock)
                {
                    renderSpectrum = _processedSpectrum ??
                                     ScaleSpectrum(spectrum!, barCount, spectrum!.Length);
                    renderedBarCount = renderSpectrum.Length;
                }
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, Constants.LOG_PREFIX, $"Error processing spectrum: {ex.Message}");
                return;
            }
            finally
            {
                if (semaphoreAcquired)
                {
                    _spectrumSemaphore.Release();
                }
            }

            // Проверяем видимость области рендеринга
            float totalWidth = renderedBarCount * (barWidth + barSpacing);
            if (canvas!.QuickReject(new SKRect(0, 0, totalWidth, info.Height)))
            {
                drawPerformanceInfo?.Invoke(canvas!, info);
                return;
            }

            RenderBars(canvas!, renderSpectrum, info, barWidth, barSpacing, paint!);
            drawPerformanceInfo?.Invoke(canvas!, info);
        }

        private void RenderBars(
            SKCanvas canvas,
            float[] spectrum,
            SKImageInfo info,
            float barWidth,
            float barSpacing,
            SKPaint basePaint)
        {
            if (canvas == null || spectrum == null || basePaint == null || _disposed)
                return;

            float totalBarWidth = barWidth + barSpacing;
            float canvasHeight = info.Height;
            float cornerRadius = MathF.Min(barWidth * Constants.DEFAULT_CORNER_RADIUS_FACTOR, Constants.MAX_CORNER_RADIUS);

            using var barPaint = new SKPaint
            {
                IsAntialias = _useAntiAlias,
                FilterQuality = _filterQuality,
                Style = SKPaintStyle.Fill
            };

            using var highlightPaint = new SKPaint
            {
                IsAntialias = _useAntiAlias,
                Style = SKPaintStyle.Fill,
                Color = SKColors.White,
                FilterQuality = _filterQuality
            };

            for (int i = 0; i < spectrum.Length; i++)
            {
                float magnitude = spectrum[i];
                float barHeight = MathF.Max(magnitude * canvasHeight, Constants.MIN_BAR_HEIGHT);
                byte alpha = (byte)MathF.Min(magnitude * Constants.ALPHA_MULTIPLIER * 255f, 255f);
                barPaint.Color = basePaint.Color.WithAlpha(alpha);

                float x = i * totalBarWidth;

                if (_useGlowEffect && _useAdvancedEffects && magnitude > Constants.HIGH_INTENSITY_THRESHOLD)
                {
                    RenderGlowEffect(canvas, x, barWidth, barHeight, canvasHeight, cornerRadius, magnitude);
                }

                RenderBar(canvas, x, barWidth, barHeight, canvasHeight, cornerRadius, barPaint);

                if (barHeight > cornerRadius * 2 && _quality != RenderQuality.Low)
                {
                    RenderBarHighlight(canvas, x, barWidth, barHeight, canvasHeight, highlightPaint, alpha);
                }
            }
        }

        private void RenderGlowEffect(
            SKCanvas canvas,
            float x,
            float barWidth,
            float barHeight,
            float canvasHeight,
            float cornerRadius,
            float magnitude)
        {
            using var glowPaint = new SKPaint
            {
                IsAntialias = _useAntiAlias,
                FilterQuality = _filterQuality,
                Style = SKPaintStyle.Fill,
                Color = SKColors.White.WithAlpha((byte)(magnitude * 255f * Constants.GLOW_EFFECT_ALPHA)),
                MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 5f)
            };

            _path.Reset();
            _path.AddRoundRect(new SKRoundRect(
                new SKRect(x, canvasHeight - barHeight, x + barWidth, canvasHeight),
                cornerRadius, cornerRadius));
            canvas.DrawPath(_path, glowPaint);
        }

        private void RenderBar(
            SKCanvas canvas,
            float x,
            float barWidth,
            float barHeight,
            float canvasHeight,
            float cornerRadius,
            SKPaint barPaint)
        {
            _path.Reset();
            _path.AddRoundRect(new SKRoundRect(
                new SKRect(x, canvasHeight - barHeight, x + barWidth, canvasHeight),
                cornerRadius, cornerRadius));
            canvas.DrawPath(_path, barPaint);
        }

        private static void RenderBarHighlight(
            SKCanvas canvas,
            float x,
            float barWidth,
            float barHeight,
            float canvasHeight,
            SKPaint highlightPaint,
            byte alpha)
        {
            float highlightWidth = barWidth * Constants.HIGHLIGHT_WIDTH_PROPORTION;
            float highlightHeight = MathF.Min(barHeight * Constants.HIGHLIGHT_HEIGHT_PROPORTION, Constants.MAX_HIGHLIGHT_HEIGHT);
            byte highlightAlpha = (byte)(alpha / Constants.HIGHLIGHT_ALPHA_DIVISOR);
            highlightPaint.Color = highlightPaint.Color.WithAlpha(highlightAlpha);

            float highlightX = x + (barWidth - highlightWidth) / 2;
            canvas.DrawRect(
                highlightX,
                canvasHeight - barHeight,
                highlightWidth,
                highlightHeight,
                highlightPaint);
        }
        #endregion

        #region Quality Settings
        protected override void ApplyQualitySettings()
        {
            base.ApplyQualitySettings();

            switch (_quality)
            {
                case RenderQuality.Low:
                    _useGlowEffect = false;
                    break;
                case RenderQuality.Medium:
                case RenderQuality.High:
                    _useGlowEffect = true;
                    break;
            }

            SmartLogger.Log(LogLevel.Debug, Constants.LOG_PREFIX, $"BarsRenderer quality set to {_quality}");
        }
        #endregion

        #region Disposal
        public override void Dispose()
        {
            if (!_disposed)
            {
                base.Dispose();
                _path?.Dispose();
                _disposed = true;
                SmartLogger.Log(LogLevel.Debug, Constants.LOG_PREFIX, "BarsRenderer disposed");
            }
        }
        #endregion
    }
}