#nullable enable

namespace SpectrumNet.SN.Visualization.Renderers;

public sealed class HackerTextRenderer : EffectSpectrumRenderer<HackerTextRenderer.QualitySettings>
{
    private static readonly Lazy<HackerTextRenderer> _instance =
        new(() => new HackerTextRenderer());

    public static HackerTextRenderer GetInstance() => _instance.Value;

    private const string DEFAULT_TEXT = "HAKER2550";

    private const float 
        BASE_TEXT_SIZE = 96f,
        TEXT_Y_POSITION_RATIO = 0.5f,
        DEFAULT_BAR_COUNT = 100f,
        TEXT_SIZE_BAR_COUNT_SCALE = 0.3f,
        MIN_TEXT_SIZE_RATIO = 0.6f,
        MAX_TEXT_SIZE_RATIO = 3.5f,
        BASE_EXTRUSION_SCALE_FACTOR = 0.5f,
        JUMP_STRENGTH_MULTIPLIER = 80f,
        RETURN_FORCE_FACTOR = 0.1f,
        DAMPING_FACTOR = 0.85f,
        MIN_SPECTRUM_FOR_JUMP = 0.02f,
        LETTER_MAGNITUDE_SMOOTHING_FACTOR = 0.6f,
        TEXT_SIZE_SMOOTHING_FACTOR = 0.08f,
        TARGET_FPS_FOR_PHYSICS_SCALING = 60f,
        LOG_FACTOR = 0.5f,
        LOG_OFFSET = 0.9f,
        TEXT_SIZE_MAGNITUDE_POWER = 0.8f,
        TEXT_SIZE_DIFF_THRESHOLD = 0.05f,
        EXTRUSION_CLAMP = 3f,
        GLOW_MIN_OFFSET = 0.1f,
        PHYSICS_TIME_SCALE = 0.016f,
        OVERLAY_PHYSICS_DAMPING = 1.2f,
        OVERLAY_JUMP_REDUCTION = 0.7f,
        EXTRUSION_ALPHA_DECAY = 0.85f,
        EXTRUSION_COLOR_DARKEN_FACTOR = 0.7f,
        GRADIENT_HIGHLIGHT_FACTOR = 1.3f,
        GRADIENT_SHADOW_FACTOR = 0.6f,
        SHADOW_BLUR_SIGMA = 3f,
        SHADOW_OFFSET_MULTIPLIER = 0.3f,
        BEVEL_HIGHLIGHT_ALPHA = 0.4f,
        BEVEL_SHADOW_ALPHA = 0.3f,
        PERSPECTIVE_FACTOR = 0.15f,
        MAX_VELOCITY = 1000f,
        MAX_DISPLACEMENT = 500f,
        BOUNDS_PADDING = 10f;

    private const int 
        EXTRUSION_LAYERS_DEFAULT = 5,
        PHYSICS_SUBSTEPS = 2;

    private const byte 
        SHADOW_BASE_ALPHA = 50,
        EXTRUSION_MIN_ALPHA = 20;

    private static readonly float[] _gradientPositions = [0f, 1f];
    private static readonly float[] _bevelGradientStops = [0f, 1f];

    private readonly List<LetterPath> _letterPaths = [];
    private readonly List<LetterPhysicsState> _physicsStates = [];
    private readonly List<LetterStaticData> _staticData = [];
    private readonly Dictionary<SKColor, SKColor[]> _gradientCache = [];

    private SKFont? _font;
    private string _currentText = DEFAULT_TEXT;
    private bool _needsLayout;
    private float _currentExtrusionOffsetX;
    private float _currentExtrusionOffsetY;
    private float _smoothedTextSize = BASE_TEXT_SIZE;
    private float _lastBarCount;
    private float _barCountFactor;
    private float _lastUpdateTime;
    private float _lastTextSize;
    private SKRect _textBoundingBox = SKRect.Empty;
    private int _framesSinceLayoutUpdate;

    public sealed class QualitySettings
    {
        public float JumpStrengthMultiplier { get; init; }
        public float DampingFactor { get; init; }
        public float ReturnForceFactor { get; init; }
        public int ExtrusionLayers { get; init; }
        public float TextScaleFactor { get; init; }
        public float ExtrusionScaleFactor { get; init; }
        public bool UseGradient { get; init; }
        public bool UseExtrusion { get; init; }
        public bool UseShadow { get; init; }
        public bool UseBevel { get; init; }
        public bool UsePerspective { get; init; }
        public float ShadowIntensity { get; init; }
        public float BevelIntensity { get; init; }
        public int PhysicsSubsteps { get; init; }
        public bool UseBatchRendering { get; init; }
    }

