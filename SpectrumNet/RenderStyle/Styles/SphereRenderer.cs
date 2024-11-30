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

        // Constants
        private const float OverlaySphereRadius = 10f;
        private const float DefaultSphereRadius = 20f;
        private const float OverlaySphereSpacing = 5f;
        private const float DefaultSphereSpacing = 10f;
        private const int OverlaySphereCount = 16;
        private const int DefaultSphereCount = 8;
        private const float AngleStep = 360f;
        private const float PiOver180 = (float)(Math.PI / 180);
        private const float MinMagnitude = 0.01f;
        private const float MaxIntensityMultiplier = 1.5f;

        private SphereRenderer() { }

        public static SphereRenderer GetInstance() => _instance ??= new SphereRenderer();

        public void Initialize()
        {
            if (_isInitialized) return;

            _spherePaint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Fill
            };

            UpdateConfiguration(false);
            _isInitialized = true;
            Log.Debug("SphereRenderer initialized");
        }

        public void Configure(bool isOverlayActive)
        {
            if (_isOverlayActive != isOverlayActive)
            {
                _isOverlayActive = isOverlayActive;
                UpdateConfiguration(isOverlayActive);
                Log.Debug($"SphereRenderer reconfigured. Overlay active: {isOverlayActive}");
            }
        }

        private void UpdateConfiguration(bool isOverlayActive)
        {
            _sphereRadius = isOverlayActive ? OverlaySphereRadius : DefaultSphereRadius;
            _sphereSpacing = isOverlayActive ? OverlaySphereSpacing : DefaultSphereSpacing;
            _sphereCount = isOverlayActive ? OverlaySphereCount : DefaultSphereCount;

            PrecomputeTrigValues();
        }

        private void PrecomputeTrigValues()
        {
            _cosValues = new float[_sphereCount];
            _sinValues = new float[_sphereCount];

            for (int i = 0; i < _sphereCount; i++)
            {
                float angle = (float)i / _sphereCount * AngleStep * PiOver180;
                _cosValues[i] = (float)Math.Cos(angle);
                _sinValues[i] = (float)Math.Sin(angle);
            }
        }

        private bool AreRenderParamsValid(SKCanvas? canvas, ReadOnlySpan<float> spectrum, SKImageInfo info, SKPaint? paint)
        {
            return canvas != null && !spectrum.IsEmpty && paint != null && info.Width > 0 && info.Height > 0;
        }

        public void Render(SKCanvas? canvas, float[]? spectrum, SKImageInfo info,
                           float unused1, float unused2, int unused3, SKPaint? paint)
        {
            if (!_isInitialized || _spherePaint == null || _cosValues == null || _sinValues == null)
            {
                Log.Warning("SphereRenderer is not initialized or improperly configured.");
                return;
            }

            if (!AreRenderParamsValid(canvas, spectrum.AsSpan(), info, paint)) return;

            int actualSphereCount = Math.Min(spectrum!.Length / 2, _sphereCount);
            float[] scaledSpectrum = ScaleSpectrum(spectrum, actualSphereCount);

            float centerX = info.Width / 2f;
            float centerY = info.Height / 2f;
            float totalSphereRadius = _sphereRadius + _sphereSpacing;
            float maxRadius = centerY - totalSphereRadius;

            RenderSpheres(canvas!, scaledSpectrum.AsSpan(), actualSphereCount, centerX, centerY, maxRadius, paint!);
        }

        private float[] ScaleSpectrum(float[] spectrum, int targetCount)
        {
            int spectrumLength = spectrum.Length / 2;
            float[] scaledSpectrum = new float[targetCount];
            float blockSize = (float)spectrumLength / targetCount;

            for (int i = 0; i < targetCount; i++)
            {
                float sum = 0;
                int start = (int)(i * blockSize);
                int end = (int)((i + 1) * blockSize);

                for (int j = start; j < end && j < spectrumLength; j++)
                {
                    sum += spectrum[j];
                }

                scaledSpectrum[i] = sum / (end - start); // Усреднение значений в блоке
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
                RenderSphere(canvas, i, magnitude, centerX, centerY, maxRadius, clonedPaint);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RenderSphere(SKCanvas canvas, int index, float magnitude,
                                  float centerX, float centerY, float maxRadius, SKPaint paint)
        {
            float x = centerX + _cosValues![index] * maxRadius;
            float y = centerY + _sinValues![index] * maxRadius;
            float sphereHeight = magnitude * _sphereRadius;

            paint.Color = paint.Color.WithAlpha((byte)(255 * Math.Min(magnitude * MaxIntensityMultiplier, 1.0f)));

            canvas.DrawCircle(x, y, sphereHeight, paint);
        }

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