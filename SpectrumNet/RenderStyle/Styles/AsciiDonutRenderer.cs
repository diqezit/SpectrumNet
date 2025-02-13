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

        /// <summary>
        /// Number of segments used to approximate the donut shape. Higher values increase detail but also computation cost.
        /// </summary>
        private const int Segments = 40; 

        /// <summary>
        /// Radius of the donut itself, from the center to the middle of the tube.
        /// </summary>
        private const float DonutRadius = 2.2f;

        /// <summary>
        /// Radius of the tube that forms the donut.
        /// </summary>
        private const float TubeRadius = 0.9f;

        /// <summary>
        /// Scaling factor for donut rendering.
        /// </summary>
        private const float DonutScale = 1.9f;

        /// <summary>
        /// Offset applied to the depth of the donut to move it away from the viewer.
        /// </summary>
        private const float DepthOffset = 7.5f;

        /// <summary>
        /// Factor to scale the depth coordinate for perspective effect.
        /// </summary>
        private const float DepthScaleFactor = 1.3f;

        /// <summary>
        /// Offset to adjust the horizontal position of characters in the rendered output.
        /// </summary>
        private const float CharOffsetX = 3.8f;

        /// <summary>
        /// Offset to adjust the vertical position of characters in the rendered output.
        /// </summary>
        private const float CharOffsetY = 3.8f;

        /// <summary>
        /// Font size used for rendering ASCII characters.
        /// </summary>
        private const float FontSize = 12f;

        /// <summary>
        /// Base intensity of rotation, influencing the rotation speed.
        /// </summary>
        private const float baseRotationIntensity = 0.45f;

        /// <summary>
        /// Scaling factor for spectrum intensity to affect rotation speed.
        /// </summary>
        private const float spectrumIntensityScale = 1.3f;

        /// <summary>
        /// Smoothing factor for spectrum data to reduce jitter in visualization.
        /// </summary>
        private const float smoothingFactorSpectrum = 0.25f;

        /// <summary>
        /// Smoothing factor for rotation angles to create smoother animation.
        /// </summary>
        private const float smoothingFactorRotation = 0.88f;

        /// <summary>
        /// Maximum allowed change in rotation angle per frame to limit rotation speed.
        /// </summary>
        private const float maxRotationAngleChange = 0.0025f;

        /// <summary>
        /// Speed of rotation around the X-axis.
        /// </summary>
        private const float RotationSpeedX = 0.011f / 4f;

        /// <summary>
        /// Speed of rotation around the Y-axis.
        /// </summary>
        private const float RotationSpeedY = 0.019f / 4f;

        /// <summary>
        /// Speed of rotation around the Z-axis.
        /// </summary>
        private const float RotationSpeedZ = 0.014f / 4f;

        /// <summary>
        /// Scale factor for bar count to affect donut scale.
        /// </summary>
        private const float barCountScaleFactorDonutScale = 0.12f;

        /// <summary>
        /// Scale factor for bar count to affect alpha intensity.
        /// </summary>
        private const float barCountScaleFactorAlpha = 0.22f;

        /// <summary>
        /// Base alpha intensity for character rendering.
        /// </summary>
        private const float baseAlphaIntensity = 0.55f;

        /// <summary>
        /// Maximum scaling factor for spectrum to affect alpha intensity.
        /// </summary>
        private const float maxSpectrumAlphaScale = 0.45f;

        /// <summary>
        /// Minimum alpha value for character rendering.
        /// </summary>
        private const float minAlphaValue = 0.22f;

        /// <summary>
        /// Range of alpha values used for character rendering.
        /// </summary>
        private const float alphaRange = 0.65f;

        /// <summary>
        /// Direction of the light source for shading the donut.
        /// </summary>
        private static readonly Vector3 LightDirection = Vector3.Normalize(new Vector3(0.6f, 0.6f, -1.0f));

        /// <summary>
        /// Characters used to represent different light intensities on the donut.
        /// </summary>
        private static readonly char[] AsciiChars = " .:=*#%@█▓".ToCharArray();

        private static readonly string[] AsciiCharStrings;
        private static readonly float[] CosTable;
        private static readonly float[] SinTable;

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
        private float _previousFrameRotationAngleX, _previousFrameRotationAngleY, _previousFrameRotationAngleZ;
        private bool _isInitialized, _isDisposed;
        private float[] _spectrum = Array.Empty<float>();
        private float[] _smoothedSpectrum = Array.Empty<float>();
        private int _currentBarCount;
        private Matrix4x4 _rotationMatrix = Matrix4x4.Identity;

        private static readonly Lazy<AsciiDonutRenderer> LazyInstance = new Lazy<AsciiDonutRenderer>(
            () => new AsciiDonutRenderer(), System.Threading.LazyThreadSafetyMode.ExecutionAndPublication);

        #endregion

        #region Constructor

        public static AsciiDonutRenderer GetInstance() => LazyInstance.Value;

        private AsciiDonutRenderer()
        {
            _vertices = new Vertex[Segments * Segments];
            _projectedVertices = new ProjectedVertex[_vertices.Length];
            _font = new SKFont { Size = FontSize, Hinting = SKFontHinting.None };
            _alphaCache = new byte[AsciiChars.Length];
            InitializeVertices();
            InitializeAlphaCache();
        }

        #endregion

        #region Public Methods

        public void Initialize() => _isInitialized = true;
        public void Configure(bool isOverlayActive) { }

        public void Render(SKCanvas canvas, float[] spectrum, SKImageInfo info, float barWidth,
            float barSpacing, int barCount, SKPaint paint, Action<SKCanvas, SKImageInfo> drawPerformanceInfo)
        {
            if (_isDisposed || info.Width <= 0 || info.Height <= 0 || canvas is null || spectrum is null || paint is null || drawPerformanceInfo is null)
                return;

            _spectrum = spectrum;
            _currentBarCount = barCount;

            UpdateRotationIntensity();
            SmoothSpectrum();
            UpdateRotationAngles();
            _rotationMatrix = CreateRotationMatrix();
            RenderDonut(canvas, info, paint);
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

        private void InitializeVertices()
        {
            int idx = 0;
            for (int i = 0; i < Segments; i++)
            {
                for (int j = 0; j < Segments; j++)
                {
                    float r = DonutRadius + TubeRadius * CosTable[j];
                    _vertices[idx++] = new Vertex(r * CosTable[i], r * SinTable[i], TubeRadius * SinTable[j]);
                }
            }
        }

        private void InitializeAlphaCache()
        {
            for (int i = 0; i < AsciiChars.Length; i++)
            {
                _alphaCache[i] = (byte)((minAlphaValue + alphaRange * (i / (float)(AsciiChars.Length - 1))) * 255);
            }
        }

        private void UpdateRotationIntensity()
        {
            float sum = 0f;
            float len = _spectrum.Length;
            if (len > 0)
            {
                for (int i = 0; i < len; i++)
                {
                    sum += _spectrum[i];
                }
                float average = sum / len;
                _currentRotationIntensity = baseRotationIntensity + (average * spectrumIntensityScale);
            }
            else
            {
                _currentRotationIntensity = 1.0f;
            }
        }


        private void SmoothSpectrum()
        {
            if (_smoothedSpectrum.Length != _spectrum.Length)
            {
                _smoothedSpectrum = new float[_spectrum.Length];
            }
            for (int i = 0; i < _spectrum.Length; i++)
            {
                _smoothedSpectrum[i] = (smoothingFactorSpectrum * _spectrum[i]) + ((1 - smoothingFactorSpectrum) * _smoothedSpectrum[i]);
            }
        }

        private void UpdateRotationAngles()
        {
            float speedX = RotationSpeedX * _currentRotationIntensity;
            float speedY = RotationSpeedY * _currentRotationIntensity;
            float speedZ = RotationSpeedZ * _currentRotationIntensity;

            float rotationChangeX = (_rotationAngleX + speedX) - _previousFrameRotationAngleX;
            float rotationChangeY = (_rotationAngleY + speedY) - _previousFrameRotationAngleY;
            float rotationChangeZ = (_rotationAngleZ + speedZ) - _previousFrameRotationAngleZ;

            float clampedRotationChangeX = Math.Clamp(rotationChangeX, -maxRotationAngleChange, maxRotationAngleChange);
            float clampedRotationChangeY = Math.Clamp(rotationChangeY, -maxRotationAngleChange, maxRotationAngleChange);
            float clampedRotationChangeZ = Math.Clamp(rotationChangeZ, -maxRotationAngleChange, maxRotationAngleChange);

            float rateLimitedRotationAngleX = _previousFrameRotationAngleX + clampedRotationChangeX;
            float rateLimitedRotationAngleY = _previousFrameRotationAngleY + clampedRotationChangeY;
            float rateLimitedRotationAngleZ = _previousFrameRotationAngleZ + clampedRotationChangeZ;

            _rotationAngleX = (1f - smoothingFactorRotation) * _rotationAngleX + smoothingFactorRotation * (rateLimitedRotationAngleX % (MathF.PI * 2f));
            _rotationAngleY = (1f - smoothingFactorRotation) * _rotationAngleY + smoothingFactorRotation * rateLimitedRotationAngleY;
            _rotationAngleZ = (1f - smoothingFactorRotation) * _rotationAngleZ + smoothingFactorRotation * rateLimitedRotationAngleZ;

            _previousFrameRotationAngleX = rateLimitedRotationAngleX;
            _previousFrameRotationAngleY = rateLimitedRotationAngleY;
            _previousFrameRotationAngleZ = rateLimitedRotationAngleZ;
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private void RenderDonut(SKCanvas canvas, SKImageInfo info, SKPaint paint)
        {
            float centerX = info.Width * 0.5f;
            float centerY = info.Height * 0.5f;
            float logBarCount = MathF.Log2(_currentBarCount + 1);
            float scale = MathF.Min(centerX, centerY) * DonutScale * (1f + logBarCount * barCountScaleFactorDonutScale);

            ProjectVertices(scale, centerX, centerY);
            Array.Sort(_projectedVertices, 0, _vertices.Length,
                 Comparer<ProjectedVertex>.Create((a, b) => b.Depth.CompareTo(a.Depth)));

            float minZ = _projectedVertices[0].Depth;
            float maxZ = _projectedVertices[_projectedVertices.Length - 1].Depth;
            float depthRange = maxZ - minZ + float.Epsilon;
            float alphaMultiplier = 1f + logBarCount * barCountScaleFactorAlpha;
            float maxSpectrum = 0f;
            foreach (var val in _spectrum)
            {
                if (val > maxSpectrum)
                    maxSpectrum = val;
            }


            var originalColor = paint.Color;
            foreach (ref var vertex in _projectedVertices.AsSpan())
            {
                float normalizedDepth = (vertex.Depth - minZ) / depthRange;
                if (normalizedDepth is < 0f or > 1f) continue;

                int charIndex = (int)(vertex.LightIntensity * (AsciiChars.Length - 1));
                byte baseAlpha = _alphaCache[charIndex];
                byte alpha = (byte)Math.Clamp(baseAlpha * (baseAlphaIntensity + maxSpectrum * maxSpectrumAlphaScale) * alphaMultiplier * normalizedDepth, 0, 255);

                paint.Color = originalColor.WithAlpha(alpha);
                canvas.DrawText(AsciiCharStrings[charIndex], vertex.X - CharOffsetX, vertex.Y + CharOffsetY, _font, paint);
            }
            paint.Color = originalColor;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ProjectVertices(float scale, float centerX, float centerY)
        {
            float m11 = _rotationMatrix.M11, m12 = _rotationMatrix.M12, m13 = _rotationMatrix.M13;
            float m21 = _rotationMatrix.M21, m22 = _rotationMatrix.M22, m23 = _rotationMatrix.M23;
            float m31 = _rotationMatrix.M31, m32 = _rotationMatrix.M32, m33 = _rotationMatrix.M33;

            Parallel.For(0, _vertices.Length, i =>
            {
                Vertex vertex = _vertices[i];
                float rx = vertex.X * m11 + vertex.Y * m21 + vertex.Z * m31;
                float ry = vertex.X * m12 + vertex.Y * m22 + vertex.Z * m32;
                float rz = vertex.X * m13 + vertex.Y * m23 + vertex.Z * m33;

                float rz_scaled = rz * DepthScaleFactor;
                float invDepth = 1f / (rz_scaled + DepthOffset);
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
                    Depth = rz_scaled + DepthOffset,
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