#nullable enable

namespace SpectrumNet
{
    public class SphereRenderer : ISpectrumRenderer, IDisposable
    {
        private static SphereRenderer? _instance;
        private bool _isInitialized;
        private bool _isOverlayActive;
        private float _sphereRadius;
        private float _sphereSpacing;
        private int _sphereCount;
        private float[]? _cosValues;
        private float[]? _sinValues;
        private SKPaint? _spherePaint;

        // Константы
        private const float AngleStep = 360f;
        private const float PiOver180 = (float)(Math.PI / 180);
        private const float MinMagnitude = 0.01f;
        private const float MaxIntensityMultiplier = 1.5f;
        private const float CircleSizeMultiplier = 2f; // Изменения размера круга

        // Настройки для различных конфигураций
        private static readonly (float Radius, float Spacing, int Count) DefaultConfig = (20f * CircleSizeMultiplier, 10f, 8);
        private static readonly (float Radius, float Spacing, int Count) OverlayConfig = (10f * CircleSizeMultiplier, 5f, 16);

        private SphereRenderer() { }

        public static SphereRenderer GetInstance() => _instance ??= new SphereRenderer();

        public void Initialize()
        {
            if (_isInitialized) return;

            _spherePaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
            UpdateConfiguration(DefaultConfig); // Инициализация с конфигурацией по умолчанию
            _isInitialized = true;
            Log.Debug("SphereRenderer initialized");
        }

        public void Configure(bool isOverlayActive)
        {
            if (_isOverlayActive == isOverlayActive)
                return;

            _isOverlayActive = isOverlayActive;
            var config = isOverlayActive ? OverlayConfig : DefaultConfig;
            UpdateConfiguration(config);
        }

        private void UpdateConfiguration((float Radius, float Spacing, int Count) config)
        {
            (_sphereRadius, _sphereSpacing, _sphereCount) = config;
            PrecomputeTrigValues();
        }

        private void PrecomputeTrigValues()
        {
            _cosValues = new float[_sphereCount];
            _sinValues = new float[_sphereCount];
            for (int i = 0; i < _sphereCount; i++)
            {
                float angle = i * AngleStep / _sphereCount * PiOver180;
                _cosValues[i] = (float)Math.Cos(angle);
                _sinValues[i] = (float)Math.Sin(angle);
            }
        }

        public void Render(SKCanvas? canvas, float[]? spectrum, SKImageInfo info,
                           float unused1, float unused2, int unused3, SKPaint? paint,
                           Action<SKCanvas, SKImageInfo> drawPerformanceInfo)
        {
            if (!_isInitialized || _spherePaint == null || _cosValues == null || _sinValues == null || !AreRenderParamsValid(canvas, spectrum, info, paint))
            {
                Log.Warning("Invalid render parameters or uninitialized SphereRenderer.");
                return;
            }

            int sphereCount = Math.Min(spectrum!.Length / 2, _sphereCount);
            var scaledSpectrum = ScaleSpectrum(spectrum, sphereCount);

            RenderSpheres(canvas!, scaledSpectrum.AsSpan(), sphereCount, info.Width / 2f, info.Height / 2f, info.Height / 2f - (_sphereRadius + _sphereSpacing), paint!);

            // Отрисовка информации о производительности
            drawPerformanceInfo(canvas!, info);
        }

        private float[] ScaleSpectrum(float[] spectrum, int targetCount)
        {
            float[] scaledSpectrum = new float[targetCount];
            float blockSize = spectrum.Length / (2f * targetCount);

            for (int i = 0; i < targetCount; i++)
            {
                float sum = 0;
                for (int j = (int)(i * blockSize); j < (int)((i + 1) * blockSize); j++)
                    sum += spectrum[j];

                scaledSpectrum[i] = sum / blockSize;
            }
            return scaledSpectrum;
        }

        private void RenderSpheres(SKCanvas canvas, ReadOnlySpan<float> spectrum, int sphereCount,
                                   float centerX, float centerY, float maxRadius, SKPaint paint)
        {
            for (int i = 0; i < sphereCount; i++)
            {
                float magnitude = spectrum[i];
                if (magnitude < MinMagnitude) continue;

                using var clonedPaint = paint.Clone();
                float x = centerX + _cosValues![i] * maxRadius;
                float y = centerY + _sinValues![i] * maxRadius;
                clonedPaint.Color = clonedPaint.Color.WithAlpha((byte)(255 * Math.Min(magnitude * MaxIntensityMultiplier, 1.0f)));
                canvas.DrawCircle(x, y, magnitude * _sphereRadius, clonedPaint);
            }
        }

        private bool AreRenderParamsValid(SKCanvas? canvas, float[]? spectrum, SKImageInfo info, SKPaint? paint) =>
            canvas != null && spectrum != null && spectrum.Length > 0 && paint != null && info.Width > 0 && info.Height > 0;

        public void Dispose()
        {
            _spherePaint?.Dispose();
            _cosValues = null;
            _sinValues = null;
            _isInitialized = false;
            Log.Debug("SphereRenderer disposed");
        }
    }
}