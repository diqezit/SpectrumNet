#nullable enable

using static SpectrumNet.Views.Renderers.CubeRenderer.Constants;
using static SpectrumNet.Views.Renderers.CubeRenderer.Constants.Quality;

namespace SpectrumNet.Views.Renderers;

public sealed class CubeRenderer : EffectSpectrumRenderer
{
    private static readonly Lazy<CubeRenderer> _instance = new(() => new CubeRenderer());

    public record Constants
    {
        public const string LOG_PREFIX = "CubeRenderer";

        public const float
            CUBE_HALF_SIZE = 0.5f,
            BASE_CUBE_SIZE = 0.5f,
            MIN_CUBE_SIZE = 0.2f,
            MAX_CUBE_SIZE = 1.0f,
            CUBE_SIZE_RESPONSE_FACTOR = 0.5f;

        public const float
            BASE_ROTATION_SPEED = 0.02f,
            SPECTRUM_ROTATION_INFLUENCE = 0.015f,
            MAX_ROTATION_SPEED = 0.05f;

        public const float
            AMBIENT_LIGHT = 0.4f,
            DIFFUSE_LIGHT = 0.6f,
            MIN_DIFFUSE_LIGHT = 0f;

        public const float
            BASE_ALPHA = 0.9f,
            SPECTRUM_ALPHA_INFLUENCE = 0.1f,
            EDGE_ALPHA_MULTIPLIER = 0.8f;

        public const int THREAD_JOIN_TIMEOUT_MS = 100;

        public const float
            CENTER_PROPORTION = 0.5f,
            DEFAULT_DELTA_TIME = 1.0f / 60.0f,
            MIN_SCALE_FACTOR = 1.0f,
            MAX_SCALE_FACTOR = 2.5f,
            SCALE_LOG_FACTOR = 0.3f,
            FACE_DEPTH_AVERAGE_FACTOR = 3.0f,
            CUBE_SIZE_SMOOTHING = 0.9f,
            TARGET_SIZE_INFLUENCE = 0.1f,
            INITIAL_SPEED_X_FACTOR = 0.8f,
            INITIAL_SPEED_Y_FACTOR = 1.2f,
            INITIAL_SPEED_Z_FACTOR = 0.6f,
            MIN_SPECTRUM_FOR_ROTATION = 3,
            ROTATION_SPEED_SMOOTHING = 0.95f,
            TARGET_SPEED_INFLUENCE = 0.05f,
            MID_FREQ_SPEED_FACTOR = 1.2f,
            HIGH_FREQ_SPEED_FACTOR = 0.8f,
            BASE_ROTATION_FACTOR = 1f;

        public const byte
            EDGE_STROKE_WIDTH = 1,
            EDGE_BLUR_RADIUS = 2,
            MIN_COLOR_BYTE = 0,
            MAX_COLOR_BYTE = 255,
            FACE_COLOR_COMPONENT_LOW = 100;

        public static class Quality
        {
            public const bool
                LOW_USE_ANTIALIASING = false,
                MEDIUM_USE_ANTIALIASING = true,
                HIGH_USE_ANTIALIASING = true;

            public const bool
                LOW_USE_ADVANCED_EFFECTS = false,
                MEDIUM_USE_ADVANCED_EFFECTS = true,
                HIGH_USE_ADVANCED_EFFECTS = true;

            public const bool
                LOW_USE_GLOW_EFFECTS = false,
                MEDIUM_USE_GLOW_EFFECTS = true,
                HIGH_USE_GLOW_EFFECTS = true;

            public const byte
                LOW_EDGE_BLUR_RADIUS = 0,
                MEDIUM_EDGE_BLUR_RADIUS = 2,
                HIGH_EDGE_BLUR_RADIUS = 4;

            public const byte
                LOW_EDGE_STROKE_WIDTH = 1,
                MEDIUM_EDGE_STROKE_WIDTH = 1,
                HIGH_EDGE_STROKE_WIDTH = 2;
        }
    }

    private static readonly Vector3 LIGHT_DIRECTION = Vector3.Normalize(new Vector3(
        CENTER_PROPORTION,
        0.7f,
        -1.0f));

