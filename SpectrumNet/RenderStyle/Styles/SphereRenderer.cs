#nullable enable

namespace SpectrumNet
{
    public class SphereRenderer : ISpectrumRenderer, IDisposable
    {
        #region Fields
        private static SphereRenderer? _instance;
        private bool _isInitialized;
        private bool _isOverlayActive;
        private float _sphereRadius, _sphereSpacing;
        private int _sphereCount;
        private float[]? _cosValues, _sinValues, _currentAlphas;
        private SKPaint? _spherePaint;
        #endregion

        #region Constants
        private const float PiOver180 = (float)(Math.PI / 180);
        private const float MinMagnitude = 0.01f, MaxIntensityMultiplier = 3f, AlphaSmoothingFactor = 0.2f, MinAlpha = 0.1f;
        private const float DefaultRadius = 40f, DefaultSpacing = 10f;
        private const int DefaultCount = 8;
        #endregion

        #region Static Configurations
        private static readonly (float Radius, float Spacing, int Count) DefaultConfig = (DefaultRadius, DefaultSpacing, DefaultCount);
        private static readonly (float Radius, float Spacing, int Count) OverlayConfig = (20f, 5f, 16);
        #endregion

        #region Constructor
        private SphereRenderer() { }
        public static SphereRenderer GetInstance() => _instance ??= new SphereRenderer();
        #endregion

        #region Public Methods
        public void Initialize()
        {
            if (_isInitialized) return;
            _spherePaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
            UpdateConfiguration(DefaultConfig);
            _isInitialized = true;
        }

        public void Configure(bool isOverlayActive)
        {
            if (_isOverlayActive == isOverlayActive) return;
            _isOverlayActive = isOverlayActive;
            UpdateConfiguration(isOverlayActive ? OverlayConfig : DefaultConfig);
        }

        public void Render(SKCanvas canvas, float[]? spectrum, SKImageInfo info, float barWidth, float barSpacing, int barCount, SKPaint paint, Action<SKCanvas, SKImageInfo> drawPerformanceInfo)
        {
            if (!_isInitialized || !AreRenderParamsValid(canvas, spectrum, info, paint)) return;

            AdjustConfiguration(barCount, barSpacing, info.Width, info.Height);
            int sphereCount = Math.Min(spectrum!.Length / 2, _sphereCount);
            float[] scaledSpectrum = ArrayPool<float>.Shared.Rent(sphereCount);

            try
            {
                ScaleSpectrum(spectrum, scaledSpectrum, sphereCount);
                UpdateAlphas(scaledSpectrum.AsSpan(0, sphereCount));
                RenderSpheres(canvas, scaledSpectrum.AsSpan(0, sphereCount), info.Width / 2f, info.Height / 2f, info.Height / 2f - (_sphereRadius + _sphereSpacing), paint);
                drawPerformanceInfo(canvas, info);
            }
            finally
            {
                ArrayPool<float>.Shared.Return(scaledSpectrum);
            }
        }

        public void Dispose()
        {
            _spherePaint?.Dispose();
            _cosValues = _sinValues = _currentAlphas = null;
            _isInitialized = false;
        }
        #endregion

        #region Private Methods
        private void UpdateConfiguration((float Radius, float Spacing, int Count) config)
        {
            (_sphereRadius, _sphereSpacing, _sphereCount) = config;
            _currentAlphas = new float[_sphereCount];
            PrecomputeTrigValues();
        }

        private void AdjustConfiguration(int barCount, float barSpacing, int canvasWidth, int canvasHeight)
        {
            _sphereRadius = Math.Max(5f, DefaultRadius - barCount * 0.2f + barSpacing * 0.5f);
            _sphereSpacing = Math.Max(2f, DefaultSpacing - barCount * 0.1f + barSpacing * 0.3f);
            _sphereCount = Math.Clamp(barCount / 2, 4, 64);
            float maxRadius = Math.Min(canvasWidth, canvasHeight) / 2f - (_sphereRadius + _sphereSpacing);
            if (_sphereRadius > maxRadius) _sphereRadius = maxRadius;
            _currentAlphas = new float[_sphereCount];
            PrecomputeTrigValues();
        }

        private void PrecomputeTrigValues()
        {
            _cosValues = new float[_sphereCount];
            _sinValues = new float[_sphereCount];
            float angleStepRad = 360f / _sphereCount * PiOver180;

            for (int i = 0; i < _sphereCount; i++)
            {
                float angle = i * angleStepRad;
                _cosValues[i] = MathF.Cos(angle);
                _sinValues[i] = MathF.Sin(angle);
            }
        }

        private static void ScaleSpectrum(float[] source, float[] target, int targetCount)
        {
            float blockSize = source.Length / (2f * targetCount);
            for (int i = 0; i < targetCount; i++)
            {
                float sum = 0;
                int start = (int)(i * blockSize), end = (int)((i + 1) * blockSize);
                for (int j = start; j < end; j++) sum += source[j];
                target[i] = sum / blockSize;
            }
        }

        private void UpdateAlphas(ReadOnlySpan<float> spectrum)
        {
            for (int i = 0; i < spectrum.Length; i++)
            {
                float targetAlpha = MathF.Max(MinAlpha, spectrum[i] * MaxIntensityMultiplier);
                _currentAlphas![i] = _currentAlphas[i] + (targetAlpha - _currentAlphas[i]) * AlphaSmoothingFactor;
            }
        }

        private void RenderSpheres(SKCanvas canvas, ReadOnlySpan<float> spectrum, float centerX, float centerY, float maxRadius, SKPaint paint)
        {
            for (int i = 0; i < spectrum.Length; i++)
            {
                float magnitude = spectrum[i];
                if (magnitude < MinMagnitude) continue;

                float x = centerX + _cosValues![i] * maxRadius;
                float y = centerY + _sinValues![i] * maxRadius;
                float alpha = MathF.Min(_currentAlphas![i], 1.0f);

                paint.Color = paint.Color.WithAlpha((byte)(255 * alpha));
                float circleSize = MathF.Max(magnitude * _sphereRadius, 2f) + _sphereSpacing * 0.2f;
                canvas.DrawCircle(x, y, circleSize, paint);
            }
        }

        private static bool AreRenderParamsValid(SKCanvas? canvas, float[]? spectrum, SKImageInfo info, SKPaint? paint) =>
            canvas != null && spectrum != null && spectrum.Length > 0 && paint != null && info.Width > 0 && info.Height > 0;
        #endregion
    }
}