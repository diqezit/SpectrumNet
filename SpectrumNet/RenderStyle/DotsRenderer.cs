using Serilog;
using SkiaSharp;
using System;
using System.Runtime.CompilerServices;

namespace SpectrumNet
{
    public class DotsRenderer : ISpectrumRenderer, IDisposable
    {
        private static DotsRenderer? _instance;
        private bool _isInitialized;

        public DotsRenderer() { }

        public static DotsRenderer GetInstance() => _instance ??= new DotsRenderer();

        public void Initialize()
        {
            if (_isInitialized) return;

            Log.Debug("DotsRenderer initialized");
            _isInitialized = true;
        }

        public void Configure(bool isOverlayActive)
        {
            // Конфигурация не требуется для этого рендерера
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
                         float barWidth, float barSpacing, int barCount, SKPaint? basePaint)
        {
            if (!_isInitialized)
            {
                Log.Warning("DotsRenderer is not initialized.");
                return;
            }

            if (!AreRenderParamsValid(canvas, spectrum.AsSpan(), info, basePaint)) return;

            float totalWidth = barWidth + barSpacing;
            int halfLength = spectrum!.Length / 2;

            RenderDots(canvas!, spectrum.AsSpan(), info, totalWidth, barWidth, halfLength, basePaint!);
        }

        private void RenderDots(SKCanvas canvas, ReadOnlySpan<float> spectrum, SKImageInfo info,
                              float totalWidth, float barWidth, int halfLength, SKPaint basePaint)
        {
            for (int i = 0; i < halfLength; i++)
            {
                float intensity = spectrum[i];
                if (intensity < 0.01f) continue; // Skip dots with very low intensity

                float dotRadius = Math.Max(barWidth / 2 * intensity, 2);
                float x = i * totalWidth + dotRadius;
                float y = info.Height - (intensity * info.Height);

                RenderMainDot(canvas, x, y, dotRadius, intensity, basePaint);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RenderMainDot(SKCanvas canvas, float x, float y, float dotRadius,
                                 float intensity, SKPaint basePaint)
        {
            using var dotPaint = basePaint.Clone();
            dotPaint.Color = basePaint.Color.WithAlpha((byte)(255 * intensity));
            canvas.DrawCircle(x, y, dotRadius, dotPaint);
        }

        public void Dispose()
        {
            // No paints to dispose as glow paints are removed
        }
    }
}