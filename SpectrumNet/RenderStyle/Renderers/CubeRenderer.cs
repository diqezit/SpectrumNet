﻿#nullable enable

namespace SpectrumNet
{
    /// <summary>
    /// Renderer that creates a 3D cube that reacts to audio spectrum data.
    /// </summary>
    public sealed class CubeRenderer : BaseSpectrumRenderer
    {
        #region Structures
        private readonly record struct Vertex(float X, float Y, float Z);
        private struct ProjectedVertex { public float X, Y, Depth; }
        private readonly record struct Face(int V1, int V2, int V3, int FaceIndex);
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

            public RenderData(ProjectedVertex[] vertices, Face[] faces, float[] faceDepths,
                              float[] faceNormals, float[] faceLightIntensities, float maxSpectrum,
                              float cubeSize, int barCount)
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

        #region Constants
        private static class Constants
        {
            // Logging
            public const string LOG_PREFIX = "CubeRenderer";

            // Cube properties
            public const float BaseCubeSize = 0.5f;       // Базовый размер куба
            public const float MinCubeSize = 0.2f;        // Минимальный размер куба
            public const float MaxCubeSize = 1.0f;        // Максимальный размер куба
            public const float CubeSizeResponseFactor = 0.5f; // Фактор изменения размера от спектра

            // Rotation properties
            public const float BaseRotationSpeed = 0.5f;  // Базовая скорость вращения
            public const float SpectrumRotationInfluence = 0.015f; // Влияние спектра на скорость
            public const float MaxRotationSpeed = 0.05f;  // Максимальная скорость вращения

            // Lighting properties
            public const float AmbientLight = 0.4f;       // Интенсивность окружающего света
            public const float DiffuseLight = 0.6f;       // Интенсивность диффузного света
            public static readonly Vector3 LightDirection = Vector3.Normalize(new Vector3(0.5f, 0.7f, -1.0f)); // Направление света

            // Color properties
            public const float BaseAlpha = 0.9f;          // Базовая прозрачность граней
            public const float SpectrumAlphaInfluence = 0.1f; // Влияние спектра на прозрачность
            public const float EdgeAlphaMultiplier = 0.8f; // Множитель прозрачности краев
            public static readonly SKColor[] FaceColors = {
                new SKColor(255, 100, 100),  // Красноватый
                new SKColor(100, 255, 100),  // Зеленоватый
                new SKColor(100, 100, 255),  // Синеватый
                new SKColor(255, 255, 100),  // Желтоватый
                new SKColor(255, 100, 255),  // Розоватый
                new SKColor(100, 255, 255)   // Голубоватый
            };
        }
        #endregion

        #region Fields
        private static readonly Lazy<CubeRenderer> _instance = new(() => new CubeRenderer());
        private readonly Vertex[] _vertices;
        private readonly Face[] _faces;
        private readonly Vector3[] _faceNormalVectors;
        private readonly ProjectedVertex[] _projectedVertices;
        private readonly float[] _faceDepths;
        private readonly float[] _faceNormals;
        private readonly float[] _faceLightIntensities;
        private float _rotationAngleX, _rotationAngleY, _rotationAngleZ;
        private float _rotationSpeedX, _rotationSpeedY, _rotationSpeedZ;
        private Matrix4x4 _rotationMatrix = Matrix4x4.Identity;
        private float[] _spectrum = Array.Empty<float>();
        private float _currentCubeSize = Constants.BaseCubeSize;
        private int _currentBarCount;
        private Thread? _processingThread;
        private CancellationTokenSource? _cts;
        private AutoResetEvent? _spectrumDataAvailable;
        private AutoResetEvent? _processingComplete;
        private readonly object _renderDataLock = new();
        private float[]? _spectrumToProcess;
        private int _barCountToProcess;
        private bool _processingRunning;
        private RenderData? _currentRenderData;
        private SKImageInfo _lastImageInfo;
        private bool _dataReady;
        private readonly SKPaint[] _facePaints;
        private readonly SKPaint _edgePaint;
        private DateTime _lastUpdateTime = DateTime.Now;
        private bool _useGlowEffects = true;
        #endregion

