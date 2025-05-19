#nullable enable

using static System.MathF;
using static SpectrumNet.Views.Renderers.WaterRenderer.Constants;
using static SpectrumNet.Views.Renderers.WaterRenderer.Constants.Quality;
using System.Text;

namespace SpectrumNet.Views.Renderers;

public sealed class WaterRenderer : EffectSpectrumRenderer
{
    private const string LogPrefix = nameof(WaterRenderer);

    private static readonly Lazy<WaterRenderer> _instance = new(() => new WaterRenderer());

    private WaterRenderer()
    {
        _gridState = new WaterGridState(_logger);
        _physicsEngine = new WaterPhysicsEngine(_logger);
        _renderState = new WaterRenderState(_logger);
        _spectrumImpactFactor = SPECTRUM_IMPACT_MIN;
    }

    public static WaterRenderer GetInstance() => _instance.Value;

    public record Constants
    {
        public const string LOG_PREFIX = nameof(WaterRenderer);

        // Grid layout constants
        public const int
            DEFAULT_COLUMNS = 40,
            DEFAULT_ROWS = 25,
            HIGH_COLUMNS = 70,
            HIGH_ROWS = 40,
            LOW_COLUMNS = 25,
            LOW_ROWS = 15;

        public const float
            DEFAULT_SPACING = 14f,
            POINT_RADIUS = 2.0f,
            LINE_WIDTH = 1.5f;

        // Physics constants
        public const float
            NEIGHBOR_FORCE = 0.1f,
            SPRING_FORCE = 0.03f,
            DAMPING = 1.05f,
            TIME_STEP = 0.05f,
            PHYSICS_UPDATE_STEP = 0.016f,
            PHYSICS_SUBSTEPS = 3;

        // Force constants
        public const float
            INTERACTION_RADIUS = 7700f,
            ATTRACTION_FORCE = 0.02f,
            REPULSION_FORCE = 0.2f;

        // Spectrum influence constants
        public const float
            SPECTRUM_IMPACT_MIN = 0.05f,
            SPECTRUM_IMPACT_MAX = 0.3f;

        // Rendering thresholds
        public const int
            CONNECTIONS_THRESHOLD = 500,
            POINT_RENDERING_THRESHOLD = 800,
            ROW_RENDERING_THRESHOLD = 20;

        // Appearance constants - Alpha values
        public const byte
            LINE_ALPHA = 100,
            WATER_ALPHA_BASE = 40,
            WATER_ALPHA_MAX = 80,
            HIGHLIGHT_ALPHA_BASE = 180,
            REFLECTION_ALPHA = 60;

        // Appearance constants - Effects
        public const float
            HIGHLIGHT_SIZE_FACTOR = 0.5f,
            HIGHLIGHT_OFFSET_FACTOR = 0.25f,
            REFLECTION_HEIGHT_FACTOR = 0.2f,
            REFLECTION_WIDTH_FACTOR = 0.8f;

        // Animation constants    
        public const float
            WAVE_SPEED = 0.5f,
            WAVE_AMPLITUDE = 0.2f;

        // Smoothing constants    
        public const float
            LOUDNESS_SMOOTHING = 0.2f,
            BRIGHTNESS_SMOOTHING = 0.1f,
            FRAME_TIME_SMOOTHING = 0.1f;

        // Performance constants
        public const float
            ADAPTIVE_PERFORMANCE_THRESHOLD = 0.025f,
            MIN_AMPLITUDE_THRESHOLD = 0.05f,
            COLOR_SHIFT_SPEED = 0.1f;

        // Grid stability constants
        public const float
            GRID_SIZE_CHANGE_THRESHOLD = 0.05f,
            BAR_SPACING_CHANGE_THRESHOLD = 0.2f,
            GRID_RESIZE_COOLDOWN = 0.5f;

        // Water colors
        public static readonly SKColor
            BASE_WATER_COLOR = new(0, 120, 255, WATER_ALPHA_BASE),
            DEEP_WATER_COLOR = new(0, 40, 150, WATER_ALPHA_BASE),
            HIGHLIGHT_COLOR = new(255, 255, 255, HIGHLIGHT_ALPHA_BASE);

        public static class Quality
        {
            // Connection display settings
            public const bool
                LOW_SHOW_CONNECTIONS = false,
                MEDIUM_SHOW_CONNECTIONS = false,
                HIGH_SHOW_CONNECTIONS = true;

            // Visual effects settings
            public const bool
                LOW_USE_ADVANCED_EFFECTS = false,
                MEDIUM_USE_ADVANCED_EFFECTS = true,
                HIGH_USE_ADVANCED_EFFECTS = true;

            // Anti-aliasing settings
            public const bool
                LOW_USE_ANTIALIASING = false,
                MEDIUM_USE_ANTIALIASING = true,
                HIGH_USE_ANTIALIASING = true;

            // Animation speed settings
            public const float
                LOW_TIME_FACTOR = 0.8f,
                MEDIUM_TIME_FACTOR = 1.0f,
                HIGH_TIME_FACTOR = 1.2f;

            // Grid density settings
            public const float
                LOW_DENSITY_FACTOR = 0.6f,
                MEDIUM_DENSITY_FACTOR = 1.0f,
                HIGH_DENSITY_FACTOR = 1.5f;

