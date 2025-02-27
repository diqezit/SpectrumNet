namespace SpectrumNet
{
    public static class GaugeConstants
    {
        public static class Db
        {
            public const float Max = 5f,
                              Min = -30f,
                              PeakThreshold = 5f;
        }
        public static class Angle
        {
            public const float Start = -150f,
                              End = -30f,
                              TotalRange = End - Start;
        }
        public static class Needle
        {
            public const float DefaultLengthMultiplier = 1.55f,
                              DefaultCenterYOffsetMultiplier = 0.4f,
                              StrokeWidth = 2.25f,
                              CenterCircleRadiusOverlay = 0.015f,
                              CenterCircleRadius = 0.02f;
        }
        public static class Background
        {
            public const float OuterFrameCornerRadius = 8f,
                              InnerFramePadding = 4f,
                              InnerFrameCornerRadius = 6f,
                              BackgroundPadding = 4f,
                              BackgroundCornerRadius = 4f,
                              VuTextSizeFactor = 0.2f,
                              VuTextBottomOffsetFactor = 0.2f;
        }
        public static class Scale
        {
            public const float CenterYOffsetFactor = 0.15f,
                              RadiusXFactorOverlay = 0.4f,
                              RadiusXFactor = 0.45f,
                              RadiusYFactorOverlay = 0.45f,
                              RadiusYFactor = 0.5f,
                              TextOffsetFactorOverlay = 0.1f,
                              TextOffsetFactor = 0.12f,
                              TextSizeFactorOverlay = 0.08f,
                              TextSizeFactor = 0.1f,
                              TickLengthZeroFactorOverlay = 0.12f,
                              TickLengthZeroFactor = 0.15f,
                              TickLengthFactorOverlay = 0.07f,
                              TickLengthFactor = 0.08f,
                              TickLengthMinorFactorOverlay = 0.05f,
                              TickLengthMinorFactor = 0.06f;
        }
        public static class PeakLamp
        {
            public const float RadiusFactorOverlay = 0.04f,
                              RadiusFactor = 0.05f,
                              LampXOffsetFactorOverlay = 0.12f,
                              LampXOffsetFactor = 0.1f,
                              LampYOffsetFactorOverlay = 0.18f,
                              LampYOffsetFactor = 0.2f,
                              TextSizeFactorOverlay = 1.2f,
                              TextSizeFactor = 1.5f,
                              TextYOffsetFactor = 2.5f,
                              RimStrokeWidth = 1f;
        }
        public static class Rendering
        {
            public const float AspectRatio = 2.0f,
                              GaugeRectPadding = 0.8f,
                              MinDbClamp = 1e-10f,
                              Margin = 0.05f;
        }
        public static class MinorMarks
        {
            public const int Divisor = 3;
        }
    }

    public readonly record struct GaugeRendererConfig
    {
        public float SmoothingFactorIncrease { get; init; }
        public float SmoothingFactorDecrease { get; init; }
        public float RiseSpeed { get; init; }
        public float FallSpeed { get; init; }
        public float Damping { get; init; }
        public float NeedleLengthMultiplier { get; init; }
        public float NeedleCenterYOffsetMultiplier { get; init; }

        public static GaugeRendererConfig Default => new()
        {
            SmoothingFactorIncrease = 0.2f,
            SmoothingFactorDecrease = 0.05f,
            RiseSpeed = 0.15f,
            FallSpeed = 0.03f,
            Damping = 0.7f,
            NeedleLengthMultiplier = GaugeConstants.Needle.DefaultLengthMultiplier,
            NeedleCenterYOffsetMultiplier = GaugeConstants.Needle.DefaultCenterYOffsetMultiplier
        };

        public GaugeRendererConfig WithOverlayMode(bool isOverlayActive) => this with
        {
            SmoothingFactorIncrease = isOverlayActive ? 0.1f : 0.2f,
            SmoothingFactorDecrease = isOverlayActive ? 0.02f : 0.05f,
            NeedleLengthMultiplier = isOverlayActive ? 1.6f : GaugeConstants.Needle.DefaultLengthMultiplier,
            NeedleCenterYOffsetMultiplier = isOverlayActive ? 0.35f : GaugeConstants.Needle.DefaultCenterYOffsetMultiplier
        };
    }

    public sealed class GaugeRenderer : ISpectrumRenderer, IDisposable
    {
        private sealed class GaugeState
        {
            public float CurrentNeedlePosition { get; set; }
            public float TargetNeedlePosition { get; set; }
            public float PreviousValue { get; set; } = GaugeConstants.Db.Min;
            public bool PeakActive { get; set; }
        }

        private readonly GaugeState _state = new();
        private GaugeRendererConfig _config = GaugeRendererConfig.Default;
        private bool _disposed;

        private int _peakHoldCounter = 0;
        private const int _peakHoldDuration = 15; // Hold the peak indicator for 15 frames

        private static readonly Lazy<GaugeRenderer> Instance = new(() => new GaugeRenderer(),
                                                                  LazyThreadSafetyMode.ExecutionAndPublication);
        private static readonly (float Value, string Label)[] MajorMarks = new[]
        {
            (-30f, "-30"), (-20f, "-20"), (-10f, "-10"),
            (-7f, "-7"), (-5f, "-5"), (-3f, "-3"),
            (0f, "0"), (3f, "+3"), (5f, "+5")
        };
        private static readonly List<float> MinorMarkValues = new();

        private static readonly SKPaint _sharedTickPaint = CreatePaint(SKPaintStyle.Stroke, SKColors.Black, 3.5f);
        private static readonly SKPaint _sharedRimPaint = CreatePaint(SKPaintStyle.Stroke, SKColors.Black, 1f);
        private static readonly SKPaint _sharedNeedlePathPaint = CreatePaint(SKPaintStyle.Stroke,
                                                                            SKColors.Black,
                                                                            GaugeConstants.Needle.StrokeWidth);
        private static readonly SKPaint _sharedPivotPaint = CreatePaint(SKPaintStyle.Fill, SKColors.DarkGray, 0);
        private static readonly SKPaint _sharedTextPaint = CreateTextPaint(30,
                                                                          SKTextAlign.Center,
                                                                          "Arial",
                                                                          SKFontStyle.Bold,
                                                                          SKColors.Black);
        private static readonly SKPaint _sharedLampPaint = CreatePaint(SKPaintStyle.Fill, SKColors.DarkRed, 0);
        private static readonly SKPaint _sharedPeakTextPaint = CreateTextPaint(30,
                                                                              SKTextAlign.Center,
                                                                              "Arial",
                                                                              SKFontStyle.Bold,
                                                                              SKColors.Black);

        static GaugeRenderer()
        {
            InitializeMinorMarks();
        }

        private GaugeRenderer() { }

        public static GaugeRenderer GetInstance() => Instance.Value;

        public void Initialize()
        {
            _state.CurrentNeedlePosition = 0f;
            _state.TargetNeedlePosition = 0f;
            _state.PreviousValue = GaugeConstants.Db.Min;
            _state.PeakActive = false;
            _config = GaugeRendererConfig.Default;
        }

        public void Configure(bool isOverlayActive) => _config = _config.WithOverlayMode(isOverlayActive);

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

        public void Render(SKCanvas? canvas,
                          float[]? spectrum,
                          SKImageInfo info,
                          float barWidth,
                          float barSpacing,
                          int barCount,
                          SKPaint? paint,
                          Action<SKCanvas, SKImageInfo> drawPerformanceInfo)
        {
            if (canvas == null || spectrum == null || spectrum.Length == 0 || paint == null)
                return;

            using var _ = new SKAutoCanvasRestore(canvas);
            try
            {
                UpdateGaugeState(spectrum);
                RenderGaugeComponents(canvas, info, drawPerformanceInfo);
            }
            catch (Exception ex)
            {
                Log.Error($"Rendering error: {ex.Message}");
            }
        }

        private void RenderGaugeComponents(SKCanvas canvas,
                                          SKImageInfo info,
                                          Action<SKCanvas, SKImageInfo> drawPerformanceInfo)
        {
            var gaugeRect = CalculateGaugeRect(info);
            bool isOverlayActive = _config.SmoothingFactorIncrease == 0.1f;

            DrawGaugeBackground(canvas, gaugeRect);
            DrawScale(canvas, gaugeRect, isOverlayActive);
            DrawNeedle(canvas, gaugeRect, _state.CurrentNeedlePosition, isOverlayActive);
            DrawPeakLamp(canvas, gaugeRect, isOverlayActive);
            drawPerformanceInfo(canvas, info);
        }

        private void DrawGaugeBackground(SKCanvas canvas, SKRect rect)
        {
            var outerFrameColor = new SKColor(80, 80, 80);
            var innerFrameColor = new SKColor(105, 105, 105);
            var backgroundStartColor = new SKColor(250, 250, 240);
            var backgroundEndColor = new SKColor(230, 230, 215);

            using var outerFramePaint = new SKPaint
            {
                Color = outerFrameColor,
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            canvas.DrawRoundRect(rect,
                                GaugeConstants.Background.OuterFrameCornerRadius,
                                GaugeConstants.Background.OuterFrameCornerRadius,
                                outerFramePaint);

            var innerFrameRect = new SKRect(rect.Left + GaugeConstants.Background.InnerFramePadding,
                                           rect.Top + GaugeConstants.Background.InnerFramePadding,
                                           rect.Right - GaugeConstants.Background.InnerFramePadding,
                                           rect.Bottom - GaugeConstants.Background.InnerFramePadding);
            using var innerFramePaint = new SKPaint
            {
                Color = innerFrameColor,
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            canvas.DrawRoundRect(innerFrameRect,
                                GaugeConstants.Background.InnerFrameCornerRadius,
                                GaugeConstants.Background.InnerFrameCornerRadius,
                                innerFramePaint);

            var backgroundRect = new SKRect(innerFrameRect.Left + GaugeConstants.Background.BackgroundPadding,
                                           innerFrameRect.Top + GaugeConstants.Background.BackgroundPadding,
                                           innerFrameRect.Right - GaugeConstants.Background.BackgroundPadding,
                                           innerFrameRect.Bottom - GaugeConstants.Background.BackgroundPadding);
            using var backgroundPaint = new SKPaint
            {
                Style = SKPaintStyle.Fill,
                IsAntialias = true,
                Shader = SKShader.CreateLinearGradient(
                    new SKPoint(backgroundRect.Left, backgroundRect.Top),
                    new SKPoint(backgroundRect.Left, backgroundRect.Bottom),
                    new[] { backgroundStartColor, backgroundEndColor },
                    null,
                    SKShaderTileMode.Clamp)
            };
            canvas.DrawRoundRect(backgroundRect,
                                GaugeConstants.Background.BackgroundCornerRadius,
                                GaugeConstants.Background.BackgroundCornerRadius,
                                backgroundPaint);

            using var textPaint = new SKPaint
            {
                Color = SKColors.Black,
                TextSize = rect.Height * GaugeConstants.Background.VuTextSizeFactor,
                TextAlign = SKTextAlign.Center,
                Typeface = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold),
                IsAntialias = true
            };
            canvas.DrawText("VU",
                           backgroundRect.MidX,
                           backgroundRect.Bottom - (backgroundRect.Height * GaugeConstants.Background.VuTextBottomOffsetFactor),
                           textPaint);
        }

        private void DrawScale(SKCanvas canvas, SKRect rect, bool isOverlayActive)
        {
            float centerX = rect.MidX;
            float centerY = rect.MidY + rect.Height * GaugeConstants.Scale.CenterYOffsetFactor;
            float radiusX = rect.Width * (isOverlayActive ? GaugeConstants.Scale.RadiusXFactorOverlay
                                                         : GaugeConstants.Scale.RadiusXFactor);
            float radiusY = rect.Height * (isOverlayActive ? GaugeConstants.Scale.RadiusYFactorOverlay
                                                          : GaugeConstants.Scale.RadiusYFactor);

            using var tickPaint = new SKPaint { IsAntialias = true, StrokeWidth = 1.8f };

            // Improved text paint with custom typeface and shadow
            using var textPaint = new SKPaint
            {
                Color = SKColors.Black,
                IsAntialias = true,
                Typeface = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold),
                ImageFilter = SKImageFilter.CreateDropShadow(
                    0.5f, 0.5f, 0.5f, 0.5f,
                    new SKColor(255, 255, 255, 180))
            };

            foreach (var (value, label) in MajorMarks)
                DrawMark(canvas, centerX, centerY, radiusX, radiusY, value, label, isOverlayActive, tickPaint, textPaint);

            foreach (float value in MinorMarkValues)
                DrawMark(canvas, centerX, centerY, radiusX, radiusY, value, null, isOverlayActive, tickPaint, null);
        }

        private void DrawMark(SKCanvas canvas,
                             float centerX,
                             float centerY,
                             float radiusX,
                             float radiusY,
                             float value,
                             string? label,
                             bool isOverlayActive,
                             SKPaint tickPaint,
                             SKPaint? textPaint)
        {
            float normalizedValue = (value - GaugeConstants.Db.Min) / (GaugeConstants.Db.Max - GaugeConstants.Db.Min);
            float angle = GaugeConstants.Angle.Start + normalizedValue * GaugeConstants.Angle.TotalRange;
            float radian = angle * (float)Math.PI / 180f;

            float tickLength = radiusY * (label != null
                ? (isOverlayActive
                    ? (value == 0 ? GaugeConstants.Scale.TickLengthZeroFactorOverlay : GaugeConstants.Scale.TickLengthFactorOverlay)
                    : (value == 0 ? GaugeConstants.Scale.TickLengthZeroFactor : GaugeConstants.Scale.TickLengthFactor))
                : (isOverlayActive ? GaugeConstants.Scale.TickLengthMinorFactorOverlay : GaugeConstants.Scale.TickLengthMinorFactor));

            float x1 = centerX + (radiusX - tickLength) * (float)Math.Cos(radian);
            float y1 = centerY + (radiusY - tickLength) * (float)Math.Sin(radian);
            float x2 = centerX + radiusX * (float)Math.Cos(radian);
            float y2 = centerY + radiusY * (float)Math.Sin(radian);

            // Enhanced tick colors with gradients for major marks
            if (label != null)
            {
                using var tickGradient = SKShader.CreateLinearGradient(
                    new SKPoint(x1, y1),
                    new SKPoint(x2, y2),
                    value >= 0
                        ? new[] { new SKColor(200, 0, 0), SKColors.Red }
                        : new[] { new SKColor(60, 60, 60), new SKColor(100, 100, 100) },
                    null,
                    SKShaderTileMode.Clamp);

                tickPaint.Shader = tickGradient;
                canvas.DrawLine(x1, y1, x2, y2, tickPaint);
                tickPaint.Shader = null;
            }
            else
            {
                tickPaint.Color = value >= 0 ? new SKColor(220, 0, 0) : new SKColor(80, 80, 80);
                canvas.DrawLine(x1, y1, x2, y2, tickPaint);
            }

            if (!string.IsNullOrEmpty(label) && textPaint != null)
            {
                float textOffset = radiusY * (isOverlayActive ? GaugeConstants.Scale.TextOffsetFactorOverlay
                                                              : GaugeConstants.Scale.TextOffsetFactor);
                textPaint.TextSize = radiusY * (isOverlayActive ? GaugeConstants.Scale.TextSizeFactorOverlay
                                                               : GaugeConstants.Scale.TextSizeFactor);

                // Make "0" text slightly larger and emphasized
                if (value == 0)
                {
                    textPaint.TextSize *= 1.15f;
                    textPaint.FakeBoldText = true;
                }
                else
                {
                    textPaint.FakeBoldText = false;
                }

                // Red color for positive values
                textPaint.Color = value >= 0 ? new SKColor(200, 0, 0) : SKColors.Black;

                float textX = x2 + textOffset * (float)Math.Cos(radian);
                float textY = y2 + textOffset * (float)Math.Sin(radian) + textPaint.FontMetrics.Descent;
                canvas.DrawText(label, textX, textY, textPaint);
            }
        }

        private void DrawNeedle(SKCanvas canvas, SKRect rect, float needlePosition, bool isOverlayActive)
        {
            float centerX = rect.MidX;
            float centerY = rect.MidY + rect.Height * _config.NeedleCenterYOffsetMultiplier;
            float radiusX = rect.Width * (isOverlayActive ? GaugeConstants.Scale.RadiusXFactorOverlay
                                                         : GaugeConstants.Scale.RadiusXFactor);
            float radiusY = rect.Height * (isOverlayActive ? GaugeConstants.Scale.RadiusYFactorOverlay
                                                          : GaugeConstants.Scale.RadiusYFactor);

            float angle = GaugeConstants.Angle.Start + needlePosition * GaugeConstants.Angle.TotalRange;
            var (ellipseX, ellipseY) = CalculatePointOnEllipse(centerX, centerY, radiusX, radiusY, angle);
            var (unitX, unitY, _) = NormalizeVector(ellipseX - centerX, ellipseY - centerY);

            float needleLength = MathF.Min(radiusX, radiusY) * _config.NeedleLengthMultiplier;
            float needleX = centerX + unitX * needleLength;
            float needleY = centerY + unitY * needleLength;

            // Create a needle path for more stylish appearance
            using var needlePath = new SKPath();

            // Calculate needle width at base
            float baseWidth = GaugeConstants.Needle.StrokeWidth * 2.5f;

            // Calculate perpendicular vector for needle width
            float perpX = -unitY;
            float perpY = unitX;

            // Define needle points
            float tipX = needleX;
            float tipY = needleY;
            float baseLeftX = centerX + perpX * baseWidth;
            float baseLeftY = centerY + perpY * baseWidth;
            float baseRightX = centerX - perpX * baseWidth;
            float baseRightY = centerY - perpY * baseWidth;

            // Draw needle as a triangle
            needlePath.MoveTo(tipX, tipY);
            needlePath.LineTo(baseLeftX, baseLeftY);
            needlePath.LineTo(baseRightX, baseRightY);
            needlePath.Close();

            // Create gradient for needle
            using var needleGradient = SKShader.CreateLinearGradient(
                new SKPoint(centerX, centerY),
                new SKPoint(needleX, needleY),
                new[] {
            new SKColor(40, 40, 40),
            needlePosition > 0.75f ? SKColors.Red : new SKColor(180, 0, 0)
                },
                null,
                SKShaderTileMode.Clamp);

            using var needlePaint = new SKPaint
            {
                Style = SKPaintStyle.Fill,
                Shader = needleGradient,
                IsAntialias = true
            };

            // Add shadow effect to needle
            needlePaint.ImageFilter = SKImageFilter.CreateDropShadow(
                2f, 2f, 1.5f, 1.5f,
                SKColors.Black.WithAlpha(100));

            canvas.DrawPath(needlePath, needlePaint);

            // Draw needle outline for definition
            using var outlinePaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 0.8f,
                Color = SKColors.Black.WithAlpha(180),
                IsAntialias = true
            };
            canvas.DrawPath(needlePath, outlinePaint);

            // Draw center pivot with metallic effect
            float centerCircleRadius = rect.Width * (isOverlayActive ? GaugeConstants.Needle.CenterCircleRadiusOverlay
                                                                    : GaugeConstants.Needle.CenterCircleRadius);

            // Create metallic gradient for pivot
            using var pivotGradient = SKShader.CreateRadialGradient(
                new SKPoint(centerX - centerCircleRadius * 0.3f, centerY - centerCircleRadius * 0.3f),
                centerCircleRadius * 2,
                new[] { SKColors.White, new SKColor(180, 180, 180), new SKColor(60, 60, 60) },
                new[] { 0.0f, 0.3f, 1.0f },
                SKShaderTileMode.Clamp);

            using var centerCirclePaint = new SKPaint
            {
                Shader = pivotGradient,
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };

            canvas.DrawCircle(centerX, centerY, centerCircleRadius, centerCirclePaint);

            // Add slight highlight to pivot
            using var highlightPaint = new SKPaint
            {
                Color = SKColors.White.WithAlpha(150),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };

            canvas.DrawCircle(
                centerX - centerCircleRadius * 0.25f,
                centerY - centerCircleRadius * 0.25f,
                centerCircleRadius * 0.4f,
                highlightPaint);
        }

        private void DrawPeakLamp(SKCanvas canvas, SKRect rect, bool isOverlayActive)
        {
            float radiusX = rect.Width * (isOverlayActive ? GaugeConstants.Scale.RadiusXFactorOverlay
                                                         : GaugeConstants.Scale.RadiusXFactor);
            float radiusY = rect.Height * (isOverlayActive ? GaugeConstants.Scale.RadiusYFactorOverlay
                                                          : GaugeConstants.Scale.RadiusYFactor);
            float lampRadius = MathF.Min(radiusX, radiusY) * (isOverlayActive ? GaugeConstants.PeakLamp.RadiusFactorOverlay
                                                                             : GaugeConstants.PeakLamp.RadiusFactor);
            float lampX = rect.Right - rect.Width * (isOverlayActive ? GaugeConstants.PeakLamp.LampXOffsetFactorOverlay
                                                                    : GaugeConstants.PeakLamp.LampXOffsetFactor);
            float lampY = rect.Top + rect.Height * (isOverlayActive ? GaugeConstants.PeakLamp.LampYOffsetFactorOverlay
                                                                   : GaugeConstants.PeakLamp.LampYOffsetFactor);

            // Fixed peak lamp logic - use a brighter red when active and add glow effect
            SKColor lampColor = _state.PeakActive ? SKColors.Red : new SKColor(80, 0, 0);
            SKColor glowColor = _state.PeakActive ? SKColors.Red.WithAlpha(80) : SKColors.Transparent;

            // Draw glow effect when lamp is active
            if (_state.PeakActive)
            {
                using var glowPaint = new SKPaint
                {
                    Color = glowColor,
                    MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, lampRadius * 0.5f),
                    IsAntialias = true
                };
                canvas.DrawCircle(lampX, lampY, lampRadius * 1.5f, glowPaint);
            }

            // Create realistic lamp appearance with gradient
            using var innerGradient = SKShader.CreateRadialGradient(
                new SKPoint(lampX - lampRadius * 0.2f, lampY - lampRadius * 0.2f),
                lampRadius * 0.8f,
                _state.PeakActive
                    ? new[] { SKColors.White, new SKColor(255, 180, 180), lampColor }
                    : new[] { new SKColor(220, 220, 220), new SKColor(180, 0, 0), new SKColor(80, 0, 0) },
                new[] { 0.0f, 0.3f, 1.0f },
                SKShaderTileMode.Clamp
            );

            using var innerPaint = new SKPaint
            {
                Style = SKPaintStyle.Fill,
                Shader = innerGradient,
                IsAntialias = true
            };
            canvas.DrawCircle(lampX, lampY, lampRadius * 0.8f, innerPaint);

            // Add glass-like reflection effect
            using var reflectionPaint = new SKPaint
            {
                Color = SKColors.White.WithAlpha(180),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            canvas.DrawCircle(lampX - lampRadius * 0.3f, lampY - lampRadius * 0.3f, lampRadius * 0.25f, reflectionPaint);

            // Enhanced rim with beveled edge look
            using var rimPaint = new SKPaint
            {
                Color = new SKColor(40, 40, 40),
                Style = SKPaintStyle.Stroke,
                StrokeWidth = GaugeConstants.PeakLamp.RimStrokeWidth * 1.2f,
                IsAntialias = true
            };
            canvas.DrawCircle(lampX, lampY, lampRadius, rimPaint);

            // Improved PEAK text with shadow and custom typeface
            using var peakTextPaint = new SKPaint
            {
                Color = _state.PeakActive ? SKColors.Red : new SKColor(180, 0, 0),
                TextSize = lampRadius * GaugeConstants.PeakLamp.TextSizeFactor * 1.2f,
                TextAlign = SKTextAlign.Center,
                Typeface = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold),
                IsAntialias = true,
                ImageFilter = SKImageFilter.CreateDropShadow(1, 1, 1, 1, SKColors.Black.WithAlpha(150))
            };
            float textYOffset = lampRadius * GaugeConstants.PeakLamp.TextYOffsetFactor + peakTextPaint.FontMetrics.Descent;
            canvas.DrawText("PEAK", lampX, lampY + textYOffset, peakTextPaint);
        }

        private static SKPaint CreatePaint(SKPaintStyle style, SKColor color, float strokeWidth = 0f)
        {
            var paint = new SKPaint { Style = style, Color = color, IsAntialias = true };
            if (style == SKPaintStyle.Stroke || style == SKPaintStyle.StrokeAndFill)
                paint.StrokeWidth = strokeWidth;
            return paint;
        }

        private static SKPaint CreateTextPaint(float textSize,
                                              SKTextAlign align,
                                              string family,
                                              SKFontStyle style,
                                              SKColor color)
        {
            return new SKPaint
            {
                TextSize = textSize,
                TextAlign = align,
                Typeface = SKTypeface.FromFamilyName(family, style),
                Color = color,
                IsAntialias = true
            };
        }

        private static (float x, float y) CalculatePointOnEllipse(float centerX,
                                                                 float centerY,
                                                                 float radiusX,
                                                                 float radiusY,
                                                                 float angleDegrees)
        {
            float radian = angleDegrees * MathF.PI / 180f;
            return (centerX + radiusX * MathF.Cos(radian), centerY + radiusY * MathF.Sin(radian));
        }

        private static (float unitX, float unitY, float length) NormalizeVector(float dx, float dy)
        {
            float length = MathF.Sqrt(dx * dx + dy * dy);
            return (dx / length, dy / length, length);
        }

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

        private void UpdateGaugeState(float[] spectrum)
        {
            float dbValue = CalculateLoudness(spectrum);
            float smoothedDb = SmoothValue(dbValue);
            _state.TargetNeedlePosition = CalculateNeedlePosition(smoothedDb);
            UpdateNeedlePosition();

            // Updated peak lamp logic - activate immediately when needle reaches +5 dB
            // Convert the +5 dB threshold to a normalized needle position value
            float thresholdPosition = CalculateNeedlePosition(GaugeConstants.Db.PeakThreshold);

            if (_state.CurrentNeedlePosition >= thresholdPosition)
            {
                _state.PeakActive = true;
                _peakHoldCounter = _peakHoldDuration; // Hold the peak lamp on for a duration
            }
            else if (_peakHoldCounter > 0)
            {
                _peakHoldCounter--; // Keep lamp on during countdown
            }
            else
            {
                _state.PeakActive = false;
            }
        }

        public static float CalculateLoudness(ReadOnlySpan<float> spectrum)
        {
            if (spectrum.IsEmpty) return GaugeConstants.Db.Min;
            float sumOfSquares = 0f;
            for (int i = 0; i < spectrum.Length; i++)
                sumOfSquares += spectrum[i] * spectrum[i];
            float rms = MathF.Sqrt(sumOfSquares / spectrum.Length);
            float db = 20f * MathF.Log10(MathF.Max(rms, GaugeConstants.Rendering.MinDbClamp));
            return Math.Clamp(db, GaugeConstants.Db.Min, GaugeConstants.Db.Max);
        }

        private float SmoothValue(float newValue)
        {
            float smoothingFactor = newValue > _state.PreviousValue
                ? _config.SmoothingFactorIncrease
                : _config.SmoothingFactorDecrease;
            return _state.PreviousValue += smoothingFactor * (newValue - _state.PreviousValue);
        }

        public static float CalculateNeedlePosition(float db)
        {
            float normalizedPosition = (Math.Clamp(db, GaugeConstants.Db.Min, GaugeConstants.Db.Max) - GaugeConstants.Db.Min)
                                     / (GaugeConstants.Db.Max - GaugeConstants.Db.Min);
            return Math.Clamp(normalizedPosition, GaugeConstants.Rendering.Margin, 1f - GaugeConstants.Rendering.Margin);
        }

        private void UpdateNeedlePosition()
        {
            float difference = _state.TargetNeedlePosition - _state.CurrentNeedlePosition;
            float speed = difference * (difference > 0 ? _config.RiseSpeed : _config.FallSpeed);
            float easedSpeed = speed * (1 - _config.Damping) * (1 - MathF.Abs(difference));
            _state.CurrentNeedlePosition += easedSpeed;
            _state.CurrentNeedlePosition = Math.Clamp(_state.CurrentNeedlePosition, 0f, 1f);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _sharedTickPaint.Dispose();
            _sharedRimPaint.Dispose();
            _sharedNeedlePathPaint.Dispose();
            _sharedPivotPaint.Dispose();
            _sharedTextPaint.Dispose();
            _sharedLampPaint.Dispose();
            _sharedPeakTextPaint.Dispose();
            _disposed = true;
        }
    }
}