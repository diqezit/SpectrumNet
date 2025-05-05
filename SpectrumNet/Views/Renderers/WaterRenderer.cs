#nullable enable

using static System.MathF;
using static SpectrumNet.Views.Renderers.WaterRenderer.Constants;

namespace SpectrumNet.Views.Renderers;

public sealed class WaterRenderer : EffectSpectrumRenderer
{
    private static readonly Lazy<WaterRenderer> _instance = new(() => new WaterRenderer());

    private readonly List<WaterPoint> _points = [];
    private readonly SKPath _fillPath = new();
    private readonly Random _random = new();
    private readonly object _syncRoot = new();
    private readonly TaskFactory _physicsTaskFactory;
    private readonly Stopwatch _frameTimeStopwatch = new();
    private readonly bool _adaptivePhysics = true;
    private readonly int[] _qualityBasedPointCount = [400, 800, 1600];
    private readonly int _updateFrameSkip = 3;
    private readonly float _spectrumImpactFactor;

    private float[]? _lastSpectrum;
    private Vector2[] _forces = [];
    private SKPaint? _pointPaint;
    private SKPaint? _linePaint;
    private SKPaint? _fillPaint;
    private SKPaint? _highlightPaint;
    private SKPaint? _reflectionPaint;
    private SKShader? _waterShader;

    private int _columns;
    private int _rows;
    private float _spacing;
    private new bool _useAntiAlias = true;
    private float _timeFactor = 1.0f;
    private new bool _useAdvancedEffects = true;
    private new bool _isOverlayActive;
    private float _physicsTimeAccumulator;
    private bool _needsGridRebuild = true;
    private float _animationTime;
    private float _lastBarWidth;
    private float _lastBarSpacing;
    private int _lastBarCount;
    private SKImageInfo _lastImageInfo;
    private int _currentFrame;
    private float _xOffset;
    private float _yOffset;
    private bool _showConnections;
    private Task? _physicsTask;
    private bool _physicsTaskRunning;
    private bool _pendingPhysicsUpdate;
    private float[] _spectrumForPhysics = [];
    private float _avgFrameTime = 0.016f;
    private float _currentWaterAlpha = WATER_ALPHA_BASE;
    private float _currentBrightness = 0.5f;
    private float _lastLoudness;

    private WaterRenderer()
    {
        _spectrumImpactFactor = SPECTRUM_IMPACT_MIN;
        _physicsTaskFactory = new TaskFactory(
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskContinuationOptions.None,
            TaskScheduler.Default);
    }

    public static WaterRenderer GetInstance() => _instance.Value;

    public record Constants
    {
        public const string LOG_PREFIX = "WaterRenderer";

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

        public const float
            NEIGHBOR_FORCE = 0.1f,
            SPRING_FORCE = 0.03f,
            DAMPING = 1.05f,
            TIME_STEP = 0.05f,
            PHYSICS_UPDATE_STEP = 0.016f,
            PHYSICS_SUBSTEPS = 3;

        public const float
            INTERACTION_RADIUS = 7700f,
            ATTRACTION_FORCE = 0.02f,
            REPULSION_FORCE = 0.2f,
            SPECTRUM_IMPACT_MIN = 0.05f,
            SPECTRUM_IMPACT_MAX = 0.3f;

        public const int
            CONNECTIONS_THRESHOLD = 500,
            POINT_RENDERING_THRESHOLD = 800,
            ROW_RENDERING_THRESHOLD = 20;

        public const byte
            LINE_ALPHA = 100,
            WATER_ALPHA_BASE = 40,
            WATER_ALPHA_MAX = 80,
            HIGHLIGHT_ALPHA_BASE = 180,
            REFLECTION_ALPHA = 60;

        public const float
            HIGHLIGHT_SIZE_FACTOR = 0.5f,
            HIGHLIGHT_OFFSET_FACTOR = 0.25f,
            REFLECTION_HEIGHT_FACTOR = 0.2f,
            REFLECTION_WIDTH_FACTOR = 0.8f,
            WAVE_SPEED = 0.5f,
            WAVE_AMPLITUDE = 0.2f,
            LOUDNESS_SMOOTHING = 0.2f,
            BRIGHTNESS_SMOOTHING = 0.1f,
            ADAPTIVE_PERFORMANCE_THRESHOLD = 0.025f,
            FRAME_TIME_SMOOTHING = 0.1f,
            MIN_AMPLITUDE_THRESHOLD = 0.05f,
            COLOR_SHIFT_SPEED = 0.1f;