    private static readonly SKColor[] FACE_COLORS =
    [
        new SKColor(MAX_COLOR_BYTE, FACE_COLOR_COMPONENT_LOW, FACE_COLOR_COMPONENT_LOW),
        new SKColor(FACE_COLOR_COMPONENT_LOW, MAX_COLOR_BYTE, FACE_COLOR_COMPONENT_LOW),
        new SKColor(FACE_COLOR_COMPONENT_LOW, FACE_COLOR_COMPONENT_LOW, MAX_COLOR_BYTE),
        new SKColor(MAX_COLOR_BYTE, MAX_COLOR_BYTE, FACE_COLOR_COMPONENT_LOW),
        new SKColor(MAX_COLOR_BYTE, FACE_COLOR_COMPONENT_LOW, MAX_COLOR_BYTE),
        new SKColor(FACE_COLOR_COMPONENT_LOW, MAX_COLOR_BYTE, MAX_COLOR_BYTE)
    ];

    private readonly record struct Vertex(float X, float Y, float Z);
    private struct ProjectedVertex { public float X, Y, Depth; }
    private readonly record struct Face(int V1, int V2, int V3, int FaceIndex);

    private readonly record struct RenderData(
        float MaxSpectrum,
        float CubeSize,
        int BarCount);

    private readonly Vertex[] _vertices;
    private readonly Vector3[] _faceNormalVectors;
    private Face[] _faces;
    private readonly ProjectedVertex[] _projectedVertices;
    private float[] _faceDepths;
    private float[] _faceNormals;
    private float[] _faceLightIntensities;
    private readonly SKPaint[] _facePaints;
    private readonly SKPaint _edgePaint;

    private float _rotationAngleX;
    private float _rotationAngleY;
    private float _rotationAngleZ;
    private Matrix4x4 _rotationMatrix = Matrix4x4.Identity;
    private DateTime _lastRenderTime;
    private float _deltaTime;
    private SKImageInfo _lastImageInfo;
    private float _rotationSpeedX;
    private float _rotationSpeedY;
    private float _rotationSpeedZ;
    private float _currentCubeSize = BASE_CUBE_SIZE;

    private Thread? _processingThread;
    private CancellationTokenSource? _cts;
    private AutoResetEvent? _spectrumDataAvailable;
    private AutoResetEvent? _processingComplete;
    private readonly object _renderDataLock = new();
    private volatile bool _processingRunning;
    private volatile bool _isConfiguring;
    private bool _dataReady;
    private float[]? _spectrumToProcess;
    private int _barCountToProcess;
    private RenderData? _currentRenderData;

    private bool _useGlowEffects;
    private new bool _useAdvancedEffects;
    private new bool _useAntiAlias;
    private byte _edgeBlurRadius;
    private byte _edgeStrokeWidth;

    private CubeRenderer()
    {
        _vertices = CreateCubeVertices();
        _faces = CreateCubeFaces();
        _faceNormalVectors = CalculateFaceNormals();
        _projectedVertices = new ProjectedVertex[_vertices.Length];
        _faceDepths = new float[_faces.Length];
        _faceNormals = new float[_faces.Length];
        _faceLightIntensities = new float[_faces.Length];
        _facePaints = CreateFacePaints();
        _edgePaint = CreateEdgePaint();
        InitializeRotationSpeeds();
    }

    public static CubeRenderer GetInstance() => _instance.Value;

    protected override void OnInitialize()
    {
        ExecuteSafely(
            () =>
            {
                base.OnInitialize();
                InitializeResources();
                InitializeQualitySettings();
                InitializeThreading();
                Log(LogLevel.Debug, LOG_PREFIX, "Initialized");
            },
            nameof(OnInitialize),
            "Failed during renderer initialization"
        );
    }

    private static void InitializeResources() { }

    private void InitializeQualitySettings() => ApplyQualitySettingsInternal();

    public override void Configure(
        bool isOverlayActive,
        RenderQuality quality)
    {
        ExecuteSafely(
            () =>
            {
                if (_isConfiguring) return;

                try
                {
                    _isConfiguring = true;
                    bool configChanged = _isOverlayActive != isOverlayActive || Quality != quality;

                    _isOverlayActive = isOverlayActive;
                    Quality = quality;
                    _smoothingFactor = isOverlayActive ? 0.5f : 0.3f;

                    if (configChanged)
                    {
                        ApplyQualitySettingsInternal();
                        OnConfigurationChanged();
                    }
                }
                finally
                {
                    _isConfiguring = false;
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
                if (_isConfiguring) return;

                try
                {
                    _isConfiguring = true;
                    base.ApplyQualitySettings();
                    ApplyQualitySettingsInternal();
                }
                finally
                {
                    _isConfiguring = false;
                }
            },
            nameof(ApplyQualitySettings),
            "Failed to apply quality settings"
        );
    }

    private void ApplyQualitySettingsInternal()
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

        UpdatePaintsQualitySettings();
        Log(LogLevel.Debug, LOG_PREFIX,
            $"Quality settings applied. New Quality: {Quality}, AntiAlias: {_useAntiAlias}, " +
            $"AdvancedEffects: {_useAdvancedEffects}, GlowEffects: {_useGlowEffects}, " +
            $"BlurRadius: {_edgeBlurRadius}, StrokeWidth: {_edgeStrokeWidth}");
    }

