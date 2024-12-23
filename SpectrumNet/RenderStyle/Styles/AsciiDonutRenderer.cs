#nullable enable

namespace SpectrumNet
{
    public sealed class AsciiDonutRenderer : ISpectrumRenderer, IDisposable
    {
        #region Constants

        private record struct Vertex(float X, float Y, float Z);
        private struct ProjectedVertex { public float X, Y, Depth, LightIntensity; }

        private const int Segments = 60;
        private const float DonutRadius = 1.5f, TubeRadius = 0.6f;
        private const float RotationSpeedX = 0.01f, RotationSpeedY = 0.02f, RotationSpeedZ = 0.015f;
        private const float DepthOffset = 6.0f, CharOffsetX = 4f, CharOffsetY = 4f, FontSize = 10f, DonutScale = 1.85f;
        private static readonly Vector3 LightDirection = Vector3.Normalize(new(0.5f, 0.5f, -1.0f));
        private static readonly char[] AsciiChars = " .:-=+*#%@".ToCharArray();
        private static readonly string[] AsciiCharStrings = AsciiChars.Select(c => c.ToString()).ToArray();
        private static readonly float[] CosTable = new float[Segments], SinTable = new float[Segments];

        #endregion

        #region Fields

        private readonly byte[] _alphaCache;
        private readonly Vertex[] _vertices;
        private readonly ProjectedVertex[] _projectedVertices;
        private readonly SKFont _font;
        private float _rotationAngleX, _rotationAngleY, _rotationAngleZ, _currentRotationIntensity = 1.0f;
        private bool _isInitialized, _isDisposed;
        private float[] _spectrum = Array.Empty<float>();
        private int _currentBarCount;
        private Matrix4x4 _rotationMatrix;

        private static readonly Lazy<AsciiDonutRenderer> LazyInstance = new(() => new(), LazyThreadSafetyMode.ExecutionAndPublication);

        #endregion

        #region Constructor

        public static AsciiDonutRenderer GetInstance() => LazyInstance.Value;

        private AsciiDonutRenderer()
        {
            for (int i = 0; i < Segments; i++)
            {
                float angle = i * MathF.PI * 2 / Segments;
                CosTable[i] = MathF.Cos(angle);
                SinTable[i] = MathF.Sin(angle);
            }

            _vertices = GenerateDonutVertices();
            _projectedVertices = new ProjectedVertex[_vertices.Length];
            _font = new SKFont { Size = FontSize };
            _alphaCache = Enumerable.Range(0, AsciiChars.Length)
                .Select(i => (byte)((0.2f + 0.6f * (i / (float)(AsciiChars.Length - 1))) * 255))
                .ToArray();
        }

        #endregion

        #region Public Methods

        public void Initialize() => _isInitialized = true;
        public void Configure(bool isOverlayActive) { }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Render(SKCanvas canvas, float[] spectrum, SKImageInfo info, float barWidth,
            float barSpacing, int barCount, SKPaint paint, Action<SKCanvas, SKImageInfo> drawPerformanceInfo)
        {
            if (_isDisposed || info.Width <= 0 || info.Height <= 0 || canvas == null || spectrum == null ||
                paint == null || drawPerformanceInfo == null) return;

            (_spectrum, _currentBarCount) = (spectrum, barCount);
            _currentRotationIntensity = spectrum.Length > 0 ? 0.5f + (spectrum.Average() * 1.5f) : 1.0f;
            UpdateRotationAngles();
            _rotationMatrix = CreateRotationMatrix();
            RenderDonut(canvas, info, paint);
            drawPerformanceInfo(canvas, info);
        }

        public void Dispose()
        {
            if (!_isDisposed) { _isDisposed = true; _font.Dispose(); }
        }

        #endregion