    protected override IReadOnlyDictionary<RenderQuality, QualitySettings>
        QualitySettingsPresets
    { get; } = new Dictionary<RenderQuality, QualitySettings>
    {
        [RenderQuality.Low] = new()
        {
            JumpStrengthMultiplier = JUMP_STRENGTH_MULTIPLIER * 0.7f,
            DampingFactor = DAMPING_FACTOR * 1.05f,
            ReturnForceFactor = RETURN_FORCE_FACTOR * 0.8f,
            ExtrusionLayers = 2,
            TextScaleFactor = 0.7f,
            ExtrusionScaleFactor = 0.5f,
            UseGradient = false,
            UseExtrusion = true,
            UseShadow = false,
            UseBevel = false,
            UsePerspective = false,
            ShadowIntensity = 0f,
            BevelIntensity = 0f,
            PhysicsSubsteps = 1,
            UseBatchRendering = true
        },
        [RenderQuality.Medium] = new()
        {
            JumpStrengthMultiplier = JUMP_STRENGTH_MULTIPLIER,
            DampingFactor = DAMPING_FACTOR,
            ReturnForceFactor = RETURN_FORCE_FACTOR,
            ExtrusionLayers = EXTRUSION_LAYERS_DEFAULT,
            TextScaleFactor = 1.0f,
            ExtrusionScaleFactor = 1.0f,
            UseGradient = true,
            UseExtrusion = true,
            UseShadow = true,
            UseBevel = false,
            UsePerspective = true,
            ShadowIntensity = 0.5f,
            BevelIntensity = 0f,
            PhysicsSubsteps = PHYSICS_SUBSTEPS,
            UseBatchRendering = true
        },
        [RenderQuality.High] = new()
        {
            JumpStrengthMultiplier = JUMP_STRENGTH_MULTIPLIER * 1.3f,
            DampingFactor = DAMPING_FACTOR * 0.95f,
            ReturnForceFactor = RETURN_FORCE_FACTOR * 1.2f,
            ExtrusionLayers = EXTRUSION_LAYERS_DEFAULT + 3,
            TextScaleFactor = 1.3f,
            ExtrusionScaleFactor = 1.5f,
            UseGradient = true,
            UseExtrusion = true,
            UseShadow = true,
            UseBevel = true,
            UsePerspective = true,
            ShadowIntensity = 0.8f,
            BevelIntensity = 0.6f,
            PhysicsSubsteps = PHYSICS_SUBSTEPS + 1,
            UseBatchRendering = false
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

        var textData = CalculateTextData(
            processedSpectrum,
            info,
            renderParams);

        if (!ValidateTextData(textData))
            return;

        RenderTextVisualization(
            canvas,
            textData,
            renderParams,
            passedInPaint);
    }

    private TextRenderData CalculateTextData(
        float[] spectrum,
        SKImageInfo info,
        RenderParameters renderParams)
    {
        UpdateRenderParameters(renderParams, spectrum);

        if (_needsLayout || _framesSinceLayoutUpdate == 0)
            UpdateTextLayout(info);

        float deltaTime = CalculateDeltaTime();
        UpdatePhysicsStates(spectrum, info.Width, renderParams.EffectiveBarCount, deltaTime);

        return new TextRenderData(
            BoundingBox: _textBoundingBox,
            ExtrusionOffsetX: _currentExtrusionOffsetX,
            ExtrusionOffsetY: _currentExtrusionOffsetY,
            TextSize: _smoothedTextSize,
            AverageIntensity: CalculateAverageIntensity(spectrum),
            LetterCount: _staticData.Count);
    }

    private bool ValidateTextData(TextRenderData data)
    {
        if (data.LetterCount == 0 || _font == null)
            return false;

        if (data.BoundingBox.Width <= 0 || data.BoundingBox.Height <= 0)
            return false;

        if (_letterPaths.Count != data.LetterCount)
            return false;

        return _physicsStates.Count == data.LetterCount;
    }

    private void RenderTextVisualization(
        SKCanvas canvas,
        TextRenderData data,
        RenderParameters _,
        SKPaint basePaint)
    {
        var expandedBounds = ExpandBoundsForEffects(data.BoundingBox);
        if (!IsAreaVisible(canvas, expandedBounds))
            return;

        var settings = CurrentQualitySettings!;

        RenderWithOverlay(canvas, () =>
        {
            if (UseAdvancedEffects && settings.UseShadow && settings.ShadowIntensity > 0)
                RenderShadowLayer(canvas, data, settings);

            if (UseAdvancedEffects && settings.UseExtrusion && settings.ExtrusionLayers > 0)
                RenderExtrusionLayers(canvas, data, basePaint.Color, settings);

            RenderMainTextLayer(canvas, data, basePaint.Color, settings);

            if (UseAdvancedEffects && settings.UseBevel && settings.BevelIntensity > 0)
                RenderBevelLayer(canvas, data, settings);
        });
    }

    private SKRect ExpandBoundsForEffects(SKRect bounds)
    {
        var settings = CurrentQualitySettings!;
        float expansion = BOUNDS_PADDING;

        if (settings.UseShadow)
            expansion += SHADOW_BLUR_SIGMA * 2;

        if (settings.UseExtrusion)
            expansion += MathF.Max(MathF.Abs(_currentExtrusionOffsetX), MathF.Abs(_currentExtrusionOffsetY)) * settings.ExtrusionLayers;

        return SKRect.Inflate(bounds, expansion, expansion);
    }

    private void UpdateRenderParameters(
        RenderParameters renderParams,
        float[] spectrum)
    {
        if (_font == null)
            InitializeFont();

        UpdateBarCountFactor(renderParams.EffectiveBarCount);
        UpdateTextSize(spectrum);
        UpdateExtrusionOffsets(renderParams.BarWidth, renderParams.BarSpacing);
    }

    private void InitializeFont()
    {
        _font = new SKFont
        {
            Size = BASE_TEXT_SIZE,
            Edging = UseAntiAlias ? SKFontEdging.SubpixelAntialias : SKFontEdging.Alias
        };
        _smoothedTextSize = BASE_TEXT_SIZE;
        _lastTextSize = BASE_TEXT_SIZE;
        _needsLayout = true;
    }

    private void UpdateBarCountFactor(int barCount)
    {
        if (MathF.Abs(_lastBarCount - barCount) < 0.1f)
            return;

        _lastBarCount = barCount;
        float rawFactor = MathF.Log10(MathF.Max(1, barCount) / DEFAULT_BAR_COUNT + LOG_OFFSET) * LOG_FACTOR;
        _barCountFactor = Lerp(_barCountFactor, rawFactor, TEXT_SIZE_SMOOTHING_FACTOR);
    }

    private void UpdateTextSize(float[] spectrum)
    {
        float avgMagnitude = CalculateAverageIntensity(spectrum);
        float targetSize = CalculateTargetTextSize(avgMagnitude);
        ApplySmoothTextSizeTransition(targetSize);
    }

    private float CalculateTargetTextSize(float avgMagnitude)
    {
        float sizeFromBars = BASE_TEXT_SIZE * (1f + _barCountFactor * CurrentQualitySettings!.TextScaleFactor);
        float sizeFromSpectrum = BASE_TEXT_SIZE * (1f + MathF.Pow(avgMagnitude, TEXT_SIZE_MAGNITUDE_POWER) * TEXT_SIZE_BAR_COUNT_SCALE);
        return MathF.Max(sizeFromBars, sizeFromSpectrum);
    }

    private void ApplySmoothTextSizeTransition(float targetSize)
    {
        float minSize = BASE_TEXT_SIZE * MIN_TEXT_SIZE_RATIO;
        float maxSize = BASE_TEXT_SIZE * MAX_TEXT_SIZE_RATIO;
        targetSize = Clamp(targetSize, minSize, maxSize);

        _smoothedTextSize = Lerp(_smoothedTextSize, targetSize, TEXT_SIZE_SMOOTHING_FACTOR);

        if (MathF.Abs(_smoothedTextSize - _lastTextSize) > TEXT_SIZE_DIFF_THRESHOLD)
        {
            _font!.Size = _smoothedTextSize;
            _lastTextSize = _smoothedTextSize;
            _needsLayout = true;
        }
    }

    private void UpdateExtrusionOffsets(float barWidth, float barSpacing)
    {
        float targetX = CalculateExtrusionOffset(barWidth);
        float targetY = CalculateExtrusionOffset(barSpacing);

        _currentExtrusionOffsetX = Lerp(_currentExtrusionOffsetX, targetX, GLOW_MIN_OFFSET);
        _currentExtrusionOffsetY = Lerp(_currentExtrusionOffsetY, targetY, GLOW_MIN_OFFSET);
    }

    private float CalculateExtrusionOffset(float baseValue)
    {
        float scaleFactor = CurrentQualitySettings!.ExtrusionScaleFactor;
        if (CurrentQualitySettings.UsePerspective)
            scaleFactor *= (1f + PERSPECTIVE_FACTOR);

        return Clamp(
            baseValue * BASE_EXTRUSION_SCALE_FACTOR * scaleFactor,
            -EXTRUSION_CLAMP,
            EXTRUSION_CLAMP);
    }

    private void UpdateTextLayout(SKImageInfo info)
    {
        if (_font == null)
            return;

        ClearLetterData();

        float totalWidth = _font.MeasureText(_currentText);
        float startX = (info.Width - totalWidth) / 2f;
        float baseY = info.Height * TEXT_Y_POSITION_RATIO;

        CreateLetterData(startX, baseY);
        UpdateTextBoundingBox();

        _needsLayout = false;
        _framesSinceLayoutUpdate = 0;
    }

    private void CreateLetterData(float startX, float baseY)
    {
        float x = startX;

        for (int i = 0; i < _currentText.Length; i++)
        {
            char c = _currentText[i];
            string charStr = c.ToString();
            float width = _font!.MeasureText(charStr);

            var path = CreateLetterPath(charStr);
            _letterPaths.Add(new LetterPath(path));

            _staticData.Add(new LetterStaticData(
                Character: c,
                X: x,
                BaseY: baseY,
                Width: width,
                CenterX: x + width / 2f));

            _physicsStates.Add(new LetterPhysicsState(
                CurrentY: baseY,
                VelocityY: 0f,
                SmoothedMagnitude: 0f));

            x += width;
        }
    }

    private SKPath CreateLetterPath(string text)
    {
        using var textPath = _font!.GetTextPath(text, new SKPoint(0, 0));
        var path = new SKPath();
        path.AddPath(textPath);
        return path;
    }

    private void UpdateTextBoundingBox()
    {
        if (_staticData.Count == 0)
        {
            _textBoundingBox = SKRect.Empty;
            return;
        }

        float minX = _staticData[0].X;
        float maxX = _staticData[^1].X + _staticData[^1].Width;
        float minY = float.MaxValue;
        float maxY = float.MinValue;

        for (int i = 0; i < _physicsStates.Count; i++)
        {
            float y = _physicsStates[i].CurrentY;
            minY = MathF.Min(minY, y - _font!.Size);
            maxY = MathF.Max(maxY, y);
        }

        _textBoundingBox = new SKRect(minX, minY, maxX, maxY);
    }

    private void UpdatePhysicsStates(
        float[] spectrum,
        float width,
        int barCount,
        float deltaTime)
    {
        if (_staticData.Count == 0 || barCount == 0 || deltaTime <= 0)
            return;

        int substeps = CurrentQualitySettings!.PhysicsSubsteps;
        float substepDelta = deltaTime / substeps;

        for (int step = 0; step < substeps; step++)
        {
            UpdatePhysicsSubstep(spectrum, width, barCount, substepDelta);
        }

        UpdateTextBoundingBox();
        _framesSinceLayoutUpdate++;
    }

    private void UpdatePhysicsSubstep(
        float[] spectrum,
        float width,
        int barCount,
        float substepDelta)
    {
        for (int i = 0; i < _staticData.Count; i++)
        {
            var staticData = _staticData[i];
            var state = _physicsStates[i];

            int spectrumIndex = MapLetterToSpectrumIndex(staticData.CenterX, width, barCount, spectrum.Length);
            float magnitude = spectrum[spectrumIndex];

            float newSmoothedMagnitude = Lerp(
                state.SmoothedMagnitude,
                magnitude,
                LETTER_MAGNITUDE_SMOOTHING_FACTOR);

            float newVelocityY = UpdateLetterVelocity(
                state,
                staticData,
                newSmoothedMagnitude,
                substepDelta);

            float newY = state.CurrentY + newVelocityY * substepDelta * TARGET_FPS_FOR_PHYSICS_SCALING;
            newY = Clamp(newY, staticData.BaseY - MAX_DISPLACEMENT, staticData.BaseY + MAX_DISPLACEMENT);

            _physicsStates[i] = new LetterPhysicsState(
                CurrentY: newY,
                VelocityY: newVelocityY,
                SmoothedMagnitude: newSmoothedMagnitude);
        }
    }

    private float UpdateLetterVelocity(
        LetterPhysicsState state,
        LetterStaticData staticData,
        float smoothedMagnitude,
        float deltaTime)
    {
        float velocity = state.VelocityY;

        if (smoothedMagnitude > MIN_SPECTRUM_FOR_JUMP)
        {
            float jumpMultiplier = CalculateJumpMultiplier();
            velocity -= smoothedMagnitude * jumpMultiplier * deltaTime;
        }

        float displacement = state.CurrentY - staticData.BaseY;
        float dampingFactor = CalculateDampingFactor();

        velocity -= displacement * CurrentQualitySettings!.ReturnForceFactor * deltaTime * TARGET_FPS_FOR_PHYSICS_SCALING;
        velocity *= MathF.Pow(dampingFactor, deltaTime * TARGET_FPS_FOR_PHYSICS_SCALING);

        return Clamp(velocity, -MAX_VELOCITY, MAX_VELOCITY);
    }

    private static int MapLetterToSpectrumIndex(
        float centerX,
        float width,
        int barCount,
        int dataLength)
    {
        int idx = (int)((centerX / width) * barCount);
        return Clamp(idx, 0, dataLength - 1);
    }

    private float CalculateJumpMultiplier()
    {
        float multiplier = CurrentQualitySettings!.JumpStrengthMultiplier;

        if (IsOverlayActive)
            multiplier *= OVERLAY_JUMP_REDUCTION;

        return multiplier;
    }

    private float CalculateDampingFactor()
    {
        float damping = CurrentQualitySettings!.DampingFactor;

        if (IsOverlayActive)
            damping *= OVERLAY_PHYSICS_DAMPING;

        return damping;
    }

    private void RenderShadowLayer(
        SKCanvas canvas,
        TextRenderData data,
        QualitySettings settings)
    {
        var shadowAlpha = CalculateShadowAlpha(data.AverageIntensity, settings.ShadowIntensity);
        var shadowOffset = new SKPoint(
            data.ExtrusionOffsetX * SHADOW_OFFSET_MULTIPLIER,
            data.ExtrusionOffsetY * SHADOW_OFFSET_MULTIPLIER);

        using var blurFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, SHADOW_BLUR_SIGMA);
        var shadowPaint = CreatePaint(new SKColor(0, 0, 0, shadowAlpha), SKPaintStyle.Fill);
        shadowPaint.MaskFilter = blurFilter;

        try
        {
            if (settings.UseBatchRendering)
                RenderShadowBatched(canvas, shadowOffset, shadowPaint);
            else
                RenderShadowIndividual(canvas, shadowOffset, shadowPaint);
        }
        finally
        {
            ReturnPaint(shadowPaint);
        }
    }

