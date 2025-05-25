#nullable enable

using static SpectrumNet.SN.Visualization.Renderers.LedPanelRenderer.Constants;

namespace SpectrumNet.SN.Visualization.Renderers;

public sealed class LedPanelRenderer : EffectSpectrumRenderer
{
    private const string LogPrefix = nameof(LedPanelRenderer);

    private static readonly Lazy<LedPanelRenderer> _instance =
        new(() => new LedPanelRenderer());

    private LedPanelRenderer() { }

    public static LedPanelRenderer GetInstance() => _instance.Value;

    public record Constants
    {
        public const float
            LED_RADIUS = 6f,
            LED_MARGIN = 3f,
            GLOW_RADIUS = 4f,
            INACTIVE_ALPHA = 0.08f,
            MIN_ACTIVE_BRIGHTNESS = 0.4f,
            DECAY_RATE = 0.85f,
            ATTACK_RATE = 0.4f,
            PEAK_HOLD_TIME = 0.5f,
            INNER_GLOW_SIZE = 0.7f;

        public const int
            MIN_GRID_SIZE = 10,
            MAX_COLUMNS = 64,
            BATCH_PROCESS_SIZE = 16,
            MAX_BARS_LOW = 32,
            MAX_BARS_MEDIUM = 48,
            MAX_BARS_HIGH = 64;

        public static readonly SKColor[] SpectrumGradient =
        [
            new(0, 200, 100),
            new(0, 255, 0),
            new(128, 255, 0),
            new(255, 255, 0),
            new(255, 200, 0),
            new(255, 128, 0),
            new(255, 64, 0),
            new(255, 0, 0),
            new(200, 0, 50)
        ];

        public static readonly SKColor
            InactiveColor = new(80, 80, 80),
            PeakColor = SKColors.White,
            BackgroundTint = new(10, 10, 15);

        public record GridSettings(
            int Rows,
            int Columns,
            float CellSize,
            float StartX,
            float StartY,
            bool UseGlow,
            bool UsePeakHold,
            float GlowStrength
        );

        public static readonly Dictionary<RenderQuality, (int maxRows, bool effects)> QualityConfig = new()
        {
            [RenderQuality.Low] = (16, false),
            [RenderQuality.Medium] = (24, true),
            [RenderQuality.High] = (32, true)
        };
    }

    private GridSettings? _grid;
    private readonly float[] _smoothedValues = new float[MAX_COLUMNS];
    private readonly float[] _peakValues = new float[MAX_COLUMNS];
    private readonly float[] _peakTimers = new float[MAX_COLUMNS];
    private readonly SKPoint[,] _ledPositions = new SKPoint[MAX_COLUMNS, 32];
    private readonly SKColor[] _rowColors = new SKColor[32];
    private SKBitmap? _inactiveLedLayer;
    private int _currentBarCount = 0;
    private bool _useExternalColors = false;
    private SKColor _externalBaseColor = SKColors.White;

    protected override int GetMaxBarsForQuality() => Quality switch
    {
        RenderQuality.Low => MAX_BARS_LOW,
        RenderQuality.Medium => MAX_BARS_MEDIUM,
        RenderQuality.High => MAX_BARS_HIGH,
        _ => MAX_BARS_MEDIUM
    };

    protected override void OnInitialize()
    {
        base.OnInitialize();
        InitializeColorGradient();
    }

    private void InitializeColorGradient()
    {
        for (int i = 0; i < _rowColors.Length; i++)
        {
            float t = i / (float)(_rowColors.Length - 1);
            _rowColors[i] = InterpolateGradient(t);
        }
    }

    private static SKColor InterpolateGradient(float t)
    {
        float scaledT = t * (SpectrumGradient.Length - 1);
        int index = (int)scaledT;
        float fraction = scaledT - index;

        if (index >= SpectrumGradient.Length - 1)
            return SpectrumGradient[^1];

        var c1 = SpectrumGradient[index];
        var c2 = SpectrumGradient[index + 1];

        return InterpolateColors(c1, c2, fraction);
    }

    private static SKColor InterpolateColors(
        SKColor c1,
        SKColor c2,
        float fraction) =>
        new(
            (byte)(c1.Red + (c2.Red - c1.Red) * fraction),
            (byte)(c1.Green + (c2.Green - c1.Green) * fraction),
            (byte)(c1.Blue + (c2.Blue - c1.Blue) * fraction)
        );

