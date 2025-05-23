#nullable enable

using static SpectrumNet.SN.Visualization.Renderers.WaterRenderer.Constants;
using static System.MathF;

namespace SpectrumNet.SN.Visualization.Renderers;

public sealed class WaterRenderer : EffectSpectrumRenderer
{
    private const string LogPrefix = nameof(WaterRenderer);

    private static readonly Lazy<WaterRenderer> _instance =
        new(() => new WaterRenderer());

    private WaterRenderer() { }

    public static WaterRenderer GetInstance() => _instance.Value;

    public static class Constants
    {
        public const int
            DEFAULT_COLUMNS = 40,
            DEFAULT_ROWS = 25,
            HIGH_COLUMNS = 70,
            HIGH_ROWS = 40,
            LOW_COLUMNS = 25,
            LOW_ROWS = 15,
            CONNECTIONS_THRESHOLD = 500,
            POINT_RENDERING_THRESHOLD = 800,
            ROW_RENDERING_THRESHOLD = 20,
            DEFAULT_POOL_SIZE = 5;

        public const float
            DEFAULT_SPACING = 14f,
            POINT_RADIUS = 2.0f,
            LINE_WIDTH = 1.5f,
            NEIGHBOR_FORCE = 0.1f,
            SPRING_FORCE = 0.03f,
            DAMPING = 1.05f,
            TIME_STEP = 0.05f,
            PHYSICS_UPDATE_STEP = 0.016f,
            PHYSICS_SUBSTEPS = 3,
            INTERACTION_RADIUS = 7700f,
            ATTRACTION_FORCE = 0.02f,
            REPULSION_FORCE = 0.2f,
            SPECTRUM_IMPACT_MIN = 0.05f,
            SPECTRUM_IMPACT_MAX = 0.3f,
            HIGHLIGHT_SIZE_FACTOR = 0.5f,
            HIGHLIGHT_OFFSET_FACTOR = 0.25f,
            REFLECTION_HEIGHT_FACTOR = 0.2f,
            REFLECTION_WIDTH_FACTOR = 0.8f,
            WAVE_SPEED = 0.5f,
            WAVE_AMPLITUDE = 0.2f,
            LOUDNESS_SMOOTHING = 0.2f,
            BRIGHTNESS_SMOOTHING = 0.1f,
            FRAME_TIME_SMOOTHING = 0.1f,
            ADAPTIVE_PERFORMANCE_THRESHOLD = 0.025f,
            MIN_AMPLITUDE_THRESHOLD = 0.05f,
            COLOR_SHIFT_SPEED = 0.1f,
            GRID_SIZE_CHANGE_THRESHOLD = 0.05f,
            BAR_SPACING_CHANGE_THRESHOLD = 0.2f,
            GRID_RESIZE_COOLDOWN = 0.5f,
            CENTER_PROPORTION = 0.5f;

        public const byte
            LINE_ALPHA = 100,
            WATER_ALPHA_BASE = 40,
            WATER_ALPHA_MAX = 80,
            HIGHLIGHT_ALPHA_BASE = 180,
            REFLECTION_ALPHA = 60;

        public static readonly SKColor
            BASE_WATER_COLOR = new(0, 120, 255, WATER_ALPHA_BASE),
            DEEP_WATER_COLOR = new(0, 40, 150, WATER_ALPHA_BASE),
            HIGHLIGHT_COLOR = new(255, 255, 255, HIGHLIGHT_ALPHA_BASE);

        public static readonly float[] GradientPositions = [0f, 1f];

        public static readonly Dictionary<RenderQuality, QualitySettings> QualityPresets = new()
        {
            [RenderQuality.Low] = new(
                ShowConnections: false,
                UseAdvancedEffects: false,
                UseAntialiasing: false,
                TimeFactor: 0.8f,
                DensityFactor: 0.6f,
                PhysicsSubsteps: 2,
                RenderSkip: 3
            ),
            [RenderQuality.Medium] = new(
                ShowConnections: false,
                UseAdvancedEffects: true,
                UseAntialiasing: true,
                TimeFactor: 1.0f,
                DensityFactor: 1.0f,
                PhysicsSubsteps: 3,
                RenderSkip: 2
            ),
            [RenderQuality.High] = new(
                ShowConnections: true,
                UseAdvancedEffects: true,
                UseAntialiasing: true,
                TimeFactor: 1.2f,
                DensityFactor: 1.5f,
                PhysicsSubsteps: 4,
                RenderSkip: 1
            )
        };

