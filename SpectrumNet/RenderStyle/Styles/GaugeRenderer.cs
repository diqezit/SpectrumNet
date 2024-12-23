namespace SpectrumNet
{
    public static class GaugeConstants
    {
        public static class Db
        {
            public const float Max = 5f, Min = -30f, PeakThreshold = 1.5f;
        }

        public static class Angle
        {
            public const float Start = -150f, End = -30f, TotalRange = End - Start;
        }

        public static class Needle
        {
            public const float DefaultLengthMultiplier = 1.55f; // Длина стрелки
            public const float DefaultCenterYOffsetMultiplier = 0.4f; // Смещение центра по Y
            public const float StrokeWidth = 2.25f; // Толщина стрелки
            public const float CenterCircleRadiusOverlay = 0.015f, CenterCircleRadius = 0.02f; // Радиус центральной точки
        }

        public static class Background
        {
            public const float OuterFrameCornerRadius = 8f, InnerFramePadding = 4f, InnerFrameCornerRadius = 6f;
            public const float BackgroundPadding = 4f, BackgroundCornerRadius = 4f;
            public const float VuTextSizeFactor = 0.2f, VuTextBottomOffsetFactor = 0.2f; // Параметры текста "VU"
        }

        public static class Scale
        {
            public const float CenterYOffsetFactor = 0.15f; // Смещение центра шкалы
            public const float RadiusXFactorOverlay = 0.4f, RadiusXFactor = 0.45f;
            public const float RadiusYFactorOverlay = 0.45f, RadiusYFactor = 0.5f;
            public const float TextOffsetFactorOverlay = 0.1f, TextOffsetFactor = 0.12f;
            public const float TextSizeFactorOverlay = 0.08f, TextSizeFactor = 0.1f;
            public const float TickLengthZeroFactorOverlay = 0.12f, TickLengthZeroFactor = 0.15f;
            public const float TickLengthFactorOverlay = 0.07f, TickLengthFactor = 0.08f;
            public const float TickLengthMinorFactorOverlay = 0.05f, TickLengthMinorFactor = 0.06f; // Параметры мелких делений
        }

        public static class PeakLamp
        {
            public const float RadiusFactorOverlay = 0.04f, RadiusFactor = 0.05f;
            public const float LampXOffsetFactorOverlay = 0.12f, LampXOffsetFactor = 0.1f;
            public const float LampYOffsetFactorOverlay = 0.18f, LampYOffsetFactor = 0.2f;
            public const float TextSizeFactorOverlay = 1.2f, TextSizeFactor = 1.5f;
            public const float TextYOffsetFactor = 2.5f, RimStrokeWidth = 1f; // Параметры текста и обводки лампы
        }

        public static class Rendering
        {
            public const float AspectRatio = 2.0f; // Соотношение сторон
            public const float GaugeRectPadding = 0.8f, MinDbClamp = 1e-10f; // Параметры рендера
            public const float Margin = 0.05f; // 5% зазора для стрелки
        }

        public static class MinorMarks
        {
            public const int Divisor = 3; // Делитель для мелких делений
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
        #region Fields and Properties
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

        private static readonly Lazy<GaugeRenderer> Instance =
            new(() => new GaugeRenderer(), LazyThreadSafetyMode.ExecutionAndPublication);

        private static readonly (float Value, string Label)[] MajorMarks = new[]
        {
            (-30f, "-30"), (-20f, "-20"), (-10f, "-10"), (-7f, "-7"),
            (-5f, "-5"), (-3f, "-3"), (0f, "0"), (3f, "+3"), (5f, "+5")
        };

        private static readonly List<float> MinorMarkValues = new();

        private static readonly SKPaint _sharedTickPaint;
        private static readonly SKPaint _sharedRimPaint;
        private static readonly SKPaint _sharedNeedlePathPaint;
        private static readonly SKPaint _sharedPivotPaint;
        private static readonly SKPaint _sharedTextPaint;
        private static readonly SKPaint _sharedLampPaint;
        private static readonly SKPaint _sharedPeakTextPaint;
        #endregion

        #region Constructor and Initialization
        static GaugeRenderer()
        {
            _sharedTickPaint = CreatePaint(SKPaintStyle.Stroke, SKColors.Black, 3.5f);
            _sharedRimPaint = CreatePaint(SKPaintStyle.Stroke, SKColors.Black, 1f);
            _sharedNeedlePathPaint = CreatePaint(SKPaintStyle.Stroke, SKColors.Black, GaugeConstants.Needle.StrokeWidth);
            _sharedPivotPaint = CreatePaint(SKPaintStyle.Fill, SKColors.DarkGray, 0);
            _sharedTextPaint = CreateTextPaint(30, SKTextAlign.Center, "Arial", SKFontStyle.Bold, SKColors.Black);
            _sharedLampPaint = CreatePaint(SKPaintStyle.Fill, SKColors.DarkRed, 0);
            _sharedPeakTextPaint = CreateTextPaint(30, SKTextAlign.Center, "Arial", SKFontStyle.Bold, SKColors.Black);

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

        public void Configure(bool isOverlayActive)
        {
            _config = _config.WithOverlayMode(isOverlayActive);
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
                {
                    MinorMarkValues.Add(value);
                }
            }
        }
        #endregion

        #region Rendering Methods
        public void Render(SKCanvas? canvas, float[]? spectrum, SKImageInfo info,
                          float barWidth, float barSpacing, int barCount,
                          SKPaint? paint, Action<SKCanvas, SKImageInfo> drawPerformanceInfo)
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

        private void RenderGaugeComponents(SKCanvas canvas, SKImageInfo info,
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
            var outerFrameColor = new SKColor(80, 80, 80); // Darker gray 
            var innerFrameColor = new SKColor(105, 105, 105); // Slightly lighter dark gray
            var backgroundStartColor = new SKColor(250, 250, 240); // Light cream
            var backgroundEndColor = new SKColor(230, 230, 215); // Darker cream

            // Outer frame with a subtle shadow for depth
            using var outerFramePaint = new SKPaint
            {
                Color = outerFrameColor,
                Style = SKPaintStyle.Fill,
                IsAntialias = true,
            };
            canvas.DrawRoundRect(rect,
                GaugeConstants.Background.OuterFrameCornerRadius,
                GaugeConstants.Background.OuterFrameCornerRadius,
                outerFramePaint);

            // Inner frame
            var innerFrameRect = new SKRect(
                rect.Left + GaugeConstants.Background.InnerFramePadding,
                rect.Top + GaugeConstants.Background.InnerFramePadding,
                rect.Right - GaugeConstants.Background.InnerFramePadding,
                rect.Bottom - GaugeConstants.Background.InnerFramePadding
            );
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

            // Background with gradient
            var backgroundRect = new SKRect(
                innerFrameRect.Left + GaugeConstants.Background.BackgroundPadding,
                innerFrameRect.Top + GaugeConstants.Background.BackgroundPadding,
                innerFrameRect.Right - GaugeConstants.Background.BackgroundPadding,
                innerFrameRect.Bottom - GaugeConstants.Background.BackgroundPadding
            );
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

            // "VU" text with improved font and positioning
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

            float radiusX = rect.Width * (isOverlayActive
                ? GaugeConstants.Scale.RadiusXFactorOverlay
                : GaugeConstants.Scale.RadiusXFactor);
            float radiusY = rect.Height * (isOverlayActive
                ? GaugeConstants.Scale.RadiusYFactorOverlay
                : GaugeConstants.Scale.RadiusYFactor);

            using var tickPaint = new SKPaint
            {
                IsAntialias = true,
                StrokeWidth = 2f 
            };

            using var textPaint = new SKPaint
            {
                Color = SKColors.Black,
                IsAntialias = true
            };

            // Draw major marks
            foreach (var (value, label) in MajorMarks)
            {
                float normalizedValue = (value - GaugeConstants.Db.Min) / (GaugeConstants.Db.Max - GaugeConstants.Db.Min);
                float angle = GaugeConstants.Angle.Start + normalizedValue * GaugeConstants.Angle.TotalRange;
                float radian = angle * (float)Math.PI / 180f;

                float tickLength = radiusY * (isOverlayActive
                    ? (value == 0 ? GaugeConstants.Scale.TickLengthZeroFactorOverlay : GaugeConstants.Scale.TickLengthFactorOverlay)
                    : (value == 0 ? GaugeConstants.Scale.TickLengthZeroFactor : GaugeConstants.Scale.TickLengthFactor));

                float x1 = centerX + (radiusX - tickLength) * (float)Math.Cos(radian);
                float y1 = centerY + (radiusY - tickLength) * (float)Math.Sin(radian);
                float x2 = centerX + radiusX * (float)Math.Cos(radian);
                float y2 = centerY + radiusY * (float)Math.Sin(radian);

                // Set color and draw line based on value
                tickPaint.Color = value >= 0 ? SKColors.Red : SKColors.DarkGray;
                canvas.DrawLine(x1, y1, x2, y2, tickPaint);

                if (!string.IsNullOrEmpty(label))
                {
                    float textOffset = radiusY * (isOverlayActive
                        ? GaugeConstants.Scale.TextOffsetFactorOverlay
                        : GaugeConstants.Scale.TextOffsetFactor);

                    textPaint.TextSize = radiusY * (isOverlayActive
                        ? GaugeConstants.Scale.TextSizeFactorOverlay
                        : GaugeConstants.Scale.TextSizeFactor);

                    float textX = x2 + textOffset * (float)Math.Cos(radian);
                    float textY = y2 + textOffset * (float)Math.Sin(radian) + textPaint.FontMetrics.Descent;

                    canvas.DrawText(label, textX, textY, textPaint);
                }
            }

            // Draw minor marks without labels
            foreach (float value in MinorMarkValues)
            {
                float normalizedValue = (value - GaugeConstants.Db.Min) / (GaugeConstants.Db.Max - GaugeConstants.Db.Min);
                float angle = GaugeConstants.Angle.Start + normalizedValue * GaugeConstants.Angle.TotalRange;
                float radian = angle * (float)Math.PI / 180f;

                float tickLength = radiusY * (isOverlayActive
                    ? GaugeConstants.Scale.TickLengthMinorFactorOverlay
                    : GaugeConstants.Scale.TickLengthMinorFactor);

                float x1 = centerX + (radiusX - tickLength) * (float)Math.Cos(radian);
                float y1 = centerY + (radiusY - tickLength) * (float)Math.Sin(radian);
                float x2 = centerX + radiusX * (float)Math.Cos(radian);
                float y2 = centerY + radiusY * (float)Math.Sin(radian);

                // Set color for minor marks
                tickPaint.Color = value >= 0 ? SKColors.Red : SKColors.DarkGray;
                canvas.DrawLine(x1, y1, x2, y2, tickPaint);
            }
        }

        private void DrawNeedle(SKCanvas canvas, SKRect rect, float needlePosition, bool isOverlayActive)
        {
            // Calculate the center position
            float centerX = rect.MidX;
            float centerY = rect.MidY + rect.Height * _config.NeedleCenterYOffsetMultiplier;

            // Calculate ellipse radii
            float radiusX = rect.Width * (isOverlayActive ? GaugeConstants.Scale.RadiusXFactorOverlay : GaugeConstants.Scale.RadiusXFactor);
            float radiusY = rect.Height * (isOverlayActive ? GaugeConstants.Scale.RadiusYFactorOverlay : GaugeConstants.Scale.RadiusYFactor);

            // Determine needle angle
            float angle = GaugeConstants.Angle.Start + needlePosition * GaugeConstants.Angle.TotalRange;
            var (ellipseX, ellipseY) = CalculatePointOnEllipse(centerX, centerY, radiusX, radiusY, angle);
            var (unitX, unitY, _) = NormalizeVector(ellipseX - centerX, ellipseY - centerY);

            // Calculate needle end point
            float needleLength = MathF.Min(radiusX, radiusY) * _config.NeedleLengthMultiplier;
            float needleX = centerX + unitX * needleLength;
            float needleY = centerY + unitY * needleLength;

            // Draw the needle with a gradient effect
            using var needlePaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                StrokeWidth = GaugeConstants.Needle.StrokeWidth,
                Shader = SKShader.CreateLinearGradient(
                    new SKPoint(centerX, centerY),
                    new SKPoint(needleX, needleY),
                    new[] { SKColors.Black, SKColors.Red }, // From black to red
                    null,
                    SKShaderTileMode.Clamp),
                IsAntialias = true
            };
            canvas.DrawLine(centerX, centerY, needleX, needleY, needlePaint);

            // Draw center circle with a distinct color
            float centerCircleRadius = rect.Width * (isOverlayActive ? GaugeConstants.Needle.CenterCircleRadiusOverlay : GaugeConstants.Needle.CenterCircleRadius);
            using var centerCirclePaint = new SKPaint
            {
                Color = SKColors.Black,
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            canvas.DrawCircle(centerX, centerY, centerCircleRadius, centerCirclePaint);
        }

        private void DrawPeakLamp(SKCanvas canvas, SKRect rect, bool isOverlayActive)
        {
            // Calculate dimensions and positions
            float radiusX = rect.Width * (isOverlayActive ? GaugeConstants.Scale.RadiusXFactorOverlay : GaugeConstants.Scale.RadiusXFactor);
            float radiusY = rect.Height * (isOverlayActive ? GaugeConstants.Scale.RadiusYFactorOverlay : GaugeConstants.Scale.RadiusYFactor);
            float lampRadius = MathF.Min(radiusX, radiusY) * (isOverlayActive ? GaugeConstants.PeakLamp.RadiusFactorOverlay : GaugeConstants.PeakLamp.RadiusFactor);
            float lampX = rect.Right - rect.Width * (isOverlayActive ? GaugeConstants.PeakLamp.LampXOffsetFactorOverlay : GaugeConstants.PeakLamp.LampXOffsetFactor);
            float lampY = rect.Top + rect.Height * (isOverlayActive ? GaugeConstants.PeakLamp.LampYOffsetFactorOverlay : GaugeConstants.PeakLamp.LampYOffsetFactor);

            // Set lamp color based on active state
            _sharedLampPaint.Color = _state.PeakActive ? SKColors.Red : new SKColor(139, 0, 0); // Dark Red with more depth

            // Add shadow for lamp
            _sharedLampPaint.ImageFilter = SKImageFilter.CreateDropShadow(0, 2, 2, 2, SKColors.Gray.WithAlpha(128));
            canvas.DrawCircle(lampX, lampY, lampRadius, _sharedLampPaint);

            // Draw rim circle with a more distinct color
            using var rimPaint = new SKPaint
            {
                Color = SKColors.Black,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = GaugeConstants.PeakLamp.RimStrokeWidth,
                IsAntialias = true
            };
            canvas.DrawCircle(lampX, lampY, lampRadius, rimPaint);

            // Draw "PEAK" text with improved positioning
            using var peakTextPaint = new SKPaint
            {
                Color = SKColors.MediumVioletRed,
                TextSize = lampRadius * 1.75f,
                TextAlign = SKTextAlign.Center,
                IsAntialias = true
            };

            // Calculate the text position using font metrics
            float textYOffset = lampRadius * GaugeConstants.PeakLamp.TextYOffsetFactor + peakTextPaint.FontMetrics.Descent;
            canvas.DrawText("PEAK", lampX, lampY + textYOffset, peakTextPaint);
        }
        #endregion

        #region Helper Methods
        private static SKPaint CreatePaint(SKPaintStyle style, SKColor color, float strokeWidth = 0f)
        {
            var paint = new SKPaint
            {
                Style = style,
                Color = color,
                IsAntialias = true
            };

            if (style == SKPaintStyle.Stroke || style == SKPaintStyle.StrokeAndFill)
            {
                paint.StrokeWidth = strokeWidth;
            }

            return paint;
        }

        private static SKPaint CreateTextPaint(float textSize, SKTextAlign align, string family,
            SKFontStyle style, SKColor color)
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

        private static (float x, float y) CalculatePointOnEllipse(
            float centerX, float centerY, float radiusX, float radiusY, float angleDegrees)
        {
            float radian = angleDegrees * MathF.PI / 180f;
            return (
                centerX + radiusX * MathF.Cos(radian),
                centerY + radiusY * MathF.Sin(radian)
            );
        }

        private static (float unitX, float unitY, float length) NormalizeVector(float dx, float dy)
        {
            float length = MathF.Sqrt(dx * dx + dy * dy);
            return (dx / length, dy / length, length);
        }

        public static SKRect CalculateGaugeRect(SKImageInfo info)
        {
            float aspectRatio = GaugeConstants.Rendering.AspectRatio;
            float width, height;

            if (info.Width / (float)info.Height > aspectRatio)
            {
                height = info.Height * GaugeConstants.Rendering.GaugeRectPadding;
                width = height * aspectRatio;
            }
            else
            {
                width = info.Width * GaugeConstants.Rendering.GaugeRectPadding;
                height = width / aspectRatio;
            }

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
            _state.PeakActive = smoothedDb >= GaugeConstants.Db.PeakThreshold;
        }

        public static float CalculateLoudness(ReadOnlySpan<float> spectrum)
        {
            if (spectrum.IsEmpty) return GaugeConstants.Db.Min;

            float sumOfSquares = 0f;
            for (int i = 0; i < spectrum.Length; i++)
            {
                sumOfSquares += spectrum[i] * spectrum[i];
            }

            float rms = MathF.Sqrt(sumOfSquares / spectrum.Length);
            float db = 20f * MathF.Log10(MathF.Max(rms, GaugeConstants.Rendering.MinDbClamp));

            return Math.Clamp(db, GaugeConstants.Db.Min, GaugeConstants.Db.Max);
        }

        private float SmoothValue(float newValue)
        {
            float smoothingFactor = newValue > _state.PreviousValue
                ? _config.SmoothingFactorIncrease
                : _config.SmoothingFactorDecrease;

            _state.PreviousValue += smoothingFactor * (newValue - _state.PreviousValue);
            return _state.PreviousValue;
        }

        public static float CalculateNeedlePosition(float db)
        {
            db = Math.Clamp(db, GaugeConstants.Db.Min, GaugeConstants.Db.Max);

            float normalizedPosition = (db - GaugeConstants.Db.Min) / (GaugeConstants.Db.Max - GaugeConstants.Db.Min);
            return Math.Clamp(normalizedPosition, GaugeConstants.Rendering.Margin, 1f - GaugeConstants.Rendering.Margin);
        }

        private void UpdateNeedlePosition()
        {
            float difference = _state.TargetNeedlePosition - _state.CurrentNeedlePosition;
            float speed = difference * (_state.TargetNeedlePosition > _state.CurrentNeedlePosition
                ? _config.RiseSpeed
                : _config.FallSpeed);

            _state.CurrentNeedlePosition += speed * (1 - _config.Damping);
            _state.CurrentNeedlePosition = Math.Clamp(_state.CurrentNeedlePosition, 0f, 1f);
        }

        #endregion

        #region IDisposable Implementation
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
        #endregion
    }
}