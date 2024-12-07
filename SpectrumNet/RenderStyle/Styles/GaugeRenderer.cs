namespace SpectrumNet
{
    public class GaugeRenderer : ISpectrumRenderer, IDisposable
    {
        private static GaugeRenderer? _instance;
        private const float SmoothingFactorNormal = 0.3f;
        private const float SmoothingFactorOverlay = 0.5f;

        // Constants
        private const float MinDb = -20f;
        private const float MaxDb = 3f;
        private const float range = MaxDb - MinDb;
        private const float StartAngle = 150f;
        private const float SweepAngle = 60f;
        private const float PeakThreshold = 0f;

        // Fields
        private bool _isInitialized;
        private float _previousValue = MinDb;
        private float _smoothingFactor = SmoothingFactorNormal;
        private bool _peakActive = false;
        private bool _isOverlayActive = false;
        private bool _disposed = false;

        public GaugeRenderer() { }

        public static GaugeRenderer GetInstance() => _instance ??= new GaugeRenderer();

        public void Initialize()
        {
            if (_isInitialized) return;
            _isInitialized = true;
            _previousValue = MinDb;
        }

        public void Configure(bool isOverlayActive)
        {
            _isOverlayActive = isOverlayActive;
            _smoothingFactor = isOverlayActive ? SmoothingFactorOverlay : SmoothingFactorNormal;
        }

        public void Render(SKCanvas? canvas, float[]? spectrum, SKImageInfo info,
                           float barWidth, float barSpacing, int barCount, SKPaint? paint,
                           Action<SKCanvas, SKImageInfo> drawPerformanceInfo)
        {
            if (_disposed || !AreRenderParamsValid(canvas, spectrum.AsSpan(), info, paint))
                return;

            float scaleFactor = _isOverlayActive ? 0.5f : 1.0f;
            canvas!.Scale(scaleFactor);

            float dbValue = CalculateLoudness(spectrum.AsSpan());
            float smoothedDb = SmoothValue(dbValue);
            float angle = CalculateAngle(smoothedDb);

            _peakActive = smoothedDb >= PeakThreshold;

            DrawGaugeBackground(canvas, info, scaleFactor);
            DrawNeedle(canvas, info, angle, scaleFactor);
            DrawLampEffect(canvas, info, scaleFactor);
            DrawPeakLamp(canvas, info, scaleFactor);

            canvas.ResetMatrix();
            drawPerformanceInfo(canvas, info);
        }

        private float CalculateLoudness(ReadOnlySpan<float> spectrum)
        {
            if (spectrum.IsEmpty)
                return MinDb;

            float sumOfSquares = 0f;
            foreach (float value in spectrum)
                sumOfSquares += value * value;

            float rms = (float)Math.Sqrt(sumOfSquares / spectrum.Length);
            float db = 20f * (float)Math.Log10(rms + 1e-10f);
            return Math.Clamp(db, MinDb, MaxDb);
        }

        private float SmoothValue(float value)
        {
            _previousValue += (value - _previousValue) * _smoothingFactor;
            return Math.Clamp(_previousValue, MinDb, MaxDb);
        }

        private float CalculateAngle(float dbValue)
        {
            float angleOffset = SweepAngle * (dbValue - MinDb) / range;
            return StartAngle + angleOffset;
        }

        private void DrawGaugeBackground(SKCanvas canvas, SKImageInfo info, float scaleFactor)
        {
            using var paint = new SKPaint
            {
                Style = SKPaintStyle.Fill,
                Color = SKColors.Black
            };

            canvas.DrawRect(info.Rect, paint);

            using var textPaint = new SKPaint
            {
                Color = SKColors.White,
                TextSize = 20f * scaleFactor,
                TextAlign = SKTextAlign.Center
            };

            float centerX = info.Width / 2f;
            float centerY = info.Height * 0.8f * scaleFactor;
            float radius = info.Width * 0.4f * scaleFactor;

            for (int i = -20; i <= 3; i += 1)
            {
                float db = i;
                float angleOffset = SweepAngle * (db - MinDb) / range;
                float angle = StartAngle + angleOffset;

                float x = centerX + radius * (float)Math.Cos(angle * (float)Math.PI / 180f);
                float y = centerY + radius * (float)Math.Sin(angle * (float)Math.PI / 180f);

                if (i % 5 == 0)
                    canvas.DrawText($"{i}", x, y, textPaint);
            }
        }

        private void DrawNeedle(SKCanvas canvas, SKImageInfo info, float angle, float scaleFactor)
        {
            float centerX = info.Width / 2f;
            float centerY = info.Height * 0.8f * scaleFactor;
            float needleLength = info.Width * 0.4f * scaleFactor;
            float needleWidth = info.Width * 0.02f * scaleFactor;
            float needleBaseRadius = info.Width * 0.05f * scaleFactor;

            float needleEndX = centerX + needleLength * (float)Math.Cos(angle * Math.PI / 180f);
            float needleEndY = centerY - needleLength * (float)Math.Sin(angle * Math.PI / 180f);

            using var needlePath = new SKPath();
            needlePath.MoveTo(centerX - needleWidth / 2, centerY);
            needlePath.LineTo(centerX + needleWidth / 2, centerY);
            needlePath.LineTo(needleEndX, needleEndY);
            needlePath.Close();

            using var needlePaint = new SKPaint
            {
                Style = SKPaintStyle.Fill,
                Shader = SKShader.CreateLinearGradient(
                    new SKPoint(centerX, centerY),
                    new SKPoint(needleEndX, needleEndY),
                    new SKColor[] { SKColors.Red, SKColors.DarkRed },
                    new float[] { 0.0f, 1.0f },
                    SKShaderTileMode.Clamp
                ),
                IsAntialias = true
            };

            canvas.DrawPath(needlePath, needlePaint);

            using var basePaint = new SKPaint
            {
                Style = SKPaintStyle.Fill,
                Shader = SKShader.CreateRadialGradient(
                    new SKPoint(centerX, centerY),
                    needleBaseRadius,
                    new SKColor[] { SKColors.Gray, SKColors.Black },
                    new float[] { 0f, 1f },
                    SKShaderTileMode.Clamp
                ),
                IsAntialias = true
            };
            canvas.DrawCircle(centerX, centerY, needleBaseRadius, basePaint);
        }

        private void DrawLampEffect(SKCanvas canvas, SKImageInfo info, float scaleFactor)
        {
            float centerX = info.Width / 2f;
            float centerY = info.Height * 0.8f * scaleFactor;
            float radius = info.Width * 0.45f * scaleFactor;

            using var gradientPaint = new SKPaint
            {
                Style = SKPaintStyle.Fill,
                Shader = SKShader.CreateRadialGradient(
                    new SKPoint(centerX, centerY),
                    radius,
                    new SKColor[] { SKColors.Yellow.WithAlpha(100), SKColors.Transparent },
                    new float[] { 0f, 1f },
                    SKShaderTileMode.Clamp
                )
            };

            canvas.Save();
            canvas.ClipRect(info.Rect);
            canvas.DrawOval(centerX - radius, centerY - radius, 2 * radius, 2 * radius, gradientPaint);
            canvas.Restore();
        }

        private void DrawPeakLamp(SKCanvas canvas, SKImageInfo info, float scaleFactor)
        {
            float centerX = info.Width / 2f;
            float peakLampY = info.Height * 0.15f * scaleFactor;
            float lampRadius = info.Width * 0.02f * scaleFactor;

            using var lampPaint = new SKPaint
            {
                Color = _peakActive ? SKColors.Red : SKColors.Gray,
                Style = SKPaintStyle.Fill
            };

            using var glowPaint = new SKPaint
            {
                Color = _peakActive ? SKColors.Red.WithAlpha(150) : SKColors.Transparent,
                Style = SKPaintStyle.Fill,
                MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 10f * scaleFactor)
            };

            canvas.DrawCircle(centerX, peakLampY, lampRadius * 1.5f, glowPaint);
            canvas.DrawCircle(centerX, peakLampY, lampRadius, lampPaint);
        }

        private bool AreRenderParamsValid(SKCanvas? canvas, ReadOnlySpan<float> spectrum, SKImageInfo info, SKPaint? paint)
        {
            if (canvas == null || spectrum.IsEmpty || paint == null || info.Width <= 0 || info.Height <= 0)
                return false;
            return true;
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;
            _disposed = true;
        }

        ~GaugeRenderer()
        {
            Dispose(disposing: false);
        }
    }
}