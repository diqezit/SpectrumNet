#nullable enable

using static System.MathF;
using static SpectrumNet.SN.Visualization.Renderers.GlitchRenderer.Constants;

namespace SpectrumNet.SN.Visualization.Renderers;

public sealed class GlitchRenderer : EffectSpectrumRenderer
{
    private const string LogPrefix = nameof(GlitchRenderer);

    private static readonly Lazy<GlitchRenderer> _instance =
        new(() => new GlitchRenderer());

    private GlitchRenderer() { }

    public static GlitchRenderer GetInstance() => _instance.Value;

    public static class Constants
    {
        public const float
            TIME_STEP = 0.016f,
            SPECTRUM_SCALE = 2.0f,
            SPECTRUM_CLAMP = 1.0f,
            SENSITIVITY = 3.0f,
            SMOOTHING_FACTOR = 0.2f,
            GLITCH_THRESHOLD = 0.5f,
            MAX_GLITCH_OFFSET = 30f,
            MIN_GLITCH_DURATION = 0.05f,
            MAX_GLITCH_DURATION = 0.3f,
            GLITCH_XOFFSET_UPDATE_CHANCE = 0.2f,
            GLITCH_NEW_SEGMENT_CHANCE_SCALE = 0.4f,
            XOFFSET_RANDOM_RANGE = 2f,
            SCANLINE_SPEED = 1.5f,
            SCANLINE_HEIGHT = 3f,
            RGB_SPLIT_MAX = 10f,
            RGB_SPLIT_THRESHOLD_SCALE = 0.5f,
            NOISE_THRESHOLD_SCALE = 0.3f;

        public const int
            PROCESSED_SPECTRUM_SIZE = 128,
            BAND_COUNT = 3,
            MAX_GLITCH_SEGMENTS = 10,
            GLITCH_SEGMENT_BASE_HEIGHT = 20,
            GLITCH_SEGMENT_RANDOM_HEIGHT_RANGE = 50,
            NOISE_ALPHA_SCALE = 150,
            NOISE_COUNT_SCALE = 1000,
            NOISE_MIN_PIXEL_SIZE = 1,
            NOISE_MAX_PIXEL_SIZE_RANDOM_ADD = 3;

        public const byte SCANLINE_ALPHA = 40;

        public static readonly Dictionary<RenderQuality, QualitySettings> QualityPresets = new()
        {
            [RenderQuality.Low] = new(
                RgbSplitIntensity: 0.5f,
                NoiseIntensity: 0.5f
            ),
            [RenderQuality.Medium] = new(
                RgbSplitIntensity: 0.75f,
                NoiseIntensity: 0.75f
            ),
            [RenderQuality.High] = new(
                RgbSplitIntensity: 1.0f,
                NoiseIntensity: 1.0f
            )
        };

        public record QualitySettings(
            float RgbSplitIntensity,
            float NoiseIntensity
        );

        public record GlitchSegment(
            int Y,
            int Height,
            float XOffset,
            float Duration,
            bool IsActive
        );
    }

    private QualitySettings _currentSettings = QualityPresets[RenderQuality.Medium];
    private readonly SemaphoreSlim _glitchSemaphore = new(1, 1);
    private readonly Random _random = new();
    private readonly List<GlitchSegment> _glitchSegments = new();

    private SKBitmap? _bufferBitmap;
    private SKPaint? _bitmapPaint;
    private SKPaint? _redPaint;
    private SKPaint? _bluePaint;
    private SKPaint? _scanlinePaint;
    private SKPaint? _noisePaint;

    private float[] _glitchProcessedSpectrum = new float[PROCESSED_SPECTRUM_SIZE];
    private int _scanlinePosition;
    private float _animationTime;

    protected override void OnInitialize()
    {
        base.OnInitialize();
        InitializeResources();
        _logger.Log(LogLevel.Debug, LogPrefix, "Initialized");
    }

