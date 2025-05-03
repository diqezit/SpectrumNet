#nullable enable

using static SpectrumNet.Views.Renderers.CubeRenderer.Constants;
using static System.MathF;

namespace SpectrumNet.Views.Renderers;

public sealed class CubeRenderer : EffectSpectrumRenderer
{
    public static class Constants
    {
        public const string LOG_PREFIX = "CubeRenderer";

        public const float
            BASE_CUBE_SIZE = 0.5f,
            MIN_CUBE_SIZE = 0.2f,
            MAX_CUBE_SIZE = 1.0f,
            CUBE_SIZE_RESPONSE_FACTOR = 0.5f;

        public const float
            BASE_ROTATION_SPEED = 0.5f,
            SPECTRUM_ROTATION_INFLUENCE = 0.015f,
            MAX_ROTATION_SPEED = 0.05f;

        public const float
            AMBIENT_LIGHT = 0.4f,
            DIFFUSE_LIGHT = 0.6f;

        public const float
            BASE_ALPHA = 0.9f,
            SPECTRUM_ALPHA_INFLUENCE = 0.1f,
            EDGE_ALPHA_MULTIPLIER = 0.8f;

        public const int THREAD_JOIN_TIMEOUT_MS = 100;
    }

    private static readonly Vector3 LIGHT_DIRECTION = Vector3.Normalize(new(0.5f, 0.7f, -1.0f));
    private static readonly SKColor[] FACE_COLORS =
    [
        new(255, 100, 100), new(100, 255, 100),
        new(100, 100, 255), new(255, 255, 100),
        new(255, 100, 255), new(100, 255, 255)
    ];

    private static readonly Lazy<CubeRenderer> _instance = new(() => new CubeRenderer());

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
    private float _rotationAngleX, _rotationAngleY, _rotationAngleZ;
    private Matrix4x4 _rotationMatrix = Matrix4x4.Identity;
    private DateTime _lastRenderTime;
    private float _deltaTime;
    private SKImageInfo _lastImageInfo;

    private float _rotationSpeedX, _rotationSpeedY, _rotationSpeedZ;
    private float _currentCubeSize = BASE_CUBE_SIZE;

    private Thread? _processingThread;
    private CancellationTokenSource? _cts;
    private AutoResetEvent? _spectrumDataAvailable;
    private AutoResetEvent? _processingComplete;
    private readonly object _renderDataLock = new();
    private volatile bool _processingRunning;
    private bool _dataReady;

    private float[]? _spectrumToProcess;
    private int _barCountToProcess;
    private RenderData? _currentRenderData;

    private readonly bool _useGlowEffects = true;

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

    public override void Initialize()
    {
        Safe(() =>
        {
            base.Initialize();
            InitializeThreading();
            Log(LogLevel.Debug, LOG_PREFIX, "Initialized");
        }, new ErrorHandlingOptions
        {
            Source = $"{LOG_PREFIX}.Initialize",
            ErrorMessage = "Failed to initialize renderer"
        });
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
        Log(LogLevel.Debug, LOG_PREFIX, "Processing thread started.");
    }

    public override void Dispose()
    {
        if (_disposed) return;

        Safe(() =>
        {
            StopProcessingThread();
            DisposeResources();
            base.Dispose();

            Log(LogLevel.Debug, LOG_PREFIX, "Disposed");
        }, new ErrorHandlingOptions
        {
            Source = $"{LOG_PREFIX}.Dispose",
            ErrorMessage = "Error during disposal"
        });
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
    }

    private void DisposeResources()
    {
        foreach (var paint in _facePaints) paint?.Dispose();
        _edgePaint?.Dispose();
        _currentRenderData = null;
    }

    private static Vertex[] CreateCubeVertices() =>
    [
        new(-0.5f, -0.5f, 0.5f), new(0.5f, -0.5f, 0.5f),
        new(0.5f, 0.5f, 0.5f), new(-0.5f, 0.5f, 0.5f),
        new(-0.5f, -0.5f, -0.5f), new(0.5f, -0.5f, -0.5f),
        new(0.5f, 0.5f, -0.5f), new(-0.5f, 0.5f, -0.5f)
    ];