        public record QualitySettings(
            bool ShowConnections,
            bool UseAdvancedEffects,
            bool UseAntialiasing,
            float TimeFactor,
            float DensityFactor,
            int PhysicsSubsteps,
            int RenderSkip
        );
    }

    private readonly List<WaterPoint> _points = [];
    private readonly ObjectPool<SKPath> _waterPathPool = new(
        () => new SKPath(),
        path => path.Reset(),
        DEFAULT_POOL_SIZE);

    private int _columns;
    private int _rows;
    private float _spacing;
    private float _xOffset;
    private float _yOffset;
    private bool _needsRebuild = true;

    private float _lastWidth;
    private float _lastHeight;
    private float _lastBarSpacing;
    private int _lastBarCount;
    private float _lastGridRebuildTime;

    private QualitySettings _currentSettings = QualityPresets[RenderQuality.Medium];
    private float _avgFrameTime = 0.016f;
    private float _currentWaterAlpha = WATER_ALPHA_BASE;
    private float _currentBrightness = 0.5f;
    private float _lastLoudness;

    private SKPaint? _pointPaint;
    private SKPaint? _linePaint;
    private SKPaint? _fillPaint;
    private SKPaint? _highlightPaint;
    private SKPaint? _reflectionPaint;
    private SKShader? _waterShader;

    private readonly Stopwatch _frameTimeStopwatch = new();
    private int _currentFrame;
    private const int UPDATE_FRAME_SKIP = 3;

    protected override void OnInitialize()
    {
        base.OnInitialize();
        _frameTimeStopwatch.Start();
        InitializePaints();
        _logger.Log(LogLevel.Debug, LogPrefix, "Initialized");
    }

    protected override void OnQualitySettingsApplied()
    {
        _currentSettings = QualityPresets[Quality];
        _needsRebuild = true;
        UpdatePaintQuality();
        _logger.Log(LogLevel.Debug, LogPrefix, $"Quality changed to {Quality}");
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
        _logger.Safe(
            () => RenderWater(canvas, spectrum, info, barWidth, barSpacing, barCount, paint),
            LogPrefix,
            "Error during rendering"
        );
    }

    private void RenderWater(
        SKCanvas canvas,
        float[] spectrum,
        SKImageInfo info,
        float barWidth,
        float barSpacing,
        int barCount,
        SKPaint basePaint)
    {
        UpdateState(spectrum, info, barWidth, barSpacing, barCount);

        if (_points.Count == 0) return;

        ConfigurePaints(basePaint, spectrum);

        if (_useAdvancedEffects && _fillPaint != null)
        {
            RenderWaterSurface(canvas);
        }

        if (_currentSettings.ShowConnections)
        {
            RenderConnections(canvas);
        }

        RenderPoints(canvas);

        if (_useAdvancedEffects)
        {
            RenderHighlightsAndReflections(canvas);
        }

        UpdateFrameTime();
    }

    private void UpdateState(
        float[] spectrum,
        SKImageInfo info,
        float barWidth,
        float barSpacing,
        int barCount)
    {
        _currentFrame = (_currentFrame + 1) % UPDATE_FRAME_SKIP;

        if (ShouldRebuildGrid(info.Width, info.Height, barSpacing, barCount))
        {
            BuildGrid(info.Width, info.Height, barCount);
            UpdateGridParameters(info.Width, info.Height, barWidth, barSpacing, barCount);
        }

        if (_currentFrame == 0)
        {
            UpdateVisualParameters(spectrum, info);
        }
    }

    private bool ShouldRebuildGrid(
        float width,
        float height,
        float barSpacing,
        int barCount)
    {
        if (_needsRebuild) return true;

        bool sizeChanged = MathF.Abs(_lastWidth - width) > width * GRID_SIZE_CHANGE_THRESHOLD ||
                          MathF.Abs(_lastHeight - height) > height * GRID_SIZE_CHANGE_THRESHOLD;

        bool spacingChanged = MathF.Abs(_lastBarSpacing - barSpacing) >
                             barSpacing * BAR_SPACING_CHANGE_THRESHOLD;

        bool barCountChanged = _lastBarCount != barCount;
        bool cooldownExpired = _time - _lastGridRebuildTime > GRID_RESIZE_COOLDOWN;

        return (sizeChanged || spacingChanged || barCountChanged) && cooldownExpired;
    }

    private void BuildGrid(float width, float height, int barCount)
    {
        CalculateGridDimensions(width, height, barCount);
        CreateGridPoints(width, height);
        ConnectGridPoints();
        _needsRebuild = false;

        _logger.Log(LogLevel.Debug, LogPrefix,
            $"Grid built: {_columns}x{_rows} points, spacing: {_spacing:F2}px");
    }

