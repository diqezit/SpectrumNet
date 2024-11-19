using Serilog;
using SkiaSharp;
using System.Runtime.CompilerServices;

namespace SpectrumNet
{
    public class BarsRenderer : ISpectrumRenderer
    {
        private static BarsRenderer? _instance;
        private bool _isInitialized;
        private readonly SKPath _path = new();

        private BarsRenderer() { }

        public static BarsRenderer GetInstance() => _instance ??= new BarsRenderer();

        public void Initialize()
        {
            if (_isInitialized) return;
            Log.Debug("BarsRenderer initialized");
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
                Log.Warning("BarsRenderer is not initialized.");
                return;
            }

            if (!AreRenderParamsValid(canvas, spectrum.AsSpan(), info, basePaint)) return;

            int actualBarCount = Math.Min(spectrum!.Length / 2, barCount);
            float totalBarWidth = barWidth + barSpacing;
            float cornerRadius = Math.Min(barWidth / 2, 10);

            using var barPaint = basePaint!.Clone();
            using var highlightPaint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Fill,
                Color = SKColors.White
            };

            RenderBars(canvas!, spectrum.AsSpan(), actualBarCount, totalBarWidth, barWidth,
                      cornerRadius, info.Height, barPaint, highlightPaint);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RenderBars(SKCanvas canvas, ReadOnlySpan<float> spectrum, int barCount,
                              float totalBarWidth, float barWidth, float cornerRadius,
                              float canvasHeight, SKPaint barPaint, SKPaint highlightPaint)
        {
            for (int i = 0; i < barCount; i++)
            {
                float barHeight = Math.Max(spectrum[i] * canvasHeight, 1);
                float x = i * totalBarWidth;

                RenderBar(canvas, x, barWidth, barHeight, canvasHeight, cornerRadius, barPaint);
                RenderHighlight(canvas, x, barWidth, barHeight, canvasHeight, cornerRadius, highlightPaint);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RenderBar(SKCanvas canvas, float x, float barWidth, float barHeight,
                             float canvasHeight, float cornerRadius, SKPaint barPaint)
        {
            _path.Reset();
            _path.AddRoundRect(new SKRoundRect(
                new SKRect(x, canvasHeight - barHeight, x + barWidth, canvasHeight),
                cornerRadius, 0
            ));

            byte alpha = (byte)(255 * Math.Min(barHeight / canvasHeight * 1.5f, 1.0f));
            barPaint.Color = barPaint.Color.WithAlpha(alpha);

            canvas.DrawPath(_path, barPaint);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RenderHighlight(SKCanvas canvas, float x, float barWidth, float barHeight,
                                   float canvasHeight, float cornerRadius, SKPaint highlightPaint)
        {
            if (barHeight <= cornerRadius * 2) return;

            float highlightWidth = barWidth * 0.6f;
            float highlightHeight = Math.Min(barHeight * 0.1f, 5);

            byte alpha = (byte)(255 * Math.Min(barHeight / canvasHeight * 1.5f, 1.0f) / 3);
            highlightPaint.Color = highlightPaint.Color.WithAlpha(alpha);

            canvas.DrawRect(
                x + (barWidth - highlightWidth) / 2,
                canvasHeight - barHeight + cornerRadius,
                highlightWidth,
                highlightHeight,
                highlightPaint
            );
        }

        public void Dispose()
        {
            _path.Dispose();
        }
    }
}