    protected override void OnConfigurationChanged()
    {
        base.OnConfigurationChanged();
        InvalidateInactiveLayer();
    }

    private void InvalidateInactiveLayer()
    {
        _needsRedraw = true;
        _inactiveLedLayer?.Dispose();
        _inactiveLedLayer = null;
    }

    protected override RenderParameters CalculateRenderParameters(
        SKImageInfo info,
        int requestedBarCount)
    {
        int maxBars = GetMaxBarsForQuality();
        int effectiveBarCount = Math.Min(requestedBarCount, maxBars);

        UpdateGridSettings(info, effectiveBarCount);

        if (_grid == null)
            return new RenderParameters(
                effectiveBarCount,
                LED_RADIUS * 2,
                LED_MARGIN,
                0f);

        return new RenderParameters(
            _grid.Columns,
            _grid.CellSize,
            0f,
            _grid.StartX
        );
    }

    private void UpdateGridSettings(
        SKImageInfo info,
        int requestedBarCount)
    {
        var dimensions = CalculateGridDimensions(info, requestedBarCount);
        var layout = CalculateGridLayout(
            info,
            dimensions.columns,
            dimensions.rows);

        if (ShouldUpdateGrid(
            dimensions.columns,
            dimensions.rows,
            requestedBarCount))
        {
            CreateNewGrid(dimensions, layout);
        }
    }

    private (int columns, int rows) CalculateGridDimensions(
        SKImageInfo info,
        int requestedBarCount)
    {
        var (maxRows, _) = QualityConfig[Quality];
        float totalLedSize = LED_RADIUS * 2 + LED_MARGIN;

        int maxAllowedColumns = Math.Min(MAX_COLUMNS, GetMaxBarsForQuality());
        int columns = Math.Min(
            Math.Min(maxAllowedColumns, requestedBarCount),
            (int)(info.Width / totalLedSize));
        int rows = Math.Min(maxRows, (int)(info.Height / totalLedSize));

        return (
            Math.Max(MIN_GRID_SIZE, columns),
            Math.Max(MIN_GRID_SIZE, rows)
        );
    }

    private (float cellSize, float startX, float startY) CalculateGridLayout(
        SKImageInfo info,
        int columns,
        int rows)
    {
        float cellSize = Math.Min(
            info.Width / (float)columns,
            info.Height / (float)rows
        );

        float gridWidth = columns * cellSize;
        float gridHeight = rows * cellSize;
        float startX = (info.Width - gridWidth) * 0.5f;
        float startY = (info.Height - gridHeight) * 0.5f;

        return (cellSize, startX, startY);
    }

    private bool ShouldUpdateGrid(
        int columns,
        int rows,
        int requestedBarCount) =>
        _grid == null ||
        _grid.Columns != columns ||
        _grid.Rows != rows ||
        _currentBarCount != requestedBarCount;

    private void CreateNewGrid(
        (int columns, int rows) dimensions,
        (float cellSize, float startX, float startY) layout)
    {
        var (_, useEffects) = QualityConfig[Quality];

        _grid = new GridSettings(
            dimensions.rows,
            dimensions.columns,
            layout.cellSize,
            layout.startX,
            layout.startY,
            useEffects && UseAdvancedEffects,
            useEffects,
            useEffects ? 1f : 0f
        );

        _currentBarCount = dimensions.columns;
        CacheLedPositions();
        _needsRedraw = true;
    }

    private void CacheLedPositions()
    {
        if (_grid == null) return;

        float halfCell = _grid.CellSize * 0.5f;

        for (int col = 0; col < _grid.Columns; col++)
            CacheLedColumnPositions(col, halfCell);
    }

    private void CacheLedColumnPositions(int col, float halfCell)
    {
        if (_grid == null) return;

        float x = _grid.StartX + col * _grid.CellSize + halfCell;

        for (int row = 0; row < _grid.Rows; row++)
        {
            float y = _grid.StartY +
                (_grid.Rows - 1 - row) * _grid.CellSize + halfCell;
            _ledPositions[col, row] = new SKPoint(x, y);
        }
    }

