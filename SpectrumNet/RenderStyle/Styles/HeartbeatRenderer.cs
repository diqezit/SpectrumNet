#nullable enable

namespace SpectrumNet
{
    #region Renderers Implementations

    public sealed class HeartbeatRenderer : ISpectrumRenderer, IDisposable
    {
        #region Fields and Properties
        private static HeartbeatRenderer? _instance;
        private bool _isInitialized, _isOverlayActive, _disposed;
        private ObjectPool<SKPath>? _pathPool;
        private float _smoothingFactor = 0.3f, _heartSize, _heartSpacing, _globalAnimationTime;
        private int _heartCount;
        private float[]? _previousSpectrum, _cachedScaledSpectrum, _cachedSmoothedSpectrum;
        private int _lastSpectrumLength, _lastTargetCount;
        private float[]? _cosValues, _sinValues; // Для равномерного расположения по кругу
        #endregion

        #region Constants
        private const float MinMagnitudeThreshold = 0.05f, PulseFrequency = 6f, HeartBaseScale = 0.6f;
        private const float GlowIntensity = 0.2f, GlowAlphaDivisor = 3f, AlphaMultiplier = 1.5f;
        private const float AnimationTimeIncrement = 0.016f, RadiansPerDegree = MathF.PI / 180f;
        private const float DefaultHeartSize = 60f, DefaultHeartSpacing = 15f;
        private const float OverlayHeartSize = 30f, OverlayHeartSpacing = 8f;
        private const int DefaultHeartCount = 8, OverlayHeartCount = 12;
        #endregion

        #region Static Configurations
        private static readonly (float Size, float Spacing, int Count) DefaultConfig =
            (DefaultHeartSize, DefaultHeartSpacing, DefaultHeartCount);
        private static readonly (float Size, float Spacing, int Count) OverlayConfig =
            (OverlayHeartSize, OverlayHeartSpacing, OverlayHeartCount);
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
        private HeartbeatRenderer() => _pathPool = new ObjectPool<SKPath>(() => new SKPath(), path => path.Reset());
        public static HeartbeatRenderer GetInstance() => _instance ??= new HeartbeatRenderer();
        #endregion

        #region ISpectrumRenderer Implementation
        public void Initialize()
        {
            if (_isInitialized || _disposed) return;
            _isInitialized = true;
            UpdateConfiguration(DefaultConfig);
            Log.Debug("HeartbeatRenderer initialized");
        }

        public void Configure(bool isOverlayActive)
        {
            if (_isOverlayActive == isOverlayActive) return;
            _isOverlayActive = isOverlayActive;
            _smoothingFactor = isOverlayActive ? 0.7f : 0.4f;
            UpdateConfiguration(isOverlayActive ? OverlayConfig : DefaultConfig);
        }

        public void Render(SKCanvas? canvas, float[]? spectrum, SKImageInfo info,
                          float barWidth, float barSpacing, int barCount,
                          SKPaint? paint, Action<SKCanvas, SKImageInfo> drawPerformanceInfo)
        {
            if (!ValidateRenderParameters(canvas, spectrum, paint)) return;

            _globalAnimationTime = (_globalAnimationTime + AnimationTimeIncrement) % 1000f;

            AdjustConfiguration(barCount, barSpacing, info.Width, info.Height);
            int actualHeartCount = Math.Min(spectrum!.Length, _heartCount);

            ProcessSpectrum(spectrum, actualHeartCount);
            RenderHeartbeats(canvas!, _cachedSmoothedSpectrum!, info, paint!);
            drawPerformanceInfo(canvas!, info);
        }
        #endregion

        #region Configuration Methods
        private void UpdateConfiguration((float Size, float Spacing, int Count) config)
        {
            (_heartSize, _heartSpacing, _heartCount) = config;
            _previousSpectrum = _cachedScaledSpectrum = _cachedSmoothedSpectrum = null;
            _lastSpectrumLength = _lastTargetCount = 0;
            PrecomputeTrigValues();
        }

