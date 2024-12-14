#nullable enable

using Vector = System.Numerics.Vector;

namespace SpectrumNet
{
    public sealed class AsciiDonutRenderer : ISpectrumRenderer, IDisposable
    {
        #region Nested Types
        private readonly record struct Vertex(float X, float Y, float Z);
        private struct ProjectedVertex { public float X, Y, Depth, LightIntensity; }
        #endregion

        #region Constants
        private const int Segments = 60;
        private const float DonutRadius = 1.5f, TubeRadius = 0.6f;
        private const float RotationSpeedX = 0.01f, RotationSpeedY = 0.02f, RotationSpeedZ = 0.015f;
        private const float DepthOffset = 6.0f, CharOffsetX = 4f, CharOffsetY = 4f, FontSize = 10f, DonutScale = 1.85f;
        private const float MinLightIntensity = 0.2f, MaxLightIntensity = 0.8f;
        private static readonly Vector3 LightDirection = Vector3.Normalize(new Vector3(0.5f, 0.5f, -1.0f));
        #endregion

        #region Static Fields
        private static readonly Lazy<AsciiDonutRenderer> LazyInstance = new(() => new AsciiDonutRenderer(), LazyThreadSafetyMode.ExecutionAndPublication);
        private static readonly char[] AsciiChars = " .:-=+*#%@".ToCharArray();
        private static readonly float[] CosTable = new float[Segments], SinTable = new float[Segments];
        #endregion

        #region Instance Fields
        private readonly byte[] _alphaCache = new byte[AsciiChars.Length];
        private readonly Vertex[] _vertices;
        private readonly ProjectedVertex[] _projectedVertices;
        private readonly SKFont _font;
        private float _rotationAngleX, _rotationAngleY, _rotationAngleZ, _currentRotationIntensity = 1.0f;
        private bool _isInitialized, _isDisposed;
        private float[] _spectrum;
        private int _currentBarCount;
        private Matrix4x4 _rotationMatrix;
        #endregion

        #region Constructor
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

            for (int i = 0; i < _alphaCache.Length; i++)
                _alphaCache[i] = (byte)((MinLightIntensity + (MaxLightIntensity - MinLightIntensity) * (i / (float)(AsciiChars.Length - 1))) * 255);
        }
        #endregion

        #region Public Methods
        public static AsciiDonutRenderer GetInstance() => LazyInstance.Value;

        public void Initialize()
        {
            if (!_isInitialized)
            {
                _isInitialized = true;
                Log.Debug("AsciiDonutRenderer initialized");
            }
        }

        public void Configure(bool isOverlayActive) { }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Render(SKCanvas canvas, float[] spectrum, SKImageInfo info, float barWidth,
            float barSpacing, int barCount, SKPaint paint, Action<SKCanvas, SKImageInfo> drawPerformanceInfo)
        {
            if (!ValidateRenderParameters(canvas, spectrum, info, paint, drawPerformanceInfo)) return;

            _spectrum = spectrum;
            _currentBarCount = barCount;

            UpdateRotationIntensity(spectrum);
            UpdateRotationAngles(barCount);
            _rotationMatrix = CreateRotationMatrix();
            RenderAsciiDonut(canvas, info, paint);
            drawPerformanceInfo(canvas, info);
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            _font.Dispose();
            Log.Debug("AsciiDonutRenderer disposed");
        }
        #endregion

        #region Private Methods

