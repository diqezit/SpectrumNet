#nullable enable

using static SpectrumNet.SN.Visualization.Renderers.AsciiDonutRenderer.Constants;
using static SpectrumNet.SN.Visualization.Renderers.AsciiDonutRenderer.Constants.Quality;

namespace SpectrumNet.SN.Visualization.Renderers;

public sealed class AsciiDonutRenderer : EffectSpectrumRenderer
{
    private static readonly Lazy<AsciiDonutRenderer> _instance = new(() => new AsciiDonutRenderer());
    private const string LogPrefix = nameof(AsciiDonutRenderer);

    private AsciiDonutRenderer() { }

    public static AsciiDonutRenderer GetInstance() => _instance.Value;

    public record Constants
    {
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
            DEFAULT_SCALE = 0.4f,
            CENTER_PROPORTION = 0.5f;

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

        public const int
            BATCH_SIZE = 128,
            MAX_VERTICES_CACHE_SIZE = 10000;

        public const byte MAX_ALPHA_BYTE = 255;

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

            public const bool
                LOW_USE_PARALLEL_PROCESSING = false,
                MEDIUM_USE_PARALLEL_PROCESSING = false,
                HIGH_USE_PARALLEL_PROCESSING = true;

            public const bool
                LOW_USE_LIGHT_EFFECTS = false,
                MEDIUM_USE_LIGHT_EFFECTS = true,
                HIGH_USE_LIGHT_EFFECTS = true;

            public const float
                LOW_ROTATION_INTENSITY_FACTOR = 0.8f,
                MEDIUM_ROTATION_INTENSITY_FACTOR = 1.0f,
                HIGH_ROTATION_INTENSITY_FACTOR = 1.2f,

                LOW_DETAIL_FACTOR = 0.6f,
                MEDIUM_DETAIL_FACTOR = 1.0f,
                HIGH_DETAIL_FACTOR = 1.5f;

            public const int
                LOW_PARALLEL_THRESHOLD = 5000,
                MEDIUM_PARALLEL_THRESHOLD = 2000,
                HIGH_PARALLEL_THRESHOLD = 1000;
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
    private float _detailFactor;
    private float _rotationIntensityFactor;
    private int _parallelThreshold;
    private bool _useParallelProcessing;
    private bool _useLightEffects;

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

    private bool _isRenderingDataDirty;

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
        base.OnInitialize();
        InitializeResources();
        _logger.Log(LogLevel.Debug, LogPrefix, "Initialized");
    }

    private void InitializeResources() =>
        _logger.Safe(() => HandleInitializeResources(), LogPrefix, "Failed to initialize renderer resources");

    private void HandleInitializeResources()
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

        _dataReady = false;
        _isRenderingDataDirty = true;

