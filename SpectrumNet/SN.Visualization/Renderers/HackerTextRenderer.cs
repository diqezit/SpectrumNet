#nullable enable

using static System.MathF;
using static SpectrumNet.SN.Visualization.Renderers.HackerTextRenderer.Constants;

namespace SpectrumNet.SN.Visualization.Renderers;

public sealed class HackerTextRenderer : EffectSpectrumRenderer
{
    private const string LogPrefix = nameof(HackerTextRenderer);

    private static readonly Lazy<HackerTextRenderer> _instance =
        new(() => new HackerTextRenderer());

    private HackerTextRenderer() { }

    public static HackerTextRenderer GetInstance() => _instance.Value;

    public float SensitivityScale { get; set; } = 1.0f;

    public static class Constants
    {
        public const string STATIC_TEXT = "Haker2550";

        public const float
            BASE_TEXT_SIZE = 64f,
            TEXT_Y_POSITION_RATIO = 0.5f,
            DEFAULT_BAR_COUNT = 100f,
            TEXT_SIZE_BAR_COUNT_SCALE = 0.2f,
            MIN_TEXT_SIZE_RATIO = 0.5f,
            MAX_TEXT_SIZE_RATIO = 3.0f,
            BASE_EXTRUSION_SCALE_FACTOR = 0.5f,
            JUMP_STRENGTH_MULTIPLIER = 80f,
            RETURN_FORCE_FACTOR = 0.1f,
            DAMPING_FACTOR = 0.85f,
            MIN_SPECTRUM_FOR_JUMP = 0.02f,
            LETTER_MAGNITUDE_SMOOTHING_FACTOR = 0.6f,
            TARGET_FPS_FOR_PHYSICS_SCALING = 60f,
            MAX_DELTA_TIME = 0.1f,
            MIN_DELTA_TIME = 0f,
            BASE_EXTRUSION_OFFSET_X = 1.0f,
            BASE_EXTRUSION_OFFSET_Y = 1.0f;

        public const int EXTRUSION_LAYERS = 5;

        public static readonly SKColor
            EXTRUSION_COLOR_DARKEN_FACTOR = new(0, 0, 0, 100),
            GRADIENT_START_COLOR_OFFSET = new(25, 25, 25, 0),
            GRADIENT_END_COLOR_OFFSET = new(25, 25, 25, 0);

        public static readonly float[] GradientColorPositions = [0f, 1f];

        public static readonly Dictionary<RenderQuality, QualitySettings> QualityPresets = new()
        {
            [RenderQuality.Low] = new(
                JumpStrengthMultiplier: JUMP_STRENGTH_MULTIPLIER * 0.7f,
                DampingFactor: DAMPING_FACTOR * 1.05f,
                ReturnForceFactor: RETURN_FORCE_FACTOR * 0.8f,
                ExtrusionLayers: 2,
                TextScaleFactor: 0.5f,
                ExtrusionScaleFactor: 0.7f
            ),
            [RenderQuality.Medium] = new(
                JumpStrengthMultiplier: JUMP_STRENGTH_MULTIPLIER,
                DampingFactor: DAMPING_FACTOR,
                ReturnForceFactor: RETURN_FORCE_FACTOR,
                ExtrusionLayers: EXTRUSION_LAYERS,
                TextScaleFactor: 1.0f,
                ExtrusionScaleFactor: 1.0f
            ),
            [RenderQuality.High] = new(
                JumpStrengthMultiplier: JUMP_STRENGTH_MULTIPLIER * 1.3f,
                DampingFactor: DAMPING_FACTOR * 0.95f,
                ReturnForceFactor: RETURN_FORCE_FACTOR * 1.2f,
                ExtrusionLayers: EXTRUSION_LAYERS + 2,
                TextScaleFactor: 2.0f,
                ExtrusionScaleFactor: 1.3f
            )
        };

        public record QualitySettings(
            float JumpStrengthMultiplier,
            float DampingFactor,
            float ReturnForceFactor,
            int ExtrusionLayers,
            float TextScaleFactor,
            float ExtrusionScaleFactor
        );
    }