    protected override void OnQualitySettingsApplied()
    {
        _currentSettings = QualityPresets[Quality];
        UpdatePaintSettings();
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
            () => RenderGlitch(canvas, spectrum, info, paint),
            LogPrefix,
            "Error during rendering"
        );
    }

    private void RenderGlitch(
        SKCanvas canvas,
        float[] spectrum,
        SKImageInfo info,
        SKPaint paint)
    {
        UpdateState(spectrum, info);
        RenderWithOverlay(canvas, () => RenderFrame(canvas, info, paint));
    }

    private void InitializeResources()
    {
        _bitmapPaint = new SKPaint { IsAntialias = _useAntiAlias };
        _redPaint = CreateBlendPaint(SKColors.Red);
        _bluePaint = CreateBlendPaint(SKColors.Blue);
        _scanlinePaint = new SKPaint
        {
            Color = SKColors.White.WithAlpha(SCANLINE_ALPHA),
            Style = SKPaintStyle.Fill
        };
        _noisePaint = new SKPaint { Style = SKPaintStyle.Fill };
    }

    private void UpdatePaintSettings()
    {
        UpdateAntialiasing(_bitmapPaint);
        UpdateAntialiasing(_redPaint);
        UpdateAntialiasing(_bluePaint);
        UpdateAntialiasing(_scanlinePaint);
        UpdateAntialiasing(_noisePaint);
    }

    private void UpdateAntialiasing(SKPaint? paint)
    {
        if (paint != null)
            paint.IsAntialias = _useAntiAlias;
    }

    private void UpdateState(float[] spectrum, SKImageInfo info)
    {
        if (!_glitchSemaphore.Wait(0)) return;

        try
        {
            PrepareBuffer(info);
            ProcessSpectrumData(spectrum);
            UpdateGlitchEffects(info);
        }
        finally
        {
            _glitchSemaphore.Release();
        }
    }

    private void RenderFrame(
        SKCanvas canvas,
        SKImageInfo info,
        SKPaint paint)
    {
        if (_bufferBitmap == null || _bitmapPaint == null) return;

        PopulateBuffer(info, paint);
        RenderBufferToCanvas(canvas, info);
    }

    private void PrepareBuffer(SKImageInfo info)
    {
        if (_bufferBitmap?.Width != info.Width || _bufferBitmap?.Height != info.Height)
        {
            _bufferBitmap?.Dispose();
            _bufferBitmap = new SKBitmap(info.Width, info.Height);
        }
    }

    private void ProcessSpectrumData(float[] source)
    {
        ResampleSpectrum(source);
        ComputeBandActivity(source);
    }

    private void ResampleSpectrum(float[] source)
    {
        int halfLength = source.Length / 2;
        for (int i = 0; i < PROCESSED_SPECTRUM_SIZE; i++)
        {
            float idx = i * halfLength / (float)PROCESSED_SPECTRUM_SIZE;
            int baseIndex = (int)idx;
            float t = idx - baseIndex;

            _glitchProcessedSpectrum[i] = baseIndex < halfLength - 1
                ? Lerp(source[baseIndex], source[baseIndex + 1], t)
                : source[halfLength - 1];

            _glitchProcessedSpectrum[i] = MathF.Min(
                _glitchProcessedSpectrum[i] * SPECTRUM_SCALE,
                SPECTRUM_CLAMP
            );
        }
    }

    private void ComputeBandActivity(float[] source)
    {
        int bandSize = source.Length / (2 * BAND_COUNT);

        for (int band = 0; band < BAND_COUNT; band++)
        {
            _glitchProcessedSpectrum[band] = MathF.Min(
                CalculateSmoothedBand(source, band, bandSize),
                SPECTRUM_CLAMP
            );
        }
    }

    private float CalculateSmoothedBand(
        float[] source,
        int band,
        int bandSize)
    {
        int start = band * bandSize;
        int end = Min((band + 1) * bandSize, source.Length / 2);
        float sum = 0;

        for (int i = start; i < end; i++)
            sum += source[i];

        float avg = end > start ? sum / (end - start) : 0;
        float current = avg * SENSITIVITY;
        float prev = _glitchProcessedSpectrum[band];

        return prev == 0 ? current : prev + (current - prev) * SMOOTHING_FACTOR;
    }

    private void UpdateGlitchEffects(SKImageInfo info)
    {
        _animationTime += TIME_STEP;
        _scanlinePosition = (_scanlinePosition + (int)SCANLINE_SPEED) % info.Height;

        UpdateExistingSegments();
        TryAddNewSegment(info);
    }

    private void UpdateExistingSegments()
    {
        for (int i = _glitchSegments.Count - 1; i >= 0; i--)
        {
            var seg = _glitchSegments[i];
            float newDuration = seg.Duration - TIME_STEP;

            if (newDuration <= 0)
            {
                _glitchSegments.RemoveAt(i);
            }
            else
            {
                float newXOffset = seg.XOffset;
                if (_random.NextDouble() < GLITCH_XOFFSET_UPDATE_CHANCE)
                {
                    newXOffset = MAX_GLITCH_OFFSET *
                        ((float)_random.NextDouble() * XOFFSET_RANDOM_RANGE - 1f) *
                        _glitchProcessedSpectrum[0];
                }

                _glitchSegments[i] = seg with
                {
                    Duration = newDuration,
                    XOffset = newXOffset
                };
            }
        }
    }

    private void TryAddNewSegment(SKImageInfo info)
    {
        if (_glitchProcessedSpectrum[0] <= GLITCH_THRESHOLD ||
            _glitchSegments.Count >= MAX_GLITCH_SEGMENTS ||
            _random.NextDouble() >= _glitchProcessedSpectrum[0] * GLITCH_NEW_SEGMENT_CHANCE_SCALE)
        {
            return;
        }

        int height = GLITCH_SEGMENT_BASE_HEIGHT +
            (int)(_random.NextDouble() * GLITCH_SEGMENT_RANDOM_HEIGHT_RANGE);

        _glitchSegments.Add(new GlitchSegment(
            Y: _random.Next(0, info.Height - height),
            Height: height,
            XOffset: MAX_GLITCH_OFFSET *
                ((float)_random.NextDouble() * XOFFSET_RANDOM_RANGE - 1f) *
                _glitchProcessedSpectrum[0],
            Duration: MIN_GLITCH_DURATION +
                (float)_random.NextDouble() * (MAX_GLITCH_DURATION - MIN_GLITCH_DURATION),
            IsActive: true
        ));
    }

    private void PopulateBuffer(SKImageInfo info, SKPaint paint)
    {
        if (_bufferBitmap == null) return;

        using var bufCanvas = new SKCanvas(_bufferBitmap);
        bufCanvas.Clear(SKColors.Black);

        DrawBaseSpectrum(bufCanvas, info, paint);

        if (_useAdvancedEffects)
        {
            ApplyRgbSplit(bufCanvas, info, paint);
            ApplyDigitalNoise(bufCanvas);
        }

        DrawScanline(bufCanvas, info);
    }

    private void RenderBufferToCanvas(SKCanvas canvas, SKImageInfo info)
    {
        if (_bufferBitmap == null || _bitmapPaint == null) return;

        RenderBufferSegments(canvas, info);
        RenderActiveSegments(canvas, info);
    }

    private SKPaint CreateBlendPaint(SKColor baseColor)
    {
        return new()
        {
            Color = baseColor.WithAlpha(SCANLINE_ALPHA),
            BlendMode = SKBlendMode.SrcOver,
            IsAntialias = _useAntiAlias
        };
    }

    private void DrawBaseSpectrum(
        SKCanvas canvas,
        SKImageInfo info,
        SKPaint paint)
    {
        int count = Min(PROCESSED_SPECTRUM_SIZE, _glitchProcessedSpectrum.Length);
        float width = info.Width / (float)count;
        float midY = info.Height / 2f;

        for (int i = 0; i < count; i++)
        {
            float x = i * width;
            float h = _glitchProcessedSpectrum[i] * info.Height * 0.5f;

            canvas.DrawLine(x, midY - h / 2f, x, midY + h / 2f, paint);
        }
    }

    private void ApplyRgbSplit(
        SKCanvas canvas,
        SKImageInfo info,
        SKPaint paint)
    {
        if (_redPaint == null || _bluePaint == null) return;

        float intensity = _glitchProcessedSpectrum[2];
        if (intensity <= GLITCH_THRESHOLD * RGB_SPLIT_THRESHOLD_SCALE *
            _currentSettings.RgbSplitIntensity)
        {
            return;
        }

        float split = intensity * RGB_SPLIT_MAX * _currentSettings.RgbSplitIntensity;

        canvas.Save();
        canvas.Translate(-split, 0);
        DrawBaseSpectrum(canvas, info, _redPaint);
        canvas.Restore();

        canvas.Save();
        canvas.Translate(split, 0);
        DrawBaseSpectrum(canvas, info, _bluePaint);
        canvas.Restore();
    }

    private void DrawScanline(SKCanvas canvas, SKImageInfo info)
    {
        if (_scanlinePaint != null)
        {
            canvas.DrawRect(
                0,
                _scanlinePosition,
                info.Width,
                SCANLINE_HEIGHT,
                _scanlinePaint
            );
        }
    }

    private void ApplyDigitalNoise(SKCanvas canvas)
    {
        if (_noisePaint == null) return;

        float intensity = _glitchProcessedSpectrum[1];
        if (intensity <= GLITCH_THRESHOLD * NOISE_THRESHOLD_SCALE *
            _currentSettings.NoiseIntensity)
        {
            return;
        }

        _noisePaint.Color = SKColors.White.WithAlpha(
            (byte)(intensity * NOISE_ALPHA_SCALE * _currentSettings.NoiseIntensity)
        );

        int count = (int)(intensity * NOISE_COUNT_SCALE * _currentSettings.NoiseIntensity);
        var bounds = canvas.DeviceClipBounds;

        for (int i = 0; i < count; i++)
        {
            canvas.DrawRect(
                _random.Next(0, bounds.Width),
                _random.Next(0, bounds.Height),
                NOISE_MIN_PIXEL_SIZE + _random.Next(0, NOISE_MAX_PIXEL_SIZE_RANDOM_ADD),
                NOISE_MIN_PIXEL_SIZE + _random.Next(0, NOISE_MAX_PIXEL_SIZE_RANDOM_ADD),
                _noisePaint
            );
        }
    }

    private void RenderBufferSegments(
        SKCanvas canvas,
        SKImageInfo info)
    {
        if (_bufferBitmap == null || _bitmapPaint == null) return;

        int currentY = 0;
        var passiveSegments = _glitchSegments
            .Where(s => !s.IsActive)
            .OrderBy(s => s.Y)
            .ToList();

        foreach (var seg in passiveSegments)
        {
            if (currentY < seg.Y)
            {
                DrawBitmapSegment(
                    canvas,
                    _bufferBitmap,
                    new SKRect(0, currentY, info.Width, seg.Y),
                    new SKRect(0, currentY, info.Width, seg.Y)
                );
            }
            currentY = seg.Y + seg.Height;
        }

        if (currentY < info.Height)
        {
            DrawBitmapSegment(
                canvas,
                _bufferBitmap,
                new SKRect(0, currentY, info.Width, info.Height),
                new SKRect(0, currentY, info.Width, info.Height)
            );
        }
    }

    private void RenderActiveSegments(
        SKCanvas canvas,
        SKImageInfo info)
    {
        if (_bufferBitmap == null || _bitmapPaint == null) return;

        var activeSegments = _glitchSegments
            .Where(s => s.IsActive)
            .OrderBy(s => s.Y);

        foreach (var seg in activeSegments)
        {
            DrawBitmapSegment(
                canvas,
                _bufferBitmap,
                new SKRect(0, seg.Y, info.Width, seg.Y + seg.Height),
                new SKRect(
                    seg.XOffset,
                    seg.Y,
                    seg.XOffset + info.Width,
                    seg.Y + seg.Height
                )
            );
        }
    }

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
        base.OnDispose();

        _glitchSemaphore?.Dispose();
        _bufferBitmap?.Dispose();
        _bitmapPaint?.Dispose();
        _redPaint?.Dispose();
        _bluePaint?.Dispose();
        _scanlinePaint?.Dispose();
        _noisePaint?.Dispose();

        _logger.Log(LogLevel.Debug, LogPrefix, "Disposed");
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;
}