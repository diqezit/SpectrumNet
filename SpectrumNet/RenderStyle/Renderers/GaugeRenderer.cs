#nullable enable

using static System.Math;
using static System.Numerics.Vector;
using static System.Runtime.CompilerServices.MethodImplOptions;

namespace SpectrumNet
{
    /// <summary>
    /// Renderer that visualizes audio levels as an analog gauge with a needle and peak indicator.
    /// </summary>
    public sealed class GaugeRenderer : BaseSpectrumRenderer
    {
        #region Singleton Pattern
        private static readonly Lazy<GaugeRenderer> Instance = new(() => new GaugeRenderer(),
                                                                  LazyThreadSafetyMode.ExecutionAndPublication);

        private GaugeRenderer() { }

        /// <summary>
        /// Gets the singleton instance of the GaugeRenderer.
        /// </summary>
        public static GaugeRenderer GetInstance() => Instance.Value;
        #endregion

        #region Constants
        /// <summary>
        /// Constants used by the GaugeRenderer for defining gauge properties and behavior.
        /// </summary>
        private static class GaugeConstants
        {
            public const string LOG_PREFIX = "[GaugeRenderer]";

            public static class Db
            {
                public const float Max = 5f;
                public const float Min = -30f;
                public const float PeakThreshold = 5f;
            }

            public static class Angle
            {
                public const float Start = -150f;
                public const float End = -30f;
                public const float TotalRange = End - Start;
            }

            public static class Needle
            {
                public const float DefaultLengthMultiplier = 1.55f;
                public const float DefaultCenterYOffsetMultiplier = 0.4f;
                public const float StrokeWidth = 2.25f;
                public const float CenterCircleRadiusOverlay = 0.015f;
                public const float CenterCircleRadius = 0.02f;
                public const float BaseWidthMultiplier = 2.5f;
            }

            public static class Background
            {
                public const float OuterFrameCornerRadius = 8f;
                public const float InnerFramePadding = 4f;
                public const float InnerFrameCornerRadius = 6f;
                public const float BackgroundPadding = 4f;
                public const float BackgroundCornerRadius = 4f;
                public const float VuTextSizeFactor = 0.2f;
                public const float VuTextBottomOffsetFactor = 0.2f;
            }

            public static class Scale
            {
                public const float CenterYOffsetFactor = 0.15f;
                public const float RadiusXFactorOverlay = 0.4f;
                public const float RadiusXFactor = 0.45f;
                public const float RadiusYFactorOverlay = 0.45f;
                public const float RadiusYFactor = 0.5f;
                public const float TextOffsetFactorOverlay = 0.1f;
                public const float TextOffsetFactor = 0.12f;
                public const float TextSizeFactorOverlay = 0.08f;
                public const float TextSizeFactor = 0.1f;
                public const float TickLengthZeroFactorOverlay = 0.12f;
                public const float TickLengthZeroFactor = 0.15f;
                public const float TickLengthFactorOverlay = 0.07f;
                public const float TickLengthFactor = 0.08f;
                public const float TickLengthMinorFactorOverlay = 0.05f;
                public const float TickLengthMinorFactor = 0.06f;
                public const float TickStrokeWidth = 1.8f;
                public const float TextSizeMultiplierZero = 1.15f;
            }

            public static class PeakLamp
            {
                public const float RadiusFactorOverlay = 0.04f;
                public const float RadiusFactor = 0.05f;
                public const float LampXOffsetFactorOverlay = 0.12f;
                public const float LampXOffsetFactor = 0.1f;
                public const float LampYOffsetFactorOverlay = 0.18f;
                public const float LampYOffsetFactor = 0.2f;
                public const float TextSizeFactorOverlay = 1.2f;
                public const float TextSizeFactor = 1.5f;
                public const float TextYOffsetFactor = 2.5f;
                public const float RimStrokeWidth = 1f;
                public const float GlowRadiusMultiplier = 1.5f;
                public const float InnerRadiusMultiplier = 0.8f;
            }

            public static class Rendering
            {
                public const float AspectRatio = 2.0f;
                public const float GaugeRectPadding = 0.8f;
                public const float MinDbClamp = 1e-10f;
                public const float Margin = 0.05f;
            }

            public static class MinorMarks
            {
                public const int Divisor = 3;
            }
        }
        #endregion

        #region Fields
        private readonly GaugeState _state = new();
        private GaugeRendererConfig _config = GaugeRendererConfig.Default;
        private new bool _disposed;
        private RenderQuality _quality = RenderQuality.Medium;
        private new bool _useAntiAlias = true;
        private new SKSamplingOptions _samplingOptions = new(SKFilterMode.Linear, SKMipmapMode.Linear);
        private new bool _useAdvancedEffects = true;
        private int _peakHoldCounter = 0;

        private readonly ObjectPool<SKPaint> _paintPool = new(() => new SKPaint(), paint => paint.Reset(), 5);
        private readonly ObjectPool<SKPath> _pathPool = new(() => new SKPath(), path => path.Reset(), 2);

        private readonly SemaphoreSlim _renderSemaphore = new(1, 1);
        private readonly object _stateLock = new();

