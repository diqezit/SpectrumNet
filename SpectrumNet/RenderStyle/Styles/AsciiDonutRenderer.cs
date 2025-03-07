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
            // Donut geometry
            public const int Segments = 40;       // Number of segments for donut approximation
            public const float DonutRadius = 2.2f;     // Radius of the donut
            public const float TubeRadius = 0.9f;     // Radius of the tube
            public const float DonutScale = 1.9f;     // Scale factor for the donut

            // Rendering parameters
            public const float DepthOffset = 7.5f;     // Offset for depth calculation
            public const float DepthScaleFactor = 1.3f;     // Scale factor for depth
            public const float CharOffsetX = 3.8f;     // X offset for character positioning
            public const float CharOffsetY = 3.8f;     // Y offset for character positioning
            public const float FontSize = 12f;      // Font size for ASCII characters

            // Rotation and animation
            public const float BaseRotationIntensity = 0.45f;    // Base intensity for rotation
            public const float SpectrumIntensityScale = 1.3f;     // Scale factor for spectrum intensity
            public const float SmoothingFactorSpectrum = 0.25f;    // Smoothing factor for spectrum
            public const float SmoothingFactorRotation = 0.88f;    // Smoothing factor for rotation
            public const float MaxRotationAngleChange = 0.0025f;  // Maximum change in rotation angle
            public const float RotationSpeedX = 0.011f / 4f; // Rotation speed around X-axis
            public const float RotationSpeedY = 0.019f / 4f; // Rotation speed around Y-axis
            public const float RotationSpeedZ = 0.014f / 4f; // Rotation speed around Z-axis

            // Scaling and alpha
            public const float BarCountScaleFactorDonutScale = 0.12f;  // Scale factor for donut based on bar count
            public const float BarCountScaleFactorAlpha = 0.22f;    // Scale factor for alpha based on bar count
            public const float BaseAlphaIntensity = 0.55f;    // Base intensity for alpha calculation
            public const float MaxSpectrumAlphaScale = 0.45f;    // Scale factor for alpha based on max spectrum
            public const float MinAlphaValue = 0.22f;    // Minimum alpha value
            public const float AlphaRange = 0.65f;    // Range for alpha values

            // Rotation intensity limits
            public const float RotationIntensityMin = 0.1f;     // Minimum rotation intensity
            public const float RotationIntensityMax = 2.0f;     // Maximum rotation intensity
            public const float RotationIntensitySmoothingFactor = 0.1f;// Smoothing factor for rotation intensity

            // Lighting and characters
            public static readonly Vector3 LightDirection = new(0.6f, 0.6f, -1.0f); // Direction of light source (normalized in static constructor)
            public static readonly char[] AsciiChars = " .:=*#%@█▓".ToCharArray(); // Characters for ASCII art
        }
        #endregion

        #region Static Fields
        private static readonly string[] AsciiCharStrings;
        private static readonly float[] CosTable;
        private static readonly float[] SinTable;
        private static readonly Lazy<AsciiDonutRenderer> LazyInstance = new(
            () => new AsciiDonutRenderer(), LazyThreadSafetyMode.ExecutionAndPublication);

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

            AsciiCharStrings = Constants.AsciiChars.Select(c => c.ToString()).ToArray();
        }
        #endregion

        #region Instance Fields
        private readonly Vertex[] _vertices;
        private readonly ProjectedVertex[] _projectedVertices;
        private readonly ProjectedVertex[] _renderedVertices;
        private readonly byte[] _alphaCache;
        private readonly SKFont _font;

        private float _rotationAngleX, _rotationAngleY, _rotationAngleZ;
        private float _prevRotationAngleX, _prevRotationAngleY, _prevRotationAngleZ;
        private float _currentRotationIntensity = 1.0f;
        private Matrix4x4 _rotationMatrix = Matrix4x4.Identity;

        private bool _isDisposed;
        private float[] _spectrum = Array.Empty<float>();
        private float[] _smoothedSpectrum = Array.Empty<float>();
        private int _currentBarCount;

        // Background processing
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

        // RenderQuality settings
        private RenderQuality _quality = RenderQuality.Medium;
        private bool _useAntiAlias = true;
        private SKFilterQuality _filterQuality = SKFilterQuality.Medium;
        private bool _useAdvancedEffects = true;
        #endregion

        #region Constructor and Initialization
        public static AsciiDonutRenderer GetInstance() => LazyInstance.Value;

        private AsciiDonutRenderer()
        {
            _vertices = new Vertex[Constants.Segments * Constants.Segments];
            _projectedVertices = new ProjectedVertex[_vertices.Length];
            _renderedVertices = new ProjectedVertex[_vertices.Length];
            _alphaCache = new byte[Constants.AsciiChars.Length];
            _font = new SKFont { Size = Constants.FontSize, Hinting = SKFontHinting.None };

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

        public void Configure(bool isOverlayActive, RenderQuality quality = RenderQuality.Medium)
        {
            Quality = quality; // Устанавливаем качество рендеринга
        }

        public RenderQuality Quality
        {
            get => _quality;
            set
            {
                if (_quality != value)
                {
                    _quality = value;
                    ApplyQualitySettings();
                }
            }
        }

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
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error in processing thread: {ex.Message}");
            }
        }

        private void ComputeDonutData(float[] spectrum, int barCount, SKImageInfo info)
        {
            try
            {
                if (_cts.Token.IsCancellationRequested) return;

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

                if (_cts.Token.IsCancellationRequested) return;

                Array.Sort(_projectedVertices, Comparer<ProjectedVertex>.Create((a, b) => b.Depth.CompareTo(a.Depth)));

                float minZ = _projectedVertices[0].Depth;
                float maxZ = _projectedVertices[^1].Depth;

                float maxSpectrum = _spectrum.Max();

                lock (_renderDataLock)
                {
                    Array.Copy(_projectedVertices, _renderedVertices, _vertices.Length);
                    _currentRenderData = new RenderData(_renderedVertices, minZ, maxZ, maxSpectrum, logBarCount);
                    _dataReady = true;
                }
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error computing donut data: {ex.Message}");
            }
        }
        #endregion

        #region Rendering
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool ValidateRenderParameters(SKCanvas? canvas, float[]? spectrum, SKPaint? paint, SKImageInfo info) =>
            !_isDisposed && canvas != null && spectrum != null && spectrum.Length > 0 && paint != null && info.Width > 0 && info.Height > 0;

        private void RenderDonut(SKCanvas canvas, SKImageInfo info, SKPaint paint)
        {
            RenderData renderData;

            lock (_renderDataLock)
            {
                if (!_dataReady || _currentRenderData == null) return;
                renderData = _currentRenderData.Value;
            }

            paint.IsAntialias = _useAntiAlias;
            paint.FilterQuality = _filterQuality;
            var originalColor = paint.Color;

            foreach (var vertex in renderData.Vertices)
            {
                // Отсечение невидимых объектов
                if (vertex.X < 0 || vertex.X >= info.Width || vertex.Y < 0 || vertex.Y >= info.Height)
                    continue;

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

            paint.Color = originalColor; // Восстановление исходного цвета
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
            float sum = _spectrum.Sum();
            float len = _spectrum.Length;
            if (len > 0)
            {
                float average = sum / len;
                float newIntensity = Constants.BaseRotationIntensity + (average * Constants.SpectrumIntensityScale);
                _currentRotationIntensity = _currentRotationIntensity * (1f - Constants.RotationIntensitySmoothingFactor) +
                                           newIntensity * Constants.RotationIntensitySmoothingFactor;
                _currentRotationIntensity = Math.Clamp(_currentRotationIntensity,
                    Constants.RotationIntensityMin, Constants.RotationIntensityMax);
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
                _smoothedSpectrum[i] = Constants.SmoothingFactorSpectrum * _spectrum[i] +
                                      (1f - Constants.SmoothingFactorSpectrum) * _smoothedSpectrum[i];
            }
        }

        private void UpdateRotationAngles()
        {
            float speedX = Constants.RotationSpeedX * _currentRotationIntensity;
            float speedY = Constants.RotationSpeedY * _currentRotationIntensity;
            float speedZ = Constants.RotationSpeedZ * _currentRotationIntensity;

            float rotationChangeX = (_rotationAngleX + speedX) - _prevRotationAngleX;
            float rotationChangeY = (_rotationAngleY + speedY) - _prevRotationAngleY;
            float rotationChangeZ = (_rotationAngleZ + speedZ) - _prevRotationAngleZ;

            float clampedRotationChangeX = Math.Clamp(rotationChangeX, -Constants.MaxRotationAngleChange, Constants.MaxRotationAngleChange);
            float clampedRotationChangeY = Math.Clamp(rotationChangeY, -Constants.MaxRotationAngleChange, Constants.MaxRotationAngleChange);
            float clampedRotationChangeZ = Math.Clamp(rotationChangeZ, -Constants.MaxRotationAngleChange, Constants.MaxRotationAngleChange);

            float rateLimitedRotationAngleX = _prevRotationAngleX + clampedRotationChangeX;
            float rateLimitedRotationAngleY = _prevRotationAngleY + clampedRotationChangeY;
            float rateLimitedRotationAngleZ = _prevRotationAngleZ + clampedRotationChangeZ;

            _rotationAngleX = (1f - Constants.SmoothingFactorRotation) * _rotationAngleX +
                              Constants.SmoothingFactorRotation * (rateLimitedRotationAngleX % (MathF.PI * 2f));
            _rotationAngleY = (1f - Constants.SmoothingFactorRotation) * _rotationAngleY +
                              Constants.SmoothingFactorRotation * rateLimitedRotationAngleY;
            _rotationAngleZ = (1f - Constants.SmoothingFactorRotation) * _rotationAngleZ +
                              Constants.SmoothingFactorRotation * rateLimitedRotationAngleZ;

            _prevRotationAngleX = rateLimitedRotationAngleX;
            _prevRotationAngleY = rateLimitedRotationAngleY;
            _prevRotationAngleZ = rateLimitedRotationAngleZ;
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
                var vertex = _vertices[i];
                float rx = vertex.X * m11 + vertex.Y * m21 + vertex.Z * m31;
                float ry = vertex.X * m12 + vertex.Y * m22 + vertex.Z * m32;
                float rz = vertex.X * m13 + vertex.Y * m23 + vertex.Z * m33;

                float rzScaled = rz * Constants.DepthScaleFactor;
                float invDepth = 1f / (rzScaled + Constants.DepthOffset);
                float length = MathF.Sqrt(rx * rx + ry * ry + rz * rz);
                float invLength = length > 0f ? 1f / length : 0f;
                float normRx = rx * invLength;
                float normRy = ry * invLength;
                float normRz = rz * invLength;
                float lightIntensity = Math.Max(0f, Vector3.Dot(new Vector3(normRx, normRy, normRz), Constants.LightDirection));

                _projectedVertices[i] = new ProjectedVertex
                {
                    X = rx * scale * invDepth + centerX,
                    Y = ry * scale * invDepth + centerY,
                    Depth = rzScaled + Constants.DepthOffset,
                    LightIntensity = lightIntensity
                };
            });
        }

        private void ApplyQualitySettings()
        {
            switch (_quality)
            {
                case RenderQuality.Low:
                    _useAntiAlias = false;
                    _filterQuality = SKFilterQuality.Low;
                    _useAdvancedEffects = false;
                    break;
                case RenderQuality.Medium:
                    _useAntiAlias = true;
                    _filterQuality = SKFilterQuality.Medium;
                    _useAdvancedEffects = true;
                    break;
                case RenderQuality.High:
                    _useAntiAlias = true;
                    _filterQuality = SKFilterQuality.High;
                    _useAdvancedEffects = true;
                    break;
            }
        }
        #endregion

        #region Logging
        private const string LogPrefix = "[AsciiDonutRenderer] ";
        #endregion
    }
}