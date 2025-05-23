#nullable enable

using static System.MathF;
using static SpectrumNet.SN.Visualization.Renderers.WaterfallRenderer.Constants;

namespace SpectrumNet.SN.Visualization.Renderers;

public sealed class WaterfallRenderer : EffectSpectrumRenderer
{
    private const string LogPrefix = nameof(WaterfallRenderer);

    private static readonly Lazy<WaterfallRenderer> _instance =
        new(() => new WaterfallRenderer());

    private WaterfallRenderer() { }

    public static WaterfallRenderer GetInstance() => _instance.Value;

    public static class Constants
    {
        public const float
            MIN_SIGNAL = 1e-6f,
            DETAIL_SCALE_FACTOR = 0.8f,
            ZOOM_THRESHOLD = 2.0f,
            BLUE_THRESHOLD = 0.25f,
            CYAN_THRESHOLD = 0.5f,
            YELLOW_THRESHOLD = 0.75f,
            PARAMETER_CHANGE_THRESHOLD = 0.1f,
            COLOR_ENHANCEMENT_FACTOR = 1.0f;

        public const int
            DEFAULT_BUFFER_HEIGHT = 256,
            OVERLAY_BUFFER_HEIGHT = 128,
            DEFAULT_SPECTRUM_WIDTH = 1024,
            MIN_BAR_COUNT = 10,
            COLOR_PALETTE_SIZE = 256,
            LOW_BAR_COUNT_THRESHOLD = 64,
            HIGH_BAR_COUNT_THRESHOLD = 256,
            SEMAPHORE_TIMEOUT_MS = 5,
            PARALLEL_THRESHOLD = 32,
            GRID_DIVISIONS = 10;

        public const byte GRID_LINE_ALPHA = 40;

        public static readonly Dictionary<RenderQuality, QualitySettings> QualityPresets = new()
        {
            [RenderQuality.Low] = new(
                UseGridLines: false,
                UseGridMarkers: false,
                GridLineWidth: 1f,
                MarkerFontSize: 10f
            ),
            [RenderQuality.Medium] = new(
                UseGridLines: true,
                UseGridMarkers: false,
                GridLineWidth: 1f,
                MarkerFontSize: 10f
            ),
            [RenderQuality.High] = new(
                UseGridLines: true,
                UseGridMarkers: true,
                GridLineWidth: 1f,
                MarkerFontSize: 12f
            )
        };

        public record QualitySettings(
            bool UseGridLines,
            bool UseGridMarkers,
            float GridLineWidth,
            float MarkerFontSize
        );
    }

    private static readonly int[] _colorPalette = InitColorPalette();

    private readonly SemaphoreSlim _renderSemaphore = new(1, 1);
    private readonly ObjectPool<float[]> _spectrumPool = new(
        () => new float[DEFAULT_SPECTRUM_WIDTH],
        array => Array.Clear(array, 0, array.Length),
        initialCount: 2
    );

    private float[]? _currentSpectrum;
    private float[][]? _spectrogramBuffer;
    private SKBitmap? _waterfallBitmap;
    private int _bufferHead;
    private bool _needsBufferResize;
    private float _lastBarWidth;
    private int _lastBarCount;
    private float _lastBarSpacing;
    private QualitySettings _currentSettings = QualityPresets[RenderQuality.Medium];

    protected override void OnInitialize()
    {
        base.OnInitialize();
        int bufferHeight = _isOverlayActive ?
            OVERLAY_BUFFER_HEIGHT : DEFAULT_BUFFER_HEIGHT;
        InitializeSpectrogramBuffer(bufferHeight, DEFAULT_SPECTRUM_WIDTH);
        _logger.Log(LogLevel.Debug, LogPrefix, "Initialized");
    }

    protected override void OnQualitySettingsApplied()
    {
        _currentSettings = QualityPresets[Quality];
        _logger.Log(LogLevel.Debug, LogPrefix, $"Quality changed to {Quality}");
    }

