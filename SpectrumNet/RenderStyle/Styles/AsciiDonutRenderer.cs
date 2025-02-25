#nullable enable
namespace SpectrumNet
{
    public sealed class AsciiDonutRenderer : ISpectrumRenderer, IDisposable
    {
        #region Helper Structures
        private readonly record struct Vertex(float X, float Y, float Z);

        private struct ProjectedVertex
        {
            public float X, Y, Depth, LightIntensity;
        }

        private readonly struct RenderData
        {
            public readonly ProjectedVertex[] Vertices;
            public readonly float MinZ, MaxZ, DepthRange, MaxSpectrum;
            public readonly float LogBarCount, AlphaMultiplier;

            public RenderData(ProjectedVertex[] vertices, float minZ, float maxZ, float maxSpectrum, float logBarCount)
            {
                Vertices = vertices;
                MinZ = minZ;
                MaxZ = maxZ;
                DepthRange = maxZ - minZ + float.Epsilon;
                MaxSpectrum = maxSpectrum;
                LogBarCount = logBarCount;
                AlphaMultiplier = 1f + logBarCount * Constants.BarCountScaleFactorAlpha;
            }
        }
        #endregion

        #region Constants
        private static class Constants
        {
            public const int Segments = 40;
            public const float DonutRadius = 2.2f;
            public const float TubeRadius = 0.9f;
            public const float DonutScale = 1.9f;
            public const float DepthOffset = 7.5f;
            public const float DepthScaleFactor = 1.3f;
            public const float CharOffsetX = 3.8f;
            public const float CharOffsetY = 3.8f;
            public const float FontSize = 12f;
            public const float BaseRotationIntensity = 0.45f;
            public const float SpectrumIntensityScale = 1.3f;
            public const float SmoothingFactorSpectrum = 0.25f;
            public const float SmoothingFactorRotation = 0.88f;
            public const float MaxRotationAngleChange = 0.0025f;
            public const float RotationSpeedX = 0.011f / 4f;
            public const float RotationSpeedY = 0.019f / 4f;
            public const float RotationSpeedZ = 0.014f / 4f;
            public const float BarCountScaleFactorDonutScale = 0.12f;
            public const float BarCountScaleFactorAlpha = 0.22f;
            public const float BaseAlphaIntensity = 0.55f;
            public const float MaxSpectrumAlphaScale = 0.45f;
            public const float MinAlphaValue = 0.22f;
            public const float AlphaRange = 0.65f;
            public const float RotationIntensityMin = 0.1f;
            public const float RotationIntensityMax = 2.0f;
            public const float RotationIntensitySmoothingFactor = 0.1f;

            public static readonly Vector3 LightDirection = Vector3.Normalize(new Vector3(0.6f, 0.6f, -1.0f));
            public static readonly char[] AsciiChars = " .:=*#%@█▓".ToCharArray();
        }
        #endregion

        #region Static Fields
        private static readonly string[] AsciiCharStrings;
        private static readonly float[] CosTable;
        private static readonly float[] SinTable;
        private static readonly Lazy<AsciiDonutRenderer> LazyInstance = new(
            () => new AsciiDonutRenderer(), System.Threading.LazyThreadSafetyMode.ExecutionAndPublication);

        static AsciiDonutRenderer()
        {
            CosTable = new float[Constants.Segments];
            SinTable = new float[Constants.Segments];
            for (int i = 0; i < Constants.Segments; i++)
            {
                float angle = i * MathF.PI * 2f / Constants.Segments;
                CosTable[i] = MathF.Cos(angle);
                SinTable[i] = MathF.Sin(angle);
            }

            AsciiCharStrings = new string[Constants.AsciiChars.Length];
            for (int i = 0; i < Constants.AsciiChars.Length; i++)
            {
                AsciiCharStrings[i] = Constants.AsciiChars[i].ToString();
            }
        }
        #endregion

        #region Instance Fields
        private readonly byte[] _alphaCache;
        private readonly Vertex[] _vertices;
        private readonly ProjectedVertex[] _projectedVertices;
        private readonly ProjectedVertex[] _renderedVertices;
        private readonly SKFont _font;