        private static readonly (float Value, string Label)[] MajorMarks = new[]
        {
            (-30f, "-30"), (-20f, "-20"), (-10f, "-10"),
            (-7f, "-7"), (-5f, "-5"), (-3f, "-3"),
            (0f, "0"), (3f, "+3"), (5f, "+5")
        };
        private static readonly List<float> MinorMarkValues = new();

        private const int PeakHoldDuration = 15;
        #endregion

        #region Initialization and Configuration
        /// <summary>
        /// Initializes the renderer and prepares resources for rendering.
        /// </summary>
        public override void Initialize()
        {
            SmartLogger.Safe(() =>
            {
                lock (_stateLock)
                {
                    _state.CurrentNeedlePosition = 0f;
                    _state.TargetNeedlePosition = 0f;
                    _state.PreviousValue = GaugeConstants.Db.Min;
                    _state.PeakActive = false;
                }
                _config = GaugeRendererConfig.Default;
                ApplyQualitySettings();
                SmartLogger.Log(LogLevel.Debug, GaugeConstants.LOG_PREFIX, "Initialized");
            }, new SmartLogger.ErrorHandlingOptions
            {
                Source = $"{GaugeConstants.LOG_PREFIX}.Initialize",
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
                _config = _config.WithOverlayMode(isOverlayActive);
                if (_quality != quality)
                {
                    _quality = quality;
                    ApplyQualitySettings();
                }
            }, new SmartLogger.ErrorHandlingOptions
            {
                Source = $"{GaugeConstants.LOG_PREFIX}.Configure",
                ErrorMessage = "Failed to configure renderer"
            });
        }

