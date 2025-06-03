#nullable enable

using System.Buffers;

namespace SpectrumNet.SN.Visualization.Renderers;

public sealed class WaterfallRenderer : EffectSpectrumRenderer<WaterfallRenderer.QualitySettings>
{
    private static readonly Lazy<WaterfallRenderer> _instance =
        new(() => new WaterfallRenderer());

    public static WaterfallRenderer GetInstance() => _instance.Value;

    private const float MIN_SIGNAL = 1e-6f,
        DETAIL_SCALE_FACTOR = 0.8f,
        ZOOM_THRESHOLD = 2.0f,
        BLUE_THRESHOLD = 0.25f,
        CYAN_THRESHOLD = 0.5f,
        YELLOW_THRESHOLD = 0.75f,
        PARAMETER_CHANGE_THRESHOLD = 0.1f;

    private const int DEFAULT_BUFFER_HEIGHT = 256,
        OVERLAY_BUFFER_HEIGHT = 128,
        DEFAULT_SPECTRUM_WIDTH = 1024,
        MIN_BAR_COUNT = 10,
        COLOR_PALETTE_SIZE = 256,
        GRID_DIVISIONS = 10;

    private const byte GRID_LINE_ALPHA = 40;

    private static readonly int[] _colorPalette = InitColorPalette();

    private float[][]? _spectrogramBuffer;
    private SKBitmap? _waterfallBitmap;
    private int _bufferHead;
    private float _lastBarWidth;
    private int _lastBarCount;
    private float _lastBarSpacing;

    public sealed class QualitySettings
    {
        public bool UseGridLines { get; init; }
        public bool UseGridMarkers { get; init; }
        public float GridLineWidth { get; init; }
        public float MarkerFontSize { get; init; }
    }

    protected override IReadOnlyDictionary<RenderQuality, QualitySettings>
        QualitySettingsPresets
    { get; } = new Dictionary<RenderQuality, QualitySettings>
    {
        [RenderQuality.Low] = new()
        {
            UseGridLines = false,
            UseGridMarkers = false,
            GridLineWidth = 1f,
            MarkerFontSize = 10f
        },
        [RenderQuality.Medium] = new()
        {
            UseGridLines = true,
            UseGridMarkers = false,
            GridLineWidth = 1f,
            MarkerFontSize = 10f
        },
        [RenderQuality.High] = new()
        {
            UseGridLines = true,
            UseGridMarkers = true,
            GridLineWidth = 1f,
            MarkerFontSize = 12f
        }
    };

    protected override void RenderEffect(
        SKCanvas canvas,
        float[] spectrum,
        SKImageInfo info,
        RenderParameters renderParams,
        SKPaint passedInPaint)
    {
        if (CurrentQualitySettings == null || renderParams.EffectiveBarCount <= 0)
            return;

        var (isValid, processedSpectrum) = ProcessSpectrum(
            spectrum,
            renderParams.EffectiveBarCount,
            applyTemporalSmoothing: true);

        if (!isValid || processedSpectrum == null)
            return;

        var waterfallData = CalculateWaterfallData(
            processedSpectrum,
            info,
            renderParams);

        if (!ValidateWaterfallData(waterfallData))
            return;

        RenderWaterfallVisualization(
            canvas,
            waterfallData,
            renderParams,
            passedInPaint);
    }

    private WaterfallData CalculateWaterfallData(
        float[] spectrum,
        SKImageInfo info,
        RenderParameters renderParams)
    {
        CheckAndUpdateParameters(renderParams);
        UpdateSpectrogramBuffer(spectrum);

        var destRect = CalculateDestRect(
            info,
            renderParams.BarWidth,
            renderParams.BarSpacing,
            renderParams.EffectiveBarCount);

        return new WaterfallData(
            DestRect: destRect,
            BufferHeight: _spectrogramBuffer?.Length ?? 0,
            SpectrumWidth: _spectrogramBuffer?[0].Length ?? 0);
    }

    private bool ValidateWaterfallData(WaterfallData data)
    {
        return data.BufferHeight > 0 &&
               data.SpectrumWidth > 0 &&
               data.DestRect.Width > 0 &&
               data.DestRect.Height > 0 &&
               _spectrogramBuffer != null;
    }

