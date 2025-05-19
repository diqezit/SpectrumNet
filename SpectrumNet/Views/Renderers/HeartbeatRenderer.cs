#nullable enable

using static SpectrumNet.Views.Renderers.HeartbeatRenderer.Constants;
using static SpectrumNet.Views.Renderers.HeartbeatRenderer.Constants.Quality;
using static System.MathF;

namespace SpectrumNet.Views.Renderers;

public sealed class HeartbeatRenderer : EffectSpectrumRenderer
{
    private static readonly Lazy<HeartbeatRenderer> _instance = new(() => new HeartbeatRenderer());
    private const string LogPrefix = nameof(HeartbeatRenderer);

    private HeartbeatRenderer() { }

    public static HeartbeatRenderer GetInstance() => _instance.Value;

    public record Constants
    {
        public const float
            MIN_MAGNITUDE_THRESHOLD = 0.05f,
            GLOW_INTENSITY = 0.2f,
            GLOW_ALPHA_DIVISOR = 3f,
            ALPHA_MULTIPLIER = 1.5f;

        public const float
            PULSE_FREQUENCY = 6f,
            HEART_BASE_SCALE = 0.6f,
            ANIMATION_TIME_INCREMENT = 0.016f,
            RADIANS_PER_DEGREE = MathF.PI / 180f;

        public static readonly (float Size, float Spacing, int Count)
            DEFAULT_CONFIG = (60f, 15f, 8),
            OVERLAY_CONFIG = (30f, 8f, 12);

        public const float
            SMOOTHING_FACTOR_NORMAL = 0.3f,
            SMOOTHING_FACTOR_OVERLAY = 0.7f;

        public static class Quality
        {
            public const int
                LOW_HEART_SIDES = 8,
                MEDIUM_HEART_SIDES = 12,
                HIGH_HEART_SIDES = 0;

            public const bool
                LOW_USE_GLOW = false,
                MEDIUM_USE_GLOW = true,
                HIGH_USE_GLOW = true;

            public const float
                LOW_SIMPLIFICATION_FACTOR = 0.5f,
                MEDIUM_SIMPLIFICATION_FACTOR = 0.2f,
                HIGH_SIMPLIFICATION_FACTOR = 0f;

            public const bool
                LOW_USE_ANTIALIASING = false,
                MEDIUM_USE_ANTIALIASING = true,
                HIGH_USE_ANTIALIASING = true;

            public const bool
                LOW_USE_ADVANCED_EFFECTS = false,
                MEDIUM_USE_ADVANCED_EFFECTS = true,
                HIGH_USE_ADVANCED_EFFECTS = true;
        }
    }

    private readonly object _renderDataLock = new();

    private float _heartSize;
    private float _heartSpacing;
    private int _heartCount;
    private float[] _cosValues = [];
    private float[] _sinValues = [];
    private SKPicture? _cachedHeartPicture;

    private int _lastSpectrumLength;
    private int _lastTargetCount;
    private float[]? _cachedScaledSpectrum;
    private float[]? _cachedSmoothedSpectrum;
    private Task? _spectrumProcessingTask;
    private bool _dataReady;

    protected override void OnInitialize()
    {
        base.OnInitialize();
        UpdateConfiguration(DEFAULT_CONFIG);
        PrecomputeTrigValues();
        _logger.Debug(LogPrefix, "Initialized");
    }

    protected override void OnConfigurationChanged()
    {
        _smoothingFactor = _isOverlayActive ? SMOOTHING_FACTOR_OVERLAY : SMOOTHING_FACTOR_NORMAL;

        UpdateConfiguration(
            _isOverlayActive ? OVERLAY_CONFIG : DEFAULT_CONFIG
        );

        InvalidateCachedResources();
        _logger.Info(LogPrefix,
            $"Configuration changed. New Quality: {Quality}, Overlay: {_isOverlayActive}");
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

        InvalidateCachedResources();

        _logger.Debug(LogPrefix,
            $"Quality settings applied. Quality: {Quality}, " +
            $"AntiAlias: {UseAntiAlias}, AdvancedEffects: {UseAdvancedEffects}");
    }

