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
            DEFAULT_POOL_SIZE = 5,
            SPECTRUM_BANDS = 8,
            UPDATE_FRAME_SKIP = 3,
            RIPPLE_RADIUS = 3;

        public const float
            DEFAULT_SPACING = 14f,
            POINT_RADIUS = 2.0f,
            LINE_WIDTH = 1.5f,
            NEIGHBOR_FORCE = 0.1f,
            SPRING_FORCE = 0.03f,
            DAMPING = 0.95f,
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
            CENTER_PROPORTION = 0.5f,
            WAVE_PROPAGATION_SPEED = 0.8f,
            WAVE_DECAY = 0.98f,
            SPECTRUM_FORCE_MULTIPLIER = 50f,
            VERTICAL_FORCE_MULTIPLIER = 1.5f,
            HORIZONTAL_FORCE_MULTIPLIER = 0.5f,
            RIPPLE_STRENGTH = 0.3f,
            TURBULENCE_STRENGTH = 0.2f,
            MAX_DISPLACEMENT = 30f,
            BAND_LERP_FACTOR = 0.3f,
            LOUDNESS_MULTIPLIER = 4.0f,
            DEEP_ALPHA_FACTOR = 0.7f,
            VELOCITY_SCALE_FACTOR = 0.02f,
            VELOCITY_OFFSET_SCALE = 0.1f,
            WAVE_PHASE_X_FACTOR = 0.2f,
            WAVE_PHASE_Y_FACTOR = 0.1f,
            HIGHLIGHT_ANIM_FACTOR = 0.1f,
            HIGHLIGHT_ANIM_AMPLITUDE = 0.3f,
            HIGHLIGHT_ANIM_OFFSET = 0.7f,
            RIPPLE_THRESHOLD = 0.1f,
            PERFORMANCE_SCALE_MIN = 0.5f,
            PERFORMANCE_SCALE_MAX = 1.0f,
            HUE_SHIFT_AMPLITUDE = 10f,
            HUE_MAX_DEGREES = 360f,
            TURBULENCE_TIME_X = 3f,
            TURBULENCE_TIME_Y = 2f,
            HIGHLIGHT_STRIDE_LARGE = 3,
            HIGHLIGHT_STRIDE_SMALL = 2,
            RENDER_SKIP_MULTIPLIER = 2;

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

    private SKShader? _waterShader;
    private readonly float[] _spectrumBands = new float[SPECTRUM_BANDS];
    private readonly float[] _previousSpectrumBands = new float[SPECTRUM_BANDS];
    private float _physicsAccumulator;

    private readonly Stopwatch _frameTimeStopwatch = new();
    private int _currentFrame;

    protected override void OnInitialize()
    {
        base.OnInitialize();
        _frameTimeStopwatch.Start();
        _logger.Log(LogLevel.Debug, LogPrefix, "Initialized");
    }

    protected override void OnQualitySettingsApplied()
    {
        _currentSettings = QualityPresets[Quality];
        _needsRebuild = true;
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
        UpdateState(spectrum, info, barWidth, barSpacing, barCount);

        if (_points.Count == 0) return;

        UpdatePhysics(spectrum);
        RenderWaterElements(canvas, paint);
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
            ProcessSpectrumBands(spectrum);
        }
    }

    private void RenderWaterElements(
        SKCanvas canvas,
        SKPaint paint)
    {
        ConfigurePaints(paint);

        if (UseAdvancedEffects)
            RenderWaterSurface(canvas);

        if (_currentSettings.ShowConnections)
            RenderConnections(canvas);

        RenderPoints(canvas);

        if (UseAdvancedEffects)
            RenderHighlightsAndReflections(canvas);
    }

    private void ProcessSpectrumBands(float[] spectrum)
    {
        if (spectrum.Length == 0) return;

        int bandSize = spectrum.Length / SPECTRUM_BANDS;

        for (int i = 0; i < SPECTRUM_BANDS; i++)
        {
            float avg = CalculateBandAverage(spectrum, i, bandSize);
            _previousSpectrumBands[i] = _spectrumBands[i];
            _spectrumBands[i] = Lerp(_spectrumBands[i], avg, BAND_LERP_FACTOR);
        }
    }

    private static float CalculateBandAverage(
        float[] spectrum,
        int bandIndex,
        int bandSize)
    {
        int start = bandIndex * bandSize;
        int end = Min((bandIndex + 1) * bandSize, spectrum.Length);

        float sum = 0;
        for (int j = start; j < end; j++)
            sum += spectrum[j];

        return sum / Max(1, end - start);
    }

    private void UpdatePhysics(float[] spectrum)
    {
        float deltaTime = _animationTimer.DeltaTime;
        _physicsAccumulator += deltaTime;

        int substeps = _currentSettings.PhysicsSubsteps;
        float fixedDeltaTime = PHYSICS_UPDATE_STEP / substeps;

        while (_physicsAccumulator >= PHYSICS_UPDATE_STEP)
        {
            for (int i = 0; i < substeps; i++)
                UpdatePointPhysics(fixedDeltaTime, spectrum);

            _physicsAccumulator -= PHYSICS_UPDATE_STEP;
        }
    }

    private void UpdatePointPhysics(float deltaTime, float[] spectrum)
    {
        ApplySpectrumForces(spectrum);
        UpdatePointPositions(deltaTime);
    }

    private void UpdatePointPositions(float deltaTime)
    {
        foreach (var point in _points)
        {
            var springForce = CalculateSpringForce(point);
            var neighborForce = CalculateNeighborForce(point);
            var totalForce = AddPoints(springForce, neighborForce);

            point.Velocity = ScalePoint(
                AddPoints(point.Velocity, ScalePoint(totalForce, deltaTime)),
                DAMPING
            );

            LimitDisplacement(point);
            point.Position = AddPoints(point.Position, ScalePoint(point.Velocity, deltaTime));
        }
    }

    private static SKPoint CalculateSpringForce(WaterPoint point) =>
        ScalePoint(
            SubtractPoints(point.OriginalPosition, point.Position),
            SPRING_FORCE
        );

    private static void LimitDisplacement(WaterPoint point)
    {
        var displacement = SubtractPoints(point.Position, point.OriginalPosition);
        float length = GetPointLength(displacement);

        if (length > MAX_DISPLACEMENT)
        {
            var normalized = NormalizePoint(displacement);
            point.Position = AddPoints(
                point.OriginalPosition,
                ScalePoint(normalized, MAX_DISPLACEMENT)
            );
        }
    }

    private void ApplySpectrumForces(float[] spectrum)
    {
        if (spectrum.Length == 0) return;

        float time = _animationTimer.Time;

        for (int y = 0; y < _rows; y++)
            for (int x = 0; x < _columns; x++)
                ApplyForceToPoint(x, y, time);
    }

    private void ApplyForceToPoint(int x, int y, float time)
    {
        int index = y * _columns + x;
        if (index >= _points.Count) return;

        var point = _points[index];
        int bandIndex = GetBandIndex(x);

        float bandValue = _spectrumBands[bandIndex];
        float bandDelta = bandValue - _previousSpectrumBands[bandIndex];

        var force = CalculatePointForce(x, y, time, bandValue, bandDelta, index);
        point.Velocity = AddPoints(point.Velocity, force);

        if (bandDelta > RIPPLE_THRESHOLD)
            PropagateRipple(x, y, bandDelta * RIPPLE_STRENGTH);
    }

    private int GetBandIndex(int x) =>
        Clamp((x * SPECTRUM_BANDS) / _columns, 0, SPECTRUM_BANDS - 1);

    private static SKPoint CalculatePointForce(
        int x,
        int y,
        float time,
        float bandValue,
        float bandDelta,
        int index)
    {
        float wavePhase = time * WAVE_SPEED + x * WAVE_PHASE_X_FACTOR + y * WAVE_PHASE_Y_FACTOR;
        float waveFactor = Sin(wavePhase) * WAVE_AMPLITUDE;

        float verticalForce = bandValue * SPECTRUM_FORCE_MULTIPLIER *
            VERTICAL_FORCE_MULTIPLIER * (1 + waveFactor);

        float horizontalForce = bandDelta * SPECTRUM_FORCE_MULTIPLIER *
            HORIZONTAL_FORCE_MULTIPLIER * Cos(wavePhase);

        float turbulence = TURBULENCE_STRENGTH * bandValue;
        float turbX = Sin(time * TURBULENCE_TIME_X + index) * turbulence;
        float turbY = Cos(time * TURBULENCE_TIME_Y + index) * turbulence;

        return new SKPoint(horizontalForce + turbX, -verticalForce + turbY);
    }

    private void PropagateRipple(int centerX, int centerY, float strength)
    {
        for (int dy = -RIPPLE_RADIUS; dy <= RIPPLE_RADIUS; dy++)
            for (int dx = -RIPPLE_RADIUS; dx <= RIPPLE_RADIUS; dx++)
                ApplyRippleForce(centerX, centerY, dx, dy, strength);
    }

    private void ApplyRippleForce(
        int centerX,
        int centerY,
        int dx,
        int dy,
        float strength)
    {
        int x = centerX + dx;
        int y = centerY + dy;

        if (!IsValidGridPosition(x, y)) return;

        int index = y * _columns + x;
        if (index >= _points.Count) return;

        float distance = Sqrt(dx * dx + dy * dy);
        if (distance > 0 && distance <= RIPPLE_RADIUS)
        {
            float falloff = 1 - (distance / RIPPLE_RADIUS);
            float rippleForce = strength * falloff * WAVE_PROPAGATION_SPEED;

            var direction = new SKPoint(dx / distance, dy / distance);
            _points[index].Velocity = AddPoints(
                _points[index].Velocity,
                ScalePoint(direction, rippleForce)
            );
        }
    }

    private bool IsValidGridPosition(int x, int y) =>
        x >= 0 && x < _columns && y >= 0 && y < _rows;

    private SKPoint CalculateNeighborForce(WaterPoint point)
    {
        var force = new SKPoint(0, 0);

        foreach (var neighbor in point.Neighbors)
        {
            var diff = SubtractPoints(neighbor.Position, point.Position);
            var distance = GetPointLength(diff);

            if (distance > 0)
            {
                var direction = ScalePoint(diff, 1f / distance);
                var springForce = (distance - _spacing) * NEIGHBOR_FORCE;
                force = AddPoints(force, ScalePoint(direction, springForce));
            }
        }

        return force;
    }

    private bool ShouldRebuildGrid(
        float width,
        float height,
        float barSpacing,
        int barCount) =>
        _needsRebuild ||
        ((IsSizeChanged(width, height) ||
          IsSpacingChanged(barSpacing) ||
          _lastBarCount != barCount) &&
         IsCooldownExpired());

    private bool IsSizeChanged(float width, float height) =>
        MathF.Abs(_lastWidth - width) > width * GRID_SIZE_CHANGE_THRESHOLD ||
        MathF.Abs(_lastHeight - height) > height * GRID_SIZE_CHANGE_THRESHOLD;

    private bool IsSpacingChanged(float barSpacing) =>
        MathF.Abs(_lastBarSpacing - barSpacing) >
        barSpacing * BAR_SPACING_CHANGE_THRESHOLD;

    private bool IsCooldownExpired() =>
        _animationTimer.Time - _lastGridRebuildTime > GRID_RESIZE_COOLDOWN;

    private void BuildGrid(float width, float height, int barCount)
    {
        CalculateGridDimensions(width, height, barCount);
        CreateGridPoints();
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
            scale = Clamp(scale, PERFORMANCE_SCALE_MIN, PERFORMANCE_SCALE_MAX);

            _columns = Max(3, (int)(_columns * scale));
            _rows = Max(2, (int)(_rows * scale));
        }
    }

    private void CreateGridPoints()
    {
        _points.Clear();
        _points.Capacity = _rows * _columns;

        for (int y = 0; y < _rows; y++)
            for (int x = 0; x < _columns; x++)
                _points.Add(new WaterPoint(
                    _xOffset + x * _spacing,
                    _yOffset + y * _spacing
                ));
    }

    private void ConnectGridPoints()
    {
        for (int y = 0; y < _rows; y++)
            for (int x = 0; x < _columns; x++)
                ConnectPoint(x, y);
    }

    private void ConnectPoint(int x, int y)
    {
        int index = y * _columns + x;
        if (index >= _points.Count) return;

        var point = _points[index];

        AddNeighborIfValid(point, x - 1, y, true);
        AddNeighborIfValid(point, x, y - 1, true);
        AddNeighborIfValid(point, x + 1, y, false);
        AddNeighborIfValid(point, x, y + 1, false);

        if (UseAdvancedEffects)
        {
            AddNeighborIfValid(point, x - 1, y - 1, false);
            AddNeighborIfValid(point, x + 1, y - 1, false);
        }
    }

    private void AddNeighborIfValid(
        WaterPoint point,
        int x,
        int y,
        bool isVisual)
    {
        if (IsValidGridPosition(x, y))
        {
            int neighborIndex = y * _columns + x;
            if (neighborIndex < _points.Count)
            {
                var neighbor = _points[neighborIndex];
                point.Neighbors.Add(neighbor);
                if (isVisual) point.VisualNeighbors.Add(neighbor);
            }
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
        _lastGridRebuildTime = _animationTimer.Time;
    }

    private void UpdateVisualParameters(float[] spectrum, SKImageInfo info)
    {
        float loudness = CalculateLoudness(spectrum);
        _lastLoudness = Lerp(_lastLoudness, loudness, LOUDNESS_SMOOTHING);

        float targetAlpha = WATER_ALPHA_BASE +
            _lastLoudness * (WATER_ALPHA_MAX - WATER_ALPHA_BASE);
        _currentWaterAlpha = MathF.Min(WATER_ALPHA_MAX, targetAlpha);

        float targetBrightness = 0.5f + _lastLoudness * 0.5f;
        _currentBrightness = Lerp(_currentBrightness, targetBrightness, BRIGHTNESS_SMOOTHING);

        if (UseAdvancedEffects)
            UpdateWaterShader(info);
    }

    private static float CalculateLoudness(float[] spectrum)
    {
        if (spectrum.Length == 0) return 0;

        float sum = 0;
        for (int i = 0; i < spectrum.Length; i++)
            sum += spectrum[i];

        return MathF.Min(1.0f, sum / spectrum.Length * LOUDNESS_MULTIPLIER);
    }

    private void UpdateWaterShader(SKImageInfo info)
    {
        _waterShader?.Dispose();

        byte baseAlpha = (byte)_currentWaterAlpha;
        byte deepAlpha = (byte)(_currentWaterAlpha * DEEP_ALPHA_FACTOR);

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

    private void ConfigurePaints(SKPaint basePaint)
    {
        _ = basePaint.Color;

        if (UseAdvancedEffects)
        {
            float hueShift = Sin(_animationTimer.Time * COLOR_SHIFT_SPEED) * HUE_SHIFT_AMPLITUDE;
            _ = ShiftHue(basePaint.Color, hueShift);
        }
    }

    private static SKColor ShiftHue(SKColor color, float shift)
    {
        color.ToHsl(out float h, out float s, out float l);
        h = (h + shift) % HUE_MAX_DEGREES;
        if (h < 0) h += HUE_MAX_DEGREES;
        return SKColor.FromHsl(h, s, l);
    }

    private void RenderWaterSurface(SKCanvas canvas)
    {
        using var fillPaint = CreateWaterFillPaint();

        int rowStride = _rows > ROW_RENDERING_THRESHOLD ? 2 : 1;

        for (int y = 0; y < _rows - 1; y += rowStride)
        {
            using var path = _waterPathPool.Get();
            BuildWaterRowPath(path, y);
            canvas.DrawPath(path, fillPaint);
        }
    }

    private SKPaint CreateWaterFillPaint()
    {
        var fillPaint = CreateStandardPaint(BASE_WATER_COLOR);

        if (UseAdvancedEffects && _waterShader != null)
        {
            float phase = _animationTimer.Time * WAVE_SPEED;
            float scaleX = 1 + Sin(phase) * WAVE_AMPLITUDE * _lastLoudness;
            float scaleY = 1 + Cos(phase) * WAVE_AMPLITUDE * _lastLoudness;

            var matrix = SKMatrix.CreateScale(scaleX, scaleY);
            fillPaint.Shader = _waterShader.WithLocalMatrix(matrix);
        }
        else
        {
            fillPaint.Color = BASE_WATER_COLOR.WithAlpha((byte)_currentWaterAlpha);
        }

        return fillPaint;
    }

    private void BuildWaterRowPath(SKPath path, int y)
    {
        int startIndex = y * _columns;
        int nextRowIndex = (y + 1) * _columns;

        if (!IsValidRowIndices(startIndex, nextRowIndex)) return;

        path.MoveTo(_points[startIndex].Position);

        AddRowPointsToPath(path, startIndex);
        AddReverseRowPointsToPath(path, nextRowIndex);

        path.Close();
    }

    private bool IsValidRowIndices(int startIndex, int nextRowIndex) =>
        startIndex < _points.Count && nextRowIndex < _points.Count;

    private void AddRowPointsToPath(SKPath path, int startIndex)
    {
        for (int x = 1; x < _columns && startIndex + x < _points.Count; x++)
            path.LineTo(_points[startIndex + x].Position);
    }

    private void AddReverseRowPointsToPath(SKPath path, int nextRowIndex)
    {
        for (int x = _columns - 1; x >= 0 && nextRowIndex + x < _points.Count; x--)
            path.LineTo(_points[nextRowIndex + x].Position);
    }

    private void RenderConnections(SKCanvas canvas)
    {
        if (_points.Count > CONNECTIONS_THRESHOLD) return;

        using var linePaint = CreateLinePaint();

        foreach (var point in _points)
            foreach (var neighbor in point.VisualNeighbors)
                canvas.DrawLine(point.Position, neighbor.Position, linePaint);
    }

    private SKPaint CreateLinePaint()
    {
        var paint = CreateStandardPaint(BASE_WATER_COLOR.WithAlpha(LINE_ALPHA));
        paint.Style = SKPaintStyle.Stroke;
        paint.StrokeWidth = LINE_WIDTH;
        return paint;
    }

    private void RenderPoints(SKCanvas canvas)
    {
        using var pointPaint = CreatePointPaint();

        int stride = CalculateRenderStride();

        for (int i = 0; i < _points.Count; i += stride)
            RenderSinglePoint(canvas, _points[i], pointPaint);
    }

    private SKPaint CreatePointPaint()
    {
        var paint = CreateStandardPaint(SKColors.White);
        paint.Style = SKPaintStyle.Fill;
        return paint;
    }

    private int CalculateRenderStride() =>
        _points.Count > POINT_RENDERING_THRESHOLD ?
        _currentSettings.RenderSkip * (int)RENDER_SKIP_MULTIPLIER :
        _currentSettings.RenderSkip;

    private static void RenderSinglePoint(
        SKCanvas canvas,
        WaterPoint point,
        SKPaint paint)
    {
        float velocityMagnitude = GetPointLength(point.Velocity);
        float pointSize = POINT_RADIUS * (1 + velocityMagnitude * VELOCITY_SCALE_FACTOR);

        canvas.DrawCircle(point.Position, pointSize, paint);
    }

    private void RenderHighlightsAndReflections(SKCanvas canvas)
    {
        using var highlightPaint = CreateHighlightPaint();
        using var reflectionPaint = CreateReflectionPaint();

        int stride = _points.Count > POINT_RENDERING_THRESHOLD ?
            (int)HIGHLIGHT_STRIDE_LARGE : (int)HIGHLIGHT_STRIDE_SMALL;

        for (int i = 0; i < _points.Count; i += stride)
        {
            var point = _points[i];
            RenderHighlight(canvas, point, i, highlightPaint);

            if (IsBottomRow(i))
                RenderReflection(canvas, point, reflectionPaint);
        }
    }

    private SKPaint CreateHighlightPaint() =>
        CreateStandardPaint(HIGHLIGHT_COLOR);

    private SKPaint CreateReflectionPaint()
    {
        var paint = CreateStandardPaint(SKColors.White.WithAlpha(REFLECTION_ALPHA));
        paint.BlendMode = SKBlendMode.SrcOver;
        return paint;
    }

    private bool IsBottomRow(int index) =>
        index >= _points.Count - _columns;

    private void RenderHighlight(
        SKCanvas canvas,
        WaterPoint point,
        int index,
        SKPaint paint)
    {
        float highlightSize = CalculateHighlightSize(point, index);
        var position = CalculateHighlightPosition(point);
        canvas.DrawCircle(position.X, position.Y, highlightSize, paint);
    }

    private float CalculateHighlightSize(WaterPoint point, int index)
    {
        float animPhase = _animationTimer.Time * WAVE_SPEED + index * HIGHLIGHT_ANIM_FACTOR;
        float animOffset = Sin(animPhase) * HIGHLIGHT_ANIM_AMPLITUDE + HIGHLIGHT_ANIM_OFFSET;
        float velocityOffset = GetPointLength(point.Velocity) * VELOCITY_OFFSET_SCALE;

        return POINT_RADIUS * HIGHLIGHT_SIZE_FACTOR * animOffset * (1 + velocityOffset);
    }

    private static SKPoint CalculateHighlightPosition(WaterPoint point) =>
        new(
            point.Position.X - POINT_RADIUS * HIGHLIGHT_OFFSET_FACTOR -
                point.Velocity.X * VELOCITY_OFFSET_SCALE,
            point.Position.Y - POINT_RADIUS * HIGHLIGHT_OFFSET_FACTOR -
                point.Velocity.Y * VELOCITY_OFFSET_SCALE
        );

    private static void RenderReflection(
        SKCanvas canvas,
        WaterPoint point,
        SKPaint paint)
    {
        float width = POINT_RADIUS * REFLECTION_WIDTH_FACTOR;
        float height = POINT_RADIUS * REFLECTION_HEIGHT_FACTOR;

        SKRect rect = new(
            point.Position.X - width,
            point.Position.Y + POINT_RADIUS,
            point.Position.X + width,
            point.Position.Y + POINT_RADIUS + height);

        canvas.DrawOval(rect, paint);
    }

    private void UpdateFrameTime()
    {
        float elapsed = (float)_frameTimeStopwatch.Elapsed.TotalSeconds;
        _frameTimeStopwatch.Restart();
        _avgFrameTime = Lerp(_avgFrameTime, elapsed, FRAME_TIME_SMOOTHING);
    }

    private static float Lerp(float a, float b, float t) =>
        a + (b - a) * t;

    private static SKPoint AddPoints(SKPoint a, SKPoint b) =>
        new(a.X + b.X, a.Y + b.Y);

    private static SKPoint SubtractPoints(SKPoint a, SKPoint b) =>
        new(a.X - b.X, a.Y - b.Y);

    private static SKPoint ScalePoint(SKPoint point, float scale) =>
        new(point.X * scale, point.Y * scale);

    private static float GetPointLength(SKPoint point) =>
        Sqrt(point.X * point.X + point.Y * point.Y);

    private static SKPoint NormalizePoint(SKPoint point)
    {
        float length = GetPointLength(point);
        return length == 0 ? new SKPoint(0, 0) : new SKPoint(point.X / length, point.Y / length);
    }

    protected override void CleanupUnusedResources()
    {
        if (_waterShader != null && !RequiresRedraw())
        {
            _waterShader.Dispose();
            _waterShader = null;
        }
    }

    protected override void OnDispose()
    {
        _waterShader?.Dispose();
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