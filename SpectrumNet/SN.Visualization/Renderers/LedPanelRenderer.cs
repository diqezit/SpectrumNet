#nullable enable

using static SpectrumNet.SN.Visualization.Renderers.LedPanelRenderer.Constants;
using static System.MathF;

namespace SpectrumNet.SN.Visualization.Renderers;

public sealed class LedPanelRenderer : EffectSpectrumRenderer
{
    private const string LogPrefix = nameof(LedPanelRenderer);

    private static readonly Lazy<LedPanelRenderer> _instance =
        new(() => new LedPanelRenderer());

    private LedPanelRenderer() { }

    public static LedPanelRenderer GetInstance() => _instance.Value;

    public static class Constants
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
            INNER_GLOW_SIZE = 0.7f,
            OVERLAY_PADDING_FACTOR = 0.95f;

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
            float GlowStrength,
            bool IsOverlay
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
    private bool _useExternalColors;
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
        _logger.Log(LogLevel.Debug, LogPrefix, "Initialized");
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
        InvalidateGrid();
    }

    private void InvalidateGrid()
    {
        _grid = null;
        RequestRedraw();
    }

    protected override RenderParameters CalculateRenderParameters(
        SKImageInfo info,
        int requestedBarCount)
    {
        int maxBars = GetMaxBarsForQuality();
        int effectiveBarCount = Min(requestedBarCount, maxBars);

        UpdateGridIfNeeded(info, effectiveBarCount);

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
            _grid.StartX);
    }

    private void UpdateGridIfNeeded(
        SKImageInfo info,
        int requestedBarCount)
    {
        if (!NeedsGridUpdate(info, requestedBarCount))
            return;

        var (columns, rows) = CalculateGridSize(info, requestedBarCount);
        var (cellSize, startX, startY) = CalculateLayout(info, columns, rows);

        CreateGrid(columns, rows, cellSize, startX, startY);
    }

    private bool NeedsGridUpdate(
        SKImageInfo info,
        int requestedBarCount)
    {
        if (_grid == null) return true;

        var (columns, rows) = CalculateGridSize(info, requestedBarCount);
        var (cellSize, startX, startY) = CalculateLayout(info, columns, rows);

        bool gridSizeChanged = _grid.Columns != columns || _grid.Rows != rows;
        bool positionChanged = MathF.Abs(_grid.StartX - startX) > 0.1f ||
                              MathF.Abs(_grid.StartY - startY) > 0.1f;
        bool cellSizeChanged = MathF.Abs(_grid.CellSize - cellSize) > 0.1f;
        bool overlayChanged = _grid.IsOverlay != IsOverlayActive;

        return gridSizeChanged || positionChanged || cellSizeChanged || overlayChanged;
    }

    private (int columns, int rows) CalculateGridSize(
        SKImageInfo info,
        int requestedBarCount)
    {
        var (maxRows, _) = QualityConfig[Quality];
        float ledSize = LED_RADIUS * 2 + LED_MARGIN;

        float availableWidth = IsOverlayActive
            ? info.Width * OVERLAY_PADDING_FACTOR
            : info.Width;
        float availableHeight = IsOverlayActive
            ? info.Height * OVERLAY_PADDING_FACTOR
            : info.Height;

        int maxAllowed = Min(MAX_COLUMNS, GetMaxBarsForQuality());
        int columns = Min(
            Min(maxAllowed, requestedBarCount),
            (int)(availableWidth / ledSize));
        int rows = Min(
            maxRows,
            (int)(availableHeight / ledSize));

        return (
            Max(MIN_GRID_SIZE, columns),
            Max(MIN_GRID_SIZE, rows)
        );
    }

    private static (float cellSize, float startX, float startY) CalculateLayout(
        SKImageInfo info,
        int columns,
        int rows)
    {
        float cellSize = MathF.Min(
            info.Width / (float)columns,
            info.Height / (float)rows
        );

        float gridWidth = columns * cellSize;
        float gridHeight = rows * cellSize;
        float startX = (info.Width - gridWidth) * 0.5f;
        float startY = (info.Height - gridHeight) * 0.5f;

        return (cellSize, startX, startY);
    }

    private void CreateGrid(
        int columns,
        int rows,
        float cellSize,
        float startX,
        float startY)
    {
        var (_, useEffects) = QualityConfig[Quality];

        float glowStrength = useEffects ? (IsOverlayActive ? 0.5f : 1f) : 0f;

        _grid = new GridSettings(
            rows,
            columns,
            cellSize,
            startX,
            startY,
            useEffects && UseAdvancedEffects,
            useEffects,
            glowStrength,
            IsOverlayActive
        );

        CacheLedPositions();
        RequestRedraw();
    }

    private void CacheLedPositions()
    {
        if (_grid == null) return;

        float halfCell = _grid.CellSize * 0.5f;

        for (int col = 0; col < _grid.Columns; col++)
        {
            CacheLedColumnPositions(col, halfCell);
        }
    }

    private void CacheLedColumnPositions(
        int col,
        float halfCell)
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
        if (!ValidateBeforeRender(canvas, paint))
            return;

        UpdateColorSettings(paint!);
    }

    private bool ValidateBeforeRender(
        SKCanvas? canvas,
        SKPaint? paint) =>
        canvas != null && _grid != null && paint != null;

    private void UpdateColorSettings(SKPaint paint)
    {
        _useExternalColors = paint.Color != SKColors.White;
        if (_useExternalColors)
            _externalBaseColor = paint.Color;
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

        if (IsOverlayActive)
        {
            using var bgPaint = new SKPaint
            {
                Color = BackgroundTint.WithAlpha(
                    (byte)(BackgroundTint.Alpha * _overlayStateManager.OverlayAlphaFactor))
            };
            canvas.DrawRect(0, 0, info.Width, info.Height, bgPaint);
        }
        else
        {
            canvas.Clear(BackgroundTint);
        }

        UpdateValues(spectrum);
        RenderInactiveLeds(canvas);
        RenderActiveLeds(canvas);
    }

    private void RenderInactiveLeds(SKCanvas canvas)
    {
        if (_grid == null) return;

        using var paint = CreateInactiveLedPaint();

        for (int col = 0; col < _grid.Columns; col++)
        {
            DrawInactiveLedColumn(canvas, paint, col);
        }
    }

    private SKPaint CreateInactiveLedPaint()
    {
        var alpha = IsOverlayActive
            ? (byte)(INACTIVE_ALPHA * 255 * _overlayStateManager.OverlayAlphaFactor)
            : (byte)(INACTIVE_ALPHA * 255);

        return CreateStandardPaint(
            InactiveColor.WithAlpha(alpha));
    }

    private void DrawInactiveLedColumn(
        SKCanvas canvas,
        SKPaint paint,
        int col)
    {
        if (_grid == null) return;

        for (int row = 0; row < _grid.Rows; row++)
        {
            canvas.DrawCircle(_ledPositions[col, row], LED_RADIUS, paint);
        }
    }

    private void UpdateValues(float[] spectrum)
    {
        if (_grid == null) return;

        float deltaTime = _animationTimer.DeltaTime;
        int count = Min(_grid.Columns, spectrum.Length);

        Parallel.For(0, count, i =>
        {
            UpdateSmoothing(i, spectrum[i]);
            UpdatePeak(i, deltaTime);
        });
    }

    private void UpdateSmoothing(
        int column,
        float target)
    {
        float current = _smoothedValues[column];
        _smoothedValues[column] = current < target
            ? Lerp(current, target, ATTACK_RATE)
            : Lerp(current, target, 1f - DECAY_RATE);
    }

    private void UpdatePeak(
        int column,
        float deltaTime)
    {
        if (!ShouldUpdatePeak(column))
            return;

        if (_smoothedValues[column] > _peakValues[column])
        {
            ResetPeak(column);
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

    private bool ShouldUpdatePeak(int column) =>
        _grid != null && _grid.UsePeakHold;

    private void ResetPeak(int column)
    {
        _peakValues[column] = _smoothedValues[column];
        _peakTimers[column] = PEAK_HOLD_TIME;
    }

    private void RenderActiveLeds(SKCanvas canvas)
    {
        if (_grid == null) return;

        int columns = Min(_grid.Columns, _smoothedValues.Length);

        for (int col = 0; col < columns; col++)
        {
            RenderColumn(canvas, col);
            RenderPeakIfActive(canvas, col);
        }
    }

    private void RenderColumn(
        SKCanvas canvas,
        int col)
    {
        if (_grid == null) return;

        float value = _smoothedValues[col];
        int activeLeds = CalculateActiveLeds(value);

        for (int row = 0; row < activeLeds; row++)
        {
            float brightness = CalculateLedBrightness(
                value,
                row == activeLeds - 1);

            RenderLed(canvas, col, row, brightness, value);
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

    private static float CalculateLedBrightness(
        float value,
        bool isTopLed)
    {
        float brightness = Lerp(MIN_ACTIVE_BRIGHTNESS, 1f, value);

        if (isTopLed)
            brightness *= 1.2f;

        return MathF.Min(brightness, 1f);
    }

    private void RenderPeakIfActive(
        SKCanvas canvas,
        int col)
    {
        if (!IsPeakActive(col))
            return;

        int peakRow = CalculatePeakRow(col);

        if (IsValidPeakRow(peakRow))
        {
            float alpha = _peakTimers[col] / PEAK_HOLD_TIME;
            RenderPeakLed(canvas, col, peakRow, alpha);
        }
    }

    private bool IsPeakActive(int col) =>
        _grid != null &&
        _grid.UsePeakHold &&
        _peakTimers[col] > 0;

    private int CalculatePeakRow(int col) =>
        _grid == null ? -1 : (int)(_peakValues[col] * _grid.Rows) - 1;

    private bool IsValidPeakRow(int row) =>
        _grid != null && row >= 0 && row < _grid.Rows;

    private void RenderLed(
        SKCanvas canvas,
        int col,
        int row,
        float brightness,
        float intensity)
    {
        if (_grid == null) return;

        var pos = _ledPositions[col, row];
        var baseColor = GetLedColor(row);

        float adjustedBrightness = IsOverlayActive
            ? brightness * _overlayStateManager.OverlayAlphaFactor
            : brightness;

        var ledColor = baseColor.WithAlpha((byte)(adjustedBrightness * 255));

        if (_grid.UseGlow)
        {
            RenderLedGlow(canvas, pos, ledColor, adjustedBrightness);
        }

        RenderLedBody(canvas, pos, ledColor);
        RenderLedInnerGlow(canvas, pos, adjustedBrightness, intensity);
    }

    private void RenderLedGlow(
        SKCanvas canvas,
        SKPoint pos,
        SKColor ledColor,
        float brightness)
    {
        if (_grid == null) return;

        using var glowPaint = CreateGlowPaint(ledColor, brightness);
        canvas.DrawCircle(pos, LED_RADIUS + GLOW_RADIUS, glowPaint);
    }

    private SKPaint CreateGlowPaint(
        SKColor ledColor,
        float brightness)
    {
        var glowAlpha = IsOverlayActive
            ? (byte)(brightness * 60 * _overlayStateManager.OverlayAlphaFactor)
            : (byte)(brightness * 60);

        var paint = CreateStandardPaint(
            ledColor.WithAlpha(glowAlpha));

        paint.MaskFilter = SKMaskFilter.CreateBlur(
            SKBlurStyle.Normal,
            GLOW_RADIUS * _grid!.GlowStrength);

        return paint;
    }

    private void RenderLedBody(
        SKCanvas canvas,
        SKPoint pos,
        SKColor ledColor)
    {
        using var ledPaint = CreateStandardPaint(ledColor);
        canvas.DrawCircle(pos, LED_RADIUS, ledPaint);
    }

    private void RenderLedInnerGlow(
        SKCanvas canvas,
        SKPoint pos,
        float brightness,
        float intensity)
    {
        var innerGlowColor = GetInnerGlowColor(brightness, intensity);

        using var innerPaint = CreateStandardPaint(innerGlowColor);
        canvas.DrawCircle(pos, LED_RADIUS * INNER_GLOW_SIZE, innerPaint);
    }

    private SKColor GetInnerGlowColor(
        float brightness,
        float intensity)
    {
        var baseAlpha = brightness * intensity * 128;
        byte alpha = IsOverlayActive
            ? (byte)(baseAlpha * _overlayStateManager.OverlayAlphaFactor)
            : (byte)baseAlpha;

        return _useExternalColors
            ? _externalBaseColor.WithAlpha(alpha)
            : SKColors.White.WithAlpha(alpha);
    }

    private void RenderPeakLed(
        SKCanvas canvas,
        int col,
        int row,
        float alpha)
    {
        var pos = _ledPositions[col, row];
        var peakColor = GetPeakColor(alpha);

        using var paint = CreatePeakPaint(peakColor);
        canvas.DrawCircle(pos, LED_RADIUS + 2, paint);
    }

    private SKColor GetPeakColor(float alpha)
    {
        var baseAlpha = alpha * 200;
        byte alphaValue = IsOverlayActive
            ? (byte)(baseAlpha * _overlayStateManager.OverlayAlphaFactor)
            : (byte)baseAlpha;

        return _useExternalColors
            ? _externalBaseColor.WithAlpha(alphaValue)
            : PeakColor.WithAlpha(alphaValue);
    }

    private SKPaint CreatePeakPaint(SKColor color)
    {
        var paint = CreateStandardPaint(color);
        paint.Style = SKPaintStyle.Stroke;
        paint.StrokeWidth = 2;
        return paint;
    }

    private SKColor GetLedColor(int row)
    {
        var baseColor = _rowColors[Min(row, _rowColors.Length - 1)];

        if (!_useExternalColors)
            return baseColor;

        return BlendWithExternalColor(baseColor, row);
    }

    private SKColor BlendWithExternalColor(
        SKColor baseColor,
        int row)
    {
        float t = row / (float)(_rowColors.Length - 1);
        float blend = 0.7f;

        return new SKColor(
            BlendColorComponent(_externalBaseColor.Red, baseColor.Red, blend, t),
            BlendColorComponent(_externalBaseColor.Green, baseColor.Green, blend, t),
            BlendColorComponent(_externalBaseColor.Blue, baseColor.Blue, blend, t)
        );
    }

    private static byte BlendColorComponent(
        byte external,
        byte gradient,
        float blend,
        float t) =>
        (byte)(external * blend + gradient * (1 - blend) * t);

    private static float Lerp(float a, float b, float t) =>
        a + (b - a) * Clamp(t, 0f, 1f);

    public override bool RequiresRedraw() =>
        base.RequiresRedraw() ||
        HasActivePeaks();

    private bool HasActivePeaks() =>
        _grid != null &&
        _grid.UsePeakHold &&
        Array.Exists(_peakTimers, t => t > 0);

    protected override void CleanupUnusedResources()
    {
        if (!ShouldCleanupArrays())
            return;

        CleanupArrayData();
    }

    private bool ShouldCleanupArrays() =>
        _grid != null &&
        _smoothedValues.Length > _grid.Columns * 2;

    private void CleanupArrayData()
    {
        if (_grid == null) return;

        int start = _grid.Columns;
        int count = _smoothedValues.Length - _grid.Columns;

        Array.Clear(_smoothedValues, start, count);
        Array.Clear(_peakValues, start, count);
        Array.Clear(_peakTimers, start, count);
    }

    protected override void OnDispose()
    {
        base.OnDispose();
        _logger.Log(LogLevel.Debug, LogPrefix, "Disposed");
    }
}