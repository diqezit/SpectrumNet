#nullable enable

using static SpectrumNet.Views.Renderers.AsciiDonutRenderer.Constants;

namespace SpectrumNet.Views.Renderers;

public sealed class AsciiDonutRenderer : EffectSpectrumRenderer
{
    private static readonly Lazy<AsciiDonutRenderer> _instance = new(() => new AsciiDonutRenderer());

    private AsciiDonutRenderer() { }

    public static AsciiDonutRenderer GetInstance() => _instance.Value;

    public record Constants
    {
        public const string LOG_PREFIX = "AsciiDonutRenderer";

        public const float
            DEFAULT_ROTATION_SPEED_X = 0.01f,
            DEFAULT_ROTATION_SPEED_Y = 0.02f,
            DEFAULT_ROTATION_SPEED_Z = 0.005f,
            DEFAULT_ROTATION_INTENSITY = 1.0f,
            DEFAULT_DEPTH_SCALE_FACTOR = 2.0f,
            DEFAULT_DEPTH_OFFSET = 3.0f;

        public const float
            MIN_ROTATION_INTENSITY = 0.5f,
            MAX_ROTATION_INTENSITY = 2.0f,
            MAX_ROTATION_ANGLE_CHANGE = 0.1f,
            ROTATION_INTENSITY_SMOOTHING = 0.2f,
            ROTATION_SMOOTHING = 0.1f;

        public const float
            LIGHT_DIR_X = 0.6f,
            LIGHT_DIR_Y = 0.6f,
            LIGHT_DIR_Z = -1.0f;

        public const int DEFAULT_SEGMENTS = 36;
        public const float
            DEFAULT_RADIUS = 1.0f,
            DEFAULT_TUBE_RADIUS = 0.5f,
            DEFAULT_SCALE = 0.4f;

        public const float
            MIN_ALPHA_VALUE = 0.2f,
            ALPHA_RANGE = 0.8f,
            BASE_ALPHA_INTENSITY = 0.7f,
            MAX_SPECTRUM_ALPHA_SCALE = 0.3f;

        public const float
            CHAR_OFFSET_X = 4.0f,
            CHAR_OFFSET_Y = 4.0f,
            DEFAULT_FONT_SIZE = 12.0f;
        public const string DEFAULT_ASCII_CHARS = " .,-~:;=!*#$@";

        public const int BATCH_SIZE = 128;

        public static class Quality
        {
            public const int
                LOW_QUALITY_SKIP_FACTOR = 3,
                MEDIUM_QUALITY_SKIP_FACTOR = 1,
                HIGH_QUALITY_SKIP_FACTOR = 0;

            public const bool
                LOW_USE_ADVANCED_EFFECTS = false,
                MEDIUM_USE_ADVANCED_EFFECTS = true,
                HIGH_USE_ADVANCED_EFFECTS = true;

            public const bool
                LOW_USE_ANTIALIASING = false,
                MEDIUM_USE_ANTIALIASING = true,
                HIGH_USE_ANTIALIASING = true;
        }
    }

    private readonly record struct Vertex(float X, float Y, float Z);

    private struct ProjectedVertex
    {
        public float X, Y, Depth, LightIntensity;
    }

    private readonly record struct RenderData(
        ProjectedVertex[] Vertices,
        float MinZ,
        float MaxZ,
        float MaxSpectrum,
        float LogBarCount)
    {
        public readonly float
            DepthRange = MaxZ - MinZ + float.Epsilon,
            AlphaMultiplier = 1f + LogBarCount * 0.1f;
    }

    private static readonly string[] _asciiCharStrings = [..
        DEFAULT_ASCII_CHARS
        .ToCharArray()
        .Select(c => c.ToString())
        ];

    private static readonly float[] _cosTable, _sinTable;

    private static readonly Vector3 _lightDirection = Vector3.Normalize(new Vector3(
        LIGHT_DIR_X,
        LIGHT_DIR_Y,
        LIGHT_DIR_Z));

    private int _skipVertexCount;
    private new bool _useAdvancedEffects;
    private new bool _useAntiAlias;

