namespace SpectrumNet
{
    public class CubesRenderer : ISpectrumRenderer, IDisposable
    {
        private static CubesRenderer? _instance;
        private bool _isInitialized;
        private readonly SKPath _cubePath = new();
        private SKPaint? _cubePaint;
        private const float MinMagnitudeThreshold = 0.01f;
        private const float CubeTopWidthProportion = 0.75f;
        private const float CubeTopHeightProportion = 0.25f;

        public CubesRenderer() { }

        public static CubesRenderer GetInstance() => _instance ??= new CubesRenderer();

        public void Initialize()
        {
            if (_isInitialized) return;

            _cubePaint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Fill
            };

            _isInitialized = true;
        }

        public void Configure(bool isOverlayActive) { }

        private bool AreRenderParamsValid(SKCanvas? canvas, ReadOnlySpan<float> spectrum, SKImageInfo info, SKPaint? paint)
        {
            if (canvas == null || spectrum.IsEmpty || paint == null || info.Width <= 0 || info.Height <= 0)
                return false;
            return true;
        }

        public void Render(SKCanvas? canvas, float[]? spectrum, SKImageInfo info,
                         float barWidth, float barSpacing, int barCount, SKPaint? paint)
        {
            if (!_isInitialized || _cubePaint == null)
                return;

            if (!AreRenderParamsValid(canvas, spectrum.AsSpan(), info, paint))
                return;

            int actualBarCount = Math.Min(spectrum!.Length / 2, barCount);
            float totalWidth = barWidth + barSpacing;

            RenderCubes(canvas!, spectrum.AsSpan(), info, actualBarCount, totalWidth, barWidth, paint!);
        }

        private void RenderCubes(SKCanvas canvas, ReadOnlySpan<float> spectrum, SKImageInfo info,
                               int barCount, float totalWidth, float barWidth, SKPaint paint)
        {
            for (int i = 0; i < barCount; i++)
            {
                float magnitude = spectrum[i];
                if (magnitude < MinMagnitudeThreshold) continue;

                float height = magnitude * info.Height;
                float x = i * totalWidth;
                float y = info.Height - height;

                using (var clonedPaint = paint.Clone())
                {
                    RenderCube(canvas, x, y, barWidth, height, magnitude, clonedPaint);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RenderCube(SKCanvas canvas, float x, float y, float barWidth,
                              float height, float magnitude, SKPaint paint)
        {
            paint.Color = paint.Color.WithAlpha((byte)(magnitude * 255));
            canvas.DrawRect(x, y, barWidth, height, paint);
            RenderCubeTop(canvas, x, y, barWidth, magnitude, paint);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RenderCubeTop(SKCanvas canvas, float x, float y, float barWidth, float magnitude, SKPaint paint)
        {
            _cubePath.Reset();
            _cubePath.MoveTo(x, y);
            _cubePath.LineTo(x + barWidth, y);
            _cubePath.LineTo(x + barWidth * CubeTopWidthProportion, y - barWidth * CubeTopHeightProportion);
            _cubePath.LineTo(x - barWidth * CubeTopHeightProportion, y - barWidth * CubeTopHeightProportion);
            _cubePath.Close();
            paint.Color = paint.Color.WithAlpha((byte)(magnitude * 200));
            canvas.DrawPath(_cubePath, paint);
        }

        public void Dispose()
        {
            _cubePath.Dispose();
            _cubePaint?.Dispose();
        }
    }
}