    private void LowQualitySettings()
    {
        _useAntiAlias = LOW_USE_ANTIALIASING;
        _useAdvancedEffects = LOW_USE_ADVANCED_EFFECTS;
        _useGlowEffects = LOW_USE_GLOW_EFFECTS;
        _edgeBlurRadius = LOW_EDGE_BLUR_RADIUS;
        _edgeStrokeWidth = LOW_EDGE_STROKE_WIDTH;
    }

    private void MediumQualitySettings()
    {
        _useAntiAlias = MEDIUM_USE_ANTIALIASING;
        _useAdvancedEffects = MEDIUM_USE_ADVANCED_EFFECTS;
        _useGlowEffects = MEDIUM_USE_GLOW_EFFECTS;
        _edgeBlurRadius = MEDIUM_EDGE_BLUR_RADIUS;
        _edgeStrokeWidth = MEDIUM_EDGE_STROKE_WIDTH;
    }

    private void HighQualitySettings()
    {
        _useAntiAlias = HIGH_USE_ANTIALIASING;
        _useAdvancedEffects = HIGH_USE_ADVANCED_EFFECTS;
        _useGlowEffects = HIGH_USE_GLOW_EFFECTS;
        _edgeBlurRadius = HIGH_EDGE_BLUR_RADIUS;
        _edgeStrokeWidth = HIGH_EDGE_STROKE_WIDTH;
    }

    private void UpdatePaintsQualitySettings()
    {
        foreach (var paint in _facePaints)
        {
            paint.IsAntialias = _useAntiAlias;
        }

        _edgePaint.IsAntialias = _useAntiAlias;
        _edgePaint.StrokeWidth = _edgeStrokeWidth;
    }

    protected override void OnQualitySettingsApplied()
    {
        ExecuteSafely(
            () =>
            {
                base.OnQualitySettingsApplied();
            },
            nameof(OnQualitySettingsApplied),
            "Failed to apply quality settings"
        );
    }

    private bool ValidateRenderParameters(
        SKCanvas? canvas,
        float[]? spectrum,
        SKImageInfo info,
        SKPaint? paint)
    {
        if (!IsCanvasValid(canvas)) return false;
        if (!IsSpectrumValid(spectrum)) return false;
        if (!IsPaintValid(paint)) return false;
        if (!AreDimensionsValid(info)) return false;
        if (IsDisposed())
        {
            Log(LogLevel.Error, LOG_PREFIX, "Renderer is disposed");
            return false;
        }
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
        if (spectrum != null && spectrum.Length > 0) return true;
        Log(LogLevel.Error, LOG_PREFIX, "Spectrum is null or empty");
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
        Log(LogLevel.Error,
            LOG_PREFIX,
            $"Invalid image dimensions: {info.Width}x{info.Height}");
        return false;
    }

    private bool IsDisposed()
    {
        if (!_disposed) return false;
        Log(LogLevel.Error, LOG_PREFIX, "Renderer is disposed");
        return true;
    }

    protected override void BeforeRender(
        SKCanvas? canvas,
        float[]? spectrum,
        SKImageInfo info,
        float barWidth,
        float barSpacing,
        int barCount,
        SKPaint? paint)
    {
        UpdateDeltaTime();
        _lastImageInfo = info;
        SubmitSpectrumForProcessing(spectrum, barCount);
        base.BeforeRender(canvas, spectrum, info, barWidth, barSpacing, barCount, paint);
    }

