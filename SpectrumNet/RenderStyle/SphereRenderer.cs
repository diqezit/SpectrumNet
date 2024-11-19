using Serilog;
using SkiaSharp;
using System;
using System.Runtime.CompilerServices;

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
        private const float MaxIntensityMultiplier = 1.5f;

        public SphereRenderer() { }

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
            Log.Debug("SphereRenderer initialized");
            _isInitialized = true;
        }

        public void Configure(bool isOverlayActive)
        {
            if (_isOverlayActive != isOverlayActive)
            {
                _isOverlayActive = isOverlayActive;
                UpdateConfiguration(isOverlayActive);
            }
        }

        private void UpdateConfiguration(bool isOverlayActive)
        {
            _sphereRadius = isOverlayActive ? 10f : 20f;
            _sphereSpacing = isOverlayActive ? 5f : 10f;
            _sphereCount = isOverlayActive ? 16 : 8;

            PrecomputeTrigValues();
        }

        private void PrecomputeTrigValues()
        {
            _cosValues = new float[_sphereCount];
            _sinValues = new float[_sphereCount];

            for (int i = 0; i < _sphereCount; i++)
            {
                float angle = (float)i / _sphereCount * 360f * (float)(Math.PI / 180);
                _cosValues[i] = (float)Math.Cos(angle);
                _sinValues[i] = (float)Math.Sin(angle);
            }
        }

        private bool AreRenderParamsValid(SKCanvas? canvas, ReadOnlySpan<float> spectrum, SKImageInfo info, SKPaint? paint)
        {
            if (canvas == null || spectrum.IsEmpty || paint == null || info.Width <= 0 || info.Height <= 0)
            {
                Log.Warning("Invalid render parameters");
                return false;
            }
            return true;
        }

        public void Render(SKCanvas? canvas, float[]? spectrum, SKImageInfo info,
                         float unused1, float unused2, int unused3, SKPaint? basePaint)
        {
            if (!_isInitialized || _spherePaint == null || _cosValues == null || _sinValues == null)
            {
                Log.Warning("SphereRenderer is not initialized.");
                return;
            }

            if (!AreRenderParamsValid(canvas, spectrum.AsSpan(), info, basePaint)) return;

            int actualSphereCount = Math.Min(spectrum!.Length / 2, _sphereCount);
            float centerX = info.Width / 2f;
            float centerY = info.Height / 2f;
            float totalSphereRadius = _sphereRadius + _sphereSpacing;
            float maxRadius = centerY - totalSphereRadius;

            RenderSpheres(canvas!, spectrum.AsSpan(), actualSphereCount, centerX, centerY,
                         maxRadius, basePaint!);
        }

        private void RenderSpheres(SKCanvas canvas, ReadOnlySpan<float> spectrum,
                                 int sphereCount, float centerX, float centerY,
                                 float maxRadius, SKPaint basePaint)
        {
            if (_spherePaint == null || _cosValues == null || _sinValues == null) return;

            for (int i = 0; i < sphereCount; i++)
            {
                float magnitude = spectrum[i];
                if (magnitude < 0.01f) continue; // Пропускаем сферы с очень низкой интенсивностью

                using (var clonedPaint = basePaint.Clone())
                {
                    RenderSphere(canvas, i, magnitude, centerX, centerY, maxRadius, clonedPaint);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RenderSphere(SKCanvas canvas, int index, float magnitude,
                                float centerX, float centerY, float maxRadius,
                                SKPaint basePaint)
        {
            if (_spherePaint == null || _cosValues == null || _sinValues == null) return;

            float x = centerX + _cosValues[index] * maxRadius;
            float y = centerY + _sinValues[index] * maxRadius;
            float sphereHeight = magnitude * maxRadius;

            basePaint.Color = basePaint.Color.WithAlpha(
                (byte)(255 * Math.Min(magnitude * MaxIntensityMultiplier, 1.0f))
            );

            canvas.DrawCircle(x, y, sphereHeight, basePaint);
        }

        public void Dispose()
        {
            _spherePaint?.Dispose();
        }
    }
}