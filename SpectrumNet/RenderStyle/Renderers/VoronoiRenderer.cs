#nullable enable

namespace SpectrumNet
{
    public sealed class VoronoiRenderer : ISpectrumRenderer, IDisposable
    {
        #region Constants
        private static class Constants
        {
            // Logging
            public const string LogPrefix = "[VoronoiRenderer] ";

            // Points configuration
            public const int DefaultPointCount = 25;      // Default number of points
            public const int OverlayPointCount = 15;      // Number of points in overlay mode
            public const float MinPointSize = 3f;         // Minimum point size
            public const float MaxPointSize = 15f;        // Maximum point size
            public const float MinMoveSpeed = 0.3f;       // Minimum point movement speed
            public const float MaxMoveSpeed = 2.0f;       // Maximum point movement speed

            // Grid for optimization
            public const int GridCellSize = 20;           // Size of grid cells for rendering
            public const float MaxDistanceFactor = 0.33f; // Max distance between points as screen size factor

            // Rendering properties
            public const float SmoothingFactor = 0.2f;    // Smoothing factor for animations
            public const float BorderWidth = 1.0f;        // Width of cell borders
            public const byte BorderAlpha = 180;          // Alpha of cell borders
            public const float TimeStep = 0.016f;         // Animation time step (~60fps)
            public const float SpectrumAmplification = 3f;// Spectrum amplification factor

            // Color variations
            public const int RedVariation = 3;            // Red color variation multiplier
            public const int GreenVariation = 7;          // Green color variation multiplier
            public const int BlueVariation = 11;          // Blue color variation multiplier
            public const int MaxColorVariation = 50;      // Maximum color variation
            public const int MinCellAlpha = 55;           // Minimum cell alpha
            public const int AlphaMultiplier = 10;        // Alpha multiplier based on point size

            // Physics
            public const float VelocityBoostFactor = 0.3f;// Velocity boost factor based on spectrum
        }
        #endregion

        #region Fields
        private static VoronoiRenderer? _instance;
        private bool _isInitialized;
        private bool _isOverlayActive;
        private volatile bool _disposed;
        private readonly SemaphoreSlim _spectrumSemaphore = new(1, 1);

        private RenderQuality _quality = RenderQuality.Medium;
        private bool _useAntiAlias = true;
        private SKFilterQuality _filterQuality = SKFilterQuality.Medium;
        private bool _useAdvancedEffects = true;
        private bool _useHardwareAcceleration = true;

        private float[]? _processedSpectrum;
        private List<VoronoiPoint>? _voronoiPoints;
        private SKPaint? _cellPaint;
        private SKPaint? _borderPaint;
        private Random _random = new();
        private float _timeAccumulator;

        // Caching
        private SKPicture? _cachedBackground;
        private int _lastWidth;
        private int _lastHeight;
        private int _gridCols;
        private int _gridRows;
        private int[,]? _nearestPointGrid;
        #endregion

        #region Constructor and Initialization
        private VoronoiRenderer() { }

        public static VoronoiRenderer GetInstance() => _instance ??= new VoronoiRenderer();

        public void Initialize()
        {
            if (_isInitialized) return;

            _cellPaint = new SKPaint
            {
                IsAntialias = _useAntiAlias,
                Style = SKPaintStyle.Fill,
                FilterQuality = _filterQuality
            };

            _borderPaint = new SKPaint
            {
                IsAntialias = _useAntiAlias,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = Constants.BorderWidth,
                Color = SKColors.White.WithAlpha(Constants.BorderAlpha),
                FilterQuality = _filterQuality
            };

            int pointCount = _isOverlayActive ? Constants.OverlayPointCount : Constants.DefaultPointCount;
            InitializePoints(pointCount);

            _isInitialized = true;
            SmartLogger.Log(LogLevel.Debug, Constants.LogPrefix, "Initialized");
        }

