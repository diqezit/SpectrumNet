#nullable enable

namespace SpectrumNet.SN.Visualization.Renderers;

public sealed class LedPanelRenderer : EffectSpectrumRenderer<LedPanelRenderer.QualitySettings>
{
    private static readonly Lazy<LedPanelRenderer> _instance =
        new(() => new LedPanelRenderer());

    public static LedPanelRenderer GetInstance() => _instance.Value;

    private const float LED_RADIUS = 6f,
        LED_MARGIN = 3f,
        INACTIVE_ALPHA = 0.08f,
        MIN_ACTIVE_BRIGHTNESS = 0.4f,
        DECAY_RATE = 0.85f,
        ATTACK_RATE = 0.4f,
        PEAK_HOLD_TIME = 0.5f,
        OVERLAY_PADDING_FACTOR = 0.95f,
        PEAK_STROKE_WIDTH = 2f,
        PEAK_RADIUS_OFFSET = 2f,
        PEAK_DECAY_RATE = 0.95f,
        MIN_VALUE_THRESHOLD = 0.05f,
        TOP_LED_BRIGHTNESS_BOOST = 1.2f,
        PEAK_ALPHA_FACTOR = 200f,
        EXTERNAL_COLOR_BLEND = 0.7f,
        ANIMATION_DELTA_TIME = 0.016f,
        GRID_POSITION_TOLERANCE = 0.1f;

    private const int MIN_GRID_SIZE = 10,
        MAX_COLUMNS = 64,
        MAX_ROWS_CACHE = 32;

    private static readonly SKColor[] _spectrumGradient =
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

    private static readonly SKColor _inactiveColor = new(80, 80, 80);
    private static readonly SKColor _peakColor = SKColors.White;

    private GridData? _gridData;
    private readonly float[] _smoothedValues = new float[MAX_COLUMNS];
    private readonly float[] _peakValues = new float[MAX_COLUMNS];
    private readonly float[] _peakTimers = new float[MAX_COLUMNS];
    private readonly SKPoint[,] _ledPositions = new SKPoint[MAX_COLUMNS, MAX_ROWS_CACHE];
    private readonly SKColor[] _rowColors = new SKColor[MAX_ROWS_CACHE];
    private bool _useExternalColors;
    private SKColor _externalBaseColor = SKColors.White;

    public sealed class QualitySettings
    {
        public bool UsePeakHold { get; init; }
        public int MaxRows { get; init; }
        public float SmoothingMultiplier { get; init; }
    }

    protected override IReadOnlyDictionary<RenderQuality, QualitySettings>
        QualitySettingsPresets
    { get; } = new Dictionary<RenderQuality, QualitySettings>
    {
        [RenderQuality.Low] = new()
        {
            UsePeakHold = false,
            MaxRows = 16,
            SmoothingMultiplier = 1.0f
        },
        [RenderQuality.Medium] = new()
        {
            UsePeakHold = true,
            MaxRows = 24,
            SmoothingMultiplier = 0.9f
        },
        [RenderQuality.High] = new()
        {
            UsePeakHold = true,
            MaxRows = 32,
            SmoothingMultiplier = 0.8f
        }
    };

    protected override void RenderEffect(
        SKCanvas canvas,
        float[] spectrum,
        SKImageInfo info,
        RenderParameters renderParams,
        SKPaint passedInPaint)
    {
        if (CurrentQualitySettings == null || renderParams.EffectiveBarCount <= 0)
            return;

        var (isValid, processedSpectrum) = ProcessSpectrum(
            spectrum,
            renderParams.EffectiveBarCount,
            applyTemporalSmoothing: true);

        if (!isValid || processedSpectrum == null)
            return;

        var ledData = CalculateLedData(
            processedSpectrum,
            info,
            renderParams,
            passedInPaint);

        if (!ValidateLedData(ledData))
            return;

        RenderLedVisualization(
            canvas,
            ledData,
            renderParams,
            passedInPaint);
    }

    private LedData CalculateLedData(
        float[] spectrum,
        SKImageInfo info,
        RenderParameters renderParams,
        SKPaint paint)
    {
        UpdateColorSettings(paint);
        UpdateGridIfNeeded(info, renderParams.EffectiveBarCount);
        UpdateValues(spectrum);

        return new LedData(
            GridData: _gridData!,
            UseExternalColors: _useExternalColors,
            ExternalBaseColor: _externalBaseColor);
    }

