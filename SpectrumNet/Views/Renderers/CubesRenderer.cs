#nullable enable

using static SpectrumNet.Views.Renderers.CubesRenderer.Constants;
using static System.MathF;

namespace SpectrumNet.Views.Renderers;

public sealed class CubesRenderer : EffectSpectrumRenderer
{
    private static readonly Lazy<CubesRenderer> _instance = new(() => new CubesRenderer());

    private new bool _useAntiAlias = true;
    private new bool _useAdvancedEffects = true;

    private CubesRenderer() { }

    public static CubesRenderer GetInstance() => _instance.Value;

    public record Constants
    {
        public const string LOG_PREFIX = "CubesRenderer";

        public const float
            CUBE_TOP_WIDTH_PROPORTION = 0.75f,
            CUBE_TOP_HEIGHT_PROPORTION = 0.25f;

        public const float
            ALPHA_MULTIPLIER = 255f,
            TOP_ALPHA_FACTOR = 0.8f,
            SIDE_FACE_ALPHA_FACTOR = 0.6f;

        public const int
            BATCH_SIZE = 32;

        public static class Quality
        {
            public const bool
                LOW_USE_ADVANCED_EFFECTS = false,
                MEDIUM_USE_ADVANCED_EFFECTS = true,
                HIGH_USE_ADVANCED_EFFECTS = true;
        }
    }

    protected override void OnInitialize()
    {
        ExecuteSafely(
            () =>
            {
                base.OnInitialize();
                if (_disposed) ResetRendererState();
                Log(LogLevel.Debug, LOG_PREFIX, "Initialized");
            },
            "OnInitialize",
            "Failed during renderer initialization"
        );
    }

    private void ResetRendererState() =>
        _disposed = false;

    public override void Configure(
        bool isOverlayActive,
        RenderQuality quality = RenderQuality.Medium)
    {
        ExecuteSafely(
            () =>
            {
                bool configChanged = _isOverlayActive != isOverlayActive || Quality != quality;
                base.Configure(isOverlayActive, quality);
                if (configChanged)
                {
                    Log(LogLevel.Debug, LOG_PREFIX, $"Configuration changed. New Quality: {Quality}");
                    OnConfigurationChanged();
                }
            },
            "Configure",
            "Failed to configure renderer"
        );
    }

    protected override void OnQualitySettingsApplied()
    {
        ExecuteSafely(
            () =>
            {
                base.OnQualitySettingsApplied();
                ApplyQualitySpecificSettings();
                Log(LogLevel.Debug, LOG_PREFIX, $"Quality settings applied. New Quality: {Quality}");
            },
            "OnQualitySettingsApplied",
            "Failed to apply specific quality settings"
        );
    }