    private void UpdateDeltaTime()
    {
        var currentTime = DateTime.Now;
        _deltaTime = _lastRenderTime != default
            ? (float)(currentTime - _lastRenderTime).TotalSeconds
            : DEFAULT_DELTA_TIME;
        _lastRenderTime = currentTime;
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
        if (!ValidateRenderParameters(canvas, spectrum, info, paint)) return;
        ExecuteSafely(
            () =>
            {
                RenderData? dataToUse = null;
                lock (_renderDataLock)
                {
                    if (_currentRenderData != null)
                    {
                        dataToUse = _currentRenderData.Value;
                        _dataReady = false;
                    }
                }
                if (dataToUse.HasValue)
                {
                    PerformFrameCalculations(_lastImageInfo, dataToUse.Value);
                    DrawCubeFaces(canvas, dataToUse.Value);
                }
            },
            nameof(RenderEffect),
            "Error rendering cube effect"
        );
    }

    private void InitializeThreading()
    {
        StopProcessingThread();
        _cts = new CancellationTokenSource();
        _spectrumDataAvailable = new AutoResetEvent(false);
        _processingComplete = new AutoResetEvent(false);
        _processingRunning = true;
        _dataReady = false;
        _currentRenderData = null;
        _processingThread = new Thread(ProcessSpectrumThreadEntry)
        {
            IsBackground = true,
            Name = "CubeProcessor"
        };
        _processingThread.Start();

        Log(LogLevel.Debug,
            LOG_PREFIX,
            "Processing thread started.");
    }

    private void StopProcessingThread()
    {
        if (_processingThread == null || !_processingRunning) return;
        _processingRunning = false;
        _cts?.Cancel();
        _spectrumDataAvailable?.Set();
        _processingThread?.Join(THREAD_JOIN_TIMEOUT_MS);
        _cts?.Dispose();
        _spectrumDataAvailable?.Dispose();
        _processingComplete?.Dispose();
        _cts = null;
        _spectrumDataAvailable = null;
        _processingComplete = null;
        _processingThread = null;

        Log(LogLevel.Debug,
            LOG_PREFIX,
            "Processing thread stopped.");
    }

    protected override void OnDispose()
    {
        ExecuteSafely(
            () =>
            {
                StopProcessingThread();
                DisposeResources();
                base.OnDispose();

                Log(LogLevel.Debug,
                    LOG_PREFIX,
                    "Renderer specific resources released.");
            },
            nameof(OnDispose),
            "Error during specific disposal"
        );
    }

    private void DisposeResources()
    {
        foreach (var paint in _facePaints) paint?.Dispose();
        _edgePaint?.Dispose();
        _currentRenderData = null;
    }

    private static Vertex[] CreateCubeVertices() =>
    [
        new Vertex(-CUBE_HALF_SIZE, -CUBE_HALF_SIZE, CUBE_HALF_SIZE),
        new Vertex(CUBE_HALF_SIZE, -CUBE_HALF_SIZE, CUBE_HALF_SIZE),
        new Vertex(CUBE_HALF_SIZE, CUBE_HALF_SIZE, CUBE_HALF_SIZE),
        new Vertex(-CUBE_HALF_SIZE, CUBE_HALF_SIZE, CUBE_HALF_SIZE),
        new Vertex(-CUBE_HALF_SIZE, -CUBE_HALF_SIZE, -CUBE_HALF_SIZE),
        new Vertex(CUBE_HALF_SIZE, -CUBE_HALF_SIZE, -CUBE_HALF_SIZE),
        new Vertex(CUBE_HALF_SIZE, CUBE_HALF_SIZE, -CUBE_HALF_SIZE),
        new Vertex(-CUBE_HALF_SIZE, CUBE_HALF_SIZE, -CUBE_HALF_SIZE)
    ];

    private static Face[] CreateCubeFaces() =>
    [
        new Face(0, 1, 2, 0), new Face(0, 2, 3, 0),
        new Face(4, 6, 5, 1), new Face(4, 7, 6, 1),
        new Face(3, 2, 6, 2), new Face(3, 6, 7, 2),
        new Face(0, 5, 1, 3), new Face(0, 4, 5, 3),
        new Face(1, 5, 6, 4), new Face(1, 6, 2, 4),
        new Face(0, 3, 7, 5), new Face(0, 7, 4, 5)
    ];

    private static Vector3[] CalculateFaceNormals() =>
    [
        new Vector3(0, 0, 1), new Vector3(0, 0, -1), new Vector3(0, 1, 0),
        new Vector3(0, -1, 0), new Vector3(1, 0, 0), new Vector3(-1, 0, 0)
    ];

