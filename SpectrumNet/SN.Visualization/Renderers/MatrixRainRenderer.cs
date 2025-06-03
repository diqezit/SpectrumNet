#nullable enable

namespace SpectrumNet.SN.Visualization.Renderers;

public sealed class MatrixRainRenderer : EffectSpectrumRenderer<MatrixRainRenderer.QualitySettings>
{
    private static readonly Lazy<MatrixRainRenderer> _instance =
        new(() => new MatrixRainRenderer());

    public static MatrixRainRenderer GetInstance() => _instance.Value;

    private const float COLUMN_WIDTH = 20f,
        COLUMN_SPACING = 5f,
        DROP_SPEED_BASE = 120f,
        DROP_SPEED_VARIANCE = 80f,
        DROP_SPEED_OVERLAY_MULTIPLIER = 0.8f,
        CHAR_SIZE_BASE = 18f,
        CHAR_SIZE_OVERLAY = 15f,
        GLOW_RADIUS_BASE = 3f,
        GLOW_RADIUS_OVERLAY = 2f,
        SPECTRUM_INFLUENCE = 0.7f,
        SPECTRUM_INFLUENCE_OVERLAY = 0.5f,
        NEW_DROP_CHANCE = 0.015f,
        NEW_DROP_INTENSITY_MULTIPLIER = 0.15f,
        CHAR_CHANGE_CHANCE = 0.08f,
        CHAR_CHANGE_CHANCE_HIGH = 0.12f,
        ANIMATION_DELTA_TIME = 0.016f,
        HEAD_GLOW_SIZE_MULTIPLIER = 0.7f,
        TRAIL_FADE_POWER = 1.5f,
        BACKGROUND_ALPHA = 240,
        BACKGROUND_ALPHA_OVERLAY = 200;

    private const byte HEAD_ALPHA = 255,
        HEAD_GLOW_ALPHA = 120,
        HEAD_GLOW_ALPHA_OVERLAY = 80,
        TRAIL_START_ALPHA = 220,
        TRAIL_END_ALPHA = 15,
        TRAIL_GLOW_ALPHA = 60,
        TRAIL_GLOW_ALPHA_OVERLAY = 40;

    private const int MIN_COLUMNS = 10,
        MAX_COLUMNS = 100,
        MAX_COLUMNS_OVERLAY = 80,
        DEFAULT_MAX_TRAIL_LENGTH = 12,
        MIN_TRAIL_LENGTH = 4,
        SPECTRUM_BANDS = 32;

    private static readonly char[] _matrixChars =
        "アイウエオカキクケコサシスセソタチツテトナニヌネノハヒフヘホマミムメモヤユヨラリルレロワヲン0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ+=-*/<>[]{}()".ToCharArray();

    private static readonly SKColor _headColorBright = new(100, 255, 100),
        _headColorNormal = new(0, 255, 0),
        _trailColorStart = new(0, 220, 0),
        _trailColorEnd = new(0, 150, 0),
        _glowColor = new(50, 255, 50),
        _backgroundColor = new(0, 10, 0);

    private MatrixColumn[] _columns = [];
    private float[] _spectrumBands = new float[SPECTRUM_BANDS];
    private float _animationTime;
    private readonly Random _random = new();
    private bool _isInitialized;

    public sealed class QualitySettings
    {
        public bool UseGlow { get; init; }
        public bool UseCharacterVariation { get; init; }
        public bool UseSpeedVariation { get; init; }
        public bool UseTrailGradient { get; init; }
        public bool UseBackgroundTint { get; init; }
        public int MaxTrailLength { get; init; }
        public float CharacterDensity { get; init; }
        public float GlowIntensity { get; init; }
        public float AnimationSpeed { get; init; }
    }