    private void RenderWaterfallVisualization(
        SKCanvas canvas,
        WaterfallData data,
        RenderParameters renderParams,
        SKPaint basePaint)
    {
        var settings = CurrentQualitySettings!;

        UpdateWaterfallBitmap(data.SpectrumWidth, data.BufferHeight);

        if (_waterfallBitmap == null)
            return;

        UpdateBitmapPixels();

        RenderWithOverlay(canvas, () =>
        {
            DrawWaterfallBitmap(canvas, data.DestRect);

            if (renderParams.BarWidth > ZOOM_THRESHOLD && UseAdvancedEffects)
                DrawGridOverlay(canvas, data.DestRect, renderParams, settings);
        });
    }

    private void CheckAndUpdateParameters(RenderParameters renderParams)
    {
        bool changed = MathF.Abs(_lastBarWidth - renderParams.BarWidth) > PARAMETER_CHANGE_THRESHOLD ||
                      MathF.Abs(_lastBarSpacing - renderParams.BarSpacing) > PARAMETER_CHANGE_THRESHOLD ||
                      _lastBarCount != renderParams.EffectiveBarCount;

        if (changed)
        {
            _lastBarWidth = renderParams.BarWidth;
            _lastBarSpacing = renderParams.BarSpacing;
            _lastBarCount = renderParams.EffectiveBarCount;
            ResizeBufferIfNeeded(renderParams.EffectiveBarCount);
        }
    }

    private void ResizeBufferIfNeeded(int barCount)
    {
        int optimalWidth = CalculateOptimalSpectrumWidth(barCount);
        int bufferHeight = IsOverlayActive ? OVERLAY_BUFFER_HEIGHT : DEFAULT_BUFFER_HEIGHT;

        if (_spectrogramBuffer == null ||
            _spectrogramBuffer.Length != bufferHeight ||
            _spectrogramBuffer[0].Length != optimalWidth)
        {
            InitializeSpectrogramBuffer(bufferHeight, optimalWidth);
        }
    }

    private void InitializeSpectrogramBuffer(int bufferHeight, int spectrumWidth)
    {
        _spectrogramBuffer = new float[bufferHeight][];

        for (int i = 0; i < bufferHeight; i++)
        {
            _spectrogramBuffer[i] = new float[spectrumWidth];
            Array.Fill(_spectrogramBuffer[i], MIN_SIGNAL);
        }

        _bufferHead = 0;
    }

    private void UpdateSpectrogramBuffer(float[] spectrum)
    {
        if (_spectrogramBuffer == null) return;

        int spectrumWidth = _spectrogramBuffer[0].Length;

        if (spectrum.Length == spectrumWidth)
        {
            Array.Copy(spectrum, _spectrogramBuffer[_bufferHead], spectrumWidth);
        }
        else
        {
            ResampleSpectrum(spectrum, _spectrogramBuffer[_bufferHead]);
        }

        _bufferHead = (_bufferHead + 1) % _spectrogramBuffer.Length;
    }

    private static void ResampleSpectrum(float[] source, float[] destination)
    {
        float ratio = (float)source.Length / destination.Length;

        for (int i = 0; i < destination.Length; i++)
        {
            if (ratio > 1.0f)
            {
                int startIdx = (int)(i * ratio);
                int endIdx = Math.Min((int)((i + 1) * ratio), source.Length);
                destination[i] = CalculateSegmentMax(source, startIdx, endIdx);
            }
            else
            {
                float exactIdx = i * ratio;
                int idx1 = (int)exactIdx;
                int idx2 = Math.Min(idx1 + 1, source.Length - 1);
                float frac = exactIdx - idx1;
                destination[i] = source[idx1] * (1 - frac) + source[idx2] * frac;
            }
        }
    }

    private static float CalculateSegmentMax(float[] array, int start, int end)
    {
        float max = MIN_SIGNAL;
        for (int i = start; i < end; i++)
            max = MathF.Max(max, array[i]);
        return max;
    }

    private void UpdateWaterfallBitmap(int width, int height)
    {
        if (_waterfallBitmap == null ||
            _waterfallBitmap.Width != width ||
            _waterfallBitmap.Height != height)
        {
            _waterfallBitmap?.Dispose();
            _waterfallBitmap = new SKBitmap(width, height);
        }
    }

