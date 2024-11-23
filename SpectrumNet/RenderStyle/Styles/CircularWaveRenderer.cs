namespace SpectrumNet
{
    public class CircularWaveRenderer : ISpectrumRenderer
    {
        private static CircularWaveRenderer? _instance;
        private bool _isInitialized;
        private readonly SKPath _path = new();
        private float _rotation;
        private const float RotationSpeed = 0.5f;
        private const float RadiusProportion = 0.4f;
        private const int MaxPointCount = 180;
        private const float AmplitudeScale = 0.5f;

        private CircularWaveRenderer() { }

        public static CircularWaveRenderer GetInstance() => _instance ??= new CircularWaveRenderer();

        public void Initialize()
        {
            if (_isInitialized) return;
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
            if (!_isInitialized)
                return;

            if (!AreRenderParamsValid(canvas, spectrum.AsSpan(), info, paint))
                return;

            int pointCount = Math.Min(spectrum!.Length / 2, MaxPointCount);
            float radius = Math.Min(info.Width, info.Height) * RadiusProportion;
            float centerX = info.Width / 2f;
            float centerY = info.Height / 2f;

            using var renderPaint = paint!.Clone();
            RenderCircularWave(canvas!, spectrum.AsSpan(), pointCount, radius, centerX, centerY, renderPaint);

            _rotation += RotationSpeed;
            if (_rotation >= 360f) _rotation -= 360f;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RenderCircularWave(SKCanvas canvas, ReadOnlySpan<float> spectrum, int pointCount,
                                      float radius, float centerX, float centerY, SKPaint paint)
        {
            _path.Reset();
            float angleStep = 360f / pointCount;

            for (int i = 0; i <= pointCount; i++)
            {
                float angle = (i * angleStep + _rotation) * (MathF.PI / 180f);
                float amplitude = i < pointCount ? spectrum[i] : spectrum[0];
                float r = radius * (1f + amplitude * AmplitudeScale);

                float x = centerX + r * MathF.Cos(angle);
                float y = centerY + r * MathF.Sin(angle);

                if (i == 0)
                    _path.MoveTo(x, y);
                else
                    _path.LineTo(x, y);
            }

            _path.Close();
            canvas.DrawPath(_path, paint);
        }

        public void Dispose()
        {
            _path.Dispose();
        }
    }
}