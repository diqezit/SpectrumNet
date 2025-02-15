#nullable enable

namespace SpectrumNet
{
    public sealed class RainbowRenderer : ISpectrumRenderer, IDisposable
    {
        private static readonly Lazy<RainbowRenderer> _instance = new(() => new RainbowRenderer());
        private bool _isInitialized, _disposed;
        private float _smoothingFactor = 0.3f;
        private float[]? _previousSpectrum;
        private readonly SKPaint _paint;

        private RainbowRenderer()
        {
            _paint = new SKPaint
            {
                IsAntialias = true,
                FilterQuality = SKFilterQuality.High,
                Style = SKPaintStyle.Fill
            };
        }

        public static RainbowRenderer GetInstance() => _instance.Value;

        public void Initialize() => _isInitialized = true;

        public void Configure(bool isOverlayActive) => _smoothingFactor = isOverlayActive ? 0.7f : 0.35f;

        public void Render(
            SKCanvas canvas,
            float[] spectrum,
            SKImageInfo info,
            float barWidth,
            float barSpacing,
            int barCount,
            SKPaint paint,
            Action<SKCanvas, SKImageInfo> drawPerformanceInfo)
        {
            if (_disposed) return;

            float[] smoothedSpectrum = SmoothSpectrum(spectrum);

            int saveCount = canvas.Save();
            DrawSpectrumBars(canvas, smoothedSpectrum, info, barWidth, barSpacing, barCount);
            drawPerformanceInfo?.Invoke(canvas, info);
            canvas.RestoreToCount(saveCount);
        }

        private void DrawSpectrumBars(SKCanvas canvas, float[] spectrum, SKImageInfo info, float barWidth, float barSpacing, int barCount)
        {
            int halfSpectrumLength = spectrum.Length / 2;
            int actualBarCount = Math.Min(halfSpectrumLength, barCount);
            float[] scaledSpectrum = ScaleSpectrum(spectrum.AsSpan(0, halfSpectrumLength), actualBarCount);

            float totalBarWidth = barWidth + barSpacing;
            float startX = (info.Width - (actualBarCount * totalBarWidth - barSpacing)) / 2f;
            float bottomY = info.Height;
            float maxBarHeight = info.Height;

            for (int i = 0; i < actualBarCount; i++)
            {
                float barHeight = maxBarHeight * scaledSpectrum[i];
                _paint.Color = GetRainbowColor(scaledSpectrum[i]);

                canvas.DrawRect(SKRect.Create(startX + i * totalBarWidth, bottomY - barHeight, barWidth, barHeight), _paint);
            }
        }

        private static float[] ScaleSpectrum(ReadOnlySpan<float> spectrum, int barCount)
        {
            float[] scaledSpectrum = new float[barCount];
            float blockSize = (float)spectrum.Length / barCount;

            for (int i = 0; i < barCount; i++)
            {
                float sum = 0;
                int start = (int)(i * blockSize);
                int end = Math.Min((int)((i + 1) * blockSize), spectrum.Length);

                for (int j = start; j < end; j++) sum += spectrum[j];
                scaledSpectrum[i] = sum / (end - start);
            }

            return scaledSpectrum;
        }

        private static SKColor GetRainbowColor(float normalizedValue)
        {
            float hue = 240 - 240 * normalizedValue;
            return SKColor.FromHsv(hue < 0 ? hue + 360 : hue, 100, 100);
        }

        private float[] SmoothSpectrum(float[] current)
        {
            _previousSpectrum ??= new float[current.Length];
            float adaptiveFactor = _smoothingFactor * (1f + MathF.Pow(CalculateLoudness(current), 2) * 0.4f);

            for (int i = 0; i < current.Length; i++)
                _previousSpectrum[i] += (current[i] - _previousSpectrum[i]) * adaptiveFactor;

            return _previousSpectrum;
        }

        private static float CalculateLoudness(ReadOnlySpan<float> spectrum)
        {
            if (spectrum.IsEmpty) return 0f;

            float sum = 0f;
            int length = spectrum.Length;
            int subBass = length >> 4, bass = length >> 3, mid = length >> 2;

            for (int i = 0; i < length; i++)
            {
                float weight = i < subBass ? 1.5f : i < bass ? 1.3f : i < mid ? 1.1f : 0.7f;
                sum += MathF.Abs(spectrum[i]) * weight;
            }

            return Math.Clamp(sum / length * 3.7f, 0f, 1f);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _paint.Dispose();
            _previousSpectrum = null;
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }
}