#nullable enable

using SpectrumNet.Service.Enums;

namespace SpectrumNet.Views.Renderers;

/// <summary>
/// Renderer that creates a reactive 3D cube visualization for audio spectrum data.
/// </summary>
public sealed class CubeRenderer : BaseSpectrumRenderer
{
    #region Constants
    private static class Constants
    {
        // Logging
        public const string LOG_PREFIX = "CubeRenderer";

        // Cube properties
        public const float BASE_CUBE_SIZE = 0.5f;             // Default size of the cube
        public const float MIN_CUBE_SIZE = 0.2f;              // Minimum cube size
        public const float MAX_CUBE_SIZE = 1.0f;              // Maximum cube size
        public const float CUBE_SIZE_RESPONSE_FACTOR = 0.5f;  // Factor for cube size change based on spectrum

        // Rotation properties
        public const float BASE_ROTATION_SPEED = 0.5f;        // Base rotation speed
        public const float SPECTRUM_ROTATION_INFLUENCE = 0.015f; // Spectrum influence on rotation speed
        public const float MAX_ROTATION_SPEED = 0.05f;        // Maximum rotation speed

        // Lighting properties
        public const float AMBIENT_LIGHT = 0.4f;              // Ambient light intensity
        public const float DIFFUSE_LIGHT = 0.6f;              // Diffuse light intensity

        // Color properties
        public const float BASE_ALPHA = 0.9f;                 // Base alpha for faces
        public const float SPECTRUM_ALPHA_INFLUENCE = 0.1f;   // Spectrum influence on alpha
        public const float EDGE_ALPHA_MULTIPLIER = 0.8f;      // Alpha multiplier for edges

        // Processing settings
        public const int THREAD_JOIN_TIMEOUT_MS = 100;        // Timeout for thread joining in milliseconds
    }

    // Direction of light for 3D lighting effect
    private static readonly Vector3 LIGHT_DIRECTION = Vector3.Normalize(new Vector3(0.5f, 0.7f, -1.0f));

    // Face colors for the cube
    private static readonly SKColor[] FACE_COLORS = {
        new SKColor(255, 100, 100),  // Red (front)
        new SKColor(100, 255, 100),  // Green (back)
        new SKColor(100, 100, 255),  // Blue (top) 
        new SKColor(255, 255, 100),  // Yellow (bottom)
        new SKColor(255, 100, 255),  // Pink (right)
        new SKColor(100, 255, 255)   // Cyan (left)
    };
    #endregion

    #region Structures
    /// <summary>
    /// Represents a 3D vertex with X, Y, Z coordinates.
    /// </summary>
    private readonly record struct Vertex(float X, float Y, float Z);

    /// <summary>
    /// Represents a projected 2D vertex with depth information.
    /// </summary>
    private struct ProjectedVertex
    {
        public float X, Y, Depth;
    }

    /// <summary>
    /// Represents a triangle face on the cube.
    /// </summary>
    private readonly record struct Face(int V1, int V2, int V3, int FaceIndex);

    /// <summary>
    /// Encapsulates all data needed for rendering the cube.
    /// </summary>
    private readonly struct RenderData
    {
        public readonly ProjectedVertex[] Vertices;
        public readonly Face[] Faces;
        public readonly float[] FaceDepths;
        public readonly float[] FaceNormals;
        public readonly float[] FaceLightIntensities;
        public readonly float MaxSpectrum;
        public readonly float CubeSize;
        public readonly int BarCount;

        public RenderData(
            ProjectedVertex[] vertices,
            Face[] faces,
            float[] faceDepths,
            float[] faceNormals,
            float[] faceLightIntensities,
            float maxSpectrum,
            float cubeSize,
            int barCount)
        {
            Vertices = vertices;
            Faces = faces;
            FaceDepths = faceDepths;
            FaceNormals = faceNormals;
            FaceLightIntensities = faceLightIntensities;
            MaxSpectrum = maxSpectrum;
            CubeSize = cubeSize;
            BarCount = barCount;
        }
    }
    #endregion

    #region Fields
    private static readonly Lazy<CubeRenderer> _instance = new(() => new CubeRenderer());

    // 3D model data
    private readonly Vertex[] _vertices;
    private readonly Face[] _faces;
    private readonly Vector3[] _faceNormalVectors;
    private readonly ProjectedVertex[] _projectedVertices;
    private readonly float[] _faceDepths;
    private readonly float[] _faceNormals;
    private readonly float[] _faceLightIntensities;