    private void RenderShadowBatched(
        SKCanvas canvas,
        SKPoint offset,
        SKPaint paint)
    {
        RenderPath(canvas, path =>
        {
            for (int i = 0; i < _letterPaths.Count; i++)
            {
                var transform = SKMatrix.CreateTranslation(
                    _staticData[i].X + offset.X,
                    _physicsStates[i].CurrentY + offset.Y);
                path.AddPath(_letterPaths[i].Path, in transform);
            }
        }, paint);
    }

    private void RenderShadowIndividual(
        SKCanvas canvas,
        SKPoint offset,
        SKPaint paint)
    {
        for (int i = 0; i < _letterPaths.Count; i++)
        {
            canvas.Save();
            canvas.Translate(
                _staticData[i].X + offset.X,
                _physicsStates[i].CurrentY + offset.Y);
            canvas.DrawPath(_letterPaths[i].Path, paint);
            canvas.Restore();
        }
    }

    private static byte CalculateShadowAlpha(float intensity, float shadowIntensity)
    {
        float alpha = SHADOW_BASE_ALPHA * shadowIntensity * (1f + intensity * 0.5f);
        return (byte)Clamp(alpha, 0, 255);
    }

    private void RenderExtrusionLayers(
        SKCanvas canvas,
        TextRenderData data,
        SKColor baseColor,
        QualitySettings settings)
    {
        for (int layer = settings.ExtrusionLayers; layer >= 1; layer--)
        {
            RenderSingleExtrusionLayer(canvas, data, baseColor, layer, settings);
        }
    }