            // Physics detail settings
            public const int
                LOW_PHYSICS_SUBSTEPS = 2,
                MEDIUM_PHYSICS_SUBSTEPS = 3,
                HIGH_PHYSICS_SUBSTEPS = 4;

            // Rendering optimization settings
            public const int
                LOW_RENDER_SKIP = 3,
                MEDIUM_RENDER_SKIP = 2,
                HIGH_RENDER_SKIP = 1;
        }
    }

    private readonly WaterGridState _gridState;
    private readonly WaterPhysicsEngine _physicsEngine;
    private readonly WaterRenderState _renderState;

    private float _lastWidth;
    private float _lastHeight;
    private float _lastBarWidth;
    private float _lastBarSpacing;
    private int _lastBarCount;
    private float _lastGridRebuildTime;

    private bool _showConnections;
    private int _renderSkip = 1;
    private float _densityFactor = 1.0f;
    private float _timeFactor = 1.0f;
    private readonly float _spectrumImpactFactor;

    private readonly Stopwatch _frameTimeStopwatch = new();
    private float _avgFrameTime = 0.016f;
    private int _currentFrame;
    private readonly int _updateFrameSkip = 3;

    protected override void OnInitialize()
    {
        _logger.Safe(
            () =>
            {
                base.OnInitialize();

                _frameTimeStopwatch.Start();
                _renderState.InitializePaintResources(UseAntiAlias);
                _physicsEngine.Initialize(_timeFactor, _spectrumImpactFactor);

                _logger.Log(LogLevel.Debug, LogPrefix, "Initialized");
            },
            LogPrefix,
            "Failed during renderer initialization"
        );
    }

    protected override void OnConfigurationChanged()
    {
        _logger.Safe(
            () =>
            {
                _gridState.MarkForRebuild();

                var configInfo = new StringBuilder()
                    .Append($"Configuration changed. ")
                    .Append($"New Quality: {Quality}, ")
                    .Append($"AntiAlias: {UseAntiAlias}, ")
                    .Append($"AdvancedEffects: {UseAdvancedEffects}, ")
                    .Append($"ShowConnections: {_showConnections}, ")
                    .Append($"TimeFactor: {_timeFactor}");

                _logger.Log(LogLevel.Information, LogPrefix, configInfo.ToString());
            },
            LogPrefix,
            "Failed to handle configuration change"
        );
    }

    protected override void OnQualitySettingsApplied()
    {
        _logger.Safe(
            () =>
            {
                switch (Quality)
                {
                    case RenderQuality.Low:
                        ApplyLowQualitySettings();
                        break;
                    case RenderQuality.Medium:
                        ApplyMediumQualitySettings();
                        break;
                    case RenderQuality.High:
                        ApplyHighQualitySettings();
                        break;
                }

                _renderState.UpdateQualitySettings(UseAntiAlias, UseAdvancedEffects);
                _physicsEngine.UpdateQualitySettings(_timeFactor);
                _gridState.UpdateQualitySettings(_densityFactor);

                _gridState.MarkForRebuild();

                var qualityInfo = new StringBuilder()
                    .Append($"Quality settings applied. ")
                    .Append($"Quality: {Quality}, ")
                    .Append($"AntiAlias: {UseAntiAlias}, ")
                    .Append($"AdvancedEffects: {UseAdvancedEffects}, ")
                    .Append($"ShowConnections: {_showConnections}, ")
                    .Append($"TimeFactor: {_timeFactor}");

                _logger.Log(LogLevel.Debug, LogPrefix, qualityInfo.ToString());
            },
            LogPrefix,
            "Failed to apply quality settings"
        );
    }

    private void ApplyLowQualitySettings()
    {
        _showConnections = LOW_SHOW_CONNECTIONS;
        _timeFactor = LOW_TIME_FACTOR;
        _renderSkip = LOW_RENDER_SKIP;
        _densityFactor = LOW_DENSITY_FACTOR;
        _physicsEngine.SetSubstepsCount(LOW_PHYSICS_SUBSTEPS);
    }

    private void ApplyMediumQualitySettings()
    {
        _showConnections = MEDIUM_SHOW_CONNECTIONS;
        _timeFactor = MEDIUM_TIME_FACTOR;
        _renderSkip = MEDIUM_RENDER_SKIP;
        _densityFactor = MEDIUM_DENSITY_FACTOR;
        _physicsEngine.SetSubstepsCount(MEDIUM_PHYSICS_SUBSTEPS);
    }