        #region Initialization
        public static CubeRenderer GetInstance() => _instance.Value;

        private CubeRenderer()
        {
            _vertices = CreateCubeVertices();
            _faces = CreateCubeFaces();
            _faceNormalVectors = CalculateFaceNormals();
            _projectedVertices = new ProjectedVertex[_vertices.Length];
            _faceDepths = new float[_faces.Length];
            _faceNormals = new float[_faces.Length];
            _faceLightIntensities = new float[_faces.Length];

            _facePaints = new SKPaint[Constants.FaceColors.Length];
            for (int i = 0; i < Constants.FaceColors.Length; i++)
            {
                _facePaints[i] = new SKPaint
                {
                    Color = Constants.FaceColors[i],
                    IsAntialias = true,
                    Style = SKPaintStyle.Fill
                };
            }

            _edgePaint = new SKPaint
            {
                Color = SKColors.White,
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 1.5f
            };

            _rotationSpeedX = Constants.BaseRotationSpeed * 0.8f;
            _rotationSpeedY = Constants.BaseRotationSpeed * 1.2f;
            _rotationSpeedZ = Constants.BaseRotationSpeed * 0.6f;
        }

        public override void Initialize() => SmartLogger.Safe(() =>
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

            SmartLogger.Log(LogLevel.Debug, Constants.LOG_PREFIX, "Initialized");
        }, new SmartLogger.ErrorHandlingOptions
        {
            Source = $"{Constants.LOG_PREFIX}.Initialize",
            ErrorMessage = "Failed to initialize renderer"
        });

        private static Vertex[] CreateCubeVertices() => new Vertex[]
        {
            new(-0.5f, -0.5f,  0.5f), new( 0.5f, -0.5f,  0.5f), new( 0.5f,  0.5f,  0.5f), new(-0.5f,  0.5f,  0.5f),
            new(-0.5f, -0.5f, -0.5f), new( 0.5f, -0.5f, -0.5f), new( 0.5f,  0.5f, -0.5f), new(-0.5f,  0.5f, -0.5f),
        };

        private static Face[] CreateCubeFaces() => new Face[]
        {
            new(0, 1, 2, 0), new(0, 2, 3, 0), // Front
            new(4, 6, 5, 1), new(4, 7, 6, 1), // Back
            new(3, 2, 6, 2), new(3, 6, 7, 2), // Top
            new(0, 5, 1, 3), new(0, 4, 5, 3), // Bottom
            new(1, 5, 6, 4), new(1, 6, 2, 4), // Right
            new(0, 3, 7, 5), new(0, 7, 4, 5)  // Left
        };

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

        #region Public Interface
        public override void Configure(bool isOverlayActive, RenderQuality quality = RenderQuality.Medium) => SmartLogger.Safe(() =>
        {
            base.Configure(isOverlayActive, quality);
        }, new SmartLogger.ErrorHandlingOptions
        {
            Source = $"{Constants.LOG_PREFIX}.Configure",
            ErrorMessage = "Failed to configure renderer"
        });

        protected override void ApplyQualitySettings() => SmartLogger.Safe(() =>
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
                paint.FilterQuality = _filterQuality;
            }

