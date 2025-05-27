#nullable enable

using static SpectrumNet.SN.Visualization.Renderers.CubesRenderer.Constants;

namespace SpectrumNet.SN.Visualization.Renderers;

public sealed class CubesRenderer() : EffectSpectrumRenderer
{
    private static readonly Lazy<CubesRenderer> _instance =
        new(() => new CubesRenderer());

    public static CubesRenderer GetInstance() => _instance.Value;

    public static class Constants
    {
        public const float
            CUBE_TOP_WIDTH_PROPORTION = 0.75f,
            CUBE_TOP_HEIGHT_PROPORTION = 0.25f,
            ALPHA_MULTIPLIER = 255f,
            TOP_ALPHA_FACTOR = 0.8f,
            SIDE_FACE_ALPHA_FACTOR = 0.6f;

        public const int CUBE_BATCH_SIZE = 32;

        public static readonly Dictionary<RenderQuality, CubeQualitySettings> QualityPresets = new()
        {
            [RenderQuality.Low] = new(
                UseTopFaceEffect: false,
                UseSideFaceEffect: false,
                TopAlphaFactor: 0.7f,
                SideFaceAlphaFactor: 0.5f,
                TopWidthProportion: 0.7f,
                TopHeightProportion: 0.2f
            ),
            [RenderQuality.Medium] = new(
                UseTopFaceEffect: true,
                UseSideFaceEffect: true,
                TopAlphaFactor: TOP_ALPHA_FACTOR,
                SideFaceAlphaFactor: SIDE_FACE_ALPHA_FACTOR,
                TopWidthProportion: CUBE_TOP_WIDTH_PROPORTION,
                TopHeightProportion: CUBE_TOP_HEIGHT_PROPORTION
            ),
            [RenderQuality.High] = new(
                UseTopFaceEffect: true,
                UseSideFaceEffect: true,
                TopAlphaFactor: 0.9f,
                SideFaceAlphaFactor: 0.7f,
                TopWidthProportion: 0.8f,
                TopHeightProportion: 0.3f
            )
        };

        public record CubeQualitySettings(
            bool UseTopFaceEffect,
            bool UseSideFaceEffect,
            float TopAlphaFactor,
            float SideFaceAlphaFactor,
            float TopWidthProportion,
            float TopHeightProportion
        );
    }

    private CubeQualitySettings _currentSettings = QualityPresets[RenderQuality.Medium];

    protected override void OnQualitySettingsApplied() =>
        _currentSettings = QualityPresets[Quality];

    protected override void RenderEffect(
        SKCanvas canvas,
        float[] spectrum,
        SKImageInfo info,
        float barWidth,
        float barSpacing,
        int barCount,
        SKPaint paint)
    {
        float canvasHeight = info.Height;
        int spectrumLength = Min(
            spectrum.Length,
            (int)(info.Width / (barWidth + barSpacing)));

        var rects = new List<SKRect>();
        var cubeData = new List<(float x, float y, float height, float magnitude)>();

        for (int i = 0; i < spectrumLength; i++)
        {
            float magnitude = spectrum[i];
            if (magnitude < MIN_MAGNITUDE_THRESHOLD) continue;

            float x = i * (barWidth + barSpacing);
            float height = magnitude * canvasHeight;
            float y = canvasHeight - height;

            if (!IsRenderAreaVisible(
                canvas,
                x,
                y - barWidth * _currentSettings.TopHeightProportion,
                barWidth + barWidth * _currentSettings.TopWidthProportion,
                height + barWidth * _currentSettings.TopHeightProportion)) continue;

            rects.Add(new SKRect(x, y, x + barWidth, y + height));
            cubeData.Add((x, y, height, magnitude));
        }

        RenderMainFaces(canvas, rects, cubeData, paint);

        if (UseAdvancedEffects)
        {
            if (_currentSettings.UseTopFaceEffect)
                RenderTopFaces(canvas, cubeData, barWidth);

            if (_currentSettings.UseSideFaceEffect)
                RenderSideFaces(canvas, cubeData, barWidth);
        }
    }

    private void RenderMainFaces(
        SKCanvas canvas,
        List<SKRect> rects,
        List<(float x, float y, float height, float magnitude)> cubeData,
        SKPaint basePaint)
    {
        var mainPaint = CreateStandardPaint(basePaint.Color);

        for (int i = 0; i < rects.Count && i < cubeData.Count; i++)
        {
            byte alpha = CalculateAlpha(cubeData[i].magnitude);
            mainPaint.Color = basePaint.Color.WithAlpha(alpha);
            canvas.DrawRect(rects[i], mainPaint);
        }

        ReturnPaint(mainPaint);
    }

    private void RenderTopFaces(
        SKCanvas canvas,
        List<(float x, float y, float height, float magnitude)> cubeData,
        float barWidth)
    {
        float topOffsetX = barWidth * _currentSettings.TopWidthProportion;
        float topOffsetY = barWidth * _currentSettings.TopHeightProportion;

        RenderBatch(canvas, path =>
        {
            foreach (var (x, y, _, magnitude) in cubeData)
            {
                path.MoveTo(x, y);
                path.LineTo(x + barWidth, y);
                path.LineTo(x + topOffsetX, y - topOffsetY);
                path.LineTo(x - (barWidth - topOffsetX), y - topOffsetY);
                path.Close();
            }
        }, CreateFacePaint(SKColors.White, cubeData, _currentSettings.TopAlphaFactor));
    }

    private void RenderSideFaces(
        SKCanvas canvas,
        List<(float x, float y, float height, float magnitude)> cubeData,
        float barWidth)
    {
        float topOffsetX = barWidth * _currentSettings.TopWidthProportion;
        float topOffsetY = barWidth * _currentSettings.TopHeightProportion;

        RenderBatch(canvas, path =>
        {
            foreach (var (x, y, height, _) in cubeData)
            {
                path.MoveTo(x + barWidth, y);
                path.LineTo(x + barWidth, y + height);
                path.LineTo(x + topOffsetX, y - topOffsetY + height);
                path.LineTo(x + topOffsetX, y - topOffsetY);
                path.Close();
            }
        }, CreateFacePaint(SKColors.Gray, cubeData, _currentSettings.SideFaceAlphaFactor));
    }

    private SKPaint CreateFacePaint(
        SKColor baseColor,
        List<(float x, float y, float height, float magnitude)> cubeData,
        float alphaFactor)
    {
        float avgMagnitude = cubeData.Count > 0 ?
            cubeData.Average(d => d.magnitude) : 0f;
        byte alpha = CalculateAlpha(avgMagnitude * alphaFactor);
        return CreateStandardPaint(baseColor.WithAlpha(alpha));
    }
}