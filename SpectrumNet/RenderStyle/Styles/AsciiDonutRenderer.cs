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
                Vertices = vertices ?? throw new ArgumentNullException(nameof(vertices));
                MinZ = minZ;
                MaxZ = maxZ;
                DepthRange = maxZ - minZ + float.Epsilon;
                MaxSpectrum = maxSpectrum;
                LogBarCount = logBarCount;
                AlphaMultiplier = 1f + logBarCount * Settings.Instance.DonutBarCountScaleFactorAlpha;
            }
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
            var settings = Settings.Instance;
            CosTable = new float[settings.DonutSegments];
            SinTable = new float[settings.DonutSegments];
            for (int i = 0; i < settings.DonutSegments; i++)
            {
                float angle = i * MathF.PI * 2f / settings.DonutSegments;
                CosTable[i] = MathF.Cos(angle);
                SinTable[i] = MathF.Sin(angle);
            }
            AsciiCharStrings = settings.DonutAsciiChars.ToCharArray().Select(c => c.ToString()).ToArray();
        }
        #endregion

        #region Instance Fields
        private readonly ISettings _settings = Settings.Instance;
        private readonly Vertex[] _vertices;
        private ProjectedVertex[] _projectedVertices;
        private ProjectedVertex[] _renderedVertices;
        private readonly byte[] _alphaCache;
        private readonly SKFont _font;
        private readonly Dictionary<int, List<ProjectedVertex>> _verticesByCharIndex = new();
        private readonly object _renderDataLock = new();
        private readonly Vector3 _lightDirection;

        private float _rotationAngleX, _rotationAngleY, _rotationAngleZ;
        private float _currentRotationIntensity = 1.0f;
        private Matrix4x4 _rotationMatrix = Matrix4x4.Identity;

        private bool _isDisposed;
        private float[] _spectrum = Array.Empty<float>();
        private float[] _smoothedSpectrum = Array.Empty<float>();
        private float[]? _lastSpectrum;
        private int _currentBarCount;
        private int _skipVertexCount;
        private int _lastWidth, _lastHeight;

        // Caching for performance optimization
        private SKPicture? _cachedDonut;
        private bool _needsRecreateCache = true;

        // Background threads for spectrum data processing
        private readonly Thread _processingThread;
        private readonly CancellationTokenSource _cts = new();
        private readonly AutoResetEvent _spectrumDataAvailable = new(false);
        private readonly AutoResetEvent _processingComplete = new(false);
        private float[]? _spectrumToProcess;
        private int _barCountToProcess;
        private bool _processingRunning;
        private RenderData? _currentRenderData;
        private SKImageInfo _lastImageInfo;
        private bool _dataReady;

        // Rendering quality settings
        private RenderQuality _quality = RenderQuality.Medium;
        private bool _useAntiAlias = true;
        private SKFilterQuality _filterQuality = SKFilterQuality.Medium;
        private bool _useAdvancedEffects = true;
        #endregion

        #region Constructor and Initialization
        public static AsciiDonutRenderer GetInstance() => LazyInstance.Value;

        private AsciiDonutRenderer()
        {
            var settings = _settings;

            // Initialize light direction
            _lightDirection = Vector3.Normalize(new Vector3(0.6f, 0.6f, -1.0f));

            _vertices = new Vertex[settings.DonutSegments * settings.DonutSegments];
            _projectedVertices = new ProjectedVertex[_vertices.Length];
            _renderedVertices = new ProjectedVertex[_vertices.Length];
            _alphaCache = new byte[settings.DonutAsciiChars.Length];
            _font = new SKFont { Size = settings.DonutFontSize, Hinting = SKFontHinting.None };

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
        public void Initialize() { /* No-op for compatibility with interface */ }

        public void Configure(bool isOverlayActive, RenderQuality quality = RenderQuality.Medium)
        {
            Quality = quality;
            SmartLogger.Log(LogLevel.Debug, LogPrefix, $"Configured: Overlay={isOverlayActive}, Quality={quality}");
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
            try
            {
                // Fast parameter validation
                if (!ValidateRenderParameters(canvas, spectrum, paint, info))
                    return;

                // Quick rejection if canvas is outside visible area
                if (canvas!.QuickReject(new SKRect(0, 0, info.Width, info.Height)))
                    return;

                _lastImageInfo = info;

                // Check if canvas size has changed - if so, invalidate cache
                if (_cachedDonut != null && (_lastWidth != info.Width || _lastHeight != info.Height))
                {
                    _needsRecreateCache = true;
                    _lastWidth = info.Width;
                    _lastHeight = info.Height;
                }

                SubmitSpectrumForProcessing(spectrum!, barCount);

                if (_dataReady)
                {
                    RenderDonut(canvas, info, paint!);
                }

                drawPerformanceInfo?.Invoke(canvas, info);
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error in Render: {ex.Message}");
            }
        }

        public void Dispose()
        {
            if (_isDisposed)
                return;

            try
            {
                _processingRunning = false;
                _cts.Cancel();
                _spectrumDataAvailable.Set();

                // Safely terminate processing thread
                if (_processingThread.IsAlive && !_processingThread.Join(100))
                {
                    SmartLogger.Log(LogLevel.Warning, LogPrefix, "Processing thread did not terminate gracefully");
                }

                _cts.Dispose();
                _spectrumDataAvailable.Dispose();
                _processingComplete.Dispose();
                _font.Dispose();
                _cachedDonut?.Dispose();

                _isDisposed = true;
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error during disposal: {ex.Message}");
            }
            finally
            {
                GC.SuppressFinalize(this);
            }
        }
        #endregion

        #region Background Processing
        private void SubmitSpectrumForProcessing(float[] spectrum, int barCount)
        {
            try
            {
                // Check if spectrum has significantly changed to decide whether to update cache
                bool significantChange = false;

                if (_lastSpectrum != null && _lastSpectrum.Length == spectrum.Length)
                {
                    float maxDiff = 0;
                    for (int i = 0; i < spectrum.Length; i++)
                    {
                        maxDiff = Math.Max(maxDiff, Math.Abs(spectrum[i] - _lastSpectrum[i]));
                    }

                    // If difference is significant, update cache
                    if (maxDiff > 0.1f)
                    {
                        significantChange = true;
                        _needsRecreateCache = true;
                    }
                }
                else
                {
                    significantChange = true;
                    _needsRecreateCache = true;
                    _lastSpectrum = new float[spectrum.Length];
                }

                // Copy current spectrum for comparison in next frame
                if (significantChange)
                {
                    Array.Copy(spectrum, _lastSpectrum, spectrum.Length);
                }

                lock (_renderDataLock)
                {
                    _spectrumToProcess = spectrum;
                    _barCountToProcess = barCount;
                }

                _spectrumDataAvailable.Set();
                // Wait for processing to complete with timeout
                if (!_processingComplete.WaitOne(5))
                {
                    SmartLogger.Log(LogLevel.Debug, LogPrefix, "Processing timeout occurred");
                }
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error in SubmitSpectrumForProcessing: {ex.Message}");
            }
        }

        private void ProcessSpectrumThreadFunc()
        {
            try
            {
                while (_processingRunning && !_cts.Token.IsCancellationRequested)
                {
                    _spectrumDataAvailable.WaitOne();

                    if (_cts.Token.IsCancellationRequested)
                        break;

                    float[]? spectrumCopy = null;
                    int barCountCopy = 0;

                    lock (_renderDataLock)
                    {
                        if (_spectrumToProcess != null)
                        {
                            spectrumCopy = new float[_spectrumToProcess.Length];
                            Array.Copy(_spectrumToProcess, spectrumCopy, _spectrumToProcess.Length);
                            barCountCopy = _barCountToProcess;
                        }
                    }

                    if (spectrumCopy != null)
                    {
                        ComputeDonutData(spectrumCopy, barCountCopy, _lastImageInfo);
                    }

                    _processingComplete.Set();
                }
            }
            catch (OperationCanceledException)
            {
                // Normal termination on cancellation
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
                float scale = MathF.Min(centerX, centerY) * _settings.DonutScale *
                              (1f + logBarCount * _settings.DonutBarCountScaleFactorDonutScale);

                // Consider skipVertexCount for optimization
                int step = _skipVertexCount + 1;
                int effectiveVertexCount = _vertices.Length / step;

                // If array size has changed, create a new one
                if (_projectedVertices.Length != effectiveVertexCount)
                {
                    _projectedVertices = new ProjectedVertex[effectiveVertexCount];
                    _renderedVertices = new ProjectedVertex[effectiveVertexCount];
                }

                ProjectVertices(scale, centerX, centerY);

                if (_cts.Token.IsCancellationRequested)
                    return;

                // Sort by depth (from greater to lesser)
                Array.Sort(_projectedVertices, Comparer<ProjectedVertex>.Create((a, b) => b.Depth.CompareTo(a.Depth)));

                float maxZ = _projectedVertices.Length > 0 ? _projectedVertices[0].Depth : 0f;
                float minZ = _projectedVertices.Length > 0 ? _projectedVertices[^1].Depth : 0f;

                float maxSpectrum = _spectrum.Length > 0 ? _spectrum.Max() : 0f;

                lock (_renderDataLock)
                {
                    Array.Copy(_projectedVertices, _renderedVertices, _projectedVertices.Length);
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
            !_isDisposed && canvas != null && spectrum != null && spectrum.Length > 0 && paint != null &&
            info.Width > 0 && info.Height > 0;

        private void RenderDonut(SKCanvas canvas, SKImageInfo info, SKPaint paint)
        {
            RenderData renderData;
            lock (_renderDataLock)
            {
                if (!_dataReady || _currentRenderData == null)
                    return;
                renderData = _currentRenderData.Value;
            }

            // Use caching only for high quality when it's justified
            if (_quality == RenderQuality.High && _useAdvancedEffects)
            {
                if (_needsRecreateCache || _cachedDonut == null)
                {
                    using var recorder = new SKPictureRecorder();
                    using var recordCanvas = recorder.BeginRecording(new SKRect(0, 0, info.Width, info.Height));

                    RenderDonutInternal(recordCanvas, info, paint, renderData);

                    _cachedDonut?.Dispose();
                    _cachedDonut = recorder.EndRecording();
                    _needsRecreateCache = false;
                }

                canvas.DrawPicture(_cachedDonut);
            }
            else
            {
                // For low and medium quality, render directly
                RenderDonutInternal(canvas, info, paint, renderData);
            }
        }

        private void RenderDonutInternal(SKCanvas canvas, SKImageInfo info, SKPaint paint, RenderData renderData)
        {
            paint.IsAntialias = _useAntiAlias;
            paint.FilterQuality = _filterQuality;
            var originalColor = paint.Color;

            // Clear and reuse dictionary for vertex grouping
            _verticesByCharIndex.Clear();

            foreach (var vertex in renderData.Vertices)
            {
                if (vertex.X < 0 || vertex.X >= info.Width || vertex.Y < 0 || vertex.Y >= info.Height)
                    continue;

                float normalizedDepth = (vertex.Depth - renderData.MinZ) / renderData.DepthRange;
                if (normalizedDepth is < 0f or > 1f)
                    continue;

                int charIndex = (int)ClampF(vertex.LightIntensity * (AsciiCharStrings.Length - 1), 0, AsciiCharStrings.Length - 1);

                if (!_verticesByCharIndex.TryGetValue(charIndex, out var vertices))
                {
                    vertices = new List<ProjectedVertex>();
                    _verticesByCharIndex[charIndex] = vertices;
                }

                vertices.Add(vertex);
            }

            // Render all vertices of the same character type in one pass to minimize state changes
            foreach (var kvp in _verticesByCharIndex)
            {
                int charIndex = kvp.Key;
                var vertices = kvp.Value;

                byte baseAlpha = _alphaCache[charIndex];
                byte alpha = (byte)ClampF(
                    baseAlpha * (_settings.DonutBaseAlphaIntensity + renderData.MaxSpectrum * _settings.DonutMaxSpectrumAlphaScale) *
                    renderData.AlphaMultiplier, 0, 255);

                paint.Color = originalColor.WithAlpha(alpha);

                foreach (var vertex in vertices)
                {
                    canvas.DrawText(AsciiCharStrings[charIndex],
                        vertex.X - _settings.DonutCharOffsetX,
                        vertex.Y + _settings.DonutCharOffsetY,
                        _font, paint);
                }
            }

            paint.Color = originalColor;
        }
        #endregion

        #region Helper Methods
        private void InitializeVertices()
        {
            int idx = 0;
            for (int i = 0; i < _settings.DonutSegments; i++)
            {
                for (int j = 0; j < _settings.DonutSegments; j++)
                {
                    float r = _settings.DonutRadius + _settings.DonutTubeRadius * CosTable[j];
                    _vertices[idx++] = new Vertex(r * CosTable[i], r * SinTable[i], _settings.DonutTubeRadius * SinTable[j]);
                }
            }
        }

        private void InitializeAlphaCache()
        {
            for (int i = 0; i < AsciiCharStrings.Length; i++)
            {
                float normalizedIndex = i / (float)(AsciiCharStrings.Length - 1);
                _alphaCache[i] = (byte)((_settings.DonutMinAlphaValue + _settings.DonutAlphaRange * normalizedIndex) * 255);
            }
        }

        private void UpdateRotationIntensity()
        {
            if (_spectrum.Length == 0)
            {
                _currentRotationIntensity = 1.0f;
                return;
            }

            float sum = 0f;
            for (int i = 0; i < _spectrum.Length; i++)
                sum += _spectrum[i];

            float average = sum / _spectrum.Length;
            float newIntensity = _settings.DonutBaseRotationIntensity + (average * _settings.DonutSpectrumIntensityScale);
            _currentRotationIntensity = _currentRotationIntensity * (1f - _settings.DonutRotationIntensitySmoothingFactor) +
                                       newIntensity * _settings.DonutRotationIntensitySmoothingFactor;
            _currentRotationIntensity = ClampF(_currentRotationIntensity, _settings.DonutRotationIntensityMin, _settings.DonutRotationIntensityMax);
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private void SmoothSpectrum()
        {
            if (_smoothedSpectrum.Length != _spectrum.Length)
            {
                _smoothedSpectrum = new float[_spectrum.Length];
                Array.Copy(_spectrum, _smoothedSpectrum, _spectrum.Length);
            }

            // Use Vector for SIMD acceleration with large arrays
            if (_spectrum.Length >= Vector<float>.Count)
            {
                int vectorSize = Vector<float>.Count;
                int vectorizedLength = _spectrum.Length - (_spectrum.Length % vectorSize);

                Vector<float> smoothingFactor = new Vector<float>(_settings.DonutSmoothingFactorSpectrum);
                Vector<float> oneMinusSmoothingFactor = new Vector<float>(1f - _settings.DonutSmoothingFactorSpectrum);

                for (int i = 0; i < vectorizedLength; i += vectorSize)
                {
                    Vector<float> spectrumVec = new Vector<float>(_spectrum, i);
                    Vector<float> smoothedVec = new Vector<float>(_smoothedSpectrum, i);

                    Vector<float> result = (smoothingFactor * spectrumVec) + (oneMinusSmoothingFactor * smoothedVec);
                    result.CopyTo(_smoothedSpectrum, i);
                }

                // Process remaining elements
                for (int i = vectorizedLength; i < _spectrum.Length; i++)
                {
                    _smoothedSpectrum[i] = _settings.DonutSmoothingFactorSpectrum * _spectrum[i] +
                                          (1f - _settings.DonutSmoothingFactorSpectrum) * _smoothedSpectrum[i];
                }
            }
            else
            {
                // For small arrays, use regular loop
                for (int i = 0; i < _spectrum.Length; i++)
                {
                    _smoothedSpectrum[i] = _settings.DonutSmoothingFactorSpectrum * _spectrum[i] +
                                          (1f - _settings.DonutSmoothingFactorSpectrum) * _smoothedSpectrum[i];
                }
            }
        }

        private void UpdateRotationAngles()
        {
            _rotationAngleX = UpdateAngle(_rotationAngleX, _settings.DonutRotationSpeedX * _currentRotationIntensity,
                                         _settings.DonutSmoothingFactorRotation, _settings.DonutMaxRotationAngleChange);
            _rotationAngleY = UpdateAngle(_rotationAngleY, _settings.DonutRotationSpeedY * _currentRotationIntensity,
                                         _settings.DonutSmoothingFactorRotation, _settings.DonutMaxRotationAngleChange);
            _rotationAngleZ = UpdateAngle(_rotationAngleZ, _settings.DonutRotationSpeedZ * _currentRotationIntensity,
                                         _settings.DonutSmoothingFactorRotation, _settings.DonutMaxRotationAngleChange);
        }

        private float UpdateAngle(float current, float speed, float smoothing, float maxChange)
        {
            float target = current + speed;
            float diff = MinimalAngleDiff(current, target);
            float clampedDiff = ClampF(diff, -maxChange, maxChange);
            return current + clampedDiff * smoothing;
        }

        private float MinimalAngleDiff(float a, float b)
        {
            float diff = b - a;
            while (diff < -MathF.PI) diff += MathF.PI * 2;
            while (diff > MathF.PI) diff -= MathF.PI * 2;
            return diff;
        }

        private static float ClampF(float value, float min, float max) =>
            value < min ? min : (value > max ? max : value);

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

            // Consider skipVertexCount for optimization
            int step = _skipVertexCount + 1;
            int vertexCount = _vertices.Length / step;

            // Use Parallel.For only for high quality or large number of vertices
            if (_quality == RenderQuality.High || _vertices.Length > 1000)
            {
                Parallel.For(0, vertexCount, i =>
                {
                    int vertexIndex = i * step;
                    var vertex = _vertices[vertexIndex];
                    ProjectSingleVertex(vertex, m11, m12, m13, m21, m22, m23, m31, m32, m33,
                                      scale, centerX, centerY, i);
                });
            }
            else
            {
                // For low and medium quality, use regular loop
                for (int i = 0; i < vertexCount; i++)
                {
                    int vertexIndex = i * step;
                    var vertex = _vertices[vertexIndex];
                    ProjectSingleVertex(vertex, m11, m12, m13, m21, m22, m23, m31, m32, m33,
                                      scale, centerX, centerY, i);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ProjectSingleVertex(Vertex vertex,
                                      float m11, float m12, float m13,
                                      float m21, float m22, float m23,
                                      float m31, float m32, float m33,
                                      float scale, float centerX, float centerY, int targetIndex)
        {
            float rx = vertex.X * m11 + vertex.Y * m21 + vertex.Z * m31;
            float ry = vertex.X * m12 + vertex.Y * m22 + vertex.Z * m32;
            float rz = vertex.X * m13 + vertex.Y * m23 + vertex.Z * m33;

            float rzScaled = rz * _settings.DonutDepthScaleFactor;
            float invDepth = 1f / (rzScaled + _settings.DonutDepthOffset);

            // Calculate lighting only for high and medium quality
            float lightIntensity;
            if (_useAdvancedEffects)
            {
                float length = MathF.Sqrt(rx * rx + ry * ry + rz * rz);
                float invLength = length > 0f ? 1f / length : 0f;
                float normRx = rx * invLength;
                float normRy = ry * invLength;
                float normRz = rz * invLength;
                lightIntensity = MathF.Max(0f, normRx * _lightDirection.X +
                                              normRy * _lightDirection.Y +
                                              normRz * _lightDirection.Z);
            }
            else
            {
                lightIntensity = 1f;
            }

            _projectedVertices[targetIndex] = new ProjectedVertex
            {
                X = rx * scale * invDepth + centerX,
                Y = ry * scale * invDepth + centerY,
                Depth = rzScaled + _settings.DonutDepthOffset,
                LightIntensity = lightIntensity
            };
        }

        private void ApplyQualitySettings()
        {
            _needsRecreateCache = true; // Invalidate cache when quality settings change

            int oldSkipVertexCount = _skipVertexCount;

            switch (_quality)
            {
                case RenderQuality.Low:
                    _useAntiAlias = false;
                    _filterQuality = SKFilterQuality.Low;
                    _useAdvancedEffects = false;
                    _skipVertexCount = _settings.DonutLowQualitySkipFactor;
                    break;
                case RenderQuality.Medium:
                    _useAntiAlias = true;
                    _filterQuality = SKFilterQuality.Medium;
                    _useAdvancedEffects = true;
                    _skipVertexCount = _settings.DonutMediumQualitySkipFactor;
                    break;
                case RenderQuality.High:
                    _useAntiAlias = true;
                    _filterQuality = SKFilterQuality.High;
                    _useAdvancedEffects = true;
                    _skipVertexCount = _settings.DonutHighQualitySkipFactor;
                    break;
            }

            // If skip factor has changed, completely reset processed data
            if (_skipVertexCount != oldSkipVertexCount)
            {
                // Reset all cached vertex data
                lock (_renderDataLock)
                {
                    _dataReady = false;
                    _currentRenderData = null;
                }
            }

            // Invalidate caches that depend on quality
            InvalidateCachedResources();

            SmartLogger.Log(LogLevel.Debug, LogPrefix, $"Quality settings applied: {_quality}");
        }

        private void InvalidateCachedResources()
        {
            if (_cachedDonut != null)
            {
                _cachedDonut.Dispose();
                _cachedDonut = null;
            }

            // Reset other cached data
            _dataReady = false;
        }
        #endregion

        #region Logging
        private const string LogPrefix = "[AsciiDonutRenderer] ";
        #endregion
    }
}