    protected override void OnConfigurationChanged()
    {
        if (_spectrogramBuffer != null)
        {
            ResizeBufferForOverlayMode(_isOverlayActive);
        }
        _logger.Log(LogLevel.Information, LogPrefix,
            $"Configuration changed. Quality: {Quality}, Overlay: {_isOverlayActive}");
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
            () => RenderWaterfall(canvas, spectrum, info, barWidth, barSpacing, barCount),
            LogPrefix,
            "Error during rendering"
        );
    }

    private void RenderWaterfall(
        SKCanvas canvas,
        float[] spectrum,
        SKImageInfo info,
        float barWidth,
        float barSpacing,
        int barCount)
    {
        bool parametersChanged = CheckParametersChanged(barWidth, barSpacing, barCount);
        UpdateState(spectrum, parametersChanged, barCount, barWidth);
        RenderWithOverlay(canvas, () =>
            RenderFrame(canvas, info, barWidth, barSpacing, barCount));
    }

    private bool CheckParametersChanged(float barWidth, float barSpacing, int barCount)
    {
        bool changed = MathF.Abs(_lastBarWidth - barWidth) > PARAMETER_CHANGE_THRESHOLD ||
                      MathF.Abs(_lastBarSpacing - barSpacing) > PARAMETER_CHANGE_THRESHOLD ||
                      _lastBarCount != barCount;

        if (changed)
        {
            _lastBarWidth = barWidth;
            _lastBarSpacing = barSpacing;
            _lastBarCount = barCount;
            _needsBufferResize = true;
        }

        return changed;
    }

    private void UpdateState(
        float[] spectrum,
        bool parametersChanged,
        int barCount,
        float barWidth)
    {
        bool acquired = false;
        try
        {
            acquired = _renderSemaphore.Wait(SEMAPHORE_TIMEOUT_MS);
            if (acquired)
            {
                ProcessSpectrumData(spectrum, parametersChanged, barCount, barWidth);
            }
        }
        finally
        {
            if (acquired)
                _renderSemaphore.Release();
        }
    }

    private void ProcessSpectrumData(
        float[] spectrum,
        bool parametersChanged,
        int barCount,
        float barWidth)
    {
        float[]? adjustedSpectrum = spectrum;

        if (spectrum != null && parametersChanged)
        {
            adjustedSpectrum = AdjustSpectrumResolution(spectrum, barCount, barWidth);
        }

        UpdateSpectrogramBuffer(adjustedSpectrum);

        if (_needsBufferResize && parametersChanged)
        {
            ResizeBufferForBarParameters(barCount);
            _needsBufferResize = false;
        }
    }

    private void RenderFrame(
        SKCanvas canvas,
        SKImageInfo info,
        float barWidth,
        float barSpacing,
        int barCount)
    {
        if (_spectrogramBuffer == null)
        {
            _logger.Log(LogLevel.Warning, LogPrefix, "Spectrogram buffer is null");
            return;
        }

        int bufferHeight = _spectrogramBuffer.Length;
        int spectrumWidth = _spectrogramBuffer[0].Length;

        UpdateWaterfallBitmap(spectrumWidth, bufferHeight);
        UpdateBitmapPixels(_waterfallBitmap!, _spectrogramBuffer, _bufferHead,
                          spectrumWidth, bufferHeight);
        DrawBitmapToCanvas(canvas, info, barWidth, barSpacing, barCount);

        if (barWidth > ZOOM_THRESHOLD && _useAdvancedEffects)
        {
            SKRect destRect = CalculateDestRect(info, barWidth, barSpacing, barCount);
            ApplyZoomEnhancement(canvas, destRect, barWidth, barCount, barSpacing);
        }
    }

    private float[] AdjustSpectrumResolution(
        float[] spectrum,
        int barCount,
        float barWidth)
    {
        if (spectrum == null || barCount <= 0)
            return spectrum ?? [];

        if (spectrum.Length == barCount)
            return spectrum;

        float[] result = GetSpectrumBuffer(barCount);
        float detailLevel = MathF.Max(0.1f, MathF.Min(1.0f, barWidth * DETAIL_SCALE_FACTOR));
        float ratio = (float)spectrum.Length / barCount;

        if (ratio > 1.0f)
        {
            ProcessSpectrumDownsampling(spectrum, result, barCount, ratio, detailLevel);
        }
        else
        {
            ProcessSpectrumUpsampling(spectrum, result, barCount, ratio);
        }

        return result;
    }

    private float[] GetSpectrumBuffer(int requiredSize)
    {
        float[] buffer = _spectrumPool.Get();
        if (buffer.Length < requiredSize)
        {
            _spectrumPool.Return(buffer);
            buffer = new float[requiredSize];
        }
        return buffer;
    }

    private static void ProcessSpectrumDownsampling(
        float[] spectrum,
        float[] result,
        int barCount,
        float ratio,
        float detailLevel)
    {
        Parallel.For(0, barCount, i =>
        {
            int startIdx = (int)(i * ratio);
            int endIdx = Min((int)((i + 1) * ratio), spectrum.Length);

            if (endIdx <= startIdx) return;

            var (sum, peak) = CalculateSegmentStats(spectrum, startIdx, endIdx);
            int count = endIdx - startIdx;
            result[i] = sum / count * (1 - detailLevel) + peak * detailLevel;
        });
    }

    private static (float sum, float peak) CalculateSegmentStats(
        float[] spectrum,
        int startIdx,
        int endIdx)
    {
        float sum = 0;
        float peak = 0;
        int count = endIdx - startIdx;

        if (IsHardwareAccelerated && count >= Vector<float>.Count)
        {
            ProcessVectorizedSegment(spectrum, startIdx, endIdx, ref sum, ref peak);
        }
        else
        {
            ProcessSequentialSegment(spectrum, startIdx, endIdx, ref sum, ref peak);
        }

        return (sum, peak);
    }

    private static void ProcessVectorizedSegment(
        float[] spectrum,
        int startIdx,
        int endIdx,
        ref float sum,
        ref float peak)
    {
        int vectorSize = Vector<float>.Count;
        int vectorCount = (endIdx - startIdx) / vectorSize;

        for (int v = 0; v < vectorCount; v++)
        {
            Vector<float> values = new(spectrum, startIdx + v * vectorSize);
            sum += VectorSum(values);
            peak = MathF.Max(peak, VectorMax(values));
        }

        for (int j = startIdx + vectorCount * vectorSize; j < endIdx; j++)
        {
            sum += spectrum[j];
            peak = MathF.Max(peak, spectrum[j]);
        }
    }

    private static void ProcessSequentialSegment(
        float[] spectrum,
        int startIdx,
        int endIdx,
        ref float sum,
        ref float peak)
    {
        for (int j = startIdx; j < endIdx; j++)
        {
            sum += spectrum[j];
            peak = MathF.Max(peak, spectrum[j]);
        }
    }

    private static void ProcessSpectrumUpsampling(
        float[] spectrum,
        float[] result,
        int barCount,
        float ratio)
    {
        for (int i = 0; i < barCount; i++)
        {
            float exactIdx = i * ratio;
            int idx1 = (int)exactIdx;
            int idx2 = Min(idx1 + 1, spectrum.Length - 1);
            float frac = exactIdx - idx1;

            result[i] = spectrum[idx1] * (1 - frac) + spectrum[idx2] * frac;
        }
    }

    private void UpdateSpectrogramBuffer(float[]? spectrum)
    {
        if (_spectrogramBuffer == null) return;

        int bufferHeight = _spectrogramBuffer.Length;
        int spectrumWidth = _spectrogramBuffer[0].Length;

        if (spectrum == null)
        {
            Array.Fill(_spectrogramBuffer[_bufferHead], MIN_SIGNAL);
        }
        else
        {
            UpdateCurrentSpectrum(spectrum);
            UpdateBufferFromSpectrum(spectrum, spectrumWidth);
        }

        _bufferHead = (_bufferHead + 1) % bufferHeight;
    }

    private void UpdateCurrentSpectrum(float[] spectrum)
    {
        if (_currentSpectrum == null || _currentSpectrum.Length != spectrum.Length)
            _currentSpectrum = new float[spectrum.Length];

        Array.Copy(spectrum, _currentSpectrum, spectrum.Length);
    }

    private void UpdateBufferFromSpectrum(float[] spectrum, int spectrumWidth)
    {
        if (spectrum.Length == spectrumWidth)
        {
            Array.Copy(spectrum, _spectrogramBuffer![_bufferHead], spectrumWidth);
        }
        else if (spectrum.Length > spectrumWidth)
        {
            ProcessSpectrumDownscaling(spectrum, spectrumWidth);
        }
        else
        {
            ProcessSpectrumUpscaling(spectrum, spectrumWidth);
        }
    }

    private void ProcessSpectrumDownscaling(float[] spectrum, int spectrumWidth)
    {
        if (_spectrogramBuffer == null) return;

        float ratio = (float)spectrum.Length / spectrumWidth;

        if (IsHardwareAccelerated && spectrum.Length >= Vector<float>.Count)
        {
            DownscaleSpectrumParallel(spectrum, spectrumWidth, ratio);
        }
        else
        {
            DownscaleSpectrumSequential(spectrum, spectrumWidth, ratio);
        }
    }

    private void DownscaleSpectrumParallel(
        float[] spectrum,
        int spectrumWidth,
        float ratio)
    {
        Parallel.For(0, spectrumWidth, i =>
        {
            int startIdx = (int)(i * ratio);
            int endIdx = Min((int)((i + 1) * ratio), spectrum.Length);
            _spectrogramBuffer![_bufferHead][i] =
                CalculateSegmentMax(spectrum, startIdx, endIdx);
        });
    }

    private void DownscaleSpectrumSequential(
        float[] spectrum,
        int spectrumWidth,
        float ratio)
    {
        for (int i = 0; i < spectrumWidth; i++)
        {
            int startIdx = (int)(i * ratio);
            int endIdx = Min((int)((i + 1) * ratio), spectrum.Length);
            _spectrogramBuffer![_bufferHead][i] =
                CalculateSegmentMax(spectrum, startIdx, endIdx);
        }
    }

    private static float CalculateSegmentMax(float[] spectrum, int startIdx, int endIdx)
    {
        if (endIdx - startIdx >= Vector<float>.Count && IsHardwareAccelerated)
        {
            return CalculateSegmentMaxVectorized(spectrum, startIdx, endIdx);
        }
        else
        {
            return CalculateSegmentMaxSequential(spectrum, startIdx, endIdx);
        }
    }

    private static float CalculateSegmentMaxVectorized(
        float[] spectrum,
        int startIdx,
        int endIdx)
    {
        int vectorSize = Vector<float>.Count;
        int vectorizedLength = (endIdx - startIdx) / vectorSize * vectorSize;
        float maxVal = MIN_SIGNAL;

        for (int j = startIdx; j < startIdx + vectorizedLength; j += vectorSize)
        {
            Vector<float> values = new(spectrum, j);
            maxVal = MathF.Max(maxVal, VectorMax(values));
        }

        for (int j = startIdx + vectorizedLength; j < endIdx; j++)
            maxVal = MathF.Max(maxVal, spectrum[j]);

        return maxVal;
    }

    private static float CalculateSegmentMaxSequential(
        float[] spectrum,
        int startIdx,
        int endIdx)
    {
        float maxVal = MIN_SIGNAL;
        for (int j = startIdx; j < endIdx; j++)
            maxVal = MathF.Max(maxVal, spectrum[j]);
        return maxVal;
    }

    private void ProcessSpectrumUpscaling(float[] spectrum, int spectrumWidth)
    {
        if (_spectrogramBuffer == null) return;

        float ratio = (float)spectrum.Length / spectrumWidth;

        for (int i = 0; i < spectrumWidth; i++)
        {
            float exactIdx = i * ratio;
            int idx1 = (int)exactIdx;
            int idx2 = Min(idx1 + 1, spectrum.Length - 1);
            float frac = exactIdx - idx1;

            _spectrogramBuffer[_bufferHead][i] =
                spectrum[idx1] * (1 - frac) + spectrum[idx2] * frac;
        }
    }

    private void ResizeBufferForOverlayMode(bool isOverlayActive)
    {
        int bufferHeight = isOverlayActive ?
            OVERLAY_BUFFER_HEIGHT :
            DEFAULT_BUFFER_HEIGHT;

        int currentWidth = _spectrogramBuffer![0].Length;
        InitializeSpectrogramBuffer(bufferHeight, currentWidth);
    }

    private void ResizeBufferForBarParameters(int barCount)
    {
        if (_spectrogramBuffer == null) return;

        int bufferHeight = _spectrogramBuffer.Length;
        int newWidth = CalculateOptimalSpectrumWidth(barCount);

        if (_spectrogramBuffer[0].Length != newWidth)
        {
            InitializeSpectrogramBuffer(bufferHeight, newWidth);
        }
    }

    private void InitializeSpectrogramBuffer(int bufferHeight, int spectrumWidth)
    {
        if (bufferHeight <= 0 || spectrumWidth <= 0)
            throw new ArgumentException("Buffer dimensions must be positive");

        if (_spectrogramBuffer == null ||
            _spectrogramBuffer.Length != bufferHeight ||
            _spectrogramBuffer[0].Length != spectrumWidth)
        {
            ReleaseExistingBuffer();
            AllocateNewBuffer(bufferHeight, spectrumWidth);
            _bufferHead = 0;
        }
    }

    private void ReleaseExistingBuffer()
    {
        if (_spectrogramBuffer != null)
        {
            for (int i = 0; i < _spectrogramBuffer.Length; i++)
                _spectrogramBuffer[i] = null!;
        }
    }

    private void AllocateNewBuffer(int bufferHeight, int spectrumWidth)
    {
        _spectrogramBuffer = new float[bufferHeight][];

        if (bufferHeight > PARALLEL_THRESHOLD)
        {
            Parallel.For(0, bufferHeight, i =>
            {
                _spectrogramBuffer![i] = new float[spectrumWidth];
                Array.Fill(_spectrogramBuffer[i], MIN_SIGNAL);
            });
        }
        else
        {
            for (int i = 0; i < bufferHeight; i++)
            {
                _spectrogramBuffer![i] = new float[spectrumWidth];
                Array.Fill(_spectrogramBuffer[i], MIN_SIGNAL);
            }
        }
    }

    private void UpdateWaterfallBitmap(int spectrumWidth, int bufferHeight)
    {
        if (_waterfallBitmap == null ||
            _waterfallBitmap.Width != spectrumWidth ||
            _waterfallBitmap.Height != bufferHeight)
        {
            _waterfallBitmap?.Dispose();
            _waterfallBitmap = new SKBitmap(spectrumWidth, bufferHeight);
        }
    }

    private void UpdateBitmapPixels(
        SKBitmap bitmap,
        float[][] spectrogramBuffer,
        int bufferHead,
        int spectrumWidth,
        int bufferHeight)
    {
        nint pixelsPtr = bitmap.GetPixels();
        if (pixelsPtr == IntPtr.Zero)
        {
            _logger.Log(LogLevel.Error, LogPrefix, "Invalid bitmap pixels pointer");
            return;
        }

        int[] pixels = ArrayPool<int>.Shared.Rent(spectrumWidth * bufferHeight);
        try
        {
            UpdatePixelArray(pixels, spectrogramBuffer, bufferHead,
                           spectrumWidth, bufferHeight);
            Marshal.Copy(pixels, 0, pixelsPtr, spectrumWidth * bufferHeight);
        }
        finally
        {
            ArrayPool<int>.Shared.Return(pixels);
        }
    }

    private static void UpdatePixelArray(
        int[] pixels,
        float[][] spectrogramBuffer,
        int bufferHead,
        int spectrumWidth,
        int bufferHeight)
    {
        if (IsHardwareAccelerated && spectrumWidth >= Vector<float>.Count)
        {
            UpdatePixelsParallel(pixels, spectrogramBuffer, bufferHead,
                               spectrumWidth, bufferHeight);
        }
        else
        {
            UpdatePixelsSequential(pixels, spectrogramBuffer, bufferHead,
                                 spectrumWidth, bufferHeight);
        }
    }

    private static void UpdatePixelsParallel(
        int[] pixels,
        float[][] spectrogramBuffer,
        int bufferHead,
        int spectrumWidth,
        int bufferHeight)
    {
        Parallel.For(0, bufferHeight, y =>
        {
            int bufferIndex = (bufferHead + 1 + y) % bufferHeight;
            int rowOffset = y * spectrumWidth;
            ProcessPixelRow(pixels, spectrogramBuffer[bufferIndex],
                          rowOffset, spectrumWidth);
        });
    }

    private static void UpdatePixelsSequential(
        int[] pixels,
        float[][] spectrogramBuffer,
        int bufferHead,
        int spectrumWidth,
        int bufferHeight)
    {
        for (int y = 0; y < bufferHeight; y++)
        {
            int bufferIndex = (bufferHead + 1 + y) % bufferHeight;
            int rowOffset = y * spectrumWidth;
            ProcessPixelRow(pixels, spectrogramBuffer[bufferIndex],
                          rowOffset, spectrumWidth);
        }
    }

    private static void ProcessPixelRow(
        int[] pixels,
        float[] spectrumRow,
        int rowOffset,
        int spectrumWidth)
    {
        int vectorizedWidth = spectrumWidth - spectrumWidth % Vector<float>.Count;

        if (IsHardwareAccelerated && vectorizedWidth > 0)
        {
            ProcessRowVectorized(pixels, spectrumRow, rowOffset, vectorizedWidth);
            ProcessRowRemainder(pixels, spectrumRow, rowOffset,
                              vectorizedWidth, spectrumWidth);
        }
        else
        {
            ProcessRowSequential(pixels, spectrumRow, rowOffset, spectrumWidth);
        }
    }

    private static void ProcessRowVectorized(
        int[] pixels,
        float[] spectrumRow,
        int rowOffset,
        int vectorizedWidth)
    {
        for (int x = 0; x < vectorizedWidth; x += Vector<float>.Count)
        {
            Vector<float> values = new(spectrumRow, x);

            for (int i = 0; i < Vector<float>.Count; i++)
            {
                float normalized = Clamp(values[i], 0f, 1f);
                int paletteIndex = (int)(normalized * (COLOR_PALETTE_SIZE - 1));
                pixels[rowOffset + x + i] = _colorPalette[paletteIndex];
            }
        }
    }

    private static void ProcessRowRemainder(
        int[] pixels,
        float[] spectrumRow,
        int rowOffset,
        int vectorizedWidth,
        int spectrumWidth)
    {
        for (int x = vectorizedWidth; x < spectrumWidth; x++)
        {
            float normalized = Clamp(spectrumRow[x], 0f, 1f);
            int paletteIndex = (int)(normalized * (COLOR_PALETTE_SIZE - 1));
            pixels[rowOffset + x] = _colorPalette[paletteIndex];
        }
    }

    private static void ProcessRowSequential(
        int[] pixels,
        float[] spectrumRow,
        int rowOffset,
        int spectrumWidth)
    {
        for (int x = 0; x < spectrumWidth; x++)
        {
            float normalized = Clamp(spectrumRow[x], 0f, 1f);
            int paletteIndex = (int)(normalized * (COLOR_PALETTE_SIZE - 1));
            pixels[rowOffset + x] = _colorPalette[paletteIndex];
        }
    }

    private void DrawBitmapToCanvas(
        SKCanvas canvas,
        SKImageInfo info,
        float barWidth,
        float barSpacing,
        int barCount)
    {
        SKRect destRect = CalculateDestRect(info, barWidth, barSpacing, barCount);

        if (canvas.QuickReject(destRect))
            return;

        using var renderPaint = CreatePaint(
            SKColors.White,
            SKPaintStyle.Fill,
            0
        );

        canvas.Clear(SKColors.Black);
        canvas.DrawBitmap(_waterfallBitmap!, destRect, renderPaint);
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
            bottom: info.Height
        );
    }

    private void ApplyZoomEnhancement(
        SKCanvas canvas,
        SKRect destRect,
        float barWidth,
        int barCount,
        float barSpacing)
    {
        if (_currentSettings.UseGridLines)
        {
            DrawGridLines(canvas, destRect, barWidth, barCount, barSpacing);
        }

        if (_currentSettings.UseGridMarkers && barWidth > ZOOM_THRESHOLD * 1.5f)
        {
            DrawGridMarkers(canvas, destRect, barWidth, barCount, barSpacing);
        }
    }

    private void DrawGridLines(
        SKCanvas canvas,
        SKRect destRect,
        float barWidth,
        int barCount,
        float barSpacing)
    {
        using var paint = CreatePaint(
            new SKColor(255, 255, 255, GRID_LINE_ALPHA),
            SKPaintStyle.Stroke,
            _currentSettings.GridLineWidth
        );

        int gridStep = Max(1, barCount / GRID_DIVISIONS);
        using var path = new SKPath();

        DrawGridLineSegments(path, destRect, barWidth, barSpacing, barCount, gridStep);
        canvas.DrawPath(path, paint);
    }

    private static void DrawGridLineSegments(
        SKPath path,
        SKRect destRect,
        float barWidth,
        float barSpacing,
        int barCount,
        int gridStep)
    {
        for (int i = 0; i < barCount; i += gridStep)
        {
            float x = destRect.Left + i * (barWidth + barSpacing) + barWidth / 2;
            path.MoveTo(x, destRect.Top);
            path.LineTo(x, destRect.Bottom);
        }
    }

    private void DrawGridMarkers(
        SKCanvas canvas,
        SKRect destRect,
        float barWidth,
        int barCount,
        float barSpacing)
    {
        using var font = new SKFont { Size = _currentSettings.MarkerFontSize };
        using var textPaint = CreatePaint(
            SKColors.White,
            SKPaintStyle.Fill,
            0
        );

        int gridStep = Max(1, barCount / GRID_DIVISIONS);
        DrawMarkerLabels(canvas, destRect, barWidth, barSpacing,
                        barCount, gridStep, font, textPaint);
    }

    private static void DrawMarkerLabels(
        SKCanvas canvas,
        SKRect destRect,
        float barWidth,
        float barSpacing,
        int barCount,
        int gridStep,
        SKFont font,
        SKPaint textPaint)
    {
        for (int i = 0; i < barCount; i += gridStep * 2)
        {
            float x = destRect.Left + i * (barWidth + barSpacing) + barWidth / 2;
            string marker = $"{i}";
            float textWidth = font.MeasureText(marker, textPaint);
            canvas.DrawText(marker, x - textWidth / 2,
                          destRect.Bottom - 5, font, textPaint);
        }
    }

    private SKPaint CreatePaint(
        SKColor color,
        SKPaintStyle style,
        float strokeWidth)
    {
        var paint = _paintPool.Get();
        paint.Color = color;
        paint.Style = style;
        paint.IsAntialias = _useAntiAlias;
        paint.StrokeWidth = strokeWidth;
        return paint;
    }

    private static int CalculateOptimalSpectrumWidth(int barCount)
    {
        int baseWidth = (int)MathF.Max(barCount, MIN_BAR_COUNT);
        int width = 1;

        while (width < baseWidth)
            width *= 2;

        return Max(width, DEFAULT_SPECTRUM_WIDTH / 4);
    }

    private static float VectorSum(Vector<float> vector)
    {
        float sum = 0;
        for (int i = 0; i < Vector<float>.Count; i++)
            sum += vector[i];
        return sum;
    }

    private static float VectorMax(Vector<float> vector)
    {
        float max = float.MinValue;
        for (int i = 0; i < Vector<float>.Count; i++)
            max = MathF.Max(max, vector[i]);
        return max;
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
        float t = (normalized - BLUE_THRESHOLD) /
                  (CYAN_THRESHOLD - BLUE_THRESHOLD);
        return new SKColor(0, (byte)(50 + t * 205), 255, 255);
    }

    private static SKColor GetYellowRangeColor(float normalized)
    {
        float t = (normalized - CYAN_THRESHOLD) /
                  (YELLOW_THRESHOLD - CYAN_THRESHOLD);
        return new SKColor((byte)(t * 255), 255, (byte)(255 - t * 255), 255);
    }

    private static SKColor GetRedRangeColor(float normalized)
    {
        float t = (normalized - YELLOW_THRESHOLD) /
                  (1f - YELLOW_THRESHOLD);
        return new SKColor(255, (byte)(255 - t * 255), 0, 255);
    }

    public override bool RequiresRedraw()
    {
        return _overlayStateChanged ||
               _overlayStateChangeRequested ||
               _isOverlayActive;
    }

    protected override void OnInvalidateCachedResources()
    {
        base.OnInvalidateCachedResources();
        _waterfallBitmap?.Dispose();
        _waterfallBitmap = null;
        _needsBufferResize = true;
        _logger.Log(LogLevel.Debug, LogPrefix, "Cached resources invalidated");
    }

    protected override void OnDispose()
    {
        _renderSemaphore.Dispose();
        _waterfallBitmap?.Dispose();
        _waterfallBitmap = null;
        _spectrogramBuffer = null;
        _currentSpectrum = null;
        _spectrumPool.Dispose();
        base.OnDispose();
        _logger.Log(LogLevel.Debug, LogPrefix, "Disposed");
    }
}