    private void ApplyQualitySpecificSettings()
    {
        switch (Quality)
        {
            case RenderQuality.Low:
                _useAntiAlias = false;
                _useAdvancedEffects = Constants.Quality.LOW_USE_ADVANCED_EFFECTS;
                break;
            case RenderQuality.Medium:
                _useAntiAlias = true;
                _useAdvancedEffects = Constants.Quality.MEDIUM_USE_ADVANCED_EFFECTS;
                break;
            case RenderQuality.High:
                _useAntiAlias = true;
                _useAdvancedEffects = Constants.Quality.HIGH_USE_ADVANCED_EFFECTS;
                break;
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
        if (!ValidateRenderParameters(canvas, spectrum, info, paint)) return;

        ExecuteSafely(
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
            "RenderEffect",
            "Error during rendering"
        );
    }

    private static bool IsCubeOutsideViewport(
        SKCanvas canvas,
        float x,
        float y,
        float barWidth,
        float height) =>
        canvas.QuickReject(new SKRect(
            x,
            y - barWidth * CUBE_TOP_HEIGHT_PROPORTION,
            x + barWidth + barWidth * CUBE_TOP_WIDTH_PROPORTION,
            y + height));

    private static byte CalculateCubeAlpha(float magnitude) =>
        (byte)MathF.Min(magnitude * ALPHA_MULTIPLIER, 255f);

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
            RenderCubeTopFace(canvas, x, y, barWidth, magnitude, paint);
            RenderCubeSideFace(canvas, x, y, barWidth, height, magnitude, paint);
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
        float topOffsetX = barWidth * CUBE_TOP_WIDTH_PROPORTION;
        float topOffsetY = barWidth * CUBE_TOP_HEIGHT_PROPORTION;
        float topXLeft = x - (barWidth - topOffsetX);

        using var topPath = _pathPool.Get();
        topPath.MoveTo(x, y);
        topPath.LineTo(topRightX, y);
        topPath.LineTo(x + topOffsetX, y - topOffsetY);
        topPath.LineTo(topXLeft, y - topOffsetY);
        topPath.Close();

        using var topPaint = _paintPool.Get();
        topPaint.Color = basePaint.Color.WithAlpha(
            (byte)MathF.Min(magnitude * ALPHA_MULTIPLIER * TOP_ALPHA_FACTOR, 255f));
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
        float topOffsetX = barWidth * CUBE_TOP_WIDTH_PROPORTION;
        float topOffsetY = barWidth * CUBE_TOP_HEIGHT_PROPORTION;

        using var sidePath = _pathPool.Get();
        sidePath.MoveTo(topRightX, y);
        sidePath.LineTo(topRightX, y + height);
        sidePath.LineTo(x + topOffsetX, y - topOffsetY + height);
        sidePath.LineTo(x + topOffsetX, y - topOffsetY);
        sidePath.Close();

        using var sidePaint = _paintPool.Get();
        sidePaint.Color = basePaint.Color.WithAlpha(
            (byte)MathF.Min(magnitude * ALPHA_MULTIPLIER * SIDE_FACE_ALPHA_FACTOR, 255f));
        sidePaint.IsAntialias = _useAntiAlias;
        sidePaint.Style = basePaint.Style;
        canvas.DrawPath(sidePath, sidePaint);
    }

    private bool ValidateRenderParameters(
        SKCanvas? canvas,
        float[]? spectrum,
        SKImageInfo info,
        SKPaint? paint)
    {
        if (!IsCanvasValid(canvas)) return false;
        if (!IsSpectrumValid(spectrum)) return false;
        if (!IsPaintValid(paint)) return false;
        if (!AreDimensionsValid(info)) return false;
        if (IsDisposed()) return false;
        return true;
    }

    private static bool IsCanvasValid(SKCanvas? canvas)
    {
        if (canvas != null) return true;
        Log(LogLevel.Error, LOG_PREFIX, "Canvas is null");
        return false;
    }

    private static bool IsSpectrumValid(float[]? spectrum)
    {
        if (spectrum != null && spectrum.Length > 0) return true;
        Log(LogLevel.Error, LOG_PREFIX, "Spectrum is null or empty");
        return false;
    }

    private static bool IsPaintValid(SKPaint? paint)
    {
        if (paint != null) return true;
        Log(LogLevel.Error, LOG_PREFIX, "Paint is null");
        return false;
    }

    private static bool AreDimensionsValid(SKImageInfo info)
    {
        if (info.Width > 0 && info.Height > 0) return true;
        Log(LogLevel.Error, LOG_PREFIX, $"Invalid image dimensions: {info.Width}x{info.Height}");
        return false;
    }

    private bool IsDisposed()
    {
        if (!_disposed) return false;
        Log(LogLevel.Error, LOG_PREFIX, "Renderer is disposed");
        return true;
    }

    public override void Dispose()
    {
        if (_disposed) return;
        ExecuteSafely(
            () =>
            {
                OnDispose();
            },
            "Dispose",
            "Error during disposal"
        );
        _disposed = true;
        GC.SuppressFinalize(this);
        Log(LogLevel.Debug, LOG_PREFIX, "Disposed");
    }

    protected override void OnDispose()
    {
        ExecuteSafely(
            () =>
            {
                base.OnDispose();
            },
            "OnDispose",
            "Error during specific disposal"
        );
    }
}