    private float _rotationAngleX, _rotationAngleY, _rotationAngleZ;
    private float _currentRotationIntensity = DEFAULT_ROTATION_INTENSITY;
    private Matrix4x4 _rotationMatrix = Matrix4x4.Identity;
    private bool _dataReady;
    private RenderData? _currentRenderData;
    private SKImageInfo _lastImageInfo;

    private Vertex[] _vertices = [];
    private ProjectedVertex[] _projectedVertices = [];
    private ProjectedVertex[] _renderedVertices = [];
    private byte[] _alphaCache = [];

    private SKFont? _font;
    private readonly Dictionary<int, List<ProjectedVertex>> _verticesByCharIndex = [];
    private readonly object _renderDataLock = new();

    static AsciiDonutRenderer()
    {
        int segments = DEFAULT_SEGMENTS;
        _cosTable = new float[segments];
        _sinTable = new float[segments];

        for (int i = 0; i < segments; i++)
        {
            float angle = i * MathF.PI * 2f / segments;
            _cosTable[i] = MathF.Cos(angle);
            _sinTable[i] = MathF.Sin(angle);
        }
    }

    protected override void OnInitialize()
    {
        ExecuteSafely(
            () =>
            {
                base.OnInitialize();
                InitializeResources();
                ApplyQualitySettings();
                Log(LogLevel.Debug, LOG_PREFIX, "Initialized");
            },
            nameof(OnInitialize),
            "Failed to initialize renderer"
        );
    }

    private void InitializeResources()
    {
        ExecuteSafely(
            () =>
            {
                int segments = DEFAULT_SEGMENTS;
                _vertices = new Vertex[segments * segments];
                _projectedVertices = new ProjectedVertex[segments * segments];
                _renderedVertices = new ProjectedVertex[segments * segments];
                _alphaCache = new byte[DEFAULT_ASCII_CHARS.Length];

                _font = new SKFont
                {
                    Size = DEFAULT_FONT_SIZE,
                    Hinting = SKFontHinting.None
                };

                InitializeVertices();
                InitializeAlphaCache();
            },
            nameof(InitializeResources),
            "Failed to initialize renderer resources"
        );
    }

    public override void Configure(
        bool isOverlayActive,
        RenderQuality quality = RenderQuality.Medium)
    {
        ExecuteSafely(
            () =>
            {
                bool configChanged = _isOverlayActive != isOverlayActive || Quality != quality;
                base.Configure(isOverlayActive, quality);

                if (configChanged)
                {
                    OnConfigurationChanged();
                }
            },
            nameof(Configure),
            "Failed to configure renderer"
        );
    }

    protected override void OnConfigurationChanged()
    {
        ExecuteSafely(
            () =>
            {
                Log(LogLevel.Debug,
                    LOG_PREFIX,
                    $"Configuration changed. New Quality: {Quality}");
            },
            nameof(OnConfigurationChanged),
            "Failed to handle configuration change"
        );
    }

    protected override void ApplyQualitySettings()
    {
        ExecuteSafely(
            () =>
            {
                base.ApplyQualitySettings();
                ApplyQualityBasedSettings();
                Log(LogLevel.Debug, LOG_PREFIX, $"Quality changed to {Quality}");
            },
            nameof(ApplyQualitySettings),
            "Failed to apply quality settings"
        );
    }

    private void ApplyQualityBasedSettings()
    {
        int oldSkipVertexCount = _skipVertexCount;

        switch (Quality)
        {
            case RenderQuality.Low:
                ApplyLowQualitySettings();
                break;
            case RenderQuality.Medium:
                ApplyMediumQualitySettings();
                break;
            case RenderQuality.High:
                ApplyHighQualitySettings();
                break;
        }

        if (_skipVertexCount != oldSkipVertexCount)
        {
            lock (_renderDataLock)
            {
                _dataReady = false;
                _currentRenderData = null;
            }
            OnInvalidateCachedResources();
        }
    }

    private void ApplyLowQualitySettings()
    {
        _skipVertexCount = Constants.Quality.LOW_QUALITY_SKIP_FACTOR;
        _useAntiAlias = Constants.Quality.LOW_USE_ANTIALIASING;
        _useAdvancedEffects = Constants.Quality.LOW_USE_ADVANCED_EFFECTS;
    }