    private static Face[] CreateCubeFaces() =>
    [
        new(0, 1, 2, 0), new(0, 2, 3, 0),
        new(4, 6, 5, 1), new(4, 7, 6, 1),
        new(3, 2, 6, 2), new(3, 6, 7, 2),
        new(0, 5, 1, 3), new(0, 4, 5, 3),
        new(1, 5, 6, 4), new(1, 6, 2, 4),
        new(0, 3, 7, 5), new(0, 7, 4, 5)
    ];

    private static Vector3[] CalculateFaceNormals() =>
    [
        new(0, 0, 1), new(0, 0, -1), new(0, 1, 0),
        new(0, -1, 0), new(1, 0, 0), new(-1, 0, 0)
    ];

    private SKPaint[] CreateFacePaints() =>
        [.. FACE_COLORS.Select(color => new SKPaint
        {
            Color = color,
            IsAntialias = UseAntiAlias,
            Style = SKPaintStyle.Fill
        })];

    private SKPaint CreateEdgePaint() => new()
    {
        Color = SKColors.White.WithAlpha(0),
        IsAntialias = UseAntiAlias,
        Style = SKPaintStyle.Stroke,
        StrokeWidth = 1.5f,
        MaskFilter = null
    };

    protected override void BeforeRender(
        SKCanvas? canvas, float[]? spectrum, SKImageInfo info,
        float barWidth, float barSpacing, int barCount, SKPaint? paint)
    {
        UpdateDeltaTime();
        base.BeforeRender(canvas, spectrum, info, barWidth, barSpacing, barCount, paint);

        _lastImageInfo = info;
        SubmitSpectrumForProcessing(spectrum, barCount);
    }

    private void UpdateDeltaTime()
    {
        var currentTime = DateTime.Now;
        _deltaTime = _lastRenderTime != default
            ? (float)(currentTime - _lastRenderTime).TotalSeconds
            : 1.0f / 60.0f;
        _lastRenderTime = currentTime;
    }

    protected override void RenderEffect(
        SKCanvas canvas, float[] spectrum, SKImageInfo info,
        float barWidth, float barSpacing, int barCount, SKPaint paint)
    {
        Safe(() =>
        {
            if (TryGetLatestRenderData(out RenderData renderData))
            {
                PerformFrameCalculations(info, renderData);
                DrawCubeFaces(canvas, renderData);
            }
        }, new ErrorHandlingOptions
        {
            Source = $"{LOG_PREFIX}.RenderEffect",
            ErrorMessage = "Error rendering cube effect"
        });
    }

    private bool TryGetLatestRenderData(out RenderData renderData)
    {
        lock (_renderDataLock)
        {
            if (!_dataReady || _currentRenderData == null)
            {
                renderData = default;
                return false;
            }
            renderData = _currentRenderData.Value;
            _dataReady = false;
            _currentRenderData = null;
            return true;
        }
    }

