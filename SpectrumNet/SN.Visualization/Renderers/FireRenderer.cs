#nullable enable

namespace SpectrumNet.SN.Visualization.Renderers;

public sealed class FireRenderer : EffectSpectrumRenderer<FireRenderer.QualitySettings>
{
    private static readonly Lazy<FireRenderer> _instance =
        new(() => new FireRenderer());

    public static FireRenderer GetInstance() => _instance.Value;

    private const float PIXEL_SIZE = 8f,
        PIXEL_SIZE_OVERLAY = 5f,
        FLAME_DECAY_RATE = 0.93f,
        FLAME_SPREAD = 0.4f,
        ANIMATION_SPEED = 0.06f,
        WIND_STRENGTH = 0.25f,
        WIND_SPEED = 2.5f,
        MIN_INTENSITY = 0.02f,
        COOLING_BASE = 0.12f,
        COOLING_HEIGHT_FACTOR = 0.4f,
        SOURCE_INTENSITY_SECONDARY = 0.9f,
        SOURCE_INTENSITY_TERTIARY = 0.75f,
        SOURCE_PROBABILITY = 0.95f,
        SOURCE_PROBABILITY_SECONDARY = 0.8f,
        SPREAD_RANDOMNESS = 0.5f,
        SMOOTH_KERNEL_CENTER = 4f,
        SMOOTH_KERNEL_TOTAL = 8f,
        INTENSITY_MULTIPLIER = 1.3f,
        WIND_VARIANCE = 0.1f;

    private const int FIRE_GRID_HEIGHT = 40,
        FIRE_GRID_HEIGHT_OVERLAY = 28,
        SOURCE_ROWS = 4,
        MIN_GRID_WIDTH = 10;

    private static readonly SKColor[] _fireColors =
    [
        SKColors.Black,
        new SKColor(24, 0, 0),
        new SKColor(48, 0, 0),
        new SKColor(96, 0, 0),
        new SKColor(144, 0, 0),
        new SKColor(192, 0, 0),
        new SKColor(224, 0, 0),
        SKColors.Red,
        new SKColor(255, 48, 0),
        new SKColor(255, 96, 0),
        new SKColor(255, 144, 0),
        SKColors.Orange,
        new SKColor(255, 192, 0),
        new SKColor(255, 224, 0),
        SKColors.Yellow,
        new SKColor(255, 255, 192),
        SKColors.White
    ];

    private readonly Random _random = new();
    private float[,] _fireGrid = new float[0, 0];
    private float[,] _coolingMap = new float[0, 0];
    private float _animationTime;
    private int _currentGridWidth;
    private int _currentGridHeight;

    public sealed class QualitySettings
    {
        public float CoolingRate { get; init; }
        public float SpreadChance { get; init; }
        public bool UseSmoothing { get; init; }
        public bool UseWind { get; init; }
        public int ColorLevels { get; init; }
        public float IntensityBoost { get; init; }
        public float DecayRate { get; init; }
    }

