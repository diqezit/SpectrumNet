#nullable enable

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace SpectrumNet
{
    /// <summary>
    /// Renderer that visualizes spectrum data as a dynamic Voronoi diagram with animated points.
    /// </summary>
    public sealed class VoronoiRenderer : BaseSpectrumRenderer
    {
        #region Singleton Pattern
        private static readonly Lazy<VoronoiRenderer> _instance = new(() => new VoronoiRenderer());
        private VoronoiRenderer() { } // Приватный конструктор
        public static VoronoiRenderer GetInstance() => _instance.Value;
        #endregion

        #region Constants
        private static class Constants
        {
            // Logging
            public const string LOG_PREFIX = "VoronoiRenderer";

            // Points configuration
            public const int DEFAULT_POINT_COUNT = 25;      // Default number of points
            public const int OVERLAY_POINT_COUNT = 15;      // Number of points in overlay mode
            public const float MIN_POINT_SIZE = 3f;         // Minimum point size
            public const float MAX_POINT_SIZE = 15f;        // Maximum point size
            public const float MIN_MOVE_SPEED = 0.3f;       // Minimum point movement speed
            public const float MAX_MOVE_SPEED = 2.0f;       // Maximum point movement speed

            // Grid for optimization
            public const int GRID_CELL_SIZE = 20;           // Size of grid cells for rendering
            public const float MAX_DISTANCE_FACTOR = 0.33f; // Max distance between points as screen size factor

            // Rendering properties
            public const float SMOOTHING_FACTOR = 0.2f;     // Smoothing factor for animations
            public const float BORDER_WIDTH = 1.0f;         // Width of cell borders
            public const byte BORDER_ALPHA = 180;           // Alpha of cell borders
            public const float TIME_STEP = 0.016f;          // Animation time step (~60fps)
            public const float SPECTRUM_AMPLIFICATION = 3f; // Spectrum amplification factor

            // Color variations
            public const int RED_VARIATION = 3;             // Red color variation multiplier
            public const int GREEN_VARIATION = 7;           // Green color variation multiplier
            public const int BLUE_VARIATION = 11;           // Blue color variation multiplier
            public const int MAX_COLOR_VARIATION = 50;      // Maximum color variation
            public const int MIN_CELL_ALPHA = 55;           // Minimum cell alpha
            public const int ALPHA_MULTIPLIER = 10;         // Alpha multiplier based on point size

            // Physics
            public const float VELOCITY_BOOST_FACTOR = 0.3f;// Velocity boost factor based on spectrum
        }
        #endregion

        #region Fields
        // Synchronization and state
        private readonly SemaphoreSlim _spectrumSemaphore = new(1, 1);
        private bool _isOverlayActive;
        private new bool _disposed;

        // Quality-dependent settings
        private new bool _useAntiAlias = true;
        private new SKSamplingOptions _samplingOptions = new(SKFilterMode.Linear, SKMipmapMode.Linear);
        private new bool _useAdvancedEffects = true;
        private bool _useHardwareAcceleration = true;

        // Spectrum and point data
        private float[]? _processedSpectrum;
        private List<VoronoiPoint>? _voronoiPoints;
        private readonly Random _random = new();
        private float _timeAccumulator;

        // Rendering resources
        private SKPaint? _cellPaint;
        private SKPaint? _borderPaint;

        // Caching and optimization
        private SKPicture? _cachedBackground;
        private int _lastWidth;
        private int _lastHeight;
        private int _gridCols;
        private int _gridRows;
        private int[,]? _nearestPointGrid;
        #endregion

        #region Helper Structures
        /// <summary>
        /// Represents a dynamic point in the Voronoi diagram.
        /// </summary>
        private struct VoronoiPoint
        {
            public float X;
            public float Y;
            public float VelocityX;
            public float VelocityY;
            public float Size;
            public int FrequencyIndex;
        }
        #endregion

        #region Initialization and Configuration
        /// <summary>
        /// Initializes the renderer and prepares resources for rendering.
        /// </summary>
        public override void Initialize()
        {
            SmartLogger.Safe(() =>
            {
                base.Initialize();

                _cellPaint = new SKPaint
                {
                    IsAntialias = _useAntiAlias,
                    Style = SKPaintStyle.Fill
                };

                _borderPaint = new SKPaint
                {
                    IsAntialias = _useAntiAlias,
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = Constants.BORDER_WIDTH,
                    Color = SKColors.White.WithAlpha(Constants.BORDER_ALPHA)
                };

                int pointCount = _isOverlayActive ? Constants.OVERLAY_POINT_COUNT : Constants.DEFAULT_POINT_COUNT;
                InitializePoints(pointCount);

                SmartLogger.Log(LogLevel.Debug, Constants.LOG_PREFIX, "Initialized");
            }, new SmartLogger.ErrorHandlingOptions
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
            SmartLogger.Safe(() =>
            {
                base.Configure(isOverlayActive, quality);

                // Update overlay state if changed
                if (_isOverlayActive != isOverlayActive)
                {
                    _isOverlayActive = isOverlayActive;
                    int pointCount = _isOverlayActive ? Constants.OVERLAY_POINT_COUNT : Constants.DEFAULT_POINT_COUNT;
                    InitializePoints(pointCount);
                }

                // Update quality if needed
                if (_quality != quality)
                {
                    _quality = quality;
                    ApplyQualitySettings();
                }
            }, new SmartLogger.ErrorHandlingOptions
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
            SmartLogger.Safe(() =>
            {
                base.ApplyQualitySettings();

                switch (_quality)
                {
                    case RenderQuality.Low:
                        _useAntiAlias = false;
                        _samplingOptions = new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None);
                        _useAdvancedEffects = false;
                        _useHardwareAcceleration = true;
                        break;

                    case RenderQuality.Medium:
                        _useAntiAlias = true;
                        _samplingOptions = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);
                        _useAdvancedEffects = true;
                        _useHardwareAcceleration = true;
                        break;

                    case RenderQuality.High:
                        _useAntiAlias = true;
                        _samplingOptions = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);
                        _useAdvancedEffects = true;
                        _useHardwareAcceleration = true;
                        break;
                }

                // Update existing paint objects
                if (_cellPaint != null)
                {
                    _cellPaint.IsAntialias = _useAntiAlias;
                }

                if (_borderPaint != null)
                {
                    _borderPaint.IsAntialias = _useAntiAlias;
                }

                // Invalidate cached resources
                InvalidateCachedResources();
            }, new SmartLogger.ErrorHandlingOptions
            {
                Source = $"{Constants.LOG_PREFIX}.ApplyQualitySettings",
                ErrorMessage = "Failed to apply quality settings"
            });
        }

        /// <summary>
        /// Invalidates cached resources to force regeneration.
        /// </summary>
        private void InvalidateCachedResources()
        {
            SmartLogger.Safe(() =>
            {
                _cachedBackground?.Dispose();
                _cachedBackground = null;
            }, new SmartLogger.ErrorHandlingOptions
            {
                Source = $"{Constants.LOG_PREFIX}.InvalidateCachedResources",
                ErrorMessage = "Failed to invalidate cached resources"
            });
        }
        #endregion

        #region Rendering
        /// <summary>
        /// Renders the Voronoi diagram visualization on the canvas using spectrum data.
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
            // Validate rendering parameters
            if (!ValidateRenderParameters(canvas, spectrum, info, paint))
            {
                drawPerformanceInfo?.Invoke(canvas!, info);
                return;
            }

            // Quick reject if canvas area is not visible
            if (canvas!.QuickReject(new SKRect(0, 0, info.Width, info.Height)))
            {
                drawPerformanceInfo?.Invoke(canvas, info);
                return;
            }

            SmartLogger.Safe(() =>
            {
                bool semaphoreAcquired = false;
                try
                {
                    semaphoreAcquired = _spectrumSemaphore.Wait(0);

                    if (semaphoreAcquired)
                    {
                        int freqBands = Math.Min(spectrum!.Length, Constants.DEFAULT_POINT_COUNT);

                        // Process spectrum and update points in parallel if quality permits
                        if (_quality == RenderQuality.High)
                        {
                            Task.Run(() => ProcessSpectrum(spectrum, freqBands));
                        }
                        else
                        {
                            ProcessSpectrum(spectrum, freqBands);
                        }

                        UpdateVoronoiPoints(info.Width, info.Height);

                        // Update grid cache if dimensions changed
                        if (_lastWidth != info.Width || _lastHeight != info.Height)
                        {
                            _gridCols = (int)Math.Ceiling((float)info.Width / Constants.GRID_CELL_SIZE);
                            _gridRows = (int)Math.Ceiling((float)info.Height / Constants.GRID_CELL_SIZE);
                            _nearestPointGrid = new int[_gridCols, _gridRows];
                            _lastWidth = info.Width;
                            _lastHeight = info.Height;

                            // Invalidate cached background
                            InvalidateCachedResources();
                        }

                        // Pre-calculate nearest points for grid cells
                        if (_quality != RenderQuality.Low)
                        {
                            PrecalculateNearestPoints();
                        }
                    }

                    RenderVoronoiDiagram(canvas!, info, paint!);
                }
                finally
                {
                    if (semaphoreAcquired)
                        _spectrumSemaphore.Release();
                }
            }, new SmartLogger.ErrorHandlingOptions
            {
                Source = $"{Constants.LOG_PREFIX}.Render",
                ErrorMessage = "Error during rendering"
            });

            // Draw performance info
            drawPerformanceInfo?.Invoke(canvas!, info);
        }

        /// <summary>
        /// Validates all render parameters before processing.
        /// </summary>
        private bool ValidateRenderParameters(SKCanvas? canvas, float[]? spectrum, SKImageInfo info, SKPaint? paint)
        {
            if (_disposed)
            {
                SmartLogger.Log(LogLevel.Error, Constants.LOG_PREFIX, "Renderer is disposed");
                return false;
            }

            if (canvas == null || spectrum == null || paint == null)
            {
                SmartLogger.Log(LogLevel.Error, Constants.LOG_PREFIX, "Invalid render parameters: null values");
                return false;
            }

            if (info.Width <= 0 || info.Height <= 0)
            {
                SmartLogger.Log(LogLevel.Error, Constants.LOG_PREFIX, $"Invalid image dimensions: {info.Width}x{info.Height}");
                return false;
            }

            if (spectrum.Length < 2)
            {
                SmartLogger.Log(LogLevel.Warning, Constants.LOG_PREFIX, "Spectrum must have at least 2 elements");
                return false;
            }

            return true;
        }
        #endregion

        #region Rendering Implementation
        /// <summary>
        /// Renders the complete Voronoi diagram.
        /// </summary>
        private void RenderVoronoiDiagram(SKCanvas canvas, SKImageInfo info, SKPaint basePaint)
        {
            SmartLogger.Safe(() =>
            {
                if (_voronoiPoints == null || _voronoiPoints.Count == 0 || _cellPaint == null || _borderPaint == null)
                    return;

                // Use base paint color for borders
                _borderPaint.Color = basePaint.Color.WithAlpha(Constants.BORDER_ALPHA);

                // Draw cells using grid optimization
                DrawVoronoiCells(canvas, info, basePaint);

                // Draw borders only if quality permits
                if (_quality != RenderQuality.Low && _useAdvancedEffects)
                {
                    DrawVoronoiBorders(canvas, info);
                }

                // Draw points
                DrawVoronoiPoints(canvas, basePaint);
            }, new SmartLogger.ErrorHandlingOptions
            {
                Source = $"{Constants.LOG_PREFIX}.RenderVoronoiDiagram",
                ErrorMessage = "Error rendering Voronoi diagram"
            });
        }

        /// <summary>
        /// Draws the Voronoi cells.
        /// </summary>
        private void DrawVoronoiCells(SKCanvas canvas, SKImageInfo info, SKPaint basePaint)
        {
            SmartLogger.Safe(() =>
            {
                // Quick reject if canvas is clipped out
                if (!canvas.QuickReject(new SKRect(0, 0, info.Width, info.Height)))
                {
                    // Use hardware-accelerated rendering if available and enabled
                    if (_useHardwareAcceleration && canvas.TotalMatrix.IsIdentity)
                    {
                        using var surface = SKSurface.Create(info);
                        if (surface != null)
                        {
                            var surfaceCanvas = surface.Canvas;
                            DrawCellsToCanvas(surfaceCanvas, info, basePaint);

                            using var image = surface.Snapshot();
                            canvas.DrawImage(image, 0, 0);
                            return;
                        }
                    }

                    // Fallback to direct drawing
                    DrawCellsToCanvas(canvas, info, basePaint);
                }
            }, new SmartLogger.ErrorHandlingOptions
            {
                Source = $"{Constants.LOG_PREFIX}.DrawVoronoiCells",
                ErrorMessage = "Error drawing Voronoi cells"
            });
        }

        /// <summary>
        /// Draws cells to the specified canvas.
        /// </summary>
        private void DrawCellsToCanvas(SKCanvas canvas, SKImageInfo info, SKPaint basePaint)
        {
            SmartLogger.Safe(() =>
            {
                // Use pre-calculated grid for better performance
                if (_quality != RenderQuality.Low && _nearestPointGrid != null)
                {
                    for (int row = 0; row < _gridRows; row++)
                    {
                        for (int col = 0; col < _gridCols; col++)
                        {
                            float cellX = col * Constants.GRID_CELL_SIZE;
                            float cellY = row * Constants.GRID_CELL_SIZE;

                            int nearestIndex = _nearestPointGrid[col, row];
                            if (nearestIndex >= 0 && nearestIndex < _voronoiPoints!.Count)
                            {
                                var point = _voronoiPoints[nearestIndex];

                                // Get color with frequency-based variation
                                byte r = (byte)(basePaint.Color.Red + (point.FrequencyIndex * Constants.RED_VARIATION) % Constants.MAX_COLOR_VARIATION);
                                byte g = (byte)(basePaint.Color.Green + (point.FrequencyIndex * Constants.GREEN_VARIATION) % Constants.MAX_COLOR_VARIATION);
                                byte b = (byte)(basePaint.Color.Blue + (point.FrequencyIndex * Constants.BLUE_VARIATION) % Constants.MAX_COLOR_VARIATION);
                                byte a = (byte)Math.Clamp(Constants.MIN_CELL_ALPHA + (int)(point.Size * Constants.ALPHA_MULTIPLIER), 0, 255);

                                _cellPaint!.Color = new SKColor(r, g, b, a);

                                // Draw cell rectangle
                                canvas.DrawRect(
                                    cellX,
                                    cellY,
                                    Math.Min(Constants.GRID_CELL_SIZE, info.Width - cellX),
                                    Math.Min(Constants.GRID_CELL_SIZE, info.Height - cellY),
                                    _cellPaint
                                );
                            }
                        }
                    }
                }
                else
                {
                    // Fallback for low quality or when grid is not available
                    for (int row = 0; row < _gridRows; row++)
                    {
                        for (int col = 0; col < _gridCols; col++)
                        {
                            float cellX = col * Constants.GRID_CELL_SIZE;
                            float cellY = row * Constants.GRID_CELL_SIZE;

                            int nearestIndex = FindNearestPointIndex(cellX, cellY);
                            if (nearestIndex >= 0 && nearestIndex < _voronoiPoints!.Count)
                            {
                                var point = _voronoiPoints[nearestIndex];

                                byte r = (byte)(basePaint.Color.Red + (point.FrequencyIndex * Constants.RED_VARIATION) % Constants.MAX_COLOR_VARIATION);
                                byte g = (byte)(basePaint.Color.Green + (point.FrequencyIndex * Constants.GREEN_VARIATION) % Constants.MAX_COLOR_VARIATION);
                                byte b = (byte)(basePaint.Color.Blue + (point.FrequencyIndex * Constants.BLUE_VARIATION) % Constants.MAX_COLOR_VARIATION);
                                byte a = (byte)Math.Clamp(Constants.MIN_CELL_ALPHA + (int)(point.Size * Constants.ALPHA_MULTIPLIER), 0, 255);

                                _cellPaint!.Color = new SKColor(r, g, b, a);

                                canvas.DrawRect(
                                    cellX,
                                    cellY,
                                    Math.Min(Constants.GRID_CELL_SIZE, info.Width - cellX),
                                    Math.Min(Constants.GRID_CELL_SIZE, info.Height - cellY),
                                    _cellPaint
                                );
                            }
                        }
                    }
                }
            }, new SmartLogger.ErrorHandlingOptions
            {
                Source = $"{Constants.LOG_PREFIX}.DrawCellsToCanvas",
                ErrorMessage = "Error drawing cells to canvas"
            });
        }

        /// <summary>
        /// Draws Voronoi border lines.
        /// </summary>
        private void DrawVoronoiBorders(SKCanvas canvas, SKImageInfo info)
        {
            SmartLogger.Safe(() =>
            {
                float maxDistance = Math.Max(info.Width, info.Height) * Constants.MAX_DISTANCE_FACTOR;

                // Batch borders for better performance
                using var path = new SKPath();

                for (int i = 0; i < _voronoiPoints!.Count; i++)
                {
                    var p1 = _voronoiPoints[i];
                    for (int j = i + 1; j < _voronoiPoints.Count; j++)
                    {
                        var p2 = _voronoiPoints[j];

                        // Calculate distance using SIMD
                        Vector2 v1 = new Vector2(p1.X, p1.Y);
                        Vector2 v2 = new Vector2(p2.X, p2.Y);
                        float distance = Vector2.Distance(v1, v2);

                        // Draw border only if points are close enough
                        if (distance < maxDistance)
                        {
                            // Find midpoint
                            float midX = (p1.X + p2.X) / 2;
                            float midY = (p1.Y + p2.Y) / 2;

                            // Add line to path
                            path.MoveTo(p1.X, p1.Y);
                            path.LineTo(midX, midY);
                        }
                    }
                }

                // Draw all borders at once
                canvas.DrawPath(path, _borderPaint!);
            }, new SmartLogger.ErrorHandlingOptions
            {
                Source = $"{Constants.LOG_PREFIX}.DrawVoronoiBorders",
                ErrorMessage = "Error drawing Voronoi borders"
            });
        }

        /// <summary>
        /// Draws the Voronoi points.
        /// </summary>
        private void DrawVoronoiPoints(SKCanvas canvas, SKPaint basePaint)
        {
            SmartLogger.Safe(() =>
            {
                // Create a single paint object for all points
                _cellPaint!.Color = basePaint.Color.WithAlpha(200);

                // Draw all points at once if possible
                if (_quality == RenderQuality.High && _useAdvancedEffects)
                {
                    using var pointsPath = new SKPath();

                    foreach (var point in _voronoiPoints!)
                    {
                        pointsPath.AddCircle(point.X, point.Y, point.Size);
                    }

                    canvas.DrawPath(pointsPath, _cellPaint);
                }
                else
                {
                    // Fallback to individual circles for medium/low quality
                    foreach (var point in _voronoiPoints!)
                    {
                        canvas.DrawCircle(point.X, point.Y, point.Size, _cellPaint);
                    }
                }
            }, new SmartLogger.ErrorHandlingOptions
            {
                Source = $"{Constants.LOG_PREFIX}.DrawVoronoiPoints",
                ErrorMessage = "Error drawing Voronoi points"
            });
        }
        #endregion

        #region Spectrum Processing
        /// <summary>
        /// Processes spectrum data for visualization.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private void ProcessSpectrum(float[] spectrum, int freqBands)
        {
            SmartLogger.Safe(() =>
            {
                if (_voronoiPoints == null || _voronoiPoints.Count == 0) return;

                if (_processedSpectrum == null || _processedSpectrum.Length < freqBands)
                    _processedSpectrum = new float[freqBands];

                float spectrumStep = spectrum.Length / (float)freqBands;

                // Process spectrum data using SIMD where possible
                for (int i = 0; i < freqBands; i++)
                {
                    int startBin = (int)(i * spectrumStep);
                    int endBin = (int)((i + 1) * spectrumStep);
                    endBin = Math.Min(endBin, spectrum.Length);

                    float sum = 0;

                    // Use SIMD for large chunks if available
                    if (Vector.IsHardwareAccelerated && endBin - startBin >= Vector<float>.Count)
                    {
                        int vectorizedEnd = startBin + ((endBin - startBin) / Vector<float>.Count) * Vector<float>.Count;

                        Vector<float> accum = Vector<float>.Zero;
                        for (int j = startBin; j < vectorizedEnd; j += Vector<float>.Count)
                        {
                            Vector<float> chunk = new Vector<float>(spectrum, j);
                            accum += chunk;
                        }

                        // Sum vector elements
                        for (int j = 0; j < Vector<float>.Count; j++)
                        {
                            sum += accum[j];
                        }

                        // Process remaining elements
                        for (int j = vectorizedEnd; j < endBin; j++)
                        {
                            sum += spectrum[j];
                        }
                    }
                    else
                    {
                        // Fallback for small chunks
                        for (int j = startBin; j < endBin; j++)
                        {
                            sum += spectrum[j];
                        }
                    }

                    // Normalize and amplify
                    float avg = sum / (endBin - startBin);
                    _processedSpectrum[i] = avg * Constants.SPECTRUM_AMPLIFICATION;
                    _processedSpectrum[i] = Math.Clamp(_processedSpectrum[i], 0, 1);
                }

                // Update point parameters based on spectrum
                for (int i = 0; i < _voronoiPoints.Count; i++)
                {
                    var point = _voronoiPoints[i];
                    int freqIndex = point.FrequencyIndex;

                    if (freqIndex < _processedSpectrum.Length)
                    {
                        float intensity = _processedSpectrum[freqIndex];
                        float targetSize = Constants.MIN_POINT_SIZE + (Constants.MAX_POINT_SIZE - Constants.MIN_POINT_SIZE) * intensity;

                        // Smoothly change size
                        point.Size += (targetSize - point.Size) * Constants.SMOOTHING_FACTOR;

                        // Increase speed with strong signal
                        point.VelocityX *= 1 + intensity * Constants.VELOCITY_BOOST_FACTOR;
                        point.VelocityY *= 1 + intensity * Constants.VELOCITY_BOOST_FACTOR;

                        _voronoiPoints[i] = point;
                    }
                }
            }, new SmartLogger.ErrorHandlingOptions
            {
                Source = $"{Constants.LOG_PREFIX}.ProcessSpectrum",
                ErrorMessage = "Error processing spectrum data"
            });
        }
        #endregion

        #region Helper Methods
        /// <summary>
        /// Initializes Voronoi points.
        /// </summary>
        private void InitializePoints(int count)
        {
            SmartLogger.Safe(() =>
            {
                _voronoiPoints = new List<VoronoiPoint>(count);

                for (int i = 0; i < count; i++)
                {
                    _voronoiPoints.Add(new VoronoiPoint
                    {
                        X = _random.Next(100, 700),
                        Y = _random.Next(100, 500),
                        VelocityX = Constants.MIN_MOVE_SPEED + (float)_random.NextDouble() * (Constants.MAX_MOVE_SPEED - Constants.MIN_MOVE_SPEED),
                        VelocityY = Constants.MIN_MOVE_SPEED + (float)_random.NextDouble() * (Constants.MAX_MOVE_SPEED - Constants.MIN_MOVE_SPEED),
                        Size = Constants.MIN_POINT_SIZE,
                        FrequencyIndex = i % Constants.DEFAULT_POINT_COUNT
                    });
                }

                // Invalidate caches
                InvalidateCachedResources();
            }, new SmartLogger.ErrorHandlingOptions
            {
                Source = $"{Constants.LOG_PREFIX}.InitializePoints",
                ErrorMessage = "Failed to initialize points"
            });
        }

        /// <summary>
        /// Updates Voronoi point positions.
        /// </summary>
        private void UpdateVoronoiPoints(float width, float height)
        {
            SmartLogger.Safe(() =>
            {
                if (_voronoiPoints == null) return;

                _timeAccumulator += Constants.TIME_STEP;

                // Update all points
                for (int i = 0; i < _voronoiPoints.Count; i++)
                {
                    var point = _voronoiPoints[i];

                    // Update position over time
                    point.X += point.VelocityX * (float)Math.Sin(_timeAccumulator + i);
                    point.Y += point.VelocityY * (float)Math.Cos(_timeAccumulator * 0.7f + i);

                    // Bounce from boundaries
                    if (point.X < 0)
                    {
                        point.X = 0;
                        point.VelocityX = -point.VelocityX;
                    }
                    else if (point.X > width)
                    {
                        point.X = width;
                        point.VelocityX = -point.VelocityX;
                    }

                    if (point.Y < 0)
                    {
                        point.Y = 0;
                        point.VelocityY = -point.VelocityY;
                    }
                    else if (point.Y > height)
                    {
                        point.Y = height;
                        point.VelocityY = -point.VelocityY;
                    }

                    _voronoiPoints[i] = point;
                }
            }, new SmartLogger.ErrorHandlingOptions
            {
                Source = $"{Constants.LOG_PREFIX}.UpdateVoronoiPoints",
                ErrorMessage = "Error updating Voronoi points"
            });
        }

        /// <summary>
        /// Pre-calculates nearest points for grid optimization.
        /// </summary>
        private void PrecalculateNearestPoints()
        {
            SmartLogger.Safe(() =>
            {
                if (_voronoiPoints == null || _nearestPointGrid == null) return;

                // Calculate nearest point for each grid cell
                Parallel.For(0, _gridRows, row =>
                {
                    for (int col = 0; col < _gridCols; col++)
                    {
                        float cellX = col * Constants.GRID_CELL_SIZE;
                        float cellY = row * Constants.GRID_CELL_SIZE;

                        _nearestPointGrid[col, row] = FindNearestPointIndex(cellX, cellY);
                    }
                });
            }, new SmartLogger.ErrorHandlingOptions
            {
                Source = $"{Constants.LOG_PREFIX}.PrecalculateNearestPoints",
                ErrorMessage = "Error pre-calculating nearest points"
            });
        }

        /// <summary>
        /// Finds the index of the nearest Voronoi point to a position.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int FindNearestPointIndex(float x, float y)
        {
            if (_voronoiPoints == null || _voronoiPoints.Count == 0)
                return -1;

            int nearest = 0;
            float minDistance = float.MaxValue;

            // Use Vector2 for SIMD acceleration
            Vector2 targetPos = new Vector2(x, y);

            for (int i = 0; i < _voronoiPoints.Count; i++)
            {
                var point = _voronoiPoints[i];
                Vector2 pointPos = new Vector2(point.X, point.Y);

                float distance = Vector2.DistanceSquared(targetPos, pointPos);

                if (distance < minDistance)
                {
                    minDistance = distance;
                    nearest = i;
                }
            }

            return nearest;
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
                SmartLogger.Safe(() =>
                {
                    // Dispose synchronization primitives
                    _spectrumSemaphore?.Dispose();

                    // Dispose rendering resources
                    _cellPaint?.Dispose();
                    _borderPaint?.Dispose();
                    _cachedBackground?.Dispose();

                    // Clear references
                    _cellPaint = null;
                    _borderPaint = null;
                    _voronoiPoints = null;
                    _processedSpectrum = null;
                    _cachedBackground = null;
                    _nearestPointGrid = null;

                    // Call base disposal
                    base.Dispose();

                    SmartLogger.Log(LogLevel.Debug, Constants.LOG_PREFIX, "Disposed");
                }, new SmartLogger.ErrorHandlingOptions
                {
                    Source = $"{Constants.LOG_PREFIX}.Dispose",
                    ErrorMessage = "Error during disposal"
                });

                _disposed = true;
            }
        }
        #endregion
    }
}