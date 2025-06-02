#nullable enable

using SpectrumNet.SN.Spectrum.Models;

namespace SpectrumNet.SN.Visualization.Renderers;

public sealed class PixelGridRenderer : EffectSpectrumRenderer<PixelGridRenderer.QualitySettings>
{
    private static readonly Lazy<PixelGridRenderer> _instance =
        new(() => new PixelGridRenderer());

    public static PixelGridRenderer GetInstance() => _instance.Value;

    private const float PIXEL_SIZE = 12f,
        PIXEL_MARGIN = 2f,
        CORNER_RADIUS = 2f,
        CORNER_RADIUS_OVERLAY = 1.5f,
        SPECTRUM_SCALE = 1.5f,
        COLUMN_DECAY_RATE = 0.2f,
        COLUMN_RISE_RATE = 0.8f,
        PEAK_FALL_SPEED = 0.05f,
        INACTIVE_BRIGHTNESS = 0.15f,
        BORDER_WIDTH = 0.5f,
        BORDER_WIDTH_OVERLAY = 0.3f,
        GRID_POSITION_TOLERANCE = 0.1f;

    private const byte INACTIVE_ALPHA = 40,
        ACTIVE_ALPHA = 255,
        PEAK_ALPHA = 255,
        BORDER_ALPHA = 60;

    private const int MIN_GRID_SIZE = 8,
        MAX_COLUMNS = 128;

    private GridData? _gridData;
    private float[][] _columnHeights = [];
    private float[] _peakHeights = [];

    public sealed class QualitySettings
    {
        public bool UseRoundedPixels { get; init; }
        public bool ShowPeaks { get; init; }
        public bool UseBorders { get; init; }
        public int MaxRows { get; init; }
        public float SmoothingFactor { get; init; }
    }

