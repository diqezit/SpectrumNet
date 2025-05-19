#nullable enable

using static SpectrumNet.Views.Renderers.GlitchRenderer.Constants;
using static SpectrumNet.Views.Renderers.GlitchRenderer.Constants.Quality;

namespace SpectrumNet.Views.Renderers;

public sealed class GlitchRenderer : EffectSpectrumRenderer
{
    private static readonly Lazy<GlitchRenderer> _instance = new(() => new GlitchRenderer());
    private const string LogPrefix = nameof(GlitchRenderer);

    private GlitchRenderer() { }

    public static GlitchRenderer GetInstance() => _instance.Value;

    public record Constants
    {
        public const float
            TIME_STEP = 0.016f;

        public const int
            PROCESSED_SPECTRUM_SIZE = 128,
            BAND_COUNT = 3;

        public const float
            SPECTRUM_SCALE = 2.0f,
            SPECTRUM_CLAMP = 1.0f,
            SENSITIVITY = 3.0f,
            SMOOTHING_FACTOR = 0.2f,
            SPECTRUM_HALF_FACTOR = 2f,
            SPECTRUM_HEIGHT_HALF_SCALE = 0.5f;

        public const int
            SPECTRUM_HALF_INDEX_DIVISOR = 2,
            SPECTRUM_MIDPOINT_DIVISOR = 2;

        public const int
            MAX_GLITCH_SEGMENTS = 10;

        public const float
            GLITCH_THRESHOLD = 0.5f,
            MAX_GLITCH_OFFSET = 30f,
            MIN_GLITCH_DURATION = 0.05f,
            MAX_GLITCH_DURATION = 0.3f,
            GLITCH_XOFFSET_UPDATE_CHANCE = 0.2f,
            GLITCH_NEW_SEGMENT_CHANCE_SCALE = 0.4f,
            XOFFSET_RANDOM_RANGE_SCALE = 2f,
            XOFFSET_RANDOM_RANGE_OFFSET = 1f;

        public const int
            GLITCH_SEGMENT_BASE_HEIGHT = 20,
            GLITCH_SEGMENT_RANDOM_HEIGHT_RANGE = 50;

        public const float
            SCANLINE_SPEED = 1.5f,
            SCANLINE_HEIGHT = 3f;

        public const byte
            SCANLINE_ALPHA = 40;

        public const float
            RGB_SPLIT_MAX = 10f,
            RGB_SPLIT_THRESHOLD_SCALE = 0.5f,
            NOISE_THRESHOLD_SCALE = 0.3f;

        public const int
            NOISE_ALPHA_SCALE = 150,
            NOISE_COUNT_SCALE = 1000,
            NOISE_MIN_PIXEL_SIZE = 1,
            NOISE_MAX_PIXEL_SIZE_RANDOM_ADD = 3;

        public static class Quality
        {
            public const bool
                LOW_USE_ANTIALIASING = false,
                MEDIUM_USE_ANTIALIASING = true,
                HIGH_USE_ANTIALIASING = true;

            public const bool
                LOW_USE_ADVANCED_EFFECTS = false,
                MEDIUM_USE_ADVANCED_EFFECTS = true,
                HIGH_USE_ADVANCED_EFFECTS = true;

            public const float
                LOW_RGB_SPLIT_INTENSITY = 0.5f,
                MEDIUM_RGB_SPLIT_INTENSITY = 0.75f,
                HIGH_RGB_SPLIT_INTENSITY = 1.0f;

            public const float
                LOW_NOISE_INTENSITY = 0.5f,
                MEDIUM_NOISE_INTENSITY = 0.75f,
                HIGH_NOISE_INTENSITY = 1.0f;
        }
    }

    private struct GlitchSegment
    {
        public int Y;
        public int Height;
        public float XOffset;
        public float Duration;
        public bool IsActive;
    }

    private readonly SemaphoreSlim _glitchSemaphore = new(1, 1);
    private readonly Random _random = new();

    private SKBitmap? _bufferBitmap;
    private SKPaint? _bitmapPaint;
    private SKPaint? _redPaint;
    private SKPaint? _bluePaint;
    private SKPaint? _scanlinePaint;
    private SKPaint? _noisePaint;