    private DateTime _lastFrameTimeForEffect = DateTime.UtcNow;
    private SKFont? _font;
    private readonly List<HackerLetter> _letters = [];
    private bool _staticTextInitialized;
    private QualitySettings _currentSettings = QualityPresets[RenderQuality.Medium];
    private float _currentExtrusionOffsetX;
    private float _currentExtrusionOffsetY;

    private struct HackerLetter
    {
        public float X, BaseY, CurrentY, VelocityY, SmoothedMagnitude, CharWidth;
        public char Character;
        public SKPath? Path;
    }

    protected override void OnInitialize()
    {
        base.OnInitialize();
        InitializeFont();
        _lastFrameTimeForEffect = DateTime.UtcNow;
        _logger.Log(LogLevel.Debug, LogPrefix, "Initialized");
    }

    private void InitializeFont()
    {
        _font = new()
        {
            Size = BASE_TEXT_SIZE,
            Edging = _useAntiAlias ? SKFontEdging.SubpixelAntialias : SKFontEdging.Alias
        };
    }

    protected override void OnQualitySettingsApplied()
    {
        _currentSettings = QualityPresets[Quality];
        InvalidateLetterPaths();
        _staticTextInitialized = false;
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

    protected override void RenderEffect(
        SKCanvas canvas,
        float[] spectrum,
        SKImageInfo info,
        float barWidth,
        float barSpacing,
        int barCount,
        SKPaint paint)
    {
        ApplyBarParameters(barCount, barWidth, barSpacing, info, spectrum);

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

    private void ApplyBarParameters(
        int barCount,
        float barWidth,
        float barSpacing,
        SKImageInfo info,
        float[] spectrum)
    {
        if (_font == null) InitializeFont();

        float averageMagnitude = CalculateAverageMagnitude(spectrum) * SensitivityScale;
        float normalizedBarFactor = barCount / DEFAULT_BAR_COUNT - 1f;

        float targetSizeFromBars = BASE_TEXT_SIZE *
            (1f + normalizedBarFactor * _currentSettings.TextScaleFactor);
        float targetSizeFromSpectrum = BASE_TEXT_SIZE *
            (1f + averageMagnitude * TEXT_SIZE_BAR_COUNT_SCALE);
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
            barWidth * BASE_EXTRUSION_SCALE_FACTOR * _currentSettings.ExtrusionScaleFactor,
            -2f, 2f);
        _currentExtrusionOffsetY = Clamp(
            barSpacing * BASE_EXTRUSION_SCALE_FACTOR * _currentSettings.ExtrusionScaleFactor,
            -2f, 2f);
    }

    private static float CalculateAverageMagnitude(float[] spectrum)
    {
        if (spectrum == null || spectrum.Length == 0) return 0f;
        float sum = 0f;
        foreach (float v in spectrum) sum += v;
        return sum / spectrum.Length;
    }

    private void InitializeStaticTextLayout(SKImageInfo info)
    {
        if (_font == null) InitializeFont();

        InvalidateLetterPaths();
        _letters.Clear();

        float totalTextWidth = _font!.MeasureText(STATIC_TEXT);
        float currentX = (info.Width - totalTextWidth) / 2f;
        float baseY = info.Height * TEXT_Y_POSITION_RATIO;

        for (int i = 0; i < STATIC_TEXT.Length; i++)
        {
            char character = STATIC_TEXT[i];
            string charStr = character.ToString();
            float charWidth = _font.MeasureText(charStr);
            SKPath? path = _font.GetTextPath(charStr, new SKPoint(0f, 0f));

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

        _staticTextInitialized = true;
    }

    private void UpdateLetterPhysics(
        float[] spectrumData,
        SKImageInfo info,
        int spectrumBarCount,
        float deltaTime)
    {
        if (_letters.Count == 0 || spectrumBarCount == 0 || deltaTime <= MIN_DELTA_TIME)
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

    private void UpdateSingleLetterPhysics(
        ref HackerLetter letter,
        float[] spectrumData,
        float canvasWidth,
        int spectrumBarCount,
        float deltaTime)
    {
        if (spectrumBarCount == 0 || spectrumData.Length == 0) return;

        float letterCenterX = letter.X + letter.CharWidth / 2f;
        int spectrumIndex = (int)((letterCenterX / canvasWidth) * spectrumBarCount);
        spectrumIndex = Clamp(spectrumIndex, 0, spectrumData.Length - 1);

        float magnitude = spectrumData[spectrumIndex];
        letter.SmoothedMagnitude = letter.SmoothedMagnitude * (1f - LETTER_MAGNITUDE_SMOOTHING_FACTOR) +
            magnitude * LETTER_MAGNITUDE_SMOOTHING_FACTOR;

        if (letter.SmoothedMagnitude > MIN_SPECTRUM_FOR_JUMP)
        {
            letter.VelocityY -= letter.SmoothedMagnitude *
                _currentSettings.JumpStrengthMultiplier *
                deltaTime *
                SensitivityScale;
        }

        float displacement = letter.CurrentY - letter.BaseY;
        float physicsScale = deltaTime * TARGET_FPS_FOR_PHYSICS_SCALING;

        letter.VelocityY -= displacement * _currentSettings.ReturnForceFactor * physicsScale;
        letter.VelocityY *= Pow(_currentSettings.DampingFactor, physicsScale);
        letter.CurrentY += letter.VelocityY * physicsScale;
    }

    private void DrawLetters3D(SKCanvas canvas, SKColor baseColor)
    {
        if (_font == null || _letters.Count == 0) return;

        using var extrusionPaint = CreatePaint(
            CalculateExtrusionColor(baseColor),
            SKPaintStyle.Fill,
            0);

        using var facePaint = _paintPool.Get();
        facePaint.IsAntialias = _useAntiAlias;
        facePaint.Style = SKPaintStyle.Fill;

        var (gradStart, gradEnd) = CalculateGradientColors(baseColor);

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

    private void DrawLetterExtrusion(
        SKCanvas canvas,
        SKPath letterPath,
        SKPaint extrusionPaint)
    {
        int layers = Max(0, _currentSettings.ExtrusionLayers);
        if (layers == 0) return;

        for (int layer = 1; layer <= layers; layer++)
        {
            canvas.Save();
            canvas.Translate(
                layer * _currentExtrusionOffsetX,
                layer * _currentExtrusionOffsetY);
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

    private static SKColor CalculateExtrusionColor(SKColor baseColor)
    {
        return new SKColor(
            (byte)Max(0, baseColor.Red - EXTRUSION_COLOR_DARKEN_FACTOR.Red),
            (byte)Max(0, baseColor.Green - EXTRUSION_COLOR_DARKEN_FACTOR.Green),
            (byte)Max(0, baseColor.Blue - EXTRUSION_COLOR_DARKEN_FACTOR.Blue),
            baseColor.Alpha);
    }

    private static (SKColor start, SKColor end) CalculateGradientColors(SKColor baseColor)
    {
        var start = new SKColor(
            (byte)Min(255, baseColor.Red + GRADIENT_START_COLOR_OFFSET.Red),
            (byte)Min(255, baseColor.Green + GRADIENT_START_COLOR_OFFSET.Green),
            (byte)Min(255, baseColor.Blue + GRADIENT_START_COLOR_OFFSET.Blue),
            baseColor.Alpha);

        var end = new SKColor(
            (byte)Max(0, baseColor.Red - GRADIENT_END_COLOR_OFFSET.Red),
            (byte)Max(0, baseColor.Green - GRADIENT_END_COLOR_OFFSET.Green),
            (byte)Max(0, baseColor.Blue - GRADIENT_END_COLOR_OFFSET.Blue),
            baseColor.Alpha);

        return (start, end);
    }

    private SKPaint CreatePaint(
        SKColor color,
        SKPaintStyle style,
        float strokeWidth)
    {
        var paint = _paintPool.Get();
        paint.Color = color;
        paint.Style = style;
        paint.IsAntialias = _useAntiAlias;
        paint.StrokeWidth = strokeWidth;
        return paint;
    }

    protected override void OnDispose()
    {
        InvalidateLetterPaths();
        _font?.Dispose();
        _font = null;
        _letters.Clear();
        _staticTextInitialized = false;
        base.OnDispose();
        _logger.Log(LogLevel.Debug, LogPrefix, "Disposed");
    }
}