    private void PerformFrameCalculations(SKImageInfo info, RenderData renderData)
    {
        UpdateRotationAngles();
        _rotationMatrix = CreateRotationMatrix();
        ProjectVertices(info, renderData);
        CalculateFaceDepthsAndNormals();
        SortFacesByDepth();
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

    private void ProjectVertices(SKImageInfo info, RenderData renderData)
    {
        Safe(() =>
        {
            float centerX = info.Width * 0.5f;
            float centerY = info.Height * 0.5f;
            float scale = CalculateProjectionScale(info, renderData);

            float m11 = _rotationMatrix.M11, m12 = _rotationMatrix.M12, m13 = _rotationMatrix.M13;
            float m21 = _rotationMatrix.M21, m22 = _rotationMatrix.M22, m23 = _rotationMatrix.M23;
            float m31 = _rotationMatrix.M31, m32 = _rotationMatrix.M32, m33 = _rotationMatrix.M33;

            for (int i = 0; i < _vertices.Length; i++)
            {
                Vertex vertex = _vertices[i];
                float rx = vertex.X * m11 + vertex.Y * m21 + vertex.Z * m31;
                float ry = vertex.X * m12 + vertex.Y * m22 + vertex.Z * m32;
                float rz = vertex.X * m13 + vertex.Y * m23 + vertex.Z * m33;

                _projectedVertices[i] = new ProjectedVertex
                {
                    X = rx * scale + centerX,
                    Y = ry * scale + centerY,
                    Depth = rz
                };
            }
        }, new ErrorHandlingOptions
        {
            Source = $"{LOG_PREFIX}.ProjectVertices",
            ErrorMessage = "Error projecting vertices"
        });
    }

    private static float CalculateProjectionScale(SKImageInfo info, RenderData renderData)
    {
        float baseScale = MathF.Min(info.Width * 0.5f, info.Height * 0.5f);
        float barCountFactor = 1.0f + Log10(Max(1, renderData.BarCount)) * 0.3f;
        barCountFactor = Clamp(barCountFactor, 1.0f, 2.5f);
        return baseScale * renderData.CubeSize * barCountFactor;
    }

    private void CalculateFaceDepthsAndNormals()
    {
        Safe(() =>
        {
            for (int i = 0; i < _faces.Length; i++)
            {
                var face = _faces[i];
                _faceDepths[i] = (_projectedVertices[face.V1].Depth +
                                  _projectedVertices[face.V2].Depth +
                                  _projectedVertices[face.V3].Depth) / 3.0f;

                Vector3 worldNormal = _faceNormalVectors[face.FaceIndex];
                Vector3 rotatedNormal = Vector3.TransformNormal(worldNormal, _rotationMatrix);
                rotatedNormal = Vector3.Normalize(rotatedNormal);

                _faceNormals[i] = Vector3.Dot(rotatedNormal, Vector3.UnitZ);
                _faceLightIntensities[i] = CalculateLightIntensity(rotatedNormal);
            }
        }, new ErrorHandlingOptions
        {
            Source = $"{LOG_PREFIX}.CalculateFaceDepthsAndNormals",
            ErrorMessage = "Error calculating face depths and normals"
        });
    }

    private static float CalculateLightIntensity(Vector3 rotatedNormal)
    {
        float diffuse = MathF.Max(0f, Vector3.Dot(rotatedNormal, LIGHT_DIRECTION));
        return AMBIENT_LIGHT + DIFFUSE_LIGHT * diffuse;
    }

    private void SortFacesByDepth()
    {
        Safe(() =>
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

        }, new ErrorHandlingOptions
        {
            Source = $"{LOG_PREFIX}.SortFacesByDepth",
            ErrorMessage = "Error sorting faces by depth"
        });
    }

    private void DrawCubeFaces(SKCanvas canvas, RenderData renderData)
    {
        for (int i = 0; i < _faces.Length; i++)
        {
            if (_faceNormals[i] <= 0) continue;

            var face = _faces[i];
            var v1 = _projectedVertices[face.V1];
            var v2 = _projectedVertices[face.V2];
            var v3 = _projectedVertices[face.V3];

            using var path = CreateFacePath(v1, v2, v3);
            DrawSingleFace(canvas, path, face, renderData, i);
        }
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
        facePaint.IsAntialias = UseAntiAlias;

        canvas.DrawPath(path, facePaint);

        if (_useGlowEffects && UseAdvancedEffects)
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

        byte r = (byte)Clamp((int)(baseColor.Red * lightIntensity), 0, 255);
        byte g = (byte)Clamp((int)(baseColor.Green * lightIntensity), 0, 255);
        byte b = (byte)Clamp((int)(baseColor.Blue * lightIntensity), 0, 255);

        float alphaFactor = BASE_ALPHA + renderData.MaxSpectrum * SPECTRUM_ALPHA_INFLUENCE;
        byte alpha = (byte)Clamp(alphaFactor * normalZ * 255f, 0f, 255f);

        return (new(r, g, b, alpha), alpha);
    }

