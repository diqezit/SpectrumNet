#nullable enable

using static System.MathF;
using static SpectrumNet.Views.Renderers.VoronoiRenderer.Constants;
using static SpectrumNet.Views.Renderers.VoronoiRenderer.Constants.Quality;

namespace SpectrumNet.Views.Renderers;

public sealed class VoronoiRenderer : EffectSpectrumRenderer
{
    private static readonly Lazy<VoronoiRenderer> _instance = new(() => new VoronoiRenderer());

    private VoronoiRenderer() { }

    public static VoronoiRenderer GetInstance() => _instance.Value;

    public record Constants
    {
        public const string LOG_PREFIX = "VoronoiRenderer";

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

    private volatile bool _isConfiguring;

    private struct VoronoiPoint
    {
        public float X, Y, VelocityX, VelocityY, Size;
        public int FrequencyIndex;
    }

    protected override void OnInitialize()
    {
        ExecuteSafely(
            () =>
            {
                base.OnInitialize();
                InitializeQualityParams();
                InitializePaints();
                InitializePoints(GetPointCount());
                Log(LogLevel.Debug, LOG_PREFIX, "Initialized");
            },
            nameof(OnInitialize),
            "Failed during renderer initialization"
        );
    }

    public override void SetOverlayTransparency(float level)
    {
        if (Math.Abs(_overlayAlphaFactor - level) < float.Epsilon)
            return;

        _overlayAlphaFactor = level;
        _overlayStateChangeRequested = true;
        _overlayStateChanged = true;
    }

    private void InitializeQualityParams()
    {
        ExecuteSafely(
            () =>
            {
                ApplyQualitySettingsInternal();
            },
            nameof(InitializeQualityParams),
            "Failed to initialize quality parameters"
        );
    }

    private void InitializePaints()
    {
        ExecuteSafely(
            () =>
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
            },
            nameof(InitializePaints),
            "Failed to initialize paints"
        );
    }

    private int GetPointCount() =>
        _isOverlayActive
            ? OVERLAY_POINT_COUNT
            : DEFAULT_POINT_COUNT;

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
                    bool overlayChanged = _isOverlayActive != isOverlayActive;
                    bool qualityChanged = Quality != quality;

                    _isOverlayActive = isOverlayActive;
                    Quality = quality;
                    _smoothingFactor = isOverlayActive ? 0.5f : 0.3f;

                    if (overlayChanged)
                    {
                        _overlayAlphaFactor = isOverlayActive ? 0.75f : 1.0f;
                        _overlayStateChangeRequested = true;
                        _overlayStateChanged = true;
                    }

                    if (overlayChanged || qualityChanged)
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
                InitializePoints(GetPointCount());
                Log(LogLevel.Information,
                    LOG_PREFIX,
                    $"Configuration changed. New Quality: {Quality}, Overlay: {_isOverlayActive}");
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

        UpdatePaintQualitySettings();
        InvalidateCachedResources();