        public static readonly SKColor
            BASE_WATER_COLOR = new(0, 120, 255, WATER_ALPHA_BASE),
            DEEP_WATER_COLOR = new(0, 40, 150, WATER_ALPHA_BASE),
            HIGHLIGHT_COLOR = new(255, 255, 255, HIGHLIGHT_ALPHA_BASE);

        public static class Quality
        {
            public const bool
                LOW_SHOW_CONNECTIONS = false,
                MEDIUM_SHOW_CONNECTIONS = false,
                HIGH_SHOW_CONNECTIONS = true;

            public const bool
                LOW_USE_ADVANCED_EFFECTS = false,
                MEDIUM_USE_ADVANCED_EFFECTS = true,
                HIGH_USE_ADVANCED_EFFECTS = true;

            public const bool
                LOW_USE_ANTIALIASING = false,
                MEDIUM_USE_ANTIALIASING = true,
                HIGH_USE_ANTIALIASING = true;

            public const float
                LOW_TIME_FACTOR = 0.8f,
                MEDIUM_TIME_FACTOR = 1.0f,
                HIGH_TIME_FACTOR = 1.2f;
        }
    }

    protected override void OnInitialize() =>
        ExecuteSafely(
            PerformInitialization,
            "OnInitialize",
            "Failed during renderer initialization"
        );

    private void PerformInitialization()
    {
        base.OnInitialize();
        InitializeResources();
        StartPhysicsTask();
        Log(LogLevel.Debug, LOG_PREFIX, "Initialized");
    }

    private void InitializeResources()
    {
        _frameTimeStopwatch.Start();
        InitializeGridParameters();
        InitializePaintResources();
        _needsGridRebuild = true;
    }

    private void InitializeGridParameters()
    {
        _columns = DEFAULT_COLUMNS;
        _rows = DEFAULT_ROWS;
        _spacing = DEFAULT_SPACING;
    }

    private void InitializePaintResources()
    {
        _pointPaint = CreatePointPaint();
        _linePaint = CreateLinePaint();
        _fillPaint = CreateFillPaint();
        _highlightPaint = CreateHighlightPaint();
        _reflectionPaint = CreateReflectionPaint();
    }

    private static SKPaint CreatePointPaint() =>
        new()
        {
            Color = SKColors.White,
            IsAntialias = true,
            Style = SKPaintStyle.StrokeAndFill,
            StrokeWidth = POINT_RADIUS
        };

    private static SKPaint CreateLinePaint() =>
        new()
        {
            Color = BASE_WATER_COLOR.WithAlpha(LINE_ALPHA),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = LINE_WIDTH
        };