    private SKPaint[] CreateFacePaints() =>
        [.. FACE_COLORS.Select(color => new SKPaint
        {
            Color = color,
            IsAntialias = _useAntiAlias,
            Style = SKPaintStyle.Fill
        })];

    private SKPaint CreateEdgePaint() => new()
    {
        Color = SKColors.White.WithAlpha(0),
        IsAntialias = _useAntiAlias,
        Style = SKPaintStyle.Stroke,
        StrokeWidth = _edgeStrokeWidth,
        MaskFilter = null
    };

    private void PerformFrameCalculations(
        SKImageInfo info,
        RenderData renderData)
    {
        ExecuteSafely(
            () =>
            {
                UpdateRotationAngles();
                _rotationMatrix = CreateRotationMatrix();
                ProjectVertices(info, renderData);
                CalculateFaceDepthsAndNormals();
                SortFacesByDepth();
            },
            nameof(PerformFrameCalculations),
            "Error during frame calculations"
        );
    }

    private void UpdateRotationAngles()
    {
        _rotationAngleX = (_rotationAngleX + _rotationSpeedX * _deltaTime) % MathF.Tau;
        _rotationAngleY = (_rotationAngleY + _rotationSpeedY * _deltaTime) % MathF.Tau;
        _rotationAngleZ = (_rotationAngleZ + _rotationSpeedZ * _deltaTime) % MathF.Tau;
    }

    private Matrix4x4 CreateRotationMatrix() =>
        Matrix4x4.CreateRotationX(_rotationAngleX) *
        Matrix4x4.CreateRotationY(_rotationAngleY) *
        Matrix4x4.CreateRotationZ(_rotationAngleZ);

    private void ProjectVertices(
        SKImageInfo info,
        RenderData renderData)
    {
        ExecuteSafely(
            () =>
            {
                CalculateProjectionParameters(
                    info,
                    renderData,
                    out float centerX,
                    out float centerY,
                    out float scale);
                for (int i = 0; i < _vertices.Length; i++)
                {
                    _projectedVertices[i] = ProjectSingleVertex(
                        _vertices[i],
                        _rotationMatrix,
                        scale,
                        centerX,
                        centerY);
                }
            },
            nameof(ProjectVertices),
            "Error projecting vertices"
        );
    }

    private static void CalculateProjectionParameters(
        SKImageInfo info,
        RenderData renderData,
        out float centerX,
        out float centerY,
        out float scale)
    {
        centerX = info.Width * CENTER_PROPORTION;
        centerY = info.Height * CENTER_PROPORTION;
        scale = CalculateProjectionScale(info, renderData);
    }

    private static ProjectedVertex ProjectSingleVertex(
        Vertex vertex,
        Matrix4x4 rotationMatrix,
        float scale,
        float centerX,
        float centerY)
    {
        float rx = vertex.X * rotationMatrix.M11 + vertex.Y * rotationMatrix.M21 + vertex.Z * rotationMatrix.M31;
        float ry = vertex.X * rotationMatrix.M12 + vertex.Y * rotationMatrix.M22 + vertex.Z * rotationMatrix.M32;
        float rz = vertex.X * rotationMatrix.M13 + vertex.Y * rotationMatrix.M23 + vertex.Z * rotationMatrix.M33;
        return new ProjectedVertex
        {
            X = rx * scale + centerX,
            Y = ry * scale + centerY,
            Depth = rz
        };
    }

    private static float CalculateProjectionScale(
        SKImageInfo info,
        RenderData renderData)
    {
        float baseScale = Min(info.Width * CENTER_PROPORTION, info.Height * CENTER_PROPORTION);

        float barCountFactor = BASE_ROTATION_FACTOR
                               + MathF.Log10(MathF.Max(BASE_ROTATION_FACTOR, renderData.BarCount))
                               * SCALE_LOG_FACTOR;

        barCountFactor = Clamp(barCountFactor, MIN_SCALE_FACTOR, MAX_SCALE_FACTOR);
        return baseScale * renderData.CubeSize * barCountFactor;
    }

    private void CalculateFaceDepthsAndNormals()
    {
        ExecuteSafely(
            () =>
            {
                for (int i = 0; i < _faces.Length; i++)
                {
                    CalculateFaceProperties(i);
                }
            },
            nameof(CalculateFaceDepthsAndNormals),
            "Error calculating face depths and normals"
        );
    }