        Log(LogLevel.Debug, LOG_PREFIX,
            $"Quality settings applied. Quality: {Quality}, " +
            $"AntiAlias: {UseAntiAlias}, AdvancedEffects: {UseAdvancedEffects}, " +
            $"BatchedPoints: {_useBatchedPoints}");
    }

    private void LowQualitySettings()
    {
        base._useAntiAlias = LOW_USE_ANTI_ALIAS;
        base._useAdvancedEffects = LOW_USE_ADVANCED_EFFECTS;
        _useBatchedPoints = LOW_USE_BATCHED_POINTS;
    }

    private void MediumQualitySettings()
    {
        base._useAntiAlias = MEDIUM_USE_ANTI_ALIAS;
        base._useAdvancedEffects = MEDIUM_USE_ADVANCED_EFFECTS;
        _useBatchedPoints = MEDIUM_USE_BATCHED_POINTS;
    }

    private void HighQualitySettings()
    {
        base._useAntiAlias = HIGH_USE_ANTI_ALIAS;
        base._useAdvancedEffects = HIGH_USE_ADVANCED_EFFECTS;
        _useBatchedPoints = HIGH_USE_BATCHED_POINTS;
    }

    private void UpdatePaintQualitySettings()
    {
        ExecuteSafely(
            () =>
            {
                if (_cellPaint != null)
                    _cellPaint.IsAntialias = UseAntiAlias;

                if (_borderPaint != null)
                    _borderPaint.IsAntialias = UseAntiAlias;
            },
            nameof(UpdatePaintQualitySettings),
            "Failed to update paint quality settings"
        );
    }

    protected override void OnInvalidateCachedResources()
    {
        ExecuteSafely(
            () =>
            {
                base.OnInvalidateCachedResources();
                _nearestPointGrid = null;
                _lastWidth = 0;
                _lastHeight = 0;

                Log(LogLevel.Debug, LOG_PREFIX, "Cached resources invalidated");
            },
            nameof(OnInvalidateCachedResources),
            "Failed to invalidate cached resources"
        );
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
                if (_overlayStateChangeRequested)
                {
                    _overlayStateChangeRequested = false;
                    _overlayStateChanged = true;
                }

                UpdateState(spectrum, info);
                RenderWithOverlay(canvas, () => RenderFrame(canvas, info, paint));

                if (_overlayStateChanged)
                {
                    _overlayStateChanged = false;
                }
            },
            nameof(RenderEffect),
            "Error during rendering"
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
        Log(LogLevel.Error, LOG_PREFIX, $"Invalid image dimensions: {info.Width}x{info.Height}");
        return false;
    }

    private bool IsDisposed()
    {
        if (!_disposed) return false;
        Log(LogLevel.Error, LOG_PREFIX, "Renderer is disposed");
        return true;
    }

    private void UpdateState(float[] spectrum, SKImageInfo info)
    {
        ExecuteSafely(
            () =>
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
            },
            nameof(UpdateState),
            "Error updating state"
        );
    }

    private void RenderFrame(SKCanvas canvas, SKImageInfo info, SKPaint paint)
    {
        ExecuteSafely(
            () =>
            {
                RenderVoronoiDiagram(canvas, info, paint);
            },
            nameof(RenderFrame),
            "Error rendering frame"
        );
    }

    private void UpdateGridIfNeeded(float width, float height)
    {
        ExecuteSafely(
            () =>
            {
                if (_lastWidth != width || _lastHeight != height)
                {
                    _gridCols = (int)Ceiling(width / GRID_CELL_SIZE);
                    _gridRows = (int)Ceiling(height / GRID_CELL_SIZE);
                    _nearestPointGrid = new int[_gridCols, _gridRows];
                    _lastWidth = (int)width;
                    _lastHeight = (int)height;
                }
            },
            nameof(UpdateGridIfNeeded),
            "Failed to update grid"
        );
    }

    private void RenderVoronoiDiagram(
        SKCanvas canvas,
        SKImageInfo info,
        SKPaint basePaint)
    {
        ExecuteSafely(
            () =>
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
            },
            nameof(RenderVoronoiDiagram),
            "Error rendering Voronoi diagram"
        );
    }

    private void DrawVoronoiCells(
        SKCanvas canvas,
        SKImageInfo info,
        SKPaint basePaint)
    {
        ExecuteSafely(
            () =>
            {
                for (int row = 0; row < _gridRows; row++)
                {
                    for (int col = 0; col < _gridCols; col++)
                    {
                        DrawSingleCell(canvas, info, basePaint, row, col);
                    }
                }
            },
            nameof(DrawVoronoiCells),
            "Error drawing Voronoi cells"
        );
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

    private void DrawVoronoiBorders(SKCanvas canvas, SKImageInfo info)
    {
        ExecuteSafely(
            () =>
            {
                float maxDistance = Max(info.Width, info.Height) * MAX_DISTANCE_FACTOR;
                using var path = new SKPath();

                for (int i = 0; i < _voronoiPoints.Count; i++)
                {
                    DrawBordersForPoint(path, i, maxDistance);
                }

                canvas.DrawPath(path, _borderPaint!);
            },
            nameof(DrawVoronoiBorders),
            "Error drawing Voronoi borders"
        );
    }

    private void DrawBordersForPoint(SKPath path, int pointIndex, float maxDistance)
    {
        ExecuteSafely(
            () =>
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
            },
            nameof(DrawBordersForPoint),
            "Error drawing borders for point"
        );
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

    private void DrawVoronoiPoints(SKCanvas canvas, SKPaint basePaint)
    {
        ExecuteSafely(
            () =>
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
            },
            nameof(DrawVoronoiPoints),
            "Error drawing Voronoi points"
        );
    }

    private void DrawVoronoiPointsWithPath(SKCanvas canvas)
    {
        ExecuteSafely(
            () =>
            {
                using var pointsPath = new SKPath();

                foreach (var point in _voronoiPoints)
                {
                    pointsPath.AddCircle(point.X, point.Y, point.Size);
                }

                canvas.DrawPath(pointsPath, _cellPaint!);
            },
            nameof(DrawVoronoiPointsWithPath),
            "Error drawing points with path"
        );
    }

    private void DrawVoronoiPointsIndividually(SKCanvas canvas)
    {
        ExecuteSafely(
            () =>
            {
                foreach (var point in _voronoiPoints)
                {
                    canvas.DrawCircle(point.X, point.Y, point.Size, _cellPaint!);
                }
            },
            nameof(DrawVoronoiPointsIndividually),
            "Error drawing individual points"
        );
    }

    private void ProcessSpectrum(float[] spectrum, int freqBands)
    {
        ExecuteSafely(
            () =>
            {
                if (_voronoiPoints.Count == 0) return;

                EnsureProcessedSpectrumSize(freqBands);
                CalculateProcessedSpectrum(spectrum, freqBands);
                UpdatePointsBasedOnSpectrum();
            },
            nameof(ProcessSpectrum),
            "Error processing spectrum"
        );
    }

    private void EnsureProcessedSpectrumSize(int freqBands)
    {
        if (base._processedSpectrum == null || base._processedSpectrum.Length < freqBands)
            base._processedSpectrum = new float[freqBands];
    }

    private void CalculateProcessedSpectrum(float[] spectrum, int freqBands)
    {
        ExecuteSafely(
            () =>
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
                    base._processedSpectrum![i] = Clamp(
                        avg * SPECTRUM_AMPLIFICATION,
                        0,
                        1);
                }
            },
            nameof(CalculateProcessedSpectrum),
            "Error calculating processed spectrum"
        );
    }

    private void UpdatePointsBasedOnSpectrum()
    {
        ExecuteSafely(
            () =>
            {
                for (int i = 0; i < _voronoiPoints.Count; i++)
                {
                    var point = _voronoiPoints[i];
                    int freqIndex = point.FrequencyIndex;

                    if (freqIndex < base._processedSpectrum!.Length)
                    {
                        UpdatePointProperties(ref point, freqIndex);
                        _voronoiPoints[i] = point;
                    }
                }
            },
            nameof(UpdatePointsBasedOnSpectrum),
            "Error updating points based on spectrum"
        );
    }

    private void UpdatePointProperties(ref VoronoiPoint point, int freqIndex)
    {
        float intensity = base._processedSpectrum![freqIndex];
        float targetSize = MIN_POINT_SIZE +
                         (MAX_POINT_SIZE - MIN_POINT_SIZE) * intensity;

        point.Size += (targetSize - point.Size) * SMOOTHING_FACTOR;
        point.VelocityX *= 1 + intensity * VELOCITY_BOOST_FACTOR;
        point.VelocityY *= 1 + intensity * VELOCITY_BOOST_FACTOR;
    }

    private void InitializePoints(int count)
    {
        ExecuteSafely(
            () =>
            {
                _voronoiPoints.Clear();

                for (int i = 0; i < count; i++)
                {
                    _voronoiPoints.Add(CreateVoronoiPoint(i));
                }
            },
            nameof(InitializePoints),
            "Error initializing points"
        );
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

    private void UpdateVoronoiPoints(float width, float height)
    {
        ExecuteSafely(
            () =>
            {
                _timeAccumulator += TIME_STEP;

                for (int i = 0; i < _voronoiPoints.Count; i++)
                {
                    UpdateSinglePoint(i, width, height);
                }
            },
            nameof(UpdateVoronoiPoints),
            "Error updating Voronoi points"
        );
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

    private void PrecalculateNearestPoints()
    {
        ExecuteSafely(
            () =>
            {
                if (_nearestPointGrid == null) return;

                Parallel.For(0, _gridRows, row =>
                {
                    CalculateNearestPointsForRow(row);
                });
            },
            nameof(PrecalculateNearestPoints),
            "Error precalculating nearest points"
        );
    }

    private void CalculateNearestPointsForRow(int row)
    {
        ExecuteSafely(
            () =>
            {
                for (int col = 0; col < _gridCols; col++)
                {
                    float cellX = col * GRID_CELL_SIZE;
                    float cellY = row * GRID_CELL_SIZE;
                    _nearestPointGrid![col, row] = FindNearestPointIndex(cellX, cellY);
                }
            },
            nameof(CalculateNearestPointsForRow),
            "Error calculating nearest points for row"
        );
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

    public override void Dispose()
    {
        if (_disposed) return;

        ExecuteSafely(
            () =>
            {
                OnDispose();
            },
            nameof(Dispose),
            "Error during disposal"
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
            "Error during specific disposal"
        );
    }

    private void DisposeManagedResources()
    {
        _cellPaint?.Dispose();
        _borderPaint?.Dispose();

        _cellPaint = null;
        _borderPaint = null;
        base._processedSpectrum = null;
        _nearestPointGrid = null;
    }
}