    private void RenderSingleExtrusionLayer(
        SKCanvas canvas,
        TextRenderData data,
        SKColor baseColor,
        int layer,
        QualitySettings settings)
    {
        float layerProgress = (float)layer / settings.ExtrusionLayers;
        var layerColor = CalculateExtrusionLayerColor(baseColor, layerProgress);
        var layerOffset = CalculateLayerOffset(data, layer, settings);

        var extrusionPaint = CreatePaint(layerColor, SKPaintStyle.Fill);

        try
        {
            if (settings.UseBatchRendering)
                RenderExtrusionBatched(canvas, layerOffset, extrusionPaint);
            else
                RenderExtrusionIndividual(canvas, layerOffset, extrusionPaint);
        }
        finally
        {
            ReturnPaint(extrusionPaint);
        }
    }

    private void RenderExtrusionBatched(
        SKCanvas canvas,
        SKPoint offset,
        SKPaint paint)
    {
        RenderPath(canvas, path =>
        {
            for (int i = 0; i < _letterPaths.Count; i++)
            {
                var transform = SKMatrix.CreateTranslation(
                    _staticData[i].X + offset.X,
                    _physicsStates[i].CurrentY + offset.Y);
                path.AddPath(_letterPaths[i].Path, in transform);
            }
        }, paint);
    }