    private void CalculateFaceProperties(int faceIndex)
    {
        var face = _faces[faceIndex];
        _faceDepths[faceIndex] = (_projectedVertices[face.V1].Depth +
                                  _projectedVertices[face.V2].Depth +
                                  _projectedVertices[face.V3].Depth) / FACE_DEPTH_AVERAGE_FACTOR;

        Vector3 worldNormal = _faceNormalVectors[face.FaceIndex];
        Vector3 rotatedNormal = Vector3.TransformNormal(worldNormal, _rotationMatrix);

        rotatedNormal = Vector3.Normalize(rotatedNormal);

        _faceNormals[faceIndex] = Vector3.Dot(rotatedNormal, Vector3.UnitZ);
        _faceLightIntensities[faceIndex] = CalculateLightIntensity(rotatedNormal);
    }

    private static float CalculateLightIntensity(Vector3 rotatedNormal)
    {
        float diffuse = Max(MIN_DIFFUSE_LIGHT, Vector3.Dot(rotatedNormal, LIGHT_DIRECTION));
        return AMBIENT_LIGHT + DIFFUSE_LIGHT * diffuse;
    }

    private void SortFacesByDepth()
    {
        ExecuteSafely(
            () =>
            {
                int[] indices = [.. Enumerable.Range(0, _faces.Length)];

                Array.Sort(indices, (a, b) => _faceDepths[b].CompareTo(_faceDepths[a]));
                Face[] sortedFaces = new Face[_faces.Length];

                float[] sortedDepths = new float[_faceDepths.Length];
                float[] sortedNormals = new float[_faceNormals.Length];
                float[] sortedLightIntensities = new float[_faceLightIntensities.Length];

                for (int i = 0; i < indices.Length; i++)
                {
                    int originalIndex = indices[i];
                    sortedFaces[i] = _faces[originalIndex];
                    sortedDepths[i] = _faceDepths[originalIndex];
                    sortedNormals[i] = _faceNormals[originalIndex];
                    sortedLightIntensities[i] = _faceLightIntensities[originalIndex];
                }
                _faces = sortedFaces;
                _faceDepths = sortedDepths;
                _faceNormals = sortedNormals;
                _faceLightIntensities = sortedLightIntensities;
            },
            nameof(SortFacesByDepth),
            "Error sorting faces by depth"
        );
    }

    private void DrawCubeFaces(
        SKCanvas canvas,
        RenderData renderData)
    {
        for (int i = 0; i < _faces.Length; i++)
        {
            if (_faceNormals[i] <= 0) continue;

            var face = _faces[i];
            var v1 = _projectedVertices[face.V1];
            var v2 = _projectedVertices[face.V2];
            var v3 = _projectedVertices[face.V3];

            DrawFace(canvas, face, v1, v2, v3, renderData, i);
        }
    }

    private void DrawFace(
        SKCanvas canvas,
        Face face,
        ProjectedVertex v1,
        ProjectedVertex v2,
        ProjectedVertex v3,
        RenderData renderData,
        int sortedFaceIndex)
    {
        using var path = CreateFacePath(v1, v2, v3);
        DrawSingleFace(canvas, path, face, renderData, sortedFaceIndex);
    }

    private static SKPath CreateFacePath(
        ProjectedVertex v1,
        ProjectedVertex v2,
        ProjectedVertex v3)
    {
        var path = new SKPath();
        path.MoveTo(v1.X, v1.Y);
        path.LineTo(v2.X, v2.Y);
        path.LineTo(v3.X, v3.Y);
        path.Close();
        return path;
    }

    private void DrawSingleFace(
        SKCanvas canvas,
        SKPath path,
        Face face,
        RenderData renderData,
        int sortedFaceIndex)
    {
        float lightIntensity = _faceLightIntensities[sortedFaceIndex];
        float normalZ = _faceNormals[sortedFaceIndex];

        (SKColor litColor, byte alphaByte) = CalculateLitFaceColorAndAlpha(
            renderData, face.FaceIndex, lightIntensity, normalZ);

        var facePaint = _facePaints[face.FaceIndex];

        facePaint.Color = litColor;
        facePaint.IsAntialias = _useAntiAlias;

        canvas.DrawPath(path, facePaint);

        if (_useGlowEffects && _useAdvancedEffects)
        {
            ApplyGlowEffect(canvas, path, alphaByte);
        }
    }

