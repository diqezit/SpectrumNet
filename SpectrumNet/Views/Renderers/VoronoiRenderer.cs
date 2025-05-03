#nullable enable

using static System.MathF;
using static SpectrumNet.Views.Renderers.VoronoiRenderer.Constants;

namespace SpectrumNet.Views.Renderers;

public sealed class VoronoiRenderer : EffectSpectrumRenderer
{
    private static readonly Lazy<VoronoiRenderer> _instance = new(() => new VoronoiRenderer());

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
    }

    private readonly Random _random = new();
    private readonly List<VoronoiPoint> _voronoiPoints = [];
    private float _timeAccumulator;

    private SKPaint? _cellPaint;
    private SKPaint? _borderPaint;

    private int _gridCols;
    private int _gridRows;
    private int[,]? _nearestPointGrid;
    private int _lastWidth;
    private int _lastHeight;

    private struct VoronoiPoint
    {
        public float X, Y, VelocityX, VelocityY, Size;
        public int FrequencyIndex;
    }

    private VoronoiRenderer() { }

    protected override void OnInitialize()
    {
        InitializePaints();
        InitializePoints(GetPointCount());
    }

    private void InitializePaints()
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

    protected override void OnConfigurationChanged()
    {
        InitializePoints(GetPointCount());
    }

    protected override void OnQualitySettingsApplied()
    {
        UpdatePaintQualitySettings();
        InvalidateCachedResources();
    }

    private void UpdatePaintQualitySettings()
    {
        if (_cellPaint != null)
            _cellPaint.IsAntialias = UseAntiAlias;

        if (_borderPaint != null)
            _borderPaint.IsAntialias = UseAntiAlias;
    }

    protected override void OnInvalidateCachedResources()
    {
        _nearestPointGrid = null;
        _lastWidth = 0;
        _lastHeight = 0;
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
        ProcessSpectrum(
            spectrum,
            Min(spectrum.Length, DEFAULT_POINT_COUNT));

        UpdateVoronoiPoints(info.Width, info.Height);
        UpdateGridIfNeeded(info.Width, info.Height);

        if (Quality != RenderQuality.Low)
        {
            PrecalculateNearestPoints();
        }

        RenderVoronoiDiagram(canvas, info, paint);
    }

    private void UpdateGridIfNeeded(float width, float height)
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

    private int GetNearestPointIndexForCell(int col, int row, float cellX, float cellY)
    {
        return Quality != RenderQuality.Low && _nearestPointGrid != null
            ? _nearestPointGrid[col, row]
            : FindNearestPointIndex(cellX, cellY);
    }

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
        float maxDistance = Max(info.Width, info.Height) * MAX_DISTANCE_FACTOR;
        using var path = new SKPath();

        for (int i = 0; i < _voronoiPoints.Count; i++)
        {
            DrawBordersForPoint(path, i, maxDistance);
        }

        canvas.DrawPath(path, _borderPaint!);
    }

    private void DrawBordersForPoint(SKPath path, int pointIndex, float maxDistance)
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

    private void DrawVoronoiPoints(SKCanvas canvas, SKPaint basePaint)
    {
        _cellPaint!.Color = basePaint.Color.WithAlpha(200);

        if (Quality == RenderQuality.High && UseAdvancedEffects)
        {
            DrawVoronoiPointsWithPath(canvas);
        }
        else
        {
            DrawVoronoiPointsIndividually(canvas);
        }
    }

    private void DrawVoronoiPointsWithPath(SKCanvas canvas)
    {
        using var pointsPath = new SKPath();

        foreach (var point in _voronoiPoints)
        {
            pointsPath.AddCircle(point.X, point.Y, point.Size);
        }

        canvas.DrawPath(pointsPath, _cellPaint!);
    }

    private void DrawVoronoiPointsIndividually(SKCanvas canvas)
    {
        foreach (var point in _voronoiPoints)
        {
            canvas.DrawCircle(point.X, point.Y, point.Size, _cellPaint!);
        }
    }

    private void ProcessSpectrum(float[] spectrum, int freqBands)
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

    private void CalculateProcessedSpectrum(float[] spectrum, int freqBands)
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

    private void UpdatePointsBasedOnSpectrum()
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

    private void InitializePoints(int count)
    {
        _voronoiPoints.Clear();

        for (int i = 0; i < count; i++)
        {
            _voronoiPoints.Add(CreateVoronoiPoint(i));
        }
    }

    private VoronoiPoint CreateVoronoiPoint(int index)
    {
        return new VoronoiPoint
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
    }

    private void UpdateVoronoiPoints(float width, float height)
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

    private void PrecalculateNearestPoints()
    {
        if (_nearestPointGrid == null) return;

        Parallel.For(0, _gridRows, row =>
        {
            CalculateNearestPointsForRow(row);
        });
    }

    private void CalculateNearestPointsForRow(int row)
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

    protected override void OnDispose()
    {
        _cellPaint?.Dispose();
        _borderPaint?.Dispose();

        _cellPaint = null;
        _borderPaint = null;
        _processedSpectrum = null;
        _nearestPointGrid = null;
    }
}