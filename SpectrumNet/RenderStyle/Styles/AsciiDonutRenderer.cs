#nullable enable

namespace SpectrumNet
{
    public class AsciiDonutRenderer : ISpectrumRenderer, IDisposable
    {
        #region Fields

        private static readonly Lazy<AsciiDonutRenderer> _lazyInstance =
            new(() => new AsciiDonutRenderer(), LazyThreadSafetyMode.ExecutionAndPublication);
        private readonly Random _random = new();
        private readonly object _lock = new();
        private readonly string _asciiChars = "@Oo. ";
        private readonly int _segments = 50;
        private readonly float _donutRadius = 1.0f, _tubeRadius = 0.3f;
        private bool _isInitialized, _isDisposed;
        private float _rotationAngle;

        #endregion

        #region Singleton

        public static AsciiDonutRenderer GetInstance() => _lazyInstance.Value;

        #endregion

        #region Public Methods

        public void Initialize()
        {
            if (!_isInitialized)
            {
                _isInitialized = true;
                Log.Debug("AsciiDonutRenderer initialized");
            }
        }

        public void Configure(bool isOverlayActive) { }

        public void Render(SKCanvas canvas, float[] spectrum, SKImageInfo info, float barWidth, float barSpacing, int barCount, SKPaint paint, Action<SKCanvas, SKImageInfo> drawPerformanceInfo)
        {
            if (ValidateRenderParameters(canvas, spectrum, info, paint, drawPerformanceInfo))
            {
                UpdateRotationAngle();
                RenderAsciiDonut(canvas, spectrum, info, paint);
                drawPerformanceInfo(canvas, info);
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Private Methods

        private void RenderAsciiDonut(SKCanvas canvas, float[] spectrum, SKImageInfo info, SKPaint paint)
        {
            float centerX = info.Width / 2, centerY = info.Height / 2, scale = Math.Min(centerX, centerY) / 2.0f;
            float[] donutVertices = GenerateDonutVertices(_segments, _donutRadius, _tubeRadius);
            float minDepth = -_tubeRadius + 3.0f, maxDepth = _tubeRadius + 3.0f;

            for (int i = 0; i < donutVertices.Length; i += 3)
            {
                float x = donutVertices[i], y = donutVertices[i + 1], z = donutVertices[i + 2];
                float rotatedX = x * (float)Math.Cos(_rotationAngle) - z * (float)Math.Sin(_rotationAngle);
                float rotatedZ = x * (float)Math.Sin(_rotationAngle) + z * (float)Math.Cos(_rotationAngle);

                float projX = rotatedX / (rotatedZ + 3.0f) * scale + centerX, projY = y / (rotatedZ + 3.0f) * scale + centerY;
                float depth = rotatedZ + 3.0f, normalizedDepth = (depth - minDepth) / (maxDepth - minDepth);
                int charIndex = Math.Clamp((int)(normalizedDepth * (_asciiChars.Length - 1)), 0, _asciiChars.Length - 1);
                char asciiChar = _asciiChars[charIndex];

                byte alpha = (byte)(Math.Clamp(normalizedDepth, 0.0f, 1.0f) * 255);
                paint.Color = paint.Color.WithAlpha(alpha);

                canvas.DrawText(asciiChar.ToString(), projX - 5, projY + 5, new SKFont { Size = 10 }, paint);
            }
        }

        private float[] GenerateDonutVertices(int segments, float donutRadius, float tubeRadius)
        {
            float[] vertices = new float[segments * segments * 3];
            for (int i = 0; i < segments; i++)
                for (int j = 0; j < segments; j++)
                {
                    float u = (float)i / segments * 2 * (float)Math.PI, v = (float)j / segments * 2 * (float)Math.PI;
                    int index = (i * segments + j) * 3;
                    vertices[index] = (donutRadius + tubeRadius * (float)Math.Cos(v)) * (float)Math.Cos(u);
                    vertices[index + 1] = (donutRadius + tubeRadius * (float)Math.Cos(v)) * (float)Math.Sin(u);
                    vertices[index + 2] = tubeRadius * (float)Math.Sin(v);
                }
            return vertices;
        }

        private void UpdateRotationAngle() => _rotationAngle = (_rotationAngle + 0.02f) % (2 * (float)Math.PI);

        private bool ValidateRenderParameters(SKCanvas? canvas, float[]? spectrum, SKImageInfo info, SKPaint? paint, Action<SKCanvas, SKImageInfo>? drawPerformanceInfo)
        {
            if (_isDisposed || info.Width <= 0 || info.Height <= 0 || !(canvas != null) || spectrum == null || paint == null || drawPerformanceInfo == null)
            {
                Log.Warning("Invalid render parameters or renderer not initialized.");
                return false;
            }
            return true;
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_isDisposed && disposing) { }
            _isDisposed = true;
            Log.Debug("AsciiDonutRenderer disposed");
        }

        #endregion
    }
}