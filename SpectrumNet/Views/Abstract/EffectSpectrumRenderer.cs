using static System.MathF;

namespace SpectrumNet.Views.Abstract;

public abstract class EffectSpectrumRenderer : BaseSpectrumRenderer
{
    protected readonly ObjectPool<SKPath> _pathPool = new(() => new SKPath(), path => path.Reset(), 5);
    protected readonly ObjectPool<SKPaint> _paintPool = new(() => new SKPaint(), paint => paint.Reset(), 5);

    protected bool _isOverlayActive;
    protected float _time;
    protected DateTime _lastUpdateTime = Now;

    protected readonly object _renderLock = new();

    protected abstract void RenderEffect(
        SKCanvas canvas,
        float[] spectrum,
        SKImageInfo info,
        float barWidth,
        float barSpacing,
        int barCount,
        SKPaint paint);

    public override void Initialize()
    {
        Safe(
            () =>
            {
                base.Initialize();
                ApplyQualitySettings();
                OnInitialize();
                Log(LogLevel.Debug, $"{GetType().Name}", "Initialized");
            },
            new ErrorHandlingOptions
            {
                Source = $"{GetType().Name}.Initialize",
                ErrorMessage = "Failed to initialize renderer"
            });
    }

    protected virtual void OnInitialize() { }

    public override void Configure(
        bool isOverlayActive,
        RenderQuality quality = RenderQuality.Medium)
    {
        Safe(
            () =>
            {
                bool configChanged = _isOverlayActive != isOverlayActive || Quality != quality;

                base.Configure(isOverlayActive, quality);

                _isOverlayActive = isOverlayActive;
                Quality = quality;

                if (configChanged)
                {
                    OnConfigurationChanged();
                }
            },
            new ErrorHandlingOptions
            {
                Source = $"{GetType().Name}.Configure",
                ErrorMessage = "Failed to configure renderer"
            });
    }

    protected virtual void OnConfigurationChanged() { }

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
        var (isValid, processedSpectrum) = PrepareRender(canvas, spectrum, info, barCount, paint);
        if (!isValid)
        {
            drawPerformanceInfo?.Invoke(canvas!, info);
            return;
        }

        Safe(
            () =>
            {
                lock (_renderLock)
                {
                    DateTime now = Now;
                    float deltaTime = MathF.Max(0, (float)(now - _lastUpdateTime).TotalSeconds);
                    _lastUpdateTime = now;
                    _time += deltaTime;

                    BeforeRender(
                        canvas,
                        spectrum,
                        info,
                        barWidth,
                        barSpacing,
                        barCount,
                        paint);

                    RenderEffect(
                        canvas!,
                        processedSpectrum!,
                        info,
                        barWidth,
                        barSpacing,
                        processedSpectrum!.Length,
                        paint!);

                    AfterRender(
                        canvas!,
                        processedSpectrum!,
                        info);
                }
            },
            new ErrorHandlingOptions
            {
                Source = $"{GetType().Name}.Render",
                ErrorMessage = "Error during rendering"
            });

        drawPerformanceInfo?.Invoke(canvas!, info);
    }

    public override void Dispose()
    {
        if (!_disposed)
        {
            Safe(
                () =>
                {
                    OnDispose();
                    _pathPool?.Dispose();
                    _paintPool?.Dispose();
                    base.Dispose();
                },
                new ErrorHandlingOptions
                {
                    Source = $"{GetType().Name}.Dispose",
                    ErrorMessage = "Error during disposal"
                });
            _disposed = true;
            SuppressFinalize(this);
        }
    }

    protected virtual void OnDispose() { }

    protected virtual void BeforeRender(
        SKCanvas? canvas,
        float[]? spectrum,
        SKImageInfo info,
        float barWidth,
        float barSpacing,
        int barCount,
        SKPaint? paint)
    { }

    protected virtual void AfterRender(
        SKCanvas canvas,
        float[] processedSpectrum,
        SKImageInfo info)
    { }

    protected override void ApplyQualitySettings()
    {
        Safe(
            () =>
            {
                (_useAntiAlias, _useAdvancedEffects) = QualityBasedSettings();
                _samplingOptions = QualityBasedSamplingOptions();
                OnQualitySettingsApplied();
            },
            new ErrorHandlingOptions
            {
                Source = $"{GetType().Name}.ApplyQualitySettings",
                ErrorMessage = "Failed to apply quality settings"
            });
    }

    protected virtual void OnQualitySettingsApplied() { }

    protected override (bool useAntiAlias, bool useAdvancedEffects) QualityBasedSettings() => Quality switch
    {
        RenderQuality.Low => (false, false),
        RenderQuality.Medium => (true, true),
        RenderQuality.High => (true, true),
        _ => (true, true)
    };

    protected override SKSamplingOptions QualityBasedSamplingOptions() => Quality switch
    {
        RenderQuality.Low => new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None),
        RenderQuality.Medium => new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear),
        RenderQuality.High => new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear),
        _ => new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear)
    };

    protected SKPaint CreateStandardPaint(SKColor color, SKPaintStyle style = Fill)
    {
        var paint = _paintPool.Get();
        paint.Color = color;
        paint.Style = style;
        paint.IsAntialias = UseAntiAlias;
        return paint;
    }

    protected SKPaint CreateGlowPaint(SKColor color, float radius, byte alpha)
    {
        var paint = _paintPool.Get();
        paint.Color = color.WithAlpha(alpha);
        paint.IsAntialias = UseAntiAlias;
        paint.MaskFilter = SKMaskFilter.CreateBlur(Normal, radius);
        return paint;
    }

    protected void InvalidateCachedResources()
    {
        Safe(
            () =>
            {
                OnInvalidateCachedResources();
            },
            new ErrorHandlingOptions
            {
                Source = $"{GetType().Name}.InvalidateCachedResources",
                ErrorMessage = "Failed to invalidate cached resources"
            });
    }

    protected virtual void OnInvalidateCachedResources() { }
}