    private float _rgbSplitIntensity;
    private float _noiseIntensity;

    private float[]? _glitchProcessedSpectrum;
    private List<GlitchSegment>? _glitchSegments;
    private int _scanlinePosition;
    private float _animationTime; // Используем другое имя во избежание конфликта с базовым классом

    protected override void OnInitialize()
    {
        base.OnInitialize();
        InitializeResources();
        _logger.Debug(LogPrefix, "Initialized");
    }

    public override void SetOverlayTransparency(float level) =>
        _logger.Safe(() => HandleSetOverlayTransparency(level),
                     LogPrefix,
                     "Error setting overlay transparency");

    private void HandleSetOverlayTransparency(float level)
    {
        if (Math.Abs(_overlayAlphaFactor - level) < float.Epsilon)
            return;

        _overlayAlphaFactor = level;
        _overlayStateChangeRequested = true;
        _overlayStateChanged = true;
    }

    private void InitializeResources() =>
        _logger.Safe(() => HandleInitializeResources(),
                     LogPrefix,
                     "Error initializing resources");

    private void HandleInitializeResources()
    {
        _bitmapPaint = new SKPaint { IsAntialias = UseAntiAlias };
        _redPaint = CreateBlendPaint(SKColors.Red);
        _bluePaint = CreateBlendPaint(SKColors.Blue);

        _scanlinePaint = new SKPaint
        {
            Color = SKColors.White.WithAlpha(SCANLINE_ALPHA),
            Style = SKPaintStyle.Fill
        };

        _noisePaint = new SKPaint { Style = SKPaintStyle.Fill };
        _glitchSegments = [];
    }

    protected override void OnConfigurationChanged()
    {
        _logger.Info(LogPrefix,
            $"Configuration changed. New Quality: {Quality}, Overlay: {_isOverlayActive}");

        if (_isOverlayActive)
        {
            _overlayAlphaFactor = 0.75f;
            _overlayStateChanged = true;
            _overlayStateChangeRequested = true;
        }
    }

    protected override void OnQualitySettingsApplied() =>
        _logger.Safe(() => HandleQualitySettingsApplied(),
                     LogPrefix,
                     "Error applying quality settings");

    private void HandleQualitySettingsApplied()
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

        UpdatePaintSettings();

