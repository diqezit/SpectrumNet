#nullable enable

namespace SpectrumNet
{
    /// <summary>
    /// Renders an ASCII art 3D donut that reacts to audio spectrum data.
    /// </summary>
    public sealed class AsciiDonutRenderer : BaseSpectrumRenderer
    {
        #region Constants
        private static class Constants
        {
            public const string LOG_PREFIX = "AsciiDonutRenderer";

            // Rendering parameters
            public const float DEFAULT_ROTATION_SPEED_X = 0.01f;
            public const float DEFAULT_ROTATION_SPEED_Y = 0.02f;
            public const float DEFAULT_ROTATION_SPEED_Z = 0.005f;
            public const float DEFAULT_ROTATION_INTENSITY = 1.0f;
            public const float DEFAULT_DEPTH_SCALE_FACTOR = 2.0f;
            public const float DEFAULT_DEPTH_OFFSET = 3.0f;

            // Animation constants
            public const float MIN_ROTATION_INTENSITY = 0.5f;
            public const float MAX_ROTATION_INTENSITY = 2.0f;
            public const float MAX_ROTATION_ANGLE_CHANGE = 0.1f;
            public const float ROTATION_INTENSITY_SMOOTHING = 0.2f;
            public const float ROTATION_SMOOTHING = 0.1f;

            // Light settings
            public const float LIGHT_DIR_X = 0.6f;
            public const float LIGHT_DIR_Y = 0.6f;
            public const float LIGHT_DIR_Z = -1.0f;

            // Geometry settings
            public const int DEFAULT_SEGMENTS = 36;
            public const float DEFAULT_RADIUS = 1.0f;
            public const float DEFAULT_TUBE_RADIUS = 0.5f;
            public const float DEFAULT_SCALE = 0.4f;

            // Alpha settings
            public const float MIN_ALPHA_VALUE = 0.2f;
            public const float ALPHA_RANGE = 0.8f;
            public const float BASE_ALPHA_INTENSITY = 0.7f;
            public const float MAX_SPECTRUM_ALPHA_SCALE = 0.3f;

            // Quality settings
            public const int LOW_QUALITY_SKIP_FACTOR = 3;
            public const int MEDIUM_QUALITY_SKIP_FACTOR = 1;
            public const int HIGH_QUALITY_SKIP_FACTOR = 0;