    protected override IReadOnlyDictionary<RenderQuality, QualitySettings>
        QualitySettingsPresets
    { get; } = new Dictionary<RenderQuality, QualitySettings>
    {
        [RenderQuality.Low] = new()
        {
            UseGlow = false,
            UseCharacterVariation = false,
            UseSpeedVariation = true,
            UseTrailGradient = false,
            UseBackgroundTint = false,
            MaxTrailLength = 8,
            CharacterDensity = 0.7f,
            GlowIntensity = 0f,
            AnimationSpeed = 1f
        },
        [RenderQuality.Medium] = new()
        {
            UseGlow = true,
            UseCharacterVariation = true,
            UseSpeedVariation = true,
            UseTrailGradient = true,
            UseBackgroundTint = true,
            MaxTrailLength = 12,
            CharacterDensity = 0.85f,
            GlowIntensity = 0.6f,
            AnimationSpeed = 1.2f
        },
        [RenderQuality.High] = new()
        {
            UseGlow = true,
            UseCharacterVariation = true,
            UseSpeedVariation = true,
            UseTrailGradient = true,
            UseBackgroundTint = true,
            MaxTrailLength = 16,
            CharacterDensity = 1.0f,
            GlowIntensity = 1.0f,
            AnimationSpeed = 1.5f
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
        RenderParameters _)
    {
        EnsureInitialized(info);
        UpdateSpectrumBands(spectrum);
        UpdateAnimation(spectrum);

        return new RenderData(
            Columns: _columns,
            SpectrumBands: _spectrumBands,
            AverageIntensity: CalculateAverageIntensity(spectrum),
            CharSize: GetCharacterSize(),
            GlowRadius: GetGlowRadius());
    }

    private static bool ValidateRenderData(RenderData data) =>
        data.Columns.Length > 0 &&
        data.CharSize > 0 &&
        data.SpectrumBands.Length > 0;

    private void RenderVisualization(
        SKCanvas canvas,
        RenderData data,
        RenderParameters _,
        SKPaint __)
    {
        var settings = CurrentQualitySettings!;

        DrawBackground(canvas, data, settings);

        RenderWithOverlay(canvas, () =>
        {
            if (UseAdvancedEffects && settings.UseGlow)
                RenderGlowLayer(canvas, data, settings);

            RenderMatrixColumns(canvas, data, settings);

            if (UseAdvancedEffects && settings.UseGlow && settings.GlowIntensity > 0.8f)
                RenderHeadHighlights(canvas, data);
        });
    }

    private void DrawBackground(SKCanvas canvas, RenderData _, QualitySettings settings)
    {
        if (!settings.UseBackgroundTint)
        {
            canvas.Clear(SKColors.Black);
            return;
        }

        byte bgAlpha = (byte)(IsOverlayActive ? BACKGROUND_ALPHA_OVERLAY : BACKGROUND_ALPHA);
        var bgColor = _backgroundColor.WithAlpha(bgAlpha);

        var bgPaint = CreatePaint(bgColor, SKPaintStyle.Fill);
        try
        {
            canvas.DrawRect(canvas.LocalClipBounds, bgPaint);
        }
        finally
        {
            ReturnPaint(bgPaint);
        }
    }

    private void EnsureInitialized(SKImageInfo info)
    {
        if (!_isInitialized || NeedsReinitialization(info))
        {
            InitializeColumns(info);
            _isInitialized = true;
        }
    }

    private bool NeedsReinitialization(SKImageInfo info)
    {
        int requiredColumns = CalculateColumnCount(info.Width);
        return _columns.Length != requiredColumns;
    }

    private void InitializeColumns(SKImageInfo info)
    {
        int columnCount = CalculateColumnCount(info.Width);
        _columns = new MatrixColumn[columnCount];

        for (int i = 0; i < columnCount; i++)
            _columns[i] = CreateColumn(i, info.Height);
    }

    private int CalculateColumnCount(float width)
    {
        float totalColumnWidth = COLUMN_WIDTH + COLUMN_SPACING;
        int columns = (int)(width / totalColumnWidth);
        int maxColumns = IsOverlayActive ? MAX_COLUMNS_OVERLAY : MAX_COLUMNS;
        return Clamp(columns, MIN_COLUMNS, maxColumns);
    }

    private MatrixColumn CreateColumn(int index, float maxHeight)
    {
        var settings = CurrentQualitySettings!;

        float baseSpeed = settings.UseSpeedVariation
            ? DROP_SPEED_BASE + (float)_random.NextDouble() * DROP_SPEED_VARIANCE
            : DROP_SPEED_BASE;

        if (IsOverlayActive)
            baseSpeed *= DROP_SPEED_OVERLAY_MULTIPLIER;

        int maxTrailLength = settings.MaxTrailLength;
        int minTrailLength = Math.Min(MIN_TRAIL_LENGTH, maxTrailLength);
        int trailLength = _random.Next(minTrailLength, maxTrailLength + 1);

        float startY = ShouldStartColumnVisible(settings.CharacterDensity)
            ? (float)_random.NextDouble() * maxHeight
            : -trailLength * GetCharacterSize();

        return new MatrixColumn(
            X: index * (COLUMN_WIDTH + COLUMN_SPACING) + COLUMN_WIDTH / 2,
            Y: startY,
            Speed: baseSpeed * settings.AnimationSpeed,
            TrailLength: trailLength,
            Characters: GenerateCharacters(trailLength),
            MaxHeight: maxHeight,
            Intensity: 0f);
    }

    private bool ShouldStartColumnVisible(float density) =>
        _random.NextDouble() < density;

    private char[] GenerateCharacters(int count)
    {
        var chars = new char[count];
        for (int i = 0; i < count; i++)
            chars[i] = GetRandomCharacter();
        return chars;
    }

    private char GetRandomCharacter() =>
        _matrixChars[_random.Next(_matrixChars.Length)];

    private void UpdateSpectrumBands(float[] spectrum)
    {
        if (spectrum.Length == 0) return;

        int samplesPerBand = Math.Max(1, spectrum.Length / SPECTRUM_BANDS);

        for (int i = 0; i < SPECTRUM_BANDS; i++)
        {
            float sum = 0f;
            int startIdx = i * samplesPerBand;
            int endIdx = Math.Min(startIdx + samplesPerBand, spectrum.Length);

            for (int j = startIdx; j < endIdx; j++)
                sum += spectrum[j];

            _spectrumBands[i] = sum / (endIdx - startIdx);
        }
    }

    private void UpdateAnimation(float[] spectrum)
    {
        _animationTime += ANIMATION_DELTA_TIME * CurrentQualitySettings!.AnimationSpeed;
        float avgIntensity = CalculateAverageIntensity(spectrum);

        for (int i = 0; i < _columns.Length; i++)
        {
            if (_columns[i] != null)
                UpdateColumn(ref _columns[i], i, avgIntensity);
        }
    }

    private void UpdateColumn(ref MatrixColumn column, int index, float avgIntensity)
    {
        float spectrumInfluence = GetColumnSpectrumInfluence(index);
        column = column with { Intensity = spectrumInfluence };

        float speedMultiplier = CalculateSpeedMultiplier(spectrumInfluence);
        float deltaY = column.Speed * speedMultiplier * ANIMATION_DELTA_TIME;

        column = column with { Y = column.Y + deltaY };

        if (ShouldResetColumn(column))
            ResetColumn(ref column, avgIntensity);

        if (CurrentQualitySettings!.UseCharacterVariation)
            UpdateCharacters(ref column, spectrumInfluence);
    }

    private float GetColumnSpectrumInfluence(int columnIndex)
    {
        if (_spectrumBands.Length == 0 || _columns.Length == 0)
            return 0f;

        int bandIndex = (columnIndex * _spectrumBands.Length) / _columns.Length;
        bandIndex = Clamp(bandIndex, 0, _spectrumBands.Length - 1);

        return _spectrumBands[bandIndex];
    }

    private float CalculateSpeedMultiplier(float spectrumInfluence)
    {
        float influence = IsOverlayActive ? SPECTRUM_INFLUENCE_OVERLAY : SPECTRUM_INFLUENCE;
        return 1f + spectrumInfluence * influence;
    }

    private static bool ShouldResetColumn(MatrixColumn column) =>
        column.Y > column.MaxHeight + column.TrailLength * column.MaxHeight * 0.02f;

    private void ResetColumn(ref MatrixColumn column, float intensity)
    {
        float resetChance = NEW_DROP_CHANCE + intensity * NEW_DROP_INTENSITY_MULTIPLIER;

        if (_random.NextDouble() < resetChance)
        {
            column = column with
            {
                Y = -column.TrailLength * GetCharacterSize(),
                Speed = RecalculateColumnSpeed(),
                Characters = RegenerateCharacters(column)
            };
        }
    }

    private float RecalculateColumnSpeed()
    {
        var settings = CurrentQualitySettings!;

        float baseSpeed = settings.UseSpeedVariation
            ? DROP_SPEED_BASE + (float)_random.NextDouble() * DROP_SPEED_VARIANCE
            : DROP_SPEED_BASE;

        if (IsOverlayActive)
            baseSpeed *= DROP_SPEED_OVERLAY_MULTIPLIER;

        return baseSpeed * settings.AnimationSpeed;
    }

    private char[] RegenerateCharacters(MatrixColumn column)
    {
        if (_random.NextDouble() < 0.3f)
            return GenerateCharacters(column.Characters.Length);

        return column.Characters;
    }

    private void UpdateCharacters(ref MatrixColumn column, float intensity)
    {
        if (column.Characters == null || column.Characters.Length == 0)
            return;

        float changeChance = Quality == RenderQuality.High
            ? CHAR_CHANGE_CHANCE_HIGH
            : CHAR_CHANGE_CHANCE;

        changeChance *= (1f + intensity * 0.5f);

        for (int i = 0; i < column.Characters.Length; i++)
        {
            if (_random.NextDouble() < changeChance)
                column.Characters[i] = GetRandomCharacter();
        }
    }

    private void RenderGlowLayer(
        SKCanvas canvas,
        RenderData data,
        QualitySettings settings)
    {
        float glowRadius = data.GlowRadius * settings.GlowIntensity;
        if (glowRadius <= 0) return;

        using var glowFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, glowRadius);

        RenderColumnGlows(canvas, data, glowFilter, settings);
    }

