#nullable enable

namespace SpectrumNet.SN.Visualization.Abstract.Core;

public abstract class EffectSpectrumRenderer<TQualitySettings> : ProcessingSpectrumRenderer
    where TQualitySettings : class
{
    private const int
        LOW_QUALITY_BAR_LIMIT = 150,
        MEDIUM_QUALITY_BAR_LIMIT = 75,
        HIGH_QUALITY_BAR_LIMIT = 75;

    private bool _useAdvancedEffects = true;
    protected bool UseAdvancedEffects => _useAdvancedEffects;

    protected abstract IReadOnlyDictionary<RenderQuality, TQualitySettings> QualitySettingsPresets { get; }
    protected TQualitySettings? CurrentQualitySettings { get; private set; }

    protected virtual int GetMaxBarsForQuality() => Quality switch
    {
        RenderQuality.Low => LOW_QUALITY_BAR_LIMIT,
        RenderQuality.Medium => MEDIUM_QUALITY_BAR_LIMIT,
        RenderQuality.High => HIGH_QUALITY_BAR_LIMIT,
        _ => HIGH_QUALITY_BAR_LIMIT
    };

    protected record RenderParameters(
        int EffectiveBarCount,
        float BarWidth,
        float BarSpacing,
        float StartOffset);

    protected virtual RenderParameters CalculateRenderParameters(
        SKImageInfo info,
        int requestedBarCount,
        float requestedBarWidth,
        float requestedBarSpacing)
    {
        int maxBars = GetMaxBarsForQuality();
        int effectiveBarCount = Max(1, Min(requestedBarCount, maxBars));

        if (info.Width <= 0 || effectiveBarCount == 0)
            return new RenderParameters(effectiveBarCount, 0, 0, 0);

        float fixedBarSpacing = requestedBarSpacing;
        float totalSpacingWidth = (effectiveBarCount - 1) * fixedBarSpacing;
        float availableWidthForBars = info.Width - totalSpacingWidth;
        float calculatedBarWidth = availableWidthForBars / effectiveBarCount;
        float startOffset = 0;

        if (effectiveBarCount == 0) calculatedBarWidth = 0;

        return new RenderParameters(
            effectiveBarCount,
            calculatedBarWidth,
            fixedBarSpacing,
            startOffset);
    }

    public override void Render(
        SKCanvas? canvas,
        float[]? spectrum,
        SKImageInfo info,
        float barWidth,
        float barSpacing,
        int barCount,
        SKPaint? paint,
        Action<SKCanvas, SKImageInfo>? drawPerformanceInfo)
    {
        if (!ValidateRenderInput(canvas, spectrum, paint, info, drawPerformanceInfo))
            return;

        if (!ValidateQualitySettings(canvas!, info, drawPerformanceInfo))
            return;

        var renderParams = CalculateRenderParameters(
            info,
            barCount,
            barWidth,
            barSpacing);

        if (!ValidateRenderParams(renderParams, canvas!, info, drawPerformanceInfo))
            return;

        var processedSpectrum = ProcessAndValidateSpectrum(
            spectrum!,
            renderParams.EffectiveBarCount,
            canvas!,
            info,
            drawPerformanceInfo);

        if (processedSpectrum == null)
            return;

        PerformRender(canvas!, processedSpectrum, info, renderParams, paint!);

        drawPerformanceInfo?.Invoke(canvas!, info);
    }

    private bool ValidateRenderInput(
        SKCanvas? canvas,
        float[]? spectrum,
        SKPaint? paint,
        SKImageInfo info,
        Action<SKCanvas, SKImageInfo>? drawPerformanceInfo)
    {
        if (!IsInitialized || canvas == null || spectrum == null ||
            paint == null || info.Width <= 0 || info.Height <= 0)
        {
            if (canvas != null)
                drawPerformanceInfo?.Invoke(canvas, info);
            return false;
        }
        return true;
    }

    private bool ValidateQualitySettings(
        SKCanvas canvas,
        SKImageInfo info,
        Action<SKCanvas, SKImageInfo>? drawPerformanceInfo)
    {
        if (CurrentQualitySettings == null)
        {
            LogError($"CurrentQualitySettings is null for {GetType().Name}. Cannot render effect.");
            drawPerformanceInfo?.Invoke(canvas, info);
            return false;
        }
        return true;
    }

    private static bool ValidateRenderParams(
        RenderParameters renderParams,
        SKCanvas canvas,
        SKImageInfo info,
        Action<SKCanvas, SKImageInfo>? drawPerformanceInfo)
    {
        if (renderParams.EffectiveBarCount <= 0)
        {
            drawPerformanceInfo?.Invoke(canvas, info);
            return false;
        }
        return true;
    }

    private float[]? ProcessAndValidateSpectrum(
        float[] spectrum,
        int effectiveBarCount,
        SKCanvas canvas,
        SKImageInfo info,
        Action<SKCanvas, SKImageInfo>? drawPerformanceInfo)
    {
        var (isValid, processedSpectrum) = ProcessSpectrum(
            spectrum,
            effectiveBarCount,
            applyTemporalSmoothing: true);

        if (!isValid || processedSpectrum == null)
        {
            drawPerformanceInfo?.Invoke(canvas, info);
            return null;
        }

        return processedSpectrum;
    }

    private void PerformRender(
        SKCanvas canvas,
        float[] processedSpectrum,
        SKImageInfo info,
        RenderParameters renderParams,
        SKPaint paint)
    {
        RenderWithOverlay(canvas, () =>
            RenderEffect(
                canvas,
                processedSpectrum,
                info,
                renderParams,
                paint));
    }

    protected void RenderWithOverlay(
        SKCanvas canvas,
        Action renderAction)
    {
        if (IsOverlayActive && OverlayAlpha > 0.001f)
        {
            using var layerPaint = new SKPaint
            {
                Color = SKColors.White.WithAlpha((byte)(255 * OverlayAlpha))
            };

            int saveCount = canvas.SaveLayer(layerPaint);
            try
            {
                renderAction();
            }
            finally
            {
                canvas.RestoreToCount(saveCount);
            }
        }
        else
        {
            renderAction();
        }
    }

    protected abstract void RenderEffect(
        SKCanvas canvas,
        float[] spectrum,
        SKImageInfo info,
        RenderParameters renderParams,
        SKPaint paint);

    protected override void OnQualitySettingsApplied()
    {
        base.OnQualitySettingsApplied();

        bool settingsApplied = ApplyQualitySettings();

        if (!settingsApplied)
        {
            CurrentQualitySettings = null;
            LogError($"Failed to apply quality settings for {GetType().Name}. CurrentQualitySettings is null.");
        }

        _useAdvancedEffects = Quality != RenderQuality.Low;
        RequestRedraw();
    }

    private bool ApplyQualitySettings()
    {
        if (QualitySettingsPresets == null)
            return false;

        if (TryApplyExactQuality())
            return true;

        if (TryApplyMediumQuality())
            return true;

        if (TryApplyFirstAvailableQuality())
            return true;

        return false;
    }

    private bool TryApplyExactQuality()
    {
        if (QualitySettingsPresets.TryGetValue(Quality, out var settings))
        {
            CurrentQualitySettings = settings;
            return true;
        }
        return false;
    }

    private bool TryApplyMediumQuality()
    {
        if (QualitySettingsPresets.TryGetValue(RenderQuality.Medium, out var mediumSettings))
        {
            CurrentQualitySettings = mediumSettings;
            LogDebug($"Quality '{Quality}' not found in QualitySettingsPresets for {GetType().Name}. " +
                     $"Fell back to Medium.");
            return true;
        }
        return false;
    }

    private bool TryApplyFirstAvailableQuality()
    {
        if (QualitySettingsPresets.Count > 0)
        {
            var firstPreset = QualitySettingsPresets.First();
            CurrentQualitySettings = firstPreset.Value;
            LogDebug($"Quality '{Quality}' and Medium not found for {GetType().Name}. " +
                     $"Fell back to first: {firstPreset.Key}.");
            return true;
        }
        return false;
    }
}