    private static (SKColor color, byte alpha) CalculateLitFaceColorAndAlpha(
        RenderData renderData,
        int faceColorIndex,
        float lightIntensity,
        float normalZ)
    {
        var baseColor = FACE_COLORS[faceColorIndex];

        SKColor litColor = ApplyLightToColor(baseColor, lightIntensity);
        byte alpha = CalculateFaceAlpha(renderData.MaxSpectrum, normalZ);

        return (new SKColor(litColor.Red, litColor.Green, litColor.Blue, alpha), alpha);
    }

    private static SKColor ApplyLightToColor(
        SKColor baseColor,
        float lightIntensity)
    {
        byte r = (byte)Clamp((int)(baseColor.Red * lightIntensity), MIN_COLOR_BYTE, MAX_COLOR_BYTE);
        byte g = (byte)Clamp((int)(baseColor.Green * lightIntensity), MIN_COLOR_BYTE, MAX_COLOR_BYTE);
        byte b = (byte)Clamp((int)(baseColor.Blue * lightIntensity), MIN_COLOR_BYTE, MAX_COLOR_BYTE);
        return new SKColor(r, g, b);
    }

    private static byte CalculateFaceAlpha(
        float maxSpectrum,
        float normalZ)
    {
        float alphaFactor = BASE_ALPHA + maxSpectrum * SPECTRUM_ALPHA_INFLUENCE;
        return (byte)Clamp(alphaFactor * normalZ * MAX_COLOR_BYTE, MIN_COLOR_BYTE, MAX_COLOR_BYTE);
    }

    private void ApplyGlowEffect(
        SKCanvas canvas,
        SKPath path,
        byte alpha)
    {
        _edgePaint.Color = SKColors.White.WithAlpha((byte)(alpha * EDGE_ALPHA_MULTIPLIER));
        _edgePaint.IsAntialias = _useAntiAlias;
        _edgePaint.StrokeWidth = _edgeStrokeWidth;

        _edgePaint.MaskFilter = _useAdvancedEffects && _edgeBlurRadius > 0
            ? SKMaskFilter.CreateBlur(SKBlurStyle.Normal, _edgeBlurRadius)
            : null;

        canvas.DrawPath(path, _edgePaint);
    }

    private void SubmitSpectrumForProcessing(
        float[]? spectrum,
        int barCount)
    {
        ExecuteSafely(
            () =>
            {
                if (spectrum == null
                    || _spectrumDataAvailable == null
                    || !_processingRunning)
                {
                    return;
                }
                lock (_renderDataLock)
                {
                    if (!_dataReady)
                    {
                        _spectrumToProcess = (float[])spectrum.Clone();
                        _barCountToProcess = barCount;
                        _spectrumDataAvailable.Set();
                    }
                }
            },
            nameof(SubmitSpectrumForProcessing),
            "Failed to submit spectrum for processing"
        );
    }

    private void ProcessSpectrumThreadEntry()
    {
        Log(LogLevel.Debug,
            LOG_PREFIX,
            "Processing thread loop started.");
        try
        {
            while (_processingRunning
                && _cts != null
                && !_cts.Token.IsCancellationRequested
                && _spectrumDataAvailable != null
                && _processingComplete != null)
            {
                bool signaled = _spectrumDataAvailable.WaitOne();

                if (!signaled
                    || _cts.Token.IsCancellationRequested
                    || !_processingRunning)
                {
                    break;
                }
                if (!_cts.Token.IsCancellationRequested)
                {
                    ProcessLatestSpectrumData();
                }
            }
        }
        catch (OperationCanceledException)
        {
            Log(LogLevel.Debug,
                LOG_PREFIX,
                "Processing thread cancelled.");
        }
        catch (ObjectDisposedException)
        {
            Log(LogLevel.Debug,
                LOG_PREFIX,
                "Processing thread synchronization object disposed.");
        }
        catch (Exception ex)
        {
            Log(LogLevel.Error,
                LOG_PREFIX,
                $"Unhandled error in cube processing thread: {ex.Message}\n{ex.StackTrace}");
        }
        finally
        {
            _processingRunning = false;
            Log(LogLevel.Debug, LOG_PREFIX, "Processing thread loop stopped.");
        }
    }

    private void ProcessLatestSpectrumData()
    {
        if (TryGetSpectrumToProcess(out var spectrumCopy, out int barCountCopy))
        {
            ComputeAndStoreRenderData(spectrumCopy, barCountCopy);
        }
    }

