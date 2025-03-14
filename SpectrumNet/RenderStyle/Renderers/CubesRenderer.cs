#nullable enable

namespace SpectrumNet
{
    /// <summary>
    /// Renderer that creates 3D cube visualizations for spectrum data.
    /// </summary>
    public sealed class CubesRenderer : BaseSpectrumRenderer
    {
        #region Constants
        private static class Constants
        {
            // Logging
            public const string LOG_PREFIX = "CubesRenderer";

            // Rendering constants
            public const float CubeTopWidthProportion = 0.75f;   // Proportion of bar width for cube top
            public const float CubeTopHeightProportion = 0.25f;  // Proportion of bar width for cube top height
            public const float AlphaMultiplier = 255f;           // Multiplier for alpha calculation
            public const float TopAlphaFactor = 0.8f;            // Alpha factor for cube top
            public const float SideFaceAlphaFactor = 0.6f;       // Alpha factor for cube side face
            public const float MIN_MAGNITUDE_THRESHOLD = 0.01f;  // Minimum magnitude threshold for rendering
        }
        #endregion

        #region Fields
        private static readonly Lazy<CubesRenderer> _instance = new(() => new CubesRenderer());
        private readonly SKPath _cubeTopPath = new();
        private bool _useGlowEffects = true;
        #endregion

        #region Constructor and Initialization
        private CubesRenderer() { }

        public static CubesRenderer GetInstance() => _instance.Value;

        public override void Initialize() => SmartLogger.Safe(() =>
        {
            base.Initialize();
            SmartLogger.Log(LogLevel.Debug, Constants.LOG_PREFIX, "Initialized");
        }, new SmartLogger.ErrorHandlingOptions
        {
            Source = $"{Constants.LOG_PREFIX}.Initialize",
            ErrorMessage = "Failed to initialize renderer"
        });
        #endregion

        #region Configuration
        public override void Configure(bool isOverlayActive, RenderQuality quality = RenderQuality.Medium) => SmartLogger.Safe(() =>
        {
            base.Configure(isOverlayActive, quality);
        }, new SmartLogger.ErrorHandlingOptions
        {
            Source = $"{Constants.LOG_PREFIX}.Configure",
            ErrorMessage = "Failed to configure renderer"
        });

        protected override void ApplyQualitySettings() => SmartLogger.Safe(() =>
        {
            base.ApplyQualitySettings();

            _useGlowEffects = _quality switch
            {
                RenderQuality.Low => false,
                RenderQuality.Medium => true,
                RenderQuality.High => true,
                _ => true
            };
        }, new SmartLogger.ErrorHandlingOptions
        {
            Source = $"{Constants.LOG_PREFIX}.ApplyQualitySettings",
            ErrorMessage = "Failed to apply quality settings"
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
            SKPaint? paint,
            Action<SKCanvas, SKImageInfo>? drawPerformanceInfo)
        {
            if (!QuickValidate(canvas, spectrum, info, paint))
            {
                drawPerformanceInfo?.Invoke(canvas!, info);
                return;
            }

            SmartLogger.Safe(() =>
            {
                int spectrumLength = spectrum!.Length;
                int actualBarCount = Math.Min(spectrumLength, barCount);
                float[] processedSpectrum = PrepareSpectrum(spectrum, actualBarCount, spectrumLength);

                RenderSpectrum(canvas!, processedSpectrum, info, barWidth, barSpacing, paint!);
            }, new SmartLogger.ErrorHandlingOptions
            {
                Source = $"{Constants.LOG_PREFIX}.Render",
                ErrorMessage = "Error during rendering"
            });

            drawPerformanceInfo?.Invoke(canvas!, info);
        }

        private void RenderSpectrum(
            SKCanvas canvas,
            float[] spectrum,
            SKImageInfo info,
            float barWidth,
            float barSpacing,
            SKPaint basePaint) => SmartLogger.Safe(() =>
            {
                float canvasHeight = info.Height;

                using var cubePaint = basePaint.Clone();
                cubePaint.IsAntialias = _useAntiAlias;
                cubePaint.FilterQuality = _filterQuality;

                for (int i = 0; i < spectrum.Length; i++)
                {
                    float magnitude = spectrum[i];
                    if (magnitude < Constants.MIN_MAGNITUDE_THRESHOLD)
                        continue;

                    float height = magnitude * canvasHeight;
                    float x = i * (barWidth + barSpacing);
                    float y = canvasHeight - height;

                    if (canvas.QuickReject(new SKRect(x, y, x + barWidth, y + height)))
                        continue;

                    cubePaint.Color = basePaint.Color.WithAlpha((byte)(magnitude * Constants.AlphaMultiplier));
                    RenderCube(canvas, x, y, barWidth, height, magnitude, cubePaint);
                }
            }, new SmartLogger.ErrorHandlingOptions
            {
                Source = $"{Constants.LOG_PREFIX}.RenderSpectrum",
                ErrorMessage = "Error rendering spectrum"
            });

        private void RenderCube(
            SKCanvas canvas,
            float x,
            float y,
            float barWidth,
            float height,
            float magnitude,
            SKPaint paint) => SmartLogger.Safe(() =>
            {
                // Front face rendering with DrawRect
                canvas.DrawRect(x, y, barWidth, height, paint);

                if (_useGlowEffects)
                {
                    float topRightX = x + barWidth;
                    float topOffsetX = barWidth * Constants.CubeTopWidthProportion;
                    float topOffsetY = barWidth * Constants.CubeTopHeightProportion;

                    // Top face rendering
                    _cubeTopPath.Reset();
                    _cubeTopPath.MoveTo(x, y);
                    _cubeTopPath.LineTo(topRightX, y);
                    _cubeTopPath.LineTo(x + topOffsetX, y - topOffsetY);
                    _cubeTopPath.LineTo(x - (barWidth - topOffsetX), y - topOffsetY);
                    _cubeTopPath.Close();

                    using var topPaint = paint.Clone();
                    topPaint.Color = paint.Color.WithAlpha((byte)(magnitude * Constants.AlphaMultiplier * Constants.TopAlphaFactor));
                    canvas.DrawPath(_cubeTopPath, topPaint);

                    // Side face rendering
                    using var sidePath = new SKPath();
                    sidePath.MoveTo(topRightX, y);
                    sidePath.LineTo(topRightX, y + height);
                    sidePath.LineTo(x + topOffsetX, y - topOffsetY + height);
                    sidePath.LineTo(x + topOffsetX, y - topOffsetY);
                    sidePath.Close();

                    using var sidePaint = paint.Clone();
                    sidePaint.Color = paint.Color.WithAlpha((byte)(magnitude * Constants.AlphaMultiplier * Constants.SideFaceAlphaFactor));
                    canvas.DrawPath(sidePath, sidePaint);
                }
            }, new SmartLogger.ErrorHandlingOptions
            {
                Source = $"{Constants.LOG_PREFIX}.RenderCube",
                ErrorMessage = "Error rendering cube"
            });
        #endregion

        #region Disposal
        public override void Dispose()
        {
            if (!_disposed)
            {
                SmartLogger.Safe(() =>
                {
                    _cubeTopPath?.Dispose();
                    base.Dispose();
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