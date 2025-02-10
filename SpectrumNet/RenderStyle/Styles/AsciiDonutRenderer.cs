#nullable enable
namespace SpectrumNet
{
    public sealed class AsciiDonutRenderer : ISpectrumRenderer, IDisposable
    {
        #region Constants

        private readonly record struct Vertex(float X, float Y, float Z);
        private struct ProjectedVertex { public float X, Y, Depth, LightIntensity; }

        private const int Segments = 60;
        private const float DonutRadius = 1.5f, TubeRadius = 0.6f;
        private const float RotationSpeedX = 0.01f, RotationSpeedY = 0.02f, RotationSpeedZ = 0.015f;
        private const float DepthOffset = 6.0f, CharOffsetX = 4f, CharOffsetY = 4f, FontSize = 10f, DonutScale = 1.85f;
        private static readonly Vector3 LightDirection = Vector3.Normalize(new Vector3(0.5f, 0.5f, -1.0f));
        private static readonly char[] AsciiChars = " .:-=+*#%@".ToCharArray();
        private static readonly string[] AsciiCharStrings = new string[AsciiChars.Length];
        private static readonly float[] CosTable = new float[Segments], SinTable = new float[Segments];

        #endregion

        #region Fields

        private readonly byte[] _alphaCache;
        private readonly Vertex[] _vertices;
        private readonly ProjectedVertex[] _projectedVertices;
        private readonly SKFont _font;
        private float _rotationAngleX, _rotationAngleY, _rotationAngleZ, _currentRotationIntensity = 1.0f;
        private bool _isInitialized, _isDisposed;
        private float[] _spectrum = System.Array.Empty<float>();
        private int _currentBarCount;
        private Matrix4x4 _rotationMatrix;

        private static readonly Lazy<AsciiDonutRenderer> LazyInstance = new Lazy<AsciiDonutRenderer>(
            () => new AsciiDonutRenderer(), System.Threading.LazyThreadSafetyMode.ExecutionAndPublication);

        #endregion

        #region Constructor

        public static AsciiDonutRenderer GetInstance() => LazyInstance.Value;

        private AsciiDonutRenderer()
        {
            // Инициализация таблиц синусов и косинусов
            for (int i = 0; i < Segments; i++)
            {
                float angle = i * MathF.PI * 2f / Segments;
                CosTable[i] = MathF.Cos(angle);
                SinTable[i] = MathF.Sin(angle);
            }
            // Инициализация строкового массива для символов ASCII
            for (int i = 0; i < AsciiChars.Length; i++)
            {
                AsciiCharStrings[i] = AsciiChars[i].ToString();
            }
            // Генерация вершин пончика без использования LINQ
            _vertices = new Vertex[Segments * Segments];
            int idx = 0;
            for (int i = 0; i < Segments; i++)
            {
                for (int j = 0; j < Segments; j++)
                {
                    float r = DonutRadius + TubeRadius * CosTable[j];
                    _vertices[idx++] = new Vertex(r * CosTable[i], r * SinTable[i], TubeRadius * SinTable[j]);
                }
            }
            _projectedVertices = new ProjectedVertex[_vertices.Length];
            _font = new SKFont { Size = FontSize };
            // Инициализация кэша альфа-каналов для символов ASCII
            _alphaCache = new byte[AsciiChars.Length];
            for (int i = 0; i < AsciiChars.Length; i++)
            {
                _alphaCache[i] = (byte)((0.2f + 0.6f * (i / (float)(AsciiChars.Length - 1))) * 255);
            }
        }

        #endregion

        #region Public Methods

        public void Initialize() => _isInitialized = true;
        public void Configure(bool isOverlayActive) { }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Render(SKCanvas canvas, float[] spectrum, SKImageInfo info, float barWidth,
            float barSpacing, int barCount, SKPaint paint, Action<SKCanvas, SKImageInfo> drawPerformanceInfo)
        {
            if (_isDisposed || info.Width <= 0 || info.Height <= 0 || canvas is null || spectrum is null ||
                paint is null || drawPerformanceInfo is null)
            {
                return;
            }

            _spectrum = spectrum;
            _currentBarCount = barCount;

            // Ручной расчет среднего и максимального значений спектра для повышения производительности
            float sum = 0f, maxSpectrum = 0f;
            for (int i = 0, len = spectrum.Length; i < len; i++)
            {
                float val = spectrum[i];
                sum += val;
                if (val > maxSpectrum)
                    maxSpectrum = val;
            }
            float average = (spectrum.Length > 0) ? sum / spectrum.Length : 0f;
            _currentRotationIntensity = (spectrum.Length > 0) ? 0.5f + (average * 1.5f) : 1.0f;

            UpdateRotationAngles();
            _rotationMatrix = CreateRotationMatrix();
            RenderDonut(canvas, info, paint, maxSpectrum);
            drawPerformanceInfo(canvas, info);
        }