    private void CalculateGridDimensions(float width, float height, int barCount)
    {
        int baseColumns = (int)MathF.Min(barCount, DEFAULT_COLUMNS);
        _columns = Max(3, (int)(baseColumns * _currentSettings.DensityFactor));

        float aspectRatio = height / width;
        _rows = Max(2, (int)(_columns * aspectRatio));

        AdaptGridToPerformance();

        _spacing = width / _columns;
        _xOffset = (width - (_columns - 1) * _spacing) / 2;
        _yOffset = (height - (_rows - 1) * _spacing) / 2;
    }

    private void AdaptGridToPerformance()
    {
        if (_avgFrameTime > ADAPTIVE_PERFORMANCE_THRESHOLD)
        {
            float scale = Sqrt(ADAPTIVE_PERFORMANCE_THRESHOLD / _avgFrameTime);
            scale = MathF.Max(0.5f, MathF.Min(1.0f, scale));

            _columns = Max(3, (int)(_columns * scale));
            _rows = Max(2, (int)(_rows * scale));
        }
    }

    private void CreateGridPoints(float width, float height)
    {
        _points.Clear();
        _points.Capacity = _rows * _columns;

        for (int y = 0; y < _rows; y++)
        {
            for (int x = 0; x < _columns; x++)
            {
                float xPos = _xOffset + x * _spacing;
                float yPos = _yOffset + y * _spacing;
                _points.Add(new WaterPoint(xPos, yPos));
            }
        }
    }

    private void ConnectGridPoints()
    {
        for (int y = 0; y < _rows; y++)
        {
            for (int x = 0; x < _columns; x++)
            {
                ConnectPoint(x, y);
            }
        }
    }

    private void ConnectPoint(int x, int y)
    {
        int index = y * _columns + x;
        if (index >= _points.Count) return;

        var point = _points[index];

        if (x > 0) AddNeighbor(point, y * _columns + (x - 1), true);
        if (y > 0) AddNeighbor(point, (y - 1) * _columns + x, true);
        if (x < _columns - 1) AddNeighbor(point, y * _columns + (x + 1), false);
        if (y < _rows - 1) AddNeighbor(point, (y + 1) * _columns + x, false);

        if (_useAdvancedEffects)
        {
            if (x > 0 && y > 0) AddNeighbor(point, (y - 1) * _columns + (x - 1), false);
            if (x < _columns - 1 && y > 0) AddNeighbor(point, (y - 1) * _columns + (x + 1), false);
        }
    }

    private void AddNeighbor(WaterPoint point, int neighborIndex, bool isVisual)
    {
        if (neighborIndex < _points.Count)
        {
            var neighbor = _points[neighborIndex];
            point.Neighbors.Add(neighbor);
            if (isVisual) point.VisualNeighbors.Add(neighbor);
        }
    }

    private void UpdateGridParameters(
        float width,
        float height,
        float barWidth,
        float barSpacing,
        int barCount)
    {
        _lastWidth = width;
        _lastHeight = height;
        _lastBarSpacing = barSpacing;
        _lastBarCount = barCount;
        _lastGridRebuildTime = _time;
    }

    private void UpdateVisualParameters(float[] spectrum, SKImageInfo info)
    {
        float loudness = CalculateLoudness(spectrum);
        _lastLoudness = _lastLoudness * (1 - LOUDNESS_SMOOTHING) + loudness * LOUDNESS_SMOOTHING;

        float targetAlpha = WATER_ALPHA_BASE + _lastLoudness * (WATER_ALPHA_MAX - WATER_ALPHA_BASE);
        _currentWaterAlpha = MathF.Min(WATER_ALPHA_MAX, targetAlpha);

        float targetBrightness = 0.5f + _lastLoudness * 0.5f;
        _currentBrightness = _currentBrightness * (1 - BRIGHTNESS_SMOOTHING) +
                           targetBrightness * BRIGHTNESS_SMOOTHING;

        if (_useAdvancedEffects)
        {
            UpdateWaterShader(info);
        }
    }

    private static float CalculateLoudness(float[] spectrum)
    {
        if (spectrum.Length == 0) return 0;

        float sum = 0;
        for (int i = 0; i < spectrum.Length; i++)
        {
            sum += spectrum[i];
        }

        return MathF.Min(1.0f, sum / spectrum.Length * 4.0f);
    }