    // Paint objects for rendering
    private readonly SKPaint[] _facePaints;
    private readonly SKPaint _edgePaint;

    // Rotation state
    private float _rotationAngleX, _rotationAngleY, _rotationAngleZ;
    private float _rotationSpeedX, _rotationSpeedY, _rotationSpeedZ;
    private Matrix4x4 _rotationMatrix = Matrix4x4.Identity;

    // Spectrum and timing
    private float[] _spectrum = Array.Empty<float>();
    private DateTime _lastUpdateTime = Now;
    private float _currentCubeSize = Constants.BASE_CUBE_SIZE;
    private int _currentBarCount;

    // Background processing
    private Thread? _processingThread;
    private CancellationTokenSource? _cts;
    private AutoResetEvent? _spectrumDataAvailable;
    private AutoResetEvent? _processingComplete;
    private readonly object _renderDataLock = new();
    private float[]? _spectrumToProcess;
    private int _barCountToProcess;
    private bool _processingRunning;

    // Rendering state
    private RenderData? _currentRenderData;
    private SKImageInfo _lastImageInfo;
    private bool _dataReady;

    // Quality settings
    private new bool _useAntiAlias = true;
    private SKSamplingOptions _samplingOptions = new(SKFilterMode.Linear, SKMipmapMode.Linear);
    private bool _useGlowEffects = true;
    private new bool _disposed;
    #endregion

    #region Singleton Pattern
    /// <summary>
    /// Private constructor to enforce Singleton pattern.
    /// </summary>
    private CubeRenderer()
    {
        _vertices = CreateCubeVertices();
        _faces = CreateCubeFaces();
        _faceNormalVectors = CalculateFaceNormals();
        _projectedVertices = new ProjectedVertex[_vertices.Length];
        _faceDepths = new float[_faces.Length];
        _faceNormals = new float[_faces.Length];
        _faceLightIntensities = new float[_faces.Length];

        _facePaints = new SKPaint[FACE_COLORS.Length];
        for (int i = 0; i < FACE_COLORS.Length; i++)
        {
            _facePaints[i] = new SKPaint
            {
                Color = FACE_COLORS[i],
                IsAntialias = true,
                Style = Fill
            };
        }

        _edgePaint = new SKPaint
        {
            Color = SKColors.White,
            IsAntialias = true,
            Style = Stroke,
            StrokeWidth = 1.5f
        };

        _rotationSpeedX = Constants.BASE_ROTATION_SPEED * 0.8f;
        _rotationSpeedY = Constants.BASE_ROTATION_SPEED * 1.2f;
        _rotationSpeedZ = Constants.BASE_ROTATION_SPEED * 0.6f;
    }

    /// <summary>
    /// Gets the singleton instance of the cube renderer.
    /// </summary>
    public static CubeRenderer GetInstance() => _instance.Value;
    #endregion

    #region Initialization and Configuration
    /// <summary>
    /// Initializes the 3D cube renderer and starts the processing thread.
    /// </summary>
    public override void Initialize()
    {
        Safe(() =>
        {
            base.Initialize();

            _cts = new CancellationTokenSource();
            _spectrumDataAvailable = new AutoResetEvent(false);
            _processingComplete = new AutoResetEvent(false);
            _processingRunning = true;

            _processingThread = new Thread(ProcessSpectrumThreadFunc)
            {
                IsBackground = true,
                Name = "CubeProcessor"
            };
            _processingThread.Start();

            Log(LogLevel.Debug, Constants.LOG_PREFIX, "Initialized");
        }, new ErrorHandlingOptions
        {
            Source = $"{Constants.LOG_PREFIX}.Initialize",
            ErrorMessage = "Failed to initialize renderer"
        });
    }

    /// <summary>
    /// Configures the renderer with overlay status and quality settings.
    /// </summary>
    /// <param name="isOverlayActive">Indicates if the renderer is used in overlay mode.</param>
    /// <param name="quality">The rendering quality level.</param>
    public override void Configure(bool isOverlayActive, RenderQuality quality = RenderQuality.Medium)
    {
        Safe(() =>
        {
            base.Configure(isOverlayActive, quality);

            // Apply quality settings if changed
            if (_quality != quality)
            {
                ApplyQualitySettings();
            }
        }, new ErrorHandlingOptions
        {
            Source = $"{Constants.LOG_PREFIX}.Configure",
            ErrorMessage = "Failed to configure renderer"
        });
    }

