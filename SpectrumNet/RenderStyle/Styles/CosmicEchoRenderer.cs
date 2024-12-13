#nullable enable

namespace SpectrumNet
{
    public sealed class CosmicEchoRenderer : ISpectrumRenderer, IDisposable
    {
        private static readonly Lazy<CosmicEchoRenderer> _instance = new(() => new CosmicEchoRenderer());

        private bool _isInitialized;
        private bool _disposed;
        private float _smoothingFactor = 0.3f;
        private float[]? _previousSpectrum;
        private readonly Random _random = new();

        private const float MinMagnitudeThreshold = 0.05f;
        private const int MaxRings = 10;
        private const float BaseRingWidth = 5f;

        private Vector2 _center;
        private float _maxRadius;

        private CosmicEchoRenderer() { }

        public static CosmicEchoRenderer GetInstance() => _instance.Value;

        public void Initialize()
        {
            if (_isInitialized || _disposed) return;

            _isInitialized = true;
            Log.Debug("CosmicEchoRenderer initialized");
        }

        public void Configure(bool isOverlayActive)
        {
            _smoothingFactor = isOverlayActive ? 0.6f : 0.3f;
        }

        public void Render(SKCanvas? canvas, float[]? spectrum, SKImageInfo info,
                           float unused1, float unused2, int unused3,
                           SKPaint? paint, Action<SKCanvas, SKImageInfo> drawPerformanceInfo)
        {
            if (!_isInitialized || _disposed || canvas == null || spectrum == null || paint == null)
            {
                Log.Warning("Invalid render parameters or uninitialized CosmicEchoRenderer.");
                return;
            }

            try
            {
                _center = new Vector2(info.Width / 2f, info.Height / 2f);
                _maxRadius = Math.Min(info.Width, info.Height) * 0.4f;

                int actualRingCount = Math.Min(spectrum.Length / 2, MaxRings);
                Span<float> scaledSpectrum = stackalloc float[actualRingCount];
                ScaleSpectrum(spectrum, scaledSpectrum);
                SmoothSpectrum(scaledSpectrum);

                RenderCosmicRings(canvas, paint, scaledSpectrum);
                drawPerformanceInfo(canvas, info);
            }
            catch (Exception ex)
            {
                Log.Error($"Render error in CosmicEchoRenderer: {ex.Message}");
            }
        }

        private void RenderCosmicRings(SKCanvas canvas, SKPaint basePaint, ReadOnlySpan<float> spectrum)
        {
            for (int i = 0; i < spectrum.Length; i++)
            {
                float magnitude = spectrum[i];
                if (magnitude < MinMagnitudeThreshold) continue;

                float radius = _maxRadius * (1 - magnitude);

                using var ringPaint = basePaint.Clone();
                ringPaint.Style = SKPaintStyle.Stroke;
                ringPaint.StrokeWidth = BaseRingWidth * magnitude;

                // Клонируем цвет с уменьшенной прозрачностью
                ringPaint.Color = ringPaint.Color.WithAlpha((byte)(100 * magnitude));

                canvas.DrawCircle(_center.X, _center.Y, radius, ringPaint);
            }
        }

        private void ScaleSpectrum(ReadOnlySpan<float> spectrum, Span<float> scaledSpectrum)
        {
            float blockSize = spectrum.Length / (2f * scaledSpectrum.Length);

            for (int i = 0; i < scaledSpectrum.Length; i++)
            {
                float sum = 0;
                int start = (int)(i * blockSize);
                int end = (int)((i + 1) * blockSize);
                for (int j = start; j < end; j++)
                    sum += spectrum[j];

                scaledSpectrum[i] = sum / blockSize;
            }
        }

        private void SmoothSpectrum(Span<float> spectrum)
        {
            if (_previousSpectrum == null || _previousSpectrum.Length != spectrum.Length)
            {
                _previousSpectrum = new float[spectrum.Length];
            }

            for (int i = 0; i < spectrum.Length; i++)
            {
                _previousSpectrum[i] = _previousSpectrum[i] + (spectrum[i] - _previousSpectrum[i]) * _smoothingFactor;
                spectrum[i] = _previousSpectrum[i];
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _previousSpectrum = null;
                _isInitialized = false;
                _disposed = true;
                Log.Debug("CosmicEchoRenderer disposed");
            }
        }
    }
}