#nullable enable

using static SpectrumNet.Views.Renderers.HackerTextRenderer.Constants;
using static SpectrumNet.Views.Renderers.HackerTextRenderer.Constants.Physics;
using static SpectrumNet.Views.Renderers.HackerTextRenderer.Constants.QualitySettings;
using static SpectrumNet.Views.Renderers.HackerTextRenderer.Constants.Style;

namespace SpectrumNet.Views.Renderers;

public sealed class HackerTextRenderer : EffectSpectrumRenderer
{
    private static readonly Lazy<HackerTextRenderer> _instance =
        new(() => new HackerTextRenderer());

    private HackerTextRenderer() { }

    public static HackerTextRenderer GetInstance() => _instance.Value;

    public float SensitivityScale { get; set; } = 1.0f;

    public record Constants
    {
        public const string
            LOG_PREFIX = "HackerTextRenderer",
            STATIC_TEXT = "Haker2550";

        public const float
            BASE_TEXT_SIZE = 64f,
            TEXT_Y_POSITION_RATIO = 0.5f,

            DEFAULT_BAR_COUNT = 100f,
            TEXT_SIZE_BAR_COUNT_SCALE = 0.2f,
            MIN_TEXT_SIZE_RATIO = 0.5f,
            MAX_TEXT_SIZE_RATIO = 3.0f, 
            BASE_EXTRUSION_SCALE_FACTOR = 0.5f;

        public static class Physics
        {
            public const float
                JUMP_STRENGTH_MULTIPLIER = 80f,
                RETURN_FORCE_FACTOR = 0.1f,
                DAMPING_FACTOR = 0.85f,
                MIN_SPECTRUM_FOR_JUMP = 0.02f,
                LETTER_MAGNITUDE_SMOOTHING_FACTOR = 0.6f,
                TARGET_FPS_FOR_PHYSICS_SCALING = 60f,
                MAX_DELTA_TIME = 0.1f,
                MIN_DELTA_TIME = 0f;
        }

        public static class Style
        {
            public const int EXTRUSION_LAYERS = 5;

            public const float
                BASE_EXTRUSION_OFFSET_X = 1.0f,
                BASE_EXTRUSION_OFFSET_Y = 1.0f;

            public static readonly SKColor
                EXTRUSION_COLOR_DARKEN_FACTOR = new(0, 0, 0, 100),
                GRADIENT_START_COLOR_OFFSET = new(25, 25, 25, 0),
                GRADIENT_END_COLOR_OFFSET = new(25, 25, 25, 0);
        }

        public static class QualitySettings
        {
            public const float
                LOW_JUMP_STRENGTH_MULTIPLIER = JUMP_STRENGTH_MULTIPLIER * 0.7f,
                MEDIUM_JUMP_STRENGTH_MULTIPLIER = JUMP_STRENGTH_MULTIPLIER,
                HIGH_JUMP_STRENGTH_MULTIPLIER = JUMP_STRENGTH_MULTIPLIER * 1.3f,
                LOW_DAMPING_FACTOR = DAMPING_FACTOR * 1.05f,
                MEDIUM_DAMPING_FACTOR = DAMPING_FACTOR,
                HIGH_DAMPING_FACTOR = DAMPING_FACTOR * 0.95f,
                LOW_RETURN_FORCE_FACTOR_SCALE = 0.8f,
                HIGH_RETURN_FORCE_FACTOR_SCALE = 1.2f,

                LOW_TEXT_SCALE_FACTOR = 0.5f,
                MEDIUM_TEXT_SCALE_FACTOR = 1.0f,
                HIGH_TEXT_SCALE_FACTOR = 2.0f,

                LOW_EXTRUSION_SCALE_FACTOR = 0.7f,
                MEDIUM_EXTRUSION_SCALE_FACTOR = 1.0f,
                HIGH_EXTRUSION_SCALE_FACTOR = 1.3f;


            public const int
                LOW_EXTRUSION_LAYERS = 2,
                MEDIUM_EXTRUSION_LAYERS = EXTRUSION_LAYERS,
                HIGH_EXTRUSION_LAYERS = EXTRUSION_LAYERS + 2;
        }
    }

    private static readonly float[] GradientColorPositions = [0f, 1f];

    private DateTime _lastFrameTimeForEffect = DateTime.UtcNow;

    private SKFont? _font;
    private readonly List<HackerLetter> _letters = [];
    private bool _staticTextInitialized;

    private float
        _currentJumpStrengthMultiplier,
        _currentDampingFactor,
        _currentReturnForceFactor,
        _currentTextScaleFactor,
        _currentExtrusionScaleFactor,
        _currentExtrusionOffsetX,
        _currentExtrusionOffsetY;

    private int _currentExtrusionLayers;

