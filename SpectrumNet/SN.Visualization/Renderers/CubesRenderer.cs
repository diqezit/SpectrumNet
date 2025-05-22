#nullable enable

using static SpectrumNet.SN.Visualization.Renderers.CubesRenderer.Constants;
using static SpectrumNet.SN.Visualization.Renderers.CubesRenderer.Constants.Quality;

namespace SpectrumNet.SN.Visualization.Renderers;

public sealed class CubesRenderer : EffectSpectrumRenderer
{
    private const string LogPrefix = nameof(CubesRenderer);

    private static readonly Lazy<CubesRenderer> _instance = new(() => new CubesRenderer());

    private CubesRenderer() { }

    public static CubesRenderer GetInstance() => _instance.Value;

    public record Constants
    {
        public const float
            CUBE_TOP_WIDTH_PROPORTION = 0.75f,
            CUBE_TOP_HEIGHT_PROPORTION = 0.25f;

        public const float
            ALPHA_MULTIPLIER = 255f,
            TOP_ALPHA_FACTOR = 0.8f,
            SIDE_FACE_ALPHA_FACTOR = 0.6f;

        public const int
            BATCH_SIZE = 32;

        public const byte MAX_ALPHA_BYTE = 255;

        public static class Quality
        {
            public const bool
                LOW_USE_ADVANCED_EFFECTS = false,
                MEDIUM_USE_ADVANCED_EFFECTS = true,
                HIGH_USE_ADVANCED_EFFECTS = true;

            public const bool
                LOW_USE_ANTIALIASING = false,
                MEDIUM_USE_ANTIALIASING = true,
                HIGH_USE_ANTIALIASING = true;

            public const bool
                LOW_USE_TOP_FACE_EFFECT = false,
                MEDIUM_USE_TOP_FACE_EFFECT = true,
                HIGH_USE_TOP_FACE_EFFECT = true;

            public const bool
                LOW_USE_SIDE_FACE_EFFECT = false,
                MEDIUM_USE_SIDE_FACE_EFFECT = true,
                HIGH_USE_SIDE_FACE_EFFECT = true;

            public const float
                LOW_TOP_ALPHA_FACTOR = 0.7f,
                MEDIUM_TOP_ALPHA_FACTOR = 0.8f,
                HIGH_TOP_ALPHA_FACTOR = 0.9f;

            public const float
                LOW_SIDE_FACE_ALPHA_FACTOR = 0.5f,
                MEDIUM_SIDE_FACE_ALPHA_FACTOR = 0.6f,
                HIGH_SIDE_FACE_ALPHA_FACTOR = 0.7f;

            public const float
                LOW_TOP_WIDTH_PROPORTION = 0.7f,
                MEDIUM_TOP_WIDTH_PROPORTION = 0.75f,
                HIGH_TOP_WIDTH_PROPORTION = 0.8f;

            public const float
                LOW_TOP_HEIGHT_PROPORTION = 0.2f,
                MEDIUM_TOP_HEIGHT_PROPORTION = 0.25f,
                HIGH_TOP_HEIGHT_PROPORTION = 0.3f;
        }
    }

    private bool _useTopFaceEffect = true;
    private bool _useSideFaceEffect = true;
    private float _topAlphaFactor = TOP_ALPHA_FACTOR;
    private float _sideFaceAlphaFactor = SIDE_FACE_ALPHA_FACTOR;
    private float _topWidthProportion = CUBE_TOP_WIDTH_PROPORTION;
    private float _topHeightProportion = CUBE_TOP_HEIGHT_PROPORTION;

    protected override void OnInitialize()
    {
        base.OnInitialize();
        _logger.Log(LogLevel.Debug, LogPrefix, "Initialized");
    }

    protected override void OnConfigurationChanged() =>
        _logger.Log(LogLevel.Debug, LogPrefix, $"Configuration changed. New Quality: {Quality}");

    protected override void OnQualitySettingsApplied()
    {
        switch (Quality)
        {
            case RenderQuality.Low:
                LowQualitySettings();
                break;
            case RenderQuality.Medium:
                MediumQualitySettings();
                break;
            case RenderQuality.High:
                HighQualitySettings();
                break;
        }

        _logger.Log(LogLevel.Debug, LogPrefix,
            $"Quality settings applied. New Quality: {Quality}, AntiAlias: {_useAntiAlias}, " +
            $"AdvancedEffects: {_useAdvancedEffects}, TopFace: {_useTopFaceEffect}, " +
            $"SideFace: {_useSideFaceEffect}");
    }

    private void LowQualitySettings()
    {
        _useAntiAlias = LOW_USE_ANTIALIASING;
        _useAdvancedEffects = LOW_USE_ADVANCED_EFFECTS;
        _useTopFaceEffect = LOW_USE_TOP_FACE_EFFECT;
        _useSideFaceEffect = LOW_USE_SIDE_FACE_EFFECT;
        _topAlphaFactor = LOW_TOP_ALPHA_FACTOR;
        _sideFaceAlphaFactor = LOW_SIDE_FACE_ALPHA_FACTOR;
        _topWidthProportion = LOW_TOP_WIDTH_PROPORTION;
        _topHeightProportion = LOW_TOP_HEIGHT_PROPORTION;
    }