    private void RenderColumnGlows(
        SKCanvas canvas,
        RenderData data,
        SKMaskFilter glowFilter,
        QualitySettings settings)
    {
        byte glowAlpha = IsOverlayActive ? HEAD_GLOW_ALPHA_OVERLAY : HEAD_GLOW_ALPHA;
        var glowPaint = CreatePaint(_glowColor.WithAlpha(glowAlpha), SKPaintStyle.Fill);
        glowPaint.MaskFilter = glowFilter;

        try
        {
            foreach (var column in data.Columns)
            {
                if (column != null && IsColumnHeadVisible(column))
                    RenderColumnHeadGlow(canvas, column, data.CharSize, glowPaint);
            }

            if (settings.UseTrailGradient)
                RenderTrailGlows(canvas, data, glowFilter);
        }
        finally
        {
            ReturnPaint(glowPaint);
        }
    }

    private static void RenderColumnHeadGlow(
        SKCanvas canvas,
        MatrixColumn column,
        float charSize,
        SKPaint glowPaint)
    {
        float glowSize = charSize * HEAD_GLOW_SIZE_MULTIPLIER * (1f + column.Intensity * 0.3f);
        canvas.DrawCircle(column.X, column.Y, glowSize, glowPaint);
    }

    private void RenderTrailGlows(
        SKCanvas canvas,
        RenderData data,
        SKMaskFilter glowFilter)
    {
        byte trailGlowAlpha = IsOverlayActive ? TRAIL_GLOW_ALPHA_OVERLAY : TRAIL_GLOW_ALPHA;
        var trailGlowPaint = CreatePaint(_glowColor.WithAlpha(trailGlowAlpha), SKPaintStyle.Fill);
        trailGlowPaint.MaskFilter = glowFilter;

        try
        {
            foreach (var column in data.Columns)
            {
                if (column != null)
                    RenderColumnTrailGlow(canvas, column, data.CharSize, trailGlowPaint);
            }
        }
        finally
        {
            ReturnPaint(trailGlowPaint);
        }
    }