    private void UpdateBitmapPixels()
    {
        if (_waterfallBitmap == null || _spectrogramBuffer == null)
            return;

        nint pixelsPtr = _waterfallBitmap.GetPixels();
        if (pixelsPtr == IntPtr.Zero)
            return;

        int width = _waterfallBitmap.Width;
        int height = _waterfallBitmap.Height;
        int totalPixels = width * height;

        int[] pixels = ArrayPool<int>.Shared.Rent(totalPixels);

        try
        {
            FillPixelArray(pixels, width, height);
            Marshal.Copy(pixels, 0, pixelsPtr, totalPixels);
        }
        finally
        {
            ArrayPool<int>.Shared.Return(pixels);
        }
    }

    private void FillPixelArray(int[] pixels, int width, int height)
    {
        for (int y = 0; y < height; y++)
        {
            int bufferIndex = (_bufferHead + 1 + y) % height;
            int rowOffset = y * width;

            for (int x = 0; x < width; x++)
            {
                float value = _spectrogramBuffer![bufferIndex][x];
                float normalized = Clamp(value, 0f, 1f);
                int paletteIndex = (int)(normalized * (COLOR_PALETTE_SIZE - 1));
                pixels[rowOffset + x] = _colorPalette[paletteIndex];
            }
        }
    }

    private void DrawWaterfallBitmap(SKCanvas canvas, SKRect destRect)
    {
        if (_waterfallBitmap == null || !IsAreaVisible(canvas, destRect))
            return;

        var paint = CreatePaint(SKColors.White, SKPaintStyle.Fill);

        try
        {
            canvas.DrawBitmap(_waterfallBitmap, destRect, paint);
        }
        finally
        {
            ReturnPaint(paint);
        }
    }

    private void DrawGridOverlay(
        SKCanvas canvas,
        SKRect destRect,
        RenderParameters renderParams,
        QualitySettings settings)
    {
        if (settings.UseGridLines)
            DrawGridLines(canvas, destRect, renderParams, settings);

        if (settings.UseGridMarkers && renderParams.BarWidth > ZOOM_THRESHOLD * 1.5f)
            DrawGridMarkers(canvas, destRect, renderParams, settings);
    }

    private void DrawGridLines(
        SKCanvas canvas,
        SKRect destRect,
        RenderParameters renderParams,
        QualitySettings settings)
    {
        var paint = CreatePaint(
            new SKColor(255, 255, 255, GRID_LINE_ALPHA),
            SKPaintStyle.Stroke);
        paint.StrokeWidth = settings.GridLineWidth;

        try
        {
            int gridStep = Math.Max(1, renderParams.EffectiveBarCount / GRID_DIVISIONS);

            RenderPath(canvas, path =>
            {
                for (int i = 0; i < renderParams.EffectiveBarCount; i += gridStep)
                {
                    float x = destRect.Left + i * (renderParams.BarWidth + renderParams.BarSpacing)
                        + renderParams.BarWidth / 2;
                    path.MoveTo(x, destRect.Top);
                    path.LineTo(x, destRect.Bottom);
                }
            }, paint);
        }
        finally
        {
            ReturnPaint(paint);
        }
    }

    private void DrawGridMarkers(
        SKCanvas canvas,
        SKRect destRect,
        RenderParameters renderParams,
        QualitySettings settings)
    {
        using var font = new SKFont { Size = settings.MarkerFontSize };
        var textPaint = CreatePaint(SKColors.White, SKPaintStyle.Fill);

        try
        {
            int gridStep = Math.Max(1, renderParams.EffectiveBarCount / GRID_DIVISIONS);

            for (int i = 0; i < renderParams.EffectiveBarCount; i += gridStep * 2)
            {
                float x = destRect.Left + i * (renderParams.BarWidth + renderParams.BarSpacing)
                    + renderParams.BarWidth / 2;
                string marker = $"{i}";
                float textWidth = font.MeasureText(marker, textPaint);
                canvas.DrawText(
                    marker,
                    x - textWidth / 2,
                    destRect.Bottom - 5,
                    font,
                    textPaint);
            }
        }
        finally
        {
            ReturnPaint(textPaint);
        }
    }