    private void LowQualitySettings()
    {
        // Базовые настройки будут установлены базовым классом
    }

    private void MediumQualitySettings()
    {
        // Базовые настройки будут установлены базовым классом
    }

    private void HighQualitySettings()
    {
        // Базовые настройки будут установлены базовым классом
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
                     "Error rendering heartbeat effect");

    private void HandleRenderEffect(
        SKCanvas canvas,
        float[] spectrum,
        SKImageInfo info,
        float barWidth,
        float barSpacing,
        int barCount,
        SKPaint paint)
    {
        _time = (_time + ANIMATION_TIME_INCREMENT) % 1000f;

        UpdateState(spectrum, barCount, info, barSpacing);

        if (_dataReady)
        {
            RenderFrame(canvas, info, paint);
        }
    }

    private void UpdateState(
        float[] spectrum,
        int barCount,
        SKImageInfo info,
        float barSpacing) =>
        _logger.Safe(() => HandleUpdateState(spectrum, barCount, info, barSpacing),
                     LogPrefix,
                     "Error updating heartbeat state");

    private void HandleUpdateState(
        float[] spectrum,
        int barCount,
        SKImageInfo info,
        float barSpacing)
    {
        AdjustConfiguration(barCount, barSpacing, info.Width, info.Height);
        int actualHeartCount = (int)Min(spectrum.Length, _heartCount);
        ProcessSpectrumData(spectrum, actualHeartCount);

        if (_cachedSmoothedSpectrum != null)
        {
            _dataReady = true;
        }
    }

    private void RenderFrame(
        SKCanvas canvas,
        SKImageInfo info,
        SKPaint basePaint) =>
        _logger.Safe(() => HandleRenderFrame(canvas, info, basePaint),
                     LogPrefix,
                     "Error rendering heartbeat frame");

    private void HandleRenderFrame(
        SKCanvas canvas,
        SKImageInfo info,
        SKPaint basePaint)
    {
        if (_cachedSmoothedSpectrum == null)
            return;

        RenderHeartbeats(canvas, _cachedSmoothedSpectrum, info, basePaint);
    }

    private void UpdateConfiguration((float Size, float Spacing, int Count) config) =>
        _logger.Safe(() => HandleUpdateConfiguration(config),
                     LogPrefix,
                     "Error updating heartbeat configuration");

    private void HandleUpdateConfiguration((float Size, float Spacing, int Count) config)
    {
        (_heartSize, _heartSpacing, _heartCount) = config;
        _previousSpectrum = _cachedScaledSpectrum = _cachedSmoothedSpectrum = null;
        _lastSpectrumLength = _lastTargetCount = 0;
        PrecomputeTrigValues();
        InvalidateCachedResources();
    }

    private void AdjustConfiguration(int barCount, float barSpacing, int canvasWidth, int canvasHeight) =>
        _logger.Safe(() => HandleAdjustConfiguration(barCount, barSpacing, canvasWidth, canvasHeight),
                     LogPrefix,
                     "Error adjusting heartbeat configuration");