    private void ApplyHighQualitySettings()
    {
        _showConnections = HIGH_SHOW_CONNECTIONS;
        _timeFactor = HIGH_TIME_FACTOR;
        _renderSkip = HIGH_RENDER_SKIP;
        _densityFactor = HIGH_DENSITY_FACTOR;
        _physicsEngine.SetSubstepsCount(HIGH_PHYSICS_SUBSTEPS);
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
            () => ProcessRenderEffect(canvas, spectrum, info, barWidth, barSpacing, barCount, paint),
            LogPrefix,
            "Error during rendering"
        );
    }

    private void ProcessRenderEffect(
        SKCanvas canvas,
        float[] spectrum,
        SKImageInfo info,
        float barWidth,
        float barSpacing,
        int barCount,
        SKPaint paint)
    {
        UpdateState(spectrum, info, barWidth, barSpacing, barCount);
        RenderFrame(canvas, paint);
        UpdateFrameTimeMeasurement();
    }

    private void UpdateState(
        float[] spectrum,
        SKImageInfo info,
        float barWidth,
        float barSpacing,
        int barCount)
    {
        _logger.Safe(
            () => ProcessStateUpdate(spectrum, info, barWidth, barSpacing, barCount),
            LogPrefix,
            "Error updating renderer state"
        );
    }

    private void ProcessStateUpdate(
        float[] spectrum,
        SKImageInfo info,
        float barWidth,
        float barSpacing,
        int barCount)
    {
        IncrementFrameCounter();
        UpdateGridIfNeeded(info.Width, info.Height, barWidth, barSpacing, barCount);

        if (_currentFrame == 0)
        {
            _physicsEngine.ScheduleUpdate(spectrum, _gridState.Points);
        }

        _renderState.UpdateVisualParameters(spectrum, info, UseAdvancedEffects, _currentFrame == 0);
    }

    private void UpdateGridIfNeeded(
        float width,
        float height,
        float barWidth,
        float barSpacing,
        int barCount)
    {
        if (ShouldRebuildGrid(width, height, barSpacing, barCount))
        {
            BuildWaterGrid(width, height, barWidth, barSpacing, barCount);
            UpdateLastGridParameters(width, height, barWidth, barSpacing, barCount);
        }
    }

    private bool ShouldRebuildGrid(
        float width,
        float height,
        float barSpacing,
        int barCount)
    {
        bool forceRebuild = _gridState.NeedsRebuild;

        bool sizeChanged =
            !AreFloatsApproximatelyEqual(
                _lastWidth,
                width,
                width * GRID_SIZE_CHANGE_THRESHOLD) ||
            !AreFloatsApproximatelyEqual(
                _lastHeight,
                height,
                height * GRID_SIZE_CHANGE_THRESHOLD);

        bool spacingChanged =
            !AreFloatsApproximatelyEqual(
                _lastBarSpacing,
                barSpacing,
                barSpacing * BAR_SPACING_CHANGE_THRESHOLD);

        bool barCountChanged = _lastBarCount != barCount;
        bool cooldownExpired = _time - _lastGridRebuildTime > GRID_RESIZE_COOLDOWN;

        return (forceRebuild || sizeChanged || spacingChanged || barCountChanged) && cooldownExpired;
    }

    private void UpdateLastGridParameters(
        float width,
        float height,
        float barWidth,
        float barSpacing,
        int barCount)
    {
        _lastWidth = width;
        _lastHeight = height;
        _lastBarWidth = barWidth;
        _lastBarSpacing = barSpacing;
        _lastBarCount = barCount;
        _lastGridRebuildTime = _time;
    }

    private static bool AreFloatsApproximatelyEqual(float a, float b, float tolerance)
    {
        return MathF.Abs(a - b) < tolerance;
    }

    private void BuildWaterGrid(
        float width,
        float height,
        float barWidth,
        float barSpacing,
        int barCount)
    {
        _logger.Safe(
            () => CreateAndConnectGrid(width, height, barWidth, barSpacing, barCount),
            LogPrefix,
            "Error building water grid"
        );
    }

    private void CreateAndConnectGrid(
        float width,
        float height,
        float barWidth,
        float barSpacing,
        int barCount)
    {
        CalculateGridDimensions(width, height, barCount);
        _gridState.CreateGrid(width, height);
        _gridState.ConnectGridPoints(UseAdvancedEffects);
        _physicsEngine.InitializeForces(_gridState.Points.Count);

        LogGridCreation();
    }

    private void LogGridCreation()
    {
        var gridInfo = $"Water grid built: {_gridState.Columns}x{_gridState.Rows} points, " +
                     $"spacing: {_gridState.Spacing:F2}px";

        _logger.Log(LogLevel.Debug, LogPrefix, gridInfo);
    }

    private void CalculateGridDimensions(float width, float height, int barCount)
    {
        int baseColumns = (int)MathF.Min(barCount, DEFAULT_COLUMNS);
        int columns = (int)MathF.Max(3, (int)(baseColumns * _densityFactor));

        float aspectRatio = height / width;
        int rows = (int)MathF.Max(2, (int)(columns * aspectRatio));

        AdaptGridSizeToPerformance(ref columns, ref rows);

        _gridState.SetDimensions(columns, rows);
        _gridState.SetSpacing(width / columns);
        _gridState.CalculateOffsets(width, height);
    }

    private void AdaptGridSizeToPerformance(ref int columns, ref int rows)
    {
        if (_avgFrameTime > ADAPTIVE_PERFORMANCE_THRESHOLD)
        {
            float scaleFactor = MathF.Sqrt(ADAPTIVE_PERFORMANCE_THRESHOLD / _avgFrameTime);
            scaleFactor = MathF.Max(0.5f, MathF.Min(1.0f, scaleFactor));

            columns = (int)MathF.Max(3, (int)(columns * scaleFactor));
            rows = (int)MathF.Max(2, (int)(rows * scaleFactor));

            LogGridSizeAdaptation(scaleFactor, columns, rows);
        }
    }

    private void LogGridSizeAdaptation(float scaleFactor, int columns, int rows)
    {
        var logMessage = new StringBuilder()
            .Append("Grid size adapted for performance. ")
            .Append($"Scale: {scaleFactor:F2}, ")
            .Append($"New size: {columns}x{rows}");

        _logger.Log(LogLevel.Debug, LogPrefix, logMessage.ToString());
    }

    private void IncrementFrameCounter() =>
        _currentFrame = (_currentFrame + 1) % _updateFrameSkip;

    private void UpdateFrameTimeMeasurement()
    {
        float elapsed = (float)_frameTimeStopwatch.Elapsed.TotalSeconds;
        _frameTimeStopwatch.Restart();

        _avgFrameTime = _avgFrameTime * (1 - FRAME_TIME_SMOOTHING) + elapsed * FRAME_TIME_SMOOTHING;
    }

    private void RenderFrame(
        SKCanvas canvas,
        SKPaint paint)
    {
        if (_gridState.Points.Count == 0)
            return;

        _logger.Safe(
            () => RenderWaterContent(canvas, paint),
            LogPrefix,
            "Error rendering frame"
        );
    }

    private void RenderWaterContent(
        SKCanvas canvas,
        SKPaint paint)
    {
        _renderState.ConfigureRenderingPaints(paint, UseAdvancedEffects, _time);
        RenderWaterElements(canvas);
    }

    private void RenderWaterElements(SKCanvas canvas)
    {
        if (!_renderState.AreRenderResourcesValid())
            return;

        RenderBasicWaterElements(canvas);

        if (_showConnections)
        {
            RenderConnections(canvas);
        }

        RenderPoints(canvas);

        if (UseAdvancedEffects)
        {
            RenderHighlightsAndReflections(canvas);
        }
    }

    private void RenderBasicWaterElements(SKCanvas canvas)
    {
        if (UseAdvancedEffects && _renderState.FillPaint != null)
        {
            RenderWaterSurface(canvas);
        }
    }

    private void RenderWaterSurface(SKCanvas canvas)
    {
        int rowStride = CalculateRowRenderingStride();

        for (int y = 0; y < _gridState.Rows - 1; y += rowStride)
        {
            RenderWaterSurfaceRow(canvas, y);
        }
    }

    private int CalculateRowRenderingStride()
    {
        return _gridState.Rows > ROW_RENDERING_THRESHOLD ? 2 : 1;
    }

    private void RenderWaterSurfaceRow(SKCanvas canvas, int y)
    {
        _renderState.BuildWaterSurfaceRowPath(y, _gridState);
        canvas.DrawPath(_renderState.FillPath, _renderState.FillPaint!);
    }

    private void RenderConnections(SKCanvas canvas)
    {
        if (ShouldSkipConnectionRendering())
            return;

        foreach (var point in _gridState.Points)
        {
            RenderPointConnections(canvas, point);
        }
    }

    private bool ShouldSkipConnectionRendering()
    {
        return _gridState.Points.Count > CONNECTIONS_THRESHOLD;
    }

    private void RenderPointConnections(SKCanvas canvas, WaterPoint point)
    {
        foreach (var neighbor in point.VisualNeighbors)
        {
            canvas.DrawLine(
                point.Position,
                neighbor.Position,
                _renderState.LinePaint!);
        }
    }

    private void RenderPoints(SKCanvas canvas)
    {
        int stride = CalculatePointRenderingStride();

        for (int i = 0; i < _gridState.Points.Count; i += stride)
        {
            canvas.DrawCircle(
                _gridState.Points[i].Position,
                POINT_RADIUS,
                _renderState.PointPaint!);
        }
    }

    private int CalculatePointRenderingStride()
    {
        return _gridState.Points.Count > POINT_RENDERING_THRESHOLD ?
            _renderSkip * 2 : _renderSkip;
    }

    private void RenderHighlightsAndReflections(SKCanvas canvas)
    {
        if (_renderState.HighlightPaint == null || _renderState.ReflectionPaint == null)
            return;

        int stride = CalculateHighlightStride();

        for (int i = 0; i < _gridState.Points.Count; i += stride)
        {
            var point = _gridState.Points[i];

            RenderHighlightForPoint(canvas, point, i);

            if (IsPointInBottomRow(i))
            {
                RenderReflectionForPoint(canvas, point);
            }
        }
    }

    private int CalculateHighlightStride()
    {
        return _gridState.Points.Count > POINT_RENDERING_THRESHOLD ? 3 : 2;
    }

    private bool IsPointInBottomRow(int pointIndex)
    {
        return pointIndex >= _gridState.Points.Count - _gridState.Columns;
    }

    private void RenderHighlightForPoint(SKCanvas canvas, WaterPoint point, int index)
    {
        // Вычисление размера подсветки
        float highlightSize = POINT_RADIUS * HIGHLIGHT_SIZE_FACTOR;

        // Создание анимации смещения
        float animPhase = _time * WAVE_SPEED + index * 0.1f;
        float animOffset = MathF.Sin(animPhase) * 0.3f + 0.7f;

        // Вычисление позиции подсветки
        float offsetX = POINT_RADIUS * HIGHLIGHT_OFFSET_FACTOR;
        float offsetY = POINT_RADIUS * HIGHLIGHT_OFFSET_FACTOR;
        float posX = point.Position.X - offsetX;
        float posY = point.Position.Y - offsetY;

        // Отрисовка подсветки
        canvas.DrawCircle(
            posX,
            posY,
            highlightSize * animOffset,
            _renderState.HighlightPaint!);
    }

    private void RenderReflectionForPoint(SKCanvas canvas, WaterPoint point)
    {
        // Вычисление размеров отражения
        float reflectionWidth = POINT_RADIUS * REFLECTION_WIDTH_FACTOR;
        float reflectionHeight = POINT_RADIUS * REFLECTION_HEIGHT_FACTOR;

        // Вычисление координат прямоугольника отражения
        float left = point.Position.X - reflectionWidth;
        float top = point.Position.Y + POINT_RADIUS;
        float right = point.Position.X + reflectionWidth;
        float bottom = point.Position.Y + POINT_RADIUS + reflectionHeight;

        // Создание прямоугольника для отражения
        SKRect reflectionRect = new(left, top, right, bottom);

        // Отрисовка овала отражения
        canvas.DrawOval(reflectionRect, _renderState.ReflectionPaint!);
    }

    protected override void OnInvalidateCachedResources()
    {
        _logger.Safe(
            () =>
            {
                base.OnInvalidateCachedResources();
                _renderState.InvalidateResources();
                _gridState.MarkForRebuild();
                _logger.Log(LogLevel.Debug, LogPrefix, "Cached resources invalidated");
            },
            LogPrefix,
            "Error invalidating cached resources"
        );
    }

    protected override void OnDispose()
    {
        _logger.Safe(
            () =>
            {
                _physicsEngine.Dispose();
                _renderState.Dispose();
                _gridState.Clear();
                base.OnDispose();
                _logger.Log(LogLevel.Debug, LogPrefix, "Disposed");
            },
            LogPrefix,
            "Error during specific disposal"
        );
    }