    private void RenderExtrusionIndividual(
        SKCanvas canvas,
        SKPoint offset,
        SKPaint paint)
    {
        for (int i = 0; i < _letterPaths.Count; i++)
        {
            canvas.Save();
            canvas.Translate(
                _staticData[i].X + offset.X,
                _physicsStates[i].CurrentY + offset.Y);
            canvas.DrawPath(_letterPaths[i].Path, paint);
            canvas.Restore();
        }
    }

    private static SKColor CalculateExtrusionLayerColor(SKColor baseColor, float layerProgress)
    {
        float darkenFactor = EXTRUSION_COLOR_DARKEN_FACTOR * layerProgress;
        byte alpha = CalculateExtrusionAlpha(baseColor.Alpha, layerProgress);

        return new SKColor(
            (byte)MathF.Max(0, baseColor.Red * darkenFactor),
            (byte)MathF.Max(0, baseColor.Green * darkenFactor),
            (byte)MathF.Max(0, baseColor.Blue * darkenFactor),
            alpha);
    }

    private static byte CalculateExtrusionAlpha(byte baseAlpha, float layerProgress)
    {
        float alpha = baseAlpha * MathF.Pow(EXTRUSION_ALPHA_DECAY, 1f - layerProgress);
        return (byte)MathF.Max(EXTRUSION_MIN_ALPHA, alpha);
    }