    protected override void BeforeRender(
        SKCanvas? canvas,
        float[]? spectrum,
        SKImageInfo info,
        float barWidth,
        float barSpacing,
        int barCount,
        SKPaint? paint)
    {
        if (!ShouldRenderInactiveLayer(canvas)) return;

        UpdateColorSettings(paint);
        EnsureInactiveLayerExists(info, paint);
        DrawInactiveLayer(canvas);
    }

    private void UpdateColorSettings(SKPaint? paint)
    {
        if (paint == null) return;

        _useExternalColors = paint.Color != SKColors.White;
        if (_useExternalColors)
            _externalBaseColor = paint.Color;
    }

    private bool ShouldRenderInactiveLayer(SKCanvas? canvas) =>
        canvas != null && _grid != null;

    private void EnsureInactiveLayerExists(
        SKImageInfo info,
        SKPaint? paint)
    {
        if (_needsRedraw || _inactiveLedLayer == null)
        {
            CreateInactiveLedLayer(info, paint);
            _needsRedraw = false;
        }
    }

    private void DrawInactiveLayer(SKCanvas? canvas)
    {
        if (canvas != null && _inactiveLedLayer != null)
            canvas.DrawBitmap(_inactiveLedLayer, 0, 0);
    }

    private void CreateInactiveLedLayer(
        SKImageInfo info,
        SKPaint? paint)
    {
        _inactiveLedLayer?.Dispose();
        _inactiveLedLayer = new SKBitmap(info.Width, info.Height);

        using var surface = SKSurface.Create(
            new SKImageInfo(info.Width, info.Height));
        var layerCanvas = surface.Canvas;

        layerCanvas.Clear(BackgroundTint);
        DrawInactiveLeds(layerCanvas, paint);

        SaveLayerToBitmap(surface);
    }

    private void SaveLayerToBitmap(SKSurface surface)
    {
        if (_inactiveLedLayer == null) return;

        using var snapshot = surface.Snapshot();
        snapshot.ReadPixels(
            _inactiveLedLayer.Info,
            _inactiveLedLayer.GetPixels());
    }

    private void DrawInactiveLeds(
        SKCanvas canvas,
        SKPaint? paint)
    {
        if (_grid == null || paint == null) return;

        var savedColor = paint.Color;
        var savedStyle = paint.Style;

        ConfigurePaintForInactiveLeds(paint);

        for (int col = 0; col < _grid.Columns; col++)
            DrawInactiveLedColumn(canvas, paint, col);

        paint.Color = savedColor;
        paint.Style = savedStyle;
    }

    private void ConfigurePaintForInactiveLeds(SKPaint paint)
    {
        paint.Color = InactiveColor.WithAlpha(
            (byte)(INACTIVE_ALPHA * 255));
        paint.Style = SKPaintStyle.Fill;
        paint.IsAntialias = UseAntiAlias;
    }