        private void AdjustConfiguration(int barCount, float barSpacing, int canvasWidth, int canvasHeight)
        {
            _heartSize = Math.Max(10f, DefaultHeartSize - barCount * 0.3f + barSpacing * 0.5f);
            _heartSpacing = Math.Max(5f, DefaultHeartSpacing - barCount * 0.1f + barSpacing * 0.2f);
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
            float angleStep = 360f / _heartCount * RadiansPerDegree;

            for (int i = 0; i < _heartCount; i++)
            {
                float angle = i * angleStep;
                _cosValues[i] = MathF.Cos(angle);
                _sinValues[i] = MathF.Sin(angle);
            }
        }
        #endregion

        #region Spectrum Processing Methods
        private void ProcessSpectrum(float[] spectrum, int targetCount)
        {
            bool needRescale = _lastSpectrumLength != spectrum.Length || _lastTargetCount != targetCount;

            if (needRescale || _cachedScaledSpectrum == null)
            {
                _cachedScaledSpectrum = new float[targetCount];
                _lastSpectrumLength = spectrum.Length;
                _lastTargetCount = targetCount;
            }

            ScaleSpectrum(spectrum, _cachedScaledSpectrum, targetCount);

            if (_cachedSmoothedSpectrum == null || _cachedSmoothedSpectrum.Length != targetCount)
                _cachedSmoothedSpectrum = new float[targetCount];

            SmoothSpectrum(_cachedScaledSpectrum, _cachedSmoothedSpectrum, targetCount);
        }

        private static void ScaleSpectrum(float[] source, float[] target, int targetCount)
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

        private void SmoothSpectrum(float[] source, float[] target, int count)
        {
            if (_previousSpectrum == null || _previousSpectrum.Length != count)
            {
                _previousSpectrum = new float[count];
                Array.Copy(source, _previousSpectrum, count);
            }

            for (int i = 0; i < count; i++)
            {
                target[i] = _previousSpectrum[i] + (source[i] - _previousSpectrum[i]) * _smoothingFactor;
                _previousSpectrum[i] = target[i];
            }
        }
        #endregion

        #region Rendering Methods
        private bool ValidateRenderParameters(SKCanvas? canvas, float[]? spectrum, SKPaint? paint) =>
            _isInitialized && !_disposed && canvas != null && spectrum != null &&
            spectrum.Length > 0 && paint != null && _pathPool != null;

        private void RenderHeartbeats(SKCanvas canvas, float[] spectrum, SKImageInfo info, SKPaint basePaint)
        {
            float centerX = info.Width / 2f;
            float centerY = info.Height / 2f;
            float radius = Math.Min(info.Width, info.Height) / 3f;

            SKPath heartPath = _pathPool!.Get();

            for (int i = 0; i < spectrum.Length; i++)
            {
                float magnitude = spectrum[i];
                if (magnitude < MinMagnitudeThreshold) continue;

                // Используем предварительно вычисленные значения для равномерного расположения
                float x = centerX + _cosValues![i] * radius * (1 - magnitude * 0.5f);
                float y = centerY + _sinValues![i] * radius * (1 - magnitude * 0.5f);

                byte alpha = (byte)MathF.Min(magnitude * AlphaMultiplier * 255f, 255f);

                using var heartPaint = basePaint.Clone();
                heartPaint.Color = heartPaint.Color.WithAlpha(alpha);
                heartPaint.Style = SKPaintStyle.Fill;

                float heartSize = _heartSize * magnitude * HeartBaseScale *
                                 (MathF.Sin(_globalAnimationTime * PulseFrequency) * 0.1f + 1f);

                CreateHeartPath(ref heartPath, x, y, heartSize);

                using var glowPaint = heartPaint.Clone();
                glowPaint.Color = glowPaint.Color.WithAlpha((byte)(alpha / GlowAlphaDivisor));
                glowPaint.MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, magnitude * GlowIntensity);

                canvas.DrawPath(heartPath, glowPaint);
                canvas.DrawPath(heartPath, heartPaint);
            }

            _pathPool.Return(heartPath);
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

            _previousSpectrum = _cachedScaledSpectrum = _cachedSmoothedSpectrum = null;
            _cosValues = _sinValues = null;
            _isInitialized = false;
            _disposed = true;

            _pathPool?.Dispose();
            _pathPool = null;

            Log.Debug("HeartbeatRenderer disposed");
            GC.SuppressFinalize(this);
        }
        #endregion
    }

    #endregion
}