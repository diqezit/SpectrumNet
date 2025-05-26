#nullable enable

using static SpectrumNet.SN.Visualization.Renderers.HackerTextRenderer.Constants;
using static System.MathF;

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
        public const string STATIC_TEXT = "HAKER2550";
        public const float
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
            BASE_EXTRUSION_OFFSET_X = 1.0f,
            BASE_EXTRUSION_OFFSET_Y = 1.0f,
            MIN_DELTA_TIME = 0f,
            TARGET_FPS_FOR_PHYSICS_SCALING = 60f;

        public const int EXTRUSION_LAYERS = 5;

        public static readonly SKColor
            EXTRUSION_COLOR_DARKEN_FACTOR = new(0, 0, 0, 100),
            GRADIENT_START_COLOR_OFFSET = new(25, 25, 25, 0),
            GRADIENT_END_COLOR_OFFSET = new(25, 25, 25, 0);

        public static readonly float[] GradientColorPositions = [0f, 1f];

        public static readonly Dictionary<RenderQuality, QualitySettings> QualityPresets = new()
        {
            [RenderQuality.Low] = new(
                JUMP_STRENGTH_MULTIPLIER * 0.7f,
                DAMPING_FACTOR * 1.05f,
                RETURN_FORCE_FACTOR * 0.8f,
                2,
                0.5f,
                0.7f
            ),
            [RenderQuality.Medium] = new(
                JUMP_STRENGTH_MULTIPLIER,
                DAMPING_FACTOR,
                RETURN_FORCE_FACTOR,
                EXTRUSION_LAYERS,
                1.0f,
                1.0f
            ),
            [RenderQuality.High] = new(
                JUMP_STRENGTH_MULTIPLIER * 1.3f,
                DAMPING_FACTOR * 0.95f,
                RETURN_FORCE_FACTOR * 1.2f,
                EXTRUSION_LAYERS + 2,
                2.0f,
                1.3f
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

    private SKFont? _font;
    private readonly List<HackerLetter> _letters = [];
    private bool _staticTextInitialized;
    private QualitySettings _currentSettings = QualityPresets[RenderQuality.Medium];
    private float _currentExtrusionOffsetX;
    private float _currentExtrusionOffsetY;
    private float _smoothedTextSize = BASE_TEXT_SIZE;
    private float _lastBarCount;
    private float _barCountFactor;
    private SKPaint? _reusableExtrusionPaint;
    private SKPaint? _reusableFacePaint;

    private struct HackerLetter
    {
        public float X;
        public float BaseY;
        public float CurrentY;
        public float VelocityY;
        public float SmoothedMagnitude;
        public float CharWidth;
        public char Character;
        public SKPath? Path;
    }

    protected override void OnInitialize()
    {
        base.OnInitialize();
        InitializeFont();
        InitializeReusablePaints();
        LogDebug("Initialized");
    }

    private void InitializeFont()
    {
        _font = new SKFont
        {
            Size = BASE_TEXT_SIZE,
            Edging = UseAntiAlias ? SKFontEdging.SubpixelAntialias : SKFontEdging.Alias
        };
        _smoothedTextSize = BASE_TEXT_SIZE;
    }

    private void InitializeReusablePaints()
    {
        _reusableExtrusionPaint = GetPaint();
        _reusableExtrusionPaint.Style = SKPaintStyle.Fill;
        _reusableExtrusionPaint.IsAntialias = UseAntiAlias;

        _reusableFacePaint = GetPaint();
        _reusableFacePaint.Style = SKPaintStyle.Fill;
        _reusableFacePaint.IsAntialias = UseAntiAlias;
    }

    protected override void OnQualitySettingsApplied()
    {
        _currentSettings = QualityPresets[Quality];
        InvalidateLetterPaths();
        UpdatePaintSettings();
        _staticTextInitialized = false;
    }

    private void UpdatePaintSettings()
    {
        if (_reusableExtrusionPaint != null)
            _reusableExtrusionPaint.IsAntialias = UseAntiAlias;

        if (_reusableFacePaint != null)
            _reusableFacePaint.IsAntialias = UseAntiAlias;
    }

    private void InvalidateLetterPaths()
    {
        for (int i = 0; i < _letters.Count; i++)
        {
            var letter = _letters[i];
            if (letter.Path != null)
            {
                ReturnPath(letter.Path);
                letter.Path = null;
            }
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
        if (canvas == null || spectrum == null || paint == null)
            return;

        ApplyRenderParameters(barCount, barWidth, barSpacing, spectrum);

        if (!_staticTextInitialized)
            InitializeStaticTextLayout(info);

        if (_staticTextInitialized)
        {
            UpdateLetterPhysics(spectrum, info, barCount, GetAnimationDeltaTime());
            DrawLetters3D(canvas, paint.Color);
        }
    }

    private void ApplyRenderParameters(
        int barCount,
        float barWidth,
        float barSpacing,
        float[] spectrum)
    {
        if (_font == null)
        {
            InitializeFont();
            if (_font == null) return;
        }

        UpdateBarCountFactor(barCount);
        UpdateTextSize(spectrum);
        UpdateExtrusionOffsets(barWidth, barSpacing);
    }

    private void UpdateBarCountFactor(int barCount)
    {
        if (_lastBarCount != barCount)
        {
            _lastBarCount = barCount;
            float rawFactor = Log10(MathF.Max(1, barCount) / DEFAULT_BAR_COUNT + 0.9f) * 0.5f;
            _barCountFactor = Lerp(_barCountFactor, rawFactor, TEXT_SIZE_SMOOTHING_FACTOR);
        }
    }

    private void UpdateTextSize(float[] spectrum)
    {
        float avgMagnitude = CalculateAverageMagnitude(spectrum) * SensitivityScale;
        float sizeFromBars = CalculateBarBasedTextSize();
        float sizeFromSpectrum = CalculateSpectrumBasedTextSize(avgMagnitude);
        float targetSize = MathF.Max(sizeFromBars, sizeFromSpectrum);

        ApplySmoothTextSizeTransition(targetSize);
    }

    private float CalculateBarBasedTextSize() =>
        BASE_TEXT_SIZE * (1f + _barCountFactor * _currentSettings.TextScaleFactor);

    private static float CalculateSpectrumBasedTextSize(float magnitude) =>
        BASE_TEXT_SIZE * (1f + Pow(magnitude, 0.8f) * TEXT_SIZE_BAR_COUNT_SCALE);

    private void ApplySmoothTextSizeTransition(float targetSize)
    {
        float minSize = BASE_TEXT_SIZE * MIN_TEXT_SIZE_RATIO;
        float maxSize = BASE_TEXT_SIZE * MAX_TEXT_SIZE_RATIO;
        targetSize = Clamp(targetSize, minSize, maxSize);

        _smoothedTextSize = Lerp(_smoothedTextSize, targetSize, TEXT_SIZE_SMOOTHING_FACTOR);

        if (MathF.Abs(_smoothedTextSize - _font!.Size) > 0.05f)
        {
            _font.Size = _smoothedTextSize;
            _staticTextInitialized = false;
        }
    }

    private void UpdateExtrusionOffsets(float barWidth, float barSpacing)
    {
        float targetX = CalculateExtrusionOffset(barWidth, BASE_EXTRUSION_OFFSET_X);
        float targetY = CalculateExtrusionOffset(barSpacing, BASE_EXTRUSION_OFFSET_Y);

        _currentExtrusionOffsetX = Lerp(_currentExtrusionOffsetX, targetX, 0.1f);
        _currentExtrusionOffsetY = Lerp(_currentExtrusionOffsetY, targetY, 0.1f);
    }

    private float CalculateExtrusionOffset(float baseValue, float _) =>
        Clamp(baseValue * BASE_EXTRUSION_SCALE_FACTOR * _currentSettings.ExtrusionScaleFactor, -2f, 2f);

    private static float CalculateAverageMagnitude(float[] spectrum)
    {
        if (spectrum == null || spectrum.Length == 0) return 0f;

        float sum = 0f;
        for (int i = 0; i < spectrum.Length; i++)
            sum += spectrum[i];

        return sum / spectrum.Length;
    }

    private void InitializeStaticTextLayout(SKImageInfo info)
    {
        if (_font == null)
        {
            InitializeFont();
            if (_font == null) return;
        }

        InvalidateLetterPaths();
        _letters.Clear();

        float totalWidth = _font.MeasureText(STATIC_TEXT);
        float startX = (info.Width - totalWidth) / 2f;
        float baseY = info.Height * TEXT_Y_POSITION_RATIO;

        CreateLetterObjects(startX, baseY);
        _staticTextInitialized = true;
    }

    private void CreateLetterObjects(float startX, float baseY)
    {
        float x = startX;

        for (int i = 0; i < STATIC_TEXT.Length; i++)
        {
            char c = STATIC_TEXT[i];
            string charStr = c.ToString();
            float width = _font!.MeasureText(charStr);

            SKPath path = GetPath();
            SKPath textPath = _font.GetTextPath(charStr, new SKPoint(0, 0));

            path.AddPath(textPath);
            textPath.Dispose();

            _letters.Add(new HackerLetter
            {
                Character = c,
                X = x,
                BaseY = baseY,
                CurrentY = baseY,
                VelocityY = 0f,
                SmoothedMagnitude = 0f,
                CharWidth = width,
                Path = path
            });

            x += width;
        }
    }

    private void UpdateLetterPhysics(
        float[] spectrum,
        SKImageInfo info,
        int barCount,
        float deltaTime)
    {
        if (_letters.Count == 0 || barCount == 0 || deltaTime <= MIN_DELTA_TIME)
            return;

        for (int i = 0; i < _letters.Count; i++)
        {
            HackerLetter letter = _letters[i];
            UpdateSingleLetterPhysics(ref letter, spectrum, info.Width, barCount, deltaTime);
            _letters[i] = letter;
        }
    }

    private void UpdateSingleLetterPhysics(
        ref HackerLetter letter,
        float[] spectrum,
        float width,
        int barCount,
        float deltaTime)
    {
        if (barCount == 0 || spectrum.Length == 0)
            return;

        int idx = MapLetterToSpectrumIndex(letter, width, barCount, spectrum.Length);
        UpdateLetterMagnitude(ref letter, spectrum[idx]);
        ApplyJumpForce(ref letter, deltaTime);
        ApplyPhysics(ref letter, deltaTime);
    }

    private static int MapLetterToSpectrumIndex(
        in HackerLetter letter,
        float width,
        int barCount,
        int dataLength)
    {
        float centerX = letter.X + letter.CharWidth / 2f;
        int idx = (int)((centerX / width) * barCount);
        return Clamp(idx, 0, dataLength - 1);
    }

    private static void UpdateLetterMagnitude(ref HackerLetter letter, float magnitude)
    {
        letter.SmoothedMagnitude = letter.SmoothedMagnitude * (1f - LETTER_MAGNITUDE_SMOOTHING_FACTOR) +
            magnitude * LETTER_MAGNITUDE_SMOOTHING_FACTOR;
    }

    private void ApplyJumpForce(ref HackerLetter letter, float deltaTime)
    {
        if (letter.SmoothedMagnitude > MIN_SPECTRUM_FOR_JUMP)
            letter.VelocityY -= letter.SmoothedMagnitude *
                _currentSettings.JumpStrengthMultiplier *
                deltaTime *
                SensitivityScale;
    }

    private static void ApplyPhysics(ref HackerLetter letter, float deltaTime)
    {
        float displacement = letter.CurrentY - letter.BaseY;
        float physicsScale = deltaTime * TARGET_FPS_FOR_PHYSICS_SCALING;

        letter.VelocityY -= displacement * RETURN_FORCE_FACTOR * physicsScale;
        letter.VelocityY *= Pow(DAMPING_FACTOR, physicsScale);
        letter.CurrentY += letter.VelocityY * physicsScale;
    }

    private void DrawLetters3D(SKCanvas canvas, SKColor baseColor)
    {
        if (_font == null || _letters.Count == 0 || canvas == null)
            return;

        PrepareExtrusionPaint(baseColor);
        PrepareFacePaint();

        var (start, end) = CalculateGradientColors(baseColor);
        DrawAllLetters(canvas, start, end);
    }

    private void PrepareExtrusionPaint(SKColor color)
    {
        if (_reusableExtrusionPaint == null)
        {
            _reusableExtrusionPaint = GetPaint();
            _reusableExtrusionPaint.Style = SKPaintStyle.Fill;
            _reusableExtrusionPaint.IsAntialias = UseAntiAlias;
        }

        _reusableExtrusionPaint.Color = CalculateExtrusionColor(color);
    }

    private void PrepareFacePaint()
    {
        if (_reusableFacePaint == null)
        {
            _reusableFacePaint = GetPaint();
            _reusableFacePaint.Style = SKPaintStyle.Fill;
            _reusableFacePaint.IsAntialias = UseAntiAlias;
        }
    }

    private void DrawAllLetters(SKCanvas canvas, SKColor gradStart, SKColor gradEnd)
    {
        foreach (var letter in _letters)
        {
            if (letter.Path == null)
                continue;

            canvas.Save();
            canvas.Translate(letter.X, letter.CurrentY);
            DrawLetterExtrusion(canvas, letter.Path);
            DrawLetterFace(canvas, letter.Path, gradStart, gradEnd);
            canvas.Restore();
        }
    }

    private void DrawLetterExtrusion(SKCanvas canvas, SKPath path)
    {
        if (canvas == null || path == null || _reusableExtrusionPaint == null)
            return;

        int layers = (int)MathF.Max(0, _currentSettings.ExtrusionLayers);
        if (layers == 0)
            return;

        for (int layer = 1; layer <= layers; layer++)
        {
            canvas.Save();
            canvas.Translate(layer * _currentExtrusionOffsetX, layer * _currentExtrusionOffsetY);
            canvas.DrawPath(path, _reusableExtrusionPaint);
            canvas.Restore();
        }
    }

    private void DrawLetterFace(SKCanvas canvas, SKPath path, SKColor start, SKColor end)
    {
        if (canvas == null || path == null || _reusableFacePaint == null)
            return;

        using var shader = CreateLetterGradient(path, start, end);
        _reusableFacePaint.Shader = shader;
        canvas.DrawPath(path, _reusableFacePaint);
        _reusableFacePaint.Shader = null;
    }

    private static SKShader CreateLetterGradient(SKPath path, SKColor start, SKColor end) =>
        SKShader.CreateLinearGradient(
            new SKPoint(path.Bounds.Left, path.Bounds.Top),
            new SKPoint(path.Bounds.Left, path.Bounds.Bottom),
            [start, end],
            GradientColorPositions,
            SKShaderTileMode.Clamp);

    private static SKColor CalculateExtrusionColor(SKColor color) =>
        new(
            (byte)MathF.Max(0, color.Red - EXTRUSION_COLOR_DARKEN_FACTOR.Red),
            (byte)MathF.Max(0, color.Green - EXTRUSION_COLOR_DARKEN_FACTOR.Green),
            (byte)MathF.Max(0, color.Blue - EXTRUSION_COLOR_DARKEN_FACTOR.Blue),
            color.Alpha);

    private static (SKColor start, SKColor end) CalculateGradientColors(SKColor color) =>
        (
            new SKColor(
                (byte)MathF.Min(255, color.Red + GRADIENT_START_COLOR_OFFSET.Red),
                (byte)MathF.Min(255, color.Green + GRADIENT_START_COLOR_OFFSET.Green),
                (byte)MathF.Min(255, color.Blue + GRADIENT_START_COLOR_OFFSET.Blue),
                color.Alpha),
            new SKColor(
                (byte)MathF.Max(0, color.Red - GRADIENT_END_COLOR_OFFSET.Red),
                (byte)MathF.Max(0, color.Green - GRADIENT_END_COLOR_OFFSET.Green),
                (byte)MathF.Max(0, color.Blue - GRADIENT_END_COLOR_OFFSET.Blue),
                color.Alpha)
        );

    protected override void CleanupUnusedResources()
    {
        base.CleanupUnusedResources();

        if (!_staticTextInitialized && _letters.Count > 0)
        {
            InvalidateLetterPaths();
            _letters.Clear();
        }
    }

    protected override void OnDispose()
    {
        InvalidateLetterPaths();

        if (_reusableExtrusionPaint != null)
        {
            ReturnPaint(_reusableExtrusionPaint);
            _reusableExtrusionPaint = null;
        }

        if (_reusableFacePaint != null)
        {
            ReturnPaint(_reusableFacePaint);
            _reusableFacePaint = null;
        }

        _font?.Dispose();
        _font = null;
        _letters.Clear();
        _staticTextInitialized = false;

        base.OnDispose();
    }
}