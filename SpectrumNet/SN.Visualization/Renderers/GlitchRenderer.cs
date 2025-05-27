#nullable enable

using static System.MathF;
using static SpectrumNet.SN.Visualization.Renderers.GlitchRenderer.Constants;

namespace SpectrumNet.SN.Visualization.Renderers;

public sealed class GlitchRenderer() : EffectSpectrumRenderer
{
    private static readonly Lazy<GlitchRenderer> _instance =
        new(() => new GlitchRenderer());

    public static GlitchRenderer GetInstance() => _instance.Value;

    public static class Constants
    {
        public const float
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
                NoiseIntensity: 0.5f),
            [RenderQuality.Medium] = new(
                RgbSplitIntensity: 0.75f,
                NoiseIntensity: 0.75f),
            [RenderQuality.High] = new(
                RgbSplitIntensity: 1.0f,
                NoiseIntensity: 1.0f)
        };

        public record QualitySettings(
            float RgbSplitIntensity,
            float NoiseIntensity);

        public record GlitchSegment(
            int Y,
            int Height,
            float XOffset,
            float Duration,
            bool IsActive);
    }

    private QualitySettings _currentSettings = QualityPresets[RenderQuality.Medium];
    private readonly SemaphoreSlim _glitchSemaphore = new(1, 1);
    private readonly Random _random = new();
    private readonly List<GlitchSegment> _glitchSegments = [];

    private SKBitmap? _bufferBitmap;
    private readonly float[] _glitchProcessedSpectrum = new float[PROCESSED_SPECTRUM_SIZE];
    private int _scanlinePosition;

    protected override void OnQualitySettingsApplied()
    {
        _currentSettings = QualityPresets[Quality];
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
        UpdateState(spectrum, info);
        RenderFrame(canvas, info, paint);
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
                SPECTRUM_CLAMP);
        }
    }

    private void ComputeBandActivity(float[] source)
    {
        int bandSize = source.Length / (2 * BAND_COUNT);

        for (int band = 0; band < BAND_COUNT; band++)
        {
            int start = band * bandSize;
            int end = Min((band + 1) * bandSize, source.Length / 2);

            float avg = GetAverageInRange(source, start, end) * SENSITIVITY;
            float prev = _glitchProcessedSpectrum[band];

            _glitchProcessedSpectrum[band] = MathF.Min(
                prev == 0 ? avg : Lerp(prev, avg, SMOOTHING_FACTOR),
                SPECTRUM_CLAMP);
        }
    }

    private void UpdateGlitchEffects(SKImageInfo info)
    {
        _scanlinePosition = (_scanlinePosition +
            (int)(SCANLINE_SPEED * GetAnimationDeltaTime() * 60)) % info.Height;

        UpdateExistingSegments();
        TryAddNewSegment(info);
    }

    private void UpdateExistingSegments()
    {
        for (int i = _glitchSegments.Count - 1; i >= 0; i--)
        {
            var seg = _glitchSegments[i];
            float newDuration = seg.Duration - GetAnimationDeltaTime();

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
            return;

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
            IsActive: true));
    }

    private void RenderFrame(SKCanvas canvas, SKImageInfo info, SKPaint paint)
    {
        if (_bufferBitmap == null) return;

        PopulateBuffer(info, paint);
        RenderBufferToCanvas(canvas, info);
    }

    private void PopulateBuffer(SKImageInfo info, SKPaint paint)
    {
        if (_bufferBitmap == null) return;

        using var bufCanvas = new SKCanvas(_bufferBitmap);
        bufCanvas.Clear(SKColors.Black);

        DrawBaseSpectrum(bufCanvas, info, paint);

        if (UseAdvancedEffects)
        {
            ApplyRgbSplit(bufCanvas, info, paint);
            ApplyDigitalNoise(bufCanvas);
        }

        DrawScanline(bufCanvas, info);
    }

    private void DrawBaseSpectrum(SKCanvas canvas, SKImageInfo info, SKPaint paint)
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

    private void ApplyRgbSplit(SKCanvas canvas, SKImageInfo info, SKPaint paint)
    {
        float intensity = _glitchProcessedSpectrum[2];
        if (intensity <= GLITCH_THRESHOLD * RGB_SPLIT_THRESHOLD_SCALE *
            _currentSettings.RgbSplitIntensity)
            return;

        float split = intensity * RGB_SPLIT_MAX * _currentSettings.RgbSplitIntensity;

        var redPaint = CreateStandardPaint(SKColors.Red.WithAlpha(SCANLINE_ALPHA));
        redPaint.BlendMode = SKBlendMode.SrcOver;

        canvas.Save();
        canvas.Translate(-split, 0);
        DrawBaseSpectrum(canvas, info, redPaint);
        canvas.Restore();
        ReturnPaint(redPaint);

        var bluePaint = CreateStandardPaint(SKColors.Blue.WithAlpha(SCANLINE_ALPHA));
        bluePaint.BlendMode = SKBlendMode.SrcOver;

        canvas.Save();
        canvas.Translate(split, 0);
        DrawBaseSpectrum(canvas, info, bluePaint);
        canvas.Restore();
        ReturnPaint(bluePaint);
    }

    private void DrawScanline(SKCanvas canvas, SKImageInfo info)
    {
        var scanlinePaint = CreateStandardPaint(SKColors.White.WithAlpha(SCANLINE_ALPHA));
        canvas.DrawRect(0, _scanlinePosition, info.Width, SCANLINE_HEIGHT, scanlinePaint);
        ReturnPaint(scanlinePaint);
    }

    private void ApplyDigitalNoise(SKCanvas canvas)
    {
        float intensity = _glitchProcessedSpectrum[1];
        if (intensity <= GLITCH_THRESHOLD * NOISE_THRESHOLD_SCALE *
            _currentSettings.NoiseIntensity)
            return;

        var noisePaint = CreateStandardPaint(
            SKColors.White.WithAlpha(
                (byte)(intensity * NOISE_ALPHA_SCALE * _currentSettings.NoiseIntensity)));

        int count = (int)(intensity * NOISE_COUNT_SCALE * _currentSettings.NoiseIntensity);
        var bounds = canvas.DeviceClipBounds;

        for (int i = 0; i < count; i++)
        {
            canvas.DrawRect(
                _random.Next(0, bounds.Width),
                _random.Next(0, bounds.Height),
                NOISE_MIN_PIXEL_SIZE + _random.Next(0, NOISE_MAX_PIXEL_SIZE_RANDOM_ADD),
                NOISE_MIN_PIXEL_SIZE + _random.Next(0, NOISE_MAX_PIXEL_SIZE_RANDOM_ADD),
                noisePaint);
        }

        ReturnPaint(noisePaint);
    }

    private void RenderBufferToCanvas(SKCanvas canvas, SKImageInfo info)
    {
        if (_bufferBitmap == null) return;

        var bitmapPaint = CreateStandardPaint(SKColors.White);

        RenderBufferSegments(canvas, info, bitmapPaint);
        RenderActiveSegments(canvas, info, bitmapPaint);

        ReturnPaint(bitmapPaint);
    }

    private void RenderBufferSegments(
        SKCanvas canvas,
        SKImageInfo info,
        SKPaint bitmapPaint)
    {
        if (_bufferBitmap == null) return;

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
                    new SKRect(0, currentY, info.Width, seg.Y),
                    bitmapPaint);
            }
            currentY = seg.Y + seg.Height;
        }

        if (currentY < info.Height)
        {
            DrawBitmapSegment(
                canvas,
                _bufferBitmap,
                new SKRect(0, currentY, info.Width, info.Height),
                new SKRect(0, currentY, info.Width, info.Height),
                bitmapPaint);
        }
    }

    private void RenderActiveSegments(
        SKCanvas canvas,
        SKImageInfo info,
        SKPaint bitmapPaint)
    {
        if (_bufferBitmap == null) return;

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
                    seg.Y + seg.Height),
                bitmapPaint);
        }
    }

    private static void DrawBitmapSegment(
        SKCanvas canvas,
        SKBitmap bitmap,
        SKRect src,
        SKRect dst,
        SKPaint paint)
    {
        canvas.DrawBitmap(bitmap, src, dst, paint);
    }

    protected override void CleanupUnusedResources()
    {
        if (_glitchSegments.Count > MAX_GLITCH_SEGMENTS * 2)
            _glitchSegments.RemoveAll(s => s.Duration <= 0);

        if (_bufferBitmap != null &&
            (_bufferBitmap.Width > 2048 || _bufferBitmap.Height > 2048))
        {
            _bufferBitmap.Dispose();
            _bufferBitmap = null;
        }
    }

    protected override void OnDispose()
    {
        _glitchSemaphore?.Dispose();
        _bufferBitmap?.Dispose();
        _glitchSegments.Clear();
        base.OnDispose();
    }
}