#nullable enable

namespace SpectrumNet
{
    public sealed class RaindropsRenderer : ISpectrumRenderer, IDisposable
    {
        #region Nested Types
        private readonly struct RenderCache
        {
            public readonly float Width, Height, LowerBound, UpperBound, StepSize;

            public RenderCache(float width, float height, bool isOverlay)
            {
                Width = width; Height = height;
                LowerBound = isOverlay ? height * Settings.Instance.OverlayHeightMultiplier : height;
                UpperBound = 0f;
                StepSize = width / Settings.Instance.MaxRaindrops;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private readonly struct Raindrop
        {
            public readonly float X, Y, FallSpeed;
            public readonly int SpectrumIndex;

            public Raindrop(float x, float y, float fallSpeed, int spectrumIndex) =>
                (X, Y, FallSpeed, SpectrumIndex) = (x, y, fallSpeed, spectrumIndex);

            public Raindrop WithNewY(float newY) => new(X, newY, FallSpeed, SpectrumIndex);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Particle
        {
            public float X, Y, VelocityX, VelocityY, Lifetime;
            public bool IsSplash;

            public Particle(float x, float y, float velocityX, float velocityY, bool isSplash) =>
                (X, Y, VelocityX, VelocityY, Lifetime, IsSplash) = (x, y, velocityX, velocityY, 1.0f, isSplash);

            public bool Update(float deltaTime, float lowerBound)
            {
                X += VelocityX * deltaTime;
                Y += VelocityY * deltaTime;
                VelocityY += deltaTime * 9.8f;
                Lifetime -= deltaTime * 0.5f;

                if (IsSplash && Y >= lowerBound)
                {
                    Y = lowerBound;
                    VelocityY = -VelocityY * 0.6f;
                    if (Math.Abs(VelocityY) < 1.0f) VelocityY = 0;
                }

                return Lifetime > 0;
            }
        }

        private sealed class ParticleBuffer
        {
            private Particle[] _particles;
            private int _count;
            private float _lowerBound;

            public ParticleBuffer(int capacity, float lowerBound) =>
                (_particles, _count, _lowerBound) = (new Particle[capacity], 0, lowerBound);

            public void UpdateLowerBound(float lowerBound) => _lowerBound = lowerBound;

            public void ResizeBuffer(int newCapacity)
            {
                if (newCapacity <= 0) return;

                var newParticles = new Particle[newCapacity];
                int copyCount = Math.Min(_count, newCapacity);

                if (copyCount > 0)
                    Array.Copy(_particles, newParticles, copyCount);

                _particles = newParticles;
                _count = copyCount;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void AddParticle(in Particle particle)
            {
                if (_count < _particles.Length) _particles[_count++] = particle;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Clear() => _count = 0;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void UpdateParticles(float deltaTime)
            {
                int writeIndex = 0;
                for (int i = 0; i < _count; i++)
                {
                    ref Particle p = ref _particles[i];
                    if (p.Update(deltaTime, _lowerBound))
                    {
                        if (writeIndex != i) _particles[writeIndex] = p;
                        writeIndex++;
                    }
                }
                _count = writeIndex;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void RenderParticles(SKCanvas canvas, SKPaint paint)
            {
                SKColor baseColor = paint.Color.WithAlpha(255);

                for (int i = 0; i < _count; i++)
                {
                    ref Particle p = ref _particles[i];

                    float clampedLifetime = Math.Clamp(p.Lifetime, 0f, 1f);
                    byte alpha = (byte)(clampedLifetime * 255);

                    paint.Color = baseColor.WithAlpha(alpha);

                    canvas.DrawCircle(
                        p.X,
                        p.Y,
                        p.IsSplash ? Settings.Instance.SplashParticleSize * 1.5f
                                   : Settings.Instance.SplashParticleSize,
                        paint);
                }
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void CreateSplashParticles(float x, float y, Random random)
            {
                int count = random.Next(5, 15);
                for (int i = 0; i < count; i++)
                {
                    float angle = (float)(random.NextDouble() * Math.PI * 2);
                    float speed = (float)(random.NextDouble() * Settings.Instance.ParticleVelocityMax);
                    AddParticle(new Particle(x, y, MathF.Cos(angle) * speed,
                                MathF.Sin(angle) * speed - Settings.Instance.SplashUpwardForce, true));
                }
            }
        }
        #endregion

        #region Fields
        private static readonly Lazy<RaindropsRenderer> _instance = new(() => new RaindropsRenderer());
        private RenderCache _renderCache;
        private Raindrop[] _raindrops;
        private int _raindropCount;
        private readonly Random _random = new();
        private float[] _scaledSpectrumCache;
        private bool _isInitialized, _isOverlayActive, _cacheNeedsUpdate, _isDisposed;
        private readonly ParticleBuffer _particleBuffer;
        private float _timeSinceLastSpawn;
        private bool _firstRender = true;
        private readonly Stopwatch _frameTimer = new Stopwatch();
        private float _actualDeltaTime = 0.016f;

        private readonly Thread _processingThread;
        private readonly CancellationTokenSource _cts = new();
        private readonly AutoResetEvent _spectrumDataAvailable = new(false);
        private readonly AutoResetEvent _processingComplete = new(false);
        private readonly object _spectrumLock = new();
        private float[]? _spectrumToProcess;
        private int _barCountToProcess;
        private bool _processingRunning;
        #endregion

        #region Constructor and Instance Management
        private RaindropsRenderer()
        {
            _raindrops = new Raindrop[Settings.Instance.MaxRaindrops];
            _scaledSpectrumCache = new float[Settings.Instance.MaxRaindrops];

            _particleBuffer = new ParticleBuffer(Settings.Instance.MaxParticles, 1);
            _renderCache = new RenderCache(1, 1, false);
            _frameTimer.Start();

            // Подписываемся на изменения настроек
            Settings.Instance.PropertyChanged += OnSettingsChanged;

            _processingThread = new Thread(ProcessSpectrumThreadFunc)
            {
                IsBackground = true,
                Name = "RaindropsProcessor"
            };
            _processingRunning = true;
            _processingThread.Start();
        }

        private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Settings.MaxRaindrops))
            {
                var newRaindrops = new Raindrop[Settings.Instance.MaxRaindrops];
                var newSpectrumCache = new float[Settings.Instance.MaxRaindrops];

                int copyCount = Math.Min(_raindropCount, Settings.Instance.MaxRaindrops);
                if (copyCount > 0)
                    Array.Copy(_raindrops, newRaindrops, copyCount);

                _raindrops = newRaindrops;
                _scaledSpectrumCache = newSpectrumCache;
                _raindropCount = copyCount;

                _cacheNeedsUpdate = true;
            }
            else if (e.PropertyName == nameof(Settings.MaxParticles))
            {
                _particleBuffer.ResizeBuffer(Settings.Instance.MaxParticles);
            }
            else if (e.PropertyName == nameof(Settings.OverlayHeightMultiplier))
            {
                _cacheNeedsUpdate = true;
            }
        }

        public static RaindropsRenderer GetInstance() => _instance.Value;
        #endregion

        #region ISpectrumRenderer Implementation
        public void Initialize()
        {
            if (_isDisposed)
            {
                _raindropCount = 0;
                _particleBuffer.Clear();
                _isDisposed = false;
            }
            _isInitialized = true;
            _firstRender = true;
        }

        public void Configure(bool isOverlayActive)
        {
            if (_isOverlayActive == isOverlayActive) return;

            _isOverlayActive = isOverlayActive;
            _cacheNeedsUpdate = true;
            _firstRender = true;
            _raindropCount = 0;
            _particleBuffer.Clear();
        }

        public void Render(SKCanvas? canvas, float[]? spectrum, SKImageInfo info,
                           float barWidth, float barSpacing, int barCount,
                           SKPaint? paint, Action<SKCanvas, SKImageInfo>? drawPerformanceInfo)
        {
            if (!ValidateRenderParameters(canvas, spectrum, paint)) return;

            try
            {
                float targetDeltaTime = 0.016f; 
                float elapsed = (float)_frameTimer.Elapsed.TotalSeconds;
                _frameTimer.Restart();

                float speedMultiplier = elapsed / targetDeltaTime;
                _actualDeltaTime = targetDeltaTime * speedMultiplier;

                if (_actualDeltaTime > Settings.Instance.MaxTimeStep)
                    _actualDeltaTime = Settings.Instance.MaxTimeStep;
                if (_actualDeltaTime < Settings.Instance.MinTimeStep)
                    _actualDeltaTime = Settings.Instance.MinTimeStep;

                using var localPaint = paint!.Clone();
                localPaint.BlendMode = SKBlendMode.SrcOver;
                localPaint.ColorFilter = null;
                localPaint.Color = localPaint.Color.WithAlpha(255);

                if (_cacheNeedsUpdate || _renderCache.Width != info.Width || _renderCache.Height != info.Height)
                {
                    _renderCache = new RenderCache(info.Width, info.Height, _isOverlayActive);
                    _particleBuffer.UpdateLowerBound(_renderCache.LowerBound);
                    _cacheNeedsUpdate = false;
                }

                int actualBarCount = Math.Min(spectrum!.Length / 2, barCount);
                SubmitSpectrumForProcessing(spectrum, actualBarCount);

                if (_firstRender)
                {
                    InitializeInitialDrops(actualBarCount);
                    _firstRender = false;
                }

                _timeSinceLastSpawn += _actualDeltaTime;
                UpdateSimulation(actualBarCount);

                RenderScene(canvas!, localPaint);
                drawPerformanceInfo?.Invoke(canvas!, info);
            }
            catch (Exception ex)
            {
                Log.Error($"RaindropsRenderer: {ex.Message}");
            }
        }
        #endregion

        #region Background Processing
        private void SubmitSpectrumForProcessing(float[] spectrum, int barCount)
        {
            lock (_spectrumLock)
            {
                _spectrumToProcess = spectrum;
                _barCountToProcess = barCount;
            }

            _spectrumDataAvailable.Set();
            _processingComplete.WaitOne(5);
        }

        private void ProcessSpectrumThreadFunc()
        {
            try
            {
                while (_processingRunning && !_cts.Token.IsCancellationRequested)
                {
                    _spectrumDataAvailable.WaitOne();

                    float[]? spectrumCopy;
                    int barCountCopy;

                    lock (_spectrumLock)
                    {
                        if (_spectrumToProcess == null) { _processingComplete.Set(); continue; }
                        spectrumCopy = _spectrumToProcess;
                        barCountCopy = _barCountToProcess;
                    }

                    ScaleSpectrum(spectrumCopy, _scaledSpectrumCache.AsSpan(0, barCountCopy), barCountCopy);
                    _processingComplete.Set();
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Log.Error($"RaindropsRenderer: {ex.Message}"); }
        }
        #endregion

        #region Simulation Methods
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool ValidateRenderParameters(SKCanvas? canvas, float[]? spectrum, SKPaint? paint) =>
            _isInitialized && !_isDisposed && canvas != null && spectrum != null && spectrum.Length > 0 && paint != null;

        private void InitializeInitialDrops(int barCount)
        {
            _raindropCount = 0;
            float width = _renderCache.Width;
            float height = _renderCache.Height;

            for (int i = 0; i < 50; i++)
            {
                int spectrumIndex = _random.Next(barCount);
                float x = width * (float)_random.NextDouble();
                float y = height * (float)_random.NextDouble() * 0.5f;
                float speedVariation = (float)(_random.NextDouble() * Settings.Instance.SpeedVariation);
                float fallSpeed = Settings.Instance.BaseFallSpeed + speedVariation;

                _raindrops[_raindropCount++] = new Raindrop(x, y, fallSpeed, spectrumIndex);
            }

            for (int i = 0; i < 20; i++)
            {
                float x = width * (float)_random.NextDouble();
                float y = _renderCache.LowerBound - height * 0.1f * (float)_random.NextDouble();
                _particleBuffer.CreateSplashParticles(x, y, _random);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ScaleSpectrum(ReadOnlySpan<float> src, Span<float> dst, int count)
        {
            if (src.IsEmpty || dst.IsEmpty || count <= 0) return;

            float blockSize = src.Length / (2f * count);
            int halfLen = src.Length / 2;
            for (int i = 0; i < count; i++)
            {
                int start = (int)(i * blockSize);
                int end = Math.Min((int)((i + 1) * blockSize), halfLen);
                float sum = 0;

                for (int j = start; j < end; j++) sum += src[j];
                dst[i] = (end > start) ? sum / (end - start) : 0f;
            }
        }

        private void UpdateSimulation(int barCount)
        {
            UpdateRaindrops();
            _particleBuffer.UpdateParticles(_actualDeltaTime);

            if (_timeSinceLastSpawn >= 0.05f)
            {
                SpawnNewDrops(barCount);
                _timeSinceLastSpawn = 0;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void UpdateRaindrops()
        {
            int writeIdx = 0;
            for (int i = 0; i < _raindropCount; i++)
            {
                Raindrop drop = _raindrops[i];
                float newY = drop.Y + drop.FallSpeed * _actualDeltaTime;

                if (newY < _renderCache.LowerBound)
                {
                    _raindrops[writeIdx++] = drop.WithNewY(newY);
                }
                else
                {
                    _particleBuffer.CreateSplashParticles(drop.X, _renderCache.LowerBound, _random);
                }
            }
            _raindropCount = writeIdx;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SpawnNewDrops(int barCount)
        {
            if (barCount <= 0) return;

            float stepWidth = _renderCache.Width / barCount;
            float threshold = _isOverlayActive ?
                Settings.Instance.SpawnThresholdOverlay :
                Settings.Instance.SpawnThresholdNormal;

            for (int i = 0; i < barCount && i < _scaledSpectrumCache.Length; i++)
            {
                float intensity = _scaledSpectrumCache[i];

                if (intensity > threshold &&
                    _random.NextDouble() < Settings.Instance.SpawnProbability * intensity)
                {
                    float x = i * stepWidth + stepWidth * 0.5f +
                              (float)(_random.NextDouble() * stepWidth * 0.5f - stepWidth * 0.25f);

                    float speedVariation = (float)(_random.NextDouble() * Settings.Instance.SpeedVariation);
                    float fallSpeed = Settings.Instance.BaseFallSpeed *
                                     (1f + intensity * Settings.Instance.IntensitySpeedMultiplier) +
                                     speedVariation;

                    if (_raindropCount < Settings.Instance.MaxRaindrops)
                        _raindrops[_raindropCount++] = new Raindrop(x, _renderCache.UpperBound, fallSpeed, i);
                }
            }
        }
        #endregion

        #region Rendering Methods
        private void RenderScene(SKCanvas canvas, SKPaint paint)
        {
            paint.Style = SKPaintStyle.Fill;

            // Рисуем капли
            for (int i = 0; i < _raindropCount; i++)
            {
                canvas.DrawCircle(
                    _raindrops[i].X,
                    _raindrops[i].Y,
                    Settings.Instance.RaindropSize,
                    paint);
            }

            // Рисуем частицы
            _particleBuffer.RenderParticles(canvas, paint);
        }
        #endregion

        #region IDisposable Implementation
        public void Dispose()
        {
            if (_isDisposed) return;

            _processingRunning = false;
            _cts.Cancel();
            _spectrumDataAvailable.Set();
            _processingThread.Join(100);

            _cts.Dispose();
            _spectrumDataAvailable.Dispose();
            _processingComplete.Dispose();

            Settings.Instance.PropertyChanged -= OnSettingsChanged;

            _isInitialized = false;
            _isDisposed = true;

            GC.SuppressFinalize(this);
        }
        #endregion
    }
}