    private void MediumQualitySettings()
    {
        _useAntiAlias = MEDIUM_USE_ANTIALIASING;
        _useAdvancedEffects = MEDIUM_USE_ADVANCED_EFFECTS;
        _useTopFaceEffect = MEDIUM_USE_TOP_FACE_EFFECT;
        _useSideFaceEffect = MEDIUM_USE_SIDE_FACE_EFFECT;
        _topAlphaFactor = MEDIUM_TOP_ALPHA_FACTOR;
        _sideFaceAlphaFactor = MEDIUM_SIDE_FACE_ALPHA_FACTOR;
        _topWidthProportion = MEDIUM_TOP_WIDTH_PROPORTION;
        _topHeightProportion = MEDIUM_TOP_HEIGHT_PROPORTION;
    }

    private void HighQualitySettings()
    {
        _useAntiAlias = HIGH_USE_ANTIALIASING;
        _useAdvancedEffects = HIGH_USE_ADVANCED_EFFECTS;
        _useTopFaceEffect = HIGH_USE_TOP_FACE_EFFECT;
        _useSideFaceEffect = HIGH_USE_SIDE_FACE_EFFECT;
        _topAlphaFactor = HIGH_TOP_ALPHA_FACTOR;
        _sideFaceAlphaFactor = HIGH_SIDE_FACE_ALPHA_FACTOR;
        _topWidthProportion = HIGH_TOP_WIDTH_PROPORTION;
        _topHeightProportion = HIGH_TOP_HEIGHT_PROPORTION;
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
        _logger.Safe(
            () =>
            {
                float canvasHeight = info.Height;

                using var cubePaint = _paintPool.Get();
                cubePaint.Color = paint.Color;
                cubePaint.IsAntialias = _useAntiAlias;

                for (int i = 0; i < spectrum.Length; i++)
                {
                    float magnitude = spectrum[i];
                    if (magnitude < MIN_MAGNITUDE_THRESHOLD)
                        continue;

                    float height = magnitude * canvasHeight;
                    float x = i * (barWidth + barSpacing);
                    float y = canvasHeight - height;

                    if (IsCubeOutsideViewport(canvas, x, y, barWidth, height)) continue;

                    cubePaint.Color = paint.Color.WithAlpha(CalculateCubeAlpha(magnitude));

                    RenderCube(canvas, x, y, barWidth, height, magnitude, cubePaint);
                }
            },
            LogPrefix,
            "Error during rendering"
        );
    }

    private bool IsCubeOutsideViewport(
        SKCanvas canvas,
        float x,
        float y,
        float barWidth,
        float height) =>
        canvas.QuickReject(new SKRect(
            x,
            y - barWidth * _topHeightProportion,
            x + barWidth + barWidth * _topWidthProportion,
            y + height));

    private static byte CalculateCubeAlpha(float magnitude) =>
        (byte)MathF.Min(magnitude * ALPHA_MULTIPLIER, MAX_ALPHA_BYTE);

    private void RenderCube(
        SKCanvas canvas,
        float x,
        float y,
        float barWidth,
        float height,
        float magnitude,
        SKPaint paint)
    {
        canvas.DrawRect(x, y, barWidth, height, paint);

        if (_useAdvancedEffects)
        {
            if (_useTopFaceEffect)
            {
                RenderCubeTopFace(canvas, x, y, barWidth, magnitude, paint);
            }

            if (_useSideFaceEffect)
            {
                RenderCubeSideFace(canvas, x, y, barWidth, height, magnitude, paint);
            }
        }
    }

    private void RenderCubeTopFace(
        SKCanvas canvas,
        float x,
        float y,
        float barWidth,
        float magnitude,
        SKPaint basePaint)
    {
        float topRightX = x + barWidth;
        float topOffsetX = barWidth * _topWidthProportion;
        float topOffsetY = barWidth * _topHeightProportion;
        float topXLeft = x - (barWidth - topOffsetX);

        using var topPath = _pathPool.Get();
        topPath.MoveTo(x, y);
        topPath.LineTo(topRightX, y);
        topPath.LineTo(x + topOffsetX, y - topOffsetY);
        topPath.LineTo(topXLeft, y - topOffsetY);
        topPath.Close();

        using var topPaint = _paintPool.Get();
        topPaint.Color = basePaint.Color.WithAlpha(
            (byte)MathF.Min(magnitude * ALPHA_MULTIPLIER * _topAlphaFactor, MAX_ALPHA_BYTE));
        topPaint.IsAntialias = _useAntiAlias;
        topPaint.Style = basePaint.Style;
        canvas.DrawPath(topPath, topPaint);
    }

    private void RenderCubeSideFace(
        SKCanvas canvas,
        float x,
        float y,
        float barWidth,
        float height,
        float magnitude,
        SKPaint basePaint)
    {
        float topRightX = x + barWidth;
        float topOffsetX = barWidth * _topWidthProportion;
        float topOffsetY = barWidth * _topHeightProportion;

        using var sidePath = _pathPool.Get();
        sidePath.MoveTo(topRightX, y);
        sidePath.LineTo(topRightX, y + height);
        sidePath.LineTo(x + topOffsetX, y - topOffsetY + height);
        sidePath.LineTo(x + topOffsetX, y - topOffsetY);
        sidePath.Close();

        using var sidePaint = _paintPool.Get();
        sidePaint.Color = basePaint.Color.WithAlpha(
            (byte)MathF.Min(magnitude * ALPHA_MULTIPLIER * _sideFaceAlphaFactor, MAX_ALPHA_BYTE));
        sidePaint.IsAntialias = _useAntiAlias;
        sidePaint.Style = basePaint.Style;
        canvas.DrawPath(sidePath, sidePaint);
    }

    protected override void OnDispose()
    {
        base.OnDispose();
        _logger.Log(LogLevel.Debug, LogPrefix, "Disposed");
    }
}