        /// <summary>
        /// Applies quality settings to adjust rendering parameters.
        /// </summary>
        protected override void ApplyQualitySettings()
        {
            SmartLogger.Safe(() =>
            {
                switch (_quality)
                {
                    case RenderQuality.Low:
                        _useAntiAlias = false;
                        _samplingOptions = new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None);
                        _useAdvancedEffects = false;
                        break;
                    case RenderQuality.Medium:
                        _useAntiAlias = true;
                        _samplingOptions = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);
                        _useAdvancedEffects = true;
                        break;
                    case RenderQuality.High:
                        _useAntiAlias = true;
                        _samplingOptions = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);
                        _useAdvancedEffects = true;
                        break;
                }
            }, new SmartLogger.ErrorHandlingOptions
            {
                Source = $"{GaugeConstants.LOG_PREFIX}.ApplyQualitySettings",
                ErrorMessage = "Failed to apply quality settings"
            });
        }

        static GaugeRenderer()
        {
            InitializeMinorMarks();
        }

        private static void InitializeMinorMarks()
        {
            var majorValues = MajorMarks.Select(m => m.Value).OrderBy(v => v).ToList();
            for (int i = 0; i < majorValues.Count - 1; i++)
            {
                float start = majorValues[i];
                float end = majorValues[i + 1];
                float step = (end - start) / GaugeConstants.MinorMarks.Divisor;
                for (float value = start + step; value < end; value += step)
                    MinorMarkValues.Add(value);
            }
        }
        #endregion

        #region Rendering
        /// <summary>
        /// Renders the gauge visualization on the canvas using spectrum data.
        /// </summary>
        /// <param name="canvas">The SkiaSharp canvas to render on.</param>
        /// <param name="spectrum">The spectrum data array.</param>
        /// <param name="info">Image information for rendering context.</param>
        /// <param name="barWidth">Width of individual bars (unused in this renderer).</param>
        /// <param name="barSpacing">Spacing between bars (unused in this renderer).</param>
        /// <param name="barCount">Number of bars (unused in this renderer).</param>
        /// <param name="paint">Paint object for styling.</param>
        /// <param name="drawPerformanceInfo">Action to draw performance information.</param>
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
            if (!ValidateRenderParameters(canvas, spectrum, paint))
            {
                drawPerformanceInfo?.Invoke(canvas!, info);
                return;
            }

            if (canvas!.QuickReject(new SKRect(0, 0, info.Width, info.Height)))
            {
                drawPerformanceInfo?.Invoke(canvas, info);
                return;
            }

            SmartLogger.Safe(() =>
            {
                bool semaphoreAcquired = false;
                try
                {
                    semaphoreAcquired = _renderSemaphore.Wait(0);
                    if (semaphoreAcquired)
                    {
                        UpdateGaugeState(spectrum!);
                    }

                    RenderGaugeComponents(canvas!, info, paint!, drawPerformanceInfo);
                }
                finally
                {
                    if (semaphoreAcquired)
                        _renderSemaphore.Release();
                }
            }, new SmartLogger.ErrorHandlingOptions
            {
                Source = $"{GaugeConstants.LOG_PREFIX}.Render",
                ErrorMessage = "Error during rendering"
            });
        }

        /// <summary>
        /// Validates rendering parameters before processing.
        /// </summary>
        private bool ValidateRenderParameters(SKCanvas? canvas, float[]? spectrum, SKPaint? paint)
        {
            if (canvas == null)
            {
                SmartLogger.Log(LogLevel.Error, GaugeConstants.LOG_PREFIX, "Canvas is null");
                return false;
            }
            if (spectrum == null || spectrum.Length == 0)
            {
                SmartLogger.Log(LogLevel.Warning, GaugeConstants.LOG_PREFIX, "Spectrum is null or empty");
                return false;
            }
            if (paint == null)
            {
                SmartLogger.Log(LogLevel.Error, GaugeConstants.LOG_PREFIX, "Paint is null");
                return false;
            }
            if (_disposed)
            {
                SmartLogger.Log(LogLevel.Error, GaugeConstants.LOG_PREFIX, "Renderer is disposed");
                return false;
            }
            return true;
        }
        #endregion

        #region Rendering Implementation
        /// <summary>
        /// Renders all gauge components.
        /// </summary>
        private void RenderGaugeComponents(SKCanvas canvas, SKImageInfo info, SKPaint basePaint, Action<SKCanvas, SKImageInfo>? drawPerformanceInfo)
        {
            var gaugeRect = CalculateGaugeRect(info);
            bool isOverlayActive = _config.SmoothingFactorIncrease == 0.1f;

            DrawGaugeBackground(canvas, gaugeRect);
            DrawScale(canvas, gaugeRect, isOverlayActive);
            DrawNeedle(canvas, gaugeRect, _state.CurrentNeedlePosition, isOverlayActive, basePaint);
            DrawPeakLamp(canvas, gaugeRect, isOverlayActive);
            drawPerformanceInfo?.Invoke(canvas, info);
        }

        /// <summary>
        /// Draws the gauge background with frames and VU text.
        /// </summary>
        [MethodImpl(AggressiveOptimization)]
        private void DrawGaugeBackground(SKCanvas canvas, SKRect rect)
        {
            using var outerFramePaint = _paintPool.Get();
            using var innerFramePaint = _paintPool.Get();
            using var backgroundPaint = _paintPool.Get();
            using var textPaint = _paintPool.Get();

            outerFramePaint.Style = SKPaintStyle.Fill;
            outerFramePaint.Color = new SKColor(80, 80, 80);
            outerFramePaint.IsAntialias = _useAntiAlias;
            canvas.DrawRoundRect(rect, GaugeConstants.Background.OuterFrameCornerRadius, GaugeConstants.Background.OuterFrameCornerRadius, outerFramePaint);

            var innerFrameRect = new SKRect(
                rect.Left + GaugeConstants.Background.InnerFramePadding,
                rect.Top + GaugeConstants.Background.InnerFramePadding,
                rect.Right - GaugeConstants.Background.InnerFramePadding,
                rect.Bottom - GaugeConstants.Background.InnerFramePadding);
            innerFramePaint.Style = SKPaintStyle.Fill;
            innerFramePaint.Color = new SKColor(105, 105, 105);
            innerFramePaint.IsAntialias = _useAntiAlias;
            canvas.DrawRoundRect(innerFrameRect, GaugeConstants.Background.InnerFrameCornerRadius, GaugeConstants.Background.InnerFrameCornerRadius, innerFramePaint);

            var backgroundRect = new SKRect(
                innerFrameRect.Left + GaugeConstants.Background.BackgroundPadding,
                innerFrameRect.Top + GaugeConstants.Background.BackgroundPadding,
                innerFrameRect.Right - GaugeConstants.Background.BackgroundPadding,
                innerFrameRect.Bottom - GaugeConstants.Background.BackgroundPadding);
            backgroundPaint.Style = SKPaintStyle.Fill;
            backgroundPaint.IsAntialias = _useAntiAlias;
            backgroundPaint.Shader = SKShader.CreateLinearGradient(
                new SKPoint(backgroundRect.Left, backgroundRect.Top),
                new SKPoint(backgroundRect.Left, backgroundRect.Bottom),
                new[] { new SKColor(250, 250, 240), new SKColor(230, 230, 215) },
                null,
                SKShaderTileMode.Clamp);
            canvas.DrawRoundRect(backgroundRect, GaugeConstants.Background.BackgroundCornerRadius, GaugeConstants.Background.BackgroundCornerRadius, backgroundPaint);

            textPaint.Color = SKColors.Black;
            textPaint.IsAntialias = _useAntiAlias;
            using var font = new SKFont(SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold), rect.Height * GaugeConstants.Background.VuTextSizeFactor);
            canvas.DrawText("VU", backgroundRect.MidX, backgroundRect.Bottom - (backgroundRect.Height * GaugeConstants.Background.VuTextBottomOffsetFactor), SKTextAlign.Center, font, textPaint);
        }

        /// <summary>
        /// Draws the gauge scale with major and minor marks.
        /// </summary>
        [MethodImpl(AggressiveOptimization)]
        private void DrawScale(SKCanvas canvas, SKRect rect, bool isOverlayActive)
        {
            float centerX = rect.MidX;
            float centerY = rect.MidY + rect.Height * GaugeConstants.Scale.CenterYOffsetFactor;
            float radiusX = rect.Width * (isOverlayActive ? GaugeConstants.Scale.RadiusXFactorOverlay : GaugeConstants.Scale.RadiusXFactor);
            float radiusY = rect.Height * (isOverlayActive ? GaugeConstants.Scale.RadiusYFactorOverlay : GaugeConstants.Scale.RadiusYFactor);

            using var tickPaint = _paintPool.Get();
            using var textPaint = _paintPool.Get();

            tickPaint.IsAntialias = _useAntiAlias;
            tickPaint.StrokeWidth = GaugeConstants.Scale.TickStrokeWidth;

            textPaint.Color = SKColors.Black;
            textPaint.IsAntialias = _useAntiAlias;
            if (_useAdvancedEffects)
                textPaint.ImageFilter = SKImageFilter.CreateDropShadow(0.5f, 0.5f, 0.5f, 0.5f, new SKColor(255, 255, 255, 180));

            foreach (var (value, label) in MajorMarks)
                DrawMark(canvas, centerX, centerY, radiusX, radiusY, value, label, isOverlayActive, tickPaint, textPaint);

            foreach (float value in MinorMarkValues)
                DrawMark(canvas, centerX, centerY, radiusX, radiusY, value, null, isOverlayActive, tickPaint, null);
        }

        /// <summary>
        /// Draws a single mark on the gauge scale.
        /// </summary>
        private void DrawMark(SKCanvas canvas, float centerX, float centerY, float radiusX, float radiusY,
                              float value, string? label, bool isOverlayActive, SKPaint tickPaint, SKPaint? textPaint)
        {
            float normalizedValue = (value - GaugeConstants.Db.Min) / (GaugeConstants.Db.Max - GaugeConstants.Db.Min);
            float angle = GaugeConstants.Angle.Start + normalizedValue * GaugeConstants.Angle.TotalRange;
            float radian = angle * (float)(PI / 180.0);

            float tickLength = radiusY * (label != null
                ? (isOverlayActive
                    ? (value == 0 ? GaugeConstants.Scale.TickLengthZeroFactorOverlay : GaugeConstants.Scale.TickLengthFactorOverlay)
                    : (value == 0 ? GaugeConstants.Scale.TickLengthZeroFactor : GaugeConstants.Scale.TickLengthFactor))
                : (isOverlayActive ? GaugeConstants.Scale.TickLengthMinorFactorOverlay : GaugeConstants.Scale.TickLengthMinorFactor));

            float x1 = centerX + (radiusX - tickLength) * (float)Cos(radian);
            float y1 = centerY + (radiusY - tickLength) * (float)Sin(radian);
            float x2 = centerX + radiusX * (float)Cos(radian);
            float y2 = centerY + radiusY * (float)Sin(radian);

            if (label != null)
            {
                using var tickGradient = SKShader.CreateLinearGradient(
                    new SKPoint(x1, y1),
                    new SKPoint(x2, y2),
                    value >= 0 ? new[] { new SKColor(200, 0, 0), SKColors.Red } : new[] { new SKColor(60, 60, 60), new SKColor(100, 100, 100) },
                    null,
                    SKShaderTileMode.Clamp);
                tickPaint.Shader = tickGradient;
                canvas.DrawLine(x1, y1, x2, y2, tickPaint);
            }
            else
            {
                tickPaint.Color = value >= 0 ? new SKColor(220, 0, 0) : new SKColor(80, 80, 80);
                canvas.DrawLine(x1, y1, x2, y2, tickPaint);
            }

            if (!string.IsNullOrEmpty(label) && textPaint != null)
            {
                float textOffset = radiusY * (isOverlayActive ? GaugeConstants.Scale.TextOffsetFactorOverlay : GaugeConstants.Scale.TextOffsetFactor);
                float textSize = radiusY * (isOverlayActive ? GaugeConstants.Scale.TextSizeFactorOverlay : GaugeConstants.Scale.TextSizeFactor);
                using var font = new SKFont(SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold), textSize);
                if (value == 0)
                {
                    font.Size *= GaugeConstants.Scale.TextSizeMultiplierZero;
                    font.Embolden = true;
                }
                else
                {
                    font.Embolden = false;
                }

                textPaint.Color = value >= 0 ? new SKColor(200, 0, 0) : SKColors.Black;
                float textX = x2 + textOffset * (float)Cos(radian);
                float textY = y2 + textOffset * (float)Sin(radian) + font.Metrics.Descent;
                canvas.DrawText(label, textX, textY, SKTextAlign.Center, font, textPaint);
            }
        }

        /// <summary>
        /// Draws the gauge needle with shadow and center circle.
        /// </summary>
        [MethodImpl(AggressiveOptimization)]
        private void DrawNeedle(SKCanvas canvas, SKRect rect, float needlePosition, bool isOverlayActive, SKPaint basePaint)
        {
            float centerX = rect.MidX;
            float centerY = rect.MidY + rect.Height * _config.NeedleCenterYOffsetMultiplier;
            float radiusX = rect.Width * (isOverlayActive ? GaugeConstants.Scale.RadiusXFactorOverlay : GaugeConstants.Scale.RadiusXFactor);
            float radiusY = rect.Height * (isOverlayActive ? GaugeConstants.Scale.RadiusYFactorOverlay : GaugeConstants.Scale.RadiusYFactor);

            float angle = GaugeConstants.Angle.Start + needlePosition * GaugeConstants.Angle.TotalRange;
            var (ellipseX, ellipseY) = CalculatePointOnEllipse(centerX, centerY, radiusX, radiusY, angle);
            var (unitX, unitY, _) = NormalizeVector(ellipseX - centerX, ellipseY - centerY);

            float needleLength = Min(radiusX, radiusY) * _config.NeedleLengthMultiplier;

            using var needlePath = _pathPool.Get();
            float baseWidth = GaugeConstants.Needle.StrokeWidth * GaugeConstants.Needle.BaseWidthMultiplier;
            float perpX = -unitY;
            float perpY = unitX;

            float tipX = centerX + unitX * needleLength;
            float tipY = centerY + unitY * needleLength;
            float baseLeftX = centerX + perpX * baseWidth;
            float baseLeftY = centerY + perpY * baseWidth;
            float baseRightX = centerX - perpX * baseWidth;
            float baseRightY = centerY - perpY * baseWidth;

            needlePath.MoveTo(tipX, tipY);
            needlePath.LineTo(baseLeftX, baseLeftY);
            needlePath.LineTo(baseRightX, baseRightY);
            needlePath.Close();

            using var needlePaint = _paintPool.Get();
            needlePaint.Style = SKPaintStyle.Fill;
            needlePaint.IsAntialias = _useAntiAlias;
            needlePaint.Shader = SKShader.CreateLinearGradient(
                new SKPoint(centerX, centerY),
                new SKPoint(tipX, tipY),
                new[] { new SKColor(40, 40, 40), needlePosition > 0.75f ? SKColors.Red : new SKColor(180, 0, 0) },
                null,
                SKShaderTileMode.Clamp);
            if (_useAdvancedEffects)
                needlePaint.ImageFilter = SKImageFilter.CreateDropShadow(2f, 2f, 1.5f, 1.5f, SKColors.Black.WithAlpha(100));
            canvas.DrawPath(needlePath, needlePaint);

            using var outlinePaint = _paintPool.Get();
            outlinePaint.Style = SKPaintStyle.Stroke;
            outlinePaint.StrokeWidth = 0.8f;
            outlinePaint.Color = SKColors.Black.WithAlpha(180);
            outlinePaint.IsAntialias = _useAntiAlias;
            canvas.DrawPath(needlePath, outlinePaint);

            float centerCircleRadius = rect.Width * (isOverlayActive ? GaugeConstants.Needle.CenterCircleRadiusOverlay : GaugeConstants.Needle.CenterCircleRadius);
            using var centerCirclePaint = _paintPool.Get();
            centerCirclePaint.Style = SKPaintStyle.Fill;
            centerCirclePaint.IsAntialias = _useAntiAlias;
            centerCirclePaint.Shader = SKShader.CreateRadialGradient(
                new SKPoint(centerX - centerCircleRadius * 0.3f, centerY - centerCircleRadius * 0.3f),
                centerCircleRadius * 2,
                new[] { SKColors.White, new SKColor(180, 180, 180), new SKColor(60, 60, 60) },
                new[] { 0.0f, 0.3f, 1.0f },
                SKShaderTileMode.Clamp);
            canvas.DrawCircle(centerX, centerY, centerCircleRadius, centerCirclePaint);

            using var highlightPaint = _paintPool.Get();
            highlightPaint.Color = SKColors.White.WithAlpha(150);
            highlightPaint.Style = SKPaintStyle.Fill;
            highlightPaint.IsAntialias = _useAntiAlias;
            canvas.DrawCircle(centerX - centerCircleRadius * 0.25f, centerY - centerCircleRadius * 0.25f, centerCircleRadius * 0.4f, highlightPaint);
        }

        /// <summary>
        /// Draws the peak lamp indicator.
        /// </summary>
        [MethodImpl(AggressiveOptimization)]
        private void DrawPeakLamp(SKCanvas canvas, SKRect rect, bool isOverlayActive)
        {
            float radiusX = rect.Width * (isOverlayActive ? GaugeConstants.Scale.RadiusXFactorOverlay : GaugeConstants.Scale.RadiusXFactor);
            float radiusY = rect.Height * (isOverlayActive ? GaugeConstants.Scale.RadiusYFactorOverlay : GaugeConstants.Scale.RadiusYFactor);
            float lampRadius = Min(radiusX, radiusY) * (isOverlayActive ? GaugeConstants.PeakLamp.RadiusFactorOverlay : GaugeConstants.PeakLamp.RadiusFactor);
            float lampX = rect.Right - rect.Width * (isOverlayActive ? GaugeConstants.PeakLamp.LampXOffsetFactorOverlay : GaugeConstants.PeakLamp.LampXOffsetFactor);
            float lampY = rect.Top + rect.Height * (isOverlayActive ? GaugeConstants.PeakLamp.LampYOffsetFactorOverlay : GaugeConstants.PeakLamp.LampYOffsetFactor);

            SKColor lampColor = _state.PeakActive ? SKColors.Red : new SKColor(80, 0, 0);
            SKColor glowColor = _state.PeakActive ? SKColors.Red.WithAlpha(80) : SKColors.Transparent;

            if (_state.PeakActive && _useAdvancedEffects)
            {
                using var glowPaint = _paintPool.Get();
                glowPaint.Color = glowColor;
                glowPaint.MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, lampRadius * GaugeConstants.PeakLamp.GlowRadiusMultiplier);
                glowPaint.IsAntialias = _useAntiAlias;
                canvas.DrawCircle(lampX, lampY, lampRadius * GaugeConstants.PeakLamp.GlowRadiusMultiplier, glowPaint);
            }

            using var innerPaint = _paintPool.Get();
            innerPaint.Style = SKPaintStyle.Fill;
            innerPaint.IsAntialias = _useAntiAlias;
            innerPaint.Shader = SKShader.CreateRadialGradient(
                new SKPoint(lampX - lampRadius * 0.2f, lampY - lampRadius * 0.2f),
                lampRadius * GaugeConstants.PeakLamp.InnerRadiusMultiplier,
                _state.PeakActive
                    ? new[] { SKColors.White, new SKColor(255, 180, 180), lampColor }
                    : new[] { new SKColor(220, 220, 220), new SKColor(180, 0, 0), new SKColor(80, 0, 0) },
                new[] { 0.0f, 0.3f, 1.0f },
                SKShaderTileMode.Clamp);
            canvas.DrawCircle(lampX, lampY, lampRadius * GaugeConstants.PeakLamp.InnerRadiusMultiplier, innerPaint);

            using var reflectionPaint = _paintPool.Get();
            reflectionPaint.Color = SKColors.White.WithAlpha(180);
            reflectionPaint.Style = SKPaintStyle.Fill;
            reflectionPaint.IsAntialias = _useAntiAlias;
            canvas.DrawCircle(lampX - lampRadius * 0.3f, lampY - lampRadius * 0.3f, lampRadius * 0.25f, reflectionPaint);

            using var rimPaint = _paintPool.Get();
            rimPaint.Color = new SKColor(40, 40, 40);
            rimPaint.Style = SKPaintStyle.Stroke;
            rimPaint.StrokeWidth = GaugeConstants.PeakLamp.RimStrokeWidth * 1.2f;
            rimPaint.IsAntialias = _useAntiAlias;
            canvas.DrawCircle(lampX, lampY, lampRadius, rimPaint);

            using var peakTextPaint = _paintPool.Get();
            peakTextPaint.Color = _state.PeakActive ? SKColors.Red : new SKColor(180, 0, 0);
            peakTextPaint.IsAntialias = _useAntiAlias;
            if (_useAdvancedEffects)
                peakTextPaint.ImageFilter = SKImageFilter.CreateDropShadow(1, 1, 1, 1, SKColors.Black.WithAlpha(150));
            using var font = new SKFont(SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold),
                                        lampRadius * (isOverlayActive ? GaugeConstants.PeakLamp.TextSizeFactorOverlay : GaugeConstants.PeakLamp.TextSizeFactor) * 1.2f);
            float textYOffset = lampRadius * GaugeConstants.PeakLamp.TextYOffsetFactor + font.Metrics.Descent;
            canvas.DrawText("PEAK", lampX, lampY + textYOffset, SKTextAlign.Center, font, peakTextPaint);
        }
        #endregion

        #region Spectrum Processing
        /// <summary>
        /// Updates the gauge state based on spectrum data.
        /// </summary>
        [MethodImpl(AggressiveOptimization)]
        private void UpdateGaugeState(float[] spectrum)
        {
            lock (_stateLock)
            {
                float dbValue = CalculateLoudness(spectrum);
                float smoothedDb = SmoothValue(dbValue);
                _state.TargetNeedlePosition = CalculateNeedlePosition(smoothedDb);
                UpdateNeedlePosition();

                float thresholdPosition = CalculateNeedlePosition(GaugeConstants.Db.PeakThreshold);
                if (_state.CurrentNeedlePosition >= thresholdPosition)
                {
                    _state.PeakActive = true;
                    _peakHoldCounter = PeakHoldDuration;
                }
                else if (_peakHoldCounter > 0)
                {
                    _peakHoldCounter--;
                }
                else
                {
                    _state.PeakActive = false;
                }
            }
        }

        /// <summary>
        /// Calculates the loudness in dB from spectrum data.
        /// </summary>
        [MethodImpl(AggressiveOptimization)]
        private float CalculateLoudness(float[] spectrum)
        {
            if (spectrum.Length == 0) return GaugeConstants.Db.Min;

            float sumOfSquares = 0f;
            if (IsHardwareAccelerated && spectrum.Length >= Vector<float>.Count)
            {
                int vectorSize = Vector<float>.Count;
                int vectorizedLength = spectrum.Length - (spectrum.Length % vectorSize);

                for (int i = 0; i < vectorizedLength; i += vectorSize)
                {
                    Vector<float> values = new(spectrum, i);
                    sumOfSquares += Vector.Dot(values, values);
                }

                for (int i = vectorizedLength; i < spectrum.Length; i++)
                    sumOfSquares += spectrum[i] * spectrum[i];
            }
            else
            {
                for (int i = 0; i < spectrum.Length; i++)
                    sumOfSquares += spectrum[i] * spectrum[i];
            }

            float rms = (float)Sqrt(sumOfSquares / spectrum.Length);
            float db = 20f * (float)Log10(Max(rms, GaugeConstants.Rendering.MinDbClamp));
            return Clamp(db, GaugeConstants.Db.Min, GaugeConstants.Db.Max);
        }

        /// <summary>
        /// Smooths the dB value based on direction of change.
        /// </summary>
        [MethodImpl(AggressiveInlining)]
        private float SmoothValue(float newValue)
        {
            float smoothingFactor = newValue > _state.PreviousValue ? _config.SmoothingFactorIncrease : _config.SmoothingFactorDecrease;
            return _state.PreviousValue += smoothingFactor * (newValue - _state.PreviousValue);
        }

        /// <summary>
        /// Calculates the needle position from a dB value.
        /// </summary>
        [MethodImpl(AggressiveInlining)]
        private float CalculateNeedlePosition(float db)
        {
            float normalizedPosition = (Clamp(db, GaugeConstants.Db.Min, GaugeConstants.Db.Max) - GaugeConstants.Db.Min) /
                                       (GaugeConstants.Db.Max - GaugeConstants.Db.Min);
            return Clamp(normalizedPosition, GaugeConstants.Rendering.Margin, 1f - GaugeConstants.Rendering.Margin);
        }

        /// <summary>
        /// Updates the needle position with easing.
        /// </summary>
        [MethodImpl(AggressiveInlining)]
        private void UpdateNeedlePosition()
        {
            float difference = _state.TargetNeedlePosition - _state.CurrentNeedlePosition;
            float speed = difference * (difference > 0 ? _config.RiseSpeed : _config.FallSpeed);
            float easedSpeed = speed * (1 - _config.Damping) * (1 - Abs(difference));
            _state.CurrentNeedlePosition += easedSpeed;
            _state.CurrentNeedlePosition = Clamp(_state.CurrentNeedlePosition, 0f, 1f);
        }
        #endregion

        #region Helper Methods
        /// <summary>
        /// Calculates a point on an ellipse given an angle.
        /// </summary>
        [MethodImpl(AggressiveInlining)]
        private static (float x, float y) CalculatePointOnEllipse(float centerX, float centerY, float radiusX, float radiusY, float angleDegrees)
        {
            float radian = angleDegrees * (float)(PI / 180.0);
            return (centerX + radiusX * (float)Cos(radian), centerY + radiusY * (float)Sin(radian));
        }

        /// <summary>
        /// Normalizes a vector and returns its unit components and length.
        /// </summary>
        [MethodImpl(AggressiveInlining)]
        private static (float unitX, float unitY, float length) NormalizeVector(float dx, float dy)
        {
            float length = (float)Sqrt(dx * dx + dy * dy);
            return length > 0 ? (dx / length, dy / length, length) : (0f, 0f, 0f);
        }

        /// <summary>
        /// Calculates the gauge rectangle based on image info.
        /// </summary>
        [MethodImpl(AggressiveInlining)]
        public static SKRect CalculateGaugeRect(SKImageInfo info)
        {
            float aspectRatio = GaugeConstants.Rendering.AspectRatio;
            float width = info.Width / (float)info.Height > aspectRatio
                ? (info.Height * GaugeConstants.Rendering.GaugeRectPadding) * aspectRatio
                : info.Width * GaugeConstants.Rendering.GaugeRectPadding;
            float height = width / aspectRatio;
            float left = (info.Width - width) / 2;
            float top = (info.Height - height) / 2;
            return new SKRect(left, top, left + width, top + height);
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
                    _renderSemaphore?.Dispose();
                    _paintPool?.Dispose();
                    _pathPool?.Dispose();
                }, new SmartLogger.ErrorHandlingOptions
                {
                    Source = $"{GaugeConstants.LOG_PREFIX}.Dispose",
                    ErrorMessage = "Error during disposal"
                });

                _disposed = true;
                SmartLogger.Log(LogLevel.Debug, GaugeConstants.LOG_PREFIX, "Disposed");
            }
        }
        #endregion
    }

    /// <summary>
    /// Constants used by the GaugeRenderer for defining gauge properties and behavior.
    /// </summary>
    public static class GaugeConstants
    {
        // Constants related to decibel (dB) values
        public static class Db
        {
            public const float Max = 5f;     // Maximum dB value for the gauge
            public const float Min = -30f;   // Minimum dB value for the gauge
            public const float PeakThreshold = 5f;     // Threshold for peak indicator activation
        }

        // Constants related to angle calculations for the gauge
        public static class Angle
        {
            public const float Start = -150f;     // Starting angle for the gauge scale
            public const float End = -30f;      // Ending angle for the gauge scale
            public const float TotalRange = End - Start; // Total angular range of the gauge
        }

        // Constants for needle properties
        public static class Needle
        {
            public const float DefaultLengthMultiplier = 1.55f; // Default multiplier for needle length
            public const float DefaultCenterYOffsetMultiplier = 0.4f;  // Default multiplier for needle center Y offset
            public const float StrokeWidth = 2.25f; // Stroke width for the needle
            public const float CenterCircleRadiusOverlay = 0.015f; // Radius of center circle in overlay mode
            public const float CenterCircleRadius = 0.02f; // Radius of center circle in normal mode
            public const float BaseWidthMultiplier = 2.5f;  // Multiplier for needle base width
        }

        // Constants for background and frame properties
        public static class Background
        {
            public const float OuterFrameCornerRadius = 8f;   // Corner radius for outer frame
            public const float InnerFramePadding = 4f;   // Padding for inner frame
            public const float InnerFrameCornerRadius = 6f;   // Corner radius for inner frame
            public const float BackgroundPadding = 4f;   // Padding for background
            public const float BackgroundCornerRadius = 4f;   // Corner radius for background
            public const float VuTextSizeFactor = 0.2f; // Factor for VU text size
            public const float VuTextBottomOffsetFactor = 0.2f; // Factor for VU text bottom offset
        }

        // Constants for scale properties
        public static class Scale
        {
            public const float CenterYOffsetFactor = 0.15f; // Factor for scale center Y offset
            public const float RadiusXFactorOverlay = 0.4f;  // Factor for X radius in overlay mode
            public const float RadiusXFactor = 0.45f; // Factor for X radius in normal mode
            public const float RadiusYFactorOverlay = 0.45f; // Factor for Y radius in overlay mode
            public const float RadiusYFactor = 0.5f;  // Factor for Y radius in normal mode
            public const float TextOffsetFactorOverlay = 0.1f;  // Factor for text offset in overlay mode
            public const float TextOffsetFactor = 0.12f; // Factor for text offset in normal mode
            public const float TextSizeFactorOverlay = 0.08f; // Factor for text size in overlay mode
            public const float TextSizeFactor = 0.1f;  // Factor for text size in normal mode
            public const float TickLengthZeroFactorOverlay = 0.12f; // Factor for zero tick length in overlay mode
            public const float TickLengthZeroFactor = 0.15f; // Factor for zero tick length in normal mode
            public const float TickLengthFactorOverlay = 0.07f; // Factor for major tick length in overlay mode
            public const float TickLengthFactor = 0.08f; // Factor for major tick length in normal mode
            public const float TickLengthMinorFactorOverlay = 0.05f; // Factor for minor tick length in overlay mode
            public const float TickLengthMinorFactor = 0.06f; // Factor for minor tick length in normal mode
            public const float TickStrokeWidth = 1.8f;  // Stroke width for scale ticks
            public const float TextSizeMultiplierZero = 1.15f; // Multiplier for "0" text size
        }

        // Constants for peak lamp properties
        public static class PeakLamp
        {
            public const float RadiusFactorOverlay = 0.04f; // Factor for lamp radius in overlay mode
            public const float RadiusFactor = 0.05f; // Factor for lamp radius in normal mode
            public const float LampXOffsetFactorOverlay = 0.12f; // Factor for lamp X offset in overlay mode
            public const float LampXOffsetFactor = 0.1f;  // Factor for lamp X offset in normal mode
            public const float LampYOffsetFactorOverlay = 0.18f; // Factor for lamp Y offset in overlay mode
            public const float LampYOffsetFactor = 0.2f;  // Factor for lamp Y offset in normal mode
            public const float TextSizeFactorOverlay = 1.2f;  // Factor for text size in overlay mode
            public const float TextSizeFactor = 1.5f;  // Factor for text size in normal mode
            public const float TextYOffsetFactor = 2.5f;  // Factor for text Y offset
            public const float RimStrokeWidth = 1f;    // Stroke width for lamp rim
            public const float GlowRadiusMultiplier = 1.5f;  // Multiplier for glow radius when active
            public const float InnerRadiusMultiplier = 0.8f;  // Multiplier for inner lamp radius
        }

        // Constants for rendering properties
        public static class Rendering
        {
            public const float AspectRatio = 2.0f;   // Aspect ratio for the gauge
            public const float GaugeRectPadding = 0.8f;   // Padding factor for gauge rectangle
            public const float MinDbClamp = 1e-10f; // Minimum value for dB calculation to avoid log(0)
            public const float Margin = 0.05f;  // Margin for needle position clamping
        }

        // Constants for minor marks
        public static class MinorMarks
        {
            public const int Divisor = 3;                 // Divisor for minor marks between major marks
        }

        // Logging prefix
        public const string LOG_PREFIX = "GaugeRenderer";
    }

    /// <summary>
    /// Configuration settings for the GaugeRenderer, controlling smoothing and needle behavior.
    /// </summary>
    public readonly record struct GaugeRendererConfig(
        float SmoothingFactorIncrease,
        float SmoothingFactorDecrease,
        float RiseSpeed,
        float FallSpeed,
        float Damping,
        float NeedleLengthMultiplier,
        float NeedleCenterYOffsetMultiplier)
    {
        public static GaugeRendererConfig Default => new(
            SmoothingFactorIncrease: 0.2f,
            SmoothingFactorDecrease: 0.05f,
            RiseSpeed: 0.15f,
            FallSpeed: 0.03f,
            Damping: 0.7f,
            NeedleLengthMultiplier: GaugeConstants.Needle.DefaultLengthMultiplier,
            NeedleCenterYOffsetMultiplier: GaugeConstants.Needle.DefaultCenterYOffsetMultiplier
        );

        public GaugeRendererConfig WithOverlayMode(bool isOverlayActive) => this with
        {
            SmoothingFactorIncrease = isOverlayActive ? 0.1f : 0.2f,
            SmoothingFactorDecrease = isOverlayActive ? 0.02f : 0.05f,
            NeedleLengthMultiplier = isOverlayActive ? 1.6f : GaugeConstants.Needle.DefaultLengthMultiplier,
            NeedleCenterYOffsetMultiplier = isOverlayActive ? 0.35f : GaugeConstants.Needle.DefaultCenterYOffsetMultiplier
        };
    }

    /// <summary>
    /// Internal state of the gauge renderer.
    /// </summary>
    internal sealed class GaugeState
    {
        public float CurrentNeedlePosition { get; set; }
        public float TargetNeedlePosition { get; set; }
        public float PreviousValue { get; set; } = GaugeConstants.Db.Min;
        public bool PeakActive { get; set; }
    }
}