    private static void RenderColumnTrailGlow(
        SKCanvas canvas,
        MatrixColumn column,
        float charSize,
        SKPaint glowPaint)
    {
        float charHeight = charSize;
        int visibleChars = Math.Min(3, column.TrailLength);

        for (int i = 1; i <= visibleChars; i++)
        {
            float charY = column.Y - i * charHeight;
            if (!IsCharacterVisible(charY, column.MaxHeight, charHeight))
                continue;

            float fadeProgress = i / (float)visibleChars;
            byte fadeAlpha = (byte)(glowPaint.Color.Alpha * (1f - fadeProgress * 0.7f));

            glowPaint.Color = glowPaint.Color.WithAlpha(fadeAlpha);
            canvas.DrawCircle(column.X, charY, charSize * 0.4f, glowPaint);
        }
    }

    private void RenderMatrixColumns(
        SKCanvas canvas,
        RenderData data,
        QualitySettings settings)
    {
        using var font = CreateMatrixFont(data.CharSize);

        foreach (var column in data.Columns)
        {
            if (column != null)
                RenderColumn(canvas, column, font, settings);
        }
    }

    private static SKFont CreateMatrixFont(float size) =>
        new(SKTypeface.FromFamilyName("Consolas", SKFontStyle.Bold), size);

    private void RenderColumn(
        SKCanvas canvas,
        MatrixColumn column,
        SKFont font,
        QualitySettings settings)
    {
        if (column.Characters == null || column.Characters.Length == 0)
            return;

        float charHeight = GetCharacterSize();

        for (int i = 0; i < column.TrailLength && i < column.Characters.Length; i++)
        {
            float charY = column.Y - i * charHeight;

            if (!IsCharacterVisible(charY, column.MaxHeight, charHeight))
                continue;

            var appearance = CalculateCharacterAppearance(i, column, settings);

            RenderCharacter(
                canvas,
                column.Characters[i],
                column.X,
                charY,
                appearance.Color,
                font);
        }
    }

