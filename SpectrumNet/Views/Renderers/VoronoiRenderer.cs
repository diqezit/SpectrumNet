#nullable enable

using static System.MathF;
using static SpectrumNet.Views.Renderers.VoronoiRenderer.Constants;
using static SpectrumNet.Views.Renderers.VoronoiRenderer.Constants.Quality;

namespace SpectrumNet.Views.Renderers;

public sealed class VoronoiRenderer : EffectSpectrumRenderer
{
    private static readonly Lazy<VoronoiRenderer> _instance = 
        new(() => new VoronoiRenderer());

    private const string LOG_PREFIX = nameof(VoronoiRenderer);

    private VoronoiRenderer() { }

    public static VoronoiRenderer GetInstance() => _instance.Value;

    public record Constants
    {
        public const int
            DEFAULT_POINT_COUNT = 25,
            OVERLAY_POINT_COUNT = 15,
            GRID_CELL_SIZE = 20,
            MIN_CELL_ALPHA = 55,
            ALPHA_MULTIPLIER = 10;

        public const float
            MIN_POINT_SIZE = 3f,
            MAX_POINT_SIZE = 15f,
            MIN_MOVE_SPEED = 0.3f,
            MAX_MOVE_SPEED = 2.0f,
            MAX_DISTANCE_FACTOR = 0.33f,
            SMOOTHING_FACTOR = 0.2f,
            BORDER_WIDTH = 1.0f,
            TIME_STEP = 0.016f,
            SPECTRUM_AMPLIFICATION = 3f,
            VELOCITY_BOOST_FACTOR = 0.3f;

        public const byte
            BORDER_ALPHA = 180;

        public static class Quality
        {
            public const bool
                LOW_USE_ADVANCED_EFFECTS = false,
                MEDIUM_USE_ADVANCED_EFFECTS = true,
                HIGH_USE_ADVANCED_EFFECTS = true;

            public const bool
                LOW_USE_ANTI_ALIAS = false,
                MEDIUM_USE_ANTI_ALIAS = true,
                HIGH_USE_ANTI_ALIAS = true;

            public const bool
                LOW_USE_BATCHED_POINTS = false,
                MEDIUM_USE_BATCHED_POINTS = false,
                HIGH_USE_BATCHED_POINTS = true;
        }
    }

    // State and generator resources
    private readonly Random _random = new();
    private readonly List<VoronoiPoint> _voronoiPoints = [];
    private float _timeAccumulator;

    // Rendering resources
    private SKPaint? _cellPaint;
    private SKPaint? _borderPaint;

    // Grid cache
    private int _gridCols;
    private int _gridRows;
    private int[,]? _nearestPointGrid;
    private int _lastWidth;
    private int _lastHeight;

    // Quality settings
    private bool _useBatchedPoints;

    private struct VoronoiPoint
    {
        public float X, Y, VelocityX, VelocityY, Size;
        public int FrequencyIndex;
    }

    protected override void OnInitialize() =>
        _logger.Safe(
            () =>
            {
                base.OnInitialize();
                InitializePaints();
                InitializePoints(GetPointCount());
                _logger.Debug(LOG_PREFIX, "Initialized");
            },
            LOG_PREFIX,
            "Failed during renderer initialization"
        );

    private void InitializePaints() =>
        _logger.Safe(
            HandleInitializePaints,
            LOG_PREFIX,
            "Failed to initialize paints"
        );

    private void HandleInitializePaints()
    {
        _cellPaint = new SKPaint
        {
            IsAntialias = UseAntiAlias,
            Style = SKPaintStyle.Fill
        };

        _borderPaint = new SKPaint
        {
            IsAntialias = UseAntiAlias,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = BORDER_WIDTH,
            Color = SKColors.White.WithAlpha(BORDER_ALPHA)
        };
    }

    private int GetPointCount() =>
        _isOverlayActive
            ? OVERLAY_POINT_COUNT
            : DEFAULT_POINT_COUNT;

    protected override void OnConfigurationChanged() =>
        _logger.Safe(
            () =>
            {
                InitializePoints(GetPointCount());
                _logger.Info(
                    LOG_PREFIX,
                    $"Configuration changed. New Quality: {Quality}, Overlay: {_isOverlayActive}");
            },
            LOG_PREFIX,
            "Failed to handle configuration change"
        );