    private static SKRect CalculateDestRect(
        SKImageInfo info,
        float barWidth,
        float barSpacing,
        int barCount)
    {
        float totalBarsWidth = barCount * barWidth + (barCount - 1) * barSpacing;
        float startX = (info.Width - totalBarsWidth) / 2;

        return new SKRect(
            left: MathF.Max(startX, 0),
            top: 0,
            right: MathF.Min(startX + totalBarsWidth, info.Width),
            bottom: info.Height);
    }

    private static int CalculateOptimalSpectrumWidth(int barCount)
    {
        int baseWidth = Math.Max(barCount, MIN_BAR_COUNT);
        int width = 1;

        while (width < baseWidth)
            width *= 2;

        return Math.Max(width, DEFAULT_SPECTRUM_WIDTH / 4);
    }

    private static int[] InitColorPalette()
    {
        int[] palette = new int[COLOR_PALETTE_SIZE];

        for (int i = 0; i < COLOR_PALETTE_SIZE; i++)
        {
            float normalized = i / (float)(COLOR_PALETTE_SIZE - 1);
            SKColor color = GetSpectrogramColor(normalized);
            palette[i] = color.Alpha << 24 | color.Red << 16 |
                        color.Green << 8 | color.Blue;
        }

        return palette;
    }

    private static SKColor GetSpectrogramColor(float normalized)
    {
        normalized = Clamp(normalized, 0f, 1f);

        return normalized switch
        {
            < BLUE_THRESHOLD => GetBlueRangeColor(normalized),
            < CYAN_THRESHOLD => GetCyanRangeColor(normalized),
            < YELLOW_THRESHOLD => GetYellowRangeColor(normalized),
            _ => GetRedRangeColor(normalized)
        };
    }

    private static SKColor GetBlueRangeColor(float normalized)
    {
        float t = normalized / BLUE_THRESHOLD;
        return new SKColor(0, (byte)(t * 50), (byte)(50 + t * 205), 255);
    }

    private static SKColor GetCyanRangeColor(float normalized)
    {
        float t = (normalized - BLUE_THRESHOLD) / (CYAN_THRESHOLD - BLUE_THRESHOLD);
        return new SKColor(0, (byte)(50 + t * 205), 255, 255);
    }

    private static SKColor GetYellowRangeColor(float normalized)
    {
        float t = (normalized - CYAN_THRESHOLD) / (YELLOW_THRESHOLD - CYAN_THRESHOLD);
        return new SKColor((byte)(t * 255), 255, (byte)(255 - t * 255), 255);
    }

    private static SKColor GetRedRangeColor(float normalized)
    {
        float t = (normalized - YELLOW_THRESHOLD) / (1f - YELLOW_THRESHOLD);
        return new SKColor(255, (byte)(255 - t * 255), 0, 255);
    }

    protected override int GetMaxBarsForQuality() => Quality switch
    {
        RenderQuality.Low => 128,
        RenderQuality.Medium => 256,
        RenderQuality.High => 512,
        _ => 256
    };

    protected override void OnQualitySettingsApplied()
    {
        base.OnQualitySettingsApplied();

        float smoothingFactor = Quality switch
        {
            RenderQuality.Low => 0.4f,
            RenderQuality.Medium => 0.3f,
            RenderQuality.High => 0.25f,
            _ => 0.3f
        };

        if (IsOverlayActive)
            smoothingFactor *= 1.2f;

        SetProcessingSmoothingFactor(smoothingFactor);

        int bufferHeight = IsOverlayActive ? OVERLAY_BUFFER_HEIGHT : DEFAULT_BUFFER_HEIGHT;
        int width = _spectrogramBuffer?[0].Length ?? DEFAULT_SPECTRUM_WIDTH;
        InitializeSpectrogramBuffer(bufferHeight, width);

        RequestRedraw();
    }

    protected override void OnDispose()
    {
        _waterfallBitmap?.Dispose();
        _waterfallBitmap = null;
        _spectrogramBuffer = null;
        _bufferHead = 0;
        _lastBarWidth = 0;
        _lastBarCount = 0;
        _lastBarSpacing = 0;
        base.OnDispose();
    }

    private record WaterfallData(
        SKRect DestRect,
        int BufferHeight,
        int SpectrumWidth);
}