    protected override IReadOnlyDictionary<RenderQuality, QualitySettings>
        QualitySettingsPresets
    { get; } = new Dictionary<RenderQuality, QualitySettings>
    {
        [RenderQuality.Low] = new()
        {
            CoolingRate = 0.06f,
            SpreadChance = 0.25f,
            UseSmoothing = false,
            UseWind = false,
            ColorLevels = 8,
            IntensityBoost = 1.0f,
            DecayRate = 0.91f
        },
        [RenderQuality.Medium] = new()
        {
            CoolingRate = 0.045f,
            SpreadChance = 0.35f,
            UseSmoothing = true,
            UseWind = true,
            ColorLevels = 12,
            IntensityBoost = 1.15f,
            DecayRate = 0.93f
        },
        [RenderQuality.High] = new()
        {
            CoolingRate = 0.035f,
            SpreadChance = 0.45f,
            UseSmoothing = true,
            UseWind = true,
            ColorLevels = 17,
            IntensityBoost = 1.25f,
            DecayRate = 0.94f
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

        var fireData = CalculateFireData(
            processedSpectrum,
            info,
            renderParams);

        if (!ValidateFireData(fireData))
            return;

        RenderFireVisualization(
            canvas,
            fireData,
            renderParams,
            passedInPaint);
    }

    private FireData CalculateFireData(
        float[] spectrum,
        SKImageInfo info,
        RenderParameters renderParams)
    {
        var settings = CurrentQualitySettings!;

        UpdateAnimation();

        int gridHeight = GetGridHeight();
        float pixelSize = GetPixelSize();
        int gridWidth = Math.Max(MIN_GRID_WIDTH, (int)(info.Width / pixelSize));

        EnsureGridSize(gridWidth, gridHeight);
        UpdateFireSimulation(spectrum, renderParams, settings);

        return new FireData(
            Grid: (float[,])_fireGrid.Clone(),
            GridWidth: gridWidth,
            GridHeight: gridHeight,
            PixelSize: pixelSize,
            CanvasHeight: info.Height);
    }

    private static bool ValidateFireData(FireData data) =>
        data.Grid != null &&
        data.GridWidth > 0 &&
        data.GridHeight > 0 &&
        data.PixelSize > 0 &&
        data.CanvasHeight > 0;

    private void RenderFireVisualization(
        SKCanvas canvas,
        FireData data,
        RenderParameters renderParams,
        SKPaint passedInPaint)
    {
        var settings = CurrentQualitySettings!;

        RenderWithOverlay(canvas, () =>
        {
            RenderPixelFire(canvas, data, settings);
        });
    }

    private void UpdateAnimation()
    {
        _animationTime += ANIMATION_SPEED;
        if (_animationTime > MathF.Tau)
            _animationTime -= MathF.Tau;
    }

    private void EnsureGridSize(int width, int height)
    {
        if (_fireGrid.GetLength(0) != width || _fireGrid.GetLength(1) != height)
        {
            _fireGrid = new float[width, height];
            _coolingMap = new float[width, height];
            _currentGridWidth = width;
            _currentGridHeight = height;

            InitializeCoolingMap();
        }
    }

    private void InitializeCoolingMap()
    {
        for (int x = 0; x < _currentGridWidth; x++)
        {
            for (int y = 0; y < _currentGridHeight; y++)
            {
                float heightFactor = (float)y / _currentGridHeight;
                float randomFactor = (float)_random.NextDouble() * COOLING_BASE;
                float positionVariance = MathF.Sin(x * 0.15f) * 0.08f;

                _coolingMap[x, y] = randomFactor +
                    heightFactor * COOLING_HEIGHT_FACTOR +
                    positionVariance;
            }
        }
    }

    private void UpdateFireSimulation(
        float[] spectrum,
        RenderParameters renderParams,
        QualitySettings settings)
    {
        SetFireSource(spectrum, renderParams, settings);
        PropagateFireUpward(settings);

        if (settings.UseSmoothing)
            SmoothFireGrid();
    }

    private void SetFireSource(
        float[] spectrum,
        RenderParameters renderParams,
        QualitySettings settings)
    {
        float pixelSize = GetPixelSize();

        for (int i = 0; i < spectrum.Length && i < renderParams.EffectiveBarCount; i++)
        {
            float boostedIntensity = spectrum[i] * settings.IntensityBoost * INTENSITY_MULTIPLIER;
            if (boostedIntensity < MIN_INTENSITY) continue;

            var (startX, endX) = CalculateBarPixelRange(i, renderParams, pixelSize);
            SetBarFireSource(startX, endX, boostedIntensity);
        }
    }

    private (int startX, int endX) CalculateBarPixelRange(
        int barIndex,
        RenderParameters renderParams,
        float pixelSize)
    {
        float xStart = (renderParams.StartOffset +
            barIndex * (renderParams.BarWidth + renderParams.BarSpacing)) / pixelSize;
        float xEnd = xStart + renderParams.BarWidth / pixelSize;

        int startX = Math.Max(0, (int)xStart);
        int endX = Math.Min(_currentGridWidth - 1, (int)xEnd);

        return (startX, endX);
    }

    private void SetBarFireSource(int startX, int endX, float intensity)
    {
        int bottomRow = _currentGridHeight - 1;

        for (int x = startX; x <= endX; x++)
        {
            _fireGrid[x, bottomRow] = intensity;

            if (bottomRow > 0 && _random.NextDouble() < SOURCE_PROBABILITY)
                _fireGrid[x, bottomRow - 1] = intensity * SOURCE_INTENSITY_SECONDARY;

            if (bottomRow > 1 && _random.NextDouble() < SOURCE_PROBABILITY_SECONDARY)
                _fireGrid[x, bottomRow - 2] = intensity * SOURCE_INTENSITY_TERTIARY;

            for (int row = 3; row < SOURCE_ROWS && bottomRow - row >= 0; row++)
                if (_random.NextDouble() < SOURCE_PROBABILITY_SECONDARY * (1f - row * 0.2f))
                    _fireGrid[x, bottomRow - row] = intensity * (1f - row * 0.15f);
        }
    }

    private void PropagateFireUpward(QualitySettings settings)
    {
        for (int y = 0; y < _currentGridHeight - SOURCE_ROWS; y++)
            for (int x = 0; x < _currentGridWidth; x++)
                UpdateFireCell(x, y, settings);
    }

    private void UpdateFireCell(int x, int y, QualitySettings settings)
    {
        float cooling = _coolingMap[x, y] * settings.CoolingRate;
        float windOffset = CalculateWindOffset(y, settings);
        float heat = CalculateHeatFromBelow(x, y, windOffset);

        heat = ApplyFirePhysics(heat, cooling, settings);
        _fireGrid[x, y] = Math.Max(0, Math.Min(1, heat));
    }

    private float CalculateWindOffset(int y, QualitySettings settings)
    {
        if (!settings.UseWind) return 0f;

        float heightFactor = 1f - (float)y / _currentGridHeight;
        float windPhase = _animationTime * WIND_SPEED + y * WIND_VARIANCE;

        return MathF.Sin(windPhase) * WIND_STRENGTH * heightFactor;
    }

    private float CalculateHeatFromBelow(int x, int y, float windOffset)
    {
        float heat = 0f;
        int samples = 0;

        for (int dx = -1; dx <= 1; dx++)
        {
            int sourceX = x + dx + (int)windOffset;
            sourceX = Math.Clamp(sourceX, 0, _currentGridWidth - 1);

            if (y + 1 < _currentGridHeight)
            {
                heat += _fireGrid[sourceX, y + 1];
                samples++;
            }
        }

        return samples > 0 ? heat / samples : 0f;
    }

    private float ApplyFirePhysics(float heat, float cooling, QualitySettings settings)
    {
        heat *= settings.DecayRate;
        heat -= cooling;

        if (_random.NextDouble() < settings.SpreadChance)
        {
            float spread = ((float)_random.NextDouble() - SPREAD_RANDOMNESS) * FLAME_SPREAD;
            heat += spread * heat;
        }

        return heat;
    }

    private void SmoothFireGrid()
    {
        var tempGrid = new float[_currentGridWidth, _currentGridHeight];

        for (int x = 1; x < _currentGridWidth - 1; x++)
            for (int y = 1; y < _currentGridHeight - 1; y++)
                tempGrid[x, y] = CalculateSmoothedValue(x, y);

        CopySmoothedValues(tempGrid);
    }

    private float CalculateSmoothedValue(int x, int y)
    {
        float sum = _fireGrid[x, y] * SMOOTH_KERNEL_CENTER +
            _fireGrid[x - 1, y] +
            _fireGrid[x + 1, y] +
            _fireGrid[x, y - 1] +
            _fireGrid[x, y + 1];

        return sum / SMOOTH_KERNEL_TOTAL;
    }

    private void CopySmoothedValues(float[,] tempGrid)
    {
        for (int x = 1; x < _currentGridWidth - 1; x++)
            for (int y = 1; y < _currentGridHeight - 1; y++)
                _fireGrid[x, y] = tempGrid[x, y];
    }

    private void RenderPixelFire(
        SKCanvas canvas,
        FireData data,
        QualitySettings settings)
    {
        var pixelPaint = CreatePaint(SKColors.Red, SKPaintStyle.Fill);

        try
        {
            RenderFirePixels(canvas, data, settings, pixelPaint);
        }
        finally
        {
            ReturnPaint(pixelPaint);
        }
    }

    private void RenderFirePixels(
        SKCanvas canvas,
        FireData data,
        QualitySettings settings,
        SKPaint pixelPaint)
    {
        for (int x = 0; x < data.GridWidth; x++)
            for (int y = 0; y < data.GridHeight; y++)
                RenderSinglePixel(canvas, data, x, y, settings, pixelPaint);
    }

    private void RenderSinglePixel(
        SKCanvas canvas,
        FireData data,
        int x,
        int y,
        QualitySettings settings,
        SKPaint pixelPaint)
    {
        float intensity = data.Grid[x, y];
        if (intensity < 0.01f) return;

        var color = GetFireColor(intensity, settings.ColorLevels);
        pixelPaint.Color = color;

        float pixelX = x * data.PixelSize;
        float pixelY = data.CanvasHeight - (data.GridHeight - y) * data.PixelSize;

        canvas.DrawRect(
            pixelX,
            pixelY,
            data.PixelSize,
            data.PixelSize,
            pixelPaint);
    }

    private static SKColor GetFireColor(float intensity, int colorLevels)
    {
        float normalizedIntensity = Math.Clamp(intensity, 0f, 1f);
        int colorIndex = (int)(normalizedIntensity * (colorLevels - 1));
        colorIndex = Math.Clamp(colorIndex, 0, _fireColors.Length - 1);

        byte alpha = CalculateAlpha(normalizedIntensity);
        return _fireColors[colorIndex].WithAlpha(alpha);
    }

    private float GetPixelSize() =>
        IsOverlayActive ? PIXEL_SIZE_OVERLAY : PIXEL_SIZE;

    private int GetGridHeight() =>
        IsOverlayActive ? FIRE_GRID_HEIGHT_OVERLAY : FIRE_GRID_HEIGHT;

    protected override int GetMaxBarsForQuality() => Quality switch
    {
        RenderQuality.Low => 50,
        RenderQuality.Medium => 100,
        RenderQuality.High => 150,
        _ => 100
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

        _fireGrid = new float[0, 0];
        _coolingMap = new float[0, 0];

        RequestRedraw();
    }

    protected override void OnDispose()
    {
        _fireGrid = new float[0, 0];
        _coolingMap = new float[0, 0];
        _animationTime = 0f;
        _currentGridWidth = 0;
        _currentGridHeight = 0;
        base.OnDispose();
    }

    private record FireData(
        float[,] Grid,
        int GridWidth,
        int GridHeight,
        float PixelSize,
        float CanvasHeight);
}