        private void UpdateRotationIntensity(float[] spectrum)
        {
            _currentRotationIntensity = spectrum?.Length > 0
                ? 0.5f + (spectrum.AsSpan().ToArray().Average() * 1.5f)
                : 1.0f;
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private void RenderAsciiDonut(SKCanvas canvas, SKImageInfo info, SKPaint paint)
        {
            float centerX = info.Width * 0.5f, centerY = info.Height * 0.5f;
            float scale = MathF.Min(centerX, centerY) * DonutScale;
            scale *= 1f + (MathF.Log(_currentBarCount + 1, 2) * 0.1f);

            ProjectVertices(scale, centerX, centerY);

            Array.Sort(_projectedVertices, (a, b) => b.Depth.CompareTo(a.Depth));

            float minZ = float.MaxValue, maxZ = float.MinValue;
            for (int i = 0; i < _projectedVertices.Length; i++)
            {
                minZ = MathF.Min(minZ, _projectedVertices[i].Depth);
                maxZ = MathF.Max(maxZ, _projectedVertices[i].Depth);
            }

            float dynamicMinDepth = minZ, dynamicDepthRange = maxZ - minZ;
            float maxSpectrum = _spectrum?.Length > 0 ? _spectrum.AsSpan().ToArray().Max() : 1f;

            SKColor originalPaintColor = paint.Color;
            float barCountAlphaMultiplier = 1f + (MathF.Log(_currentBarCount + 1, 2) * 0.2f);

            for (int i = 0; i < _projectedVertices.Length; i++)
            {
                ref ProjectedVertex vertex = ref _projectedVertices[i];

                if (vertex.Depth < dynamicMinDepth || vertex.Depth > dynamicMinDepth + dynamicDepthRange) continue;

                float normalizedDepth = (vertex.Depth - dynamicMinDepth) / dynamicDepthRange;
                int charIndex = (int)(vertex.LightIntensity * (AsciiChars.Length - 1));

                byte dynamicAlpha = (byte)Math.Clamp(
                    _alphaCache[charIndex] * (0.5f + (maxSpectrum / 2f)) * barCountAlphaMultiplier * normalizedDepth, 0, 255);

                paint.Color = originalPaintColor.WithAlpha(dynamicAlpha);
                canvas.DrawText(AsciiChars[charIndex].ToString(), vertex.X - CharOffsetX, vertex.Y + CharOffsetY, _font, paint);
            }

            paint.Color = originalPaintColor;
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private void ProjectVertices(float scale, float centerX, float centerY)
        {
            Parallel.For(0, _vertices.Length, i =>
            {
                ref Vertex v = ref _vertices[i];
                Vector3 rotatedVertex = Vector3.Transform(new Vector3(v.X, v.Y, v.Z), _rotationMatrix);
                float depth = rotatedVertex.Z + DepthOffset;

                float invDepth = 1 / depth;
                _projectedVertices[i] = new ProjectedVertex
                {
                    X = (rotatedVertex[0] * scale * invDepth) + centerX,
                    Y = (rotatedVertex[1] * scale * invDepth) + centerY,
                    Depth = depth,
                    LightIntensity = CalculateLightIntensity(rotatedVertex)
                };
            });
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private static Vertex[] GenerateDonutVertices()
        {
            var vertices = new Vertex[Segments * Segments];

            Parallel.For(0, Segments, i =>
            {
                float cosU = CosTable[i], sinU = SinTable[i];
                for (int j = 0; j < Segments; j++)
                {
                    float cosV = CosTable[j], sinV = SinTable[j];
                    float r = DonutRadius + TubeRadius * cosV;
                    vertices[i * Segments + j] = new Vertex(r * cosU, r * sinU, TubeRadius * sinV);
                }
            });

            return vertices;
        }

        private Matrix4x4 CreateRotationMatrix() =>
            Matrix4x4.CreateRotationX(_rotationAngleX) *
            Matrix4x4.CreateRotationY(_rotationAngleY) *
            Matrix4x4.CreateRotationZ(_rotationAngleZ);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void UpdateRotationAngles(int barCount)
        {
            float rotSpeedX = RotationSpeedX, rotSpeedY = RotationSpeedY, rotSpeedZ = RotationSpeedZ;

            if (_spectrum?.Length > 0)
            {
                int segmentSize = (_spectrum.Length / 2 ) / barCount;
                Span<float> spectrumSpan = _spectrum;

                Vector<float> sumLow = Vector<float>.Zero;
                Vector<float> sumMid = Vector<float>.Zero;
                Vector<float> sumHigh = Vector<float>.Zero;

                int vectorSize = Vector<float>.Count;
                int limitLow = segmentSize * (barCount / 3);
                int limitMid = segmentSize * (2 * barCount / 3);
                int limitHigh = segmentSize * barCount;

                int i = 0;
                for (; i <= limitLow - vectorSize; i += vectorSize)
                {
                    sumLow += new Vector<float>(spectrumSpan.Slice(i, vectorSize));
                }
                for (; i <= limitMid - vectorSize; i += vectorSize)
                {
                    sumMid += new Vector<float>(spectrumSpan.Slice(i, vectorSize));
                }
                for (; i <= limitHigh - vectorSize; i += vectorSize)
                {
                    sumHigh += new Vector<float>(spectrumSpan.Slice(i, vectorSize));
                }

                float avgLowFreq = Vector.Dot(sumLow, Vector<float>.One) / limitLow;
                float avgMidFreq = Vector.Dot(sumMid, Vector<float>.One) / (limitMid - limitLow);
                float avgHighFreq = Vector.Dot(sumHigh, Vector<float>.One) / (limitHigh - limitMid);

                for (; i < limitLow; i++) avgLowFreq += spectrumSpan[i] / limitLow;
                for (; i < limitMid; i++) avgMidFreq += spectrumSpan[i] / (limitMid - limitLow);
                for (; i < limitHigh; i++) avgHighFreq += spectrumSpan[i] / (limitHigh - limitMid);

                float barCountMultiplier = MathF.Log(barCount + 1, 2);
                rotSpeedX *= 1f + (avgLowFreq * 2f * barCountMultiplier);
                rotSpeedY *= 1f + (avgMidFreq * 2f * barCountMultiplier);
                rotSpeedZ *= 1f + (avgHighFreq * 2f * barCountMultiplier);
            }

            _rotationAngleX = (_rotationAngleX + rotSpeedX * _currentRotationIntensity) % (MathF.PI * 2);
            _rotationAngleY = (_rotationAngleY + rotSpeedY * _currentRotationIntensity) % (MathF.PI * 2);
            _rotationAngleZ = (_rotationAngleZ + rotSpeedZ * _currentRotationIntensity) % (MathF.PI * 2);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool ValidateRenderParameters(SKCanvas canvas, float[] spectrum, SKImageInfo info, SKPaint paint, Action<SKCanvas, SKImageInfo> drawPerformanceInfo) =>
            !_isDisposed && info.Width > 0 && info.Height > 0 && canvas != null && spectrum != null && paint != null && drawPerformanceInfo != null;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float CalculateLightIntensity(Vector3 v)
        {
            Vector3 normal = Vector3.Normalize(v);
            return (Vector3.Dot(normal, LightDirection) + 1) * 0.5f;
        }

        #endregion

    }

    public struct Vector3
    {
        public float[] Data;

        public Vector3(float x, float y, float z)
        {
            Data = new float[] { x, y, z };
        }

        public Vector3(float[] data)
        {
            if (data.Length != 3)
            {
                throw new ArgumentException("Array must have length 3");
            }
            Data = data;
        }

        public float X
        {
            get => Data[0];
            set => Data[0] = value;
        }

        public float Y
        {
            get => Data[1];
            set => Data[1] = value;
        }

        public float Z
        {
            get => Data[2];
            set => Data[2] = value;
        }

        public float this[int index]
        {
            get => Data[index];
            set => Data[index] = value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 Normalize(Vector3 v)
        {
            float length = MathF.Sqrt(v[0] * v[0] + v[1] * v[1] + v[2] * v[2]);
            return new Vector3(v[0] / length, v[1] / length, v[2] / length);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Dot(Vector3 a, Vector3 b)
        {
            return a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 Transform(Vector3 v, Matrix4x4 m)
        {
            return new Vector3(
                v[0] * m[0, 0] + v[1] * m[1, 0] + v[2] * m[2, 0] + m[3, 0],
                v[0] * m[0, 1] + v[1] * m[1, 1] + v[2] * m[2, 1] + m[3, 1],
                v[0] * m[0, 2] + v[1] * m[1, 2] + v[2] * m[2, 2] + m[3, 2]
            );
        }

        public static Vector3 operator +(Vector3 a, Vector3 b)
        {
            return new Vector3(a[0] + b[0], a[1] + b[1], a[2] + b[2]);
        }

        public static Vector3 operator -(Vector3 a, Vector3 b)
        {
            return new Vector3(a[0] - b[0], a[1] - b[1], a[2] - b[2]);
        }

        public static Vector3 operator *(Vector3 v, float scalar)
        {
            return new Vector3(v[0] * scalar, v[1] * scalar, v[2] * scalar);
        }

        public static Vector3 operator /(Vector3 v, float scalar)
        {
            return new Vector3(v[0] / scalar, v[1] / scalar, v[2] / scalar);
        }
    }

    public struct Matrix4x4
    {
        public float[,] Data;

        public Matrix4x4(float[,] data)
        {
            if (data.GetLength(0) != 4 || data.GetLength(1) != 4)
            {
                throw new ArgumentException("Array must be 4x4");
            }
            Data = data;
        }

        public float this[int row, int col]
        {
            get => Data[row, col];
            set => Data[row, col] = value;
        }

        public static Matrix4x4 CreateRotationX(float radians)
        {
            float cos = MathF.Cos(radians);
            float sin = MathF.Sin(radians);
            return new Matrix4x4(new float[,]
            {
            { 1, 0, 0, 0 },
            { 0, cos, sin, 0 },
            { 0, -sin, cos, 0 },
            { 0, 0, 0, 1 }
            });
        }

        public static Matrix4x4 CreateRotationY(float radians)
        {
            float cos = MathF.Cos(radians);
            float sin = MathF.Sin(radians);
            return new Matrix4x4(new float[,]
            {
            { cos, 0, -sin, 0 },
            { 0, 1, 0, 0 },
            { sin, 0, cos, 0 },
            { 0, 0, 0, 1 }
            });
        }

        public static Matrix4x4 CreateRotationZ(float radians)
        {
            float cos = MathF.Cos(radians);
            float sin = MathF.Sin(radians);
            return new Matrix4x4(new float[,]
            {
            { cos, sin, 0, 0 },
            { -sin, cos, 0, 0 },
            { 0, 0, 1, 0 },
            { 0, 0, 0, 1 }
            });
        }

        public static Matrix4x4 operator *(Matrix4x4 m1, Matrix4x4 m2)
        {
            float[,] result = new float[4, 4];
            for (int i = 0; i < 4; i++)
            {
                for (int j = 0; j < 4; j++)
                {
                    for (int k = 0; k < 4; k++)
                    {
                        result[i, j] += m1[i, k] * m2[k, j];
                    }
                }
            }
            return new Matrix4x4(result);
        }
    }
}