    private static SKPoint CalculateLayerOffset(
        TextRenderData data,
        int layer,
        QualitySettings settings)
    {
        float offsetX = layer * data.ExtrusionOffsetX;
        float offsetY = layer * data.ExtrusionOffsetY;

        if (settings.UsePerspective)
        {
            float perspectiveScale = 1f + (layer * PERSPECTIVE_FACTOR / settings.ExtrusionLayers);
            offsetX *= perspectiveScale;
            offsetY *= perspectiveScale;
        }

        return new SKPoint(offsetX, offsetY);
    }

    private void RenderMainTextLayer(
        SKCanvas canvas,
        TextRenderData data,
        SKColor baseColor,
        QualitySettings settings)
    {
        if (settings.UseBatchRendering && settings.UseGradient)
        {
            RenderMainBatchedGradient(canvas, baseColor);
        }
        else if (settings.UseBatchRendering)
        {
            RenderMainBatchedSolid(canvas, baseColor);
        }
        else
        {
            RenderMainIndividual(canvas, baseColor, settings);
        }
    }

    private void RenderMainBatchedGradient(
        SKCanvas canvas,
        SKColor baseColor)
    {
        var gradientColors = GetOrCreateGradientColors(baseColor);

        using var shader = SKShader.CreateLinearGradient(
            new SKPoint(_textBoundingBox.Left, _textBoundingBox.Top),
            new SKPoint(_textBoundingBox.Left, _textBoundingBox.Bottom),
            gradientColors,
            _gradientPositions,
            SKShaderTileMode.Clamp);

        var gradientPaint = CreatePaint(baseColor, SKPaintStyle.Fill, shader);

        try
        {
            RenderPath(canvas, path =>
            {
                for (int i = 0; i < _letterPaths.Count; i++)
                {
                    var transform = SKMatrix.CreateTranslation(
                        _staticData[i].X,
                        _physicsStates[i].CurrentY);
                    path.AddPath(_letterPaths[i].Path, in transform);
                }
            }, gradientPaint);
        }
        finally
        {
            ReturnPaint(gradientPaint);
        }
    }

    private void RenderMainBatchedSolid(
        SKCanvas canvas,
        SKColor baseColor)
    {
        var solidPaint = CreatePaint(baseColor, SKPaintStyle.Fill);

        try
        {
            RenderPath(canvas, path =>
            {
                for (int i = 0; i < _letterPaths.Count; i++)
                {
                    var transform = SKMatrix.CreateTranslation(
                        _staticData[i].X,
                        _physicsStates[i].CurrentY);
                    path.AddPath(_letterPaths[i].Path, in transform);
                }
            }, solidPaint);
        }
        finally
        {
            ReturnPaint(solidPaint);
        }
    }

    private void RenderMainIndividual(
        SKCanvas canvas,
        SKColor baseColor,
        QualitySettings settings)
    {
        for (int i = 0; i < _letterPaths.Count; i++)
        {
            canvas.Save();
            canvas.Translate(_staticData[i].X, _physicsStates[i].CurrentY);

            if (settings.UsePerspective)
                ApplyPerspectiveTransform(canvas, i);

            if (settings.UseGradient)
                RenderLetterWithGradient(canvas, _letterPaths[i].Path, baseColor);
            else
                RenderLetterSolid(canvas, _letterPaths[i].Path, baseColor);

            canvas.Restore();
        }
    }

    private void ApplyPerspectiveTransform(
        SKCanvas canvas,
        int letterIndex)
    {
        float displacement = MathF.Abs(_physicsStates[letterIndex].CurrentY - _staticData[letterIndex].BaseY);
        float scale = 1f + (displacement * PERSPECTIVE_FACTOR / 100f);

        canvas.Scale(scale, scale, _staticData[letterIndex].Width / 2f, 0);
    }

    private void RenderLetterWithGradient(
        SKCanvas canvas,
        SKPath path,
        SKColor baseColor)
    {
        var gradientColors = GetOrCreateGradientColors(baseColor);

        using var shader = SKShader.CreateLinearGradient(
            new SKPoint(path.Bounds.Left, path.Bounds.Top),
            new SKPoint(path.Bounds.Left, path.Bounds.Bottom),
            gradientColors,
            _gradientPositions,
            SKShaderTileMode.Clamp);

        var gradientPaint = CreatePaint(baseColor, SKPaintStyle.Fill, shader);

        try
        {
            canvas.DrawPath(path, gradientPaint);
        }
        finally
        {
            ReturnPaint(gradientPaint);
        }
    }

