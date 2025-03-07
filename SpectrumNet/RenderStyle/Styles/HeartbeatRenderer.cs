#nullable enable

namespace SpectrumNet
{
    public sealed class HeartbeatRenderer : ISpectrumRenderer, IDisposable
    {
        #region Constants
        private static class Constants
        {
            // Rendering thresholds
            public const float MinMagnitudeThreshold = 0.05f;    // Minimum threshold for displaying spectrum magnitude
            public const float GlowIntensity = 0.2f;             // Controls the intensity of the glow effect
            public const float GlowAlphaDivisor = 3f;            // Divisor for alpha channel of glow effect
            public const float AlphaMultiplier = 1.5f;           // Multiplier for alpha channel calculation

            // Animation properties
            public const float PulseFrequency = 6f;              // Frequency of heart pulse animation
            public const float HeartBaseScale = 0.6f;            // Base scale factor for hearts
            public const float AnimationTimeIncrement = 0.016f;  // Time increment for animation per frame
            public const float RadiansPerDegree = MathF.PI / 180f; // Radians per degree conversion factor

            // Layout configurations
            public static readonly (float Size, float Spacing, int Count) DefaultConfig =
                (60f, 15f, 8);                                  // Default configuration for normal mode
            public static readonly (float Size, float Spacing, int Count) OverlayConfig =
                (30f, 8f, 12);                                  // Configuration for overlay mode

            // Smoothing factors
            public const float SmoothingFactorNormal = 0.3f;     // Smoothing factor for normal mode
            public const float SmoothingFactorOverlay = 0.7f;    // Smoothing factor for overlay mode

            public static class Quality
            {
                // Low quality settings
                public const int LowHeartSides = 8;              // Number of sides for heart shape in low quality
                public const bool LowUseGlow = false;            // Whether to use glow effect in low quality
                public const float LowSimplificationFactor = 0.5f; // Simplification factor for path in low quality

                // Medium quality settings
                public const int MediumHeartSides = 12;          // Number of sides for heart shape in medium quality
                public const bool MediumUseGlow = true;          // Whether to use glow effect in medium quality
                public const float MediumSimplificationFactor = 0.2f; // Simplification factor for path in medium quality

                // High quality settings
                public const int HighHeartSides = 0;             // Number of sides for heart shape in high quality (0 means use cubic path)
                public const bool HighUseGlow = true;            // Whether to use glow effect in high quality
                public const float HighSimplificationFactor = 0f; // Simplification factor for path in high quality
            }
        }
        #endregion

        #region Fields and Properties
        private static HeartbeatRenderer? _instance;
        private bool _isInitialized, _isOverlayActive, _disposed;
        private ObjectPool<SKPath>? _pathPool;
        private ObjectPool<SKPaint>? _paintPool;
        private float _smoothingFactor = Constants.SmoothingFactorNormal;
        private float _heartSize, _heartSpacing, _globalAnimationTime;
        private int _heartCount;
        private float[]? _previousSpectrum, _cachedScaledSpectrum, _cachedSmoothedSpectrum;
        private int _lastSpectrumLength, _lastTargetCount;
        private float[]? _cosValues, _sinValues;
        private SKPicture? _cachedHeartPicture;
        private RenderQuality _quality = RenderQuality.Medium;
        private bool _useGlow = true;
        private int _heartSides = Constants.Quality.MediumHeartSides;
        private float _simplificationFactor = Constants.Quality.MediumSimplificationFactor;
        private bool _useAntiAlias = true;
        private SKFilterQuality _filterQuality = SKFilterQuality.Medium;
        private Task? _spectrumProcessingTask;
        private readonly object _spectrumLock = new();
        private const string LogPrefix = "HeartbeatRenderer";
        #endregion

        #region Nested Classes
        private class ObjectPool<T> where T : class
        {
            private readonly ConcurrentBag<T> _objects = new();
            private readonly Func<T> _objectGenerator;
            private readonly Action<T>? _objectResetter;

            public ObjectPool(Func<T> objectGenerator, Action<T>? objectResetter = null)
            {
                _objectGenerator = objectGenerator ?? throw new ArgumentNullException(nameof(objectGenerator));
                _objectResetter = objectResetter;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public T Get() => _objects.TryTake(out T? item) ? item : _objectGenerator();

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Return(T item)
            {
                _objectResetter?.Invoke(item);
                _objects.Add(item);
            }

            public void Dispose()
            {
                while (_objects.TryTake(out T? item))
                {
                    if (item is IDisposable disposable) disposable.Dispose();
                }
            }
        }
        #endregion