    private void ApplyGlowEffect(SKCanvas canvas, SKPath path, byte alpha)
    {
        _edgePaint.Color = SKColors.White.WithAlpha((byte)(alpha * EDGE_ALPHA_MULTIPLIER));
        _edgePaint.IsAntialias = UseAntiAlias;
        _edgePaint.MaskFilter = UseAdvancedEffects
            ? SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 2.0f)
            : null;
        canvas.DrawPath(path, _edgePaint);
    }

    private void SubmitSpectrumForProcessing(float[]? spectrum, int barCount)
    {
        Safe(() =>
        {
            if (spectrum == null || _spectrumDataAvailable == null || !_processingRunning)
            {
                return;
            }
            lock (_renderDataLock)
            {
                _spectrumToProcess = (float[])spectrum.Clone();
                _barCountToProcess = barCount;
            }
            _spectrumDataAvailable.Set();
        }, new ErrorHandlingOptions
        {
            Source = $"{LOG_PREFIX}.SubmitSpectrumForProcessing",
            ErrorMessage = "Failed to submit spectrum for processing"
        });
    }

    private void ProcessSpectrumThreadEntry()
    {
        Log(LogLevel.Debug, LOG_PREFIX, "Processing thread loop started.");
        try
        {
            while (_processingRunning && _cts != null && !_cts.Token.IsCancellationRequested &&
                   _spectrumDataAvailable != null && _processingComplete != null)
            {
                bool signaled = _spectrumDataAvailable.WaitOne();
                if (!signaled || _cts.Token.IsCancellationRequested || !_processingRunning)
                {
                    break;
                }
                ProcessLatestSpectrumData();
            }
        }
        catch (OperationCanceledException)
        {
            Log(LogLevel.Debug, LOG_PREFIX, "Processing thread cancelled.");
        }
        catch (ObjectDisposedException)
        {
            Log(LogLevel.Debug, LOG_PREFIX, "Processing thread synchronization object disposed.");
        }
        catch (Exception ex)
        {
            Log(LogLevel.Error, LOG_PREFIX, $"Unhandled error in cube processing thread: {ex.Message}\n{ex.StackTrace}");
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

    private void ComputeAndStoreRenderData(float[] spectrum, int barCount)
    {
        Safe(() =>
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
        }, new ErrorHandlingOptions
        {
            Source = $"{LOG_PREFIX}.ComputeAndStoreRenderData",
            ErrorMessage = "Error computing cube data"
        });
    }

    private void UpdateCurrentCubeSize(float[] spectrum)
    {
        Safe(() =>
        {
            if (spectrum.Length == 0) return;

            float avgIntensity = spectrum.Average();
            float targetSize = BASE_CUBE_SIZE + avgIntensity * CUBE_SIZE_RESPONSE_FACTOR;
            targetSize = Clamp(targetSize, MIN_CUBE_SIZE, MAX_CUBE_SIZE);
            _currentCubeSize = _currentCubeSize * 0.9f + targetSize * 0.1f;

        }, new ErrorHandlingOptions
        {
            Source = $"{LOG_PREFIX}.UpdateCurrentCubeSize",
            ErrorMessage = "Error updating cube size"
        });
    }

    private void InitializeRotationSpeeds()
    {
        _rotationSpeedX = BASE_ROTATION_SPEED * 0.8f;
        _rotationSpeedY = BASE_ROTATION_SPEED * 1.2f;
        _rotationSpeedZ = BASE_ROTATION_SPEED * 0.6f;
    }

    private void UpdateRotationSpeeds(float[] spectrum)
    {
        Safe(() =>
        {
            if (spectrum.Length < 3)
            {
                InitializeRotationSpeeds();
                return;
            }

            float lowFreq = spectrum[0];
            float midFreq = spectrum[spectrum.Length / 2];
            float highFreq = spectrum[^1];

            float targetSpeedX = BASE_ROTATION_SPEED + lowFreq * SPECTRUM_ROTATION_INFLUENCE;
            float targetSpeedY = BASE_ROTATION_SPEED * 1.2f + midFreq * SPECTRUM_ROTATION_INFLUENCE;
            float targetSpeedZ = BASE_ROTATION_SPEED * 0.8f + highFreq * SPECTRUM_ROTATION_INFLUENCE;

            targetSpeedX = MathF.Min(targetSpeedX, MAX_ROTATION_SPEED);
            targetSpeedY = MathF.Min(targetSpeedY, MAX_ROTATION_SPEED);
            targetSpeedZ = MathF.Min(targetSpeedZ, MAX_ROTATION_SPEED);

            _rotationSpeedX = _rotationSpeedX * 0.95f + targetSpeedX * 0.05f;
            _rotationSpeedY = _rotationSpeedY * 0.95f + targetSpeedY * 0.05f;
            _rotationSpeedZ = _rotationSpeedZ * 0.95f + targetSpeedZ * 0.05f;

        }, new ErrorHandlingOptions
        {
            Source = $"{LOG_PREFIX}.UpdateRotationSpeeds",
            ErrorMessage = "Error updating rotation speeds"
        });
    }
}