    private void RenderLetterSolid(
        SKCanvas canvas,
        SKPath path,
        SKColor baseColor)
    {
        var solidPaint = CreatePaint(baseColor, SKPaintStyle.Fill);

        try
        {
            canvas.DrawPath(path, solidPaint);
        }
        finally
        {
            ReturnPaint(solidPaint);
        }
    }

    private void RenderBevelLayer(
        SKCanvas canvas,
        TextRenderData data,
        QualitySettings settings)
    {
        if (settings.UseBatchRendering)
        {
            RenderBevelBatched(canvas, settings.BevelIntensity);
        }
        else
        {
            RenderBevelIndividual(canvas, settings.BevelIntensity);
        }
    }

    private void RenderBevelBatched(
        SKCanvas canvas,
        float intensity)
    {
        RenderBevelHighlightBatched(canvas, intensity);
        RenderBevelShadowBatched(canvas, intensity);
    }

    private void RenderBevelHighlightBatched(
        SKCanvas canvas,
        float intensity)
    {
        var highlightAlpha = (byte)(255 * BEVEL_HIGHLIGHT_ALPHA * intensity);

        using var highlightShader = SKShader.CreateLinearGradient(
            new SKPoint(_textBoundingBox.Left, _textBoundingBox.Top - 1),
            new SKPoint(_textBoundingBox.Left, _textBoundingBox.MidY),
            [new SKColor(255, 255, 255, highlightAlpha), SKColors.Transparent],
            _bevelGradientStops,
            SKShaderTileMode.Clamp);

        var highlightPaint = CreatePaint(SKColors.White, SKPaintStyle.Fill, highlightShader);

        try
        {
            RenderPath(canvas, path =>
            {
                for (int i = 0; i < _letterPaths.Count; i++)
                {
                    var transform = SKMatrix.CreateTranslation(
                        _staticData[i].X,
                        _physicsStates[i].CurrentY);
                    path.AddPath(_letterPaths[i].Path, in transform);
                }
            }, highlightPaint);
        }
        finally
        {
            ReturnPaint(highlightPaint);
        }
    }

    private void RenderBevelShadowBatched(
        SKCanvas canvas,
        float intensity)
    {
        var shadowAlpha = (byte)(255 * BEVEL_SHADOW_ALPHA * intensity);

        using var shadowShader = SKShader.CreateLinearGradient(
            new SKPoint(_textBoundingBox.Left, _textBoundingBox.MidY),
            new SKPoint(_textBoundingBox.Left, _textBoundingBox.Bottom + 1),
            [SKColors.Transparent, new SKColor(0, 0, 0, shadowAlpha)],
            _bevelGradientStops,
            SKShaderTileMode.Clamp);

        var shadowPaint = CreatePaint(SKColors.Black, SKPaintStyle.Fill, shadowShader);

        try
        {
            RenderPath(canvas, path =>
            {
                for (int i = 0; i < _letterPaths.Count; i++)
                {
                    var transform = SKMatrix.CreateTranslation(
                        _staticData[i].X,
                        _physicsStates[i].CurrentY);
                    path.AddPath(_letterPaths[i].Path, in transform);
                }
            }, shadowPaint);
        }
        finally
        {
            ReturnPaint(shadowPaint);
        }
    }

    private void RenderBevelIndividual(
        SKCanvas canvas,
        float intensity)
    {
        for (int i = 0; i < _letterPaths.Count; i++)
        {
            canvas.Save();
            canvas.Translate(_staticData[i].X, _physicsStates[i].CurrentY);

            RenderLetterBevel(canvas, _letterPaths[i].Path, intensity);

            canvas.Restore();
        }
    }

    private void RenderLetterBevel(
        SKCanvas canvas,
        SKPath path,
        float intensity)
    {
        var bounds = path.Bounds;

        RenderLetterBevelHighlight(canvas, path, bounds, intensity);
        RenderLetterBevelShadow(canvas, path, bounds, intensity);
    }

    private void RenderLetterBevelHighlight(
        SKCanvas canvas,
        SKPath path,
        SKRect bounds,
        float intensity)
    {
        var highlightAlpha = (byte)(255 * BEVEL_HIGHLIGHT_ALPHA * intensity);

        using var highlightShader = SKShader.CreateLinearGradient(
            new SKPoint(bounds.Left, bounds.Top - 1),
            new SKPoint(bounds.Left, bounds.MidY),
            [new SKColor(255, 255, 255, highlightAlpha), SKColors.Transparent],
            _bevelGradientStops,
            SKShaderTileMode.Clamp);

        var highlightPaint = CreatePaint(SKColors.White, SKPaintStyle.Fill, highlightShader);

        try
        {
            canvas.DrawPath(path, highlightPaint);
        }
        finally
        {
            ReturnPaint(highlightPaint);
        }
    }

