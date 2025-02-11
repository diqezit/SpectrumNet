#nullable enable
namespace SpectrumNet
{
    public sealed class AsciiDonutRenderer : ISpectrumRenderer, IDisposable
    {
        #region Helper Structures

        private readonly record struct Vertex(float X, float Y, float Z);
        private struct ProjectedVertex { public float X, Y, Depth, LightIntensity; }

        #endregion

        #region Constants and Static Fields

        private const int Segments = 60;
        private const float DonutRadius = 1.5f;
        private const float TubeRadius = 0.6f;
        private const float RotationSpeedX = 0.01f;
        private const float RotationSpeedY = 0.02f;
        private const float RotationSpeedZ = 0.015f;
        private const float DepthOffset = 6.0f;
        private const float CharOffsetX = 4f;
        private const float CharOffsetY = 4f;
        private const float FontSize = 10f;
        private const float DonutScale = 1.85f;
        private static readonly Vector3 LightDirection = Vector3.Normalize(new Vector3(0.5f, 0.5f, -1.0f));
        private static readonly char[] AsciiChars = " .:-=+*#%@".ToCharArray();
        private static readonly string[] AsciiCharStrings;
        private static readonly float[] CosTable;
        private static readonly float[] SinTable;

        // Static constructor for initializing static fields
        static AsciiDonutRenderer()
        {
            CosTable = new float[Segments];
            SinTable = new float[Segments];
            for (int i = 0; i < Segments; i++)
            {
                float angle = i * MathF.PI * 2f / Segments;
                CosTable[i] = MathF.Cos(angle);
                SinTable[i] = MathF.Sin(angle);
            }
            AsciiCharStrings = new string[AsciiChars.Length];
            for (int i = 0; i < AsciiChars.Length; i++)
            {
                AsciiCharStrings[i] = AsciiChars[i].ToString();
            }
        }

        #endregion

        #region Instance Fields

        private readonly byte[] _alphaCache;
        private readonly Vertex[] _vertices;
        private readonly ProjectedVertex[] _projectedVertices;
        private readonly SKFont _font;
        private float _rotationAngleX, _rotationAngleY, _rotationAngleZ, _currentRotationIntensity = 1.0f;
        private bool _isInitialized, _isDisposed;
        private float[] _spectrum = Array.Empty<float>();
        private int _currentBarCount;
        private Matrix4x4 _rotationMatrix;

        private static readonly Lazy<AsciiDonutRenderer> LazyInstance = new Lazy<AsciiDonutRenderer>(
            () => new AsciiDonutRenderer(), System.Threading.LazyThreadSafetyMode.ExecutionAndPublication);

        #endregion

        #region Constructor

        public static AsciiDonutRenderer GetInstance() => LazyInstance.Value;

        private AsciiDonutRenderer()
        {
            // Generate donut vertices (without using LINQ)
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

            // Initialize alpha channel cache for ASCII characters
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

            // Single-pass calculation of spectrum sum and maximum value
            float sum = 0f, maxSpectrum = 0f;
            int len = spectrum.Length;
            for (int i = 0; i < len; i++)
            {
                float val = spectrum[i];
                sum += val;
                if (val > maxSpectrum)
                    maxSpectrum = val;
            }
            float average = (len > 0) ? sum / len : 0f;
            _currentRotationIntensity = (len > 0) ? 0.5f + (average * 1.5f) : 1.0f;

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
            float logBarCount = MathF.Log2(_currentBarCount + 1);
            float scale = MathF.Min(centerX, centerY) * DonutScale * (1f + logBarCount * 0.1f);

            ProjectVertices(scale, centerX, centerY);
            // Sort vertices by depth (from far to near)
            Array.Sort(_projectedVertices, 0, _vertices.Length,
                Comparer<ProjectedVertex>.Create((a, b) => b.Depth.CompareTo(a.Depth)));

            float minZ = _projectedVertices[0].Depth;
            float maxZ = _projectedVertices[_projectedVertices.Length - 1].Depth;
            float depthRange = maxZ - minZ + float.Epsilon;
            float alphaMultiplier = 1f + logBarCount * 0.2f;

            var originalColor = paint.Color;
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void UpdateRotationAngles()
        {
            var speeds = (_spectrum.Length > 0)
                ? GetSpectralRotationSpeeds()
                : (RotationSpeedX, RotationSpeedY, RotationSpeedZ);

            _rotationAngleX = (_rotationAngleX + speeds.Item1 * _currentRotationIntensity) % (MathF.PI * 2f);
            _rotationAngleY = (_rotationAngleY + speeds.Item2 * _currentRotationIntensity) % (MathF.PI * 2f);
            _rotationAngleZ = (_rotationAngleZ + speeds.Item3 * _currentRotationIntensity) % (MathF.PI * 2f);
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
                else
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ProjectVertices(float scale, float centerX, float centerY)
        {
            // Extract rotation matrix elements to reduce overhead
            float m11 = _rotationMatrix.M11, m12 = _rotationMatrix.M12, m13 = _rotationMatrix.M13;
            float m21 = _rotationMatrix.M21, m22 = _rotationMatrix.M22, m23 = _rotationMatrix.M23;
            float m31 = _rotationMatrix.M31, m32 = _rotationMatrix.M32, m33 = _rotationMatrix.M33;

            Parallel.For(0, _vertices.Length, i =>
            {
                Vertex vertex = _vertices[i];
                float rx = vertex.X * m11 + vertex.Y * m21 + vertex.Z * m31;
                float ry = vertex.X * m12 + vertex.Y * m22 + vertex.Z * m32;
                float rz = vertex.X * m13 + vertex.Y * m23 + vertex.Z * m33;

                float invDepth = 1f / (rz + DepthOffset);
                float length = MathF.Sqrt(rx * rx + ry * ry + rz * rz);
                float invLength = (length > 0f) ? 1f / length : 0f;
                float normRx = rx * invLength;
                float normRy = ry * invLength;
                float normRz = rz * invLength;
                float lightIntensity = (normRx * LightDirection.X + normRy * LightDirection.Y + normRz * LightDirection.Z) * 0.5f + 0.5f;

                _projectedVertices[i] = new ProjectedVertex
                {
                    X = rx * scale * invDepth + centerX,
                    Y = ry * scale * invDepth + centerY,
                    Depth = rz + DepthOffset,
                    LightIntensity = lightIntensity
                };
            });
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Matrix4x4 CreateRotationMatrix() =>
            Matrix4x4.CreateRotationX(_rotationAngleX) *
            Matrix4x4.CreateRotationY(_rotationAngleY) *
            Matrix4x4.CreateRotationZ(_rotationAngleZ);

        #endregion
    }
}