        public void Configure(bool isOverlayActive, RenderQuality quality = RenderQuality.Medium)
        {
            // Update overlay state if changed
            if (_isOverlayActive != isOverlayActive)
            {
                _isOverlayActive = isOverlayActive;
                int pointCount = _isOverlayActive ? Constants.OverlayPointCount : Constants.DefaultPointCount;
                InitializePoints(pointCount);
            }

            // Set quality
            Quality = quality;
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

        private void ApplyQualitySettings()
        {
            switch (_quality)
            {
                case RenderQuality.Low:
                    _useAntiAlias = false;
                    _filterQuality = SKFilterQuality.Low;
                    _useAdvancedEffects = false;
                    _useHardwareAcceleration = true;
                    break;

                case RenderQuality.Medium:
                    _useAntiAlias = true;
                    _filterQuality = SKFilterQuality.Medium;
                    _useAdvancedEffects = true;
                    _useHardwareAcceleration = true;
                    break;

                case RenderQuality.High:
                    _useAntiAlias = true;
                    _filterQuality = SKFilterQuality.High;
                    _useAdvancedEffects = true;
                    _useHardwareAcceleration = true;
                    break;
            }

            // Update existing paint objects
            if (_cellPaint != null)
            {
                _cellPaint.IsAntialias = _useAntiAlias;
                _cellPaint.FilterQuality = _filterQuality;
            }

            if (_borderPaint != null)
            {
                _borderPaint.IsAntialias = _useAntiAlias;
                _borderPaint.FilterQuality = _filterQuality;
            }

            // Invalidate cached resources
            _cachedBackground?.Dispose();
            _cachedBackground = null;
        }
        #endregion

        #region Rendering
        public void Render(
            SKCanvas? canvas,
            float[]? spectrum,
            SKImageInfo info,
            float barWidth,
            float barSpacing,
            int barCount,
            SKPaint? paint,
            Action<SKCanvas, SKImageInfo>? drawPerformanceInfo)
        {
            if (!ValidateRenderParams(canvas, spectrum, info, paint, drawPerformanceInfo))
                return;

            bool semaphoreAcquired = false;
            try
            {
                semaphoreAcquired = _spectrumSemaphore.Wait(0);

                if (semaphoreAcquired)
                {
                    int freqBands = Math.Min(spectrum!.Length, Constants.DefaultPointCount);

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
                        _gridCols = (int)Math.Ceiling((float)info.Width / Constants.GridCellSize);
                        _gridRows = (int)Math.Ceiling((float)info.Height / Constants.GridCellSize);
                        _nearestPointGrid = new int[_gridCols, _gridRows];
                        _lastWidth = info.Width;
                        _lastHeight = info.Height;

                        // Invalidate cached background
                        _cachedBackground?.Dispose();
                        _cachedBackground = null;
                    }

                    // Pre-calculate nearest points for grid cells
                    if (_quality != RenderQuality.Low)
                    {
                        PrecalculateNearestPoints();
                    }
                }

                RenderVoronoiDiagram(canvas!, info, paint!);
                drawPerformanceInfo?.Invoke(canvas!, info);
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, Constants.LogPrefix, $"Error in Render: {ex.Message}");
            }
            finally
            {
                if (semaphoreAcquired)
                    _spectrumSemaphore.Release();
            }
        }

        private void RenderVoronoiDiagram(SKCanvas canvas, SKImageInfo info, SKPaint basePaint)
        {
            if (_voronoiPoints == null || _voronoiPoints.Count == 0 || _cellPaint == null || _borderPaint == null)
                return;

            // Use base paint color for borders
            _borderPaint.Color = basePaint.Color.WithAlpha(Constants.BorderAlpha);

            // Draw cells using grid optimization
            DrawVoronoiCells(canvas, info, basePaint);

            // Draw borders only if quality permits
            if (_quality != RenderQuality.Low && _useAdvancedEffects)
            {
                DrawVoronoiBorders(canvas, info);
            }

            // Draw points
            DrawVoronoiPoints(canvas, basePaint);
        }