    private void RenderLetterBevelShadow(
        SKCanvas canvas,
        SKPath path,
        SKRect bounds,
        float intensity)
    {
        var shadowAlpha = (byte)(255 * BEVEL_SHADOW_ALPHA * intensity);

        using var shadowShader = SKShader.CreateLinearGradient(
            new SKPoint(bounds.Left, bounds.MidY),
            new SKPoint(bounds.Left, bounds.Bottom + 1),
            [SKColors.Transparent, new SKColor(0, 0, 0, shadowAlpha)],
            _bevelGradientStops,
            SKShaderTileMode.Clamp);

        var shadowPaint = CreatePaint(SKColors.Black, SKPaintStyle.Fill, shadowShader);

        try
        {
            canvas.DrawPath(path, shadowPaint);
        }
        finally
        {
            ReturnPaint(shadowPaint);
        }
    }

    private SKColor[] GetOrCreateGradientColors(SKColor baseColor)
    {
        if (_gradientCache.TryGetValue(baseColor, out var cachedColors))
            return cachedColors;

        var colors = new[]
        {
            new SKColor(
                (byte)MathF.Min(255, baseColor.Red * GRADIENT_HIGHLIGHT_FACTOR),
                (byte)MathF.Min(255, baseColor.Green * GRADIENT_HIGHLIGHT_FACTOR),
                (byte)MathF.Min(255, baseColor.Blue * GRADIENT_HIGHLIGHT_FACTOR),
                baseColor.Alpha),
            new SKColor(
                (byte)(baseColor.Red * GRADIENT_SHADOW_FACTOR),
                (byte)(baseColor.Green * GRADIENT_SHADOW_FACTOR),
                (byte)(baseColor.Blue * GRADIENT_SHADOW_FACTOR),
                baseColor.Alpha)
        };

        if (_gradientCache.Count > 10)
            _gradientCache.Clear();

        _gradientCache[baseColor] = colors;
        return colors;
    }

    private float CalculateDeltaTime()
    {
        float currentTime = Environment.TickCount / 1000f;
        float deltaTime = _lastUpdateTime > 0
            ? currentTime - _lastUpdateTime
            : PHYSICS_TIME_SCALE;

        _lastUpdateTime = currentTime;
        return MathF.Min(deltaTime, 0.1f);
    }

    private static float CalculateAverageIntensity(float[] spectrum)
    {
        if (spectrum.Length == 0)
            return 0f;

        float sum = 0f;
        for (int i = 0; i < spectrum.Length; i++)
            sum += spectrum[i];

        return sum / spectrum.Length;
    }

    private void ClearLetterData()
    {
        foreach (var letterPath in _letterPaths)
            letterPath.Path.Dispose();

        _letterPaths.Clear();
        _physicsStates.Clear();
        _staticData.Clear();
    }

    protected override int GetMaxBarsForQuality() => Quality switch
    {
        RenderQuality.Low => 36,
        RenderQuality.Medium => 72,
        RenderQuality.High => 144,
        _ => 72
    };

    protected override void OnQualitySettingsApplied()
    {
        base.OnQualitySettingsApplied();

        float smoothingFactor = Quality switch
        {
            RenderQuality.Low => 0.5f,
            RenderQuality.Medium => 0.4f,
            RenderQuality.High => 0.35f,
            _ => 0.4f
        };

        if (IsOverlayActive)
            smoothingFactor *= 1.1f;

        SetProcessingSmoothingFactor(smoothingFactor);

        _needsLayout = true;
        _gradientCache.Clear();

        RequestRedraw();
    }

    protected override void CleanupUnusedResources()
    {
        base.CleanupUnusedResources();

        if (_gradientCache.Count > 20)
            _gradientCache.Clear();

        if (_framesSinceLayoutUpdate > 300 && _needsLayout)
        {
            _needsLayout = false;
            _framesSinceLayoutUpdate = 0;
        }
    }

    protected override void OnDispose()
    {
        ClearLetterData();
        _gradientCache.Clear();
        _font?.Dispose();
        _font = null;
        _currentText = DEFAULT_TEXT;
        _needsLayout = false;
        _smoothedTextSize = BASE_TEXT_SIZE;
        _lastTextSize = BASE_TEXT_SIZE;
        _lastBarCount = 0;
        _barCountFactor = 0;
        _currentExtrusionOffsetX = 0;
        _currentExtrusionOffsetY = 0;
        _lastUpdateTime = 0;
        _textBoundingBox = SKRect.Empty;
        _framesSinceLayoutUpdate = 0;

        base.OnDispose();
    }

    private record TextRenderData(
        SKRect BoundingBox,
        float ExtrusionOffsetX,
        float ExtrusionOffsetY,
        float TextSize,
        float AverageIntensity,
        int LetterCount);

    private record LetterStaticData(
        char Character,
        float X,
        float BaseY,
        float Width,
        float CenterX);

    private record LetterPhysicsState(
        float CurrentY,
        float VelocityY,
        float SmoothedMagnitude);

    private sealed class LetterPath(SKPath path)
    {
        public SKPath Path { get; } = path;
    }
}