    private struct HackerLetter
    {
        public float
            X, BaseY, CurrentY,
            VelocityY,
            SmoothedMagnitude,
            CharWidth;
        public char Character;
        public SKPath? Path;
    }

    protected override void OnInitialize()
    {
        base.OnInitialize();
        InitializeFont();
        _lastFrameTimeForEffect = UtcNow;
    }

    private void InitializeFont()
    {
        _font = new()
        {
            Size = BASE_TEXT_SIZE,
            Edging = UseAntiAlias ? SKFontEdging.SubpixelAntialias : SKFontEdging.Alias
        };
    }

    protected override void OnQualitySettingsApplied()
    {
        base.OnQualitySettingsApplied();
        SetCurrentQualityParameters(Quality);
        InvalidateLetterPaths();
        _staticTextInitialized = false;
    }

    private void SetCurrentQualityParameters(RenderQuality quality)
    {
        switch (quality)
        {
            case RenderQuality.Low: LowSettings(); break;
            case RenderQuality.Medium: MediumSettings(); break;
            case RenderQuality.High: HighSettings(); break;
        }
    }

    private void HighSettings()
    {
        _currentJumpStrengthMultiplier = HIGH_JUMP_STRENGTH_MULTIPLIER;
        _currentDampingFactor = HIGH_DAMPING_FACTOR;
        _currentReturnForceFactor = RETURN_FORCE_FACTOR * HIGH_RETURN_FORCE_FACTOR_SCALE;
        _currentExtrusionLayers = HIGH_EXTRUSION_LAYERS;
        _currentTextScaleFactor = HIGH_TEXT_SCALE_FACTOR;
        _currentExtrusionScaleFactor = HIGH_EXTRUSION_SCALE_FACTOR;
    }

    private void MediumSettings()
    {
        _currentJumpStrengthMultiplier = MEDIUM_JUMP_STRENGTH_MULTIPLIER;
        _currentDampingFactor = MEDIUM_DAMPING_FACTOR;
        _currentReturnForceFactor = RETURN_FORCE_FACTOR;
        _currentExtrusionLayers = MEDIUM_EXTRUSION_LAYERS;
        _currentTextScaleFactor = MEDIUM_TEXT_SCALE_FACTOR;
        _currentExtrusionScaleFactor = MEDIUM_EXTRUSION_SCALE_FACTOR;
    }

    private void LowSettings()
    {
        _currentJumpStrengthMultiplier = LOW_JUMP_STRENGTH_MULTIPLIER;
        _currentDampingFactor = LOW_DAMPING_FACTOR;
        _currentReturnForceFactor = RETURN_FORCE_FACTOR * LOW_RETURN_FORCE_FACTOR_SCALE;
        _currentExtrusionLayers = LOW_EXTRUSION_LAYERS;
        _currentTextScaleFactor = LOW_TEXT_SCALE_FACTOR;
        _currentExtrusionScaleFactor = LOW_EXTRUSION_SCALE_FACTOR;
    }

    private void InvalidateLetterPaths()
    {
        for (int i = 0; i < _letters.Count; i++)
        {
            var letter = _letters[i];
            letter.Path?.Dispose();
            letter.Path = null;
            _letters[i] = letter;
        }
    }


    private static float CalculateTotalTextWidth(SKFont font, string text)
        => string.IsNullOrEmpty(text) ? 0f : font.MeasureText(text);

    private void PopulateHackerLetters(
        SKFont font,
        float startX,
        float baseY)
    {
        float currentX = startX;
        for (int i = 0; i < STATIC_TEXT.Length; i++)
        {
            char character = STATIC_TEXT[i];
            string charStr = character.ToString();
            float charWidth = font.MeasureText(charStr);

            SKPath? path = font.GetTextPath(charStr, new SKPoint(0f, 0f));

            _letters.Add(new HackerLetter
            {
                Character = character,
                X = currentX,
                BaseY = baseY,
                CurrentY = baseY,
                VelocityY = 0f,
                SmoothedMagnitude = 0f,
                CharWidth = charWidth,
                Path = path
            });

            currentX += charWidth;
        }
    }

    private void InitializeStaticTextLayout(SKImageInfo info)
    {
        if (_font == null) InitializeFont();
        SKFont currentFont = _font!;

        InvalidateLetterPaths();
        _letters.Clear();

        float totalTextWidth = CalculateTotalTextWidth(currentFont, STATIC_TEXT);
        float currentX = (info.Width - totalTextWidth) / 2f;
        float baseY = info.Height * TEXT_Y_POSITION_RATIO;

        PopulateHackerLetters(currentFont, currentX, baseY);
        _staticTextInitialized = true;
    }