        private void DrawVoronoiCells(SKCanvas canvas, SKImageInfo info, SKPaint basePaint)
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
        }

        private void DrawCellsToCanvas(SKCanvas canvas, SKImageInfo info, SKPaint basePaint)
        {
            // Use pre-calculated grid for better performance
            if (_quality != RenderQuality.Low && _nearestPointGrid != null)
            {
                for (int row = 0; row < _gridRows; row++)
                {
                    for (int col = 0; col < _gridCols; col++)
                    {
                        float cellX = col * Constants.GridCellSize;
                        float cellY = row * Constants.GridCellSize;

                        int nearestIndex = _nearestPointGrid[col, row];
                        if (nearestIndex >= 0 && nearestIndex < _voronoiPoints!.Count)
                        {
                            var point = _voronoiPoints[nearestIndex];

                            // Get color with frequency-based variation
                            byte r = (byte)(basePaint.Color.Red + (point.FrequencyIndex * Constants.RedVariation) % Constants.MaxColorVariation);
                            byte g = (byte)(basePaint.Color.Green + (point.FrequencyIndex * Constants.GreenVariation) % Constants.MaxColorVariation);
                            byte b = (byte)(basePaint.Color.Blue + (point.FrequencyIndex * Constants.BlueVariation) % Constants.MaxColorVariation);
                            byte a = (byte)Math.Clamp(Constants.MinCellAlpha + (int)(point.Size * Constants.AlphaMultiplier), 0, 255);

                            _cellPaint!.Color = new SKColor(r, g, b, a);

                            // Draw cell rectangle
                            canvas.DrawRect(
                                cellX,
                                cellY,
                                Math.Min(Constants.GridCellSize, info.Width - cellX),
                                Math.Min(Constants.GridCellSize, info.Height - cellY),
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
                        float cellX = col * Constants.GridCellSize;
                        float cellY = row * Constants.GridCellSize;

                        int nearestIndex = FindNearestPointIndex(cellX, cellY);
                        if (nearestIndex >= 0 && nearestIndex < _voronoiPoints!.Count)
                        {
                            var point = _voronoiPoints[nearestIndex];

                            byte r = (byte)(basePaint.Color.Red + (point.FrequencyIndex * Constants.RedVariation) % Constants.MaxColorVariation);
                            byte g = (byte)(basePaint.Color.Green + (point.FrequencyIndex * Constants.GreenVariation) % Constants.MaxColorVariation);
                            byte b = (byte)(basePaint.Color.Blue + (point.FrequencyIndex * Constants.BlueVariation) % Constants.MaxColorVariation);
                            byte a = (byte)Math.Clamp(Constants.MinCellAlpha + (int)(point.Size * Constants.AlphaMultiplier), 0, 255);

                            _cellPaint!.Color = new SKColor(r, g, b, a);

                            canvas.DrawRect(
                                cellX,
                                cellY,
                                Math.Min(Constants.GridCellSize, info.Width - cellX),
                                Math.Min(Constants.GridCellSize, info.Height - cellY),
                                _cellPaint
                            );
                        }
                    }
                }
            }
        }

        private void DrawVoronoiBorders(SKCanvas canvas, SKImageInfo info)
        {
            float maxDistance = Math.Max(info.Width, info.Height) * Constants.MaxDistanceFactor;

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
        }

        private void DrawVoronoiPoints(SKCanvas canvas, SKPaint basePaint)
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
        }
        #endregion

        #region Spectrum Processing
        private void ProcessSpectrum(float[] spectrum, int freqBands)
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
                _processedSpectrum[i] = avg * Constants.SpectrumAmplification;
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
                    float targetSize = Constants.MinPointSize + (Constants.MaxPointSize - Constants.MinPointSize) * intensity;

                    // Smoothly change size
                    point.Size += (targetSize - point.Size) * Constants.SmoothingFactor;

                    // Increase speed with strong signal
                    point.VelocityX *= 1 + intensity * Constants.VelocityBoostFactor;
                    point.VelocityY *= 1 + intensity * Constants.VelocityBoostFactor;

                    _voronoiPoints[i] = point;
                }
            }
        }
        #endregion

        #region Helper Methods
        private void InitializePoints(int count)
        {
            _voronoiPoints = new List<VoronoiPoint>(count);

            for (int i = 0; i < count; i++)
            {
                _voronoiPoints.Add(new VoronoiPoint
                {
                    X = _random.Next(100, 700),
                    Y = _random.Next(100, 500),
                    VelocityX = Constants.MinMoveSpeed + (float)_random.NextDouble() * (Constants.MaxMoveSpeed - Constants.MinMoveSpeed),
                    VelocityY = Constants.MinMoveSpeed + (float)_random.NextDouble() * (Constants.MaxMoveSpeed - Constants.MinMoveSpeed),
                    Size = Constants.MinPointSize,
                    FrequencyIndex = i % Constants.DefaultPointCount
                });
            }

            // Invalidate caches
            _cachedBackground?.Dispose();
            _cachedBackground = null;
        }

        private void UpdateVoronoiPoints(float width, float height)
        {
            if (_voronoiPoints == null) return;

            _timeAccumulator += Constants.TimeStep;

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
        }

        private void PrecalculateNearestPoints()
        {
            if (_voronoiPoints == null || _nearestPointGrid == null) return;

            // Calculate nearest point for each grid cell
            Parallel.For(0, _gridRows, row =>
            {
                for (int col = 0; col < _gridCols; col++)
                {
                    float cellX = col * Constants.GridCellSize;
                    float cellY = row * Constants.GridCellSize;

                    _nearestPointGrid[col, row] = FindNearestPointIndex(cellX, cellY);
                }
            });
        }

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

        private bool ValidateRenderParams(
            SKCanvas? canvas,
            float[]? spectrum,
            SKImageInfo info,
            SKPaint? paint,
            Action<SKCanvas, SKImageInfo>? drawPerformanceInfo)
        {
            if (_disposed)
                return false;

            if (!_isInitialized)
            {
                SmartLogger.Log(LogLevel.Error, Constants.LogPrefix, "Not initialized before rendering");
                return false;
            }

            if (canvas == null ||
                spectrum == null || spectrum.Length < 2 ||
                paint == null ||
                drawPerformanceInfo == null ||
                info.Width <= 0 || info.Height <= 0)
            {
                SmartLogger.Log(LogLevel.Error, Constants.LogPrefix, "Invalid render parameters");
                return false;
            }

            return true;
        }
        #endregion

        #region Structures
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

        #region Disposal
        public void Dispose()
        {
            if (_disposed) return;

            _spectrumSemaphore.Dispose();
            _cellPaint?.Dispose();
            _borderPaint?.Dispose();
            _cachedBackground?.Dispose();

            _cellPaint = null;
            _borderPaint = null;
            _voronoiPoints = null;
            _processedSpectrum = null;
            _cachedBackground = null;
            _nearestPointGrid = null;

            _disposed = true;
            _isInitialized = false;
            SmartLogger.Log(LogLevel.Debug, Constants.LogPrefix, "Disposed");
        }
        #endregion
    }
}