    private static CharacterAppearance CalculateCharacterAppearance(
        int position,
        MatrixColumn column,
        QualitySettings settings)
    {
        if (position == 0)
            return CreateHeadAppearance(column.Intensity);

        return settings.UseTrailGradient
            ? CreateGradientTrailAppearance(position, column.TrailLength)
            : CreateSimpleTrailAppearance(position, column.TrailLength);
    }

    private static CharacterAppearance CreateHeadAppearance(float intensity)
    {
        var color = intensity > 0.7f ? _headColorBright : _headColorNormal;
        return new CharacterAppearance(color.WithAlpha(HEAD_ALPHA));
    }

    private static CharacterAppearance CreateGradientTrailAppearance(int position, int trailLength)
    {
        float progress = position / (float)trailLength;
        progress = MathF.Pow(progress, TRAIL_FADE_POWER);

        var color = InterpolateColor(_trailColorStart, _trailColorEnd, progress);
        byte alpha = (byte)Lerp(TRAIL_START_ALPHA, TRAIL_END_ALPHA, progress);

        return new CharacterAppearance(color.WithAlpha(alpha));
    }

    private static CharacterAppearance CreateSimpleTrailAppearance(int position, int trailLength)
    {
        float progress = position / (float)trailLength;
        byte alpha = (byte)Lerp(TRAIL_START_ALPHA, TRAIL_END_ALPHA, progress);

        return new CharacterAppearance(_trailColorStart.WithAlpha(alpha));
    }

    private static SKColor InterpolateColor(SKColor from, SKColor to, float t)
    {
        t = Clamp(t, 0f, 1f);
        return new SKColor(
            (byte)Lerp(from.Red, to.Red, t),
            (byte)Lerp(from.Green, to.Green, t),
            (byte)Lerp(from.Blue, to.Blue, t));
    }

    private void RenderCharacter(
        SKCanvas canvas,
        char character,
        float x,
        float y,
        SKColor color,
        SKFont font)
    {
        var paint = CreatePaint(color, SKPaintStyle.Fill);

        try
        {
            canvas.DrawText(
                character.ToString(),
                x,
                y,
                SKTextAlign.Center,
                font,
                paint);
        }
        finally
        {
            ReturnPaint(paint);
        }
    }

    private void RenderHeadHighlights(
        SKCanvas canvas,
        RenderData data)
    {
        var highlightPaint = CreatePaint(SKColors.White.WithAlpha(100), SKPaintStyle.Fill);

        try
        {
            foreach (var column in data.Columns)
            {
                if (column != null && IsColumnHeadVisible(column) && column.Intensity > 0.8f)
                {
                    canvas.DrawCircle(
                        column.X,
                        column.Y,
                        data.CharSize * 0.2f,
                        highlightPaint);
                }
            }
        }
        finally
        {
            ReturnPaint(highlightPaint);
        }
    }

    private static bool IsColumnHeadVisible(MatrixColumn column) =>
        column.Y >= 0 && column.Y <= column.MaxHeight;

    private static bool IsCharacterVisible(float y, float maxHeight, float charHeight) =>
        y >= -charHeight && y <= maxHeight + charHeight;

    private float GetCharacterSize() =>
        IsOverlayActive ? CHAR_SIZE_OVERLAY : CHAR_SIZE_BASE;

    private float GetGlowRadius() =>
        IsOverlayActive ? GLOW_RADIUS_OVERLAY : GLOW_RADIUS_BASE;

    private static float CalculateAverageIntensity(float[] spectrum)
    {
        if (spectrum.Length == 0) return 0f;

        float sum = 0f;
        foreach (float value in spectrum)
            sum += value;

        return sum / spectrum.Length;
    }

    protected override int GetMaxBarsForQuality() => Quality switch
    {
        RenderQuality.Low => 50,
        RenderQuality.Medium => 75,
        RenderQuality.High => 100,
        _ => 75
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

        _isInitialized = false;
        _spectrumBands = new float[SPECTRUM_BANDS];

        RequestRedraw();
    }

    protected override void OnDispose()
    {
        _columns = [];
        _spectrumBands = [];
        _animationTime = 0f;
        _isInitialized = false;
        base.OnDispose();
    }

    private record MatrixColumn(
        float X,
        float Y,
        float Speed,
        int TrailLength,
        char[] Characters,
        float MaxHeight,
        float Intensity);

    private record RenderData(
        MatrixColumn[] Columns,
        float[] SpectrumBands,
        float AverageIntensity,
        float CharSize,
        float GlowRadius);

    private record CharacterAppearance(SKColor Color);
}