    private void UpdateWaterShader(SKImageInfo info)
    {
        _waterShader?.Dispose();

        byte baseAlpha = (byte)_currentWaterAlpha;
        byte deepAlpha = (byte)(_currentWaterAlpha * 0.7f);

        var colors = new[]
        {
            BASE_WATER_COLOR.WithAlpha(baseAlpha),
            DEEP_WATER_COLOR.WithAlpha(deepAlpha)
        };

        _waterShader = SKShader.CreateLinearGradient(
            new SKPoint(0, 0),
            new SKPoint(0, info.Height),
            colors,
            GradientPositions,
            SKShaderTileMode.Clamp);
    }

    private void ConfigurePaints(SKPaint basePaint, float[] spectrum)
    {
        SKColor baseColor = basePaint.Color;

        if (_useAdvancedEffects)
        {
            float hueShift = Sin(_time * COLOR_SHIFT_SPEED) * 10;
            baseColor = ShiftHue(baseColor, hueShift);
        }

        if (_pointPaint != null) _pointPaint.Color = baseColor;
        if (_linePaint != null) _linePaint.Color = baseColor.WithAlpha(LINE_ALPHA);

        if (_fillPaint != null)
        {
            if (_useAdvancedEffects && _waterShader != null)
            {
                float phase = _time * WAVE_SPEED;
                float scaleX = 1 + Sin(phase) * WAVE_AMPLITUDE * _lastLoudness;
                float scaleY = 1 + Cos(phase) * WAVE_AMPLITUDE * _lastLoudness;

                var matrix = SKMatrix.CreateScale(scaleX, scaleY);
                _fillPaint.Shader = _waterShader.WithLocalMatrix(matrix);
            }
            else
            {
                _fillPaint.Shader = null;
                _fillPaint.Color = baseColor.WithAlpha((byte)_currentWaterAlpha);
            }
        }

        if (_reflectionPaint != null) _reflectionPaint.Color = baseColor.WithAlpha(REFLECTION_ALPHA);
    }

    private static SKColor ShiftHue(SKColor color, float shift)
    {
        color.ToHsl(out float h, out float s, out float l);
        h = (h + shift) % 360;
        if (h < 0) h += 360;
        return SKColor.FromHsl(h, s, l);
    }

    private void RenderWaterSurface(SKCanvas canvas)
    {
        int rowStride = _rows > ROW_RENDERING_THRESHOLD ? 2 : 1;

        for (int y = 0; y < _rows - 1; y += rowStride)
        {
            using var path = _waterPathPool.Get();
            BuildWaterRowPath(path, y);
            canvas.DrawPath(path, _fillPaint!);
        }
    }

    private void BuildWaterRowPath(SKPath path, int y)
    {
        int startIndex = y * _columns;
        int nextRowIndex = (y + 1) * _columns;

        if (startIndex >= _points.Count || nextRowIndex >= _points.Count) return;

        path.MoveTo(_points[startIndex].Position);

        for (int x = 1; x < _columns && startIndex + x < _points.Count; x++)
        {
            path.LineTo(_points[startIndex + x].Position);
        }

        for (int x = _columns - 1; x >= 0 && nextRowIndex + x < _points.Count; x--)
        {
            path.LineTo(_points[nextRowIndex + x].Position);
        }

        path.Close();
    }

    private void RenderConnections(SKCanvas canvas)
    {
        if (_points.Count > CONNECTIONS_THRESHOLD || _linePaint == null) return;

        foreach (var point in _points)
        {
            foreach (var neighbor in point.VisualNeighbors)
            {
                canvas.DrawLine(point.Position, neighbor.Position, _linePaint);
            }
        }
    }

    private void RenderPoints(SKCanvas canvas)
    {
        if (_pointPaint == null) return;

        int stride = _points.Count > POINT_RENDERING_THRESHOLD ?
            _currentSettings.RenderSkip * 2 : _currentSettings.RenderSkip;

        for (int i = 0; i < _points.Count; i += stride)
        {
            canvas.DrawCircle(_points[i].Position, POINT_RADIUS, _pointPaint);
        }
    }

    private void RenderHighlightsAndReflections(SKCanvas canvas)
    {
        if (_highlightPaint == null || _reflectionPaint == null) return;

        int stride = _points.Count > POINT_RENDERING_THRESHOLD ? 3 : 2;

        for (int i = 0; i < _points.Count; i += stride)
        {
            var point = _points[i];
            RenderHighlight(canvas, point, i);

            if (i >= _points.Count - _columns)
            {
                RenderReflection(canvas, point);
            }
        }
    }