    private static bool ValidateLedData(LedData data) =>
        data.GridData != null &&
        data.GridData.Columns > 0 &&
        data.GridData.Rows > 0;

    private void RenderLedVisualization(
        SKCanvas canvas,
        LedData data,
        RenderParameters renderParams,
        SKPaint basePaint)
    {
        var settings = CurrentQualitySettings!;

        RenderWithOverlay(canvas, () =>
        {
            RenderInactiveLeds(canvas, data.GridData);
            RenderActiveLeds(canvas, data, settings);
        });
    }

    private void UpdateColorSettings(SKPaint paint)
    {
        _useExternalColors = paint.Color != SKColors.White;
        if (_useExternalColors)
            _externalBaseColor = paint.Color;
    }

    private void UpdateGridIfNeeded(SKImageInfo info, int requestedBarCount)
    {
        if (!NeedsGridUpdate(info, requestedBarCount))
            return;

        var (columns, rows) = CalculateGridSize(info, requestedBarCount);
        var (cellSize, startX, startY) = CalculateLayout(info, columns, rows);

        CreateGrid(columns, rows, cellSize, startX, startY);
    }

    private bool NeedsGridUpdate(SKImageInfo info, int requestedBarCount)
    {
        if (_gridData == null) return true;

        var (columns, rows) = CalculateGridSize(info, requestedBarCount);
        var (cellSize, startX, startY) = CalculateLayout(info, columns, rows);

        return _gridData.Columns != columns ||
               _gridData.Rows != rows ||
               MathF.Abs(_gridData.StartX - startX) > GRID_POSITION_TOLERANCE ||
               MathF.Abs(_gridData.StartY - startY) > GRID_POSITION_TOLERANCE ||
               MathF.Abs(_gridData.CellSize - cellSize) > GRID_POSITION_TOLERANCE ||
               _gridData.IsOverlay != IsOverlayActive;
    }

    private (int columns, int rows) CalculateGridSize(
        SKImageInfo info,
        int requestedBarCount)
    {
        var settings = CurrentQualitySettings!;
        float ledSize = LED_RADIUS * 2 + LED_MARGIN;

        float availableWidth = IsOverlayActive
            ? info.Width * OVERLAY_PADDING_FACTOR
            : info.Width;
        float availableHeight = IsOverlayActive
            ? info.Height * OVERLAY_PADDING_FACTOR
            : info.Height;

        int maxAllowed = Math.Min(MAX_COLUMNS, GetMaxBarsForQuality());
        int columns = Math.Min(
            Math.Min(maxAllowed, requestedBarCount),
            (int)(availableWidth / ledSize));
        int rows = Math.Min(
            settings.MaxRows,
            (int)(availableHeight / ledSize));

        return (
            Math.Max(MIN_GRID_SIZE, columns),
            Math.Max(MIN_GRID_SIZE, rows));
    }

    private static (float cellSize, float startX, float startY) CalculateLayout(
        SKImageInfo info,
        int columns,
        int rows)
    {
        float cellSize = Math.Min(
            info.Width / (float)columns,
            info.Height / (float)rows);

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
        _gridData = new GridData(
            Rows: rows,
            Columns: columns,
            CellSize: cellSize,
            StartX: startX,
            StartY: startY,
            IsOverlay: IsOverlayActive);

        CacheLedPositions();
        InitializeColorGradient();
        RequestRedraw();
    }

    private void CacheLedPositions()
    {
        if (_gridData == null) return;

        float halfCell = _gridData.CellSize * 0.5f;

        for (int col = 0; col < _gridData.Columns; col++)
        {
            float x = _gridData.StartX + col * _gridData.CellSize + halfCell;

            for (int row = 0; row < _gridData.Rows; row++)
            {
                float y = _gridData.StartY +
                    (_gridData.Rows - 1 - row) * _gridData.CellSize + halfCell;
                _ledPositions[col, row] = new SKPoint(x, y);
            }
        }
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
        float scaledT = t * (_spectrumGradient.Length - 1);
        int index = (int)scaledT;
        float fraction = scaledT - index;

        if (index >= _spectrumGradient.Length - 1)
            return _spectrumGradient[^1];

        var c1 = _spectrumGradient[index];
        var c2 = _spectrumGradient[index + 1];

        return new SKColor(
            (byte)(c1.Red + (c2.Red - c1.Red) * fraction),
            (byte)(c1.Green + (c2.Green - c1.Green) * fraction),
            (byte)(c1.Blue + (c2.Blue - c1.Blue) * fraction));
    }