    private void ApplyBarParameters(
            int barCount,
            float barWidth,
            float barSpacing,
            SKImageInfo __,
            SKPaint _,
            float[] spectrum)
    {
        if (_font == null) InitializeFont();

        float averageMagnitude = CalculateAverageMagnitude(spectrum) * SensitivityScale;
        float normalizedBarFactor = barCount / DEFAULT_BAR_COUNT - 1f;

        float targetSizeFromBars = BASE_TEXT_SIZE * (1f + normalizedBarFactor * _currentTextScaleFactor);
        float targetSizeFromSpectrum = BASE_TEXT_SIZE * (1f + averageMagnitude * TEXT_SIZE_BAR_COUNT_SCALE);
        float targetTextSize = MathF.Max(targetSizeFromBars, targetSizeFromSpectrum);

        float minAllowed = BASE_TEXT_SIZE * MIN_TEXT_SIZE_RATIO;
        float maxAllowed = BASE_TEXT_SIZE * MAX_TEXT_SIZE_RATIO;
        float newSize = Clamp(targetTextSize, minAllowed, maxAllowed);

        if (MathF.Abs(newSize - _font!.Size) > 0.1f)
        {
            _font.Size = newSize;
            _staticTextInitialized = false;
        }

        _currentExtrusionOffsetX = Clamp(
            barWidth * BASE_EXTRUSION_SCALE_FACTOR * _currentExtrusionScaleFactor,
            -2f, 2f);
        _currentExtrusionOffsetY = Clamp(
            barSpacing * BASE_EXTRUSION_SCALE_FACTOR * _currentExtrusionScaleFactor,
            -2f, 2f);
    }

    private static float CalculateAverageMagnitude(float[] spectrum)
    {
        if (spectrum == null || spectrum.Length == 0) return 0f;
        float sum = 0f;
        foreach (float v in spectrum) sum += v;
        return sum / spectrum.Length;
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
        if (_currentJumpStrengthMultiplier == 0f
            && _currentDampingFactor == 0f
            && _currentReturnForceFactor == 0f)
        {
            OnQualitySettingsApplied();
        }

        ApplyBarParameters(barCount, barWidth, barSpacing, info, paint, spectrum);

        if (!_staticTextInitialized)
        {
            InitializeStaticTextLayout(info);
            _lastFrameTimeForEffect = DateTime.UtcNow;
        }

        if (_staticTextInitialized)
        {
            DateTime now = DateTime.UtcNow;
            float deltaTime = (float)(now - _lastFrameTimeForEffect).TotalSeconds;
            _lastFrameTimeForEffect = now;
            deltaTime = Clamp(deltaTime, MIN_DELTA_TIME, MAX_DELTA_TIME);

            UpdateLetterPhysics(spectrum, info, barCount, deltaTime);
            DrawLetters3D(canvas, paint.Color);
        }
    }
    private void UpdateSingleLetterPhysics(
        ref HackerLetter letter,
        float[] spectrumData,
        float canvasWidth,
        int spectrumBarCount,
        float deltaTime)
    {
        if (spectrumBarCount == 0 || spectrumData.Length == 0) return;

        float letterCenterX = letter.X + letter.CharWidth / 2f;
        int spectrumIndex = MapLetterXToSpectrumIndex(
            letterCenterX,
            canvasWidth,
            spectrumBarCount);

        spectrumIndex = Clamp(spectrumIndex, 0, spectrumData.Length - 1);

        float magnitude = spectrumData[spectrumIndex];

        letter.SmoothedMagnitude = ApplySmoothing(
            letter.SmoothedMagnitude,
            magnitude,
            LETTER_MAGNITUDE_SMOOTHING_FACTOR);

        if (letter.SmoothedMagnitude > MIN_SPECTRUM_FOR_JUMP)
        {
            letter.VelocityY -= letter.SmoothedMagnitude
                * _currentJumpStrengthMultiplier
                * deltaTime
                * SensitivityScale;
        }

        float displacement = letter.CurrentY - letter.BaseY;
        float physicsScale = deltaTime * TARGET_FPS_FOR_PHYSICS_SCALING;

        letter.VelocityY -= displacement * _currentReturnForceFactor * physicsScale;
        letter.VelocityY *= MathF.Pow(_currentDampingFactor, physicsScale);
        letter.CurrentY += letter.VelocityY * physicsScale;
    }

    private void UpdateLetterPhysics(
        float[] spectrumData,
        SKImageInfo info,
        int spectrumBarCount,
        float deltaTime)
    {
        if (_letters.Count == 0
            || spectrumBarCount == 0
            || deltaTime <= MIN_DELTA_TIME)
            return;

        for (int i = 0; i < _letters.Count; i++)
        {
            HackerLetter letter = _letters[i];
            UpdateSingleLetterPhysics(
                ref letter,
                spectrumData,
                info.Width,
                spectrumBarCount,
                deltaTime);
            _letters[i] = letter;
        }
    }