    private void RenderHighlight(SKCanvas canvas, WaterPoint point, int index)
    {
        float highlightSize = POINT_RADIUS * HIGHLIGHT_SIZE_FACTOR;
        float animPhase = _time * WAVE_SPEED + index * 0.1f;
        float animOffset = Sin(animPhase) * 0.3f + 0.7f;

        float x = point.Position.X - POINT_RADIUS * HIGHLIGHT_OFFSET_FACTOR;
        float y = point.Position.Y - POINT_RADIUS * HIGHLIGHT_OFFSET_FACTOR;

        canvas.DrawCircle(x, y, highlightSize * animOffset, _highlightPaint!);
    }

    private void RenderReflection(SKCanvas canvas, WaterPoint point)
    {
        float width = POINT_RADIUS * REFLECTION_WIDTH_FACTOR;
        float height = POINT_RADIUS * REFLECTION_HEIGHT_FACTOR;

        SKRect rect = new(
            point.Position.X - width,
            point.Position.Y + POINT_RADIUS,
            point.Position.X + width,
            point.Position.Y + POINT_RADIUS + height);

        canvas.DrawOval(rect, _reflectionPaint!);
    }

    private void UpdateFrameTime()
    {
        float elapsed = (float)_frameTimeStopwatch.Elapsed.TotalSeconds;
        _frameTimeStopwatch.Restart();
        _avgFrameTime = _avgFrameTime * (1 - FRAME_TIME_SMOOTHING) + elapsed * FRAME_TIME_SMOOTHING;
    }

    private void InitializePaints()
    {
        _pointPaint = CreatePaint(
            SKColors.White,
            SKPaintStyle.StrokeAndFill,
            POINT_RADIUS);

        _linePaint = CreatePaint(
            BASE_WATER_COLOR.WithAlpha(LINE_ALPHA),
            SKPaintStyle.Stroke,
            LINE_WIDTH);

        _fillPaint = CreatePaint(
            BASE_WATER_COLOR,
            SKPaintStyle.Fill,
            0);

        _highlightPaint = CreatePaint(
            HIGHLIGHT_COLOR,
            SKPaintStyle.Fill,
            0);

        _reflectionPaint = CreatePaint(
            SKColors.White.WithAlpha(REFLECTION_ALPHA),
            SKPaintStyle.Fill,
            0,
            blendMode: SKBlendMode.SrcOver);
    }

    private void UpdatePaintQuality()
    {
        if (_pointPaint != null) _pointPaint.IsAntialias = _useAntiAlias;
        if (_linePaint != null) _linePaint.IsAntialias = _useAntiAlias;
        if (_fillPaint != null) _fillPaint.IsAntialias = _useAntiAlias;
        if (_highlightPaint != null) _highlightPaint.IsAntialias = _useAntiAlias;
        if (_reflectionPaint != null) _reflectionPaint.IsAntialias = _useAntiAlias;
    }

    private SKPaint CreatePaint(
        SKColor color,
        SKPaintStyle style,
        float strokeWidth,
        SKStrokeCap strokeCap = SKStrokeCap.Butt,
        SKBlendMode blendMode = SKBlendMode.SrcOver)
    {
        var paint = _paintPool.Get();
        paint.Color = color;
        paint.Style = style;
        paint.IsAntialias = _useAntiAlias;
        paint.StrokeWidth = strokeWidth;
        paint.StrokeCap = strokeCap;
        paint.BlendMode = blendMode;
        return paint;
    }

    protected override void OnInvalidateCachedResources()
    {
        base.OnInvalidateCachedResources();
        _waterShader?.Dispose();
        _waterShader = null;
        _needsRebuild = true;
    }

    protected override void OnDispose()
    {
        _waterShader?.Dispose();
        _pointPaint?.Dispose();
        _linePaint?.Dispose();
        _fillPaint?.Dispose();
        _highlightPaint?.Dispose();
        _reflectionPaint?.Dispose();
        _waterPathPool.Dispose();
        _points.Clear();
        base.OnDispose();
        _logger.Log(LogLevel.Debug, LogPrefix, "Disposed");
    }

    private class WaterPoint(float x, float y)
    {
        public SKPoint Position { get; set; } = new(x, y);
        public SKPoint Velocity { get; set; } = new(0, 0);
        public readonly SKPoint OriginalPosition = new(x, y);
        public readonly List<WaterPoint> Neighbors = [];
        public readonly List<WaterPoint> VisualNeighbors = [];
    }
}