    private void UpdateValues(float[] spectrum)
    {
        if (_gridData == null) return;

        int count = Math.Min(_gridData.Columns, spectrum.Length);

        for (int i = 0; i < count; i++)
        {
            UpdateSmoothing(i, spectrum[i]);
            UpdatePeak(i);
        }
    }

    private void UpdateSmoothing(int column, float target)
    {
        float current = _smoothedValues[column];
        float attackRate = ATTACK_RATE * CurrentQualitySettings!.SmoothingMultiplier;
        float decayRate = 1f - (DECAY_RATE * CurrentQualitySettings!.SmoothingMultiplier);

        _smoothedValues[column] = current < target
            ? Lerp(current, target, attackRate)
            : Lerp(current, target, decayRate);
    }

    private void UpdatePeak(int column)
    {
        var settings = CurrentQualitySettings!;
        if (!settings.UsePeakHold) return;

        if (_smoothedValues[column] > _peakValues[column])
        {
            _peakValues[column] = _smoothedValues[column];
            _peakTimers[column] = PEAK_HOLD_TIME;
        }
        else if (_peakTimers[column] > 0)
        {
            _peakTimers[column] -= ANIMATION_DELTA_TIME;
        }
        else
        {
            _peakValues[column] *= PEAK_DECAY_RATE;
        }
    }

    private void RenderInactiveLeds(SKCanvas canvas, GridData grid)
    {
        var alpha = IsOverlayActive
            ? (byte)(INACTIVE_ALPHA * 255 * GetOverlayAlphaFactor())
            : (byte)(INACTIVE_ALPHA * 255);

        var paint = CreatePaint(
            _inactiveColor.WithAlpha(alpha),
            SKPaintStyle.Fill);

        try
        {
            var rects = new List<SKRect>(grid.Columns * grid.Rows);

            for (int col = 0; col < grid.Columns; col++)
            {
                for (int row = 0; row < grid.Rows; row++)
                {
                    var pos = _ledPositions[col, row];
                    rects.Add(new SKRect(
                        pos.X - LED_RADIUS,
                        pos.Y - LED_RADIUS,
                        pos.X + LED_RADIUS,
                        pos.Y + LED_RADIUS));
                }
            }

            RenderRects(canvas, rects, paint, LED_RADIUS);
        }
        finally
        {
            ReturnPaint(paint);
        }
    }

    private void RenderActiveLeds(
        SKCanvas canvas,
        LedData data,
        QualitySettings settings)
    {
        int columns = Math.Min(data.GridData.Columns, _smoothedValues.Length);
        var activeRects = new List<(SKRect rect, SKColor color)>();
        var peakCircles = new List<(SKPoint center, float radius)>();

        for (int col = 0; col < columns; col++)
        {
            float value = _smoothedValues[col];
            int activeLeds = CalculateActiveLeds(value, data.GridData.Rows);

            for (int row = 0; row < activeLeds; row++)
            {
                float brightness = CalculateLedBrightness(value, row == activeLeds - 1);
                var pos = _ledPositions[col, row];
                var color = GetLedColor(row, data, brightness);

                activeRects.Add((
                    new SKRect(
                        pos.X - LED_RADIUS,
                        pos.Y - LED_RADIUS,
                        pos.X + LED_RADIUS,
                        pos.Y + LED_RADIUS),
                    color));
            }

            if (settings.UsePeakHold && _peakTimers[col] > 0)
            {
                int peakRow = CalculatePeakRow(col, data.GridData.Rows);
                if (peakRow >= 0 && peakRow < data.GridData.Rows)
                {
                    peakCircles.Add((_ledPositions[col, peakRow], LED_RADIUS + PEAK_RADIUS_OFFSET));
                }
            }
        }

        RenderBatchedLeds(canvas, activeRects);

        if (peakCircles.Count > 0)
            RenderBatchedPeaks(canvas, peakCircles, data);
    }

    private void RenderBatchedLeds(
    SKCanvas canvas,
    List<(SKRect rect, SKColor color)> leds)
    {
        if (leds.Count == 0) return;

        var groupedByColor = leds
            .GroupBy(led => led.color)
            .ToList();

        foreach (var group in groupedByColor)
        {
            var paint = CreatePaint(group.Key, SKPaintStyle.Fill);

            try
            {
                var rects = group.Select(g => g.rect).ToList();
                RenderRects(canvas, rects, paint, LED_RADIUS);
            }
            finally
            {
                ReturnPaint(paint);
            }
        }
    }