        #region Constructor and Instance Management
        private HeartbeatRenderer()
        {
            _pathPool = new ObjectPool<SKPath>(() => new SKPath(), path => path.Reset());
            _paintPool = new ObjectPool<SKPaint>(() => new SKPaint(), ResetPaint);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ResetPaint(SKPaint paint)
        {
            paint.Reset();
            paint.IsAntialias = _useAntiAlias;
            paint.FilterQuality = _filterQuality;
        }

        public static HeartbeatRenderer GetInstance() => _instance ??= new HeartbeatRenderer();
        #endregion

        #region Quality Management
        public RenderQuality Quality
        {
            get => _quality;
            set
            {
                if (_quality != value)
                {
                    _quality = value;
                    ApplyQualitySettings();

                    SmartLogger.Log(LogLevel.Debug, LogPrefix, $"Quality changed to {_quality}");
                }
            }
        }

        private void ApplyQualitySettings()
        {
            switch (_quality)
            {
                case RenderQuality.Low:
                    _useAntiAlias = false;
                    _filterQuality = SKFilterQuality.Low;
                    _useGlow = Constants.Quality.LowUseGlow;
                    _heartSides = Constants.Quality.LowHeartSides;
                    _simplificationFactor = Constants.Quality.LowSimplificationFactor;
                    break;

                case RenderQuality.Medium:
                    _useAntiAlias = true;
                    _filterQuality = SKFilterQuality.Medium;
                    _useGlow = Constants.Quality.MediumUseGlow;
                    _heartSides = Constants.Quality.MediumHeartSides;
                    _simplificationFactor = Constants.Quality.MediumSimplificationFactor;
                    break;

                case RenderQuality.High:
                    _useAntiAlias = true;
                    _filterQuality = SKFilterQuality.High;
                    _useGlow = Constants.Quality.HighUseGlow;
                    _heartSides = Constants.Quality.HighHeartSides;
                    _simplificationFactor = Constants.Quality.HighSimplificationFactor;
                    break;
            }

            InvalidateCachedResources();
        }

        private void InvalidateCachedResources()
        {
            _cachedHeartPicture?.Dispose();
            _cachedHeartPicture = null;
        }
        #endregion

        #region ISpectrumRenderer Implementation
        public void Initialize()
        {
            if (_isInitialized || _disposed) return;
            _isInitialized = true;
            UpdateConfiguration(Constants.DefaultConfig);
            ApplyQualitySettings();
            SmartLogger.Log(LogLevel.Debug, LogPrefix, "Initialized");
        }

        public void Configure(bool isOverlayActive, RenderQuality quality = RenderQuality.Medium)
        {
            bool configChanged = _isOverlayActive != isOverlayActive || _quality != quality;

            _isOverlayActive = isOverlayActive;
            _smoothingFactor = isOverlayActive ?
                Constants.SmoothingFactorOverlay :
                Constants.SmoothingFactorNormal;

            UpdateConfiguration(isOverlayActive ? Constants.OverlayConfig : Constants.DefaultConfig);

            Quality = quality;

            if (configChanged)
            {
                InvalidateCachedResources();
            }

            SmartLogger.Log(LogLevel.Debug, LogPrefix, $"Configured: Overlay={isOverlayActive}, Quality={quality}");
        }

        public void Render(SKCanvas? canvas, float[]? spectrum, SKImageInfo info,
                           float barWidth, float barSpacing, int barCount,
                           SKPaint? paint, Action<SKCanvas, SKImageInfo> drawPerformanceInfo)
        {
            if (!ValidateRenderParameters(canvas, spectrum, paint))
                return;

            if (canvas!.QuickReject(new SKRect(0, 0, info.Width, info.Height)))
                return;

            _globalAnimationTime = (_globalAnimationTime + Constants.AnimationTimeIncrement) % 1000f;

            AdjustConfiguration(barCount, barSpacing, info.Width, info.Height);
            int actualHeartCount = Math.Min(spectrum!.Length, _heartCount);

            if (_quality == RenderQuality.High && _spectrumProcessingTask == null)
            {
                ProcessSpectrumAsync(spectrum, actualHeartCount);
            }
            else
            {
                ProcessSpectrum(spectrum, actualHeartCount);
            }

            if (_cachedSmoothedSpectrum == null)
            {
                ProcessSpectrum(spectrum, actualHeartCount);
            }

            if (paint is null)
            {
                throw new ArgumentNullException(nameof(paint));
            }

            RenderHeartbeats(canvas, _cachedSmoothedSpectrum, info, paint);
            drawPerformanceInfo(canvas, info);
        }
        #endregion

        #region Configuration Methods
        private void UpdateConfiguration((float Size, float Spacing, int Count) config)
        {
            (_heartSize, _heartSpacing, _heartCount) = config;
            _previousSpectrum = _cachedScaledSpectrum = _cachedSmoothedSpectrum = null;
            _lastSpectrumLength = _lastTargetCount = 0;
            PrecomputeTrigValues();
            InvalidateCachedResources();
        }

        private void AdjustConfiguration(int barCount, float barSpacing, int canvasWidth, int canvasHeight)
        {
            _heartSize = Math.Max(10f, Constants.DefaultConfig.Size - barCount * 0.3f + barSpacing * 0.5f);
            _heartSpacing = Math.Max(5f, Constants.DefaultConfig.Spacing - barCount * 0.1f + barSpacing * 0.2f);
            _heartCount = Math.Clamp(barCount / 2, 4, 32);

            float maxSize = Math.Min(canvasWidth, canvasHeight) / 4f;
            if (_heartSize > maxSize) _heartSize = maxSize;

            if (_cosValues == null || _sinValues == null || _cosValues.Length != _heartCount)
                PrecomputeTrigValues();
        }

        private void PrecomputeTrigValues()
        {
            _cosValues = new float[_heartCount];
            _sinValues = new float[_heartCount];
            float angleStep = 360f / _heartCount * Constants.RadiansPerDegree;

            for (int i = 0; i < _heartCount; i++)
            {
                float angle = i * angleStep;
                _cosValues[i] = MathF.Cos(angle);
                _sinValues[i] = MathF.Sin(angle);
            }
        }
        #endregion

        #region Spectrum Processing Methods
        private void ProcessSpectrumAsync(float[] spectrum, int targetCount)
        {
            if (_spectrumProcessingTask != null && !_spectrumProcessingTask.IsCompleted)
                return;

            _spectrumProcessingTask = Task.Run(() =>
            {
                lock (_spectrumLock)
                {
                    ProcessSpectrum(spectrum, targetCount);
                }
            });
        }

        private void ProcessSpectrum(float[] spectrum, int targetCount)
        {
            bool needRescale = _lastSpectrumLength != spectrum.Length || _lastTargetCount != targetCount;

            if (needRescale || _cachedScaledSpectrum == null)
            {
                _cachedScaledSpectrum = new float[targetCount];
                _lastSpectrumLength = spectrum.Length;
                _lastTargetCount = targetCount;
            }

            if (Vector.IsHardwareAccelerated && spectrum.Length >= Vector<float>.Count)
            {
                ScaleSpectrumSIMD(spectrum, _cachedScaledSpectrum, targetCount);
            }
            else
            {
                ScaleSpectrumStandard(spectrum, _cachedScaledSpectrum, targetCount);
            }

            if (_cachedSmoothedSpectrum == null || _cachedSmoothedSpectrum.Length != targetCount)
                _cachedSmoothedSpectrum = new float[targetCount];

            SmoothSpectrum(_cachedScaledSpectrum, _cachedSmoothedSpectrum, targetCount);
        }

        private static void ScaleSpectrumStandard(float[] source, float[] target, int targetCount)
        {
            float blockSize = source.Length / (float)targetCount;

            for (int i = 0; i < targetCount; i++)
            {
                float sum = 0;
                int startIdx = (int)(i * blockSize);
                int endIdx = Math.Min(source.Length, (int)((i + 1) * blockSize));

                for (int j = startIdx; j < endIdx; j++) sum += source[j];

                target[i] = sum / Math.Max(1, endIdx - startIdx);
            }
        }

        private static void ScaleSpectrumSIMD(float[] source, float[] target, int targetCount)
        {
            float blockSize = source.Length / (float)targetCount;

            for (int i = 0; i < targetCount; i++)
            {
                int startIdx = (int)(i * blockSize);
                int endIdx = Math.Min(source.Length, (int)((i + 1) * blockSize));
                int count = endIdx - startIdx;

                if (count < Vector<float>.Count)
                {
                    float blockSum = 0;
                    for (int blockIdx = startIdx; blockIdx < endIdx; blockIdx++)
                        blockSum += source[blockIdx];
                    target[i] = blockSum / Math.Max(1, count);
                    continue;
                }

                Vector<float> sumVector = Vector<float>.Zero;
                int vectorized = count - (count % Vector<float>.Count);
                int vecIdx = 0;

                for (; vecIdx < vectorized; vecIdx += Vector<float>.Count)
                {
                    Vector<float> vec = new Vector<float>(source, startIdx + vecIdx);
                    sumVector += vec;
                }

                float remainingSum = 0;
                for (int k = 0; k < Vector<float>.Count; k++)
                {
                    remainingSum += sumVector[k];
                }

                for (; vecIdx < count; vecIdx++)
                {
                    remainingSum += source[startIdx + vecIdx];
                }

                target[i] = remainingSum / Math.Max(1, count);
            }
        }

        private void SmoothSpectrum(float[] source, float[] target, int count)
        {
            if (_previousSpectrum == null || _previousSpectrum.Length != count)
            {
                _previousSpectrum = new float[count];
                Array.Copy(source, _previousSpectrum, count);
            }

            if (Vector.IsHardwareAccelerated && count >= Vector<float>.Count)
            {
                int vectorized = count - (count % Vector<float>.Count);

                for (int i = 0; i < vectorized; i += Vector<float>.Count)
                {
                    Vector<float> sourcevector = new Vector<float>(source, i);
                    Vector<float> previousVector = new Vector<float>(_previousSpectrum, i);
                    Vector<float> diff = sourcevector - previousVector;
                    Vector<float> smoothingVector = new Vector<float>(_smoothingFactor);
                    Vector<float> resultVector = previousVector + diff * smoothingVector;

                    resultVector.CopyTo(target, i);
                    resultVector.CopyTo(_previousSpectrum, i);
                }

                for (int i = vectorized; i < count; i++)
                {
                    target[i] = _previousSpectrum[i] + (source[i] - _previousSpectrum[i]) * _smoothingFactor;
                    _previousSpectrum[i] = target[i];
                }
            }
            else
            {
                for (int i = 0; i < count; i++)
                {
                    target[i] = _previousSpectrum[i] + (source[i] - _previousSpectrum[i]) * _smoothingFactor;
                    _previousSpectrum[i] = target[i];
                }
            }
        }
        #endregion

        #region Rendering Methods
        private bool ValidateRenderParameters(SKCanvas? canvas, float[]? spectrum, SKPaint? paint) =>
            _isInitialized && !_disposed && canvas != null && spectrum != null &&
            spectrum.Length > 0 && paint != null && _pathPool != null && _paintPool != null;

        private void RenderHeartbeats(SKCanvas canvas, float[]? spectrum, SKImageInfo info, SKPaint? basePaint)
        {
            if (spectrum == null || basePaint == null)
                return;

            float centerX = info.Width / 2f;
            float centerY = info.Height / 2f;
            float radius = Math.Min(info.Width, info.Height) / 3f;

            SKPath heartPath = _pathPool!.Get();

            if (_cachedHeartPicture == null)
            {
                var recorder = new SKPictureRecorder();
                var recordCanvas = recorder.BeginRecording(new SKRect(-1, -1, 1, 1));
                CreateHeartPath(ref heartPath, 0, 0, 1f);
                recordCanvas.DrawPath(heartPath, basePaint);
                _cachedHeartPicture = recorder.EndRecording();
                heartPath.Reset();
            }

            SKPaint heartPaint = _paintPool!.Get();
            heartPaint.IsAntialias = _useAntiAlias;
            heartPaint.FilterQuality = _filterQuality;
            heartPaint.Style = SKPaintStyle.Fill;

            SKPaint? glowPaint = null;
            if (_useGlow)
            {
                glowPaint = _paintPool.Get();
                glowPaint.IsAntialias = _useAntiAlias;
                glowPaint.FilterQuality = _filterQuality;
                glowPaint.Style = SKPaintStyle.Fill;
            }

            lock (_spectrumLock)
            {
                for (int i = 0; i < spectrum.Length; i++)
                {
                    float magnitude = spectrum[i];
                    if (magnitude < Constants.MinMagnitudeThreshold)
                        continue;

                    float x = centerX + _cosValues![i] * radius * (1 - magnitude * 0.5f);
                    float y = centerY + _sinValues![i] * radius * (1 - magnitude * 0.5f);

                    float heartSize = _heartSize * magnitude * Constants.HeartBaseScale *
                                      (MathF.Sin(_globalAnimationTime * Constants.PulseFrequency) * 0.1f + 1f);

                    SKRect heartBounds = new(
                        x - heartSize,
                        y - heartSize,
                        x + heartSize,
                        y + heartSize
                    );

                    if (canvas.QuickReject(heartBounds))
                        continue;

                    byte alpha = (byte)MathF.Min(magnitude * Constants.AlphaMultiplier * 255f, 255f);
                    heartPaint.Color = basePaint.Color.WithAlpha(alpha);

                    if (_heartSides > 0)
                    {
                        DrawSimplifiedHeart(canvas, x, y, heartSize, heartPaint, glowPaint, alpha);
                    }
                    else
                    {
                        DrawCachedHeart(canvas, x, y, heartSize, heartPaint, glowPaint, alpha);
                    }
                }
            }

            _pathPool.Return(heartPath);
            _paintPool.Return(heartPaint);

            if (glowPaint != null)
                _paintPool.Return(glowPaint);
        }

        private void DrawSimplifiedHeart(SKCanvas canvas, float x, float y, float size,
                                       SKPaint heartPaint, SKPaint? glowPaint, byte alpha)
        {
            SKPath simplePath = _pathPool!.Get();

            float angleStep = 360f / _heartSides * Constants.RadiansPerDegree;
            simplePath.MoveTo(x, y + size / 2);

            for (int i = 0; i < _heartSides; i++)
            {
                float angle = i * angleStep;
                float radius = size * (1 + 0.3f * MathF.Sin(angle * 2)) * (1 - _simplificationFactor * 0.5f);
                float px = x + MathF.Cos(angle) * radius;
                float py = y + MathF.Sin(angle) * radius - size * 0.2f;
                simplePath.LineTo(px, py);
            }

            simplePath.Close();

            if (_useGlow && glowPaint != null)
            {
                glowPaint.Color = heartPaint.Color.WithAlpha((byte)(alpha / Constants.GlowAlphaDivisor));
                glowPaint.MaskFilter = SKMaskFilter.CreateBlur(
                    SKBlurStyle.Normal,
                    size * 0.2f * (1 - _simplificationFactor)
                );
                canvas.DrawPath(simplePath, glowPaint);
            }

            canvas.DrawPath(simplePath, heartPaint);
            _pathPool.Return(simplePath);
        }

        private void DrawCachedHeart(SKCanvas canvas, float x, float y, float size,
                                   SKPaint heartPaint, SKPaint? glowPaint, byte alpha)
        {
            canvas.Save();
            canvas.Translate(x, y);
            canvas.Scale(size, size);

            if (_useGlow && glowPaint != null)
            {
                glowPaint.Color = heartPaint.Color.WithAlpha((byte)(alpha / Constants.GlowAlphaDivisor));
                glowPaint.MaskFilter = SKMaskFilter.CreateBlur(
                    SKBlurStyle.Normal,
                    size * Constants.GlowIntensity
                );

                canvas.DrawPicture(_cachedHeartPicture!, glowPaint);
            }

            canvas.DrawPicture(_cachedHeartPicture!, heartPaint);
            canvas.Restore();
        }

        private static void CreateHeartPath(ref SKPath path, float x, float y, float size)
        {
            path.Reset();
            path.MoveTo(x, y + size / 2);
            path.CubicTo(x - size, y, x - size, y - size / 2, x, y - size);
            path.CubicTo(x + size, y - size / 2, x + size, y, x, y + size / 2);
            path.Close();
        }
        #endregion

        #region IDisposable Implementation
        public void Dispose()
        {
            if (_disposed) return;

            _spectrumProcessingTask?.Wait(100);

            _previousSpectrum = _cachedScaledSpectrum = _cachedSmoothedSpectrum = null;
            _cosValues = _sinValues = null;
            _isInitialized = false;
            _disposed = true;

            InvalidateCachedResources();

            _pathPool?.Dispose();
            _pathPool = null;

            if (_paintPool != null)
            {
                _paintPool.Dispose();
                _paintPool = null;
            }

            SmartLogger.Log(LogLevel.Debug, LogPrefix, "Disposed");
            GC.SuppressFinalize(this);
        }
        #endregion
    }
}