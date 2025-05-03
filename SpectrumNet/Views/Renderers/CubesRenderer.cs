#nullable enable

using static System.MathF;

namespace SpectrumNet.Views.Renderers;

public sealed class CubesRenderer : EffectSpectrumRenderer
{
    private static class Constants
    {
        public const string
            LOG_PREFIX = "CubesRenderer";

        public const float
            CUBE_TOP_WIDTH_PROPORTION = 0.75f,
            CUBE_TOP_HEIGHT_PROPORTION = 0.25f,
            ALPHA_MULTIPLIER = 255f,
            TOP_ALPHA_FACTOR = 0.8f,
            SIDE_FACE_ALPHA_FACTOR = 0.6f;

        public const int
            BATCH_SIZE = 32;
    }

    private static readonly Lazy<CubesRenderer> _instance = new(() => new CubesRenderer());

    private readonly SKPath _cubeTopPath = new();
    private bool _useGlowEffects = true;

    private CubesRenderer() { }

    public static CubesRenderer GetInstance() => _instance.Value;

    protected override void OnQualitySettingsApplied()
    {
        Safe(() =>
        {
            base.OnQualitySettingsApplied();
            _useGlowEffects = base.Quality switch
            {
                RenderQuality.Low => false,
                RenderQuality.Medium => true,
                RenderQuality.High => true,
                _ => true
            };
            Log(LogLevel.Debug, Constants.LOG_PREFIX, $"Quality set to {base.Quality}");
        }, new ErrorHandlingOptions
        {
            Source = $"{GetType().Name}.OnQualitySettingsApplied",
            ErrorMessage = "Failed to apply cubes specific quality settings"
        });
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
        float canvasHeight = info.Height;

        using var cubePaint = _paintPool.Get();
        cubePaint.Color = paint.Color;
        cubePaint.IsAntialias = UseAntiAlias;

        for (int i = 0; i < spectrum.Length; i++)
        {
            float magnitude = spectrum[i];
            if (magnitude < MIN_MAGNITUDE_THRESHOLD)
                continue;

            float height = magnitude * canvasHeight;
            float x = i * (barWidth + barSpacing);
            float y = canvasHeight - height;

            if (canvas.QuickReject(new SKRect(x, y - barWidth * Constants.CUBE_TOP_HEIGHT_PROPORTION, x + barWidth + barWidth * Constants.CUBE_TOP_WIDTH_PROPORTION, y + height)))
                continue;

            cubePaint.Color = paint.Color.WithAlpha((byte)MathF.Min(magnitude * Constants.ALPHA_MULTIPLIER, 255f));

            RenderCube(canvas, x, y, barWidth, height, magnitude, cubePaint);
        }
    }

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

        if (UseAdvancedEffects && _useGlowEffects)
        {
            float topRightX = x + barWidth;
            float topOffsetX = barWidth * Constants.CUBE_TOP_WIDTH_PROPORTION;
            float topOffsetY = barWidth * Constants.CUBE_TOP_HEIGHT_PROPORTION;
            float topXLeft = x - (barWidth - topOffsetX);

            using var topPath = _pathPool.Get();
            topPath.MoveTo(x, y);
            topPath.LineTo(topRightX, y);
            topPath.LineTo(x + topOffsetX, y - topOffsetY);
            topPath.LineTo(topXLeft, y - topOffsetY);
            topPath.Close();

            using var topPaint = _paintPool.Get();
            topPaint.Color = paint.Color.WithAlpha(
                (byte)MathF.Min(magnitude * Constants.ALPHA_MULTIPLIER * Constants.TOP_ALPHA_FACTOR, 255f));
            topPaint.IsAntialias = paint.IsAntialias;
            topPaint.Style = paint.Style;
            canvas.DrawPath(topPath, topPaint);


            using var sidePath = _pathPool.Get();
            sidePath.MoveTo(topRightX, y);
            sidePath.LineTo(topRightX, y + height);
            sidePath.LineTo(x + topOffsetX, y - topOffsetY + height);
            sidePath.LineTo(x + topOffsetX, y - topOffsetY);
            sidePath.Close();

            using var sidePaint = _paintPool.Get();
            sidePaint.Color = paint.Color.WithAlpha(
                (byte)MathF.Min(magnitude * Constants.ALPHA_MULTIPLIER * Constants.SIDE_FACE_ALPHA_FACTOR, 255f));
            sidePaint.IsAntialias = paint.IsAntialias;
            sidePaint.Style = paint.Style;
            canvas.DrawPath(sidePath, sidePaint);
        }
    }

    protected override void OnDispose()
    {
        Safe(() =>
        {
            _cubeTopPath?.Dispose();
            base.OnDispose();
        }, new ErrorHandlingOptions
        {
            Source = $"{GetType().Name}.OnDispose",
            ErrorMessage = "Error during CubesRenderer disposal"
        });
    }
}