#nullable enable

using static SpectrumNet.Views.Renderers.HeartbeatRenderer.Constants;
using static System.MathF;

namespace SpectrumNet.Views.Renderers;

public sealed class HeartbeatRenderer : EffectSpectrumRenderer
{
    private static readonly Lazy<HeartbeatRenderer> _instance = new(() => new HeartbeatRenderer());

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

    private HeartbeatRenderer() { }

    public static HeartbeatRenderer GetInstance() => _instance.Value;

    public record Constants
    {
        public const string LOG_PREFIX = "HeartbeatRenderer";

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
        }
    }

    public override void Initialize()
    {
        Safe(
            () =>
            {
                base.Initialize();
                InitializeResources();
                ApplyQualitySettings();
                Log(
                    LogLevel.Debug,
                    LOG_PREFIX,
                    "Initialized"
                );
            },
            new ErrorHandlingOptions
            {
                Source = $"{LOG_PREFIX}.Initialize",
                ErrorMessage = "Failed to initialize renderer"
            }
        );
    }

    private void InitializeResources()
    {
        Safe(
            () =>
            {
                UpdateConfiguration(DEFAULT_CONFIG);
                PrecomputeTrigValues();
            },
            new ErrorHandlingOptions
            {
                Source = $"{LOG_PREFIX}.InitializeResources",
                ErrorMessage = "Failed to initialize renderer resources"
            }
        );
    }

    public override void Configure(
        bool isOverlayActive,
        RenderQuality quality = RenderQuality.Medium)
    {
        Safe(
            () =>
            {
                bool configChanged = _isOverlayActive != isOverlayActive || Quality != quality;

                base.Configure(
                    isOverlayActive,
                    quality
                );

                _smoothingFactor = isOverlayActive
                    ? SMOOTHING_FACTOR_OVERLAY
                    : SMOOTHING_FACTOR_NORMAL;

                UpdateConfiguration(
                    isOverlayActive
                        ? OVERLAY_CONFIG
                        : DEFAULT_CONFIG
                );

                if (configChanged)
                {
                    OnConfigurationChanged();
                }
            },
            new ErrorHandlingOptions
            {
                Source = $"{LOG_PREFIX}.Configure",
                ErrorMessage = "Failed to configure renderer"
            }
        );
    }

    protected override void OnConfigurationChanged()
    {
        Safe(
            () =>
            {
                InvalidateCachedResources();
            },
            new ErrorHandlingOptions
            {
                Source = $"{LOG_PREFIX}.OnConfigurationChanged",
                ErrorMessage = "Failed to handle configuration change"
            }
        );
    }

    protected override void ApplyQualitySettings()
    {
        Safe(
            () =>
            {
                base.ApplyQualitySettings();
                OnQualitySettingsApplied();
            },
            new ErrorHandlingOptions
            {
                Source = $"{LOG_PREFIX}.ApplyQualitySettings",
                ErrorMessage = "Failed to apply quality settings"
            }
        );
    }

    protected override void OnQualitySettingsApplied()
    {
        Safe(
            () =>
            {
                InvalidateCachedResources();
            },
            new ErrorHandlingOptions
            {
                Source = $"{LOG_PREFIX}.OnQualitySettingsApplied",
                ErrorMessage = "Failed to apply quality-specific settings"
            }
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
        if (!ValidateRenderParameters(
            canvas,
            spectrum,
            info,
            paint))
        {
            return;
        }

        Safe(
            () =>
            {
                _time = (_time + ANIMATION_TIME_INCREMENT) % 1000f;

                UpdateState(
                    spectrum,
                    barCount,
                    info,
                    barSpacing
                );

                if (_dataReady)
                {
                    RenderFrame(
                        canvas,
                        info,
                        paint
                    );
                }
            },
            new ErrorHandlingOptions
            {
                Source = $"{LOG_PREFIX}.RenderEffect",
                ErrorMessage = "Error in RenderEffect method"
            }
        );
    }

    private bool ValidateRenderParameters(
        SKCanvas? canvas,
        float[]? spectrum,
        SKImageInfo info,
        SKPaint? paint)
    {
        if (!_isInitialized)
        {
            Log(
                LogLevel.Error,
                LOG_PREFIX,
                "Renderer is not initialized"
            );
            return false;
        }

        if (canvas == null || spectrum == null || paint == null)
        {
            Log(
                LogLevel.Error,
                LOG_PREFIX,
                "Invalid render parameters: null values"
            );
            return false;
        }

        if (info.Width <= 0 || info.Height <= 0)
        {
            Log(
                LogLevel.Error,
                LOG_PREFIX,
                $"Invalid image dimensions: {info.Width}x{info.Height}"
            );
            return false;
        }

        if (spectrum.Length == 0)
        {
            Log(
                LogLevel.Warning,
                LOG_PREFIX,
                "Empty spectrum data"
            );
            return false;
        }

        if (_disposed)
        {
            Log(
                LogLevel.Error,
                LOG_PREFIX,
                "Renderer is disposed"
            );
            return false;
        }

        return true;
    }

    private void UpdateState(
        float[] spectrum,
        int barCount,
        SKImageInfo info,
        float barSpacing)
    {
        Safe(
            () =>
            {
                AdjustConfiguration(
                    barCount,
                    barSpacing,
                    info.Width,
                    info.Height
                );

                int actualHeartCount = (int)MathF.Min(spectrum.Length, _heartCount);
                ProcessSpectrumData(spectrum, actualHeartCount);

                if (_cachedSmoothedSpectrum != null)
                {
                    _dataReady = true;
                }
            },
            new ErrorHandlingOptions
            {
                Source = $"{LOG_PREFIX}.UpdateState",
                ErrorMessage = "Error updating renderer state"
            }
        );
    }

    private void ProcessSpectrumData(float[] spectrum, int actualHeartCount)
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

    private void RenderFrame(
        SKCanvas canvas,
        SKImageInfo info,
        SKPaint basePaint)
    {
        Safe(
            () =>
            {
                if (_cachedSmoothedSpectrum == null)
                {
                    return;
                }

                RenderHeartbeats(canvas, _cachedSmoothedSpectrum, info, basePaint);
            },
            new ErrorHandlingOptions
            {
                Source = $"{LOG_PREFIX}.RenderFrame",
                ErrorMessage = "Error rendering frame"
            }
        );
    }

    private void UpdateConfiguration((float Size, float Spacing, int Count) config)
    {
        Safe(
            () =>
            {
                (_heartSize, _heartSpacing, _heartCount) = config;
                _previousSpectrum = _cachedScaledSpectrum = _cachedSmoothedSpectrum = null;
                _lastSpectrumLength = _lastTargetCount = 0;
                PrecomputeTrigValues();
                InvalidateCachedResources();
            },
            new ErrorHandlingOptions
            {
                Source = $"{LOG_PREFIX}.UpdateConfiguration",
                ErrorMessage = "Failed to update configuration"
            }
        );
    }

    private void AdjustConfiguration(int barCount, float barSpacing, int canvasWidth, int canvasHeight)
    {
        Safe(
            () =>
            {
                _heartSize = CalculateHeartSize(barCount, barSpacing);
                _heartSpacing = CalculateHeartSpacing(barCount, barSpacing);
                _heartCount = CalculateHeartCount(barCount);

                AdjustHeartSizeToCanvas(canvasWidth, canvasHeight);
                EnsureTrigValuesInitialized();
            },
            new ErrorHandlingOptions
            {
                Source = $"{LOG_PREFIX}.AdjustConfiguration",
                ErrorMessage = "Failed to adjust configuration"
            }
        );
    }

    private static float CalculateHeartSize(int barCount, float barSpacing) =>
        MathF.Max(10f, DEFAULT_CONFIG.Size - barCount * 0.3f + barSpacing * 0.5f);

    private static float CalculateHeartSpacing(int barCount, float barSpacing) =>
        MathF.Max(5f, DEFAULT_CONFIG.Spacing - barCount * 0.1f + barSpacing * 0.2f);

    private static int CalculateHeartCount(int barCount) =>
        Clamp(barCount / 2, 4, 32);

    private void AdjustHeartSizeToCanvas(int canvasWidth, int canvasHeight)
    {
        float maxSize = MathF.Min(canvasWidth, canvasHeight) / 4f;
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

    private void PrecomputeTrigValues()
    {
        Safe(
            () =>
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
            },
            new ErrorHandlingOptions
            {
                Source = $"{LOG_PREFIX}.PrecomputeTrigValues",
                ErrorMessage = "Failed to precompute trigonometric values"
            }
        );
    }

    private void ProcessSpectrumAsync(float[] spectrum, int targetCount)
    {
        Safe(
            () =>
            {
                if (_spectrumProcessingTask != null && !_spectrumProcessingTask.IsCompleted)
                {
                    return;
                }

                _spectrumProcessingTask = Task.Run(() =>
                {
                    lock (_renderDataLock)
                    {
                        ProcessSpectrum(spectrum, targetCount);
                    }
                });
            },
            new ErrorHandlingOptions
            {
                Source = $"{LOG_PREFIX}.ProcessSpectrumAsync",
                ErrorMessage = "Failed to process spectrum asynchronously"
            }
        );
    }

    private void ProcessSpectrum(float[] spectrum, int targetCount)
    {
        Safe(
            () =>
            {
                EnsureSpectrumBuffers(spectrum, targetCount);
                ScaleSpectrum(spectrum, targetCount);
                EnsureSmoothedBuffer(targetCount);
                SmoothSpectrum(
                    _cachedScaledSpectrum!,
                    _cachedSmoothedSpectrum!,
                    targetCount
                );
            },
            new ErrorHandlingOptions
            {
                Source = $"{LOG_PREFIX}.ProcessSpectrum",
                ErrorMessage = "Failed to process spectrum"
            }
        );
    }

    private void EnsureSpectrumBuffers(float[] spectrum, int targetCount)
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

    private void ScaleSpectrum(float[] spectrum, int targetCount)
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

    private void EnsureSmoothedBuffer(int targetCount)
    {
        if (_cachedSmoothedSpectrum == null || _cachedSmoothedSpectrum.Length != targetCount)
        {
            _cachedSmoothedSpectrum = new float[targetCount];
        }
    }

    [MethodImpl(AggressiveInlining)]
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
            int endIdx = (int)MathF.Min(
                source.Length,
                (int)((i + 1) * blockSize)
            );

            for (int j = startIdx; j < endIdx; j++)
            {
                sum += source[j];
            }

            target[i] = sum / MathF.Max(1, endIdx - startIdx);
        }
    }

    [MethodImpl(AggressiveOptimization)]
    private static void ScaleSpectrumSIMD(
        float[] source,
        float[] target,
        int targetCount)
    {
        float blockSize = source.Length / (float)targetCount;

        for (int i = 0; i < targetCount; i++)
        {
            int startIdx = (int)(i * blockSize);
            int endIdx = (int)MathF.Min(
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
        return blockSum / MathF.Max(1, endIdx - startIdx);
    }

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

        return remainingSum / MathF.Max(1, count);
    }

    private static float SumVectorElements(Vector<float> vector)
    {
        float sum = 0;
        for (int k = 0; k < Vector<float>.Count; k++)
        {
            sum += vector[k];
        }
        return sum;
    }

    [MethodImpl(AggressiveOptimization)]
    private void SmoothSpectrum(float[] source, float[] target, int count)
    {
        Safe(
            () =>
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
            },
            new ErrorHandlingOptions
            {
                Source = $"{LOG_PREFIX}.SmoothSpectrum",
                ErrorMessage = "Failed to smooth spectrum"
            }
        );
    }

    private bool CanUseVectorization(int count) =>
        IsHardwareAccelerated && count >= Vector<float>.Count;

    private void EnsurePreviousSpectrumBuffer(float[] source, int count)
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

        // Process remaining elements
        SmoothSpectrumSequential(source, target, count, vectorized);
    }

    private Vector<float> CalculateSmoothingVectorized(
        Vector<float> sourceVector,
        Vector<float> previousVector)
    {
        Vector<float> diff = sourceVector - previousVector;
        Vector<float> smoothingVector = new(_smoothingFactor);
        return previousVector + diff * smoothingVector;
    }

    private void SmoothSpectrumSequential(float[] source, float[] target, int count, int startIndex = 0)
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
        SKPaint basePaint)
    {
        Safe(
            () =>
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
            },
            new ErrorHandlingOptions
            {
                Source = $"{LOG_PREFIX}.RenderHeartbeats",
                ErrorMessage = "Failed to render heartbeats"
            }
        );
    }

    private static (float centerX, float centerY, float radius) CalculateRenderGeometry(SKImageInfo info)
    {
        float centerX = info.Width / 2f;
        float centerY = info.Height / 2f;
        float radius = MathF.Min(info.Width, info.Height) / 3f;

        return (centerX, centerY, radius);
    }

    private void EnsureCachedHeartPicture(SKPath heartPath, SKPaint basePaint)
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

    private SKPaint ConfigureHeartPaint()
    {
        var heartPaint = _paintPool.Get();
        heartPaint.IsAntialias = UseAntiAlias;
        heartPaint.Style = Fill;
        return heartPaint;
    }

    private SKPaint? ConfigureGlowPaint()
    {
        if (!UseAdvancedEffects)
        {
            return null;
        }

        var glowPaint = _paintPool.Get();
        glowPaint.IsAntialias = UseAntiAlias;
        glowPaint.Style = Fill;
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
        float simplificationFactor)
    {
        for (int i = 0; i < spectrum.Length; i++)
        {
            float magnitude = spectrum[i];
            if (magnitude < MIN_MAGNITUDE_THRESHOLD)
            {
                continue;
            }

            DrawSingleHeart(
                canvas,
                magnitude,
                i,
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
        {
            return;
        }

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

    private (float x, float y) CalculateHeartPosition(
        float centerX,
        float centerY,
        float radius,
        float magnitude,
        int index)
    {
        float x = centerX + _cosValues[index] * radius * (1 - magnitude * 0.5f);
        float y = centerY + _sinValues[index] * radius * (1 - magnitude * 0.5f);
        return (x, y);
    }

    private static float CalculateHeartSize(float magnitude, float heartSize, float time) =>
        heartSize * magnitude * HEART_BASE_SCALE * (Sin(time * PULSE_FREQUENCY) * 0.1f + 1f);

    private static SKRect CalculateHeartBounds(float x, float y, float heartSize) =>
        new(
            x - heartSize,
            y - heartSize,
            x + heartSize,
            y + heartSize
        );

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
        float simplificationFactor)
    {
        if (heartSides > 0)
        {
            DrawSimplifiedHeart(
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
        else
        {
            DrawCachedHeart(
                canvas,
                x,
                y,
                heartSize,
                heartPaint,
                glowPaint,
                alpha
            );
        }
    }

    private int GetHeartSidesForQuality() => Quality switch
    {
        RenderQuality.Low => Constants.Quality.LOW_HEART_SIDES,
        RenderQuality.Medium => Constants.Quality.MEDIUM_HEART_SIDES,
        RenderQuality.High => Constants.Quality.HIGH_HEART_SIDES,
        _ => Constants.Quality.MEDIUM_HEART_SIDES
    };

    private float GetSimplificationFactorForQuality() => Quality switch
    {
        RenderQuality.Low => Constants.Quality.LOW_SIMPLIFICATION_FACTOR,
        RenderQuality.Medium => Constants.Quality.MEDIUM_SIMPLIFICATION_FACTOR,
        RenderQuality.High => Constants.Quality.HIGH_SIMPLIFICATION_FACTOR,
        _ => Constants.Quality.MEDIUM_SIMPLIFICATION_FACTOR
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
        float simplificationFactor)
    {
        Safe(
            () =>
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
            },
            new ErrorHandlingOptions
            {
                Source = $"{LOG_PREFIX}.DrawSimplifiedHeart",
                ErrorMessage = "Failed to draw simplified heart"
            }
        );
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
                Normal,
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
        byte alpha)
    {
        Safe(
            () =>
            {
                SetupHeartTransform(canvas, x, y, size);
                DrawCachedHeartWithEffects(canvas, heartPaint, glowPaint, alpha, size);
                canvas.Restore();
            },
            new ErrorHandlingOptions
            {
                Source = $"{LOG_PREFIX}.DrawCachedHeart",
                ErrorMessage = "Failed to draw cached heart"
            }
        );
    }

    private static void SetupHeartTransform(SKCanvas canvas, float x, float y, float size)
    {
        canvas.Save();
        canvas.Translate(x, y);
        canvas.Scale(size, size);
    }

    private void DrawCachedHeartWithEffects(
        SKCanvas canvas,
        SKPaint heartPaint,
        SKPaint? glowPaint,
        byte alpha,
        float size)
    {
        if (UseAdvancedEffects && glowPaint != null)
        {
            ConfigureGlowEffect(glowPaint, heartPaint.Color, alpha, size);
            canvas.DrawPicture(_cachedHeartPicture!, glowPaint);
        }

        canvas.DrawPicture(_cachedHeartPicture!, heartPaint);
    }

    private static void ConfigureGlowEffect(SKPaint glowPaint, SKColor baseColor, byte alpha, float size)
    {
        glowPaint.Color = baseColor.WithAlpha((byte)(alpha / GLOW_ALPHA_DIVISOR));
        glowPaint.MaskFilter = SKMaskFilter.CreateBlur(
            Normal,
            size * GLOW_INTENSITY
        );
    }

    private static void CreateHeartPath(SKPath path, float x, float y, float size)
    {
        path.Reset();
        path.MoveTo(x, y + size / 2);
        path.CubicTo(x - size, y, x - size, y - size / 2, x, y - size);
        path.CubicTo(x + size, y - size / 2, x + size, y, x, y + size / 2);
        path.Close();
    }

    protected override void OnInvalidateCachedResources()
    {
        Safe(
            () =>
            {
                _cachedHeartPicture?.Dispose();
                _cachedHeartPicture = null;
                _dataReady = false;
            },
            new ErrorHandlingOptions
            {
                Source = $"{LOG_PREFIX}.OnInvalidateCachedResources",
                ErrorMessage = "Failed to invalidate cached resources"
            }
        );
    }

    protected override void OnDispose()
    {
        Safe(
            () =>
            {
                DisposeManagedResources();
                base.OnDispose();
                Log(
                    LogLevel.Debug,
                    LOG_PREFIX,
                    "Disposed"
                );
            },
            new ErrorHandlingOptions
            {
                Source = $"{LOG_PREFIX}.OnDispose",
                ErrorMessage = "Error during OnDispose"
            }
        );
    }

    private void DisposeManagedResources()
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