        private float _rotationAngleX, _rotationAngleY, _rotationAngleZ;
        private float _previousFrameRotationAngleX, _previousFrameRotationAngleY, _previousFrameRotationAngleZ;
        private float _currentRotationIntensity = 1.0f;
        private Matrix4x4 _rotationMatrix = Matrix4x4.Identity;

        private bool _isDisposed;
        private float[] _spectrum = Array.Empty<float>();
        private float[] _smoothedSpectrum = Array.Empty<float>();
        private int _currentBarCount;

        // Поля для многопоточной обработки
        private readonly Thread _processingThread;
        private readonly CancellationTokenSource _cts = new();
        private readonly AutoResetEvent _spectrumDataAvailable = new(false);
        private readonly AutoResetEvent _processingComplete = new(false);
        private readonly object _renderDataLock = new();
        private float[]? _spectrumToProcess;
        private int _barCountToProcess;
        private bool _processingRunning;
        private RenderData? _currentRenderData;
        private SKImageInfo _lastImageInfo;
        private bool _dataReady;
        #endregion

        #region Constructor and Initialization
        public static AsciiDonutRenderer GetInstance() => LazyInstance.Value;

        private AsciiDonutRenderer()
        {
            _vertices = new Vertex[Constants.Segments * Constants.Segments];
            _projectedVertices = new ProjectedVertex[_vertices.Length];
            _renderedVertices = new ProjectedVertex[_vertices.Length];
            _font = new SKFont { Size = Constants.FontSize, Hinting = SKFontHinting.None };
            _alphaCache = new byte[Constants.AsciiChars.Length];

            InitializeVertices();
            InitializeAlphaCache();

            _processingThread = new Thread(ProcessSpectrumThreadFunc)
            {
                IsBackground = true,
                Name = "DonutProcessor"
            };
            _processingRunning = true;
            _processingThread.Start();
        }
        #endregion

        #region Public Methods
        public void Initialize() { }

        public void Configure(bool isOverlayActive) { }

        public void Render(SKCanvas? canvas, float[]? spectrum, SKImageInfo info, float barWidth,
            float barSpacing, int barCount, SKPaint? paint, Action<SKCanvas, SKImageInfo>? drawPerformanceInfo)
        {
            if (!ValidateRenderParameters(canvas, spectrum, paint, info)) return;

            _lastImageInfo = info;
            SubmitSpectrumForProcessing(spectrum, barCount);

            if (_dataReady)
            {
                RenderDonut(canvas!, info, paint!);
            }

            drawPerformanceInfo?.Invoke(canvas!, info);
        }

        public void Dispose()
        {
            if (_isDisposed) return;

            _processingRunning = false;
            _cts.Cancel();
            _spectrumDataAvailable.Set();
            _processingThread.Join(100);

            _cts.Dispose();
            _spectrumDataAvailable.Dispose();
            _processingComplete.Dispose();
            _font.Dispose();

            _isDisposed = true;

            GC.SuppressFinalize(this);
        }
        #endregion

        #region Background Processing
        private void SubmitSpectrumForProcessing(float[]? spectrum, int barCount)
        {
            if (spectrum == null) return;

            lock (_renderDataLock)
            {
                _spectrumToProcess = spectrum;
                _barCountToProcess = barCount;
            }

            _spectrumDataAvailable.Set();
            _processingComplete.WaitOne(5);
        }