    private void ApplyMediumQualitySettings()
    {
        _skipVertexCount = Constants.Quality.MEDIUM_QUALITY_SKIP_FACTOR;
        _useAntiAlias = Constants.Quality.MEDIUM_USE_ANTIALIASING;
        _useAdvancedEffects = Constants.Quality.MEDIUM_USE_ADVANCED_EFFECTS;
    }

    private void ApplyHighQualitySettings()
    {
        _skipVertexCount = Constants.Quality.HIGH_QUALITY_SKIP_FACTOR;
        _useAntiAlias = Constants.Quality.HIGH_USE_ANTIALIASING;
        _useAdvancedEffects = Constants.Quality.HIGH_USE_ADVANCED_EFFECTS;
    }

    protected override void RenderEffect(
        SKCanvas canvas,
        float[] spectrum,
        SKImageInfo info,
        float barWidth,
        float barSpacing,
        int barCount,
        SKPaint paint)
    {
        if (!ValidateRenderParameters(canvas, spectrum, info, paint))
            return;

        ExecuteSafely(
            () =>
            {
                UpdateState(spectrum, barCount, info);
                if (_dataReady)
                {
                    RenderFrame(canvas, info, paint);
                }
            },
            nameof(RenderEffect),
            "Error in RenderEffect method"
        );
    }

    private bool ValidateRenderParameters(
        SKCanvas? canvas,
        float[]? spectrum,
        SKImageInfo info,
        SKPaint? paint)
    {
        if (!_isInitialized)
        {
            Log(LogLevel.Error, LOG_PREFIX, "Renderer is not initialized");
            return false;
        }
        if (!IsCanvasValid(canvas)) return false;
        if (!IsSpectrumValid(spectrum)) return false;
        if (!IsPaintValid(paint)) return false;
        if (!AreDimensionsValid(info)) return false;
        if (IsDisposed()) return false;

        return true;
    }

    private static bool IsCanvasValid(SKCanvas? canvas)
    {
        if (canvas != null) return true;
        Log(LogLevel.Error, LOG_PREFIX, "Canvas is null");
        return false;
    }

    private static bool IsSpectrumValid(float[]? spectrum)
    {
        if (spectrum != null) return true;
        Log(LogLevel.Error, LOG_PREFIX, "Spectrum is null");
        return false;
    }

    private static bool IsPaintValid(SKPaint? paint)
    {
        if (paint != null) return true;
        Log(LogLevel.Error, LOG_PREFIX, "Paint is null");
        return false;
    }

    private static bool AreDimensionsValid(SKImageInfo info)
    {
        if (info.Width > 0 && info.Height > 0) return true;
        Log(LogLevel.Error, LOG_PREFIX, $"Invalid image dimensions: {info.Width}x{info.Height}");
        return false;
    }

    private bool IsDisposed()
    {
        if (!_disposed) return false;
        Log(LogLevel.Error, LOG_PREFIX, "Renderer is disposed");
        return true;
    }

    private void UpdateState(
        float[] spectrum,
        int barCount,
        SKImageInfo info)
    {
        ExecuteSafely(
            () =>
            {
                _lastImageInfo = info;
                float[] processedSpectrum = ProcessSpectrumForDonut(spectrum, barCount);
                UpdateRotation(processedSpectrum);

                (float minZ, float maxZ, float maxSpectrum, float logBarCount) =
                    ProjectAndSortVertices(info, barCount, processedSpectrum);

                PrepareRenderData(minZ, maxZ, maxSpectrum, logBarCount);
            },
            nameof(UpdateState),
            "Error updating renderer state"
        );
    }

    private static float[] ProcessSpectrumForDonut(
        float[] spectrum,
        int barCount)
    {
        int targetCount = (int)MathF.Min(spectrum.Length, barCount);
        float[] processedSpectrum = new float[targetCount];
        if (spectrum.Length == 0 || targetCount <= 0)
            return processedSpectrum;

        float blockSize = (float)spectrum.Length / targetCount;

        for (int i = 0; i < targetCount; i++)
        {
            float sum = 0;
            int start = (int)(i * blockSize);
            int end = (int)((i + 1) * blockSize);
            int actualEnd = (int)MathF.Min(end, spectrum.Length);
            int count = actualEnd - start;

            if (count <= 0)
            {
                processedSpectrum[i] = 0;
                continue;
            }

            for (int j = start; j < actualEnd; j++)
            {
                sum += spectrum[j];
            }

            processedSpectrum[i] = sum / count;
        }

        return processedSpectrum;
    }