    protected override IReadOnlyDictionary<RenderQuality, QualitySettings>
        QualitySettingsPresets
    { get; } = new Dictionary<RenderQuality, QualitySettings>
    {
        [RenderQuality.Low] = new()
        {
            UseRoundedPixels = true,
            ShowPeaks = true,
            UseBorders = false,
            MaxRows = 16,
            SmoothingFactor = 0.3f
        },
        [RenderQuality.Medium] = new()
        {
            UseRoundedPixels = true,
            ShowPeaks = true,
            UseBorders = false,
            MaxRows = 24,
            SmoothingFactor = 0.2f
        },
        [RenderQuality.High] = new()
        {
            UseRoundedPixels = true,
            ShowPeaks = true,
            UseBorders = true,
            MaxRows = 32,
            SmoothingFactor = 0.15f
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

        var renderData = CalculateRenderData(
            processedSpectrum,
            info,
            renderParams);

        if (!ValidateRenderData(renderData))
            return;

        RenderVisualization(
            canvas,
            renderData,
            renderParams,
            passedInPaint);
    }

    private RenderData CalculateRenderData(
        float[] spectrum,
        SKImageInfo info,
        RenderParameters renderParams)
    {
        UpdateGridIfNeeded(info, renderParams.EffectiveBarCount);
        UpdateColumnData(spectrum);

        return new RenderData(
            GridData: _gridData!,
            ColumnHeights: _columnHeights,
            PeakHeights: _peakHeights);
    }

    private static bool ValidateRenderData(RenderData data) =>
        data.GridData != null &&
        data.GridData.Columns > 0 &&
        data.GridData.Rows > 0 &&
        data.ColumnHeights.Length == data.GridData.Columns;

    private void RenderVisualization(
        SKCanvas canvas,
        RenderData data,
        RenderParameters renderParams,
        SKPaint basePaint)
    {
        var settings = CurrentQualitySettings!;

        RenderWithOverlay(canvas, () =>
        {
            RenderInactiveLayer(canvas, data, basePaint.Color, settings);
            RenderActiveLayer(canvas, data, basePaint.Color, settings);

            if (settings.ShowPeaks)
                RenderPeakLayer(canvas, data, basePaint.Color, settings);
        });
    }

    private void UpdateGridIfNeeded(SKImageInfo info, int requestedBarCount)
    {
        if (!NeedsGridUpdate(info, requestedBarCount))
            return;

        var gridParams = CalculateGridParameters(info, requestedBarCount);
        CreateGrid(gridParams);
    }

    private bool NeedsGridUpdate(SKImageInfo info, int requestedBarCount)
    {
        if (_gridData == null) return true;

        var newParams = CalculateGridParameters(info, requestedBarCount);
        return IsGridDifferent(newParams);
    }

    private GridParameters CalculateGridParameters(SKImageInfo info, int requestedBarCount)
    {
        var (columns, rows) = CalculateGridSize(info, requestedBarCount);
        var (cellSize, startX, startY) = CalculateLayout(info, columns, rows);

        return new GridParameters(
            Columns: columns,
            Rows: rows,
            CellSize: cellSize,
            StartX: startX,
            StartY: startY,
            IsOverlay: IsOverlayActive);
    }

    private bool IsGridDifferent(GridParameters newParams) =>
        _gridData!.Columns != newParams.Columns ||
        _gridData.Rows != newParams.Rows ||
        MathF.Abs(_gridData.CellSize - newParams.CellSize) > GRID_POSITION_TOLERANCE ||
        MathF.Abs(_gridData.StartX - newParams.StartX) > GRID_POSITION_TOLERANCE ||
        MathF.Abs(_gridData.StartY - newParams.StartY) > GRID_POSITION_TOLERANCE ||
        _gridData.IsOverlay != newParams.IsOverlay;

    private (int columns, int rows) CalculateGridSize(SKImageInfo info, int requestedBarCount)
    {
        float pixelTotalSize = PIXEL_SIZE + PIXEL_MARGIN;

        int columns = CalculateColumnCount(info.Width, requestedBarCount, pixelTotalSize);
        int rows = CalculateRowCount(info.Height, pixelTotalSize);

        return (columns, rows);
    }

    private int CalculateColumnCount(float width, int requestedBarCount, float pixelTotalSize)
    {
        int maxAllowed = Math.Min(MAX_COLUMNS, GetMaxBarsForQuality());
        int maxByWidth = (int)(width / pixelTotalSize);
        int columns = Math.Min(Math.Min(maxAllowed, requestedBarCount), maxByWidth);
        return Math.Max(MIN_GRID_SIZE, columns);
    }

    private int CalculateRowCount(float height, float pixelTotalSize)
    {
        int maxByHeight = (int)(height / pixelTotalSize);
        int rows = Math.Min(CurrentQualitySettings!.MaxRows, maxByHeight);
        return Math.Max(MIN_GRID_SIZE, rows);
    }

    private static (float cellSize, float startX, float startY) CalculateLayout(
        SKImageInfo info,
        int columns,
        int rows)
    {
        float cellSize = PIXEL_SIZE + PIXEL_MARGIN;
        float gridWidth = columns * cellSize - PIXEL_MARGIN;
        float gridHeight = rows * cellSize - PIXEL_MARGIN;

        float startX = (info.Width - gridWidth) * 0.5f;
        float startY = info.Height - gridHeight;

        return (cellSize, startX, startY);
    }

    private void CreateGrid(GridParameters parameters)
    {
        _gridData = new GridData(
            Rows: parameters.Rows,
            Columns: parameters.Columns,
            CellSize: parameters.CellSize,
            StartX: parameters.StartX,
            StartY: parameters.StartY,
            IsOverlay: parameters.IsOverlay);

        InitializeArrays();
        RequestRedraw();
    }

    private void InitializeArrays()
    {
        if (_gridData == null) return;

        _columnHeights = new float[_gridData.Columns][];
        for (int i = 0; i < _gridData.Columns; i++)
            _columnHeights[i] = new float[_gridData.Rows];

        _peakHeights = new float[_gridData.Columns];
    }

    private void UpdateColumnData(float[] spectrum)
    {
        if (_gridData == null) return;

        for (int column = 0; column < _gridData.Columns; column++)
        {
            float value = CalculateColumnValue(spectrum, column);
            UpdateColumnHeights(column, value);
            UpdatePeakHeight(column, value);
        }
    }

    private float CalculateColumnValue(float[] spectrum, int column)
    {
        if (_gridData == null) return 0f;

        float spectrumIndex = column * spectrum.Length / (float)_gridData.Columns;
        float interpolatedValue = InterpolateSpectrumValue(spectrum, spectrumIndex);

        return Clamp(interpolatedValue * SPECTRUM_SCALE, 0f, 1f);
    }

    private static float InterpolateSpectrumValue(float[] spectrum, float index)
    {
        int baseIndex = (int)index;
        float fraction = index - baseIndex;

        if (baseIndex + 1 < spectrum.Length)
            return Lerp(spectrum[baseIndex], spectrum[baseIndex + 1], fraction);

        return spectrum[baseIndex];
    }

    private void UpdateColumnHeights(int column, float value)
    {
        if (_gridData == null) return;

        float targetHeight = value * _gridData.Rows;
        float currentHeight = CalculateCurrentHeight(column);
        float smoothedHeight = SmoothHeight(currentHeight, targetHeight);

        DistributeHeightToPixels(column, smoothedHeight);
    }

    private float CalculateCurrentHeight(int column)
    {
        float height = 0;
        for (int y = 0; y < _gridData!.Rows; y++)
            height += _columnHeights[column][y];
        return height;
    }

    private static float SmoothHeight(float current, float target)
    {
        float rate = current > target ? COLUMN_DECAY_RATE : COLUMN_RISE_RATE;
        return Lerp(current, target, rate);
    }

    private void DistributeHeightToPixels(int column, float totalHeight)
    {
        int fullPixels = (int)totalHeight;
        float remainder = totalHeight - fullPixels;

        for (int y = 0; y < _gridData!.Rows; y++)
        {
            if (y < fullPixels)
                _columnHeights[column][y] = 1f;
            else if (y == fullPixels && remainder > 0)
                _columnHeights[column][y] = remainder;
            else
                _columnHeights[column][y] = 0f;
        }
    }

    private void UpdatePeakHeight(int column, float value)
    {
        if (!CurrentQualitySettings!.ShowPeaks || _gridData == null)
            return;

        if (value > _peakHeights[column])
            _peakHeights[column] = value;
        else
            _peakHeights[column] = Math.Max(0, _peakHeights[column] - PEAK_FALL_SPEED);
    }

    private void RenderInactiveLayer(
        SKCanvas canvas,
        RenderData data,
        SKColor baseColor,
        QualitySettings settings)
    {
        var inactiveRects = CollectInactivePixelRects(data);
        if (inactiveRects.Count == 0) return;

        var inactivePaint = CreatePaint(
            AdjustBrightness(baseColor, INACTIVE_BRIGHTNESS).WithAlpha(INACTIVE_ALPHA),
            SKPaintStyle.Fill);

        try
        {
            float cornerRadius = GetCornerRadius(settings);
            RenderRects(canvas, inactiveRects, inactivePaint, cornerRadius);
        }
        finally
        {
            ReturnPaint(inactivePaint);
        }
    }

    private void RenderActiveLayer(
        SKCanvas canvas,
        RenderData data,
        SKColor baseColor,
        QualitySettings settings)
    {
        var activeRects = CollectActivePixelRects(data);
        if (activeRects.Count == 0) return;

        var activePaint = CreatePaint(baseColor, SKPaintStyle.Fill);

        try
        {
            float cornerRadius = GetCornerRadius(settings);
            RenderRects(canvas, activeRects, activePaint, cornerRadius);

            if (UseAdvancedEffects && settings.UseBorders)
                RenderBorderLayer(canvas, activeRects, baseColor, settings);
        }
        finally
        {
            ReturnPaint(activePaint);
        }
    }

    private void RenderBorderLayer(
        SKCanvas canvas,
        List<SKRect> rects,
        SKColor baseColor,
        QualitySettings settings)
    {
        var borderPaint = CreatePaint(
            baseColor.WithAlpha(BORDER_ALPHA),
            SKPaintStyle.Stroke);

        borderPaint.StrokeWidth = IsOverlayActive ? BORDER_WIDTH_OVERLAY : BORDER_WIDTH;

        try
        {
            float cornerRadius = GetCornerRadius(settings);
            RenderRects(canvas, rects, borderPaint, cornerRadius);
        }
        finally
        {
            ReturnPaint(borderPaint);
        }
    }

    private void RenderPeakLayer(
        SKCanvas canvas,
        RenderData data,
        SKColor baseColor,
        QualitySettings settings)
    {
        var peakRects = CollectPeakRects(data);
        if (peakRects.Count == 0) return;

        var peakPaint = CreatePaint(
            baseColor.WithAlpha(PEAK_ALPHA),
            SKPaintStyle.Fill);

        try
        {
            float cornerRadius = GetCornerRadius(settings);
            RenderRects(canvas, peakRects, peakPaint, cornerRadius);
        }
        finally
        {
            ReturnPaint(peakPaint);
        }
    }

    private static List<SKRect> CollectInactivePixelRects(RenderData data)
    {
        var rects = new List<SKRect>();

        for (int x = 0; x < data.GridData.Columns; x++)
        {
            for (int y = 0; y < data.GridData.Rows; y++)
            {
                if (data.ColumnHeights[x][y] == 0)
                {
                    var rect = CalculatePixelRect(x, y, data.GridData);
                    rects.Add(rect);
                }
            }
        }

        return rects;
    }

    private static List<SKRect> CollectActivePixelRects(RenderData data)
    {
        var rects = new List<SKRect>();

        for (int x = 0; x < data.GridData.Columns; x++)
        {
            for (int y = 0; y < data.GridData.Rows; y++)
            {
                if (data.ColumnHeights[x][y] > 0)
                {
                    var rect = CalculatePixelRect(x, y, data.GridData);
                    rects.Add(rect);
                }
            }
        }

        return rects;
    }

    private static List<SKRect> CollectPeakRects(RenderData data)
    {
        var peakRects = new List<SKRect>();

        for (int x = 0; x < data.GridData.Columns; x++)
        {
            if (data.PeakHeights[x] <= 0)
                continue;

            int peakY = CalculatePeakRow(data.PeakHeights[x], data.GridData.Rows);
            if (!IsValidPeakRow(peakY, data.GridData.Rows))
                continue;

            var rect = CalculatePixelRect(x, peakY - 1, data.GridData);
            peakRects.Add(rect);
        }

        return peakRects;
    }

    private static int CalculatePeakRow(float peakHeight, int totalRows) =>
        (int)(peakHeight * totalRows);

    private static bool IsValidPeakRow(int peakRow, int totalRows) =>
        peakRow > 0 && peakRow <= totalRows;

    private float GetCornerRadius(QualitySettings settings) =>
        settings.UseRoundedPixels
            ? (IsOverlayActive ? CORNER_RADIUS_OVERLAY : CORNER_RADIUS)
            : 0f;

    private static SKRect CalculatePixelRect(int x, int y, GridData grid)
    {
        float left = grid.StartX + x * grid.CellSize;
        float top = grid.StartY + (grid.Rows - y - 1) * grid.CellSize;

        return new SKRect(
            left,
            top,
            left + PIXEL_SIZE,
            top + PIXEL_SIZE);
    }

    private static SKColor AdjustBrightness(SKColor color, float factor)
    {
        byte r = (byte)(color.Red * factor);
        byte g = (byte)(color.Green * factor);
        byte b = (byte)(color.Blue * factor);
        return new SKColor(r, g, b, color.Alpha);
    }

    protected override int GetMaxBarsForQuality() => Quality switch
    {
        RenderQuality.Low => 32,
        RenderQuality.Medium => 64,
        RenderQuality.High => 96,
        _ => 64
    };

    protected override void OnQualitySettingsApplied()
    {
        base.OnQualitySettingsApplied();

        float smoothingFactor = Quality switch
        {
            RenderQuality.Low => 0.3f,
            RenderQuality.Medium => 0.2f,
            RenderQuality.High => 0.15f,
            _ => 0.2f
        };

        if (IsOverlayActive)
        {
            smoothingFactor *= 1.2f;
        }

        SetProcessingSmoothingFactor(smoothingFactor);

        _gridData = null;

        RequestRedraw();
    }

    protected override void OnDispose()
    {
        _gridData = null;
        _columnHeights = [];
        _peakHeights = [];
        base.OnDispose();
    }

    private record GridData(
        int Rows,
        int Columns,
        float CellSize,
        float StartX,
        float StartY,
        bool IsOverlay);

    private record RenderData(
        GridData GridData,
        float[][] ColumnHeights,
        float[] PeakHeights);

    private record GridParameters(
        int Columns,
        int Rows,
        float CellSize,
        float StartX,
        float StartY,
        bool IsOverlay);
}