    private bool TryGetSpectrumToProcess(
        [MaybeNullWhen(false)] out float[] spectrum,
        out int barCount)
    {
        lock (_renderDataLock)
        {
            if (_spectrumToProcess == null)
            {
                spectrum = null;
                barCount = 0;
                return false;
            }
            spectrum = _spectrumToProcess;
            barCount = _barCountToProcess;
            _spectrumToProcess = null;
            return true;
        }
    }

    private void ComputeAndStoreRenderData(
        float[] spectrum,
        int barCount)
    {
        ExecuteSafely(
            () =>
            {
                if (_cts == null || _cts.Token.IsCancellationRequested) return;

                UpdateCurrentCubeSize(spectrum);
                UpdateRotationSpeeds(spectrum);

                float maxSpectrumValue = spectrum.Length > 0 ? spectrum.Max() : 0f;
                RenderData computedData = new(
                    maxSpectrumValue,
                    _currentCubeSize,
                    barCount);

                lock (_renderDataLock)
                {
                    _currentRenderData = computedData;
                    _dataReady = true;
                }
            },
            nameof(ComputeAndStoreRenderData),
            "Error computing cube data"
        );
    }

    private void UpdateCurrentCubeSize(float[] spectrum)
    {
        ExecuteSafely(
            () =>
            {
                if (spectrum.Length == 0) return;

                float avgIntensity = spectrum.Average();
                float targetSize = BASE_CUBE_SIZE + avgIntensity * CUBE_SIZE_RESPONSE_FACTOR;
                targetSize = Clamp(targetSize, MIN_CUBE_SIZE, MAX_CUBE_SIZE);
                _currentCubeSize = SmoothValue(_currentCubeSize, targetSize, CUBE_SIZE_SMOOTHING);
            },
            nameof(UpdateCurrentCubeSize),
            "Error updating cube size"
        );
    }

    private void InitializeRotationSpeeds()
    {
        _rotationSpeedX = BASE_ROTATION_SPEED * INITIAL_SPEED_X_FACTOR;
        _rotationSpeedY = BASE_ROTATION_SPEED * INITIAL_SPEED_Y_FACTOR;
        _rotationSpeedZ = BASE_ROTATION_SPEED * INITIAL_SPEED_Z_FACTOR;
    }

    private void UpdateRotationSpeeds(float[] spectrum)
    {
        ExecuteSafely(
            () =>
            {
                if (spectrum.Length < MIN_SPECTRUM_FOR_ROTATION)
                {
                    InitializeRotationSpeeds();
                    return;
                }

                int midIndex = spectrum.Length / 2;
                int highIndex = spectrum.Length - 1;

                float lowFreq = spectrum[0];
                float midFreq = spectrum[midIndex];
                float highFreq = spectrum[highIndex];

                float targetSpeedX = BASE_ROTATION_SPEED + lowFreq * SPECTRUM_ROTATION_INFLUENCE;

                float targetSpeedY = BASE_ROTATION_SPEED
                                     * MID_FREQ_SPEED_FACTOR
                                     + midFreq
                                     * SPECTRUM_ROTATION_INFLUENCE;

                float targetSpeedZ = BASE_ROTATION_SPEED
                                     * HIGH_FREQ_SPEED_FACTOR
                                     + highFreq
                                     * SPECTRUM_ROTATION_INFLUENCE;

                targetSpeedX = Min(targetSpeedX, MAX_ROTATION_SPEED);
                targetSpeedY = Min(targetSpeedY, MAX_ROTATION_SPEED);
                targetSpeedZ = Min(targetSpeedZ, MAX_ROTATION_SPEED);

                _rotationSpeedX = SmoothValue(_rotationSpeedX, targetSpeedX, ROTATION_SPEED_SMOOTHING);
                _rotationSpeedY = SmoothValue(_rotationSpeedY, targetSpeedY, ROTATION_SPEED_SMOOTHING);
                _rotationSpeedZ = SmoothValue(_rotationSpeedZ, targetSpeedZ, ROTATION_SPEED_SMOOTHING);
            },
            nameof(UpdateRotationSpeeds),
            "Error updating rotation speeds"
        );
    }

    private static float SmoothValue(
        float currentValue,
        float targetValue,
        float smoothingFactor) =>
        currentValue * smoothingFactor + targetValue * (1.0f - smoothingFactor);
}