    /// <summary>
    /// Applies quality settings based on the current quality level.
    /// </summary>
    protected override void ApplyQualitySettings()
    {
        Safe(() =>
        {
            base.ApplyQualitySettings();

            _useGlowEffects = _quality switch
            {
                RenderQuality.Low => false,
                RenderQuality.Medium => true,
                RenderQuality.High => true,
                _ => true
            };

            foreach (var paint in _facePaints)
            {
                paint.IsAntialias = _useAntiAlias;
            }

            _edgePaint.IsAntialias = _useAntiAlias;

            _samplingOptions = _quality switch
            {
                RenderQuality.Low => new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None),
                RenderQuality.Medium => new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear),
                RenderQuality.High => new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear),
                _ => new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear)
            };
        }, new ErrorHandlingOptions
        {
            Source = $"{Constants.LOG_PREFIX}.ApplyQualitySettings",
            ErrorMessage = "Failed to apply quality settings"
        });
    }
    #endregion

    #region 3D Geometry Construction
    /// <summary>
    /// Creates the cube vertices in 3D space.
    /// </summary>
    private static Vertex[] CreateCubeVertices() => new Vertex[]
    {
        new(-0.5f, -0.5f,  0.5f), new( 0.5f, -0.5f,  0.5f), new( 0.5f,  0.5f,  0.5f), new(-0.5f,  0.5f,  0.5f),
        new(-0.5f, -0.5f, -0.5f), new( 0.5f, -0.5f, -0.5f), new( 0.5f,  0.5f, -0.5f), new(-0.5f,  0.5f, -0.5f),
    };

    /// <summary>
    /// Creates the cube faces as triangles.
    /// </summary>
    private static Face[] CreateCubeFaces() => new Face[]
    {
        new(0, 1, 2, 0), new(0, 2, 3, 0), // Front
        new(4, 6, 5, 1), new(4, 7, 6, 1), // Back
        new(3, 2, 6, 2), new(3, 6, 7, 2), // Top
        new(0, 5, 1, 3), new(0, 4, 5, 3), // Bottom
        new(1, 5, 6, 4), new(1, 6, 2, 4), // Right
        new(0, 3, 7, 5), new(0, 7, 4, 5)  // Left
    };

    /// <summary>
    /// Calculates face normal vectors for lighting calculations.
    /// </summary>
    private Vector3[] CalculateFaceNormals()
    {
        Vector3[] normals = new Vector3[6];
        normals[0] = new Vector3(0, 0, 1);   // Front
        normals[1] = new Vector3(0, 0, -1);  // Back
        normals[2] = new Vector3(0, 1, 0);   // Top
        normals[3] = new Vector3(0, -1, 0);  // Bottom
        normals[4] = new Vector3(1, 0, 0);   // Right
        normals[5] = new Vector3(-1, 0, 0);  // Left
        return normals;
    }
    #endregion

    #region Rendering
    /// <summary>
    /// Renders the 3D cube visualization on the canvas using spectrum data.
    /// </summary>
    public override void Render(
        SKCanvas? canvas,
        float[]? spectrum,
        SKImageInfo info,
        float barWidth,
        float barSpacing,
        int barCount,
        SKPaint? paint,
        Action<SKCanvas, SKImageInfo>? drawPerformanceInfo)
    {
        if (!QuickValidate(canvas, spectrum, info, paint))
        {
            drawPerformanceInfo?.Invoke(canvas!, info);
            return;
        }

        // Define render bounds and quick reject if not visible
        SKRect renderBounds = new(0, 0, info.Width, info.Height);
        if (canvas!.QuickReject(renderBounds))
        {
            drawPerformanceInfo?.Invoke(canvas, info);
            return;
        }

        Safe(() =>
        {
            _lastImageInfo = info;
            SubmitSpectrumForProcessing(spectrum, barCount);

            DateTime now = Now;
            float deltaTime = (float)(now - _lastUpdateTime).TotalSeconds;
            _lastUpdateTime = now;

            _rotationAngleX = (_rotationAngleX + _rotationSpeedX * deltaTime) % MathF.Tau;
            _rotationAngleY = (_rotationAngleY + _rotationSpeedY * deltaTime) % MathF.Tau;
            _rotationAngleZ = (_rotationAngleZ + _rotationSpeedZ * deltaTime) % MathF.Tau;

            if (_dataReady) RenderCube(canvas, info, paint!);
        }, new ErrorHandlingOptions
        {
            Source = $"{Constants.LOG_PREFIX}.Render",
            ErrorMessage = "Error during rendering"
        });

        drawPerformanceInfo?.Invoke(canvas!, info);
    }

    /// <summary>
    /// Renders the 3D cube based on the current render data.
    /// </summary>
    [MethodImpl(AggressiveOptimization)]
    private void RenderCube(SKCanvas canvas, SKImageInfo info, SKPaint paint)
    {
        Safe(() =>
        {
            RenderData renderData;
            lock (_renderDataLock)
            {
                if (!_dataReady || _currentRenderData == null) return;
                renderData = _currentRenderData.Value;
            }

            for (int i = 0; i < renderData.Faces.Length; i++)
            {
                var face = renderData.Faces[i];
                float normalValue = renderData.FaceNormals[i];
                if (normalValue <= 0) continue;

                var v1 = renderData.Vertices[face.V1];
                var v2 = renderData.Vertices[face.V2];
                var v3 = renderData.Vertices[face.V3];

                using var path = new SKPath();
                path.MoveTo(v1.X, v1.Y);
                path.LineTo(v2.X, v2.Y);
                path.LineTo(v3.X, v3.Y);
                path.Close();

                var baseColor = FACE_COLORS[face.FaceIndex];
                float intensity = renderData.FaceLightIntensities[i];
                byte r = (byte)Clamp(baseColor.Red * intensity, 0, 255);
                byte g = (byte)Clamp(baseColor.Green * intensity, 0, 255);
                byte b = (byte)Clamp(baseColor.Blue * intensity, 0, 255);
                byte alpha = (byte)Clamp(
                    (Constants.BASE_ALPHA + renderData.MaxSpectrum * Constants.SPECTRUM_ALPHA_INFLUENCE) *
                    255 * normalValue, 0, 255);

                SKColor litColor = new SKColor(r, g, b, alpha);
                var facePaint = _facePaints[face.FaceIndex];
                facePaint.Color = litColor;

                canvas.DrawPath(path, facePaint);

                if (_useGlowEffects)
                {
                    _edgePaint.Color = SKColors.White.WithAlpha((byte)(alpha * Constants.EDGE_ALPHA_MULTIPLIER));
                    canvas.DrawPath(path, _edgePaint);
                }
            }
        }, new ErrorHandlingOptions
        {
            Source = $"{Constants.LOG_PREFIX}.RenderCube",
            ErrorMessage = "Error rendering cube"
        });
    }
    #endregion

    #region Spectrum Processing
    /// <summary>
    /// Submits spectrum data for background processing.
    /// </summary>
    private void SubmitSpectrumForProcessing(float[]? spectrum, int barCount)
    {
        Safe(() =>
        {
            if (spectrum == null || _spectrumDataAvailable == null || _processingComplete == null) return;

            lock (_renderDataLock)
            {
                _spectrumToProcess = spectrum;
                _barCountToProcess = barCount;
            }

            _spectrumDataAvailable.Set();
            _processingComplete.WaitOne(5);
        }, new ErrorHandlingOptions
        {
            Source = $"{Constants.LOG_PREFIX}.SubmitSpectrumForProcessing",
            ErrorMessage = "Failed to submit spectrum for processing"
        });
    }

    /// <summary>
    /// Background thread function for processing spectrum data.
    /// </summary>
    private void ProcessSpectrumThreadFunc()
    {
        try
        {
            while (_processingRunning && _cts != null && !_cts.Token.IsCancellationRequested &&
                   _spectrumDataAvailable != null && _processingComplete != null)
            {
                _spectrumDataAvailable.WaitOne();

                float[]? spectrumCopy;
                int barCountCopy;

                lock (_renderDataLock)
                {
                    if (_spectrumToProcess == null)
                    {
                        _processingComplete.Set();
                        continue;
                    }

                    spectrumCopy = _spectrumToProcess;
                    barCountCopy = _barCountToProcess;
                }

                ComputeCubeData(spectrumCopy, barCountCopy, _lastImageInfo);
                _processingComplete.Set();
            }
        }
        catch (OperationCanceledException) { /* Expected during shutdown */ }
        catch (Exception ex)
        {
            Log(LogLevel.Error, Constants.LOG_PREFIX, $"Error in cube processing thread: {ex.Message}");
        }
    }

    /// <summary>
    /// Computes 3D cube data based on spectrum information.
    /// </summary>
    [MethodImpl(AggressiveOptimization)]
    private void ComputeCubeData(float[] spectrum, int barCount, SKImageInfo info)
    {
        Safe(() =>
        {
            if (_cts == null || _cts.Token.IsCancellationRequested) return;

            _spectrum = spectrum;
            _currentBarCount = barCount;

            UpdateCubeSize();
            UpdateRotationSpeeds();
            _rotationMatrix = CreateRotationMatrix();

            float centerX = info.Width * 0.5f;
            float centerY = info.Height * 0.5f;
            float barCountScale = 1.0f + MathF.Log10(Max(1, _currentBarCount)) * 0.3f;
            barCountScale = Clamp(barCountScale, 1.0f, 2.5f);
            float scale = MathF.Min(centerX, centerY) * _currentCubeSize * barCountScale;

            ProjectVertices(scale, centerX, centerY);
            if (_cts.Token.IsCancellationRequested) return;

            CalculateFaceDepthsAndNormals();
            SortFacesByDepth();

            float maxSpectrumValue = 0f;
            foreach (var val in _spectrum) if (val > maxSpectrumValue) maxSpectrumValue = val;

            lock (_renderDataLock)
            {
                _currentRenderData = new RenderData(
                    _projectedVertices,
                    _faces,
                    _faceDepths,
                    _faceNormals,
                    _faceLightIntensities,
                    maxSpectrumValue,
                    _currentCubeSize,
                    _currentBarCount);

                _dataReady = true;
            }
        }, new ErrorHandlingOptions
        {
            Source = $"{Constants.LOG_PREFIX}.ComputeCubeData",
            ErrorMessage = "Error computing cube data"
        });
    }
    #endregion

    #region 3D Math and Transformations
    /// <summary>
    /// Updates the cube size based on spectrum intensity.
    /// </summary>
    private void UpdateCubeSize()
    {
        Safe(() =>
        {
            if (_spectrum.Length == 0) return;

            float avgIntensity = 0f;
            for (int i = 0; i < _spectrum.Length; i++) avgIntensity += _spectrum[i];
            avgIntensity /= _spectrum.Length;

            float targetSize = Constants.BASE_CUBE_SIZE + avgIntensity * Constants.CUBE_SIZE_RESPONSE_FACTOR;
            targetSize = Clamp(targetSize, Constants.MIN_CUBE_SIZE, Constants.MAX_CUBE_SIZE);
            _currentCubeSize = _currentCubeSize * 0.9f + targetSize * 0.1f;
        }, new ErrorHandlingOptions
        {
            Source = $"{Constants.LOG_PREFIX}.UpdateCubeSize",
            ErrorMessage = "Error updating cube size"
        });
    }

    /// <summary>
    /// Updates rotation speeds based on different frequency bands.
    /// </summary>
    private void UpdateRotationSpeeds()
    {
        Safe(() =>
        {
            if (_spectrum.Length < 3) return;

            float lowFreq = _spectrum.Length > 0 ? _spectrum[0] : 0;
            float midFreq = _spectrum.Length > 3 ? _spectrum[_spectrum.Length / 2] : 0;
            float highFreq = _spectrum.Length > 6 ? _spectrum[_spectrum.Length - 1] : 0;

            _rotationSpeedX = Constants.BASE_ROTATION_SPEED + lowFreq * Constants.SPECTRUM_ROTATION_INFLUENCE;
            _rotationSpeedY = Constants.BASE_ROTATION_SPEED * 1.2f + midFreq * Constants.SPECTRUM_ROTATION_INFLUENCE;
            _rotationSpeedZ = Constants.BASE_ROTATION_SPEED * 0.8f + highFreq * Constants.SPECTRUM_ROTATION_INFLUENCE;

            _rotationSpeedX = Min(_rotationSpeedX, Constants.MAX_ROTATION_SPEED);
            _rotationSpeedY = Min(_rotationSpeedY, Constants.MAX_ROTATION_SPEED);
            _rotationSpeedZ = Min(_rotationSpeedZ, Constants.MAX_ROTATION_SPEED);
        }, new ErrorHandlingOptions
        {
            Source = $"{Constants.LOG_PREFIX}.UpdateRotationSpeeds",
            ErrorMessage = "Error updating rotation speeds"
        });
    }

    /// <summary>
    /// Creates a combined rotation matrix for all three axes.
    /// </summary>
    [MethodImpl(AggressiveInlining)]
    private Matrix4x4 CreateRotationMatrix() =>
        Matrix4x4.CreateRotationX(_rotationAngleX) *
        Matrix4x4.CreateRotationY(_rotationAngleY) *
        Matrix4x4.CreateRotationZ(_rotationAngleZ);

    /// <summary>
    /// Projects 3D vertices to 2D screen coordinates with optimized matrix math.
    /// </summary>
    [MethodImpl(AggressiveOptimization)]
    private void ProjectVertices(float scale, float centerX, float centerY)
    {
        Safe(() =>
        {
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
            Source = $"{Constants.LOG_PREFIX}.ProjectVertices",
            ErrorMessage = "Error projecting vertices"
        });
    }

    /// <summary>
    /// Calculates face depths and normals for lighting and depth sorting.
    /// </summary>
    [MethodImpl(AggressiveOptimization)]
    private void CalculateFaceDepthsAndNormals()
    {
        Safe(() =>
        {
            for (int i = 0; i < _faces.Length; i++)
            {
                var face = _faces[i];
                _faceDepths[i] = (_projectedVertices[face.V1].Depth +
                                  _projectedVertices[face.V2].Depth +
                                  _projectedVertices[face.V3].Depth) / 3f;

                Vector3 faceNormal = _faceNormalVectors[face.FaceIndex];
                Vector3 rotatedNormal = Vector3.Transform(faceNormal, _rotationMatrix);
                rotatedNormal = Vector3.Normalize(rotatedNormal);

                _faceNormals[i] = Vector3.Dot(rotatedNormal, new Vector3(0, 0, 1));
                float lightIntensity = Vector3.Dot(rotatedNormal, LIGHT_DIRECTION);
                lightIntensity = Constants.AMBIENT_LIGHT + Constants.DIFFUSE_LIGHT * Max(0, lightIntensity);
                _faceLightIntensities[i] = lightIntensity;
            }
        }, new ErrorHandlingOptions
        {
            Source = $"{Constants.LOG_PREFIX}.CalculateFaceDepthsAndNormals",
            ErrorMessage = "Error calculating face depths and normals"
        });
    }

    /// <summary>
    /// Sorts faces by depth for proper rendering (painter's algorithm).
    /// </summary>
    [MethodImpl(AggressiveOptimization)]
    private void SortFacesByDepth()
    {
        Safe(() =>
        {
            int[] indices = new int[_faces.Length];
            for (int i = 0; i < indices.Length; i++) indices[i] = i;

            Array.Sort(indices, (a, b) => _faceDepths[a].CompareTo(_faceDepths[b]));

            Face[] sortedFaces = new Face[_faces.Length];
            float[] sortedDepths = new float[_faceDepths.Length];
            float[] sortedNormals = new float[_faceNormals.Length];
            float[] sortedLightIntensities = new float[_faceLightIntensities.Length];

            for (int i = 0; i < indices.Length; i++)
            {
                sortedFaces[i] = _faces[indices[i]];
                sortedDepths[i] = _faceDepths[indices[i]];
                sortedNormals[i] = _faceNormals[indices[i]];
                sortedLightIntensities[i] = _faceLightIntensities[indices[i]];
            }

            Array.Copy(sortedFaces, _faces, _faces.Length);
            Array.Copy(sortedDepths, _faceDepths, _faceDepths.Length);
            Array.Copy(sortedNormals, _faceNormals, _faceNormals.Length);
            Array.Copy(sortedLightIntensities, _faceLightIntensities, _faceLightIntensities.Length);
        }, new ErrorHandlingOptions
        {
            Source = $"{Constants.LOG_PREFIX}.SortFacesByDepth",
            ErrorMessage = "Error sorting faces by depth"
        });
    }
    #endregion

    #region Disposal
    /// <summary>
    /// Disposes of resources used by the renderer.
    /// </summary>
    public override void Dispose()
    {
        if (!_disposed)
        {
            Safe(() =>
            {
                _processingRunning = false;
                _cts?.Cancel();
                _spectrumDataAvailable?.Set();
                _processingThread?.Join(Constants.THREAD_JOIN_TIMEOUT_MS);

                foreach (var paint in _facePaints) paint.Dispose();
                _edgePaint.Dispose();

                _cts?.Dispose();
                _spectrumDataAvailable?.Dispose();
                _processingComplete?.Dispose();

                _cts = null;
                _spectrumDataAvailable = null;
                _processingComplete = null;
                _processingThread = null;

                base.Dispose();

                _disposed = true;
                Log(LogLevel.Debug, Constants.LOG_PREFIX, "Disposed");
            }, new ErrorHandlingOptions
            {
                Source = $"{Constants.LOG_PREFIX}.Dispose",
                ErrorMessage = "Error during disposal"
            });
        }
    }
    #endregion
}