            // Character rendering
            public const float CHAR_OFFSET_X = 4.0f;
            public const float CHAR_OFFSET_Y = 4.0f;
            public const float DEFAULT_FONT_SIZE = 12.0f;
            public const string DEFAULT_ASCII_CHARS = " .,-~:;=!*#$@";
        }
        #endregion

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
                AlphaMultiplier = 1f + logBarCount * 0.1f; // Scale factor for alpha based on bar count
            }
        }
        #endregion

        #region Static Fields
        private static readonly string[] _asciiCharStrings;
        private static readonly float[] _cosTable;
        private static readonly float[] _sinTable;
        private static readonly Lazy<AsciiDonutRenderer> _lazyInstance = new(
            () => new AsciiDonutRenderer(), System.Threading.LazyThreadSafetyMode.ExecutionAndPublication);

        static AsciiDonutRenderer()
        {
            int segments = Constants.DEFAULT_SEGMENTS;
            _cosTable = new float[segments];
            _sinTable = new float[segments];

            for (int i = 0; i < segments; i++)
            {
                float angle = i * MathF.PI * 2f / segments;
                _cosTable[i] = MathF.Cos(angle);
                _sinTable[i] = MathF.Sin(angle);
            }

            _asciiCharStrings = Constants.DEFAULT_ASCII_CHARS.ToCharArray().Select(c => c.ToString()).ToArray();
        }
        #endregion

        #region Instance Fields
        private readonly Vertex[] _vertices;
        private ProjectedVertex[] _projectedVertices;
        private ProjectedVertex[] _renderedVertices;
        private readonly byte[] _alphaCache;
        private readonly SKFont _font;
        private readonly Dictionary<int, List<ProjectedVertex>> _verticesByCharIndex = new();
        private readonly object _renderDataLock = new();
        private readonly Vector3 _lightDirection;

        private float _rotationAngleX, _rotationAngleY, _rotationAngleZ;
        private float _currentRotationIntensity = Constants.DEFAULT_ROTATION_INTENSITY;
        private Matrix4x4 _rotationMatrix = Matrix4x4.Identity;

        private int _skipVertexCount;
        private int _lastWidth, _lastHeight;

        // Caching for performance optimization
        private SKPicture? _cachedDonut;
        private bool _needsRecreateCache = true;

        // Background processing
        private bool _dataReady;
        private RenderData? _currentRenderData;
        private SKImageInfo _lastImageInfo;
        #endregion

        #region Singleton
        public static AsciiDonutRenderer GetInstance() => _lazyInstance.Value;

        private AsciiDonutRenderer()
        {
            // Initialize light direction
            _lightDirection = Vector3.Normalize(new Vector3(
                Constants.LIGHT_DIR_X,
                Constants.LIGHT_DIR_Y,
                Constants.LIGHT_DIR_Z));

            // Initialize geometry
            int segments = Constants.DEFAULT_SEGMENTS;
            _vertices = new Vertex[segments * segments];
            _projectedVertices = new ProjectedVertex[_vertices.Length];
            _renderedVertices = new ProjectedVertex[_vertices.Length];
            _alphaCache = new byte[Constants.DEFAULT_ASCII_CHARS.Length];

            _font = new SKFont
            {
                Size = Constants.DEFAULT_FONT_SIZE,
                Hinting = SKFontHinting.None
            };

            InitializeVertices();
            InitializeAlphaCache();
        }
        #endregion

        #region Initialization and Configuration
        public override void Initialize() => SmartLogger.Safe(
            () => {
                base.Initialize();
                _needsRecreateCache = true;
                SmartLogger.Log(LogLevel.Debug, Constants.LOG_PREFIX, "Initialized");
            },
            new SmartLogger.ErrorHandlingOptions
            {
                Source = $"{Constants.LOG_PREFIX}.Initialize",
                ErrorMessage = "Failed to initialize renderer"
            });

        public override void Configure(bool isOverlayActive, RenderQuality quality = RenderQuality.Medium) => SmartLogger.Safe(
            () => {
                base.Configure(isOverlayActive, quality);
                ApplyQualitySettings();
            },
            new SmartLogger.ErrorHandlingOptions
            {
                Source = $"{Constants.LOG_PREFIX}.Configure",
                ErrorMessage = "Failed to configure renderer"
            });

        protected override void ApplyQualitySettings() => SmartLogger.Safe(
            () => {
                base.ApplyQualitySettings();

                int oldSkipVertexCount = _skipVertexCount;

                _skipVertexCount = _quality switch
                {
                    RenderQuality.Low => Constants.LOW_QUALITY_SKIP_FACTOR,
                    RenderQuality.Medium => Constants.MEDIUM_QUALITY_SKIP_FACTOR,
                    RenderQuality.High => Constants.HIGH_QUALITY_SKIP_FACTOR,
                    _ => Constants.MEDIUM_QUALITY_SKIP_FACTOR
                };

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
            },
            new SmartLogger.ErrorHandlingOptions
            {
                Source = $"{Constants.LOG_PREFIX}.ApplyQualitySettings",
                ErrorMessage = "Failed to apply quality settings"
            });
        #endregion

        #region Rendering Implementation
        public override void Render(
            SKCanvas? canvas,
            float[]? spectrum,
            SKImageInfo info,
            float barWidth,
            float barSpacing,
            int barCount,
            SKPaint? paint,
            Action<SKCanvas, SKImageInfo> drawPerformanceInfo) => SmartLogger.Safe(
            () => {
                // Use base class validation
                if (!QuickValidate(canvas, spectrum, info, paint))
                {
                    drawPerformanceInfo?.Invoke(canvas!, info);
                    return;
                }

                // Quick rejection if canvas is outside visible area
                if (canvas!.QuickReject(new SKRect(0, 0, info.Width, info.Height)))
                {
                    drawPerformanceInfo?.Invoke(canvas, info);
                    return;
                }

                _lastImageInfo = info;

                // Check if canvas size has changed - if so, invalidate cache
                if (_cachedDonut != null && (_lastWidth != info.Width || _lastHeight != info.Height))
                {
                    _needsRecreateCache = true;
                    _lastWidth = info.Width;
                    _lastHeight = info.Height;
                }

                // Process spectrum data using base class method
                int pointCount = Math.Min(spectrum!.Length, barCount);
                float[] processedSpectrum = PrepareSpectrum(spectrum, pointCount, spectrum.Length);

                // Compute donut data with processed spectrum
                ComputeDonutData(processedSpectrum, barCount, info);

                if (_dataReady)
                {
                    RenderDonut(canvas, info, paint!);
                }

                drawPerformanceInfo?.Invoke(canvas, info);
            },
            new SmartLogger.ErrorHandlingOptions
            {
                Source = $"{Constants.LOG_PREFIX}.Render",
                ErrorMessage = "Error in render method"
            });
        #endregion

        #region Donut Data Processing
        private void ComputeDonutData(float[] spectrum, int barCount, SKImageInfo info) => SmartLogger.Safe(
            () => {
                UpdateRotationIntensity(spectrum);
                UpdateRotationAngles();
                _rotationMatrix = CreateRotationMatrix();

                float centerX = info.Width * 0.5f;
                float centerY = info.Height * 0.5f;
                float logBarCount = MathF.Log2(barCount + 1);
                float scale = MathF.Min(centerX, centerY) * Constants.DEFAULT_SCALE;

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

                // Sort by depth (from greater to lesser)
                Array.Sort(_projectedVertices, Comparer<ProjectedVertex>.Create((a, b) =>
                    b.Depth.CompareTo(a.Depth)));

                float maxZ = _projectedVertices.Length > 0 ? _projectedVertices[0].Depth : 0f;
                float minZ = _projectedVertices.Length > 0 ? _projectedVertices[^1].Depth : 0f;

                float maxSpectrum = spectrum.Length > 0 ? spectrum.Max() : 0f;

                lock (_renderDataLock)
                {
                    Array.Copy(_projectedVertices, _renderedVertices, _projectedVertices.Length);
                    _currentRenderData = new RenderData(_renderedVertices, minZ, maxZ, maxSpectrum, logBarCount);
                    _dataReady = true;
                }
            },
            new SmartLogger.ErrorHandlingOptions
            {
                Source = $"{Constants.LOG_PREFIX}.ComputeDonutData",
                ErrorMessage = "Error computing donut data"
            });

        private void UpdateRotationIntensity(float[] spectrum) => SmartLogger.Safe(
            () => {
                if (spectrum.Length == 0)
                {
                    _currentRotationIntensity = Constants.DEFAULT_ROTATION_INTENSITY;
                    return;
                }

                float sum = 0f;
                for (int i = 0; i < spectrum.Length; i++)
                    sum += spectrum[i];

                float average = sum / spectrum.Length;
                float newIntensity = Constants.DEFAULT_ROTATION_INTENSITY + average;

                _currentRotationIntensity = _currentRotationIntensity * (1f - Constants.ROTATION_INTENSITY_SMOOTHING) +
                                          newIntensity * Constants.ROTATION_INTENSITY_SMOOTHING;

                _currentRotationIntensity = ClampF(
                    _currentRotationIntensity,
                    Constants.MIN_ROTATION_INTENSITY,
                    Constants.MAX_ROTATION_INTENSITY);
            },
            new SmartLogger.ErrorHandlingOptions
            {
                Source = $"{Constants.LOG_PREFIX}.UpdateRotationIntensity",
                ErrorMessage = "Error updating rotation intensity"
            });

        private void UpdateRotationAngles() => SmartLogger.Safe(
            () => {
                _rotationAngleX = UpdateAngle(
                    _rotationAngleX,
                    Constants.DEFAULT_ROTATION_SPEED_X * _currentRotationIntensity,
                    Constants.ROTATION_SMOOTHING,
                    Constants.MAX_ROTATION_ANGLE_CHANGE);

                _rotationAngleY = UpdateAngle(
                    _rotationAngleY,
                    Constants.DEFAULT_ROTATION_SPEED_Y * _currentRotationIntensity,
                    Constants.ROTATION_SMOOTHING,
                    Constants.MAX_ROTATION_ANGLE_CHANGE);

                _rotationAngleZ = UpdateAngle(
                    _rotationAngleZ,
                    Constants.DEFAULT_ROTATION_SPEED_Z * _currentRotationIntensity,
                    Constants.ROTATION_SMOOTHING,
                    Constants.MAX_ROTATION_ANGLE_CHANGE);
            },
            new SmartLogger.ErrorHandlingOptions
            {
                Source = $"{Constants.LOG_PREFIX}.UpdateRotationAngles",
                ErrorMessage = "Error updating rotation angles"
            });

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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Matrix4x4 CreateRotationMatrix() =>
            Matrix4x4.CreateRotationX(_rotationAngleX) *
            Matrix4x4.CreateRotationY(_rotationAngleY) *
            Matrix4x4.CreateRotationZ(_rotationAngleZ);
        #endregion

        #region Vertex Processing
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private void ProjectVertices(float scale, float centerX, float centerY) => SmartLogger.Safe(
            () => {
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
            },
            new SmartLogger.ErrorHandlingOptions
            {
                Source = $"{Constants.LOG_PREFIX}.ProjectVertices",
                ErrorMessage = "Error projecting vertices"
            });

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

            float rzScaled = rz * Constants.DEFAULT_DEPTH_SCALE_FACTOR;
            float invDepth = 1f / (rzScaled + Constants.DEFAULT_DEPTH_OFFSET);

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
                Depth = rzScaled + Constants.DEFAULT_DEPTH_OFFSET,
                LightIntensity = lightIntensity
            };
        }
        #endregion

        #region Donut Rendering
        private void RenderDonut(SKCanvas canvas, SKImageInfo info, SKPaint paint) => SmartLogger.Safe(
            () => {
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
            },
            new SmartLogger.ErrorHandlingOptions
            {
                Source = $"{Constants.LOG_PREFIX}.RenderDonut",
                ErrorMessage = "Error rendering donut"
            });

        private void RenderDonutInternal(SKCanvas canvas, SKImageInfo info, SKPaint paint, RenderData renderData) => SmartLogger.Safe(
            () => {
                paint.IsAntialias = _useAntiAlias;
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

                    int charIndex = (int)ClampF(vertex.LightIntensity * (_asciiCharStrings.Length - 1), 0, _asciiCharStrings.Length - 1);

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
                        baseAlpha * (Constants.BASE_ALPHA_INTENSITY + renderData.MaxSpectrum * Constants.MAX_SPECTRUM_ALPHA_SCALE) *
                        renderData.AlphaMultiplier, 0, 255);

                    paint.Color = originalColor.WithAlpha(alpha);

                    foreach (var vertex in vertices)
                    {
                        canvas.DrawText(_asciiCharStrings[charIndex],
                            vertex.X - Constants.CHAR_OFFSET_X,
                            vertex.Y + Constants.CHAR_OFFSET_Y,
                            _font, paint);
                    }
                }

                paint.Color = originalColor;
            },
            new SmartLogger.ErrorHandlingOptions
            {
                Source = $"{Constants.LOG_PREFIX}.RenderDonutInternal",
                ErrorMessage = "Error in internal donut rendering"
            });

        private void InvalidateCachedResources() => SmartLogger.Safe(
            () => {
                if (_cachedDonut != null)
                {
                    _cachedDonut.Dispose();
                    _cachedDonut = null;
                }

                // Reset other cached data
                _dataReady = false;
            },
            new SmartLogger.ErrorHandlingOptions
            {
                Source = $"{Constants.LOG_PREFIX}.InvalidateCachedResources",
                ErrorMessage = "Failed to invalidate cached resources"
            });
        #endregion

        #region Initialization Helpers
        private void InitializeVertices() => SmartLogger.Safe(
            () => {
                int segments = Constants.DEFAULT_SEGMENTS;
                int idx = 0;

                for (int i = 0; i < segments; i++)
                {
                    for (int j = 0; j < segments; j++)
                    {
                        float r = Constants.DEFAULT_RADIUS + Constants.DEFAULT_TUBE_RADIUS * _cosTable[j];
                        _vertices[idx++] = new Vertex(
                            r * _cosTable[i],
                            r * _sinTable[i],
                            Constants.DEFAULT_TUBE_RADIUS * _sinTable[j]);
                    }
                }
            },
            new SmartLogger.ErrorHandlingOptions
            {
                Source = $"{Constants.LOG_PREFIX}.InitializeVertices",
                ErrorMessage = "Failed to initialize vertices"
            });

        private void InitializeAlphaCache() => SmartLogger.Safe(
            () => {
                for (int i = 0; i < _asciiCharStrings.Length; i++)
                {
                    float normalizedIndex = i / (float)(_asciiCharStrings.Length - 1);
                    _alphaCache[i] = (byte)((Constants.MIN_ALPHA_VALUE + Constants.ALPHA_RANGE * normalizedIndex) * 255);
                }
            },
            new SmartLogger.ErrorHandlingOptions
            {
                Source = $"{Constants.LOG_PREFIX}.InitializeAlphaCache",
                ErrorMessage = "Failed to initialize alpha cache"
            });
        #endregion

        #region Utility Methods
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float ClampF(float value, float min, float max) =>
            value < min ? min : (value > max ? max : value);
        #endregion

        #region Disposal
        public override void Dispose()
        {
            if (!_disposed)
            {
                SmartLogger.Safe(() =>
                {
                    // Dispose managed resources
                    _font.Dispose();
                    _cachedDonut?.Dispose();

                    // Call base class disposal
                    base.Dispose();

                    SmartLogger.Log(LogLevel.Debug, Constants.LOG_PREFIX, "Disposed");
                },
                new SmartLogger.ErrorHandlingOptions
                {
                    Source = $"{Constants.LOG_PREFIX}.Dispose",
                    ErrorMessage = "Error disposing renderer"
                });
            }
        }
        #endregion
    }
}