        private void ProcessSpectrumThreadFunc()
        {
            try
            {
                while (_processingRunning && !_cts.Token.IsCancellationRequested)
                {
                    _spectrumDataAvailable.WaitOne();

                    float[]? spectrumCopy;
                    int barCountCopy;

                    lock (_renderDataLock)
                    {
                        if (_spectrumToProcess == null) { _processingComplete.Set(); continue; }
                        spectrumCopy = _spectrumToProcess;
                        barCountCopy = _barCountToProcess;
                    }

                    ComputeDonutData(spectrumCopy, barCountCopy, _lastImageInfo);
                    _processingComplete.Set();
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Log.Error($"Error in donut processing thread: {ex.Message}");
            }
        }

        private void ComputeDonutData(float[] spectrum, int barCount, SKImageInfo info)
        {
            try
            {
                if (_cts.Token.IsCancellationRequested)
                    return;

                _spectrum = spectrum;
                _currentBarCount = barCount;

                UpdateRotationIntensity();
                SmoothSpectrum();
                UpdateRotationAngles();
                _rotationMatrix = CreateRotationMatrix();

                float centerX = info.Width * 0.5f;
                float centerY = info.Height * 0.5f;
                float logBarCount = MathF.Log2(_currentBarCount + 1);
                float scale = MathF.Min(centerX, centerY) * Constants.DonutScale *
                              (1f + logBarCount * Constants.BarCountScaleFactorDonutScale);

                ProjectVertices(scale, centerX, centerY);

                if (_cts.Token.IsCancellationRequested)
                    return;

                Array.Sort(_projectedVertices, 0, _vertices.Length,
                    Comparer<ProjectedVertex>.Create((a, b) => b.Depth.CompareTo(a.Depth)));

                float minZ = _projectedVertices[0].Depth;
                float maxZ = _projectedVertices[_projectedVertices.Length - 1].Depth;

                float maxSpectrumValue = 0f;
                foreach (var val in _spectrum)
                {
                    if (val > maxSpectrumValue)
                        maxSpectrumValue = val;
                }

                lock (_renderDataLock)
                {
                    Array.Copy(_projectedVertices, _renderedVertices, _projectedVertices.Length);
                    _currentRenderData = new RenderData(
                        _renderedVertices, minZ, maxZ, maxSpectrumValue, logBarCount);
                    _dataReady = true;
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Log.Error($"Error computing donut data: {ex.Message}");
            }
        }
        #endregion

        #region Rendering
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool ValidateRenderParameters(SKCanvas? canvas, float[]? spectrum, SKPaint? paint, SKImageInfo info) =>
            !_isDisposed && canvas != null && spectrum != null &&
            spectrum.Length > 0 && paint != null && info.Width > 0 && info.Height > 0;

        private void RenderDonut(SKCanvas canvas, SKImageInfo info, SKPaint paint)
        {
            RenderData renderData;

            lock (_renderDataLock)
            {
                if (!_dataReady || _currentRenderData == null)
                    return;

                renderData = _currentRenderData.Value;
            }

            var originalColor = paint.Color;

            foreach (var vertex in renderData.Vertices)
            {
                float normalizedDepth = (vertex.Depth - renderData.MinZ) / renderData.DepthRange;
                if (normalizedDepth is < 0f or > 1f) continue;

                int charIndex = (int)(vertex.LightIntensity * (Constants.AsciiChars.Length - 1));
                byte baseAlpha = _alphaCache[charIndex];
                byte alpha = (byte)Math.Clamp(
                    baseAlpha * (Constants.BaseAlphaIntensity + renderData.MaxSpectrum * Constants.MaxSpectrumAlphaScale) *
                    renderData.AlphaMultiplier * normalizedDepth, 0, 255);

                paint.Color = originalColor.WithAlpha(alpha);
                canvas.DrawText(AsciiCharStrings[charIndex],
                                vertex.X - Constants.CharOffsetX,
                                vertex.Y + Constants.CharOffsetY,
                                _font, paint);
            }

            paint.Color = originalColor;
        }
        #endregion

        #region Helper Methods
        private void InitializeVertices()
        {
            int idx = 0;
            for (int i = 0; i < Constants.Segments; i++)
            {
                for (int j = 0; j < Constants.Segments; j++)
                {
                    float r = Constants.DonutRadius + Constants.TubeRadius * CosTable[j];
                    _vertices[idx++] = new Vertex(r * CosTable[i], r * SinTable[i], Constants.TubeRadius * SinTable[j]);
                }
            }
        }

        private void InitializeAlphaCache()
        {
            for (int i = 0; i < Constants.AsciiChars.Length; i++)
            {
                float normalizedIndex = i / (float)(Constants.AsciiChars.Length - 1);
                _alphaCache[i] = (byte)((Constants.MinAlphaValue + Constants.AlphaRange * normalizedIndex) * 255);
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
                float newIntensity = Constants.BaseRotationIntensity + (average * Constants.SpectrumIntensityScale);
                _currentRotationIntensity = _currentRotationIntensity * (1f - Constants.RotationIntensitySmoothingFactor) +
                                           newIntensity * Constants.RotationIntensitySmoothingFactor;
                _currentRotationIntensity = Math.Clamp(_currentRotationIntensity,
                                                      Constants.RotationIntensityMin,
                                                      Constants.RotationIntensityMax);
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
                Array.Copy(_spectrum, _smoothedSpectrum, _spectrum.Length);
            }

            for (int i = 0; i < _spectrum.Length; i++)
            {
                _smoothedSpectrum[i] = (Constants.SmoothingFactorSpectrum * _spectrum[i]) +
                                      ((1 - Constants.SmoothingFactorSpectrum) * _smoothedSpectrum[i]);
            }
        }

        private void UpdateRotationAngles()
        {
            float speedX = Constants.RotationSpeedX * _currentRotationIntensity;
            float speedY = Constants.RotationSpeedY * _currentRotationIntensity;
            float speedZ = Constants.RotationSpeedZ * _currentRotationIntensity;

            float rotationChangeX = (_rotationAngleX + speedX) - _previousFrameRotationAngleX;
            float rotationChangeY = (_rotationAngleY + speedY) - _previousFrameRotationAngleY;
            float rotationChangeZ = (_rotationAngleZ + speedZ) - _previousFrameRotationAngleZ;

            float clampedRotationChangeX = Math.Clamp(rotationChangeX, -Constants.MaxRotationAngleChange, Constants.MaxRotationAngleChange);
            float clampedRotationChangeY = Math.Clamp(rotationChangeY, -Constants.MaxRotationAngleChange, Constants.MaxRotationAngleChange);
            float clampedRotationChangeZ = Math.Clamp(rotationChangeZ, -Constants.MaxRotationAngleChange, Constants.MaxRotationAngleChange);

            float rateLimitedRotationAngleX = _previousFrameRotationAngleX + clampedRotationChangeX;
            float rateLimitedRotationAngleY = _previousFrameRotationAngleY + clampedRotationChangeY;
            float rateLimitedRotationAngleZ = _previousFrameRotationAngleZ + clampedRotationChangeZ;

            _rotationAngleX = (1f - Constants.SmoothingFactorRotation) * _rotationAngleX +
                             Constants.SmoothingFactorRotation * (rateLimitedRotationAngleX % (MathF.PI * 2f));
            _rotationAngleY = (1f - Constants.SmoothingFactorRotation) * _rotationAngleY +
                             Constants.SmoothingFactorRotation * rateLimitedRotationAngleY;
            _rotationAngleZ = (1f - Constants.SmoothingFactorRotation) * _rotationAngleZ +
                             Constants.SmoothingFactorRotation * rateLimitedRotationAngleZ;

            _previousFrameRotationAngleX = rateLimitedRotationAngleX;
            _previousFrameRotationAngleY = rateLimitedRotationAngleY;
            _previousFrameRotationAngleZ = rateLimitedRotationAngleZ;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Matrix4x4 CreateRotationMatrix() =>
            Matrix4x4.CreateRotationX(_rotationAngleX) *
            Matrix4x4.CreateRotationY(_rotationAngleY) *
            Matrix4x4.CreateRotationZ(_rotationAngleZ);

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
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

                float rz_scaled = rz * Constants.DepthScaleFactor;
                float invDepth = 1f / (rz_scaled + Constants.DepthOffset);
                float length = MathF.Sqrt(rx * rx + ry * ry + rz * rz);
                float invLength = (length > 0f) ? 1f / length : 0f;
                float normRx = rx * invLength;
                float normRy = ry * invLength;
                float normRz = rz * invLength;
                float lightIntensity = (normRx * Constants.LightDirection.X +
                                       normRy * Constants.LightDirection.Y +
                                       normRz * Constants.LightDirection.Z) * 0.5f + 0.5f;

                _projectedVertices[i] = new ProjectedVertex
                {
                    X = rx * scale * invDepth + centerX,
                    Y = ry * scale * invDepth + centerY,
                    Depth = rz_scaled + Constants.DepthOffset,
                    LightIntensity = lightIntensity
                };
            });
        }
        #endregion
    }
}