#pragma warning disable CS9113 // Parameter is unread.
    private class WaterGridState(ISmartLogger logger)
#pragma warning restore CS9113 // Parameter is unread.
    {
        public int Columns { get; private set; }
        public int Rows { get; private set; }
        public float Spacing { get; private set; }
        public float XOffset { get; private set; }
        public float YOffset { get; private set; }
        public List<WaterPoint> Points { get; } = [];
        public bool NeedsRebuild { get; private set; } = true;
        private float _densityFactor = 1.0f;

        public void SetDimensions(int columns, int rows)
        {
            Columns = columns;
            Rows = rows;
        }

        public void SetSpacing(float spacing)
        {
            Spacing = spacing;
        }

        public void CalculateOffsets(float width, float height)
        {
            XOffset = (width - (Columns - 1) * Spacing) / 2;
            YOffset = (height - (Rows - 1) * Spacing) / 2;
        }

        public void UpdateQualitySettings(float densityFactor)
        {
            _densityFactor = densityFactor;
        }

        public void MarkForRebuild()
        {
            NeedsRebuild = true;
        }

        public void Clear()
        {
            Points.Clear();
            NeedsRebuild = true;
        }

        public void CreateGrid(float width, float height)
        {
            Points.Clear();
            Points.Capacity = Rows * Columns;

            for (int y = 0; y < Rows; y++)
            {
                CreateGridPointsRow(y);
            }

            NeedsRebuild = false;
        }

        private void CreateGridPointsRow(int y)
        {
            for (int x = 0; x < Columns; x++)
            {
                float xPos = XOffset + x * Spacing;
                float yPos = YOffset + y * Spacing;
                Points.Add(new WaterPoint(xPos, yPos));
            }
        }

        public void ConnectGridPoints(bool useAdvancedEffects)
        {
            if (Points.Count > 400 && Rows > 2 && Columns > 2)
            {
                Parallel.For(0, Rows, y =>
                {
                    for (int x = 0; x < Columns; x++)
                    {
                        ConnectPointAtPosition(x, y, useAdvancedEffects);
                    }
                });
            }
            else
            {
                for (int y = 0; y < Rows; y++)
                {
                    for (int x = 0; x < Columns; x++)
                    {
                        ConnectPointAtPosition(x, y, useAdvancedEffects);
                    }
                }
            }
        }

        private void ConnectPointAtPosition(int x, int y, bool useAdvancedEffects)
        {
            int index = y * Columns + x;
            if (index >= Points.Count) return;

            var point = Points[index];

            ConnectToCardinalNeighbors(x, y, point);

            if (useAdvancedEffects)
            {
                ConnectToDiagonalNeighbors(x, y, point);
            }
        }

        private void ConnectToCardinalNeighbors(int x, int y, WaterPoint point)
        {
            ConnectToLeftNeighbor(x, y, point);
            ConnectToTopNeighbor(x, y, point);
            ConnectToRightNeighbor(x, y, point);
            ConnectToBottomNeighbor(x, y, point);
        }

        private void ConnectToLeftNeighbor(int x, int y, WaterPoint point)
        {
            if (x > 0)
            {
                int neighborIndex = y * Columns + (x - 1);
                if (neighborIndex < Points.Count)
                {
                    var leftNeighbor = Points[neighborIndex];
                    point.Neighbors.Add(leftNeighbor);
                    point.VisualNeighbors.Add(leftNeighbor);
                }
            }
        }

        private void ConnectToTopNeighbor(int x, int y, WaterPoint point)
        {
            if (y > 0)
            {
                int neighborIndex = (y - 1) * Columns + x;
                if (neighborIndex < Points.Count)
                {
                    var topNeighbor = Points[neighborIndex];
                    point.Neighbors.Add(topNeighbor);
                    point.VisualNeighbors.Add(topNeighbor);
                }
            }
        }

        private void ConnectToRightNeighbor(int x, int y, WaterPoint point)
        {
            if (x < Columns - 1)
            {
                int neighborIndex = y * Columns + (x + 1);
                if (neighborIndex < Points.Count)
                {
                    var rightNeighbor = Points[neighborIndex];
                    point.Neighbors.Add(rightNeighbor);
                }
            }
        }

        private void ConnectToBottomNeighbor(int x, int y, WaterPoint point)
        {
            if (y < Rows - 1)
            {
                int neighborIndex = (y + 1) * Columns + x;
                if (neighborIndex < Points.Count)
                {
                    var bottomNeighbor = Points[neighborIndex];
                    point.Neighbors.Add(bottomNeighbor);
                }
            }
        }

        private void ConnectToDiagonalNeighbors(int x, int y, WaterPoint point)
        {
            ConnectToTopLeftNeighbor(x, y, point);
            ConnectToTopRightNeighbor(x, y, point);
        }

        private void ConnectToTopLeftNeighbor(int x, int y, WaterPoint point)
        {
            if (x > 0 && y > 0)
            {
                int neighborIndex = (y - 1) * Columns + (x - 1);
                if (neighborIndex < Points.Count)
                {
                    var diagNeighbor = Points[neighborIndex];
                    point.Neighbors.Add(diagNeighbor);
                }
            }
        }

        private void ConnectToTopRightNeighbor(int x, int y, WaterPoint point)
        {
            if (x < Columns - 1 && y > 0)
            {
                int neighborIndex = (y - 1) * Columns + (x + 1);
                if (neighborIndex < Points.Count)
                {
                    var diagNeighbor = Points[neighborIndex];
                    point.Neighbors.Add(diagNeighbor);
                }
            }
        }
    }

    private class WaterPhysicsEngine(ISmartLogger logger)
    {
        private const string LogPrefix = nameof(WaterPhysicsEngine);
        private Vector2[] _forces = [];

        private float 
            _animationTime,
            _physicsTimeAccumulator,
            _timeFactor = 1.0f,
            _spectrumImpactFactor;

        private float[] _spectrumForPhysics = [];

        private readonly object _syncRoot = new();

        private readonly TaskFactory _physicsTaskFactory = new(
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskContinuationOptions.None,
                TaskScheduler.Default);

        private Task? _physicsTask;

        private bool 
            _physicsTaskRunning,
            _pendingPhysicsUpdate;

        private float _substepsCount = PHYSICS_SUBSTEPS;

        public void Initialize(float timeFactor, float spectrumImpactFactor)
        {
            _timeFactor = timeFactor;
            _spectrumImpactFactor = spectrumImpactFactor;
            StartPhysicsTask();
        }

        public void UpdateQualitySettings(float timeFactor)
        {
            _timeFactor = timeFactor;
        }

        public void SetSubstepsCount(int count)
        {
            _substepsCount = count;
        }

        public void InitializeForces(int count)
        {
            _forces = new Vector2[count];
        }

        public void ScheduleUpdate(float[] spectrum, List<WaterPoint> points)
        {
            if (points.Count == 0 || spectrum.Length == 0) return;

            logger.Safe(
                () => ProcessSpectrumData(spectrum),
                LogPrefix,
                "Error scheduling physics update"
            );
        }

        private void ProcessSpectrumData(float[] spectrum)
        {
            int length = spectrum.Length;
            CopySpectrumData(spectrum, length);
            _pendingPhysicsUpdate = true;
        }

        private void CopySpectrumData(float[] spectrum, int length)
        {
            if (_spectrumForPhysics.Length != length)
            {
                float[] newArray = new float[length];
                Array.Copy(spectrum, newArray, length);
                _spectrumForPhysics = newArray;
            }
            else
            {
                Array.Copy(spectrum, _spectrumForPhysics, length);
            }
        }

        private void StartPhysicsTask()
        {
            logger.Safe(
                () =>
                {
                    if (_physicsTask != null) return;

                    _physicsTaskRunning = true;
                    _physicsTask = _physicsTaskFactory.StartNew(RunPhysicsLoop);
                },
                LogPrefix,
                "Failed to start physics task"
            );
        }

        private void RunPhysicsLoop()
        {
            while (_physicsTaskRunning)
            {
                try
                {
                    if (_pendingPhysicsUpdate)
                    {
                        ProcessPhysicsUpdate();
                    }

                    Thread.Sleep(5);
                }
                catch (Exception ex)
                {
                    HandlePhysicsError(ex);
                }
            }
        }

        private void ProcessPhysicsUpdate()
        {
            lock (_syncRoot)
            {
                if (_pendingPhysicsUpdate)
                {
                    PerformPhysicsCalculations();
                    _pendingPhysicsUpdate = false;
                }
            }
        }

        private void PerformPhysicsCalculations()
        {
            _animationTime += TIME_STEP * _timeFactor;
            _physicsTimeAccumulator += TIME_STEP * _timeFactor;

            for (int step = 0; step < _substepsCount; step++)
            {
                if (_physicsTimeAccumulator >= PHYSICS_UPDATE_STEP)
                {
                    _physicsTimeAccumulator -= PHYSICS_UPDATE_STEP;
                }
            }
        }

        private void HandlePhysicsError(Exception ex)
        {
            logger.Log(LogLevel.Error, LogPrefix, $"Physics error: {ex.Message}");
            Thread.Sleep(100);
        }

        public void Dispose()
        {
            logger.Safe(
                () =>
                {
                    _physicsTaskRunning = false;
                    try
                    {
                        _physicsTask?.Wait(500);
                    }
                    catch { }

                    _physicsTask = null;
                    _forces = [];
                    _spectrumForPhysics = [];
                },
                LogPrefix,
                "Error stopping physics task"
            );
        }
    }

    private class WaterRenderState(ISmartLogger logger)
    {
        private const string LogPrefix = nameof(WaterRenderState);

        public SKPath FillPath { get; } = new();
        public SKPaint? PointPaint { get; private set; }
        public SKPaint? LinePaint { get; private set; }
        public SKPaint? FillPaint { get; private set; }
        public SKPaint? HighlightPaint { get; private set; }
        public SKPaint? ReflectionPaint { get; private set; }
        private SKShader? _waterShader;

        private float _currentWaterAlpha = WATER_ALPHA_BASE;
        private float _currentBrightness = 0.5f;
        private float _lastLoudness;

        public void InitializePaintResources(bool useAntiAlias)
        {
            PointPaint = CreatePointPaint(useAntiAlias);
            LinePaint = CreateLinePaint(useAntiAlias);
            FillPaint = CreateFillPaint(useAntiAlias);
            HighlightPaint = CreateHighlightPaint(useAntiAlias);
            ReflectionPaint = CreateReflectionPaint(useAntiAlias);
        }

        public void UpdateQualitySettings(bool useAntiAlias, bool useAdvancedEffects)
        {
            if (PointPaint != null)
                PointPaint.IsAntialias = useAntiAlias;

            if (LinePaint != null)
                LinePaint.IsAntialias = useAntiAlias;

            if (FillPaint != null)
                FillPaint.IsAntialias = useAntiAlias;

            if (HighlightPaint != null)
                HighlightPaint.IsAntialias = useAntiAlias;

            if (ReflectionPaint != null)
                ReflectionPaint.IsAntialias = useAntiAlias;
        }

        public bool AreRenderResourcesValid() =>
            PointPaint != null &&
            LinePaint != null &&
            FillPaint != null;

        public void InvalidateResources()
        {
            _waterShader?.Dispose();
            _waterShader = null;
        }

        public void Dispose()
        {
            FillPath?.Dispose();
            PointPaint?.Dispose();
            LinePaint?.Dispose();
            FillPaint?.Dispose();
            HighlightPaint?.Dispose();
            ReflectionPaint?.Dispose();
            _waterShader?.Dispose();

            PointPaint = null;
            LinePaint = null;
            FillPaint = null;
            HighlightPaint = null;
            ReflectionPaint = null;
            _waterShader = null;
        }

        private static SKPaint CreatePointPaint(bool useAntiAlias) =>
            new()
            {
                Color = SKColors.White,
                IsAntialias = useAntiAlias,
                Style = SKPaintStyle.StrokeAndFill,
                StrokeWidth = POINT_RADIUS
            };

        private static SKPaint CreateLinePaint(bool useAntiAlias) =>
            new()
            {
                Color = BASE_WATER_COLOR.WithAlpha(LINE_ALPHA),
                IsAntialias = useAntiAlias,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = LINE_WIDTH
            };

        private static SKPaint CreateFillPaint(bool useAntiAlias) =>
            new()
            {
                Color = BASE_WATER_COLOR,
                IsAntialias = useAntiAlias,
                Style = SKPaintStyle.Fill
            };

        private static SKPaint CreateHighlightPaint(bool useAntiAlias) =>
            new()
            {
                Color = HIGHLIGHT_COLOR,
                IsAntialias = useAntiAlias,
                Style = SKPaintStyle.Fill
            };

        private static SKPaint CreateReflectionPaint(bool useAntiAlias) =>
            new()
            {
                Color = SKColors.White.WithAlpha(REFLECTION_ALPHA),
                IsAntialias = useAntiAlias,
                Style = SKPaintStyle.Fill,
                BlendMode = SKBlendMode.SrcOver
            };

        public void UpdateVisualParameters(
            float[] spectrum,
            SKImageInfo info,
            bool useAdvancedEffects,
            bool updateShader)
        {
            logger.Safe(
                () => ProcessVisualParameterUpdate(spectrum, info, useAdvancedEffects, updateShader),
                LogPrefix,
                "Error updating visual parameters"
            );
        }

        private void ProcessVisualParameterUpdate(
            float[] spectrum,
            SKImageInfo info,
            bool useAdvancedEffects,
            bool updateShader)
        {
            UpdateLoudnessAndBrightness(spectrum);

            if (useAdvancedEffects && updateShader)
            {
                UpdateWaterShader(info);
            }
        }

        private void UpdateLoudnessAndBrightness(float[] spectrum)
        {
            float loudness = CalculateLoudness(spectrum);
            _lastLoudness = _lastLoudness * (1 - LOUDNESS_SMOOTHING) + loudness * LOUDNESS_SMOOTHING;

            float targetAlpha = WATER_ALPHA_BASE + _lastLoudness * (WATER_ALPHA_MAX - WATER_ALPHA_BASE);
            _currentWaterAlpha = MathF.Min(WATER_ALPHA_MAX, targetAlpha);

            float targetBrightness = 0.5f + _lastLoudness * 0.5f;
            _currentBrightness = _currentBrightness * (1 - BRIGHTNESS_SMOOTHING) +
                                targetBrightness * BRIGHTNESS_SMOOTHING;
        }

        private void UpdateWaterShader(SKImageInfo info)
        {
            _waterShader?.Dispose();
            _waterShader = CreateWaterShader(info.Width, info.Height, _currentWaterAlpha);
        }

        private static SKShader CreateWaterShader(float width, float height, float alpha)
        {
            // Настройка цветов градиента
            byte baseAlpha = (byte)alpha;
            byte deepAlpha = (byte)(alpha * 0.7f);

            var colors = new[]
            {
                BASE_WATER_COLOR.WithAlpha(baseAlpha),
                DEEP_WATER_COLOR.WithAlpha(deepAlpha)
            };

            // Создание градиента
            SKPoint startPoint = new(0, 0);
            SKPoint endPoint = new(0, height);

            return SKShader.CreateLinearGradient(
                startPoint,
                endPoint,
                colors,
                [0, 1],
                SKShaderTileMode.Clamp);
        }

        private static float CalculateLoudness(float[] spectrum)
        {
            if (spectrum == null || spectrum.Length == 0)
                return 0;

            float sum = 0;
            for (int i = 0; i < spectrum.Length; i++)
            {
                sum += spectrum[i];
            }

            return MathF.Min(1.0f, sum / spectrum.Length * 4.0f);
        }

        private static SKColor ShiftHue(SKColor color, float shift)
        {
            color.ToHsl(out float h, out float s, out float l);
            h = (h + shift) % 360;
            if (h < 0) h += 360;
            return SKColor.FromHsl(h, s, l);
        }

        public void ConfigureRenderingPaints(SKPaint externalPaint, bool useAdvancedEffects, float animationTime)
        {
            SKColor baseColor = externalPaint.Color;

            if (useAdvancedEffects)
            {
                float hueShift = Sin(animationTime * COLOR_SHIFT_SPEED) * 10;
                baseColor = ShiftHue(baseColor, hueShift);
            }

            UpdatePointPaint(baseColor);
            UpdateLinePaint(baseColor);
            UpdateFillPaint(baseColor, useAdvancedEffects, animationTime);
            UpdateHighlightPaint();
            UpdateReflectionPaint(baseColor);
        }

        private void UpdatePointPaint(SKColor baseColor)
        {
            if (PointPaint != null)
            {
                PointPaint.Color = baseColor;
            }
        }

        private void UpdateLinePaint(SKColor baseColor)
        {
            if (LinePaint != null)
            {
                LinePaint.Color = baseColor.WithAlpha(LINE_ALPHA);
            }
        }

        private void UpdateFillPaint(SKColor baseColor, bool useAdvancedEffects, float animationTime)
        {
            if (FillPaint == null) return;

            if (useAdvancedEffects && _waterShader != null)
            {
                float phase = animationTime * WAVE_SPEED;

                // Создание матрицы вращения
                SKMatrix rotationMatrix = SKMatrix.CreateRotationDegrees(MathF.Sin(phase) * 2);

                // Создание матрицы масштабирования
                float scaleX = 1 + MathF.Sin(phase) * WAVE_AMPLITUDE * _lastLoudness;
                float scaleY = 1 + MathF.Cos(phase) * WAVE_AMPLITUDE * _lastLoudness;
                SKMatrix scaleMatrix = SKMatrix.CreateScale(scaleX, scaleY);

                // Объединение матриц
                SKMatrix finalMatrix = rotationMatrix.PostConcat(scaleMatrix);

                // Применение шейдера с матрицей
                FillPaint.Shader = _waterShader.WithLocalMatrix(finalMatrix);
            }
            else
            {
                FillPaint.Shader = null;
                FillPaint.Color = baseColor.WithAlpha((byte)_currentWaterAlpha);
            }
        }

        private void UpdateHighlightPaint()
        {
            if (HighlightPaint != null)
            {
                HighlightPaint.Color = HIGHLIGHT_COLOR;
            }
        }

        private void UpdateReflectionPaint(SKColor baseColor)
        {
            if (ReflectionPaint != null)
            {
                ReflectionPaint.Color = baseColor.WithAlpha(REFLECTION_ALPHA);
            }
        }

        public void BuildWaterSurfaceRowPath(int y, WaterGridState gridState)
        {
            FillPath.Reset();

            int startIndex = y * gridState.Columns;
            int endRowIndex = (y + 1) * gridState.Columns;
            var points = gridState.Points;

            if (startIndex >= points.Count || endRowIndex >= points.Count)
                return;

            CreateTopRowPath(y, gridState, points);
            CreateBottomRowPath(y, gridState, points);

            FillPath.Close();
        }

        private void CreateTopRowPath(int y, WaterGridState gridState, List<WaterPoint> points)
        {
            int startIndex = y * gridState.Columns;

            if (startIndex < points.Count)
            {
                FillPath.MoveTo(points[startIndex].Position);

                for (int x = 1; x < gridState.Columns && startIndex + x < points.Count; x++)
                {
                    FillPath.LineTo(points[startIndex + x].Position);
                }
            }
        }

        private void CreateBottomRowPath(int y, WaterGridState gridState, List<WaterPoint> points)
        {
            int bottomRowStart = (y + 1) * gridState.Columns;

            if (bottomRowStart < points.Count)
            {
                for (int x = gridState.Columns - 1; x >= 0 && bottomRowStart + x < points.Count; x--)
                {
                    FillPath.LineTo(points[bottomRowStart + x].Position);
                }
            }
        }
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