        #region Private Methods

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private void RenderDonut(SKCanvas canvas, SKImageInfo info, SKPaint paint)
        {
            float centerX = info.Width * 0.5f, centerY = info.Height * 0.5f;
            float scale = MathF.Min(centerX, centerY) * DonutScale * (1f + MathF.Log2(_currentBarCount + 1) * 0.1f);

            ProjectVertices(scale, centerX, centerY);
            Array.Sort(_projectedVertices, 0, _vertices.Length, Comparer<ProjectedVertex>.Create((a, b) => b.Depth.CompareTo(a.Depth)));

            float minZ = _projectedVertices[0].Depth, maxZ = _projectedVertices[^1].Depth;
            float depthRange = maxZ - minZ + float.Epsilon;
            float maxSpectrum = _spectrum.Length > 0 ? _spectrum.Max() : 1f;
            float alphaMultiplier = 1f + MathF.Log2(_currentBarCount + 1) * 0.2f;

            var originalColor = paint.Color;

            foreach (ref var vertex in _projectedVertices.AsSpan())
            {
                float normalizedDepth = (vertex.Depth - minZ) / depthRange;
                if (normalizedDepth < 0 || normalizedDepth > 1) continue;

                byte alpha = (byte)Math.Clamp(
                    _alphaCache[(int)(vertex.LightIntensity * (AsciiChars.Length - 1))] *
                    (0.5f + maxSpectrum * 0.5f) * alphaMultiplier * normalizedDepth,
                    0, 255);

                paint.Color = originalColor.WithAlpha(alpha);
                canvas.DrawText(
                    AsciiCharStrings[(int)(vertex.LightIntensity * (AsciiChars.Length - 1))],
                    vertex.X - CharOffsetX,
                    vertex.Y + CharOffsetY,
                    _font,
                    paint);
            }
            paint.Color = originalColor;
        }

        private static Vertex[] GenerateDonutVertices() =>
            Enumerable.Range(0, Segments).SelectMany(i =>
                Enumerable.Range(0, Segments).Select(j =>
                {
                    float r = DonutRadius + TubeRadius * CosTable[j];
                    return new Vertex(
                        r * CosTable[i],
                        r * SinTable[i],
                        TubeRadius * SinTable[j]);
                })).ToArray();

        private void UpdateRotationAngles()
        {
            var speeds = (_spectrum?.Length > 0)
                ? GetSpectralRotationSpeeds()
                : (RotationSpeedX, RotationSpeedY, RotationSpeedZ);

            _rotationAngleX = (_rotationAngleX + speeds.Item1 * _currentRotationIntensity) % (MathF.PI * 2);
            _rotationAngleY = (_rotationAngleY + speeds.Item2 * _currentRotationIntensity) % (MathF.PI * 2);
            _rotationAngleZ = (_rotationAngleZ + speeds.Item3 * _currentRotationIntensity) % (MathF.PI * 2);
        }

        private (float, float, float) GetSpectralRotationSpeeds()
        {
            int segmentSize = (_spectrum.Length / 2) / _currentBarCount;
            var freqs = _spectrum.Chunk(segmentSize)
                .Take(3)
                .Select(chunk => chunk.Average())
                .ToArray();

            float multiplier = MathF.Log(_currentBarCount + 1, 2);
            return (
                RotationSpeedX * (1f + freqs[0] * 2f * multiplier),
                RotationSpeedY * (1f + freqs[1] * 2f * multiplier),
                RotationSpeedZ * (1f + freqs[2] * 2f * multiplier)
            );
        }

        private void ProjectVertices(float scale, float centerX, float centerY)
        {
            Parallel.For(0, _vertices.Length, i =>
            {
                var vertex = _vertices[i];
                var rotated = Vector3.Transform(new Vector3(vertex.X, vertex.Y, vertex.Z), _rotationMatrix);

                float invDepth = 1 / (rotated.Z + DepthOffset);
                float lightIntensity = Vector3.Dot(Vector3.Normalize(rotated), LightDirection) * 0.5f + 0.5f;

                _projectedVertices[i] = new ProjectedVertex
                {
                    X = rotated.X * scale * invDepth + centerX,
                    Y = rotated.Y * scale * invDepth + centerY,
                    Depth = rotated.Z + DepthOffset,
                    LightIntensity = lightIntensity
                };
            });
        }

        private Matrix4x4 CreateRotationMatrix() =>
            Matrix4x4.CreateRotationX(_rotationAngleX) *
            Matrix4x4.CreateRotationY(_rotationAngleY) *
            Matrix4x4.CreateRotationZ(_rotationAngleZ);

        #endregion
    }
}