    private void HandleAdjustConfiguration(int barCount, float barSpacing, int canvasWidth, int canvasHeight)
    {
        _heartSize = CalculateHeartSize(barCount, barSpacing);
        _heartSpacing = CalculateHeartSpacing(barCount, barSpacing);
        _heartCount = CalculateHeartCount(barCount);

        AdjustHeartSizeToCanvas(canvasWidth, canvasHeight);
        EnsureTrigValuesInitialized();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float CalculateHeartSize(int barCount, float barSpacing) =>
        MathF.Max(10f, DEFAULT_CONFIG.Size - barCount * 0.3f + barSpacing * 0.5f);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float CalculateHeartSpacing(int barCount, float barSpacing) =>
        MathF.Max(5f, DEFAULT_CONFIG.Spacing - barCount * 0.1f + barSpacing * 0.2f);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int CalculateHeartCount(int barCount) =>
        Clamp(barCount / 2, 4, 32);

    private void AdjustHeartSizeToCanvas(int canvasWidth, int canvasHeight)
    {
        float maxSize = Min(canvasWidth, canvasHeight) / 4f;
        if (_heartSize > maxSize)
        {
            _heartSize = maxSize;
        }
    }

    private void EnsureTrigValuesInitialized()
    {
        if (_cosValues.Length != _heartCount || _sinValues.Length != _heartCount)
        {
            PrecomputeTrigValues();
        }
    }

    private void PrecomputeTrigValues() =>
        _logger.Safe(() => HandlePrecomputeTrigValues(),
                     LogPrefix,
                     "Error precomputing trig values");

    private void HandlePrecomputeTrigValues()
    {
        _cosValues = new float[_heartCount];
        _sinValues = new float[_heartCount];
        float angleStep = 360f / _heartCount * RADIANS_PER_DEGREE;

        for (int i = 0; i < _heartCount; i++)
        {
            float angle = i * angleStep;
            _cosValues[i] = Cos(angle);
            _sinValues[i] = Sin(angle);
        }
    }

    private void ProcessSpectrumData(float[] spectrum, int actualHeartCount) =>
        _logger.Safe(() => HandleProcessSpectrumData(spectrum, actualHeartCount),
                     LogPrefix,
                     "Error processing spectrum data");

    private void HandleProcessSpectrumData(float[] spectrum, int actualHeartCount)
    {
        if (Quality == RenderQuality.High && _spectrumProcessingTask == null)
        {
            ProcessSpectrumAsync(spectrum, actualHeartCount);
        }
        else
        {
            ProcessSpectrum(spectrum, actualHeartCount);
        }
    }

    private void ProcessSpectrumAsync(float[] spectrum, int targetCount) =>
        _logger.Safe(() => HandleProcessSpectrumAsync(spectrum, targetCount),
                     LogPrefix,
                     "Error processing spectrum asynchronously");

    private void HandleProcessSpectrumAsync(float[] spectrum, int targetCount)
    {
        if (_spectrumProcessingTask != null && !_spectrumProcessingTask.IsCompleted)
            return;

        _spectrumProcessingTask = Task.Run(() =>
        {
            lock (_renderDataLock)
            {
                ProcessSpectrum(spectrum, targetCount);
            }
        });
    }

    private void ProcessSpectrum(float[] spectrum, int targetCount) =>
        _logger.Safe(() => HandleProcessSpectrum(spectrum, targetCount),
                     LogPrefix,
                     "Error processing spectrum");

    private void HandleProcessSpectrum(float[] spectrum, int targetCount)
    {
        EnsureSpectrumBuffers(spectrum, targetCount);
        ScaleSpectrum(spectrum, targetCount);
        EnsureSmoothedBuffer(targetCount);
        SmoothSpectrum(
            _cachedScaledSpectrum!,
            _cachedSmoothedSpectrum!,
            targetCount
        );
    }

    private void EnsureSpectrumBuffers(float[] spectrum, int targetCount) =>
        _logger.Safe(() => HandleEnsureSpectrumBuffers(spectrum, targetCount),
                     LogPrefix,
                     "Error ensuring spectrum buffers");

    private void HandleEnsureSpectrumBuffers(float[] spectrum, int targetCount)
    {
        bool needRescale = _lastSpectrumLength != spectrum.Length ||
                           _lastTargetCount != targetCount ||
                           _cachedScaledSpectrum == null;

        if (needRescale)
        {
            _cachedScaledSpectrum = new float[targetCount];
            _lastSpectrumLength = spectrum.Length;
            _lastTargetCount = targetCount;
        }
    }

    private void ScaleSpectrum(float[] spectrum, int targetCount) =>
        _logger.Safe(() => HandleScaleSpectrum(spectrum, targetCount),
                     LogPrefix,
                     "Error scaling spectrum");

    private void HandleScaleSpectrum(float[] spectrum, int targetCount)
    {
        if (CanUseVectorization(spectrum.Length))
        {
            ScaleSpectrumSIMD(
                spectrum,
                _cachedScaledSpectrum!,
                targetCount
            );
        }
        else
        {
            ScaleSpectrumStandard(
                spectrum,
                _cachedScaledSpectrum!,
                targetCount
            );
        }
    }

    private void EnsureSmoothedBuffer(int targetCount) =>
        _logger.Safe(() => HandleEnsureSmoothedBuffer(targetCount),
                     LogPrefix,
                     "Error ensuring smoothed buffer");

    private void HandleEnsureSmoothedBuffer(int targetCount)
    {
        if (_cachedSmoothedSpectrum == null || _cachedSmoothedSpectrum.Length != targetCount)
        {
            _cachedSmoothedSpectrum = new float[targetCount];
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ScaleSpectrumStandard(
        float[] source,
        float[] target,
        int targetCount)
    {
        float blockSize = source.Length / (float)targetCount;

        for (int i = 0; i < targetCount; i++)
        {
            float sum = 0;
            int startIdx = (int)(i * blockSize);
            int endIdx = (int)Min(
                source.Length,
                (int)((i + 1) * blockSize)
            );

            for (int j = startIdx; j < endIdx; j++)
            {
                sum += source[j];
            }

            target[i] = sum / Max(1, endIdx - startIdx);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static void ScaleSpectrumSIMD(
        float[] source,
        float[] target,
        int targetCount)
    {
        float blockSize = source.Length / (float)targetCount;

        for (int i = 0; i < targetCount; i++)
        {
            int startIdx = (int)(i * blockSize);
            int endIdx = (int)Min(
                source.Length,
                (int)((i + 1) * blockSize)
            );
            int count = endIdx - startIdx;

            if (count < Vector<float>.Count)
            {
                target[i] = CalculateBlockAverage(
                    source,
                    startIdx,
                    endIdx
                );
                continue;
            }

            target[i] = CalculateBlockAverageVectorized(
                source,
                startIdx,
                count
            );
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float CalculateBlockAverage(
        float[] source,
        int startIdx,
        int endIdx)
    {
        float blockSum = 0;
        for (int blockIdx = startIdx; blockIdx < endIdx; blockIdx++)
        {
            blockSum += source[blockIdx];
        }
        return blockSum / Max(1, endIdx - startIdx);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float CalculateBlockAverageVectorized(
        float[] source,
        int startIdx,
        int count)
    {
        Vector<float> sumVector = Vector<float>.Zero;
        int vectorized = count - count % Vector<float>.Count;
        int vecIdx = 0;

        for (; vecIdx < vectorized; vecIdx += Vector<float>.Count)
        {
            Vector<float> vec = new(source, startIdx + vecIdx);
            sumVector += vec;
        }

        float remainingSum = SumVectorElements(sumVector);

        for (; vecIdx < count; vecIdx++)
        {
            remainingSum += source[startIdx + vecIdx];
        }

        return remainingSum / Max(1, count);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float SumVectorElements(Vector<float> vector)
    {
        float sum = 0;
        for (int k = 0; k < Vector<float>.Count; k++)
        {
            sum += vector[k];
        }
        return sum;
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private void SmoothSpectrum(float[] source, float[] target, int count) =>
        _logger.Safe(() => HandleSmoothSpectrum(source, target, count),
                     LogPrefix,
                     "Error smoothing spectrum");

    private void HandleSmoothSpectrum(float[] source, float[] target, int count)
    {
        EnsurePreviousSpectrumBuffer(source, count);

        if (CanUseVectorization(count))
        {
            SmoothSpectrumVectorized(source, target, count);
        }
        else
        {
            SmoothSpectrumSequential(source, target, count);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool CanUseVectorization(int count) =>
        IsHardwareAccelerated && count >= Vector<float>.Count;

    private void EnsurePreviousSpectrumBuffer(float[] source, int count) =>
        _logger.Safe(() => HandleEnsurePreviousSpectrumBuffer(source, count),
                     LogPrefix,
                     "Error ensuring previous spectrum buffer");

    private void HandleEnsurePreviousSpectrumBuffer(float[] source, int count)
    {
        if (_previousSpectrum == null || _previousSpectrum.Length != count)
        {
            _previousSpectrum = new float[count];
            Array.Copy(source, _previousSpectrum, count);
        }
    }

    private void SmoothSpectrumVectorized(
        float[] source,
        float[] target,
        int count) =>
        _logger.Safe(() => HandleSmoothSpectrumVectorized(source, target, count),
                     LogPrefix,
                     "Error vectorizing spectrum smoothing");

    private void HandleSmoothSpectrumVectorized(
        float[] source,
        float[] target,
        int count)
    {
        int vectorized = count - count % Vector<float>.Count;

        for (int i = 0; i < vectorized; i += Vector<float>.Count)
        {
            Vector<float> sourceVector = new(source, i);
            Vector<float> previousVector = new(_previousSpectrum!, i);

            Vector<float> resultVector = CalculateSmoothingVectorized(
                sourceVector,
                previousVector
            );

            resultVector.CopyTo(target, i);
            resultVector.CopyTo(_previousSpectrum!, i);
        }

        SmoothSpectrumSequential(source, target, count, vectorized);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Vector<float> CalculateSmoothingVectorized(
        Vector<float> sourceVector,
        Vector<float> previousVector)
    {
        Vector<float> diff = sourceVector - previousVector;
        Vector<float> smoothingVector = new(_smoothingFactor);
        return previousVector + diff * smoothingVector;
    }

    private void SmoothSpectrumSequential(float[] source, float[] target, int count, int startIndex = 0) =>
        _logger.Safe(() => HandleSmoothSpectrumSequential(source, target, count, startIndex),
                     LogPrefix,
                     "Error smoothing spectrum sequentially");

    private void HandleSmoothSpectrumSequential(float[] source, float[] target, int count, int startIndex = 0)
    {
        for (int i = startIndex; i < count; i++)
        {
            target[i] = _previousSpectrum![i] + (source[i] - _previousSpectrum[i]) * _smoothingFactor;
            _previousSpectrum[i] = target[i];
        }
    }

    private void RenderHeartbeats(
        SKCanvas canvas,
        float[] spectrum,
        SKImageInfo info,
        SKPaint basePaint) =>
        _logger.Safe(() => HandleRenderHeartbeats(canvas, spectrum, info, basePaint),
                     LogPrefix,
                     "Error rendering heartbeats");

    private void HandleRenderHeartbeats(
        SKCanvas canvas,
        float[] spectrum,
        SKImageInfo info,
        SKPaint basePaint)
    {
        (float centerX, float centerY, float radius) = CalculateRenderGeometry(info);

        using var heartPath = _pathPool.Get();
        EnsureCachedHeartPicture(heartPath, basePaint);

        using var heartPaint = ConfigureHeartPaint();
        using var glowPaint = ConfigureGlowPaint();

        int heartSides = GetHeartSidesForQuality();
        float simplificationFactor = GetSimplificationFactorForQuality();

        lock (_renderDataLock)
        {
            DrawHearts(
                canvas,
                spectrum,
                centerX,
                centerY,
                radius,
                heartPath,
                heartPaint,
                glowPaint,
                basePaint,
                heartSides,
                simplificationFactor
            );
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static (float centerX, float centerY, float radius) CalculateRenderGeometry(SKImageInfo info)
    {
        float centerX = info.Width / 2f;
        float centerY = info.Height / 2f;
        float radius = Min(info.Width, info.Height) / 3f;

        return (centerX, centerY, radius);
    }

    private void EnsureCachedHeartPicture(SKPath heartPath, SKPaint basePaint) =>
        _logger.Safe(() => HandleEnsureCachedHeartPicture(heartPath, basePaint),
                     LogPrefix,
                     "Error ensuring cached heart picture");

    private void HandleEnsureCachedHeartPicture(SKPath heartPath, SKPaint basePaint)
    {
        if (_cachedHeartPicture == null)
        {
            var recorder = new SKPictureRecorder();
            var recordCanvas = recorder.BeginRecording(new SKRect(-1, -1, 1, 1));
            CreateHeartPath(heartPath, 0, 0, 1f);
            recordCanvas.DrawPath(heartPath, basePaint);
            _cachedHeartPicture = recorder.EndRecording();
            heartPath.Reset();
        }
    }

    private static void CreateHeartPath(SKPath path, float x, float y, float size)
    {
        path.Reset();
        path.MoveTo(x, y + size / 2);
        path.CubicTo(x - size, y, x - size, y - size / 2, x, y - size);
        path.CubicTo(x + size, y - size / 2, x + size, y, x, y + size / 2);
        path.Close();
    }

    private SKPaint ConfigureHeartPaint()
    {
        var heartPaint = _paintPool.Get();
        heartPaint.IsAntialias = UseAntiAlias;
        heartPaint.Style = SKPaintStyle.Fill;
        return heartPaint;
    }

    private SKPaint? ConfigureGlowPaint()
    {
        if (!UseAdvancedEffects)
            return null;

        var glowPaint = _paintPool.Get();
        glowPaint.IsAntialias = UseAntiAlias;
        glowPaint.Style = SKPaintStyle.Fill;
        return glowPaint;
    }

    private void DrawHearts(
        SKCanvas canvas,
        float[] spectrum,
        float centerX,
        float centerY,
        float radius,
        SKPath heartPath,
        SKPaint heartPaint,
        SKPaint? glowPaint,
        SKPaint basePaint,
        int heartSides,
        float simplificationFactor) =>
        _logger.Safe(() => HandleDrawHearts(canvas, spectrum, centerX, centerY,
            radius, heartPath, heartPaint, glowPaint, basePaint, heartSides, simplificationFactor),
                     LogPrefix,
                     "Error drawing hearts");

    private void HandleDrawHearts(
        SKCanvas canvas,
        float[] spectrum,
        float centerX,
        float centerY,
        float radius,
        SKPath heartPath,
        SKPaint heartPaint,
        SKPaint? glowPaint,
        SKPaint basePaint,
        int heartSides,
        float simplificationFactor)
    {
        for (int i = 0; i < spectrum.Length; i++)
        {
            float magnitude = spectrum[i];
            if (magnitude < MIN_MAGNITUDE_THRESHOLD)
                continue;

            DrawSingleHeart(canvas, magnitude, i, centerX, centerY,
                radius, heartPath, heartPaint, glowPaint, basePaint,
                heartSides, simplificationFactor);
        }
    }

    private void DrawSingleHeart(
        SKCanvas canvas,
        float magnitude,
        int index,
        float centerX,
        float centerY,
        float radius,
        SKPath heartPath,
        SKPaint heartPaint,
        SKPaint? glowPaint,
        SKPaint basePaint,
        int heartSides,
        float simplificationFactor) =>
        _logger.Safe(() => HandleDrawSingleHeart(canvas, magnitude, index, centerX, centerY,
            radius, heartPath, heartPaint, glowPaint, basePaint, heartSides, simplificationFactor),
                     LogPrefix,
                     "Error drawing single heart");

    private void HandleDrawSingleHeart(
        SKCanvas canvas,
        float magnitude,
        int index,
        float centerX,
        float centerY,
        float radius,
        SKPath heartPath,
        SKPaint heartPaint,
        SKPaint? glowPaint,
        SKPaint basePaint,
        int heartSides,
        float simplificationFactor)
    {
        (float x, float y) = CalculateHeartPosition(
            centerX,
            centerY,
            radius,
            magnitude,
            index
        );

        float heartSize = CalculateHeartSize(
            magnitude,
            _heartSize,
            _time
        );

        SKRect heartBounds = CalculateHeartBounds(x, y, heartSize);

        if (canvas.QuickReject(heartBounds))
            return;

        byte alpha = CalculateAlpha(magnitude);
        heartPaint.Color = basePaint.Color.WithAlpha(alpha);

        DrawHeart(
            canvas,
            x,
            y,
            heartSize,
            heartPath,
            heartPaint,
            glowPaint,
            alpha,
            heartSides,
            simplificationFactor
        );
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private (float x, float y) CalculateHeartPosition(
        float centerX,
        float centerY,
        float radius,
        float magnitude,
        int index)
    {
        if (index >= _cosValues.Length || index >= _sinValues.Length)
            return (centerX, centerY);

        float x = centerX + _cosValues[index] * radius * (1 - magnitude * 0.5f);
        float y = centerY + _sinValues[index] * radius * (1 - magnitude * 0.5f);
        return (x, y);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float CalculateHeartSize(float magnitude, float heartSize, float time) =>
        heartSize * magnitude * HEART_BASE_SCALE * (Sin(time * PULSE_FREQUENCY) * 0.1f + 1f);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static SKRect CalculateHeartBounds(float x, float y, float heartSize) =>
        new(
            x - heartSize,
            y - heartSize,
            x + heartSize,
            y + heartSize
        );

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte CalculateAlpha(float magnitude) =>
        (byte)MathF.Min(magnitude * ALPHA_MULTIPLIER * 255f, 255f);

    private void DrawHeart(
        SKCanvas canvas,
        float x,
        float y,
        float heartSize,
        SKPath heartPath,
        SKPaint heartPaint,
        SKPaint? glowPaint,
        byte alpha,
        int heartSides,
        float simplificationFactor) =>
        _logger.Safe(() => HandleDrawHeart(canvas, x, y, heartSize, heartPath, heartPaint,
            glowPaint, alpha, heartSides, simplificationFactor),
                     LogPrefix,
                     "Error drawing heart");

    private void HandleDrawHeart(
        SKCanvas canvas,
        float x,
        float y,
        float heartSize,
        SKPath heartPath,
        SKPaint heartPaint,
        SKPaint? glowPaint,
        byte alpha,
        int heartSides,
        float simplificationFactor)
    {
        if (heartSides > 0)
        {
            DrawSimplifiedHeart(canvas, x, y, heartSize, heartPath, heartPaint,
                glowPaint, alpha, heartSides, simplificationFactor);
        }
        else
        {
            DrawCachedHeart(canvas, x, y, heartSize, heartPaint, glowPaint, alpha);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int GetHeartSidesForQuality() => Quality switch
    {
        RenderQuality.Low => LOW_HEART_SIDES,
        RenderQuality.Medium => MEDIUM_HEART_SIDES,
        RenderQuality.High => HIGH_HEART_SIDES,
        _ => MEDIUM_HEART_SIDES
    };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private float GetSimplificationFactorForQuality() => Quality switch
    {
        RenderQuality.Low => LOW_SIMPLIFICATION_FACTOR,
        RenderQuality.Medium => MEDIUM_SIMPLIFICATION_FACTOR,
        RenderQuality.High => HIGH_SIMPLIFICATION_FACTOR,
        _ => MEDIUM_SIMPLIFICATION_FACTOR
    };

    private void DrawSimplifiedHeart(
        SKCanvas canvas,
        float x,
        float y,
        float size,
        SKPath path,
        SKPaint heartPaint,
        SKPaint? glowPaint,
        byte alpha,
        int sides,
        float simplificationFactor) =>
        _logger.Safe(() => HandleDrawSimplifiedHeart(canvas, x, y, size, path, heartPaint,
            glowPaint, alpha, sides, simplificationFactor),
                     LogPrefix,
                     "Error drawing simplified heart");

    private void HandleDrawSimplifiedHeart(
        SKCanvas canvas,
        float x,
        float y,
        float size,
        SKPath path,
        SKPaint heartPaint,
        SKPaint? glowPaint,
        byte alpha,
        int sides,
        float simplificationFactor)
    {
        CreatePolygonHeartPath(path, x, y, size, sides, simplificationFactor);
        DrawHeartWithGlowEffect(
            canvas,
            path,
            heartPaint,
            UseAdvancedEffects ? glowPaint : null,
            alpha,
            size,
            simplificationFactor);
    }

    private static void CreatePolygonHeartPath(
        SKPath path,
        float x,
        float y,
        float size,
        int sides,
        float simplificationFactor)
    {
        path.Reset();
        float angleStep = 360f / sides * RADIANS_PER_DEGREE;
        path.MoveTo(x, y + size / 2);

        for (int i = 0; i < sides; i++)
        {
            float angle = i * angleStep;
            float radius = size * (1 + 0.3f * Sin(angle * 2)) * (1 - simplificationFactor * 0.5f);
            float px = x + Cos(angle) * radius;
            float py = y + Sin(angle) * radius - size * 0.2f;
            path.LineTo(px, py);
        }

        path.Close();
    }

    private static void DrawHeartWithGlowEffect(
        SKCanvas canvas,
        SKPath path,
        SKPaint heartPaint,
        SKPaint? glowPaint,
        byte alpha,
        float size,
        float simplificationFactor)
    {
        if (glowPaint != null)
        {
            glowPaint.Color = heartPaint.Color.WithAlpha((byte)(alpha / GLOW_ALPHA_DIVISOR));
            glowPaint.MaskFilter = SKMaskFilter.CreateBlur(
                SKBlurStyle.Normal,
                size * 0.2f * (1 - simplificationFactor)
            );
            canvas.DrawPath(path, glowPaint);
        }

        canvas.DrawPath(path, heartPaint);
    }

    private void DrawCachedHeart(
        SKCanvas canvas,
        float x,
        float y,
        float size,
        SKPaint heartPaint,
        SKPaint? glowPaint,
        byte alpha) =>
        _logger.Safe(() => HandleDrawCachedHeart(canvas, x, y, size, heartPaint, glowPaint, alpha),
                     LogPrefix,
                     "Error drawing cached heart");

    private void HandleDrawCachedHeart(
        SKCanvas canvas,
        float x,
        float y,
        float size,
        SKPaint heartPaint,
        SKPaint? glowPaint,
        byte alpha)
    {
        canvas.Save();
        try
        {
            canvas.Translate(x, y);
            canvas.Scale(size, size);

            if (UseAdvancedEffects && glowPaint != null && _cachedHeartPicture != null)
            {
                glowPaint.Color = heartPaint.Color.WithAlpha((byte)(alpha / GLOW_ALPHA_DIVISOR));
                glowPaint.MaskFilter = SKMaskFilter.CreateBlur(
                    SKBlurStyle.Normal,
                    size * GLOW_INTENSITY
                );
                canvas.DrawPicture(_cachedHeartPicture, glowPaint);
            }

            if (_cachedHeartPicture != null)
                canvas.DrawPicture(_cachedHeartPicture, heartPaint);
        }
        finally
        {
            canvas.Restore();
        }
    }

    protected override void OnInvalidateCachedResources()
    {
        _logger.Safe(() => HandleInvalidateCachedResources(),
                     LogPrefix,
                     "Error invalidating cached resources");

        base.OnInvalidateCachedResources();
    }

    private void HandleInvalidateCachedResources()
    {
        _cachedHeartPicture?.Dispose();
        _cachedHeartPicture = null;
        _dataReady = false;

        _logger.Debug(LogPrefix, "Cached resources invalidated");
    }

    protected override void OnDispose()
    {
        _logger.Safe(() => HandleCleanupResources(),
                     LogPrefix,
                     "Error cleaning up heartbeat renderer resources");

        base.OnDispose();
        _logger.Debug(LogPrefix, "Disposed");
    }

    private void HandleCleanupResources()
    {
        WaitForPendingTasks();
        DisposeHeartResources();
        ClearBuffers();
    }

    private void WaitForPendingTasks()
    {
        _spectrumProcessingTask?.Wait(100);
    }

    private void DisposeHeartResources()
    {
        _cachedHeartPicture?.Dispose();
        _cachedHeartPicture = null;
    }

    private void ClearBuffers()
    {
        _previousSpectrum = _cachedScaledSpectrum = _cachedSmoothedSpectrum = null;
        _cosValues = _sinValues = [];
    }
}