    private void DrawInactiveLedColumn(
        SKCanvas canvas,
        SKPaint paint,
        int col)
    {
        if (_grid == null) return;

        for (int row = 0; row < _grid.Rows; row++)
        {
            var pos = _ledPositions[col, row];
            canvas.DrawCircle(pos, LED_RADIUS, paint);
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
        if (_grid == null) return;

        UpdateSmoothingAndPeaks(spectrum);
        ProcessColumnsInParallel(spectrum);
        RenderActiveLeds(canvas, spectrum, paint);
    }

    private void ProcessColumnsInParallel(float[] spectrum)
    {
        if (_grid == null) return;

        int columnsToProcess = Math.Min(_grid.Columns, spectrum.Length);

        Parallel.For(0, columnsToProcess, new ParallelOptions
        {
            MaxDegreeOfParallelism = Environment.ProcessorCount
        },
        col => ProcessColumn(col));
    }

    private void UpdateSmoothingAndPeaks(float[] spectrum)
    {
        if (_grid == null) return;

        float deltaTime = _animationTimer.DeltaTime;
        int length = Math.Min(_grid.Columns, spectrum.Length);

        for (int i = 0; i < length; i++)
        {
            UpdateSmoothingForColumn(i, spectrum[i]);
            UpdatePeakForColumn(i, deltaTime);
        }
    }

    private void UpdateSmoothingForColumn(
        int column,
        float targetValue)
    {
        float current = _smoothedValues[column];

        _smoothedValues[column] = current < targetValue
            ? Lerp(current, targetValue, ATTACK_RATE)
            : Lerp(current, targetValue, 1f - DECAY_RATE);
    }

    private void UpdatePeakForColumn(
        int column,
        float deltaTime)
    {
        if (_grid == null || !_grid.UsePeakHold) return;

        if (_smoothedValues[column] > _peakValues[column])
        {
            _peakValues[column] = _smoothedValues[column];
            _peakTimers[column] = PEAK_HOLD_TIME;
        }
        else if (_peakTimers[column] > 0)
        {
            _peakTimers[column] -= deltaTime;
        }
        else
        {
            _peakValues[column] *= 0.95f;
        }
    }

    private void ProcessColumn(int col) { }

    private void RenderActiveLeds(
        SKCanvas canvas,
        float[] spectrum,
        SKPaint paint)
    {
        if (_grid == null) return;

        int columns = Math.Min(_grid.Columns, spectrum.Length);

        for (int col = 0; col < columns; col += BATCH_PROCESS_SIZE)
        {
            int batchEnd = Math.Min(col + BATCH_PROCESS_SIZE, columns);
            RenderBatch(canvas, col, batchEnd, paint);
        }
    }

    private void RenderBatch(
        SKCanvas canvas,
        int startCol,
        int endCol,
        SKPaint paint)
    {
        if (_grid == null) return;

        for (int col = startCol; col < endCol; col++)
        {
            RenderColumn(canvas, col, paint);
            RenderPeakIfNeeded(canvas, col, paint);
        }
    }

    private void RenderColumn(
        SKCanvas canvas,
        int col,
        SKPaint paint)
    {
        if (_grid == null) return;

        float value = _smoothedValues[col];
        int activeLeds = CalculateActiveLeds(value);

        for (int row = 0; row < activeLeds; row++)
        {
            bool isTop = row == activeLeds - 1;
            RenderSingleLed(canvas, col, row, value, isTop, paint);
        }
    }

    private int CalculateActiveLeds(float value)
    {
        if (_grid == null) return 0;

        int activeLeds = (int)(value * _grid.Rows);

        if (activeLeds == 0 && value > 0.05f)
            activeLeds = 1;

        return activeLeds;
    }

    private void RenderPeakIfNeeded(
        SKCanvas canvas,
        int col,
        SKPaint paint)
    {
        if (_grid == null || !_grid.UsePeakHold || _peakTimers[col] <= 0)
            return;

        int peakRow = CalculatePeakRow(col);
        if (IsValidRow(peakRow))
        {
            float alpha = _peakTimers[col] / PEAK_HOLD_TIME;
            RenderPeakLed(canvas, col, peakRow, alpha, paint);
        }
    }

    private int CalculatePeakRow(int col) =>
        _grid == null ? -1 : (int)(_peakValues[col] * _grid.Rows) - 1;

    private bool IsValidRow(int row) =>
        _grid != null && row >= 0 && row < _grid.Rows;

    private void RenderSingleLed(
        SKCanvas canvas,
        int col,
        int row,
        float intensity,
        bool isTop,
        SKPaint paint)
    {
        if (_grid == null) return;

        var pos = _ledPositions[col, row];
        var baseColor = GetLedColor(row, paint);
        float brightness = CalculateBrightness(intensity, isTop);

        RenderLedWithEffects(
            canvas,
            pos,
            baseColor,
            brightness,
            intensity,
            paint);
    }

    private SKColor GetLedColor(int row, SKPaint paint)
    {
        if (_useExternalColors)
        {
            float t = row / (float)(_rowColors.Length - 1);
            return BlendWithExternalColor(_rowColors[row], t);
        }

        return _rowColors[Math.Min(row, _rowColors.Length - 1)];
    }

    private SKColor BlendWithExternalColor(SKColor gradientColor, float t)
    {
        float blendFactor = 0.7f;
        return new SKColor(
            (byte)(_externalBaseColor.Red * blendFactor +
                gradientColor.Red * (1 - blendFactor) * t),
            (byte)(_externalBaseColor.Green * blendFactor +
                gradientColor.Green * (1 - blendFactor) * t),
            (byte)(_externalBaseColor.Blue * blendFactor +
                gradientColor.Blue * (1 - blendFactor) * t)
        );
    }

    private float CalculateBrightness(float intensity, bool isTop)
    {
        float brightness = Lerp(MIN_ACTIVE_BRIGHTNESS, 1f, intensity);
        if (isTop) brightness *= 1.2f;
        return brightness;
    }

    private void RenderLedWithEffects(
        SKCanvas canvas,
        SKPoint pos,
        SKColor baseColor,
        float brightness,
        float intensity,
        SKPaint paint)
    {
        if (_grid == null) return;

        var ledColor = baseColor.WithAlpha((byte)(brightness * 255));

        if (_grid.UseGlow)
            RenderLedGlow(canvas, pos, ledColor, brightness, paint);

        RenderLedBody(canvas, pos, ledColor, paint);
        RenderLedInnerGlow(canvas, pos, brightness, intensity, paint);
    }

    private void RenderLedGlow(
        SKCanvas canvas,
        SKPoint pos,
        SKColor ledColor,
        float brightness,
        SKPaint paint)
    {
        if (_grid == null) return;

        var savedColor = paint.Color;
        var savedMaskFilter = paint.MaskFilter;

        paint.Color = ledColor.WithAlpha((byte)(brightness * 60));
        paint.MaskFilter = SKMaskFilter.CreateBlur(
            SKBlurStyle.Normal,
            GLOW_RADIUS * _grid.GlowStrength);

        canvas.DrawCircle(pos, LED_RADIUS + GLOW_RADIUS, paint);

        paint.Color = savedColor;
        paint.MaskFilter = savedMaskFilter;
    }

    private void RenderLedBody(
        SKCanvas canvas,
        SKPoint pos,
        SKColor ledColor,
        SKPaint paint)
    {
        var savedColor = paint.Color;
        paint.Color = ledColor;
        canvas.DrawCircle(pos, LED_RADIUS, paint);
        paint.Color = savedColor;
    }

    private void RenderLedInnerGlow(
        SKCanvas canvas,
        SKPoint pos,
        float brightness,
        float intensity,
        SKPaint paint)
    {
        var savedColor = paint.Color;
        var glowColor = _useExternalColors
            ? _externalBaseColor.WithAlpha((byte)(brightness * intensity * 128))
            : SKColors.White.WithAlpha((byte)(brightness * intensity * 128));

        paint.Color = glowColor;
        canvas.DrawCircle(pos, LED_RADIUS * INNER_GLOW_SIZE, paint);
        paint.Color = savedColor;
    }

    private void RenderPeakLed(
        SKCanvas canvas,
        int col,
        int row,
        float alpha,
        SKPaint paint)
    {
        var pos = _ledPositions[col, row];

        var savedColor = paint.Color;
        var savedStyle = paint.Style;
        var savedStrokeWidth = paint.StrokeWidth;

        var peakColor = _useExternalColors
            ? _externalBaseColor.WithAlpha((byte)(alpha * 200))
            : PeakColor.WithAlpha((byte)(alpha * 200));

        paint.Color = peakColor;
        paint.Style = SKPaintStyle.Stroke;
        paint.StrokeWidth = 2;

        canvas.DrawCircle(pos, LED_RADIUS + 2, paint);

        paint.Color = savedColor;
        paint.Style = savedStyle;
        paint.StrokeWidth = savedStrokeWidth;
    }

    private static float Lerp(float a, float b, float t) =>
        a + (b - a) * t;

    protected override void CleanupUnusedResources()
    {
        if (_grid != null && _smoothedValues.Length > _grid.Columns * 2)
            ClearUnusedArrayData();
    }

    private void ClearUnusedArrayData()
    {
        if (_grid == null) return;

        int startIndex = _grid.Columns;
        int count = _smoothedValues.Length - _grid.Columns;

        Array.Clear(_smoothedValues, startIndex, count);
        Array.Clear(_peakValues, startIndex, count);
        Array.Clear(_peakTimers, startIndex, count);
    }

    protected override void OnDispose()
    {
        _inactiveLedLayer?.Dispose();
        _inactiveLedLayer = null;
        base.OnDispose();
    }
}