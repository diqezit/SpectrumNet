#nullable enable

using static SpectrumNet.SN.Visualization.Renderers.CubesRenderer.Constants;

namespace SpectrumNet.SN.Visualization.Renderers;

public sealed class CubesRenderer : EffectSpectrumRenderer
{
    private const string LogPrefix = nameof(CubesRenderer);

    private static readonly Lazy<CubesRenderer> _instance = new(() => new CubesRenderer());

    private CubesRenderer() { }

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
        public const byte MAX_ALPHA_BYTE = 255;

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

    protected override void OnInitialize()
    {
        base.OnInitialize();
        _logger.Log(LogLevel.Debug, LogPrefix, "Initialized");
    }

    protected override void OnQualitySettingsApplied()
    {
        _currentSettings = QualityPresets[Quality];
        _logger.Log(LogLevel.Debug, LogPrefix, $"Quality changed to {Quality}");
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
            () => RenderCubes(
                canvas,
                spectrum,
                info,
                barWidth,
                barSpacing,
                paint),
            LogPrefix,
            "Error during rendering"
        );
    }

    private void RenderCubes(
        SKCanvas canvas,
        float[] spectrum,
        SKImageInfo info,
        float barWidth,
        float barSpacing,
        SKPaint basePaint)
    {
        float canvasHeight = info.Height;
        int spectrumLength = (int)MathF.Min(
            spectrum.Length,
            info.Width / (barWidth + barSpacing));

        for (int i = 0; i < spectrumLength; i += CUBE_BATCH_SIZE)
        {
            int batchEnd = (int)MathF.Min(i + CUBE_BATCH_SIZE, spectrumLength);
            RenderBatch(
                canvas,
                spectrum,
                i,
                batchEnd,
                basePaint,
                barWidth,
                barSpacing,
                canvasHeight);
        }
    }

    private void RenderBatch(
        SKCanvas canvas,
        float[] spectrum,
        int start,
        int end,
        SKPaint basePaint,
        float barWidth,
        float barSpacing,
        float canvasHeight)
    {
        for (int i = start; i < end; i++)
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

            RenderSingleCube(
                canvas,
                x,
                y,
                barWidth,
                height,
                magnitude,
                basePaint);
        }
    }

    private void RenderSingleCube(
        SKCanvas canvas,
        float x,
        float y,
        float barWidth,
        float height,
        float magnitude,
        SKPaint basePaint)
    {
        byte alpha = (byte)MathF.Min(
            magnitude * ALPHA_MULTIPLIER,
            MAX_ALPHA_BYTE);

        using var cubePaint = CreateStandardPaint(
            basePaint.Color.WithAlpha(alpha));
        canvas.DrawRect(x, y, barWidth, height, cubePaint);

        if (!UseAdvancedEffects) return;

        if (_currentSettings.UseTopFaceEffect)
        {
            RenderCubeTopFace(canvas, x, y, barWidth, magnitude);
        }

        if (_currentSettings.UseSideFaceEffect)
        {
            RenderCubeSideFace(canvas, x, y, barWidth, height, magnitude);
        }
    }

    private void RenderCubeTopFace(
        SKCanvas canvas,
        float x,
        float y,
        float barWidth,
        float magnitude)
    {
        float topOffsetX = barWidth * _currentSettings.TopWidthProportion;
        float topOffsetY = barWidth * _currentSettings.TopHeightProportion;

        var topPath = _resourceManager.GetPath();
        try
        {
            topPath.MoveTo(x, y);
            topPath.LineTo(x + barWidth, y);
            topPath.LineTo(x + topOffsetX, y - topOffsetY);
            topPath.LineTo(x - (barWidth - topOffsetX), y - topOffsetY);
            topPath.Close();

            byte alpha = (byte)MathF.Min(
                magnitude * ALPHA_MULTIPLIER * _currentSettings.TopAlphaFactor,
                MAX_ALPHA_BYTE);
            using var topPaint = CreateStandardPaint(
                SKColors.White.WithAlpha(alpha));

            canvas.DrawPath(topPath, topPaint);
        }
        finally
        {
            _resourceManager.ReturnPath(topPath);
        }
    }

    private void RenderCubeSideFace(
        SKCanvas canvas,
        float x,
        float y,
        float barWidth,
        float height,
        float magnitude)
    {
        float topOffsetX = barWidth * _currentSettings.TopWidthProportion;
        float topOffsetY = barWidth * _currentSettings.TopHeightProportion;

        var sidePath = _resourceManager.GetPath();
        try
        {
            sidePath.MoveTo(x + barWidth, y);
            sidePath.LineTo(x + barWidth, y + height);
            sidePath.LineTo(x + topOffsetX, y - topOffsetY + height);
            sidePath.LineTo(x + topOffsetX, y - topOffsetY);
            sidePath.Close();

            byte alpha = (byte)MathF.Min(
                magnitude * ALPHA_MULTIPLIER * _currentSettings.SideFaceAlphaFactor,
                MAX_ALPHA_BYTE);
            using var sidePaint = CreateStandardPaint(
                SKColors.Gray.WithAlpha(alpha));

            canvas.DrawPath(sidePath, sidePaint);
        }
        finally
        {
            _resourceManager.ReturnPath(sidePath);
        }
    }

    protected override void OnDispose()
    {
        base.OnDispose();
        _logger.Log(LogLevel.Debug, LogPrefix, "Disposed");
    }
}