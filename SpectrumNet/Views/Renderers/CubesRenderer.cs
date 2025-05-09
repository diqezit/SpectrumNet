#nullable enable

using static SpectrumNet.Views.Renderers.CubesRenderer.Constants;
using static SpectrumNet.Views.Renderers.CubesRenderer.Constants.Quality;
using static System.MathF;

namespace SpectrumNet.Views.Renderers;

public sealed class CubesRenderer : EffectSpectrumRenderer
{
    private static readonly Lazy<CubesRenderer> _instance = new(() => new CubesRenderer());

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

    private new bool _useAntiAlias = true;
    private new bool _useAdvancedEffects = true;
    private bool _useTopFaceEffect = true;
    private bool _useSideFaceEffect = true;
    private float _topAlphaFactor = TOP_ALPHA_FACTOR;
    private float _sideFaceAlphaFactor = SIDE_FACE_ALPHA_FACTOR;
    private float _topWidthProportion = CUBE_TOP_WIDTH_PROPORTION;
    private float _topHeightProportion = CUBE_TOP_HEIGHT_PROPORTION;
    private volatile bool _isConfiguring;

    protected override void OnInitialize()
    {
        ExecuteSafely(
            () =>
            {
                base.OnInitialize();
                if (_disposed) ResetRendererState();
                InitializeQualitySettings();
                Log(LogLevel.Debug, LOG_PREFIX, "Initialized");
            },
            nameof(OnInitialize),
            "Failed during renderer initialization"
        );
    }

    private void InitializeQualitySettings() => 
        ApplyQualitySettingsInternal();

    private void ResetRendererState() =>
        _disposed = false;

    public override void Configure(
        bool isOverlayActive,
        RenderQuality quality = RenderQuality.Medium)
    {
        ExecuteSafely(
            () =>
            {
                if (_isConfiguring) return;

                try
                {
                    _isConfiguring = true;
                    bool configChanged = _isOverlayActive != isOverlayActive
                                         || Quality != quality;

                    _isOverlayActive = isOverlayActive;
                    Quality = quality;
                    _smoothingFactor = isOverlayActive ? 0.5f : 0.3f;

                    if (configChanged)
                    {
                        ApplyQualitySettingsInternal();
                        OnConfigurationChanged();
                    }
                }
                finally
                {
                    _isConfiguring = false;
                }
            },
            nameof(Configure),
            "Failed to configure renderer"
        );
    }

    protected override void OnConfigurationChanged()
    {
        ExecuteSafely(
            () =>
            {
                Log(LogLevel.Debug,
                    LOG_PREFIX,
                    $"Configuration changed. New Quality: {Quality}");
            },
            nameof(OnConfigurationChanged),
            "Failed to handle configuration change"
        );
    }

    protected override void ApplyQualitySettings()
    {
        ExecuteSafely(
            () =>
            {
                if (_isConfiguring) return;

                try
                {
                    _isConfiguring = true;
                    base.ApplyQualitySettings();
                    ApplyQualitySettingsInternal();
                }
                finally
                {
                    _isConfiguring = false;
                }
            },
            nameof(ApplyQualitySettings),
            "Failed to apply quality settings"
        );
    }

    private void ApplyQualitySettingsInternal()
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

        Log(LogLevel.Debug, LOG_PREFIX,
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

    protected override void OnQualitySettingsApplied()
    {
        ExecuteSafely(
            () =>
            {
                base.OnQualitySettingsApplied();
            },
            nameof(OnQualitySettingsApplied),
            "Failed to apply specific quality settings"
        );
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
            nameof(RenderEffect),
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
        Log(LogLevel.Error,
            LOG_PREFIX,
            $"Invalid image dimensions: {info.Width}x{info.Height}");
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
            nameof(Dispose),
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
            nameof(OnDispose),
            "Error during specific disposal"
        );
    }
}