            _edgePaint.IsAntialias = _useAntiAlias;
            _edgePaint.FilterQuality = _filterQuality;
        }, new SmartLogger.ErrorHandlingOptions
        {
            Source = $"{Constants.LOG_PREFIX}.ApplyQualitySettings",
            ErrorMessage = "Failed to apply quality settings"
        });

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

            SmartLogger.Safe(() =>
            {
                _lastImageInfo = info;
                SubmitSpectrumForProcessing(spectrum, barCount);

                DateTime now = DateTime.Now;
                float deltaTime = (float)(now - _lastUpdateTime).TotalSeconds;
                _lastUpdateTime = now;

                _rotationAngleX = (_rotationAngleX + _rotationSpeedX * deltaTime) % MathF.Tau;
                _rotationAngleY = (_rotationAngleY + _rotationSpeedY * deltaTime) % MathF.Tau;
                _rotationAngleZ = (_rotationAngleZ + _rotationSpeedZ * deltaTime) % MathF.Tau;

                if (_dataReady) RenderCube(canvas!, info, paint!);
            }, new SmartLogger.ErrorHandlingOptions
            {
                Source = $"{Constants.LOG_PREFIX}.Render",
                ErrorMessage = "Error during rendering"
            });

            drawPerformanceInfo?.Invoke(canvas!, info);
        }

        public override void Dispose()
        {
            if (!_disposed)
            {
                SmartLogger.Safe(() =>
                {
                    _processingRunning = false;
                    _cts?.Cancel();
                    _spectrumDataAvailable?.Set();
                    _processingThread?.Join(100);

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
                }, new SmartLogger.ErrorHandlingOptions
                {
                    Source = $"{Constants.LOG_PREFIX}.Dispose",
                    ErrorMessage = "Error during disposal"
                });
            }
        }
        #endregion

        #region Processing
        private void SubmitSpectrumForProcessing(float[]? spectrum, int barCount) => SmartLogger.Safe(() =>
        {
            if (spectrum == null || _spectrumDataAvailable == null || _processingComplete == null) return;
            lock (_renderDataLock) { _spectrumToProcess = spectrum; _barCountToProcess = barCount; }
            _spectrumDataAvailable.Set();
            _processingComplete.WaitOne(5);
        }, new SmartLogger.ErrorHandlingOptions
        {
            Source = $"{Constants.LOG_PREFIX}.SubmitSpectrumForProcessing",
            ErrorMessage = "Failed to submit spectrum for processing"
        });

        private void ProcessSpectrumThreadFunc()
        {
            try
            {
                while (_processingRunning && _cts != null && !_cts.Token.IsCancellationRequested && _spectrumDataAvailable != null && _processingComplete != null)
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
                    ComputeCubeData(spectrumCopy, barCountCopy, _lastImageInfo);
                    _processingComplete.Set();
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, Constants.LOG_PREFIX, $"Error in cube processing thread: {ex.Message}");
            }
        }

        private void ComputeCubeData(float[] spectrum, int barCount, SKImageInfo info) => SmartLogger.Safe(() =>
        {
            if (_cts == null || _cts.Token.IsCancellationRequested) return;
            _spectrum = spectrum;
            _currentBarCount = barCount;

            UpdateCubeSize();
            UpdateRotationSpeeds();
            _rotationMatrix = CreateRotationMatrix();

            float centerX = info.Width * 0.5f;
            float centerY = info.Height * 0.5f;
            float barCountScale = 1.0f + MathF.Log10(Math.Max(1, _currentBarCount)) * 0.3f;
            barCountScale = Math.Clamp(barCountScale, 1.0f, 2.5f);
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
                    _projectedVertices, _faces, _faceDepths, _faceNormals, _faceLightIntensities,
                    maxSpectrumValue, _currentCubeSize, _currentBarCount);
                _dataReady = true;
            }
        }, new SmartLogger.ErrorHandlingOptions
        {
            Source = $"{Constants.LOG_PREFIX}.ComputeCubeData",
            ErrorMessage = "Error computing cube data"
        });
        #endregion

        #region Rendering
        private void RenderCube(SKCanvas canvas, SKImageInfo info, SKPaint paint) => SmartLogger.Safe(() =>
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

                var baseColor = Constants.FaceColors[face.FaceIndex];
                float intensity = renderData.FaceLightIntensities[i];
                byte r = (byte)Math.Clamp(baseColor.Red * intensity, 0, 255);
                byte g = (byte)Math.Clamp(baseColor.Green * intensity, 0, 255);
                byte b = (byte)Math.Clamp(baseColor.Blue * intensity, 0, 255);
                byte alpha = (byte)Math.Clamp(
                    (Constants.BaseAlpha + renderData.MaxSpectrum * Constants.SpectrumAlphaInfluence) *
                    255 * normalValue, 0, 255);

                SKColor litColor = new SKColor(r, g, b, alpha);
                var facePaint = _facePaints[face.FaceIndex];
                facePaint.Color = litColor;

                canvas.DrawPath(path, facePaint);

                if (_useGlowEffects)
                {
                    _edgePaint.Color = SKColors.White.WithAlpha((byte)(alpha * Constants.EdgeAlphaMultiplier));
                    canvas.DrawPath(path, _edgePaint);
                }
            }
        }, new SmartLogger.ErrorHandlingOptions
        {
            Source = $"{Constants.LOG_PREFIX}.RenderCube",
            ErrorMessage = "Error rendering cube"
        });
        #endregion

        #region Computation Methods
        private void UpdateCubeSize() => SmartLogger.Safe(() =>
        {
            if (_spectrum.Length == 0) return;
            float avgIntensity = 0f;
            for (int i = 0; i < _spectrum.Length; i++) avgIntensity += _spectrum[i];
            avgIntensity /= _spectrum.Length;

            float targetSize = Constants.BaseCubeSize + (avgIntensity * Constants.CubeSizeResponseFactor);
            targetSize = Math.Clamp(targetSize, Constants.MinCubeSize, Constants.MaxCubeSize);
            _currentCubeSize = _currentCubeSize * 0.9f + targetSize * 0.1f;
        }, new SmartLogger.ErrorHandlingOptions
        {
            Source = $"{Constants.LOG_PREFIX}.UpdateCubeSize",
            ErrorMessage = "Error updating cube size"
        });

        private void UpdateRotationSpeeds() => SmartLogger.Safe(() =>
        {
            if (_spectrum.Length < 3) return;
            float lowFreq = _spectrum.Length > 0 ? _spectrum[0] : 0;
            float midFreq = _spectrum.Length > 3 ? _spectrum[_spectrum.Length / 2] : 0;
            float highFreq = _spectrum.Length > 6 ? _spectrum[_spectrum.Length - 1] : 0;

            _rotationSpeedX = Constants.BaseRotationSpeed + (lowFreq * Constants.SpectrumRotationInfluence);
            _rotationSpeedY = Constants.BaseRotationSpeed * 1.2f + (midFreq * Constants.SpectrumRotationInfluence);
            _rotationSpeedZ = Constants.BaseRotationSpeed * 0.8f + (highFreq * Constants.SpectrumRotationInfluence);

            _rotationSpeedX = Math.Min(_rotationSpeedX, Constants.MaxRotationSpeed);
            _rotationSpeedY = Math.Min(_rotationSpeedY, Constants.MaxRotationSpeed);
            _rotationSpeedZ = Math.Min(_rotationSpeedZ, Constants.MaxRotationSpeed);
        }, new SmartLogger.ErrorHandlingOptions
        {
            Source = $"{Constants.LOG_PREFIX}.UpdateRotationSpeeds",
            ErrorMessage = "Error updating rotation speeds"
        });

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Matrix4x4 CreateRotationMatrix() =>
            Matrix4x4.CreateRotationX(_rotationAngleX) *
            Matrix4x4.CreateRotationY(_rotationAngleY) *
            Matrix4x4.CreateRotationZ(_rotationAngleZ);

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private void ProjectVertices(float scale, float centerX, float centerY) => SmartLogger.Safe(() =>
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
        }, new SmartLogger.ErrorHandlingOptions
        {
            Source = $"{Constants.LOG_PREFIX}.ProjectVertices",
            ErrorMessage = "Error projecting vertices"
        });

        private void CalculateFaceDepthsAndNormals() => SmartLogger.Safe(() =>
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
                float lightIntensity = Vector3.Dot(rotatedNormal, Constants.LightDirection);
                lightIntensity = Constants.AmbientLight + Constants.DiffuseLight * Math.Max(0, lightIntensity);
                _faceLightIntensities[i] = lightIntensity;
            }
        }, new SmartLogger.ErrorHandlingOptions
        {
            Source = $"{Constants.LOG_PREFIX}.CalculateFaceDepthsAndNormals",
            ErrorMessage = "Error calculating face depths and normals"
        });

        private void SortFacesByDepth() => SmartLogger.Safe(() =>
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
        }, new SmartLogger.ErrorHandlingOptions
        {
            Source = $"{Constants.LOG_PREFIX}.SortFacesByDepth",
            ErrorMessage = "Error sorting faces by depth"
        });
        #endregion
    }
}