    private void UpdateRotation(float[] processedSpectrum)
    {
        UpdateRotationIntensity(processedSpectrum);
        UpdateRotationAngles();
        _rotationMatrix = CreateRotationMatrix();
    }

    private void UpdateRotationIntensity(float[] spectrum)
    {
        ExecuteSafely(
            () =>
            {
                if (spectrum == null || spectrum.Length == 0)
                {
                    _currentRotationIntensity = DEFAULT_ROTATION_INTENSITY;
                    return;
                }

                float sum = 0f;
                for (int i = 0; i < spectrum.Length; i++)
                    sum += spectrum[i];

                float average = sum / spectrum.Length;
                float newIntensity = DEFAULT_ROTATION_INTENSITY + average;

                _currentRotationIntensity = _currentRotationIntensity * (1f - ROTATION_INTENSITY_SMOOTHING) +
                                            newIntensity * ROTATION_INTENSITY_SMOOTHING;

                _currentRotationIntensity = ClampF(
                    _currentRotationIntensity,
                    MIN_ROTATION_INTENSITY,
                    MAX_ROTATION_INTENSITY);
            },
            nameof(UpdateRotationIntensity),
            "Error updating rotation intensity"
        );
    }

    private void UpdateRotationAngles()
    {
        ExecuteSafely(
            () =>
            {
                _rotationAngleX = UpdateAngle(
                    _rotationAngleX,
                    DEFAULT_ROTATION_SPEED_X * _currentRotationIntensity,
                    ROTATION_SMOOTHING,
                    MAX_ROTATION_ANGLE_CHANGE);

                _rotationAngleY = UpdateAngle(
                    _rotationAngleY,
                    DEFAULT_ROTATION_SPEED_Y * _currentRotationIntensity,
                    ROTATION_SMOOTHING,
                    MAX_ROTATION_ANGLE_CHANGE);

                _rotationAngleZ = UpdateAngle(
                    _rotationAngleZ,
                    DEFAULT_ROTATION_SPEED_Z * _currentRotationIntensity,
                    ROTATION_SMOOTHING,
                    MAX_ROTATION_ANGLE_CHANGE);
            },
            nameof(UpdateRotationAngles),
            "Error updating rotation angles"
        );
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float UpdateAngle(
        float current,
        float speed,
        float smoothing,
        float maxChange)
    {
        float target = current + speed;
        float diff = MinimalAngleDiff(current, target);
        float clampedDiff = ClampF(diff, -maxChange, maxChange);
        return current + clampedDiff * smoothing;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float MinimalAngleDiff(
        float a,
        float b)
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

    private (float minZ, float maxZ, float maxSpectrum, float logBarCount) ProjectAndSortVertices(
        SKImageInfo info,
        int barCount,
        float[] processedSpectrum)
    {
        float centerX = info.Width * 0.5f;
        float centerY = info.Height * 0.5f;
        float scale = MathF.Min(centerX, centerY) * DEFAULT_SCALE;

        int step = _skipVertexCount + 1;
        int effectiveVertexCount = _vertices.Length / step;

        if (_projectedVertices.Length != effectiveVertexCount)
        {
            _projectedVertices = new ProjectedVertex[effectiveVertexCount];
            _renderedVertices = new ProjectedVertex[effectiveVertexCount];
        }

        ProjectVerticesInternal(scale, centerX, centerY, step);

        Array.Sort(
            _projectedVertices,
            0,
            effectiveVertexCount,
            Comparer<ProjectedVertex>.Create((a, b) => b.Depth.CompareTo(a.Depth)));

        float maxZ = effectiveVertexCount > 0 ? _projectedVertices[0].Depth : 0f;
        float minZ = effectiveVertexCount > 0 ? _projectedVertices[effectiveVertexCount - 1].Depth : 0f;
        float maxSpectrum = effectiveVertexCount > 0 ? _projectedVertices.Max(v => v.LightIntensity) : 0f;
        float logBarCount = MathF.Log2(_projectedVertices.Length + 1);

        return (minZ, maxZ, maxSpectrum, logBarCount);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private void ProjectVerticesInternal(
        float scale,
        float centerX,
        float centerY,
        int step)
    {
        float m11 = _rotationMatrix.M11, m12 = _rotationMatrix.M12, m13 = _rotationMatrix.M13;
        float m21 = _rotationMatrix.M21, m22 = _rotationMatrix.M22, m23 = _rotationMatrix.M23;
        float m31 = _rotationMatrix.M31, m32 = _rotationMatrix.M32, m33 = _rotationMatrix.M33;

        int effectiveVertexCount = _vertices.Length / step;

        if (_projectedVertices.Length != effectiveVertexCount)
        {
            _projectedVertices = new ProjectedVertex[effectiveVertexCount];
            _renderedVertices = new ProjectedVertex[effectiveVertexCount];
        }

        if (Quality == RenderQuality.High || effectiveVertexCount > 1000)
        {
            Parallel.For(0, effectiveVertexCount, i =>
            {
                int vertexIndex = i * step;
                var vertex = _vertices[vertexIndex];
                ProjectSingleVertex(
                    vertex,
                    m11, m12, m13,
                    m21, m22, m23,
                    m31, m32, m33,
                    scale,
                    centerX,
                    centerY,
                    i);
            });
        }
        else
        {
            for (int i = 0; i < effectiveVertexCount; i++)
            {
                int vertexIndex = i * step;
                var vertex = _vertices[vertexIndex];
                ProjectSingleVertex(
                    vertex,
                    m11, m12, m13,
                    m21, m22, m23,
                    m31, m32, m33,
                    scale,
                    centerX,
                    centerY,
                    i);
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ProjectSingleVertex(
        Vertex vertex,
        float m11, float m12, float m13,
        float m21, float m22, float m23,
        float m31, float m32, float m33,
        float scale,
        float centerX,
        float centerY,
        int targetIndex)
    {
        float rx = vertex.X * m11 + vertex.Y * m21 + vertex.Z * m31;
        float ry = vertex.X * m12 + vertex.Y * m22 + vertex.Z * m32;
        float rz = vertex.X * m13 + vertex.Y * m23 + vertex.Z * m33;

        float rzScaled = rz * DEFAULT_DEPTH_SCALE_FACTOR;
        float invDepth = 1f / (rzScaled + DEFAULT_DEPTH_OFFSET);

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
            Depth = rzScaled + DEFAULT_DEPTH_OFFSET,
            LightIntensity = lightIntensity
        };
    }

    private void PrepareRenderData(
        float minZ,
        float maxZ,
        float maxSpectrum,
        float logBarCount)
    {
        lock (_renderDataLock)
        {
            Array.Copy(
                _projectedVertices,
                _renderedVertices,
                _projectedVertices.Length);
            _currentRenderData = new RenderData(
                _renderedVertices,
                minZ,
                maxZ,
                maxSpectrum,
                logBarCount);
            _dataReady = true;
        }
    }

    private void RenderFrame(
        SKCanvas canvas,
        SKImageInfo info,
        SKPaint paint)
    {
        ExecuteSafely(
            () =>
            {
                RenderData renderData;
                lock (_renderDataLock)
                {
                    if (!_dataReady || _currentRenderData == null)
                        return;
                    renderData = _currentRenderData.Value;
                }

                RenderDonutInternal(canvas, info, paint, renderData);
            },
            nameof(RenderFrame),
            "Error rendering donut frame"
        );
    }

    private void RenderDonutInternal(
        SKCanvas canvas,
        SKImageInfo info,
        SKPaint paint,
        RenderData renderData)
    {
        ExecuteSafely(
            () =>
            {
                paint.IsAntialias = _useAntiAlias;
                var originalColor = paint.Color;

                GroupVerticesByChar(renderData.Vertices, info, renderData);
                DrawGroupedVertices(canvas, paint, originalColor, renderData);

                paint.Color = originalColor;
            },
            nameof(RenderDonutInternal),
            "Error in internal donut rendering"
        );
    }

    private void GroupVerticesByChar(
        ProjectedVertex[] vertices,
        SKImageInfo info,
        RenderData renderData)
    {
        _verticesByCharIndex.Clear();

        foreach (var vertex in vertices)
        {
            if (vertex.X < 0 || vertex.X >= info.Width || vertex.Y < 0 || vertex.Y >= info.Height)
                continue;

            float normalizedDepth = (vertex.Depth - renderData.MinZ) / renderData.DepthRange;
            if (normalizedDepth is < 0f or > 1f)
                continue;

            int charIndex = (int)ClampF(
                vertex.LightIntensity * (_asciiCharStrings.Length - 1),
                0,
                _asciiCharStrings.Length - 1);

            if (!_verticesByCharIndex.TryGetValue(charIndex, out var groupedVertices))
            {
                groupedVertices = [];
                _verticesByCharIndex[charIndex] = groupedVertices;
            }
            groupedVertices.Add(vertex);
        }
    }

    private void DrawGroupedVertices(
        SKCanvas canvas,
        SKPaint paint,
        SKColor originalColor,
        RenderData renderData)
    {
        foreach (var kvp in _verticesByCharIndex)
        {
            int charIndex = kvp.Key;
            var vertices = kvp.Value;

            byte baseAlpha = _alphaCache[charIndex];
            byte alpha = (byte)ClampF(
                baseAlpha * (BASE_ALPHA_INTENSITY + renderData.MaxSpectrum * MAX_SPECTRUM_ALPHA_SCALE) *
                renderData.AlphaMultiplier,
                0,
                255);

            paint.Color = originalColor.WithAlpha(alpha);

            foreach (var vertex in vertices)
            {
                canvas.DrawText(
                    _asciiCharStrings[charIndex],
                    vertex.X - CHAR_OFFSET_X,
                    vertex.Y + CHAR_OFFSET_Y,
                    _font!,
                    paint);
            }
        }
    }

    protected override void OnInvalidateCachedResources()
    {
        ExecuteSafely(
            () =>
            {
                _dataReady = false;
                _currentRenderData = null;
            },
            nameof(OnInvalidateCachedResources),
            "Failed to invalidate cached resources"
        );
    }

    private void InitializeVertices()
    {
        ExecuteSafely(
            () =>
            {
                int segments = DEFAULT_SEGMENTS;
                int idx = 0;

                for (int i = 0; i < segments; i++)
                {
                    for (int j = 0; j < segments; j++)
                    {
                        float r = DEFAULT_RADIUS + DEFAULT_TUBE_RADIUS * _cosTable[j];
                        _vertices[idx++] = new Vertex(
                            r * _cosTable[i],
                            r * _sinTable[i],
                            DEFAULT_TUBE_RADIUS * _sinTable[j]);
                    }
                }
            },
            nameof(InitializeVertices),
            "Failed to initialize vertices"
        );
    }

    private void InitializeAlphaCache()
    {
        ExecuteSafely(
            () =>
            {
                for (int i = 0; i < _asciiCharStrings.Length; i++)
                {
                    float normalizedIndex = i / (float)(_asciiCharStrings.Length - 1);
                    _alphaCache[i] = (byte)((MIN_ALPHA_VALUE + ALPHA_RANGE * normalizedIndex) * 255);
                }
            },
            nameof(InitializeAlphaCache),
            "Failed to initialize alpha cache"
        );
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float ClampF(
        float value,
        float min,
        float max) =>
        value < min ? min : value > max ? max : value;

    public override void Dispose()
    {
        if (_disposed) return;

        ExecuteSafely(
            () =>
            {
                OnDispose();
            },
            nameof(Dispose),
            "Error during renderer disposal"
        );

        _disposed = true;
        GC.SuppressFinalize(this);
        Log(LogLevel.Debug, LOG_PREFIX, "Disposed");
    }

    protected override void OnDispose()
    {
        ExecuteSafely(
            () =>
            {
                DisposeManagedResources();
                base.OnDispose();
            },
            nameof(OnDispose),
            "Error during OnDispose"
        );
    }

    private void DisposeManagedResources()
    {
        _font?.Dispose();
        _font = null;

        _verticesByCharIndex.Clear();

        _vertices = [];
        _projectedVertices = [];
        _renderedVertices = [];
        _alphaCache = [];
    }
}