        _logger.Debug(LogPrefix,
            $"Quality settings applied. Quality: {Quality}, " +
            $"AntiAlias: {UseAntiAlias}, AdvancedEffects: {UseAdvancedEffects}, " +
            $"RgbSplitIntensity: {_rgbSplitIntensity}, NoiseIntensity: {_noiseIntensity}");
    }

    private void LowQualitySettings()
    {
        _rgbSplitIntensity = LOW_RGB_SPLIT_INTENSITY;
        _noiseIntensity = LOW_NOISE_INTENSITY;
        // Базовые настройки установлены в базовом классе
    }

    private void MediumQualitySettings()
    {
        _rgbSplitIntensity = MEDIUM_RGB_SPLIT_INTENSITY;
        _noiseIntensity = MEDIUM_NOISE_INTENSITY;
        // Базовые настройки установлены в базовом классе
    }

    private void HighQualitySettings()
    {
        _rgbSplitIntensity = HIGH_RGB_SPLIT_INTENSITY;
        _noiseIntensity = HIGH_NOISE_INTENSITY;
        // Базовые настройки установлены в базовом классе
    }

    private void UpdatePaintSettings() =>
        _logger.Safe(() => HandleUpdatePaintSettings(),
                     LogPrefix,
                     "Error updating paint settings");

    private void HandleUpdatePaintSettings()
    {
        UpdateAntialiasing(_bitmapPaint);
        UpdateAntialiasing(_redPaint);
        UpdateAntialiasing(_bluePaint);
        UpdateAntialiasing(_scanlinePaint);
        UpdateAntialiasing(_noisePaint);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void UpdateAntialiasing(SKPaint? paint)
    {
        if (paint != null)
            paint.IsAntialias = UseAntiAlias;
    }

    protected override void RenderEffect(
        SKCanvas canvas,
        float[] spectrum,
        SKImageInfo info,
        float barWidth,
        float barSpacing,
        int barCount,
        SKPaint paint) =>
        _logger.Safe(() => HandleRenderEffect(canvas, spectrum, info, barWidth, barSpacing, barCount, paint),
                     LogPrefix,
                     "Error during rendering");

    private void HandleRenderEffect(
        SKCanvas canvas,
        float[] spectrum,
        SKImageInfo info,
        float barWidth,
        float barSpacing,
        int barCount,
        SKPaint paint)
    {
        if (_overlayStateChangeRequested)
        {
            _overlayStateChangeRequested = false;
            _overlayStateChanged = true;
        }

        UpdateState(spectrum, info);
        RenderWithOverlay(canvas, () => RenderFrame(canvas, info, paint));

        if (_overlayStateChanged)
        {
            _overlayStateChanged = false;
        }
    }

    private void UpdateState(float[] spectrum, SKImageInfo info) =>
        _logger.Safe(() => HandleUpdateState(spectrum, info),
                     LogPrefix,
                     "Error updating glitch state");

    private void HandleUpdateState(float[] spectrum, SKImageInfo info)
    {
        bool semaphoreAcquired = false;
        try
        {
            semaphoreAcquired = _glitchSemaphore.Wait(0);
            if (semaphoreAcquired)
            {
                PrepareBuffer(info);
                ProcessSpectrumData(spectrum);
                UpdateGlitchEffects(info);
            }
        }
        finally
        {
            if (semaphoreAcquired)
                _glitchSemaphore.Release();
        }
    }

    private void RenderFrame(SKCanvas canvas, SKImageInfo info, SKPaint paint) =>
        _logger.Safe(() => HandleRenderFrame(canvas, info, paint),
                     LogPrefix,
                     "Error rendering glitch frame");

    private void HandleRenderFrame(SKCanvas canvas, SKImageInfo info, SKPaint paint)
    {
        if (!CanRenderFinal(info))
            return;

        PopulateBuffer(info, paint);
        RenderBufferToCanvas(canvas, info);
    }

    private void PrepareBuffer(SKImageInfo info) =>
        _logger.Safe(() => HandlePrepareBuffer(info),
                     LogPrefix,
                     "Error preparing buffer");

    private void HandlePrepareBuffer(SKImageInfo info)
    {
        if (_bufferBitmap == null
            || _bufferBitmap.Width != info.Width
            || _bufferBitmap.Height != info.Height)
        {
            _bufferBitmap?.Dispose();
            _bufferBitmap = new SKBitmap(info.Width, info.Height);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private void ProcessSpectrumData(float[] source)
    {
        ResampleSpectrum(source);
        ComputeBandActivity(source);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private void ResampleSpectrum(float[] source)
    {
        int size = PROCESSED_SPECTRUM_SIZE;
        if (_glitchProcessedSpectrum == null || _glitchProcessedSpectrum.Length < size)
            _glitchProcessedSpectrum = new float[size];

        for (int i = 0; i < size; i++)
        {
            float idx = i * source.Length / (SPECTRUM_HALF_FACTOR * size);
            int baseIndex = (int)idx;
            float t = idx - baseIndex;

            if (baseIndex < source.Length / SPECTRUM_HALF_INDEX_DIVISOR - 1)
            {
                _glitchProcessedSpectrum[i] = Lerp(source[baseIndex], source[baseIndex + 1], t);
            }
            else
            {
                _glitchProcessedSpectrum[i] = source[source.Length / SPECTRUM_HALF_INDEX_DIVISOR - 1];
            }

            _glitchProcessedSpectrum[i] = MathF.Min(_glitchProcessedSpectrum[i] * SPECTRUM_SCALE, SPECTRUM_CLAMP);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private void ComputeBandActivity(float[] source)
    {
        if (_glitchProcessedSpectrum == null)
            return;

        int bandSize = source.Length / (SPECTRUM_HALF_INDEX_DIVISOR * BAND_COUNT);

        for (int band = 0; band < BAND_COUNT; band++)
        {
            float smoothedBandValue = CalculateSmoothedBand(source, band, bandSize);
            _glitchProcessedSpectrum[band] = MathF.Min(smoothedBandValue, SPECTRUM_CLAMP);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private float CalculateSmoothedBand(float[] source, int band, int bandSize)
    {
        int start = band * bandSize;
        int end = (int)MathF.Min((band + 1) * bandSize, source.Length / SPECTRUM_HALF_INDEX_DIVISOR);
        float sum = 0;

        for (int i = start; i < end; i++)
            sum += source[i];

        int actualBandSize = end - start;
        float avg = actualBandSize > 0 ? sum / actualBandSize : 0;

        if (_glitchProcessedSpectrum == null || band >= _glitchProcessedSpectrum.Length)
        {
            return avg * SENSITIVITY;
        }

        float prev = _glitchProcessedSpectrum[band];

        return prev == 0
             ? avg * SENSITIVITY
             : prev + (avg * SENSITIVITY - prev) * SMOOTHING_FACTOR;
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private void UpdateGlitchEffects(SKImageInfo info)
    {
        AdvanceTimeAndScanline(info.Height);
        UpdateExistingSegments();
        TryAddNewSegment(info);
    }

    private void AdvanceTimeAndScanline(int height) =>
        _logger.Safe(() => HandleAdvanceTimeAndScanline(height),
                     LogPrefix,
                     "Error advancing time and scanline");

    private void HandleAdvanceTimeAndScanline(int height)
    {
        _animationTime += TIME_STEP;
        _scanlinePosition = (_scanlinePosition + (int)SCANLINE_SPEED) % height;
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private void UpdateExistingSegments()
    {
        if (_glitchSegments == null || _glitchProcessedSpectrum == null)
            return;

        for (int i = _glitchSegments.Count - 1; i >= 0; i--)
        {
            var seg = _glitchSegments[i];
            seg.Duration -= TIME_STEP;

            if (seg.Duration <= 0)
            {
                _glitchSegments.RemoveAt(i);
            }
            else
            {
                if (_random.NextDouble() < GLITCH_XOFFSET_UPDATE_CHANCE)
                {
                    float randomFactor = ((float)_random.NextDouble()
                        * XOFFSET_RANDOM_RANGE_SCALE
                        - XOFFSET_RANDOM_RANGE_OFFSET);

                    seg.XOffset = MAX_GLITCH_OFFSET
                        * randomFactor
                        * _glitchProcessedSpectrum[0];
                }

                _glitchSegments[i] = seg;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private void TryAddNewSegment(SKImageInfo info)
    {
        if (_glitchSegments == null || _glitchProcessedSpectrum == null)
            return;

        if (_glitchProcessedSpectrum[0] <= GLITCH_THRESHOLD ||
            _glitchSegments.Count >= MAX_GLITCH_SEGMENTS ||
            _random.NextDouble() >= _glitchProcessedSpectrum[0] * GLITCH_NEW_SEGMENT_CHANCE_SCALE)
        {
            return;
        }

        int height = (int)(GLITCH_SEGMENT_BASE_HEIGHT
            + _random.NextDouble() * GLITCH_SEGMENT_RANDOM_HEIGHT_RANGE);

        int y = _random.Next(0, info.Height - height);

        float duration = MIN_GLITCH_DURATION
            + (float)_random.NextDouble() * (MAX_GLITCH_DURATION - MIN_GLITCH_DURATION);

        float randomFactor = ((float)_random.NextDouble()
            * XOFFSET_RANDOM_RANGE_SCALE
            - XOFFSET_RANDOM_RANGE_OFFSET);

        float offset = MAX_GLITCH_OFFSET
            * randomFactor
            * _glitchProcessedSpectrum[0];

        _glitchSegments.Add(new GlitchSegment
        {
            Y = y,
            Height = height,
            Duration = duration,
            XOffset = offset,
            IsActive = true
        });
    }

    private void PopulateBuffer(SKImageInfo info, SKPaint paint) =>
        _logger.Safe(() => HandlePopulateBuffer(info, paint),
                     LogPrefix,
                     "Error populating buffer");

    private void HandlePopulateBuffer(SKImageInfo info, SKPaint paint)
    {
        if (_bufferBitmap == null) return;

        using var bufCanvas = new SKCanvas(_bufferBitmap);
        bufCanvas.Clear(SKColors.Black);

        DrawBaseSpectrumToBuffer(bufCanvas, info, paint);

        if (UseAdvancedEffects)
        {
            ApplyAdvancedEffects(bufCanvas, info, paint);
        }

        DrawScanlineToBuffer(bufCanvas, info);
    }

    private void ApplyAdvancedEffects(
        SKCanvas bufCanvas,
        SKImageInfo info,
        SKPaint paint) =>
        _logger.Safe(() => HandleApplyAdvancedEffects(bufCanvas, info, paint),
                     LogPrefix,
                     "Error applying advanced effects");

    private void HandleApplyAdvancedEffects(
        SKCanvas bufCanvas,
        SKImageInfo info,
        SKPaint paint)
    {
        if (_glitchProcessedSpectrum == null) return;

        ApplyRgbSplitToBuffer(bufCanvas, info, paint);
        ApplyDigitalNoiseToBuffer(bufCanvas);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool CanRenderFinal(SKImageInfo info) =>
        _bufferBitmap != null
        && _bitmapPaint != null
        && _glitchSegments != null
        && _glitchProcessedSpectrum != null
        && _bufferBitmap.Width == info.Width
        && _bufferBitmap.Height == info.Height;

    private void RenderBufferToCanvas(SKCanvas canvas, SKImageInfo info) =>
        _logger.Safe(() => HandleRenderBufferToCanvas(canvas, info),
                     LogPrefix,
                     "Error rendering buffer to canvas");

    private void HandleRenderBufferToCanvas(SKCanvas canvas, SKImageInfo info)
    {
        RenderBufferSegments(canvas, info);
        RenderActiveSegments(canvas, info);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private SKPaint CreateBlendPaint(SKColor baseColor)
    {
        return new()
        {
            Color = baseColor.WithAlpha(SCANLINE_ALPHA),
            BlendMode = SKBlendMode.SrcOver,
            IsAntialias = UseAntiAlias
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private void DrawBaseSpectrumToBuffer(
        SKCanvas bufCanvas,
        SKImageInfo info,
        SKPaint paint)
    {
        if (_glitchProcessedSpectrum == null) return;

        int count = (int)MathF.Min(PROCESSED_SPECTRUM_SIZE, _glitchProcessedSpectrum.Length);
        float width = info.Width / (float)count;

        for (int i = 0; i < count; i++)
        {
            float amp = _glitchProcessedSpectrum[i];
            float x = i * width;
            float h = amp * info.Height * SPECTRUM_HEIGHT_HALF_SCALE;

            bufCanvas.DrawLine(
                x,
                info.Height / SPECTRUM_MIDPOINT_DIVISOR - h / SPECTRUM_MIDPOINT_DIVISOR,
                x,
                info.Height / SPECTRUM_MIDPOINT_DIVISOR + h / SPECTRUM_MIDPOINT_DIVISOR,
                paint);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private void ApplyRgbSplitToBuffer(SKCanvas bufCanvas, SKImageInfo info, SKPaint paint)
    {
        if (_glitchProcessedSpectrum == null || _redPaint == null || _bluePaint == null) return;

        if (_glitchProcessedSpectrum[2] <= GLITCH_THRESHOLD * RGB_SPLIT_THRESHOLD_SCALE * _rgbSplitIntensity)
            return;

        float split = _glitchProcessedSpectrum[2] * RGB_SPLIT_MAX * _rgbSplitIntensity;

        foreach (var (dx, dy, paintToUse) in new[] { (-split, 0f, _redPaint), (split, 0f, _bluePaint) })
        {
            bufCanvas.Save();
            bufCanvas.Translate(dx, dy);
            DrawBaseSpectrumToBuffer(bufCanvas, info, paintToUse);
            bufCanvas.Restore();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private void DrawScanlineToBuffer(SKCanvas bufCanvas, SKImageInfo info)
    {
        if (_scanlinePaint == null) return;
        bufCanvas.DrawRect(0, _scanlinePosition, info.Width, SCANLINE_HEIGHT, _scanlinePaint);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private void ApplyDigitalNoiseToBuffer(SKCanvas bufCanvas)
    {
        if (_glitchProcessedSpectrum == null || _noisePaint == null) return;

        if (_glitchProcessedSpectrum[1] <= GLITCH_THRESHOLD * NOISE_THRESHOLD_SCALE * _noiseIntensity)
            return;

        _noisePaint.Color = SKColors.White.WithAlpha((byte)(_glitchProcessedSpectrum[1] * NOISE_ALPHA_SCALE * _noiseIntensity));
        int count = (int)(_glitchProcessedSpectrum[1] * NOISE_COUNT_SCALE * _noiseIntensity);

        for (int i = 0; i < count; i++)
        {
            bufCanvas.DrawRect(
                _random.Next(0, bufCanvas.DeviceClipBounds.Width),
                _random.Next(0, bufCanvas.DeviceClipBounds.Height),
                NOISE_MIN_PIXEL_SIZE + _random.Next(0, NOISE_MAX_PIXEL_SIZE_RANDOM_ADD),
                NOISE_MIN_PIXEL_SIZE + _random.Next(0, NOISE_MAX_PIXEL_SIZE_RANDOM_ADD),
                _noisePaint);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private void RenderBufferSegments(
        SKCanvas canvas,
        SKImageInfo info)
    {
        if (_glitchSegments == null || _bufferBitmap == null || _bitmapPaint == null)
            return;

        int currentY = 0;
        var passiveSegments = _glitchSegments.Where(s => !s.IsActive).OrderBy(s => s.Y).ToList();

        foreach (var seg in passiveSegments)
        {
            if (currentY < seg.Y)
            {
                DrawBitmapSegment(
                    canvas,
                    _bufferBitmap,
                    new SKRect(0, currentY, info.Width, seg.Y),
                    new SKRect(0, currentY, info.Width, seg.Y));
            }
            currentY = seg.Y + seg.Height;
        }

        if (currentY < info.Height)
        {
            DrawBitmapSegment(
                canvas,
                _bufferBitmap,
                new SKRect(0, currentY, info.Width, info.Height),
                new SKRect(0, currentY, info.Width, info.Height));
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private void RenderActiveSegments(
        SKCanvas canvas,
        SKImageInfo info)
    {
        if (_glitchSegments == null || _bufferBitmap == null || _bitmapPaint == null)
            return;

        var activeSegments = _glitchSegments.Where(s => s.IsActive).OrderBy(s => s.Y).ToList();

        foreach (var seg in activeSegments)
        {
            DrawBitmapSegment(
                canvas,
                _bufferBitmap,
                new SKRect(0, seg.Y, info.Width, seg.Y + seg.Height),
                new SKRect(seg.XOffset, seg.Y, seg.XOffset + info.Width, seg.Y + seg.Height));
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void DrawBitmapSegment(
        SKCanvas canvas,
        SKBitmap bitmap,
        SKRect src,
        SKRect dst)
    {
        if (_bitmapPaint != null)
        {
            canvas.DrawBitmap(bitmap, src, dst, _bitmapPaint);
        }
    }

    protected override void OnDispose()
    {
        _logger.Safe(() => CleanupResources(),
                     LogPrefix,
                     "Error disposing resources");

        base.OnDispose();
        _logger.Debug(LogPrefix, "Disposed");
    }

    private void CleanupResources()
    {
        _glitchSemaphore?.Dispose();

        _bufferBitmap?.Dispose();
        _bitmapPaint?.Dispose();
        _redPaint?.Dispose();
        _bluePaint?.Dispose();
        _scanlinePaint?.Dispose();
        _noisePaint?.Dispose();

        _bufferBitmap = null;
        _bitmapPaint = _redPaint = _bluePaint = _scanlinePaint = _noisePaint = null;
        _glitchSegments = null;
        _glitchProcessedSpectrum = null;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float Lerp(float a, float b, float t) => a + (b - a) * t;
}