        public void Dispose()
        {
            if (!_isDisposed)
            {
                _isDisposed = true;
                _font.Dispose();
            }
        }

        #endregion

        #region Private Methods

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private void RenderDonut(SKCanvas canvas, SKImageInfo info, SKPaint paint, float maxSpectrum)
        {
            float centerX = info.Width * 0.5f;
            float centerY = info.Height * 0.5f;
            float scale = MathF.Min(centerX, centerY) * DonutScale * (1f + MathF.Log2(_currentBarCount + 1) * 0.1f);

            ProjectVertices(scale, centerX, centerY);
            System.Array.Sort(_projectedVertices, 0, _vertices.Length,
                Comparer<ProjectedVertex>.Create((a, b) => b.Depth.CompareTo(a.Depth)));

            float minZ = _projectedVertices[0].Depth;
            float maxZ = _projectedVertices[_projectedVertices.Length - 1].Depth;
            float depthRange = maxZ - minZ + float.Epsilon;
            float alphaMultiplier = 1f + MathF.Log2(_currentBarCount + 1) * 0.2f;

            var originalColor = paint.Color;

            // Отрисовка вершин с расчетом интенсивности освещения и альфа-канала
            foreach (ref var vertex in _projectedVertices.AsSpan())
            {
                float normalizedDepth = (vertex.Depth - minZ) / depthRange;
                if (normalizedDepth < 0f || normalizedDepth > 1f)
                    continue;

                int charIndex = (int)(vertex.LightIntensity * (AsciiChars.Length - 1));
                byte baseAlpha = _alphaCache[charIndex];
                byte alpha = (byte)Math.Clamp(baseAlpha * (0.5f + maxSpectrum * 0.5f) * alphaMultiplier * normalizedDepth, 0, 255);

                paint.Color = originalColor.WithAlpha(alpha);
                canvas.DrawText(AsciiCharStrings[charIndex],
                    vertex.X - CharOffsetX,
                    vertex.Y + CharOffsetY,
                    _font,
                    paint);
            }
            paint.Color = originalColor;
        }

        private void UpdateRotationAngles()
        {
            (float sx, float sy, float sz) speeds = (_spectrum.Length > 0)
                ? GetSpectralRotationSpeeds()
                : (RotationSpeedX, RotationSpeedY, RotationSpeedZ);

            _rotationAngleX = (_rotationAngleX + speeds.sx * _currentRotationIntensity) % (MathF.PI * 2f);
            _rotationAngleY = (_rotationAngleY + speeds.sy * _currentRotationIntensity) % (MathF.PI * 2f);
            _rotationAngleZ = (_rotationAngleZ + speeds.sz * _currentRotationIntensity) % (MathF.PI * 2f);
        }

        private (float, float, float) GetSpectralRotationSpeeds()
        {
            int halfSpectrum = _spectrum.Length / 2;
            int segmentSize = halfSpectrum / _currentBarCount;
            if (segmentSize <= 0)
                return (RotationSpeedX, RotationSpeedY, RotationSpeedZ);

            float sum0 = 0f, sum1 = 0f, sum2 = 0f;
            int count0 = 0, count1 = 0, count2 = 0;
            int limit = segmentSize * 3;
            int len = (halfSpectrum < limit) ? halfSpectrum : limit;
            for (int i = 0; i < len; i++)
            {
                if (i < segmentSize)
                {
                    sum0 += _spectrum[i];
                    count0++;
                }
                else if (i < 2 * segmentSize)
                {
                    sum1 += _spectrum[i];
                    count1++;
                }
                else // if (i < 3 * segmentSize)
                {
                    sum2 += _spectrum[i];
                    count2++;
                }
            }
            float avg0 = (count0 > 0) ? sum0 / count0 : 0f;
            float avg1 = (count1 > 0) ? sum1 / count1 : 0f;
            float avg2 = (count2 > 0) ? sum2 / count2 : 0f;
            float multiplier = MathF.Log(_currentBarCount + 1, 2f);
            return (RotationSpeedX * (1f + avg0 * 2f * multiplier),
                    RotationSpeedY * (1f + avg1 * 2f * multiplier),
                    RotationSpeedZ * (1f + avg2 * 2f * multiplier));
        }

        private void ProjectVertices(float scale, float centerX, float centerY)
        {
            System.Threading.Tasks.Parallel.For(0, _vertices.Length, i =>
            {
                Vertex vertex = _vertices[i];
                Vector3 rotated = Vector3.Transform(new Vector3(vertex.X, vertex.Y, vertex.Z), _rotationMatrix);

                float invDepth = 1f / (rotated.Z + DepthOffset);
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