    [MethodImpl(AggressiveInlining)]
    private static int MapLetterXToSpectrumIndex(
        float letterX,
        float canvasWidth,
        int spectrumBarCount)
    {
        if (canvasWidth <= 0 || spectrumBarCount <= 0) return 0;

        float normalizedX = letterX / canvasWidth;
        int index = (int)(normalizedX * spectrumBarCount);
        return Clamp(index, 0, spectrumBarCount - 1);
    }

    [MethodImpl(AggressiveInlining)]
    private static float ApplySmoothing(
        float previousValue,
        float currentValue,
        float smoothingFactor) =>
        previousValue * (1f - smoothingFactor) + currentValue * smoothingFactor;

    private static SKColor CalculateExtrusionRenderColor(SKColor baseColor)
    {
        return new SKColor(
            (byte)Max(0, baseColor.Red - EXTRUSION_COLOR_DARKEN_FACTOR.Red),
            (byte)Max(0, baseColor.Green - EXTRUSION_COLOR_DARKEN_FACTOR.Green),
            (byte)Max(0, baseColor.Blue - EXTRUSION_COLOR_DARKEN_FACTOR.Blue),
            baseColor.Alpha
        );
    }

    private static void CalculateGradientColors(
        SKColor baseColor,
        out SKColor startColor,
        out SKColor endColor)
    {
        startColor = new SKColor(
            (byte)Min(255, baseColor.Red + GRADIENT_START_COLOR_OFFSET.Red),
            (byte)Min(255, baseColor.Green + GRADIENT_START_COLOR_OFFSET.Green),
            (byte)Min(255, baseColor.Blue + GRADIENT_START_COLOR_OFFSET.Blue),
            baseColor.Alpha);

        endColor = new SKColor(
            (byte)Max(0, baseColor.Red - GRADIENT_END_COLOR_OFFSET.Red),
            (byte)Max(0, baseColor.Green - GRADIENT_END_COLOR_OFFSET.Green),
            (byte)Max(0, baseColor.Blue - GRADIENT_END_COLOR_OFFSET.Blue),
            baseColor.Alpha);
    }

    private void DrawLetterExtrusion(
        SKCanvas canvas,
        SKPath letterPath,
        SKPaint extrusionPaint)
    {
        if (canvas == null
            || letterPath == null
            || extrusionPaint == null)
            return;

        int layers = Math.Max(0, _currentExtrusionLayers);
        if (layers == 0)
            return;

        for (int layer = 1; layer <= layers; layer++)
        {
            canvas.Save();
            canvas.Translate(
                layer * _currentExtrusionOffsetX,
                layer * _currentExtrusionOffsetY
            );
            canvas.DrawPath(letterPath, extrusionPaint);
            canvas.Restore();
        }
    }

    private static void DrawLetterFace(
        SKCanvas canvas,
        SKPath letterPath,
        SKPaint facePaint,
        SKColor gradStartColor,
        SKColor gradEndColor)
    {
        SKRect pathBounds = letterPath.Bounds;
        using var shader = SKShader.CreateLinearGradient(
            new SKPoint(pathBounds.Left, pathBounds.Top),
            new SKPoint(pathBounds.Left, pathBounds.Bottom),
            [gradStartColor, gradEndColor],
            GradientColorPositions,
            SKShaderTileMode.Clamp);

        facePaint.Shader = shader;
        canvas.DrawPath(letterPath, facePaint);
        facePaint.Shader = null;
    }

    private void DrawLetters3D(SKCanvas canvas, SKColor baseColor)
    {
        if (_font == null || _letters.Count == 0) return;

        SKPaint? extrusionPaint = null;
        SKPaint? facePaint = null;

        try
        {
            extrusionPaint = _paintPool.Get();
            extrusionPaint.IsAntialias = UseAntiAlias;
            extrusionPaint.Style = SKPaintStyle.Fill;
            extrusionPaint.Color = CalculateExtrusionRenderColor(baseColor);


            facePaint = _paintPool.Get();
            facePaint.IsAntialias = UseAntiAlias;
            facePaint.Style = SKPaintStyle.Fill;
            CalculateGradientColors(baseColor, out SKColor gradStart, out SKColor gradEnd);

            foreach (var letter in _letters)
            {
                if (letter.Path == null) continue;

                canvas.Save();
                canvas.Translate(letter.X, letter.CurrentY);

                DrawLetterExtrusion(canvas, letter.Path, extrusionPaint);

                DrawLetterFace(canvas, letter.Path, facePaint, gradStart, gradEnd);

                canvas.Restore();
            }
        }
        finally
        {
            if (extrusionPaint != null) _paintPool.Return(extrusionPaint);
            if (facePaint != null) _paintPool.Return(facePaint);
        }
    }

    protected override void OnDispose()
    {
        base.OnDispose();
        InvalidateLetterPaths();
        _font?.Dispose();
        _font = null;
        _letters.Clear();
        _staticTextInitialized = false;
    }
}