    private static SKPaint CreateFillPaint() =>
        new()
        {
            Color = BASE_WATER_COLOR,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

    private static SKPaint CreateHighlightPaint() =>
        new()
        {
            Color = HIGHLIGHT_COLOR,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

    private static SKPaint CreateReflectionPaint() =>
        new()
        {
            Color = SKColors.White.WithAlpha(REFLECTION_ALPHA),
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            BlendMode = SKBlendMode.SrcOver
        };

    private static SKShader CreateWaterShader(float width, float height, float alpha)
    {
        var colors = new[]
        {
            BASE_WATER_COLOR.WithAlpha((byte)alpha),
            DEEP_WATER_COLOR.WithAlpha((byte)(alpha * 0.7f))
        };

        return SKShader.CreateLinearGradient(
            new SKPoint(0, 0),
            new SKPoint(0, height),
            colors,
            [0, 1],
            SKShaderTileMode.Clamp);
    }

    private void StartPhysicsTask()
    {
        if (_physicsTask != null) return;

        _physicsTaskRunning = true;
        _physicsTask = _physicsTaskFactory.StartNew(RunPhysicsLoop);
    }

    private void RunPhysicsLoop()
    {
        while (_physicsTaskRunning && !_disposed)
        {
            try
            {
                PhysicsLoopIteration();
                Thread.Sleep(5);
            }
            catch (Exception ex)
            {
                Log(LogLevel.Error, LOG_PREFIX, $"Physics error: {ex.Message}");
                Thread.Sleep(100);
            }
        }
    }

    private void PhysicsLoopIteration()
    {
        if (!_pendingPhysicsUpdate || _points.Count == 0) return;

        lock (_syncRoot)
        {
            if (!_pendingPhysicsUpdate) return;

            float[] spectrum = _spectrumForPhysics;

            _animationTime += TIME_STEP * _timeFactor;
            AccumulatePhysicsTime();
            ProcessPhysicsSteps(spectrum);

            _pendingPhysicsUpdate = false;
        }
    }

    private void SchedulePhysicsUpdate(float[] spectrum)
    {
        int length = spectrum.Length;
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

        _pendingPhysicsUpdate = true;
    }

    private void StopPhysicsTask()
    {
        _physicsTaskRunning = false;
        try
        {
            _physicsTask?.Wait(500);
        }
        catch { }

        _physicsTask = null;
    }

    public override void Configure(
        bool isOverlayActive,
        RenderQuality quality = RenderQuality.Medium) =>
        ExecuteSafely(
            () => PerformConfiguration(isOverlayActive, quality),
            "Configure",
            "Failed to configure renderer"
        );

    private void PerformConfiguration(
        bool isOverlayActive,
        RenderQuality quality)
    {
        bool configChanged = _isOverlayActive != isOverlayActive || Quality != quality;
        base.Configure(isOverlayActive, quality);
        _isOverlayActive = isOverlayActive;

        if (configChanged)
        {
            ApplyQualitySpecificSettings();
            _needsGridRebuild = true;
        }
    }

    protected override void OnQualitySettingsApplied() =>
        ExecuteSafely(
            PerformQualitySettingsApplication,
            "OnQualitySettingsApplied",
            "Failed to apply specific quality settings"
        );

    private void PerformQualitySettingsApplication()
    {
        base.OnQualitySettingsApplied();
        ApplyQualitySpecificSettings();
    }

    private void ApplyQualitySpecificSettings()
    {
        ApplyQualityBasedParameters();
        UpdatePaintProperties();
        _needsGridRebuild = true;
    }

    private void ApplyQualityBasedParameters()
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
    }

    private void ApplyLowQualitySettings()
    {
        _useAntiAlias = Constants.Quality.LOW_USE_ANTIALIASING;
        _showConnections = Constants.Quality.LOW_SHOW_CONNECTIONS;
        _useAdvancedEffects = Constants.Quality.LOW_USE_ADVANCED_EFFECTS;
        _timeFactor = Constants.Quality.LOW_TIME_FACTOR;
    }

    private void ApplyMediumQualitySettings()
    {
        _useAntiAlias = Constants.Quality.MEDIUM_USE_ANTIALIASING;
        _showConnections = Constants.Quality.MEDIUM_SHOW_CONNECTIONS;
        _useAdvancedEffects = Constants.Quality.MEDIUM_USE_ADVANCED_EFFECTS;
        _timeFactor = Constants.Quality.MEDIUM_TIME_FACTOR;
    }

    private void ApplyHighQualitySettings()
    {
        _useAntiAlias = Constants.Quality.HIGH_USE_ANTIALIASING;
        _showConnections = Constants.Quality.HIGH_SHOW_CONNECTIONS;
        _useAdvancedEffects = Constants.Quality.HIGH_USE_ADVANCED_EFFECTS;
        _timeFactor = Constants.Quality.HIGH_TIME_FACTOR;
    }

    private void UpdatePaintProperties()
    {
        if (_pointPaint != null && _linePaint != null && _fillPaint != null)
        {
            _pointPaint.IsAntialias = _useAntiAlias;
            _linePaint.IsAntialias = _useAntiAlias;
            _fillPaint.IsAntialias = _useAntiAlias;

            if (_highlightPaint != null)
                _highlightPaint.IsAntialias = _useAntiAlias;

            if (_reflectionPaint != null)
                _reflectionPaint.IsAntialias = _useAntiAlias;
        }
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
        {
            return;
        }

        ExecuteSafely(
            () => PerformRender(
                canvas,
                spectrum,
                info,
                barWidth,
                barSpacing,
                barCount,
                paint),
            "RenderEffect",
            "Error during rendering"
        );

        UpdateFrameTimeMeasurement();
    }

    private void PerformRender(
        SKCanvas canvas,
        float[] spectrum,
        SKImageInfo info,
        float barWidth,
        float barSpacing,
        int barCount,
        SKPaint paint)
    {
        BuildWaterGrid(
            info.Width,
            info.Height,
            barWidth,
            barSpacing,
            barCount);

        UpdatePhysicsIfNeeded(spectrum);
        UpdateVisualParameters(spectrum, info);
        RenderWaterGrid(canvas, paint);
    }

    private void UpdatePhysicsIfNeeded(float[] spectrum)
    {
        IncrementFrameCounter();
        _lastSpectrum = spectrum;

        if (_currentFrame == 0)
        {
            SchedulePhysicsUpdate(spectrum);
        }
    }

    private void IncrementFrameCounter() =>
        _currentFrame = (_currentFrame + 1) % _updateFrameSkip;

    private void UpdateFrameTimeMeasurement()
    {
        float elapsed = (float)_frameTimeStopwatch.Elapsed.TotalSeconds;
        _frameTimeStopwatch.Restart();

        _avgFrameTime = _avgFrameTime * (1 - FRAME_TIME_SMOOTHING) + elapsed * FRAME_TIME_SMOOTHING;

        if (_adaptivePhysics && _currentFrame == 0)
        {
            if (_avgFrameTime > ADAPTIVE_PERFORMANCE_THRESHOLD)
            {
                _needsGridRebuild = true;
            }
        }
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

        if (_useAdvancedEffects && _currentFrame == 0)
        {
            _waterShader?.Dispose();
            _waterShader = CreateWaterShader(info.Width, info.Height, _currentWaterAlpha);
        }
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

    private void RenderWaterGrid(SKCanvas canvas, SKPaint externalPaint)
    {
        if (!AreRenderResourcesValid())
        {
            return;
        }

        ConfigureRenderingPaints(externalPaint);
        RenderWaterGridElements(canvas);
    }

    private bool AreRenderResourcesValid() =>
        _points.Count > 0 &&
        _pointPaint != null &&
        _linePaint != null &&
        _fillPaint != null;

    private void ConfigureRenderingPaints(SKPaint externalPaint)
    {
        SKColor baseColor = externalPaint.Color;

        if (_useAdvancedEffects)
        {
            float hueShift = Sin(_animationTime * COLOR_SHIFT_SPEED) * 10;
            baseColor = ShiftHue(baseColor, hueShift);
        }

        UpdatePointPaint(baseColor);
        UpdateLinePaint(baseColor);
        UpdateFillPaint(baseColor);
        UpdateHighlightPaint();
        UpdateReflectionPaint(baseColor);
    }

    private void UpdatePointPaint(SKColor baseColor)
    {
        if (_pointPaint != null)
        {
            _pointPaint.Color = baseColor;
        }
    }

    private void UpdateLinePaint(SKColor baseColor)
    {
        if (_linePaint != null)
        {
            _linePaint.Color = baseColor.WithAlpha(LINE_ALPHA);
        }
    }

    private void UpdateFillPaint(SKColor baseColor)
    {
        if (_fillPaint != null)
        {
            if (_useAdvancedEffects && _waterShader != null)
            {
                float phase = _animationTime * WAVE_SPEED;
                SKMatrix matrix = SKMatrix.CreateRotationDegrees(Sin(phase) * 2);
                matrix = matrix.PostConcat(SKMatrix.CreateScale(1 + Sin(phase) * WAVE_AMPLITUDE * _lastLoudness,
                                                               1 + Cos(phase) * WAVE_AMPLITUDE * _lastLoudness));
                _fillPaint.Shader = _waterShader.WithLocalMatrix(matrix);
            }
            else
            {
                _fillPaint.Shader = null;
                _fillPaint.Color = baseColor.WithAlpha((byte)_currentWaterAlpha);
            }
        }
    }

    private void UpdateHighlightPaint()
    {
        if (_highlightPaint != null)
        {
            _highlightPaint.Color = HIGHLIGHT_COLOR;
        }
    }

    private void UpdateReflectionPaint(SKColor baseColor)
    {
        if (_reflectionPaint != null)
        {
            _reflectionPaint.Color = baseColor.WithAlpha(REFLECTION_ALPHA);
        }
    }

    private void RenderWaterGridElements(SKCanvas canvas)
    {
        if (_useAdvancedEffects && _fillPaint != null)
        {
            DrawWaterSurface(canvas);
        }

        if (_showConnections)
        {
            RenderConnections(canvas);
        }

        RenderPoints(canvas);

        if (_useAdvancedEffects)
        {
            RenderHighlightsAndReflections(canvas);
        }
    }

    private void RenderConnections(SKCanvas canvas)
    {
        if (_points.Count > CONNECTIONS_THRESHOLD) return;

        for (int i = 0; i < _points.Count; i++)
        {
            var point = _points[i];
            foreach (var neighbor in point.VisualNeighbors)
            {
                canvas.DrawLine(
                    point.Position,
                    neighbor.Position,
                    _linePaint!);
            }
        }
    }

    private void RenderPoints(SKCanvas canvas)
    {
        int stride = _points.Count > POINT_RENDERING_THRESHOLD ? 2 : 1;

        for (int i = 0; i < _points.Count; i += stride)
        {
            canvas.DrawCircle(
                _points[i].Position,
                POINT_RADIUS,
                _pointPaint!);
        }
    }

    private void RenderHighlightsAndReflections(SKCanvas canvas)
    {
        if (_highlightPaint == null || _reflectionPaint == null)
            return;

        int stride = _points.Count > POINT_RENDERING_THRESHOLD ? 3 : 2;

        for (int i = 0; i < _points.Count; i += stride)
        {
            var point = _points[i];
            float highlightSize = POINT_RADIUS * HIGHLIGHT_SIZE_FACTOR;
            float animOffset = Sin(_animationTime * WAVE_SPEED + i * 0.1f) * 0.3f + 0.7f;

            canvas.DrawCircle(
                point.Position.X - POINT_RADIUS * HIGHLIGHT_OFFSET_FACTOR,
                point.Position.Y - POINT_RADIUS * HIGHLIGHT_OFFSET_FACTOR,
                highlightSize * animOffset,
                _highlightPaint);

            if (i >= _points.Count - _columns)
            {
                float reflectionWidth = POINT_RADIUS * REFLECTION_WIDTH_FACTOR;
                float reflectionHeight = POINT_RADIUS * REFLECTION_HEIGHT_FACTOR;

                SKRect reflectionRect = new(
                    point.Position.X - reflectionWidth,
                    point.Position.Y + POINT_RADIUS,
                    point.Position.X + reflectionWidth,
                    point.Position.Y + POINT_RADIUS + reflectionHeight);

                canvas.DrawOval(reflectionRect, _reflectionPaint);
            }
        }
    }

    private void DrawWaterSurface(SKCanvas canvas)
    {
        int rowStride = _rows > ROW_RENDERING_THRESHOLD ? 2 : 1;

        for (int y = 0; y < _rows - 1; y += rowStride)
        {
            DrawWaterSurfaceRow(canvas, y);
        }
    }

    private void DrawWaterSurfaceRow(SKCanvas canvas, int y)
    {
        BuildWaterSurfaceRowPath(y);
        canvas.DrawPath(_fillPath, _fillPaint!);
    }

    private void BuildWaterSurfaceRowPath(int y)
    {
        _fillPath.Reset();

        int startIndex = y * _columns;
        int endRowIndex = (y + 1) * _columns;

        if (startIndex >= _points.Count || endRowIndex >= _points.Count)
            return;

        CreateTopRowPath(y);
        CreateBottomRowPath(y);

        _fillPath.Close();
    }

    private void CreateTopRowPath(int y)
    {
        int startIndex = y * _columns;

        if (startIndex < _points.Count)
        {
            _fillPath.MoveTo(_points[startIndex].Position);

            for (int x = 1; x < _columns && startIndex + x < _points.Count; x++)
            {
                _fillPath.LineTo(_points[startIndex + x].Position);
            }
        }
    }

    private void CreateBottomRowPath(int y)
    {
        int bottomRowStart = (y + 1) * _columns;

        if (bottomRowStart < _points.Count)
        {
            for (int x = _columns - 1; x >= 0 && bottomRowStart + x < _points.Count; x--)
            {
                _fillPath.LineTo(_points[bottomRowStart + x].Position);
            }
        }
    }

    private static bool ValidateRenderParameters(
        SKCanvas? canvas,
        float[]? spectrum,
        SKImageInfo info,
        SKPaint? paint) =>
        canvas != null &&
        spectrum != null &&
        spectrum.Length > 0 &&
        paint != null &&
        info.Width > 0 &&
        info.Height > 0;

    private void BuildWaterGrid(
        float width,
        float height,
        float barWidth,
        float barSpacing,
        int barCount)
    {
        if (IsGridUpToDate(width, height, barWidth, barSpacing, barCount))
        {
            return;
        }

        UpdateGridParameters(width, height, barWidth, barSpacing, barCount);
        CalculateAdaptiveGridDimensions(width, height);
        ClearCurrentGrid();
        CreateGridPoints();
        ConnectGridPoints();
        InitializeForces();

        _needsGridRebuild = false;
    }

    private bool IsGridUpToDate(
        float width,
        float height,
        float barWidth,
        float barSpacing,
        int barCount) =>
        !_needsGridRebuild &&
        _lastBarWidth == barWidth &&
        _lastBarSpacing == barSpacing &&
        _lastBarCount == barCount &&
        _lastImageInfo.Width == width &&
        _lastImageInfo.Height == height;

    private void UpdateGridParameters(
        float width,
        float height,
        float barWidth,
        float barSpacing,
        int barCount)
    {
        _lastBarWidth = barWidth;
        _lastBarSpacing = barSpacing;
        _lastBarCount = barCount;
        _lastImageInfo = new SKImageInfo((int)width, (int)height);

        _waterShader?.Dispose();
        _waterShader = CreateWaterShader(width, height, _currentWaterAlpha);
    }

    private void CalculateAdaptiveGridDimensions(
        float width,
        float height)
    {
        int maxPoints = GetMaxPointsForQuality();

        _columns = Min(_lastBarCount, 40);

        float aspectRatio = height / width;
        int proposedRows = Max(2, (int)(_columns * aspectRatio));

        int totalPoints = _columns * proposedRows;
        if (totalPoints > maxPoints)
        {
            float scaleFactor = Sqrt((float)maxPoints / totalPoints);
            _columns = Max(3, (int)(_columns * scaleFactor));
            proposedRows = Max(2, (int)(proposedRows * scaleFactor));
        }

        _rows = proposedRows;
        _spacing = width / _columns;

        _xOffset = (width - (_columns - 1) * _spacing) / 2;
        _yOffset = (height - (_rows - 1) * _spacing) / 2;
    }

    private int GetMaxPointsForQuality()
    {
        if (_adaptivePhysics)
        {
            float targetFrameTime = 1.0f / 60.0f;
            float loadFactor = MathF.Min(3.0f, MathF.Max(0.5f, _avgFrameTime / targetFrameTime));

            int basePoints = _qualityBasedPointCount[(int)Quality];
            return (int)(basePoints / loadFactor);
        }

        return _qualityBasedPointCount[(int)Quality];
    }

    private void ClearCurrentGrid()
    {
        _points.Clear();
        _points.Capacity = _rows * _columns;
    }

    private void CreateGridPoints()
    {
        for (int y = 0; y < _rows; y++)
        {
            CreateGridPointsRow(y);
        }
    }

    private void CreateGridPointsRow(int y)
    {
        for (int x = 0; x < _columns; x++)
        {
            float xPos = _xOffset + x * _spacing;
            float yPos = _yOffset + y * _spacing;
            _points.Add(new WaterPoint(xPos, yPos));
        }
    }

    private void InitializeForces() =>
        _forces = new Vector2[_points.Count];

    private void ConnectGridPoints()
    {
        if (_points.Count > 400 && _rows > 2 && _columns > 2)
        {
            Parallel.For(0, _rows, y =>
            {
                for (int x = 0; x < _columns; x++)
                {
                    ConnectPointAtPosition(x, y);
                }
            });
        }
        else
        {
            for (int y = 0; y < _rows; y++)
            {
                for (int x = 0; x < _columns; x++)
                {
                    ConnectPointAtPosition(x, y);
                }
            }
        }
    }

    private void ConnectPointAtPosition(int x, int y)
    {
        int index = y * _columns + x;
        if (index >= _points.Count) return;

        var point = _points[index];

        ConnectToCardinalNeighbors(x, y, point);

        if (_useAdvancedEffects)
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
            int neighborIndex = y * _columns + (x - 1);
            if (neighborIndex < _points.Count)
            {
                var leftNeighbor = _points[neighborIndex];
                point.Neighbors.Add(leftNeighbor);
                point.VisualNeighbors.Add(leftNeighbor);
            }
        }
    }

    private void ConnectToTopNeighbor(int x, int y, WaterPoint point)
    {
        if (y > 0)
        {
            int neighborIndex = (y - 1) * _columns + x;
            if (neighborIndex < _points.Count)
            {
                var topNeighbor = _points[neighborIndex];
                point.Neighbors.Add(topNeighbor);
                point.VisualNeighbors.Add(topNeighbor);
            }
        }
    }

    private void ConnectToRightNeighbor(int x, int y, WaterPoint point)
    {
        if (x < _columns - 1)
        {
            int neighborIndex = y * _columns + (x + 1);
            if (neighborIndex < _points.Count)
            {
                var rightNeighbor = _points[neighborIndex];
                point.Neighbors.Add(rightNeighbor);
            }
        }
    }

    private void ConnectToBottomNeighbor(int x, int y, WaterPoint point)
    {
        if (y < _rows - 1)
        {
            int neighborIndex = (y + 1) * _columns + x;
            if (neighborIndex < _points.Count)
            {
                var bottomNeighbor = _points[neighborIndex];
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
            int neighborIndex = (y - 1) * _columns + (x - 1);
            if (neighborIndex < _points.Count)
            {
                var diagNeighbor = _points[neighborIndex];
                point.Neighbors.Add(diagNeighbor);
            }
        }
    }

    private void ConnectToTopRightNeighbor(int x, int y, WaterPoint point)
    {
        if (x < _columns - 1 && y > 0)
        {
            int neighborIndex = (y - 1) * _columns + (x + 1);
            if (neighborIndex < _points.Count)
            {
                var diagNeighbor = _points[neighborIndex];
                point.Neighbors.Add(diagNeighbor);
            }
        }
    }

    private void AccumulatePhysicsTime() =>
        _physicsTimeAccumulator += TIME_STEP * _timeFactor;

    private void ProcessPhysicsSteps(float[] spectrum)
    {
        while (_physicsTimeAccumulator >= PHYSICS_UPDATE_STEP)
        {
            ExecutePhysicsSubstep(spectrum);
            _physicsTimeAccumulator -= PHYSICS_UPDATE_STEP;
        }
    }

    private void ExecutePhysicsSubstep(float[] spectrum)
    {
        CalculateForces(spectrum);
        ApplyForcesToPoints();
    }

    private void CalculateForces(float[] spectrum)
    {
        if (_points.Count > 500)
        {
            Parallel.For(0, _points.Count, i =>
            {
                CalculateForceForPoint(i, spectrum);
            });
        }
        else
        {
            for (int i = 0; i < _points.Count; i++)
            {
                CalculateForceForPoint(i, spectrum);
            }
        }
    }

    private void CalculateForceForPoint(int pointIndex, float[] spectrum)
    {
        var point = _points[pointIndex];
        _forces[pointIndex] = Vector2.Zero;

        CalculateNeighborForces(pointIndex, point);
        CalculateSpringForce(pointIndex, point);

        if (spectrum != null && spectrum.Length > 0)
        {
            CalculateSpectrumForces(pointIndex, point, spectrum);
        }

        if (_useAdvancedEffects)
        {
            CalculateWaveForces(pointIndex);
        }
    }

    private void CalculateWaveForces(int pointIndex)
    {
        float x = pointIndex % _columns;
        float y = pointIndex / _columns;
        float normalizedX = x / _columns;
        float normalizedY = y / _rows;

        float distFromCenter = Sqrt(
            Pow(normalizedX - 0.5f, 2) +
            Pow(normalizedY - 0.5f, 2));

        float wavePhase = _animationTime * WAVE_SPEED + distFromCenter * 10;
        float waveFactor = Sin(wavePhase) * WAVE_AMPLITUDE * 0.1f;

        float dirX = normalizedX - 0.5f;
        float dirY = normalizedY - 0.5f;

        if (MathF.Abs(dirX) > 0.001f || MathF.Abs(dirY) > 0.001f)
        {
            float length = Sqrt(dirX * dirX + dirY * dirY);
            dirX /= length;
            dirY /= length;

            _forces[pointIndex] += new Vector2(
                dirX * waveFactor,
                dirY * waveFactor
            );
        }
    }

    private void CalculateNeighborForces(int pointIndex, WaterPoint point)
    {
        foreach (var neighbor in point.Neighbors)
        {
            CalculateNeighborForce(pointIndex, point, neighbor);
        }
    }

    private void CalculateNeighborForce(
        int pointIndex,
        WaterPoint point,
        WaterPoint neighbor)
    {
        float distance = SKPoint.Distance(point.Position, neighbor.Position);
        float targetDistance = SKPoint.Distance(
            point.OriginalPosition,
            neighbor.OriginalPosition);

        float factor = (distance - targetDistance) / targetDistance;
        Vector2 direction = CalculateDirection(point, neighbor);

        if (direction != Vector2.Zero)
        {
            Vector2 force = direction * factor * NEIGHBOR_FORCE;
            _forces[pointIndex] += force;
        }
    }

    private static Vector2 CalculateDirection(WaterPoint point, WaterPoint neighbor)
    {
        float dirX = neighbor.Position.X - point.Position.X;
        float dirY = neighbor.Position.Y - point.Position.Y;
        float lengthSquared = dirX * dirX + dirY * dirY;

        if (lengthSquared < 0.0001f)
            return Vector2.Zero;

        float length = Sqrt(lengthSquared);
        return new Vector2(dirX / length, dirY / length);
    }

    private void CalculateSpringForce(int pointIndex, WaterPoint point)
    {
        float springX = (point.OriginalPosition.X - point.Position.X) * SPRING_FORCE;
        float springY = (point.OriginalPosition.Y - point.Position.Y) * SPRING_FORCE;

        _forces[pointIndex] += new Vector2(springX, springY);
    }

    private void CalculateSpectrumForces(
        int pointIndex,
        WaterPoint point,
        float[] spectrum)
    {
        int spectrumIndex = CalculateSpectrumIndex(pointIndex, spectrum.Length);
        float amplitude = spectrum[spectrumIndex];

        if (amplitude <= MIN_AMPLITUDE_THRESHOLD)
        {
            return;
        }

        CalculateVerticalSpectrumForce(pointIndex, amplitude);

        if (_useAdvancedEffects)
        {
            CalculateHorizontalSpectrumForce(pointIndex, amplitude);
        }
    }

    private int CalculateSpectrumIndex(int pointIndex, int spectrumLength) =>
        (pointIndex * spectrumLength / _points.Count) % spectrumLength;

    private void CalculateVerticalSpectrumForce(int pointIndex, float amplitude)
    {
        float columnIndex = pointIndex % _columns;
        float force = amplitude * _spectrumImpactFactor;
        float distanceFromCenter = MathF.Abs(columnIndex - _columns / 2f) / (_columns / 2f);

        force *= (1.0f - distanceFromCenter);
        _forces[pointIndex] += new Vector2(0, -force * 10);
    }

    private void CalculateHorizontalSpectrumForce(int pointIndex, float amplitude)
    {
        float columnIndex = pointIndex % _columns;
        float force = amplitude * _spectrumImpactFactor;
        float horizontalForce = (columnIndex / _columns - 0.5f) * force * 2;

        _forces[pointIndex] += new Vector2(horizontalForce, 0);
    }

    private void ApplyForcesToPoints()
    {
        for (int i = 0; i < _points.Count; i++)
        {
            ApplyForceToPoint(i);
        }
    }

    private void ApplyForceToPoint(int pointIndex)
    {
        var point = _points[pointIndex];

        point.Velocity = new SKPoint(
            point.Velocity.X + _forces[pointIndex].X,
            point.Velocity.Y + _forces[pointIndex].Y
        );

        point.Position = new SKPoint(
            point.Position.X + point.Velocity.X,
            point.Position.Y + point.Velocity.Y
        );

        point.Velocity = new SKPoint(
            point.Velocity.X / DAMPING,
            point.Velocity.Y / DAMPING
        );
    }

    protected override void OnDispose() =>
        ExecuteSafely(
            PerformDispose,
            "OnDispose",
            "Error during specific disposal"
        );

    private void PerformDispose()
    {
        StopPhysicsTask();
        DisposeManagedResources();
        base.OnDispose();
    }

    private void DisposeManagedResources()
    {
        DisposeGraphicsResources();
        ClearData();
    }

    private void DisposeGraphicsResources()
    {
        _fillPath?.Dispose();
        _pointPaint?.Dispose();
        _linePaint?.Dispose();
        _fillPaint?.Dispose();
        _highlightPaint?.Dispose();
        _reflectionPaint?.Dispose();
        _waterShader?.Dispose();
    }

    private void ClearData()
    {
        _pointPaint = null;
        _linePaint = null;
        _fillPaint = null;
        _highlightPaint = null;
        _reflectionPaint = null;
        _waterShader = null;
        _lastSpectrum = null;
        _forces = [];
        _points.Clear();
    }

    public override void Dispose()
    {
        if (_disposed) return;

        ExecuteSafely(
            OnDispose,
            "Dispose",
            "Error during disposal"
        );

        _disposed = true;
        GC.SuppressFinalize(this);
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