    private void RenderBatchedPeaks(
        SKCanvas canvas,
        List<(SKPoint center, float radius)> peaks,
        LedData data)
    {
        var peakColor = data.UseExternalColors
            ? data.ExternalBaseColor.WithAlpha((byte)PEAK_ALPHA_FACTOR)
            : _peakColor.WithAlpha((byte)PEAK_ALPHA_FACTOR);

        if (IsOverlayActive)
        {
            var alpha = (byte)(peakColor.Alpha * GetOverlayAlphaFactor());
            peakColor = peakColor.WithAlpha(alpha);
        }

        var paint = CreatePaint(peakColor, SKPaintStyle.Stroke);
        paint.StrokeWidth = PEAK_STROKE_WIDTH;

        try
        {
            foreach (var (center, radius) in peaks)
            {
                canvas.DrawCircle(center, radius, paint);
            }
        }
        finally
        {
            ReturnPaint(paint);
        }
    }

    private static int CalculateActiveLeds(float value, int maxRows)
    {
        int activeLeds = (int)(value * maxRows);

        if (activeLeds == 0 && value > MIN_VALUE_THRESHOLD)
            activeLeds = 1;

        return activeLeds;
    }

    private static float CalculateLedBrightness(float value, bool isTopLed)
    {
        float brightness = Lerp(MIN_ACTIVE_BRIGHTNESS, 1f, value);

        if (isTopLed)
            brightness *= TOP_LED_BRIGHTNESS_BOOST;

        return Math.Min(brightness, 1f);
    }

    private int CalculatePeakRow(int col, int maxRows) =>
        (int)(_peakValues[col] * maxRows) - 1;

    private SKColor GetLedColor(int row, LedData data, float brightness)
    {
        var baseColor = _rowColors[Math.Min(row, _rowColors.Length - 1)];

        if (data.UseExternalColors)
            baseColor = BlendWithExternalColor(baseColor, row, data.ExternalBaseColor);

        float adjustedBrightness = IsOverlayActive
            ? brightness * GetOverlayAlphaFactor()
            : brightness;

        return baseColor.WithAlpha((byte)(adjustedBrightness * 255));
    }

    private SKColor BlendWithExternalColor(
        SKColor baseColor,
        int row,
        SKColor externalColor)
    {
        float t = row / (float)_rowColors.Length;

        return new SKColor(
            BlendColorComponent(externalColor.Red, baseColor.Red, EXTERNAL_COLOR_BLEND, t),
            BlendColorComponent(externalColor.Green, baseColor.Green, EXTERNAL_COLOR_BLEND, t),
            BlendColorComponent(externalColor.Blue, baseColor.Blue, EXTERNAL_COLOR_BLEND, t));
    }

    private static byte BlendColorComponent(
        byte external,
        byte gradient,
        float blend,
        float t) =>
        (byte)(external * blend + gradient * (1 - blend) * t);

    protected override int GetMaxBarsForQuality() => Quality switch
    {
        RenderQuality.Low => 32,
        RenderQuality.Medium => 48,
        RenderQuality.High => 64,
        _ => 48
    };

    protected override void OnQualitySettingsApplied()
    {
        base.OnQualitySettingsApplied();

        float smoothingFactor = Quality switch
        {
            RenderQuality.Low => 0.4f,
            RenderQuality.Medium => 0.3f,
            RenderQuality.High => 0.25f,
            _ => 0.3f
        };

        if (IsOverlayActive)
            smoothingFactor *= 1.2f;

        SetProcessingSmoothingFactor(smoothingFactor);

        _gridData = null;

        RequestRedraw();
    }

    protected override void OnDispose()
    {
        _gridData = null;
        Array.Clear(_smoothedValues, 0, _smoothedValues.Length);
        Array.Clear(_peakValues, 0, _peakValues.Length);
        Array.Clear(_peakTimers, 0, _peakTimers.Length);
        _useExternalColors = false;
        _externalBaseColor = SKColors.White;

        base.OnDispose();
    }

    private record GridData(
        int Rows,
        int Columns,
        float CellSize,
        float StartX,
        float StartY,
        bool IsOverlay);

    private record LedData(
        GridData GridData,
        bool UseExternalColors,
        SKColor ExternalBaseColor);
}