        _logger.Log(LogLevel.Debug, LogPrefix, "Resources initialized");
    }

    protected override void OnConfigurationChanged()
    {
        _isRenderingDataDirty = true;
        _logger.Log(LogLevel.Information, LogPrefix,
            $"Configuration changed. New Quality: {Quality}, AntiAlias: {_useAntiAlias}");
    }

    protected override void OnQualitySettingsApplied()
    {
        switch (Quality)
        {
            case RenderQuality.Low:
                LowQualitySettings();
                break;
            case RenderQuality.Medium:
                MediumQualitySettings();
                break;
            case RenderQuality.High:
                HighQualitySettings();
                break;
        }

        _isRenderingDataDirty = true;
        _logger.Log(LogLevel.Information, LogPrefix,
            $"Quality settings applied: {Quality}");
    }

    private void LowQualitySettings()
    {
        _skipVertexCount = LOW_QUALITY_SKIP_FACTOR;
        _useAntiAlias = LOW_USE_ANTIALIASING;
        _useAdvancedEffects = LOW_USE_ADVANCED_EFFECTS;
        _useParallelProcessing = LOW_USE_PARALLEL_PROCESSING;
        _useLightEffects = LOW_USE_LIGHT_EFFECTS;
        _detailFactor = LOW_DETAIL_FACTOR;
        _rotationIntensityFactor = LOW_ROTATION_INTENSITY_FACTOR;
        _parallelThreshold = LOW_PARALLEL_THRESHOLD;
    }

    private void MediumQualitySettings()
    {
        _skipVertexCount = MEDIUM_QUALITY_SKIP_FACTOR;
        _useAntiAlias = MEDIUM_USE_ANTIALIASING;
        _useAdvancedEffects = MEDIUM_USE_ADVANCED_EFFECTS;
        _useParallelProcessing = MEDIUM_USE_PARALLEL_PROCESSING;
        _useLightEffects = MEDIUM_USE_LIGHT_EFFECTS;
        _detailFactor = MEDIUM_DETAIL_FACTOR;
        _rotationIntensityFactor = MEDIUM_ROTATION_INTENSITY_FACTOR;
        _parallelThreshold = MEDIUM_PARALLEL_THRESHOLD;
    }

    private void HighQualitySettings()
    {
        _skipVertexCount = HIGH_QUALITY_SKIP_FACTOR;
        _useAntiAlias = HIGH_USE_ANTIALIASING;
        _useAdvancedEffects = HIGH_USE_ADVANCED_EFFECTS;
        _useParallelProcessing = HIGH_USE_PARALLEL_PROCESSING;
        _useLightEffects = HIGH_USE_LIGHT_EFFECTS;
        _detailFactor = HIGH_DETAIL_FACTOR;
        _rotationIntensityFactor = HIGH_ROTATION_INTENSITY_FACTOR;
        _parallelThreshold = HIGH_PARALLEL_THRESHOLD;
    }

    protected override void RenderEffect(
        SKCanvas canvas,
        float[] spectrum,
        SKImageInfo info,
        float barWidth,
        float barSpacing,
        int barCount,
        SKPaint paint) =>
        _logger.Safe(() => HandleRenderEffect(canvas, spectrum, info, barWidth, barSpacing, barCount, paint),
                  LogPrefix,
                  "Error in RenderEffect method");

    private void HandleRenderEffect(
        SKCanvas canvas,
        float[] spectrum,
        SKImageInfo info,
        float barWidth,
        float barSpacing,
        int barCount,
        SKPaint paint)
    {
        if (HasInfoSizeChanged(info) || _isRenderingDataDirty)
        {
            _lastImageInfo = info;
            _isRenderingDataDirty = false;
            ResetRenderState();
        }

        UpdateState(spectrum, barCount, info);
        if (_dataReady)
        {
            RenderFrame(canvas, info, paint);
        }
    }

    private bool HasInfoSizeChanged(SKImageInfo info) =>
        _lastImageInfo.Width != info.Width || _lastImageInfo.Height != info.Height;

    private void ResetRenderState() =>
        _logger.Safe(() => HandleResetRenderState(), LogPrefix, "Failed to reset render state");

    private void HandleResetRenderState()
    {
        lock (_renderDataLock)
        {
            _dataReady = false;
            _currentRenderData = null;
        }
    }

    private void UpdateState(
        float[] spectrum,
        int barCount,
        SKImageInfo info) =>
        _logger.Safe(() => HandleUpdateState(spectrum, barCount, info), LogPrefix, "Error updating renderer state");

    private void HandleUpdateState(
        float[] spectrum,
        int barCount,
        SKImageInfo info)
    {
        float[] processedSpectrum = ProcessSpectrumForDonut(spectrum, barCount);
        UpdateRotation(processedSpectrum);

        (float minZ, float maxZ, float maxSpectrum, float logBarCount) =
            ProjectAndSortVertices(info, barCount, processedSpectrum);

        PrepareRenderData(minZ, maxZ, maxSpectrum, logBarCount);
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

    private void UpdateRotation(float[] processedSpectrum) =>
        _logger.Safe(() => HandleUpdateRotation(processedSpectrum), LogPrefix, "Error updating rotation");

    private void HandleUpdateRotation(float[] processedSpectrum)
    {
        UpdateRotationIntensity(processedSpectrum);
        UpdateRotationAngles();
        _rotationMatrix = CreateRotationMatrix();
    }

    private void UpdateRotationIntensity(float[] spectrum) =>
        _logger.Safe(() => HandleUpdateRotationIntensity(spectrum), LogPrefix, "Error updating rotation intensity");

    private void HandleUpdateRotationIntensity(float[] spectrum)
    {
        if (spectrum == null || spectrum.Length == 0)
        {
            _currentRotationIntensity = DEFAULT_ROTATION_INTENSITY * _rotationIntensityFactor;
            return;
        }

        float sum = 0f;
        for (int i = 0; i < spectrum.Length; i++)
            sum += spectrum[i];

        float average = sum / spectrum.Length;
        float newIntensity = (DEFAULT_ROTATION_INTENSITY + average) * _rotationIntensityFactor;

        _currentRotationIntensity = _currentRotationIntensity * (1f - ROTATION_INTENSITY_SMOOTHING) +
                                    newIntensity * ROTATION_INTENSITY_SMOOTHING;

        _currentRotationIntensity = ClampF(
            _currentRotationIntensity,
            MIN_ROTATION_INTENSITY,
            MAX_ROTATION_INTENSITY);
    }

    private void UpdateRotationAngles() =>
        _logger.Safe(() => HandleUpdateRotationAngles(), LogPrefix, "Error updating rotation angles");

    private void HandleUpdateRotationAngles()
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
        float centerX = info.Width * CENTER_PROPORTION;
        float centerY = info.Height * CENTER_PROPORTION;
        float scale = MathF.Min(centerX, centerY) * DEFAULT_SCALE * _detailFactor;

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
        float maxSpectrum = 0f;

        if (_useAdvancedEffects && effectiveVertexCount > 0)
        {
            maxSpectrum = _projectedVertices.Take(effectiveVertexCount).Max(v => v.LightIntensity);
        }
        else
        {
            maxSpectrum = processedSpectrum.Length > 0 ? processedSpectrum.Max() : 0f;
        }

        float logBarCount = MathF.Log2(_projectedVertices.Length + 1);

        return (minZ, maxZ, maxSpectrum, logBarCount);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private void ProjectVerticesInternal(
        float scale,
        float centerX,
        float centerY,
        int step) =>
        _logger.Safe(() => HandleProjectVerticesInternal(scale, centerX, centerY, step),
                  LogPrefix,
                  "Error in internal vertex projection");

    private void HandleProjectVerticesInternal(
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

        bool shouldUseParallel = _useParallelProcessing && effectiveVertexCount > _parallelThreshold;

        if (shouldUseParallel)
        {
            Parallel.For(0, effectiveVertexCount, i =>
            {
                int vertexIndex = i * step;
                if (vertexIndex < _vertices.Length)
                {
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
            });
        }
        else
        {
            for (int i = 0; i < effectiveVertexCount; i++)
            {
                int vertexIndex = i * step;
                if (vertexIndex < _vertices.Length)
                {
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
        if (_useAdvancedEffects && _useLightEffects)
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
        float logBarCount) =>
        _logger.Safe(() => HandlePrepareRenderData(minZ, maxZ, maxSpectrum, logBarCount),
                  LogPrefix,
                  "Error preparing render data");

    private void HandlePrepareRenderData(
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
                (int)MathF.Min(_projectedVertices.Length, _renderedVertices.Length));

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
        SKPaint paint) =>
        _logger.Safe(() => HandleRenderFrame(canvas, info, paint), LogPrefix, "Error rendering donut frame");

    private void HandleRenderFrame(
        SKCanvas canvas,
        SKImageInfo info,
        SKPaint paint)
    {
        RenderData renderData;
        lock (_renderDataLock)
        {
            if (!_dataReady || _currentRenderData == null)
                return;
            renderData = _currentRenderData.Value;
        }

        RenderDonutInternal(canvas, info, paint, renderData);
    }

    private void RenderDonutInternal(
        SKCanvas canvas,
        SKImageInfo info,
        SKPaint paint,
        RenderData renderData) =>
        _logger.Safe(() => HandleRenderDonutInternal(canvas, info, paint, renderData),
                  LogPrefix,
                  "Error in internal donut rendering");

    private void HandleRenderDonutInternal(
        SKCanvas canvas,
        SKImageInfo info,
        SKPaint paint,
        RenderData renderData)
    {
        paint.IsAntialias = _useAntiAlias;
        var originalColor = paint.Color;

        GroupVerticesByChar(renderData.Vertices, info, renderData);
        DrawGroupedVertices(canvas, paint, originalColor, renderData);

        paint.Color = originalColor;
    }

    private void GroupVerticesByChar(
        ProjectedVertex[] vertices,
        SKImageInfo info,
        RenderData renderData) =>
        _logger.Safe(() => HandleGroupVerticesByChar(vertices, info, renderData),
                  LogPrefix,
                  "Error grouping vertices by character");

    private void HandleGroupVerticesByChar(
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
            if (normalizedDepth < 0f || normalizedDepth > 1f)
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

            if (groupedVertices.Count < MAX_VERTICES_CACHE_SIZE)
            {
                groupedVertices.Add(vertex);
            }
        }
    }

    private void DrawGroupedVertices(
        SKCanvas canvas,
        SKPaint paint,
        SKColor originalColor,
        RenderData renderData) =>
        _logger.Safe(() => HandleDrawGroupedVertices(canvas, paint, originalColor, renderData),
                  LogPrefix,
                  "Error drawing grouped vertices");

    private void HandleDrawGroupedVertices(
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
                MAX_ALPHA_BYTE);

            paint.Color = originalColor.WithAlpha(alpha);

            BatchDrawCharacters(canvas, vertices, _asciiCharStrings[charIndex], _font!, paint);
        }
    }

    private static void BatchDrawCharacters(
        SKCanvas canvas,
        List<ProjectedVertex> vertices,
        string charStr,
        SKFont font,
        SKPaint paint)
    {
        int batchSize = Constants.BATCH_SIZE;

        if (vertices.Count <= batchSize)
        {
            foreach (var vertex in vertices)
            {
                canvas.DrawText(
                    charStr,
                    vertex.X - CHAR_OFFSET_X,
                    vertex.Y + CHAR_OFFSET_Y,
                    font,
                    paint);
            }
        }
        else
        {
            int batches = (vertices.Count + batchSize - 1) / batchSize;

            for (int i = 0; i < batches; i++)
            {
                int startIdx = i * batchSize;
                int endIdx = Math.Min(startIdx + batchSize, vertices.Count);

                for (int j = startIdx; j < endIdx; j++)
                {
                    var vertex = vertices[j];
                    canvas.DrawText(
                        charStr,
                        vertex.X - CHAR_OFFSET_X,
                        vertex.Y + CHAR_OFFSET_Y,
                        font,
                        paint);
                }
            }
        }
    }

    protected override void OnInvalidateCachedResources()
    {
        base.OnInvalidateCachedResources();
        _dataReady = false;
        _currentRenderData = null;
        _isRenderingDataDirty = true;
        _logger.Log(LogLevel.Debug, LogPrefix, "Cached resources invalidated");
    }

    private void InitializeVertices() =>
        _logger.Safe(() => HandleInitializeVertices(), LogPrefix, "Failed to initialize vertices");

    private void HandleInitializeVertices()
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

        _logger.Log(LogLevel.Debug,
            LogPrefix,
            $"Vertices initialized - count: {_vertices.Length}");
    }

    private void InitializeAlphaCache() =>
        _logger.Safe(() => HandleInitializeAlphaCache(), LogPrefix, "Failed to initialize alpha cache");

    private void HandleInitializeAlphaCache()
    {
        for (int i = 0; i < _asciiCharStrings.Length; i++)
        {
            float normalizedIndex = i / (float)(_asciiCharStrings.Length - 1);
            _alphaCache[i] = (byte)((MIN_ALPHA_VALUE + ALPHA_RANGE * normalizedIndex) * MAX_ALPHA_BYTE);
        }

        _logger.Log(LogLevel.Debug, LogPrefix, $"Alpha cache initialized - count: {_alphaCache.Length}");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float ClampF(
        float value,
        float min,
        float max) =>
        value < min ? min : value > max ? max : value;

    public override bool RequiresRedraw() =>
        base.RequiresRedraw() || _isRenderingDataDirty;

    protected override void OnDispose()
    {
        DisposeManagedResources();
        base.OnDispose();
        _logger.Log(LogLevel.Debug, LogPrefix, "Disposed");
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

        _dataReady = false;
        _currentRenderData = null;
    }
}