    protected override void OnQualitySettingsApplied() =>
        _logger.Safe(
            () =>
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

                UpdatePaintQualitySettings();
                InvalidateCachedResources();

                _logger.Debug(LOG_PREFIX,
                    $"Quality settings applied. Quality: {Quality}, " +
                    $"AntiAlias: {UseAntiAlias}, AdvancedEffects: {UseAdvancedEffects}, " +
                    $"BatchedPoints: {_useBatchedPoints}");
            },
            LOG_PREFIX,
            "Failed to apply quality settings"
        );

    private void LowQualitySettings()
    {
        _useAntiAlias = LOW_USE_ANTI_ALIAS;
        _useAdvancedEffects = LOW_USE_ADVANCED_EFFECTS;
        _useBatchedPoints = LOW_USE_BATCHED_POINTS;
    }

    private void MediumQualitySettings()
    {
        _useAntiAlias = MEDIUM_USE_ANTI_ALIAS;
        _useAdvancedEffects = MEDIUM_USE_ADVANCED_EFFECTS;
        _useBatchedPoints = MEDIUM_USE_BATCHED_POINTS;
    }

    private void HighQualitySettings()
    {
        _useAntiAlias = HIGH_USE_ANTI_ALIAS;
        _useAdvancedEffects = HIGH_USE_ADVANCED_EFFECTS;
        _useBatchedPoints = HIGH_USE_BATCHED_POINTS;
    }

    private void UpdatePaintQualitySettings() =>
        _logger.Safe(
            HandleUpdatePaintQualitySettings,
            LOG_PREFIX,
            "Failed to update paint quality settings"
        );

    private void HandleUpdatePaintQualitySettings()
    {
        if (_cellPaint != null)
            _cellPaint.IsAntialias = UseAntiAlias;

        if (_borderPaint != null)
            _borderPaint.IsAntialias = UseAntiAlias;
    }

    protected override void OnInvalidateCachedResources() =>
        _logger.Safe(
            () =>
            {
                base.OnInvalidateCachedResources();
                _nearestPointGrid = null;
                _lastWidth = 0;
                _lastHeight = 0;

                _logger.Debug(LOG_PREFIX, "Cached resources invalidated");
            },
            LOG_PREFIX,
            "Failed to invalidate cached resources"
        );

    protected override void RenderEffect(
        SKCanvas canvas,
        float[] spectrum,
        SKImageInfo info,
        float barWidth,
        float barSpacing,
        int barCount,
        SKPaint paint) =>
        _logger.Safe(
            () =>
            {
                UpdateState(spectrum, info);
                RenderWithOverlay(canvas, () => RenderFrame(canvas, info, paint));
            },
            LOG_PREFIX,
            "Error during rendering"
        );

    private void UpdateState(float[] spectrum, SKImageInfo info) =>
        _logger.Safe(
            () => HandleUpdateState(spectrum, info),
            LOG_PREFIX,
            "Error updating state"
        );

    private void HandleUpdateState(float[] spectrum, SKImageInfo info)
    {
        ProcessSpectrum(
            spectrum,
            Min(spectrum.Length, DEFAULT_POINT_COUNT));

        UpdateVoronoiPoints(info.Width, info.Height);
        UpdateGridIfNeeded(info.Width, info.Height);

        if (Quality != RenderQuality.Low)
        {
            PrecalculateNearestPoints();
        }
    }

    private void RenderFrame(SKCanvas canvas, SKImageInfo info, SKPaint paint) =>
        _logger.Safe(
            () => HandleRenderFrame(canvas, info, paint),
            LOG_PREFIX,
            "Error rendering frame"
        );

    private void HandleRenderFrame(SKCanvas canvas, SKImageInfo info, SKPaint paint) => 
        RenderVoronoiDiagram(canvas, info, paint);

    private void UpdateGridIfNeeded(float width, float height) =>
        _logger.Safe(
            () => HandleUpdateGridIfNeeded(width, height),
            LOG_PREFIX,
            "Failed to update grid"
        );

    private void HandleUpdateGridIfNeeded(float width, float height)
    {
        if (_lastWidth != width || _lastHeight != height)
        {
            _gridCols = (int)Ceiling(width / GRID_CELL_SIZE);
            _gridRows = (int)Ceiling(height / GRID_CELL_SIZE);
            _nearestPointGrid = new int[_gridCols, _gridRows];
            _lastWidth = (int)width;
            _lastHeight = (int)height;
        }
    }

    private void RenderVoronoiDiagram(
        SKCanvas canvas,
        SKImageInfo info,
        SKPaint basePaint) =>
        _logger.Safe(
            () => HandleRenderVoronoiDiagram(canvas, info, basePaint),
            LOG_PREFIX,
            "Error rendering Voronoi diagram"
        );

    private void HandleRenderVoronoiDiagram(
        SKCanvas canvas,
        SKImageInfo info,
        SKPaint basePaint)
    {
        if (_voronoiPoints.Count == 0 || _cellPaint == null || _borderPaint == null)
            return;

        _borderPaint.Color = basePaint.Color.WithAlpha(BORDER_ALPHA);

        DrawVoronoiCells(canvas, info, basePaint);

        if (Quality != RenderQuality.Low && UseAdvancedEffects)
        {
            DrawVoronoiBorders(canvas, info);
        }

        DrawVoronoiPoints(canvas, basePaint);
    }

    private void DrawVoronoiCells(
        SKCanvas canvas,
        SKImageInfo info,
        SKPaint basePaint) =>
        _logger.Safe(
            () => HandleDrawVoronoiCells(canvas, info, basePaint),
            LOG_PREFIX,
            "Error drawing Voronoi cells"
        );

    private void HandleDrawVoronoiCells(
        SKCanvas canvas,
        SKImageInfo info,
        SKPaint basePaint)
    {
        for (int row = 0; row < _gridRows; row++)
        {
            for (int col = 0; col < _gridCols; col++)
            {
                DrawSingleCell(canvas, info, basePaint, row, col);
            }
        }
    }

    private void DrawSingleCell(
        SKCanvas canvas,
        SKImageInfo info,
        SKPaint basePaint,
        int row,
        int col)
    {
        float cellX = col * GRID_CELL_SIZE;
        float cellY = row * GRID_CELL_SIZE;

        int nearestIndex = GetNearestPointIndexForCell(col, row, cellX, cellY);
        if (nearestIndex < 0 || nearestIndex >= _voronoiPoints.Count)
            return;

        var point = _voronoiPoints[nearestIndex];
        UpdateCellPaintColor(basePaint, point);

        DrawCellRect(
            canvas,
            cellX,
            cellY,
            MathF.Min(GRID_CELL_SIZE, info.Width - cellX),
            MathF.Min(GRID_CELL_SIZE, info.Height - cellY));
    }

    private int GetNearestPointIndexForCell(int col, int row, float cellX, float cellY) =>
        Quality != RenderQuality.Low && _nearestPointGrid != null
            ? _nearestPointGrid[col, row]
            : FindNearestPointIndex(cellX, cellY);

    private void UpdateCellPaintColor(SKPaint basePaint, VoronoiPoint point)
    {
        byte r = (byte)((basePaint.Color.Red + point.FrequencyIndex * 3) % 256);
        byte g = (byte)((basePaint.Color.Green + point.FrequencyIndex * 7) % 256);
        byte b = (byte)((basePaint.Color.Blue + point.FrequencyIndex * 11) % 256);
        byte a = (byte)Clamp(
            MIN_CELL_ALPHA + (int)(point.Size * ALPHA_MULTIPLIER),
            0,
            255);

        _cellPaint!.Color = new SKColor(r, g, b, a);
    }

    private void DrawCellRect(
        SKCanvas canvas,
        float x,
        float y,
        float width,
        float height)
    {
        canvas.DrawRect(x, y, width, height, _cellPaint!);
    }

    private void DrawVoronoiBorders(SKCanvas canvas, SKImageInfo info) =>
        _logger.Safe(
            () => HandleDrawVoronoiBorders(canvas, info),
            LOG_PREFIX,
            "Error drawing Voronoi borders"
        );

    private void HandleDrawVoronoiBorders(SKCanvas canvas, SKImageInfo info)
    {
        float maxDistance = Max(info.Width, info.Height) * MAX_DISTANCE_FACTOR;
        using var path = new SKPath();

        for (int i = 0; i < _voronoiPoints.Count; i++)
        {
            DrawBordersForPoint(path, i, maxDistance);
        }

        canvas.DrawPath(path, _borderPaint!);
    }

    private void DrawBordersForPoint(SKPath path, int pointIndex, float maxDistance) =>
        _logger.Safe(
            () => HandleDrawBordersForPoint(path, pointIndex, maxDistance),
            LOG_PREFIX,
            "Error drawing borders for point"
        );

    private void HandleDrawBordersForPoint(SKPath path, int pointIndex, float maxDistance)
    {
        var p1 = _voronoiPoints[pointIndex];

        for (int j = pointIndex + 1; j < _voronoiPoints.Count; j++)
        {
            var p2 = _voronoiPoints[j];
            float distance = CalculateDistance(p1.X, p1.Y, p2.X, p2.Y);

            if (distance < maxDistance)
            {
                float midX = (p1.X + p2.X) / 2;
                float midY = (p1.Y + p2.Y) / 2;

                path.MoveTo(p1.X, p1.Y);
                path.LineTo(midX, midY);
            }
        }
    }

    private static float CalculateDistance(
        float x1,
        float y1,
        float x2,
        float y2)
    {
        float dx = x1 - x2;
        float dy = y1 - y2;
        return Sqrt(dx * dx + dy * dy);
    }

    private void DrawVoronoiPoints(SKCanvas canvas, SKPaint basePaint) =>
        _logger.Safe(
            () => HandleDrawVoronoiPoints(canvas, basePaint),
            LOG_PREFIX,
            "Error drawing Voronoi points"
        );

    private void HandleDrawVoronoiPoints(SKCanvas canvas, SKPaint basePaint)
    {
        _cellPaint!.Color = basePaint.Color.WithAlpha(200);

        if (_useBatchedPoints && UseAdvancedEffects)
        {
            DrawVoronoiPointsWithPath(canvas);
        }
        else
        {
            DrawVoronoiPointsIndividually(canvas);
        }
    }

    private void DrawVoronoiPointsWithPath(SKCanvas canvas) =>
        _logger.Safe(
            () => HandleDrawVoronoiPointsWithPath(canvas),
            LOG_PREFIX,
            "Error drawing points with path"
        );

    private void HandleDrawVoronoiPointsWithPath(SKCanvas canvas)
    {
        using var pointsPath = new SKPath();

        foreach (var point in _voronoiPoints)
        {
            pointsPath.AddCircle(point.X, point.Y, point.Size);
        }

        canvas.DrawPath(pointsPath, _cellPaint!);
    }

    private void DrawVoronoiPointsIndividually(SKCanvas canvas) =>
        _logger.Safe(
            () => HandleDrawVoronoiPointsIndividually(canvas),
            LOG_PREFIX,
            "Error drawing individual points"
        );

    private void HandleDrawVoronoiPointsIndividually(SKCanvas canvas)
    {
        foreach (var point in _voronoiPoints)
        {
            canvas.DrawCircle(point.X, point.Y, point.Size, _cellPaint!);
        }
    }

    private void ProcessSpectrum(float[] spectrum, int freqBands) =>
        _logger.Safe(
            () => HandleProcessSpectrum(spectrum, freqBands),
            LOG_PREFIX,
            "Error processing spectrum"
        );

    private void HandleProcessSpectrum(float[] spectrum, int freqBands)
    {
        if (_voronoiPoints.Count == 0) return;

        EnsureProcessedSpectrumSize(freqBands);
        CalculateProcessedSpectrum(spectrum, freqBands);
        UpdatePointsBasedOnSpectrum();
    }

    private void EnsureProcessedSpectrumSize(int freqBands)
    {
        if (_processedSpectrum == null || _processedSpectrum.Length < freqBands)
            _processedSpectrum = new float[freqBands];
    }

    private void CalculateProcessedSpectrum(float[] spectrum, int freqBands) =>
        _logger.Safe(
            () => HandleCalculateProcessedSpectrum(spectrum, freqBands),
            LOG_PREFIX,
            "Error calculating processed spectrum"
        );

    private void HandleCalculateProcessedSpectrum(float[] spectrum, int freqBands)
    {
        float spectrumStep = spectrum.Length / (float)freqBands;

        for (int i = 0; i < freqBands; i++)
        {
            int startBin = (int)(i * spectrumStep);
            int endBin = Min((int)((i + 1) * spectrumStep), spectrum.Length);

            float sum = 0;
            for (int j = startBin; j < endBin; j++)
            {
                sum += spectrum[j];
            }

            float avg = sum / (endBin - startBin);
            _processedSpectrum![i] = Clamp(
                avg * SPECTRUM_AMPLIFICATION,
                0,
                1);
        }
    }

    private void UpdatePointsBasedOnSpectrum() =>
        _logger.Safe(
            HandleUpdatePointsBasedOnSpectrum,
            LOG_PREFIX,
            "Error updating points based on spectrum"
        );

    private void HandleUpdatePointsBasedOnSpectrum()
    {
        for (int i = 0; i < _voronoiPoints.Count; i++)
        {
            var point = _voronoiPoints[i];
            int freqIndex = point.FrequencyIndex;

            if (freqIndex < _processedSpectrum!.Length)
            {
                UpdatePointProperties(ref point, freqIndex);
                _voronoiPoints[i] = point;
            }
        }
    }

    private void UpdatePointProperties(ref VoronoiPoint point, int freqIndex)
    {
        float intensity = _processedSpectrum![freqIndex];
        float targetSize = MIN_POINT_SIZE +
                         (MAX_POINT_SIZE - MIN_POINT_SIZE) * intensity;

        point.Size += (targetSize - point.Size) * SMOOTHING_FACTOR;
        point.VelocityX *= 1 + intensity * VELOCITY_BOOST_FACTOR;
        point.VelocityY *= 1 + intensity * VELOCITY_BOOST_FACTOR;
    }

    private void InitializePoints(int count) =>
        _logger.Safe(
            () => HandleInitializePoints(count),
            LOG_PREFIX,
            "Error initializing points"
        );

    private void HandleInitializePoints(int count)
    {
        _voronoiPoints.Clear();

        for (int i = 0; i < count; i++)
        {
            _voronoiPoints.Add(CreateVoronoiPoint(i));
        }
    }

    private VoronoiPoint CreateVoronoiPoint(int index) =>
        new()
        {
            X = _random.Next(100, 700),
            Y = _random.Next(100, 500),
            VelocityX = MIN_MOVE_SPEED +
                      (float)_random.NextDouble() *
                      (MAX_MOVE_SPEED - MIN_MOVE_SPEED),
            VelocityY = MIN_MOVE_SPEED +
                      (float)_random.NextDouble() *
                      (MAX_MOVE_SPEED - MIN_MOVE_SPEED),
            Size = MIN_POINT_SIZE,
            FrequencyIndex = index % DEFAULT_POINT_COUNT
        };

    private void UpdateVoronoiPoints(float width, float height) =>
        _logger.Safe(
            () => HandleUpdateVoronoiPoints(width, height),
            LOG_PREFIX,
            "Error updating Voronoi points"
        );

    private void HandleUpdateVoronoiPoints(float width, float height)
    {
        _timeAccumulator += TIME_STEP;

        for (int i = 0; i < _voronoiPoints.Count; i++)
        {
            UpdateSinglePoint(i, width, height);
        }
    }

    private void UpdateSinglePoint(int index, float width, float height)
    {
        var point = _voronoiPoints[index];

        point.X += point.VelocityX * Sin(_timeAccumulator + index);
        point.Y += point.VelocityY * Cos(_timeAccumulator * 0.7f + index);

        HandleHorizontalBoundaries(ref point, width);
        HandleVerticalBoundaries(ref point, height);

        _voronoiPoints[index] = point;
    }

    private static void HandleHorizontalBoundaries(ref VoronoiPoint point, float width)
    {
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
    }

    private static void HandleVerticalBoundaries(ref VoronoiPoint point, float height)
    {
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
    }

    private void PrecalculateNearestPoints() =>
        _logger.Safe(
            HandlePrecalculateNearestPoints,
            LOG_PREFIX,
            "Error precalculating nearest points"
        );

    private void HandlePrecalculateNearestPoints()
    {
        if (_nearestPointGrid == null) return;

        Parallel.For(0, _gridRows, row =>
        {
            CalculateNearestPointsForRow(row);
        });
    }

    private void CalculateNearestPointsForRow(int row) =>
        _logger.Safe(
            () => HandleCalculateNearestPointsForRow(row),
            LOG_PREFIX,
            "Error calculating nearest points for row"
        );

    private void HandleCalculateNearestPointsForRow(int row)
    {
        for (int col = 0; col < _gridCols; col++)
        {
            float cellX = col * GRID_CELL_SIZE;
            float cellY = row * GRID_CELL_SIZE;
            _nearestPointGrid![col, row] = FindNearestPointIndex(cellX, cellY);
        }
    }

    private int FindNearestPointIndex(float x, float y)
    {
        if (_voronoiPoints.Count == 0)
            return -1;

        int nearest = 0;
        float minDistance = float.MaxValue;

        for (int i = 0; i < _voronoiPoints.Count; i++)
        {
            var point = _voronoiPoints[i];
            float distanceSquared = CalculateDistanceSquared(x, y, point.X, point.Y);

            if (distanceSquared < minDistance)
            {
                minDistance = distanceSquared;
                nearest = i;
            }
        }

        return nearest;
    }

    private static float CalculateDistanceSquared(
        float x1,
        float y1,
        float x2,
        float y2)
    {
        float dx = x1 - x2;
        float dy = y1 - y2;
        return dx * dx + dy * dy;
    }

    protected override void OnDispose() =>
        _logger.Safe(
            () =>
            {
                _cellPaint?.Dispose();
                _borderPaint?.Dispose();

                _cellPaint = null;
                _borderPaint = null;
                _processedSpectrum = null;
                _nearestPointGrid = null;

                base.OnDispose();
                _logger.Debug(LOG_PREFIX, "Disposed");
            },
            LOG_PREFIX,
            "Error during specific disposal"
        );
}